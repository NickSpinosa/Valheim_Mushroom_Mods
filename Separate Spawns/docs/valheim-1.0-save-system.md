# What Valheim 1.0 changed under this mod

Notes taken while fixing issues #7 and #8 against 1.0.7. Everything here was
read out of a decompile of `assembly_valheim.dll` / `assembly_utils.dll`; none
of it has been confirmed in-game yet (see "Still unverified" at the bottom).

## `ZDOMan.GetPortals()` no longer returns portals

It returns `Dictionary<ZoneSystem.SectorIndex, List<ZDO>>` — the portal ZDOs
bucketed by sector. The flat `List<ZDO>` the mod wants is now
`ZDOMan.GetPortalList()`, which flattens that dictionary on every call.

The failure mode is nasty because the name did not change: `foreach (var zdo in
GetPortals())` still compiles as a loop, `zdo` is just a `KeyValuePair` now, and
you get a wall of nineteen "no definition for GetString/GetBool/Set" errors
pointing at the loop *bodies* rather than at the one line that actually broke.
If a future update makes the same shape change somewhere else, look at what the
loop variable is, not at the members it fails on.

`GetPortalList()` allocates a new list each call. The mod calls it five times,
all during bootstrap or a portal repair pass, so that is fine — do not put it
in an `Update`.

## `Heightmap.Poke(bool)` is now `Poke(int, bool)`

`Poke(int delayed = 0, bool paintOnly = false)`. `delayed > 0` sets
`m_doLateUpdate` to that many frames; `delayed == 0` calls `Regenerate()`
immediately, which is what the old `Poke(delayed: false)` did. So `Poke(0)` is
the faithful translation, and `Poke(false)` would have silently compiled as
`Poke(0, false)`… except `bool` does not convert to `int`, which is the one
piece of luck here: the compiler catches it.

## The 1.0 world save layout, and what it costs the seed reroll

This is the part that matters. A world used to be two files next to each
other:

    <savedata>/worlds_local/<name>.fwl     metadata
    <savedata>/worlds_local/<name>.db      the ZDOs

1.0 replaced that with a folder per world:

    <savedata>/worlds_local/<saveName>/_main.<n>.fwl2      metadata
    <savedata>/worlds_local/<saveName>/_main.<n>.chunks    ) the ZDOs, split
    <savedata>/worlds_local/<saveName>/*.chunk             ) into chunk files
    <savedata>/worlds_local/<saveName>/_main.<n>.ok        write-completed marker
    <savedata>/worlds_local/<saveName>/cacheMinimap*       explored-map cache

and added one file that lives **outside** the folder:

    <savedata>/cache/<m_name>_biomedatacache.bin           alt-biome data

Four consequences the reroll has to respect.

### 1. A world now has two names, and they are not interchangeable

- `World.m_worldName` is the **save folder name**. `SaveSystem` indexes every
  save by it; `World.RemoveWorld(name, source)` resolves through
  `SaveSystem.TryGetSaveByName(name, ...)`, so it wants this one. A dedicated
  server's `-world` argument matches this one.
- `World.m_name` is the name stored *inside* the .fwl2 payload.
  `World.LoadWorld` sets `m_worldName` from the file on disk and `m_name` from
  the package, so renaming a world folder makes them diverge permanently.

`World.m_fileName` is gone; the mechanical replacement in the issue is
`m_worldName`, and that happens to be the correct one for deletion too.

The reroll's identity keys were audited against this: `WorldLayoutStore` is
keyed on `m_uid`, which `new World(name, seed)` *regenerates*
(`name.GetStableHashCode() + Utils.GenerateUID()`), so a recreated world can
never read the doomed world's layout file — correct, but it orphans that file,
so the reroll deletes it. `SeedRerollStore` was keyed on `m_name`; it is now
keyed on the save name, because that is the only identity guaranteed to be the
same before and after a delete-and-recreate. Keying the attempt counter on
anything the recreation regenerates silently resets the 10-attempt cap and
turns an infeasible-seed world into an infinite restart loop.

### 2. The alt-biome cache outlives the world folder

`AltBiomeWorldData.TryLoadCache` compares **only `Version.World`** — never the
seed, never the uid. So a `cache/<name>_biomedatacache.bin` left over from the
old seed will be loaded for the new one, and the server will run with biome
data that does not match its terrain.

`SaveSystem.Delete` does call `AltBiomeWorldData.RemoveCache`, but only on the
chunked-save branch, and it passes the *save* name while `SaveCache` and
`TryLoadCache` use `world.m_name`. The reroll therefore clears the cache
explicitly, under both names.

Why this does not bite on the first reroll but does on the second: a
freshly-created world has `m_worldVersion == 0` (the constructor never sets it;
only `LoadWorld` does, from the file), so the version comparison fails and the
cache is regenerated anyway. From the second reroll on, the world has been
loaded from disk at least once, `m_worldVersion == Version.World.DeepNorth`
(41), and a surviving cache matches and is reused. A bug that only appears on
the second iteration is exactly the kind that gets shipped.

The minimap caches, by contrast, live *inside* the world folder, so deleting
the folder is enough. `Minimap.DeleteMapTextureData` does not need calling.

### 3. The save number has to be reset to 0

The `.fwl2` filename embeds `SaveSystem.GetSaveNumber()`, and the chunk files of
a save carry the same number. `World.GetCreateWorld` calls
`SaveSystem.SetSaveNumber(0u)` before `SaveWorldFWLData` for exactly this
reason: a brand-new world has no chunk files, so its metadata belongs at number
0. Writing the replacement at whatever number the doomed world had reached
leaves a `.fwl2` advertising chunks that were just deleted. The reroll now
mirrors `GetCreateWorld` line for line.

`World.SaveWorldMetaData` was renamed `SaveWorldFWLData(DateTime)`. It creates
the world folder itself (`FileWriter` calls
`FileHelpers.EnsureDirectoryExists`), and it calls
`SaveSystem.InvalidateCache(SaveDataType.World)` on the way out, so the next
`TryGetSaveByName` rescans disk and sees the replacement. Nothing extra is
needed on either count.

### 4. `FileHelpers.FileSource.Local` is 2, not 1

`Auto = 1, Local = 2, Cloud = 4, Legacy = 8`. This matters more than it looks:
`SaveSystem.GetWorldsSaveRootPath` branches on `fileSource.IsLocal()` and
returns `…/worlds_local` or `…/worlds`. Get the value wrong and the replacement
world is written to a directory the server never looks in — a silent failure
that presents as "the reroll did nothing".

The old code reached for the enum by reflection
(`Type.GetType("FileHelpers+FileSource, assembly_utils")`) and fell back to the
literal `1`, i.e. `Auto`. That fallback is now unreachable because the project
takes a real `assembly_utils` reference, like every other mod in this repo.
That was the reason for the reflection in the first place: `FileHelpers` lives
in `assembly_utils`, while `World`, `SaveSystem` and `AltBiomeWorldData` are all
in `assembly_valheim`. One reference line is cheaper than guessing enum values.

## Not saving the doomed world

1.0 added a supported kill switch: `SaveSystemSessionFlags.DontSaveWorld`, set
via `SaveSystem.SetSessionFlags` and checked by `ZNet.Save` before it does
anything, and by `Game.UpdateSaving` before the autosave timer runs. That is
strictly better than the previous approach of reflecting
`Game.m_shuttingDown`, and the reroll sets it first.

`Game.m_shuttingDown` is still set on top of it, for a different reason:
it is what makes `Game.OnApplicationQuit` skip its `Shutdown(saveWorld)` call,
which would also write the **player profile**. `DontSaveWorld` alone does not
cover that. Note `SetSessionFlags` ORs into the field and there is no way to
clear it — acceptable only because the one caller quits immediately.

`ZNet.ShutdownWithoutSave(bool suspending)` still exists and still does not
save. `StopAll` joins the background save thread first, so there is no
half-written save racing the delete.

## Rejected

**Reusing the world's `World` instance and just changing its seed.** The seed
is baked into `m_seed`, `m_seedName`, the alt-biome cache and every generated
ZDO; there is no in-place reseed. Delete-and-recreate is the only path.

**Calling `World.GetCreateWorld` after the delete instead of hand-rolling the
recreate.** It generates its own seed and would work, but it also *loads* the
world if it finds one, so it depends on the delete having fully succeeded and
gives no way to carry the starting global keys across. Constructing the `World`
directly and following `GetCreateWorld`'s save sequence is the same three lines
with none of the ambiguity.

## Still unverified — needs a running 1.0.7 server

- Whether the whole reroll actually completes end to end. Force it with a tiny
  `InnerRadius`, watch for the "World '<name>' regenerated" line, restart, and
  confirm the new seed loads, the old save folder is gone, and layout
  generation proceeds.
- Whether the save folder is really deletable at the moment the reroll fires,
  i.e. whether `ShutdownWithoutSave` has released every file handle. The mod
  now checks `Directory.Exists` after the delete and aborts the reroll rather
  than writing a replacement `.fwl2` next to the old world's chunk files, so a
  failure here is loud instead of corrupting. If it does fail, the fix is
  probably a frame's delay between the shutdown and the delete.
- Whether a second consecutive reroll behaves like the first. That is the run
  where the stale alt-biome cache and the save number would bite if the
  reasoning above is wrong.
- Whether a dedicated server restarts cleanly after `Application.Quit` here, or
  whether the supervisor sees a non-zero exit.
