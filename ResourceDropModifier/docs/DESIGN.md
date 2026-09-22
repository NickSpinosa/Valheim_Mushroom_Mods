# Resource Drop Modifier — Design

A per-item drop multiplier for every item in Valheim, stored as one config
setting per item in the plugin's own `.cfg`, grouped into a section per biome,
generated from the game data, edited like any other setting, and followed by
every client.

This document is the design `src/` implements. The "Where drops are decided"
section is the result of reading the decompiled 1.0.7 game code; re-check it
after a game update, the same way `docs/devops.md` says to re-check every
Harmony target. The patch targets, by file: `Patches.cs` holds all six patch
classes; `DropCatalog.cs` names every component field the walk reads.

## Purpose

A long-lived, heavily populated no-map server depletes some resources (copper
near bases, surtling cores) and drowns in others. Valheim's own **resource
rate** world modifier scales everything at once. This mod is the per-item
version of that dial: leave wood alone, double copper, turn off the tenth
trophy nobody wants.

**Non-goals for v1:** a different multiplier for the same item from different
sources (wood from trees vs wood from greydwarfs); a multiplier that depends on
where the drop happens (see "Grouping"); Haldor's stock (Haldor Expansion owns
that); fishing catches themselves (only their extra drops).

## What the admin sees

The plugin's ordinary config file,
`BepInEx/config/Gonfreecss.ResourceDropModifier.cfg` (`/config/bepinex/` on a
lloesche Docker server), with a section per biome and a setting per item:

```ini
[General]

## If on, the server owns the multipliers and connected clients use the server's values.
# Setting type: Boolean
# Default value: true
LockConfiguration = true

[Meadows]

## Wood. Drops from: Beech, Greydwarf, ... Vanilla amount x this.
# Setting type: Single
# Default value: 1
# Acceptable value range: From 0 to 10
Wood = 1

## Resin. Drops from: Beech, Greydwarf.
# Setting type: Single
# Default value: 1
# Acceptable value range: From 0 to 10
Resin = 1

[BlackForest]

## Copper ore. Drops from: MineRock_Copper.
CopperOre = 1
```

It was going to be a YAML file with a hand-written parser. It is a `.cfg`
because that is the only format the BepInEx **Configuration Manager** (the F1
window) can edit, and the format r2modman's and Thunderstore Mod Manager's
config editors understand with descriptions and ranges. Anything else, JSON
included, would have been a text file the admin opens in Notepad. The cost is
that the file is a couple of hundred settings long, which the mod managers
handle and the F1 window searches.

**Only items that drop from something are listed.** Crafted gear has no
setting: a bronze sword is made at a forge, not dropped, and a multiplier on
it would be a line that does nothing. The catalog walk decides: an item gets a
setting when some drop source names it, and not otherwise. Gear that a chest
or loot pile hands out *is* dropped, so it appears under that location's biome
with the chest as its source.

Rules the settings follow:

- **Keys are prefab names**, the ones `spawn` accepts in the console. The
  display name and where the item was seen dropping go in the description, so
  the F1 window shows them as a tooltip and the file carries them as `##`
  comments. Descriptions are regenerated on every bind; values never are.
- **A missing setting is 1.** BepInEx binds it back at the default on the next
  world load, so deleting a line is the same as writing `1`.
- **Values are `0` to `10`**, an `AcceptableValueRange<float>` so the F1 window
  draws a slider with a text box. BepInEx clamps out-of-range values on read,
  which is also how a typo of `100` for `1.0` gets caught. `0` removes the
  drop. Raise the bound in `DropConfig.MaxMultiplier` if a server wants more.
- **New items are added, old values are kept.** Binding is idempotent: an entry
  already in the file keeps its value, an entry not there appears at 1. A key
  the catalog no longer recognises (a renamed prefab after an update, a mod
  prefab from an uninstalled mod) is not deleted: BepInEx keeps orphaned
  entries in the file across saves. Nothing to implement.
- **A bad value changes only that setting.** BepInEx falls back to the default
  for a line it cannot parse and logs it; the rest of the file loads.

### Where the settings live is not where they are read from

The entries are how the multipliers are **stored and edited**. The multipliers
in force at a drop are a `MultiplierTable`, a plain dictionary rebuilt from the
entries' values, and on a client connected to a server that table comes from
the server, not from the client's own entries. A client's F1 window shows its
own settings, which are editable and ignored until it hosts or plays alone;
every entry's description ends with a line saying so.

### Why not MushroomSync's config sync

The obvious shape is `ConfigSync.Register` on every entry and let the value
overlay do the rest, as Combat Adjustments does. It does not work here, for a
timing reason: config sync **discards any path the receiving client has not
bound**, and it replaces its stored values wholesale on every payload. This
mod binds its entries during world load, from a catalog walk that needs
`ObjectDB` complete, and the host's payload arrives at peer-ready, which can be
before that. The client would apply nothing and wait for a broadcast that never
comes.

So the runtime table travels over a **raw channel** (`MultiplierSync`), which
sends a dictionary and does not care what the client has bound. Only entries
other than 1 are sent, so a tuned server's payload is a few dozen pairs. The
entries stay ordinary `ConfigEntry`s for editing; the channel is how their
values reach clients. `LockConfiguration` gates the send side, as in Haldor
Expansion and Craftable Spawners.

## Where drops are decided

Every drop of an item in vanilla goes through one of two seams. This was checked
by grepping the decompiled `assembly_valheim` for every reader of `DropTable`,
`CharacterDrop` and `Game.m_resourceRate`.

| Drop source | Component | Path to the item count |
|---|---|---|
| Creatures | `CharacterDrop.GenerateDropList` | `Game.ScaleDrops(GameObject, min, max)` per drop |
| Bushes, berries, mushrooms, flint, carrots | `Pickable.RPC_Pick` | `Game.ScaleDrops(GameObject, amount)` plus `m_extraDrops.GetDropListItems()` |
| Trees, logs | `TreeBase`, `TreeLog` | `DropTable.GetDropList()` |
| Rocks, ore veins | `MineRock`, `MineRock5` | `DropTable.GetDropList()` |
| Breakable props, bone piles, barrels | `DropOnDestroyed` | `DropTable.GetDropList()` |
| Dungeon loot piles | `LootSpawner` | `DropTable.GetDropList()` |
| Chests' starting contents | `Container.m_defaultItems` | `DropTable.GetDropListItems()` |
| Fish extra drops | `Fish.m_extraDrops` via `FishingFloat` | `DropTable.GetDropListItems()` |
| Beehive honey | `Beehive` | `Game.ScaleDrops(ItemData, 1)` |
| Sap collector | `SapCollector` | `Game.ScaleDrops(ItemData, 1)` |
| Single items lying in locations | `PickableItem` | `Game.ScaleDrops(ItemData, min, max)` |

Two facts make the patch set small:

1. **`Game.ScaleDrops` is already the resource-rate hook.** Every path except
   `DropTable.GetDropList()` funnels through one of its overloads, and the
   `ItemData` overloads receive the item, so they know what to look up. The
   `GameObject` overloads short-circuit when the world resource rate is 1 and
   never reach the `ItemData` overload, which is the one wrinkle: a prefix on the
   `GameObject` overloads has to route to the `ItemData` overload regardless of
   the rate, so that every path reaches exactly one postfix. Without that, a
   world with resource rate 1 would skip creature and pickable drops, and a
   world with any other rate would double-apply them.

2. **`DropTable.GetDropList()` returns a `List<GameObject>` with one entry per
   unit.** Trees, rocks and destructibles instantiate each entry as a stack of
   one. Multiplying is rebuilding that list: count each prefab, scale, emit.
   The public no-argument overload is the patch target; the private
   `GetDropList(int)` is what it calls, so the attribute needs an empty type
   list to pick the right one.

`DropTable.GetDropListItems()` reaches the `ItemData` overload of `ScaleDrops`
per entry, so its counts need no patch. It does get a small postfix for the
zero case: a multiplier of 0 leaves a stack of 0 in the list, which a chest
would store as an item of nothing and a pickable would drop as one object, so
empty stacks are removed from the result.

**The pickable floor.** `Pickable.RPC_Pick` computes
`Max(m_minAmountScaled, ScaleDrops(...))`, a floor vanilla keeps so a low world
rate never picks nothing. That would turn `0` into one berry and make `0.5` do
nothing on a one-item bush. A prefix sets `m_minAmountScaled` to 0 for the
duration of the pick when the item's multiplier is below 1, and a finalizer
restores it. At 1 or above the floor is untouched, so vanilla behaviour only
changes for an item the admin reduced. `PickableItem` has the same floor in
`GetStackSize` and keeps it: it is a single object lying in a location, not a
resource.

### `m_dontScale` is respected

Every drop definition (`CharacterDrop.Drop`, `DropTable.DropData`, `Pickable`)
carries a `m_dontScale` flag that exempts it from the resource rate. Vanilla
uses it for things a multiplier would break: boss trophies, keys, the wishbone.
On the `ScaleDrops` paths the exemption is automatic, because a `m_dontScale`
drop calls `Random.Range` directly and never enters `ScaleDrops`. On the
`GetDropList()` path the postfix has to look it up: `__instance.m_drops` holds
the flag per prefab, so entries flagged there are copied through unchanged.

Consequence for the description text: an item whose only drop source is
flagged `m_dontScale` has a setting that does nothing. The catalog walk can
tell, and the description should say `Not scalable: vanilla exempts this drop`.

### Composition with the world modifier

The mod multiplies **after** vanilla has applied `Game.m_resourceRate`. The
postfix sees the rate-scaled count and scales it again, so the two are
independent and multiply: resource rate "More" (1.5) with `CopperOre = 2`
gives 3x copper. This is the intended reading of "on top of", and it means
turning the world modifier off does not change the relative tuning in the file.

### Rounding

`MultiplierTable.Scale` rounds stochastically: `floor(n × m)` plus one with
probability equal to the fractional part. `0.5` on a creature that drops one
hide then drops it half the time, rather than always (`Mathf.Round` on 0.5
rounds to even, so vanilla-style rounding would have made 0.5 and 1.0
indistinguishable on single drops). `0` is exactly no drop. Results are never
negative and never clamped up to 1, unlike vanilla's `ScaleDrops`, because "turn
this off" is a use case.

### Caps the game imposes

- **Creature drops are capped at 100 objects per drop entry** in
  `GenerateDropList`, before the mod sees them, and the mod caps its own result
  at 100 again: 100 physics objects from one death is already a hitch.
- **Stacked drops clamp to the item's max stack size.** `ScaleDrops` on the
  `ItemData` overloads clamps to `m_maxStackSize`; the postfix re-clamps after
  multiplying. So a chest that would give 20 coins at 5x gives 100, not 999.
  Splitting overflow into extra stacks would need every call site patched and
  is out of scope for v1. Document it: multipliers on high-count stacked drops
  saturate.
- **`GetDropList()` entries have no clamp**, since each is its own object; the
  mod caps the rebuilt list at 200 entries per call for the same reason as the
  creature cap.

### Drops are generated on the owner, not the server

`CharacterDrop.OnDeath`, `Pickable.RPC_Pick`, `MineRock5.RPC_Damage` and the
rest run on **whichever peer owns the object's ZDO**, which on a dedicated
server is nearly always a client. A server-only patch would scale nothing. So:

- Every client needs the mod and the table. This is the same "everyone needs
  it" the other Mushroom mods carry, for a different reason.
- A client without the mod drops vanilla amounts for anything it owns. That is
  not detectable from the server without a handshake this mod does not have;
  the README says it plainly instead.
- There is no client-side opt-out: a client that could refuse the server's
  multipliers could farm at vanilla rates.

## Grouping

The sections group items by biome, but at runtime **a drop is looked up by the
item alone**. The biome is where the setting lives in the file, not a condition
on when it applies. `Wood = 2` under `[Meadows]` doubles wood from a Plains
birch too.

The alternative — a multiplier keyed by (biome the drop happens in, item) — was
considered and deferred. It is the more powerful reading of "grouped by biome"
and it fits a progression-gated server well (cheap wood in the Ashlands, where
nobody is going to build a base). It costs two things the v1 design avoids:

1. **`DropTable.GetDropList()` and `Game.ScaleDrops` have no position.** The
   biome would have to be captured by prefix patches on each of the eleven
   callers in the table above, stashed in a static, and cleared in a postfix.
   Eleven more patch targets to re-verify after every update.
2. **The file becomes ambiguous.** Wood would need a setting under every biome
   it can drop in, and an admin who sets one and not the others gets a
   surprise at the biome edge.

If per-biome behaviour turns out to be wanted, the sections already carry the
biome, so the change is a runtime lookup key and the position capture, not a
migration. `MultiplierTable` would gain a biome parameter with `None` meaning
"anywhere".

**Which biome an item is filed under** when it drops in several: the earliest
in `DropCatalog.BiomeOrder`, which is progression order (Meadows, Black Forest,
Swamp, Mountain, Plains, Ocean, Mistlands, Ashlands, Deep North). Wood goes
under Meadows, Resin under Meadows, Surtling cores under Black Forest even
though the Swamp has more of them. Each item appears **once**. An item no drop
source claims does not appear at all.

## Building the catalog

Runs on every machine, server and client alike, from a postfix on
`ZoneSystem.Start`, which is after `ObjectDB.CopyOtherDB` has replaced the
main-menu database with the real one and after `SetupLocations` has resolved
location prefabs. Clients run it too because their entries have to exist for
the F1 window and for the fallback when they leave a server; only the server's
values are broadcast.

Sources walked, in this order, each contributing (item, biome, source name):

1. **Spawn lists.** Every `SpawnSystemList` that `Resources.FindObjectsOfTypeAll`
   can see, prefab assets included: the lists are serialised on the zone
   controller prefab and the scene instance may not exist yet. Each
   `m_spawners[]` entry has `m_biome` as a flag set and `m_prefab` as the
   creature, whose `CharacterDrop.m_drops[].m_prefab` are the items.
2. **Vegetation.** `ZoneSystem.instance.m_vegetation[]`: `m_biome`, `m_prefab`.
   Inspect the prefab for every component in the table above.
3. **Locations.** `ZoneSystem.instance.m_locations[]`: `m_biome`, and
   `m_prefab` is a `SoftReference<GameObject>` (from `SoftReferenceableAssets.dll`,
   which the project references for this alone). When `IsLoaded` is false the
   walk calls `Load()` and `Release()`s afterwards, so memory is left as it was
   found; `ZoneSystem.SetupLocations` has usually loaded them already. Walk the
   loaded prefab's children for the same components, plus
   `CreatureSpawner.m_creaturePrefab` and `SpawnArea.m_prefabs[].m_prefab`,
   which lead to creatures, and `DungeonGenerator`, which leads to rooms.
4. **Dungeon rooms**, reached through a location's generator. The crypt chests,
   loot piles and body piles are in room prefabs, not the location prefab.
   `DungeonDB.GetRooms()` lists every room with its theme mask; a room is walked
   under the location's biome when `(room.m_theme & generator.m_themes) != 0`,
   with the same load-and-release handling as locations.
`ObjectDB.m_items` is **not** walked as a source. It is read only to build the
`SharedData` reverse map (see "Looking up the item at the patch point"); an
item that none of the three sources above reach gets no setting.

Prefab chains to follow, with a visited set because they recurse:
`TreeBase.m_logPrefab` → `TreeLog.m_subLogPrefab` (the log is where the wood
is); `Destructible.m_spawnWhenDestroyed`; `Growup.m_grownPrefab` and
`m_altGrownPrefabs[].m_prefab` for juveniles; `Pickable.m_itemPrefab` and
`m_extraDrops`; `Container.m_defaultItems`; `LootSpawner.m_items`;
`Beehive.m_honeyItem`; `SapCollector.m_spawnItem`; `Fish.m_extraDrops`;
`PickableItem.m_itemPrefab` and `m_randomItemPrefabs[].m_itemPrefab`.

A prefab reached from a drop list that has no `ItemDrop` is not an item (a
creature that spawns another creature on death, for instance) and is followed
as a prefab instead. The visited set is per root, so a Greydwarf reached from a
Meadows spawner and a Black Forest spawner records both biomes.

The display name for the description is
`Localization.instance.Localize(m_shared.m_name)`. `Localization` lives in
`assembly_guiutils`. Whether it initialises on the headless server is still to
be confirmed on the first dedicated-server run; the code falls back to the
token without its `$` when the result is empty or an unresolved `[...]`, so a
failure there costs the description a nicer name and nothing else.

**Cost.** The walk touches a few thousand prefabs once per world load and
should take well under a second; the elapsed time is logged so a regression is
visible. Location prefabs that were not already loaded are the expensive part;
they are loaded and released one at a time.

## Storing the multipliers

`DropConfig.BindAll` turns the catalog into entries on the plugin's `Config`:

- **Section** is the biome name from `Heightmap.Biome` (`Meadows`,
  `BlackForest`, ...). **Key** is the prefab name.
  BepInEx rejects `=`, newline, tab, backslash, quotes and square brackets in
  keys; prefab names are not expected to contain any, but the bind checks and
  logs a prefab it cannot key rather than throwing out of the walk.
- **Description** is the localized name, the drop sources, and the reminder
  that the server's value is the one in force while connected.
- **`AcceptableValueRange<float>(0, MaxMultiplier)`** for the slider and the
  clamp.

**Turn `SaveOnConfigSet` off around the bulk bind.** `ConfigFile.Bind` saves
the whole file on every new entry when it is on, which for several hundred
entries is several hundred rewrites of a file that is growing each time, on
the main thread, during world load. Set it false before the loop, `Save()`
once after, restore it. This is the trap most worth remembering from this
section.

**Late binding is fine for the F1 window.** Configuration Manager rebuilds its
list each time the window opens, so entries bound during world load appear
the next time F1 is pressed; verify on the first run rather than trust it.

After the bind, `DropConfig.BuildTable()` produces the `MultiplierTable`.
Server authority (dedicated server, host, single player) sets `Plugin.Table`
to it and broadcasts; a client stores it as `MultiplierSync.LocalTable` for
the fallback and waits for the server's.

## Reloading

Three ways a value changes after load, all ending in "rebuild the table,
broadcast if server":

- **The F1 window or `ConfigEntry.Value`** raises `ConfigFile.SettingChanged`.
  Subscribe once per file (the event is per file; CraftableSpawners' comment
  on `WatchForChanges` explains why not per entry) and debounce to one rebuild
  per second, since a slider drag fires per frame.
- **Editing the file on disk** (a dedicated server, or a mod manager's editor)
  does not raise anything by itself. A `FileSystemWatcher` on the config path
  sets a flag from its thread-pool thread; `Update` picks it up on the main
  thread and calls `Config.Reload()`, which raises `SettingChanged` per changed
  entry and `ConfigReloaded` once, both of which join the path above. The
  mod's own writes trip the watcher too - the bulk bind's `Save()`, and the
  save BepInEx does on every `Value` set - so watcher events within two
  seconds of an own write are ignored rather than reloaded.
- **`rdm_reload`** does the same on demand from the console, for when the
  watcher does not fire (network shares, some Docker volume drivers);
  **`rdm_rescan`** re-runs the catalog walk to pick up prefabs another mod
  registered late; **`rdm_show <prefab>`** prints the multiplier in force and
  whether it is the server's or the local one. They register from a postfix on
  `Terminal.InitTerminal` inside a try/catch, the same shape as Combat
  Adjustments' commands and for the same reason: the `ConsoleCommand`
  constructor has changed across updates, and a `MissingMethodException` there
  must not escape into the other mods' postfixes.

## General settings

Everything in `[General]`, that is, everything that is not an item:

| Setting | Default | Meaning |
|---|---|---|
| `LockConfiguration` | true | Host publishes its multipliers to clients. Off means clients use their own values, which is only sensible for testing. |
| `EnableDebugLogging` | false | Log every scaled drop: item, vanilla count, multiplier, result. Noisy. |

## Verification

Everything above compiles against 1.0.7 and is implemented; none of it has
been exercised in a running game yet. The checks, in the order the design
depends on them:

1. **The catalog.** Load a world; the log has one `Drop catalog:` line with the
   counts and elapsed time, and the config file has a section per biome.
   Compare it with the wiki's resource list: every gatherable resource should
   have a setting. A missing one means a drop component the walk does not
   read; the table under "Where drops are decided" is where to add it.
   Confirm the description names are localized and not `item_wood`; if they
   are tokens on the dedicated server only, `Localization` does not initialise
   headless and the fallback is doing its job.
2. **The F1 window** on a host shows the biome sections after a world load
   (Configuration Manager rebuilds its list on open; if it does not show them,
   that assumption was wrong and the entries need to be bound earlier).
3. **Each seam**, with `EnableDebugLogging` on: a creature (Greydwarf,
   `Wood = 2`), a rock (`CopperOre = 0`), a berry bush at `0` and at `0.5`, a
   chest in a burial chamber, a beehive. Each logs a line with vanilla count,
   multiplier and result.
4. **Sync**: join from a second client, confirm the "Using N drop multiplier(s)
   from the server" line, kill something as that client, confirm the count.
   Disconnect and confirm the fallback line.
5. **Reload**: drag a slider in F1 on the host, edit the file on the dedicated
   server, and run each console command.

Anything that turns out version-specific goes in the 1.0.7 lesson table in
`docs/devops.md`.

## Decisions

Settled on 2026-09-22, before implementation:

- **Biome grouping is presentation only.** Runtime lookup is by item. See
  "Grouping" for what the per-biome alternative would cost if it is ever
  wanted.
- **Crafted gear is not listed.** Only items a drop source names get a
  setting; there is no catch-all section.
- **No client-side opt-out.** A client follows the server or it is not on the
  server.
- **`m_dontScale` drops stay exempt**, matching vanilla, with no switch to
  override it.

Still open: the slider bound of 10 in `DropConfig.MaxMultiplier`. Enough for
tuning; not enough for "a hundred coins per chest". Easy to raise.
