# Horn of Calling — context

Why this mod is built the way it is.

## Jötunn was tried and dropped

The first version registered its item through [Jötunn](https://valheim-modding.github.io/Jotunn/),
which reduces item registration to about ten lines. It was dropped because the cost
landed in the wrong place: Jötunn is a separate BepInEx plugin that every player and the
dedicated server would have had to install, which would have made this the only mod in
the repo with an install-time dependency beyond BepInEx.

vegvisir-compass already adds a custom item without it, so the pattern existed in-repo.
Registering by hand is roughly 60 lines against `ObjectDB` and `ZNetScene`, and the
result has no runtime dependency at all.

Dropping it also removed a pile of build friction that was purely Jötunn's:

- **`JotunnLib` ships `build/JotunnLib.props`, which injects ~150 copy-local
  `<Reference>` items** pointing into the game folder. Left alone, every build copied
  the entire Unity runtime into `bin/`, against the repo rule that no game assembly is
  ever shipped. It needed a target stamping `Private=false` onto every reference.
- **Its `BEPINEX_PATH` is broken on Linux.** `Paths.props` builds it as
  `$(VALHEIM_INSTALL)\BepInEx` — a hardcoded backslash, not a separator on Unix — so
  Jötunn's own BepInEx references never resolved on Linux. Working around that pulled in
  a `BepInEx.Core` PackageReference and a `nuget.config` for `nuget.bepinex.dev`, since
  BepInEx is not on nuget.org.
- **It forced `net462`** (JotunnLib ships only that target), which in turn needed
  `Microsoft.NETFramework.ReferenceAssemblies` to build on Linux, which dragged ~100
  netstandard facade DLLs into `bin/`.
- **It auto-detects the game install silently**, falling back to
  `$(HOME)/.steam/steam/steamapps/common/Valheim` on Unix. That path exists on a normal
  Linux Steam install, so game references resolved *even when the project was
  misconfigured* — builds could succeed for reasons unrelated to `ValheimPath`.

None of that applies now. The project is `netstandard2.1` like the rest of the repo,
`bin/Release` holds three files, and the only package reference is the publicizer.

## Registration order is the whole problem

Adding an item by hand is not hard; getting it to happen at the right moment is.

- **`ObjectDB` is populated more than once.** There is a stripped-down copy in the main
  menu, then the real one merged in via `CopyOtherDB`. Registering only on `Awake` gives
  an item cloned from an incomplete database. Both entry points are patched, and
  `EnsureRegistered` guards on `odb.GetItemPrefab("Wood") == null` to detect the
  main-menu copy and bail.

- **`CopyOtherDB` does not merge — it replaces.** The name suggests copying entries in.
  It actually reassigns the list references outright:

  ```csharp
  m_items   = other.m_items;
  m_recipes = other.m_recipes;
  ```

  So everything registered against the main-menu database is discarded when a world
  loads. **Never latch a "registered" boolean**; test the live list every time. This cost
  real time to find because the failure is asymmetric and looks nothing like its cause:
  item registration re-tests `odb.m_items.Contains(_prefab)` and so silently healed
  itself, while the recipe used a `_recipeAdded` flag and stayed gone. The symptom was
  `spawn FrostAxe` working perfectly while the workbench showed nothing — which reads
  like a recipe bug, not a lifecycle one.

- **No patch point is guaranteed to be both late enough and ordered correctly.**
  `CopyOtherDB` can replace the recipe list while the crafting station prefabs are still
  unloaded, and `ZNetScene.Awake` may already have run by then, leaving no retry. The
  backstop is a prefix on `Player.UpdateKnownRecipesList`, which runs immediately before
  the game enumerates recipes — the one moment the recipe is definitely needed. With the
  presence check the common case is a single list scan.
- **The recipe cannot be built at the first `ObjectDB.Awake`.** A `Recipe` needs a
  `CraftingStation`, which is a component on a *piece* prefab, not an item — so it does
  not live in `ObjectDB` and may not exist yet. Recipe registration is therefore a
  separate idempotent step attempted from all three patch points, returning early and
  retrying while the station is missing.
- **Cloning must not run `Awake`.** The clone is instantiated into an inactive,
  `DontDestroyOnLoad` container so Unity treats it as a prefab rather than a live scene
  object. Borrowed from vegvisir-compass.
- `ItemDrop.ItemData.SharedData` is a plain `[Serializable]` class, so `Instantiate`
  deep-copies it. Edits to the clone's `m_shared` cannot leak back into the vanilla item.

## The Horn of Celebration is `TankardAnniversary`

The item is cloned from the Horn of Celebration, which supplies the horn model, the icon
and the one-handed hold pose in one step — no AssetBundle, no Unity Editor.

Its prefab name is not guessable from the in-game label. The chain that leads to it:

```bash
strings valheim_Data/resources.assets | grep -i celebration      # -> $item_tankard_anniversary
strings valheim_Data/StreamingAssets/SoftRef/manifest_extended \
  | grep -i tankard                                              # -> .../misc/TankardAnniversary.prefab
```

What the prefab actually carries, read out of the bundle rather than assumed:

| Field | Value | Consequence |
|---|---|---|
| `m_itemType` | `3` (`OneHandedWeapon`) | equippable, and left click runs an attack |
| `m_animationState` | `5` (`Torch`) | held in the torch pose, which suits a horn |
| `m_attack.m_attackAnimation` | `emote_drink` | left click plays an *emote*, not a swing |
| `m_attack.m_attackStamina` | `0` | using it is currently free |
| `m_startEffect` | `vfx_MeadSplash`, `sfx_MeadBurp` | inherited effects are mead-specific |

`m_attackAnimation` being an emote is the useful part: the design note asks for a roar
emote, and that is a one-string change on this field rather than new animation work.

### Reading prefab fields without launching the game

The assemblies answer questions about *code*; they say nothing about *asset data* like
which item type the tankard is. That comes out of the SoftRef bundles, and Valheim ships
embedded typetrees, so [UnityPy](https://github.com/K0lb3/UnityPy) can read them with no
game running and no AssetRipper:

```python
env = UnityPy.load(".../StreamingAssets/SoftRef/Bundles/c4210710")   # 600 MB, ~1 min
byid = {o.path_id: o for o in env.objects}
go = next(o.parse_as_dict() for o in env.objects
          if o.type.name == "GameObject" and o.parse_as_dict().get("m_Name") == "TankardAnniversary")
# then walk go["m_Component"] and parse_as_dict() the ItemDrop MonoBehaviour
```

A `MonoBehaviour`'s `m_Name` is empty, so components are found by walking the
`GameObject`'s `m_Component` list, never by searching for a name.

## The sound rides `m_startEffect`, not a Harmony patch

`Attack.Start` is a tempting patch point, but the game already has the hook:

```csharp
// Attack.cs, once per attack, after stamina is spent
m_weapon.m_shared.m_startEffect.Create(attackOrigin.position, m_character.transform.rotation, attackOrigin);
```

It fires exactly once when an attack genuinely begins — after the stamina, ammo and
dungeon checks, so it cannot sound on a swing the game refused. Using it means the horn
needs no patch at all for its audio.

**The effect prefab is cloned, not built.** A hand-made `GameObject` with an
`AudioSource` would not be routed to the SFX `AudioMixerGroup` — `AudioMan` exposes only
its ambient and GUI mixers — so it would ignore the player's sound-effects volume slider
and Valheim's 3D falloff curve. Cloning `sfx_MeadBurp` off the item's own start effect
inherits the wired `AudioSource`, the `ZSFX`, and a `TimedDestruction` of 10 s (longer
than the 4.9 s clip, so nothing is cut off), then only `m_audioClips` is swapped.

**The burp's tuning has to be undone, and it is not obvious.** `sfx_MeadBurp` is
configured to sound like a burp:

| `ZSFX` field | Inherited | Why it has to change |
|---|---|---|
| `m_minDelay` / `m_maxDelay` | `4.0` / `5.0` | the sound would start four to five seconds after the click |
| `m_minPitch` / `m_maxPitch` | `0.5` / `0.9` | randomly pitched down |
| `m_minVol` / `m_maxVol` | `0.4` | plays at 40% |
| `m_closedCaptionToken` | `$caption_burp` | would subtitle the horn as a burp |
| `m_hash` | the burp's | `ZSFX` groups concurrent sources by hash; horns would cut off burps |

The delay is the one that would have looked like "the sound never plays".

The clone keeps the `ZNetView` it inherits, so it takes a ZDO the moment it spawns and
its prefab hash **must** be registered with `ZNetScene` alongside the item — otherwise
every *other* client logs an unresolvable prefab. Registering matches what vanilla does;
stripping the component would have been the guess.

The inherited effects are replaced rather than appended: a mead splash and a burp are
both wrong on a horn.

## The Horn of Celebration's ammunition is mead

The single most surprising thing about the clone source, and the one that produced a
real bug: `m_shared.m_ammoType` is `"mead"`.

`Attack.Start` runs the same ammunition path a bow does:

```csharp
if (!HaveAmmo(character, m_weapon)) return false;   // no mead -> the attack never starts
EquipAmmoItem(character, m_weapon);
```

and on the trigger, `UseAmmo` finds the mead, sees `ItemType.Consumable`, and calls
`ConsumeItem` — which drinks it and applies its status effect.

So the horn inherited two behaviours that read as unrelated bugs:

- **With a mead in the inventory**, sounding the horn drank it and granted, in the case
  that surfaced this, "Lingering stamina mead".
- **Without one**, `HaveAmmo` returned `false` and `Attack.Start` bailed *before*
  reaching `m_startEffect` — so the horn would have been completely silent, with only a
  "$msg_outof mead" message to explain it. The sound appeared to work only because
  there happened to be a mead in the inventory.

The fix is `m_ammoType = ""`, which every one of `HaveAmmo`, `EquipAmmoItem` and
`UseAmmo` guards on with `string.IsNullOrEmpty` and returns success for.

Worth generalising: **nothing about this is visible in the item's object references.**
Walking every `PPtr` under the tankard's `m_itemData` finds exactly three — the icon and
two effect prefabs — and no status effect anywhere. The behaviour is a *string* that
names a category of other items. Dumping references is not enough; the scalar fields
have to be read too.

## The tooltip stat block is driven by fields, not by a flag

`ItemDrop.GetTooltip` switches on `m_itemType`, and `OneHandedWeapon` renders the weapon
stat block. The horn has to stay that type — it is what makes the item equip to the hand
and run an attack on left click.

There is no "hide the stats" flag. Every line is printed only when its own field is
above zero, so zeroing the fields is the mechanism:

| Field | Vanilla | Tooltip line |
|---|---|---|
| `m_damages` | already all zero | damage lines (each gated on `!= 0f`) |
| `m_attackForce` | `30` | `$item_knockback` |
| `m_backstabBonus` | `4` | `$item_backstab` |
| `m_blockPower` | `4` | `$item_blockarmor` |
| `m_deflectionForce` | `5` | `$item_blockforce` |
| `m_timedBlockBonus` | `1.5` | `$item_parrybonus` |

**`AddBlockTooltip` gates each of its lines on a separate field.** Clearing
`m_blockPower` alone leaves "block force" and "parry bonus" sitting there — the trap is
assuming one field turns off "blocking".

`ItemType.Tool` skips the stat block entirely and looks like the obvious answer, but
`AddHandedTip` lists `Tool` under `$item_twohanded`, so it trades a stat block for a
wrong line and a two-handed hold.

`m_weight` and `$item_onehanded` are printed for every item of this type and are not
part of the stat block; they stay.

`m_skillType` is also cleared to `None`. It is vanilla `Swords`, and with damage zeroed
it renders nothing, but leaving it would have the horn train the sword skill.

## The blast is networked, and the range has two ceilings

It is easy to assume an effect spawned by `EffectList.Create` is local — `Create` is a
plain `Object.Instantiate` with no networking in it at all. The networking comes from the
prefab: the cloned `sfx_MeadBurp` carries a `ZNetView`, and `ZNetView.Awake` on an
instance with no `m_initZDO` calls

```csharp
m_zdo = ZDOMan.instance.CreateNewZDO(transform.position, prefabName.GetStableHashCode());
```

so the object replicates to nearby peers, who instantiate it by hash and hear it. This is
the reason the effect prefab must be registered with `ZNetScene`; without it the hash is
unresolvable on every other client.

That gives two independent ceilings on range:

| Ceiling | Value | Where it comes from |
|---|---|---|
| Audible falloff | whatever the `AudioSource` curve says | vanilla template: silent past 25 m |
| ZDO replication | ~64 m guaranteed | `ZoneSystem.m_activeArea = 1`, `m_zoneSize = 64` |

`ZDOMan.FindSectorObjects(zone, m_activeArea, ...)` walks the peer's own zone plus one
ring, so a non-distant ZDO reaches at least 64 m and at most ~135 m diagonally. **Raising
the audio range past that does nothing** — the peer never receives the object. Going
further needs `m_distant = true` (which only buys `m_activeDistantArea`, another ring) or
an explicit `ZRoutedRpc` broadcast with each client playing the blast locally. Only the
RPC makes the range a number you choose.

### Two traps in the curve itself

**Unity and Valheim normalise the curve differently.** Unity evaluates a custom rolloff
curve over `0..maxDistance`. `ZSFX.GetVolumeModifierByDistance` instead does

```csharp
float time = Mathf.InverseLerp(m_audioSource.minDistance, m_audioSource.maxDistance, distance);
```

These disagree whenever `minDistance != 0`. It does not bite here — that method is only
consulted for looping sounds and concurrency, and the blast is a one-shot whose
`m_maxConcurrentSources` is `0`, so `AudioMan.RequestPlaySound` returns `true` before
reaching it. Worth knowing before reusing the helper's numbers to reason about volume.

**Flat tangents are what make a plateau flat.** `AnimationCurve` interpolates with a
cubic Hermite, so keys left on default tangents bow between the points and the "steps"
sag. With in/out tangents of zero, two keys of equal value hold the level exactly (the
Hermite basis satisfies `h00 + h01 = 1`), and a pair straddling a boundary steps between
levels over the gap.

## Audio is embedded as PCM and decoded by hand

The blast ships as a 16-bit mono PCM WAV embedded in the assembly (`assets/viking.wav`,
~430 KB, converted from an MP3 with `ffmpeg`).

The obvious alternative, `UnityWebRequestMultimedia.GetAudioClip`, needs three things
this does not: a file on disk, a coroutine to await, and a compressed format Unity is
willing to decode at runtime on this platform. Uncompressed PCM trades ~430 KB of
assembly size for a clip that exists synchronously on first use with no I/O at all —
`AudioClip.Create` plus `SetData` over a `float[]`.

Two details in the decoder that are easy to get wrong:

- **The header is not a fixed 44 bytes.** `ffmpeg` writes a `LIST`/`INFO` chunk between
  `fmt ` and `data` by default, so the chunks must be *walked*. (The committed file is
  produced with `-fflags +bitexact` so it has no such chunk — but the walk is what makes
  that a convenience rather than a requirement.)
- **Chunks are padded to an even length**, so the step is `size + (size & 1)`.

Mono is deliberate, not a size saving: Unity only spatialises mono clips properly, and
the blast is a positional sound.

## Publicizer

`ObjectDB.UpdateRegisters()` and `ZNetScene.m_namedPrefabs` are both private and both
are required. `BepInEx.AssemblyPublicizer.MSBuild` rewrites the reference assemblies at
build time only — nothing changes at runtime and players need nothing extra.

## Item naming

`m_shared.m_name` is set to the plain string `"Horn of Calling"`, not a `$item_`
localization token. The mod ships no translation table, and an unresolved token displays in game as
the literal `$item_frostaxe` rather than failing — a silent, easy-to-miss bug. If
localization is added later, the token and the table have to land together.

## Crafting station lookup

The station is found by scanning `Resources.FindObjectsOfTypeAll<CraftingStation>()` for
a matching `name`, rather than reading it out of `ZNetScene`, because the lookup is
needed from three patch points with different guarantees about what is loaded.

The names are prefab names and are not guessable from the in-game labels: the Workbench
is `piece_workbench` while the Forge is plain `forge`. Both were confirmed from the log,
not assumed.

On failure it logs every station name it *did* find. A station prefab renamed between
game versions otherwise shows up as a recipe that silently never appears, which is
expensive to diagnose from the game side.

## CI

No change to `.github/actions/build-mods` was needed. The mod resolves through
`ValheimManaged` / `BepInExCore`, which the workflow already passes as global
properties, and every assembly it references was already in the verify step's required
list.

Verified locally by building with `-p:ValheimPath=/nonexistent` plus the real paths as
globals, reproducing how a runner overrides the local default.
