# Separate Spawns

A Valheim mod (BepInEx) that splits players into groups at world creation and gives each group its own starting area, far from the other groups, with everything needed to progress through the early game. Each group is linked back to the world center by a private portal.

Terminology used below is defined in [CONTEXT.md](docs/CONTEXT.md).

## Player and group configuration

- A JSON roster in `BepInEx/config/` maps each group (generic names: `groupA`, `groupB`, ...) to its members' Platform User IDs (e.g. `Steam_7656...`).
- A standard BepInEx `.cfg` holds all tunables: score weights, radii and distances, burial chamber count, surtling core cost, layout cap, reroll cap.
- A player not present in the roster is assigned a random group on first join; the assignment is written back into the JSON. The roster is the single source of truth for membership.
- Spawn priority is vanilla except the final fallback: logout point first, then bed, then the player's Group Spawn (instead of the sacrificial stones).

## Group Spawn placement rules

Every Group Spawn must satisfy all of the following:

- Lies in a Meadows biome patch that touches the coast.
- Has any Black Forest within 350m of the spawn, and at least 3 burial chambers in Black Forest within that same 350m.
- An Eikthyr altar (`Eikthyrnir`) within 200m. If none exists naturally, the mod places a functional altar within 200m of the spawn, on Meadows terrain, never underwater (up to 3 placement attempts if spawn fails).
- At least 1000m from the sacrificial stones.
- At least 500m from every other Group Spawn.
- Inside the world's inner 3000m radius.

## Layout generation and selection

- Candidate spawn points are enumerated on a 50m grid over the inner 3000m radius; only points passing all placement rules qualify.
- Up to 100,000 layouts are randomly sampled (one random candidate per group, all pairwise distances >= 500m) and scored.
- Score per layout (max 35):
  - Different islands: weight 16 (an island is a landmass surrounded by water; landmasses within 100m across water count as one island).
  - Closest-spawn separation: the layout whose closest two spawns are farthest apart gets full weight 10; the layout whose closest two are nearest gets 0; others scale linearly between those extremes.
  - Larger Meadows patches: weight 9.
- The highest-scoring layout is applied automatically at world creation. Once any player has spawned in the world, the layout is frozen and never changes.

## Infeasible seeds

- If a seed produces zero valid layouts, the mod logs the failure (including which rule eliminated the most candidates), deletes the world, and regenerates it with a new random seed.
- Rerolling only ever happens to a world in which no player has ever spawned, and is capped at 10 attempts. After that the mod fails loudly and players spawn vanilla at the sacrificial stones.
- If the roster becomes unsatisfiable on a world that has already been played (e.g. a group is added mid-game), the mod fails loudly for the affected group instead of rerolling; those players spawn vanilla until the admin resolves it.

## Layout report

- Informational only; never blocks world creation.
- After selection, the mod writes one PNG per top-10 layout plus a score summary to the plugin folder.
- Each image shows the inner 3000m radius only: biomes, Group Spawns, burial chambers, Eikthyr altars, and the sacrificial stones, with the score breakdown.
- Purpose: let a human verify the placement rules produce well-spread groups that won't stumble into each other early.

## Group Portals

- One portal pair per group: one end at the Group Spawn, one end at the sacrificial stones.
- Stones-end portals are spaced evenly on a circle around the sacrificial stones (default radius 28m), at the temple plaza height.
- If a portal fails to place, the mod retries up to 3 nearby positions.
- Portals start inactive. A member of the owning group activates the pair permanently by interacting with the Group Spawn end while carrying 2 surtling cores (consumed; single interaction; no partial deposits; payment only at the spawn end).
- Only members of the owning group may teleport through, enforced at both ends.
- Group portals are indestructible, their pairing is fixed (no tag editing), and vanilla item-teleport restrictions apply. Portals a player crafts from the hammer are ordinary pieces: they can be dismantled, and their tags can be set normally.

## Build and install

1. Build the project:

```powershell
dotnet build SeparateSpawns.sln -c Release
```

2. Copy `bin/SeparateSpawns.dll` to `BepInEx/plugins/SeparateSpawns/` in your Valheim install (server and every client).

3. On first run the mod creates `BepInEx/config/abortipus.separatespawns.cfg` and `BepInEx/config/SeparateSpawns.groups.json`. Edit the JSON roster with each player's Platform User ID.

4. Optional: copy `UnityEngine.ImageConversionModule.dll` from Valheim's `valheim_Data/Managed` folder into `Libs/` and add a project reference if you want PNG layout reports instead of BMP fallback images.

## Runtime outputs

- Selected layout state: `BepInEx/config/SeparateSpawns/worlds/{worldUid}.json`
- Layout report images: `BepInEx/plugins/SeparateSpawns/SeparateSpawns/reports/{worldUid}/`
- Seed reroll attempts: `BepInEx/config/SeparateSpawns/rerolls/{worldName}.json`
