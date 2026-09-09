# Valheim 1.0.7 notes

## Harmony type lists break on *added optional parameters*

Both tooltip patches (`ItemData_GetTooltip_Patch` in `Patches.cs`,
`ItemData_GetTooltip_TwoHanded_Patch` in `TwoHandedCombat.cs`) pin their target
with an explicit `typeof(...)` list. 1.0.7 appended one optional parameter to the
static overload — `GetTooltip(ItemData, int, bool, float, int stackOverride = -1,
bool appending = false)` — and both attributes went stale.

The lesson worth keeping: an explicit type list is an *exact* match. C# will
happily let you call the new method with the old five arguments, so nothing in
our source looks wrong and the build stays green; the mismatch only exists at
runtime, where Harmony resolves the attribute against the real method and finds
nothing. Optional parameters are the worst version of this, because adding one is
a source-compatible change from the game's point of view and a breaking one from
ours. Any Valheim update is a reason to re-check every attribute in this mod that
names types, not just the ones whose *behaviour* changed.

## Why one stale attribute took the whole plugin down

`Harmony.PatchAll` does not skip a patch class it cannot resolve — it throws
`ArgumentException: Undefined target method for patch method ...`. That call sits
in the middle of `ShieldReworkPlugin.Awake`, so the throw skipped everything
after it: `Sync.Register`, the feast and ocean config hooks,
`Sync.WatchForChanges`, `OceanWeather.Apply()` and the "loaded" log line. Every
patch class compiled after the bad one (`SailingWind`, `StaggerDebugHud`, all of
`TwoHandedCombat`) was never applied either.

What made it expensive to read: the failure looked *partial*, not total. Shield
stats still applied in game, because the `ObjectDB` patches come earlier in file
order and had already been installed when the throw happened. A log that shows
correct shield numbers and no "loaded" line is the signature of this class of
bug — check for the `PatchAll` exception before believing any half-working
symptom.

The structural fix is patch isolation: patch classes applied individually inside
try/catch so one bad target costs one feature instead of the plugin. That is
tracked separately in issue #18 and deliberately not done here.

## Deep North: where the prefab names came from, and which are still guesses

1.0 added the Deep North — `Heightmap.Biome.DeepNorth = 0x40`, an eighth boss, a
feast and spice, and a full Gold equipment line. The mod's tables are keyed by
prefab name, so covering a new biome means knowing thirty-odd exact strings, and
none of them are in `assembly_valheim.dll`: item prefabs live in the asset
bundles, and boss keys live on the boss prefab as data.

The right source is an in-game `ObjectDB` dump. Nobody could run 1.0.7 while this
was written, so the names below were recovered offline instead, and the dump tool
was built as part of the same change so the next person never has to repeat it.

### How the offline recovery worked, and why it is not proof

Two sources, both indirect:

- **`valheim_Data/StreamingAssets/SoftRef/Bundles`**, grepped as raw bytes. The
  bundles are LZ4-compressed, and grep only ever matches inside a *stored literal
  run*, so every hit is a genuine contiguous substring of the decompressed data —
  but it can be cut off at either end, and often is. This is why the raw output
  has `SpiceDeepNor` next to `SpiceDeepNorth`, `AtgeirGold_FrostFi` next to
  `AtgeirGold_FrostFir`, and never once the whole `_FrostFire`. **Take the longest
  form, and trust it further when it appears with a `_Material`, `Eat` or
  `Uncooked` sibling**, because those siblings only exist for real prefab
  families. A truncation is *always* a prefix of something real; it is never
  evidence of the full length.
- **`resources.assets`**, which holds the localisation table. That is where the
  boss came from: `enemy_boss_frozenking_deathmessage`, `enemy_frozenking`,
  `ach_boss8frozenking`, `piece_offerbowl_frozenking`. Every boss key through
  Fader matches its own localisation id exactly (`bonemass`, `dragon`,
  `goblinking`, `queen`, `fader`), so `defeated_frozenking` is the strong
  inference — and the bundles do contain a truncated `defeated_f`, which is
  consistent with it and equally consistent with `defeated_fader`. Not proof.

The naming pattern is what the Ashlands rows confirm. Ashlands is
`FeastAshlands` / `SpiceAshlands` / `ShieldFlametal` / `THSwordSlayerBlood`; Deep
North is `FeastDeepNorth` / `SpiceDeepNorth` / `Shield{Round,Tower,Buckler}Gold` /
`THSwordGold_BloodLightning`. Note the two breaks with precedent: the shield shape
is now spelled out in the name (`ShieldRoundGold`, not `ShieldGold` the way
`ShieldFlametal` meant the round one), and elemental variants took an underscore
and a compound element (`_FrostFire`, `_BloodLightning`, against Ashlands'
`Blood` / `Lightning` / `Nature`).

### Confidence, row by row

**Confirmed** — full string seen, with an `Uncooked` / `Eat` / `_Material` sibling
or a second independent hit:

`FeastDeepNorth`, `THSwordGold`, `AtgeirGold`, `SledgeGold`, `FistGold`,
`KnifeGold`, `MaceGold`, `BowGold`, `AxeJotunBane`, `ShieldBucklerGold`, and the
`_FrostFire` stems for atgeir, battleaxe, bow, crossbow, fist, knife, spear,
sword and two-handed sword.

**Inferred from a truncation plus the family pattern** — the form used in the
tables, not seen whole:

`SpiceDeepNorth` (saw `SpiceDeepNor`, and lowercase `spicedeepnorth`),
`ShieldRoundGold` and `ShieldTowerGold` (saw only their `…Uncooked`
intermediates), `BattleaxeGold`, every `_BloodLightning` suffix (longest hit was
`_BloodLightn`), and `defeated_frozenking`.

**Deliberately excluded.** `<Weapon>GoldUncooked` is a crafting intermediate — a
Material, not a weapon — and the tables must not carry it. `ShieldGold` and
`ShieldGoldBuckler` both appear as real substrings alongside `ShieldRoundGold` and
`ShieldBucklerGold`; a name can be a mesh or material rather than a prefab, and
guessing between two live candidates is exactly what the dump is for.
`Axe1h_JotunWarrior`, `Axe2h_JotunWarrior` and `Sword2h_JotunWarrior` look like
the JotunWarrior enemy's own weapon models, not player items.

A wrong guess is not silent for feasts — `FeastStats.CanonicalPrefabs` logs
`prefab '…' not found in ObjectDB` at startup. It *is* silent for a weapon or
shield row, which simply never matches. That asymmetry is why the dump has a
coverage section that prints `MISSING` for every table entry ObjectDB does not
have.

### The dump

`[Diagnostics] DumpObjectDb = false` by default. Switch it on and load a world, or
type `cadump` in the console, and `Diagnostics` writes
`CombatAdjustments.ShieldRework.objectdb-dump.txt` **next to the .cfg the plugin
actually loaded** — `BepInEx/config/` on a client, `config/bepinex/` on a
dedicated server, so the two layouts do not fight over one path. The log line
gives the full path.

It is excluded from config sync on purpose: it makes a machine write a file, so a
host switching it on must not make every connected client write one too. The
console command exists because a dedicated server has no console to type into, and
the flag exists because a client should not have to restart to get a second
dump — one trigger per kind of installation.

It prints the world's global keys, the eight boss keys the feast gate names with
whether each is set, the coverage report, then every `ItemDrop` with its item
type, plus shield block/parry/grant lines and food health/stamina/eitr lines. The
food numbers are the values **after** this mod's bonuses; set
`Feasts.EnableStatBonuses = false` before reading vanilla ones.

### `m_nonPlayer`

`HitData.DamageTypes` gained an `m_nonPlayer` channel in 1.0, and
`ApplyRoundedTenPercentDamageBonus` now includes it. Player weapons carry 0 there
today, so this changes no number in the game as it stands. It is worth doing
anyway because the method's contract is "every damage channel", and the failure
mode of the alternative is a two-handed weapon quietly missing part of its +10%
years from now, in a build where nobody remembers the list was written against
0.221.

`Heightmap.Biome.DeepNorth` itself needed no code change — the mod only ever
compares against `Biome.Ocean`, in `OceanWeather`.
