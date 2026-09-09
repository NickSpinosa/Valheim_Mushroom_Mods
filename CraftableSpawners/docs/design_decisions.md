# Craftable Spawners — Design Decisions

## Locked in

- **Implementation:** Clone vanilla spawner prefabs into separate craftable prefabs; do not mutate world spawners in place.
- **Source prefabs:**
  - Skeleton / Evil bone pile → `BonePileSpawner`
  - Greydwarf nest → `Spawner_GreydwarfNest`
  - Draugr / Body pile → `Spawner_DraugrPile`
  - Surtling / Fire pillar → built from `BonePileSpawner` structure + `Surtling` SpawnArea retarget + fire visual (vanilla `Spawner_imp_respawn` is an invisible CreatureSpawner with no mesh)
  - Tar blob / Bone pile → built from `BonePileSpawner` structure + `BlobTar` SpawnArea retarget + `lox_ribs` (tar pit bones) visual (vanilla `Spawner_BlobTar_respawn_30` is an invisible CreatureSpawner; trophy is `TrophyGrowth`)
- **Build UI:** Hammer → Misc tab; reuse existing spawner models; placeholder icons OK.
- **Removal:** Hammer-removable with full material refund.
- **Recipe unlock:** Not known by default. Each craftable spawner unlocks independently when the player obtains the **first** trophy of that type (Skeleton / Greydwarf / Draugr / Surtling / Growth). Full recipe counts are only required at craft time.
- **Spawn behavior:** Exact vanilla clone behavior (timers, caps, ranges, etc.). No spawn-tuning config in v1. Custom rebuilds (Fire pillar, Bone pile/tar) use a 20s SpawnArea interval so bases feel usable.
- **Destructibility:** Keep vanilla combat/destruction behavior on clones.
- **Material refund:** Full recipe refund on **both** hammer remove and combat destruction.
- **Placement:** Ground-only; no biome restriction.
- **Unlock retroactivity:** If the player already knows/discovered the relevant trophy, unlock that spawner immediately on load (not only on a new pickup).
- **Config:** BepInEx config file named `CraftableSpawners` (i.e. `CraftableSpawners.cfg`). Values sync between server and clients through the shared [MushroomSync](../../MushroomSync/README.md) plugin. ServerSync was removed - it wrapped login sockets and broke on a Valheim update - and this mod carried its own replacement until that was factored out.
  - Client path: `Valheim/BepInEx/config/CraftableSpawners.cfg`
  - Dedicated server path: `Valheim/config/bepinex/CraftableSpawners.cfg` (fallback when client file is missing; overlays client when both exist)
  - **v1 config contents:** enable/disable each of the spawners; recipe ingredient amounts (proposed numbers as defaults).
  - **Not in v1 config:** unlock rules, refund behavior, ground-only, spawn stats, workstation requirement.
- **Display names:**
  - Evil bone pile — Spawns skeletons
  - Greydwarf nest — Spawns greydwarves
  - Body pile — Spawns draugr
  - Fire pillar — Spawns surtlings
  - Bone pile — Spawns tar blobs
- **Icons (v1):** Use the matching trophy icons as placeholders.
- **Combat destruction refund delivery:** Drop recipe materials as world pickups at the spawner position (not into inventory).
- **Hammer remove refund delivery:** Normal Valheim piece recover into the removing player’s inventory.
- **Plugin identity:** Author `Gonfreecss`; BepInEx GUID `Gonfreecss.CraftableSpawners`; display name `CraftableSpawners`; version starts at `0.1.0`.

## Recipes (proposed defaults; configurable)

| Spawner | Materials |
| --- | --- |
| Skeleton | 40 BoneFragments, 5 TrophySkeleton |
| Greydwarf | 20 GreydwarfEye, 10 AncientSeed, 5 TrophyGreydwarf |
| Draugr | 40 Entrails, 5 TrophyDraugr |
| Surtling | 20 SurtlingCore, 20 Coal, 5 TrophySurtling |
| Tar blob | 40 Tar, 5 TrophyGrowth |

## Open / revisit later

- **Crafting station requirement:** For now, **no workstation** — Hammer + materials only (like typical Misc builds). Revisit whether these should require Workbench, Forge, or another station before release or as a balance pass.

## Implementation notes

- Plugin GUID: `Gonfreecss.CraftableSpawners`
- Config file paths:
  - Client: `Valheim/BepInEx/config/CraftableSpawners.cfg` (client save target; host values are applied at runtime and never written here)
  - Dedicated server: `Valheim/config/bepinex/CraftableSpawners.cfg` (server save target / fallback)
- Build: `dotnet build -c Release`. Also builds MushroomSync, which this mod references and cannot load without. ILRepack no longer merges anything — `ILRepack.targets` records that ServerSync was removed.
- Output: `bin/Release/CraftableSpawners.dll` **and** `MushroomSync/bin/Release/MushroomSync.dll` → copy both to `BepInEx/plugins/`, or use the release's `MushroomMods-plugins.zip`
- Clone prefab names: `CS_BonePileSpawner`, `CS_GreydwarfNest`, `CS_DraugrPile`, `CS_FirePillar`, `CS_TarBonePile`
- Valheim 1.0 replaced the category grid with a tag-driven build UI. What that means for the pieces this mod adds — and which Harmony targets were re-checked against 1.0.7 — is in [valheim-1.0-buildui.md](valheim-1.0-buildui.md).
