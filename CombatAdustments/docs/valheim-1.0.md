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
was first written, so the names below were recovered offline instead, and the dump
tool was built as part of the same change so the next person never has to repeat
it. **The dump has since been run on 1.0.7** (Sep 2026) and the tables are
corrected against it; what follows keeps the offline method and its score, because
the next game update will pose the same problem.

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
North is `FeastDeepNorth` / `SpiceDeepNorth` / `ShieldGold{,Tower,Buckler}` /
`THSwordGold_BloodLightning`. Elemental variants did break with precedent — they
took an underscore and a compound element (`_FrostFire`, `_BloodLightning`,
against Ashlands' `Blood` / `Lightning` / `Nature`) — but the shields did not.

### The shields: the guess was wrong, and it was wrong the informative way

The tables first shipped `ShieldRoundGold` / `ShieldTowerGold` /
`ShieldBucklerGold`, on the reading that 1.0 had started spelling the shape out
in the name. The 1.0.7 dump has none of those three. The real names are

```
ShieldGold          Round    q3  blockPower 132  perLevel 6  maxBlock 144  parry 1.5
ShieldGoldBuckler   Buckler  q3  blockPower  88  perLevel 6  maxBlock 100  parry 2.5
ShieldGoldTower     Tower    q3  blockPower 166  perLevel 7  maxBlock 180  parry 0
```

— exactly the `ShieldFlametal` / `ShieldFlametalTower` precedent, with the
buckler suffixed the same way. Both candidate spellings really were in the
bundles, so the bytes could not settle it; the tie-break should have been the
precedent, not the *appearance* of a new convention in a set of truncated hits.
The lesson to keep: when two live candidates disagree, the one that matches how
the previous tier was named is the better guess, and a substring that merely
*exists* is not evidence that the other one does not.

The tower's numbers there are worth one more line, because they misread easily:
166 / 180 is post-mod. `Shield.EnableTowerArmorBonus` has already added its +5%
(`ceil(158 × 1.05) = 166`, `ceil(6 × 1.05) = 7`), so vanilla is 158 / +6 / **170**
— which is the figure the leftover seed is derived from in
`shield-rework-requirements.md`. The dump now says this in its SHIELDS header.

### How the offline guesses scored

The 1.0.7 dump settled every row. Kept for the next time someone has to recover
names without a running game, because the *shape* of the errors is the reusable
part:

- **Every weapon row was right**, including the ones that were only ever seen
  truncated: `BattleaxeGold`, all three `_FrostFire` and all three
  `_BloodLightning` stems, `AxeJotunBane`. Truncation-plus-family-pattern is a
  good inference when the family is real and no rival spelling exists.
- **Every feast and spice row was right**, `SpiceDeepNorth` and `FeastDeepNorth`
  included.
- **All three shield rows were wrong** — the one place two live candidates
  existed and precedent was overruled. See the section above.
- `defeated_frozenking` is **still unconfirmed**: the dump's boss-key block reads
  every key `set: False` in a world where no boss has been killed, which proves
  nothing either way. Nothing gates on it (feasts shift to the *previous* boss),
  so it stays as-is until someone kills the Deep North boss and re-dumps.

**Deliberately excluded.** `<Weapon>GoldUncooked` is a crafting intermediate — a
Material, not a weapon — and the tables must not carry it (the dump confirms
those prefabs exist and are Materials). `Axe1h_JotunWarrior`,
`Axe2h_JotunWarrior` and `Sword2h_JotunWarrior` are the JotunWarrior enemy's own
weapons; `Axe1h_JotunWarrior 1` even shows up in the SHIELDS section as a q1
round with 10 block power, which is what an enemy's off-hand looks like, not a
player item.

A wrong guess is not silent for feasts — `FeastStats.CanonicalPrefabs` logs
`prefab '…' not found in ObjectDB` at startup. It *is* silent for a weapon or
shield row, which simply never matches. That asymmetry is why the dump has a
coverage section that prints `MISSING` for every table entry ObjectDB does not
have — every entry except the handful of deliberate aliases, which it labels as
such so `MISSING` keeps meaning "defect".

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
type, plus shield block/parry/grant lines and food health/stamina/eitr lines.

Two things it learned from its first real run:

- **Read the numbers as *current*, not vanilla.** Food rows are post-bonus and
  tower block armor is post-+5%. Both are now labelled in the file. Feast rows
  additionally print the vanilla triple beside the current one, taken from the
  `FeastStats.Originals` cache — the mod has to keep the pre-bonus values anyway
  so a config change can be re-applied idempotently, so "what is vanilla
  `FeastDeepNorth` eitr?" is answerable from a normal dump instead of requiring a
  second run with `Feasts.EnableStatBonuses = false`.
- **`MISSING` has to mean defect.** The first run printed it for `FeastSwamp`,
  `FeastMountain` and `FeastOcean`, which are deliberate singular aliases sitting
  next to the real plural names, so a correct dump appeared to contain three
  faults. Those entries are now listed in `FeastStats.AliasPrefabs` and report
  `alias (not in this version)`. A report whose expected output is "three of these
  are fine" trains the reader to skim past the line that matters; the target is
  zero `MISSING`, and anything else is worth acting on.

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
