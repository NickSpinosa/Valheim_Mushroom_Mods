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
- **Network registration needs a retry too, for a different reason.** The three points
  above all guard against *game* lifecycle ordering. `ZNetScene.Awake` has a second
  hazard: it is a crowded patch point, and a postfix from another mod that throws aborts
  the rest of the chain. If that happens before ours runs, the horn and its blast never
  get their prefab hashes registered, and nothing retries — for the whole session,
  dropping the horn fails and other clients cannot resolve the blast. Two halves guard
  it, both copied from vegvisir-compass: `[HarmonyPriority(Priority.First)]` on the
  shared postfixes so we run before a thrower can get to us, and a `Game.Start` postfix
  that re-runs all three `Ensure*` calls. `Game.Start` is a *separate patch chain*, which
  is the entire point — a chain aborted at `ZNetScene.Awake` cannot take it down with it.
  Every `Ensure*` call is idempotent, so the duplicate pass costs a list scan.
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

**Which is why building the prefab must not depend on the audio decoding.** The tempting
shape is to load the clip first and bail if it fails — a peer with no audio has nothing
to play, so why build the prefab? Because the prefab is not only a sound: it is a
networked object whose hash every *other* peer needs to resolve. The peer most likely to
fail the decode is the headless dedicated server, where `AudioClip.Create`/`SetData` is
the least exercised path and there is no audio device at all — and that is precisely the
peer that must be able to resolve a hash every client is spawning at it. So `Attach`
instantiates, tunes and names the prefab first, then attaches the clip if there is one:

```csharp
sfx.m_audioClips = clip != null ? new[] { clip } : new AudioClip[0];
```

`ZSFX.Play()` opens with `m_audioClips.Length != 0`, so an empty array is safe — the
blast is simply silent. A silent peer is a log line; a peer missing the prefab is a
networking fault on every peer around it.

That gives two independent ceilings on range:

| Ceiling | Value | Where it comes from |
|---|---|---|
| Audible falloff | whatever the `AudioSource` curve says | vanilla template: silent past 25 m |
| ZDO replication | 64 m guaranteed, more per listener | the listener's simulation distance |

**Raising the audio range past the replication ceiling does nothing** — the peer never
receives the object, so no volume setting reaches it. The ceiling itself, though, stopped
being a constant in 1.0.7.

### Simulation distance replaced the active area

Up to 0.221 the reach was two fields: `ZoneSystem.m_activeArea = 1` over `m_zoneSize = 64`,
so `ZDOMan.FindSectorObjects(zone, m_activeArea, ...)` walked the peer's own zone plus one
ring — a 3×3 square, the same for everybody.

In 1.0.7 `m_activeArea` and `m_activeDistantArea` are **gone**. `FindSectorObjects` now
takes a `SimulationDistance` struct, and the value is a *player setting*:

```csharp
// ZDOMan.CreateSyncList - what the server sends this peer
FindSectorObjects(zone, peer.m_peer.m_simulationDistance, m_tempSectorObjects, m_tempToSyncDistant);

// ZNetScene.CreateDestroyObjects - what this client instantiates
ZDOMan.instance.FindSectorObjects(zone, ZNet.instance.GetSyncedSimulationDistance(), ...);
```

Both ends agree because both resolve to the same clamp: the client asks for its graphics
setting, and `ZNet` stores `min(requested, the server's own)` — `GetSyncedSimulationDistance()`
on the client, `ZNetPeer.m_simulationDistance` on the server. A player cannot raise their
reach above what the server allows, and the server cannot force it up.

`SimulationDistance.GetSimulationDistance(level)` maps the setting's `0..6` range
(`GraphicsSettingInt.SimulationDistance.GetRange()`) onto near/far zone counts, and the
`classic` flag decides the *shape* of the near set — a square when set, and a disc when
not, because each ring zone is then gated on `ZoneSystem.ZonesWithinRadius`:

| Level | `SimulationDistance` | Near zones | Guaranteed reach |
|---|---|---|---|
| 0 | `(1, 2, classic: true)` | 3×3 square | 64 m |
| 1 | `(2, 2)` | 5×5 less its corners | ~90 m |
| 2 (default) | `(2, 2, classic: true)` = `OriginalDistance` | 5×5 square | 128 m |
| 3–6 | `(level, 2)` | disc of that radius | further |

The guaranteed figure is the worst case within a zone, not the zone width: zone *n* spans
`[64n-32, 64n+32)`, so a listener standing at its edge still has a full zone of grid in
front of them. In the best case — listener at one corner of their zone, blast at the far
corner of the diagonal neighbour — level 0 reaches ~181 m. Only the floor is worth tuning
to, since it is the only reach every listener has.

**64 m survived that change by coincidence, and it is the right coincidence.** Level 0 is
`classic` with a near distance of 1, which is the old one-ring square exactly — so the
number the curve was tuned to is now the floor of a range instead of a constant. What
changed is that most listeners are further than that: at the default level 2 the blast
reaches 128 m, and the curve goes quiet at 64.

Deriving `MaxDistance` from `GetSyncedSimulationDistance()` is tempting and not wrong in
principle — the curve is evaluated on the *listener's* machine, and it is the listener's
own setting that decides what reaches them, so the two genuinely do line up. It was left
alone because `Attach` builds the prefab once, while the setting can change at any point
afterwards: the derived value would be a snapshot that quietly goes stale, for a range the
horn does not currently use anyway.

Going past the near set needs `m_distant = true`, which moves the blast into the far rings
(out to `TotalSimulationDistance`, near + a fixed far of 2) — but `CreateSyncList` only
appends distant ZDOs when the near list came back with fewer than 10 entries, so it is a
best-effort tier, not a longer guarantee. The alternative is an explicit `ZRoutedRpc`
broadcast with each client playing the blast locally. Only the RPC makes the range a
number you choose.

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

## The volume slider lives in the vanilla Audio tab

The blast volume is a `ConfigEntry<float>` on `Config`, and the row on the game's own
**Settings → Audio** tab is a front end for it. The config file is the storage: OK writes
the slider back into the entry, and assigning the entry both saves the file and raises
`SettingChanged`, which is what actually applies the value. That way the F1
ConfigurationManager overlay and a hand-edited `.cfg` reach the same place with no second
code path.

**It is deliberately not a MushroomSync setting.** The blast prefab is instantiated
locally on every peer that hears it, so the level on *this* machine's prefab decides what
*this* player hears and nothing else — the same shape as the game's own SFX slider. A
host-authoritative version would let one player turn down horns in someone else's
headphones.

### Volume belongs on the prefab's ZSFX, not on the AudioSource

`AudioSource.volume` is rewritten every frame:

```csharp
// ZSFX.CustomUpdate, while the source is playing
m_audioSource.volume = vol * num * m_concurrencyVolumeModifier * m_volumeModifier;
```

where `vol` is `m_vol`, drawn in `Play()` as `Random.Range(m_minVol, m_maxVol)`. So the
lever is `m_minVol`/`m_maxVol`, and setting both to the same number is what stops the
horn varying. Setting them on the **prefab** — rather than on each instance — is enough:
every blast is an `Instantiate` of it, the one you sound and the one a player 40 m away
sounds, so one field reaches all of them with no per-instance code.

`SetVolume` therefore tolerates the prefab not existing yet. The settings menu opens from
the main menu, where `ObjectDB` has not produced a real item and `Attach` has never run.
Nothing is lost, because `Attach` applies the configured level itself when it does build
the prefab.

### The Audio tab's rows, read out of AudioTab.prefab

Read the same way the tankard's fields were — UnityPy over bundle `c4210710`, which holds
`Assets/UI/prefabs/Settings/AudioTab.prefab`:

```
AudioTab
  List
    MasterVolume    <- the Slider component is on the row GameObject itself
      Background, Fill Area/Fill, Handle Slide Area/Handle, Label, Value
    SfxVolume
    MusicVolume
    ContinuosMusic  <- spelled that way in the prefab
```

The load-bearing detail is the first line: **the `Slider` is on the row, and the caption
and the "50%" readout are its children.** So `Instantiate(m_sfxVolumeSlider.gameObject)`
clones the entire row — background, fill, handle, layout and both texts — and no
RectTransform has to be assembled by hand. Which matters beyond convenience: a
hand-built row would not track the panel the next time it is restyled.

`m_sfxVolumeText` is the `Value` child, so the clone's readout is found by that same name
rather than a hardcoded `"Value"`, and the caption is the row's other `TMP_Text`.

### Three traps in cloning a settings row

**`RemoveAllListeners()` does not remove a persistent call.** The SFX slider carries an
inspector-wired call to `AudioSettings.OnAudioChanged`, and the clone inherits it —
pointing at the *real* `AudioSettings`, because `Instantiate` only re-targets references
that live inside the copied subtree and that component sits on `AudioTab`, above it. So
dragging the horn slider would re-apply the three vanilla volumes and rewrite their
labels. `RemoveAllListeners` drops only listeners added from code; the persistent list
survives it. Replacing the event object outright is what clears it:

```csharp
_slider.onValueChanged = new Slider.SliderEvent();
```

**The rows use `Navigation.Mode.Explicit`, not Automatic.** Each names the row above and
below it by hand — `MasterVolume → SfxVolume → MusicVolume → ContinuosMusic`. A clone
therefore arrives pointing at the *SFX* row's neighbours, and the row it was dropped
after still points past it, so a gamepad or keyboard walking down the list skips the new
row entirely and a player without a mouse can never reach it. Sibling index changes only
the drawing order. Splicing in is three links, not one, because the row below has to
point back up:

```csharp
next = previous.navigation.selectOnDown;
GuiUtils.SetNavigationDown(previous, inserted);
GuiUtils.SetNavigationUp(inserted, previous);
GuiUtils.SetNavigationDown(inserted, next);
GuiUtils.SetNavigationUp(next, inserted);
```

`GuiUtils` lives in `assembly_guiutils.dll` and does the `Navigation`-struct copy-back
that assigning `selectable.navigation.selectOnDown` directly would silently discard.

**The caption is a localization token.** `Label` holds the literal string
`$settings_sfxvol`, resolved when `Localization` walks the panel. The mod ships no
translation table, so the row is captioned with plain text instead — an unknown token
would display as `$settings_hornvol` rather than failing. Text with no `$` passes through
`Localize` unchanged, so a plain caption survives however often the panel is localized.
Same reasoning as the item name.

### Loudness at 100% lives in the clip, not in the code

There is nowhere in the code to make the horn louder once it is already asking for full
volume. `ZSFX.CustomUpdate` writes `m_audioSource.volume = vol * ...`, and Unity clamps
`AudioSource.volume` to `0..1`, so `m_maxVol` above 1 buys nothing. Everything above that
is fixed: the SFX mixer group is the player's setting, and the falloff curve is already
1.0 at 0 m.

So the lever is the recording, and the committed one had 5 dB sitting unused:

| | Peak | Mean |
|---|---|---|
| As converted from the source MP3 | −5.38 dBFS | −21.1 dB |
| Committed now | −0.30 dBFS | −16.0 dB |

**Mean volume is the wrong number to judge this by, and it is the one `volumedetect`
prints first.** The blast itself runs 0–2 s; what follows is a reverb tail decaying to
−86 dBFS, and roughly the last 1.5 seconds is inaudible. Averaged over the file that tail
drags the mean 15 dB below the peak, which reads like a quiet recording with lots of room
to amplify. The peak is what actually caps the gain — and it allowed 5.08 dB, verified by
checking the result has no samples sitting at the rail.

Anything past that needs compression or limiting, which changes how the horn sounds rather
than how loud it is. The `ffmpeg` line in the csproj carries the gain so the next
regeneration does not silently undo it.

A footnote on that line, found while regenerating: **`-fflags +bitexact` is positional,
and the recipe originally had it in front of `-i`** — where it configures the demuxer, not
the muxer, and ffmpeg goes back to writing a `LIST`/`INFO` chunk between `fmt ` and
`data`. Harmless, since the decoder walks the chunks precisely because that chunk exists,
but it is why the flag now sits with the output.

### The preview is a settle timer, not a mouse-up

A volume slider you cannot hear is most of a volume slider, so the row sounds the blast
once when the player stops moving it.

**Mouse-up alone would be wrong.** The row is also driven by arrow keys and a gamepad
stick — that is what the navigation splice above exists for — and those produce a stream of
`onValueChanged` calls and no release event at all, so a pointer-only trigger would leave
the preview silent for exactly the players who cannot see a handle move under a cursor. A
deadline pushed forward on every change covers both: keyboard fires a third of a second
after the last nudge, and the pointer handlers only *suppress* it while the handle is
actually held, so releasing fires at once because the deadline has already passed.

It times on `Time.unscaledTime`. The settings menu pauses the game in single-player, and a
scaled timer would never come due.

**The AudioSource is added to the row itself**, not to a GameObject of the mod's own. The
panel is destroyed on OK and Back, which takes the source with it and stops a preview still
sounding after the menu closes — teardown that would otherwise have to be written and got
right on both exits.

**It plays a flat 2D source, but routed through the mixer group read off the blast
prefab's own AudioSource.** The group has to come from the prefab rather than a name looked
up on `AudioMan` — `AudioMan` exposes only its ambient and GUI groups — and going through
it is what makes the preview obey Valheim's sound-effects slider, as the real blast does. A
preview that skipped it would be reassuring about the wrong number. 2D is right despite the
blast being positional: the level a player standing at the horn hears is the falloff curve
at 0 m, which is 1, so a 2D source at the configured volume is the same number without
depending on where the listener is standing or what reverb zone they are in.

The consequence is that the preview is silent in the main menu, where `ObjectDB` holds no
real items and the prefab has never been built. The slider still works there; it is logged
at debug rather than warned about.

### Lifecycle

`Settings.CloseSettings` calls `Destroy(gameObject)` on **both** OK and Back, so the panel
is built fresh every time the menu opens. The row is therefore rebuilt per opening and the
static references are re-pointed by `Build`; there is nothing to keep alive between
openings and no `Terminate` hook to write (`ISettingsTab.Terminate` is a default interface
method that `AudioSettings` does not implement, so there is no method on the class to
patch).

The three patch points map to the three moments the menu offers:

| Patch | Moment | What it does |
|---|---|---|
| `AudioSettings.Initialize` postfix | `Settings.Awake`, once per opening | clones the row, seeds it from the config |
| `AudioSettings.OnOkAsync` postfix | OK | writes the slider back to the config entry |
| `AudioSettings.OnBack` postfix | Back, or Escape | re-applies the saved value, discarding the drag |

Applying live on drag and reverting on Back is what the vanilla rows do — they set
`AudioListener.volume` immediately and restore an `m_oldVolume` in `OnBack`.

`Valheim.SettingsGui.AudioSettings` collides with `UnityEngine.AudioSettings`, so it is
imported under an alias rather than with a `using` of the namespace.

## Publicizer

`ObjectDB.UpdateRegisters()` and `ZNetScene.m_namedPrefabs` are both private and both
are required. `BepInEx.AssemblyPublicizer.MSBuild` rewrites the reference assemblies at
build time only — nothing changes at runtime and players need nothing extra.

## Item naming

`m_shared.m_name` is set to the plain string `"Horn of Calling"`, not a `$item_`
localization token. The mod ships no translation table, and an unresolved token displays in game as
the literal `$item_hornofcalling` rather than failing — a silent, easy-to-miss bug. If
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
