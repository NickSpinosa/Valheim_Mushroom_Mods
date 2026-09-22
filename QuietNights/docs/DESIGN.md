# Quiet Nights — design

**Status: built, not yet run in the game.** It compiles and the rule matcher has
been exercised offline. The manual test plan at the bottom has not been run, and
its step 0 is what confirms the default creature names.

Everything here about the game was read out of a decompile of
`assembly_valheim.dll` 1.0.7. What could *not* be read that way is called out,
because the most important input — the actual spawn table — is one of them.

## What the game does

Ambient spawning is `SpawnSystem`. One lives on every zone's `_ZoneCtrl` object
and ticks `UpdateSpawning` once a second. Each tick walks a list of
`SpawnSystem.SpawnData` entries, and for each entry rolls a chance and then
applies this gate (`SpawnSystem.UpdateSpawnList`):

```csharp
if ((!string.IsNullOrEmpty(spawner.m_requiredGlobalKey) && !ZoneSystem.instance.GetGlobalKey(spawner.m_requiredGlobalKey))
    || (spawner.m_requiredEnvironments.Count > 0 && !EnvMan.instance.IsEnvironment(spawner.m_requiredEnvironments))
    || (!spawner.m_spawnAtDay && EnvMan.IsDay())
    || (!spawner.m_spawnAtNight && EnvMan.IsNight()))
    break;
```

So "a night spawn that a boss kill unlocks" is not a special system. It is an
ordinary entry with three fields set a particular way:

| Field | Value |
|---|---|
| `m_requiredGlobalKey` | a boss key: `defeated_eikthyr`, `defeated_gdking`, `defeated_bonemass`, `defeated_dragon`, `defeated_goblinking`, … |
| `m_spawnAtNight` | `true` |
| `m_spawnAtDay` | `false` |

The key is set world-wide by `Character.OnDeath` through the boss prefab's
`m_defeatSetGlobalKey`. The spawn gate reads it with `ZoneSystem.GetGlobalKey`,
which is the *world* key list — not the per-player keys the "player events"
world modifier uses for raids. One kill by anyone turns these on for everyone.

Three facts about this system drive the whole design.

### 1. The spawn roll runs on a client, never on the dedicated server

`UpdateSpawning` opens with:

```csharp
if (!m_nview.IsValid() || !m_nview.IsOwner() || Player.m_localPlayer == null) return;
```

A dedicated server has no local player, so it never spawns anything. The roll
happens on whichever client owns the zone's ZDO. Two consequences:

- **The mod has to run on clients**, and a client without it spawns vanilla
  night creatures for everyone nearby. There is no server-only version of this
  mod. The README says so up front.
- **The server's config means nothing unless it reaches the clients.** That is
  why the plugin depends on MushroomSync from the first commit rather than
  growing it later: a rule list living only in the server's `.cfg` would
  suppress nothing at all.

### 2. The entries are shared objects, not per-zone copies

`SpawnSystem.m_spawnLists` is a list of `SpawnSystemList` components. Every
zone's `SpawnSystem` is instantiated from the one `ZoneSystem.m_zoneCtrlPrefab`,
and they all point at the same `SpawnSystemList` objects and therefore the same
`SpawnData` instances. Flip a field on one entry and every zone, loaded or not
yet loaded, sees it on its next tick.

**This one is an assumption until test 0 confirms it.** It holds if the prefab's
`m_spawnLists` point at separate prefab assets (Unity's `Instantiate` leaves
references to outside assets alone), and fails if the `SpawnSystemList`s are
children of `_ZoneCtrl`, in which case every zone gets its own clone. The code
cannot tell us which; the asset can. `quietnights dump` therefore also prints
whether a live zone's `m_spawnLists[0]` is reference-equal to the prefab's, and
the implementation handles both answers rather than betting on one: a
`SpawnSystem.Awake` postfix edits a zone's lists when they are not the prefab's
own, and `Apply` walks `SpawnSystem.m_instances` as well as the prefab.

1.0 added a second source: `AltBiome.m_spawn` (biome modifiers), walked by the
same method and reachable through the static `AltBiomeList.m_altBiomes`. Same
entry type, same gate.

### 3. An entry's position in its list is part of its identity

The per-entry spawn timer is stored on the zone ZDO under
`(groupSalt + prefab.name + index).GetStableHashCode()`, where `index` is the
entry's 1-based position. **Never remove or reorder entries** — every entry
after the removed one would inherit a different timer in every saved zone. Any
change has to be made in place.

## What cannot be known offline

The spawn table itself is serialized in the asset bundles, not in code. The
decompile shows the *shape* of an entry and nothing about which creatures,
which biomes, or which keys exist in 1.0.7 — and the memory note for this
machine records that there is no `strings`-style way to scan the bundles. The
pre-1.0 wiki table (greydwarves in the Meadows after Eikthyr, and so on) is a
hint at best; the premise of this mod is that 1.0 changed it.

So the mod ships with the tool that answers the question (`quietnights dump`),
and its default rules are prefix patterns precisely because the exact prefab
names could not be checked when they were written.

## The requirement

Take away the night spawns that a boss kill unlocks for **fulings, seekers and
the charred** — but only *away from home*. Fulings still belong in the Plains,
seekers in the Mistlands, charred in the Ashlands, at any hour and whatever keys
are set. What goes is those creatures turning up at night in biomes the players
already cleared.

## Implementation

### What counts as a covered entry

`SpawnSuppressor.Classify` looks at an entry's values *as the game shipped them*
and answers one of six things. An entry is touched only if all of these hold:

1. `m_requiredGlobalKey` matches `Rules.BossKeys` (default `defeated_*`);
2. its prefab matches a `Rules.Creatures` rule (default `Goblin*`, `Seeker*`,
   `Charred*`);
3. `m_spawnAtNight && !m_spawnAtDay` — night-only;
4. its biome mask reaches outside the creature's home biome.

Anything else is left exactly as it was, including the same creature's un-gated
entries in its own biome, which fail test 1 before biome is even considered.

### Suppress by narrowing the biome mask, in place

```
kept = entry.m_biome & creature.Native
kept == m_biome   -> NativeOnly   leave alone
kept == None      -> Disabled     m_biome = None
otherwise         -> Narrowed     m_biome = kept
```

One field, one edit, for both cases. An entry whose mask is entirely away from
home ends up with `Biome.None`, and that *is* a disabled entry:
`UpdateSpawnList` opens with `!m_heightmap.HaveBiome(spawner.m_biome)` and
`continue`s — before it reads or writes the entry's timer on the zone ZDO, so a
suppressed entry costs nothing per tick and leaves no trace in the save.
`IsSpawnPointGood` checks the same mask per point, which is what makes the
`Narrowed` case correct in a zone that straddles two biomes.

**`m_enabled = false` was the first design and was dropped.** It cannot express
"off here, on there" for an entry whose mask spans home and away, so the mask
had to be edited anyway; and once it is, the empty mask already does what
`m_enabled` would. Leaving `m_enabled` alone also means it stays purely the
game's (and other mods') flag: an entry that is already disabled when we find it
is skipped and never recorded, so restoring cannot switch on something someone
else switched off.

Originals are kept in a `ConditionalWeakTable<SpawnData, Original>`, not a
dictionary. If lists turn out to be cloned per zone (fact 2), those entries die
with their zone, and a dictionary would pin every one of them for the session.

**Why mutate data instead of patching the gate.** The gate sits mid-loop in a
90-line method, so reaching it means a transpiler — the most update-fragile kind
of patch, in the repo whose devops doc exists because of update breakage. A
prefix on `Spawn` is the other candidate and is worse: by then the timer has
been consumed and a spawn point searched for, every second, for nothing.
Editing public fields needs no publicizer; the Harmony targets are three
parameterless methods (`ZoneSystem.Start`, `SpawnSystem.Awake`,
`Terminal.InitTerminal`).

The cost is that shared state is being edited, so correctness rests on the
restore step. Tests 4, 5 and 7 are for that.

### When it runs

`SpawnSuppressor.Apply` restores every reachable entry, then re-applies the
current rules. "Reachable" is the zone-control prefab's lists, every
`AltBiomeList.m_altBiomes[*].m_spawn`, and the lists of live `SpawnSystem`s,
deduplicated by reference. It is called from:

1. a postfix on `ZoneSystem.Start`, which also caches the prefab's lists;
2. `ConfigSync.OnApplied` — host values arrived, or were dropped;
3. the three settings' `SettingChanged`, for single-player and a listen host,
   where no sync message ever arrives.

The prefab lists are cached rather than looked up through
`ZoneSystem.instance` because (2) fires on disconnect, when the instance may
already be gone. The prefab is an asset that outlives the world; edits left on
it would otherwise carry silently into the next world loaded that session.

`SpawnSystem.Awake` has a postfix too. It returns immediately when the zone's
lists are the prefab's own (the expected case) and exists only for the cloned
case, where every new zone would otherwise arrive unedited.

### The one case deliberately not handled

A covered entry with `m_spawnAtDay` also true. Removing only its night half
away from home means splitting it into two entries, and entries cannot be added
without the index problem in fact 3 (appending is index-safe, but a second entry
for the same prefab doubles the `m_maxSpawned` accounting in its home biome).
None is known to exist. `Classify` returns `SpawnsByDayToo`, the entry is left
alone, and the dump prints that verdict so it cannot hide.

### Config

All three synced through MushroomSync.

| Setting | Default | |
|---|---|---|
| `General.Enabled` | `true` | Master switch |
| `Rules.Creatures` | `Goblin*:Plains, Seeker*:Mistlands, Charred*:AshLands` | `Prefab:HomeBiome` pairs. Trailing `*` is a prefix match; `+` joins several home biomes |
| `Rules.BossKeys` | `defeated_*` | Which required keys count as a boss kill |

**The default prefab patterns are an educated guess, not a verified fact.** The
pre-1.0 names were `Goblin`/`GoblinArcher`/`GoblinShaman`/`GoblinBrute`,
`Seeker`/`SeekerBrute`/`SeekerBrood` and `Charred_Melee`/`Charred_Archer`/…,
which is why the defaults are prefixes. Test 0 is where that gets confirmed or
corrected; nothing offline can.

Prefix matching is deliberately the only wildcard. It is what the three
families need, and a bad item is reported once and dropped rather than failing
the whole list — one typo on the server should not turn every rule off for
every client.

### `quietnights dump`

Not a cheat; read-only. Writes to the BepInEx log and
`BepInEx/config/QuietNights.dump.txt`. The header records the settings in
effect, whether they came from the host, the world's global keys, and the
answer to fact 2 (`SHARED` / `CLONED`). Then one line for every entry that has a
required key *or* belongs to a covered creature — so the home-biome entries show
up alongside the ones being removed:

```
list0:… idx=41 name="…" prefab=Goblin biome=Meadows, BlackForest now=None key=defeated_goblinking night=1 day=0 enabled=1 max=… interval=… chance=… hunt=…  [Disabled, APPLIED]
```

`biome=` is always the shipped mask; `now=` appears only on edited entries. The
verdict is what the rules say *should* happen and `APPLIED` is whether it has,
so the two disagreeing is itself a finding (`Enabled = false`, or an entry some
other mod had already disabled).

### Not in scope

- **Raids** (`RandEventSystem`, `RandomEvent.m_requiredGlobalKeys`). Different
  system, different expectations.
- **Location spawners** (`CreatureSpawner.m_requiredGlobalKey`). Fixed points in
  camps and dungeons.
- **Creatures already alive.** Night-only spawns carry `SetDespawnInDay(true)`
  and leave at dawn on their own, so enabling the mod mid-night self-corrects
  within a day.

### Known interaction: Spawn That and friends

A mod that rewrites `SpawnSystemList` at world load races with the
`ZoneSystem.Start` postfix. Only entries that exist when `Apply` runs are
edited, so the failure mode is "their added entries are not suppressed", not
corruption. This machine's own plugin folder has ValheimPlus in it, which is
worth remembering if a test result looks wrong.

## Manual test plan

Vanilla dev commands (`devcommands` in the F5 console; launch with `-console`):

| Command | Use |
|---|---|
| `resetkeys` / `setkey defeated_goblinking` / `listkeys` | Fake a boss kill |
| `tod 0` / `tod 0.5` / `tod -1` | Force midnight / noon / release the clock |
| `test` then `test spawns 1` | Vanilla debug: logs `Spawning <prefab> at <pos>` and **pings the spot** for every ambient spawn |
| `killall` | Clear the area between runs |
| `debugmode` + fly | Cover ground; spawns land 40–80 m from the player |

The `test spawns` ping is what makes this testable at all: without it, "nothing
spawned" is indistinguishable from "it spawned behind a hill".

Verified offline so far: the project builds, Thunderstore packaging derives the
MushroomSync dependency, and `Rules` parsing and matching were run against the
real `Heightmap.Biome` enum in a scratch harness (prefix and exact match,
`+`-joined biomes, four kinds of malformed item). **Nothing below has been run
in the game yet.**

**0. Baseline dump.** Load any world, `quietnights dump`, open
`BepInEx/config/QuietNights.dump.txt`. Check, in this order:

- `Spawn lists =` says `SHARED`. If `CLONED`, the per-zone path is live and test
  4 matters more.
- There are `[Disabled…]` or `[Narrowed…]` lines for fulings, seekers and
  charred. **If a family has none, the default prefab pattern is wrong** — find
  the real names in the dump and fix `Rules.Creatures`.
- Every such line's `biome=` really is away from home, and the same creature's
  home entries read `[NotCovered]` or `[NativeOnly]`.
- No `[SpawnsByDayToo]` lines. If there are, that is the unhandled case above
  and needs a decision.
- Skim the other key-gated lines for anything 1.0 added that the group also
  wants gone.

**1. Reproduce vanilla.** `Enabled = false`. `resetkeys`, `tod 0`,
`test spawns 1`, fly the Meadows for ~5 minutes: no fulings.
`setkey defeated_goblinking`: they appear. *If this step cannot produce the
spawn, stop — every later "pass" would be meaningless.* Repeat for a seeker and a
charred entry, using the key and biome the dump gives for each.

**2. Suppression away from home.** `Enabled = true`. Same world, key still set,
`tod 0`, `killall`, same flight: no fulings in the Meadows, while other night
spawns the dump lists for that biome still appear.

**3. Home is untouched.** Same settings, fly to the Plains, day and night:
fulings as usual. Same for seekers in the Mistlands and charred in the Ashlands.
This is the half of the requirement an over-broad matcher would break.

**4. Live toggle and restore.** Edit the `.cfg` while in-world (or use
ConfigurationManager). `Enabled` off: Meadows fulings return within a few
minutes at `tod 0`, no relog, and the dump loses its `APPLIED` marks. On: gone
again. Delete `Goblin*:Plains` from the list: only fulings return.

**5. World switch.** Log out to the menu and load a different world in the same
session. The log shows one `Night spawns: N entries suppressed (N restored
first)` line per world start with the same N, not a growing one.

**6. Dedicated server, synced config.** Server with the defaults, client whose
local `.cfg` has `Creatures` empty. Join: the client's dump says
`Host values = yes` and shows `APPLIED`. Spawn check as in 2. Then change the
server's `.cfg` while connected and confirm the client follows.

**7. Disconnect drops the overlay.** That client leaves and loads a local world:
its own empty list applies, the dump shows nothing `APPLIED`, Meadows fulings
are back.

**8. The documented limitation.** Two clients, one without the mod, in
different zones. Suppressed creatures appear near the unmodded player only. Not
a bug to fix — a behaviour to confirm matches the README.

Tests 0–5 are single-player and fast. 6–8 need the dedicated server and are the
ones that justify the MushroomSync dependency.

## Open questions

1. **Are the default prefab patterns right?** Test 0 answers it.
2. **Is the name right?** `QuietNights` / `mushroom.quietnights`. Cheap to
   change now, permanent after the first Thunderstore publish.
