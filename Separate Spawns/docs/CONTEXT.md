# Separate Spawns

A Valheim mod that assigns players to groups at world creation, gives each group its own spawn point far from the others, and links each group back to the sacrificial stones with a group-restricted portal.

Other docs here: [spawn-priority.md](spawn-priority.md) for the vanilla
`FindSpawnPoint` ordering trap, and
[valheim-1.0-save-system.md](valheim-1.0-save-system.md) for what the 1.0 world
save layout means for the Seed Reroll — read that one before touching
`WorldBootstrap.HandleInfeasibleSeed` or `WorldDeleteHelper`, and
[valheim-1.0-platform-id.md](valheim-1.0-platform-id.md) for how the local
Platform User ID is found on 1.0 and the hosting-client deadlock that shaped it,
and [valheim-1.0-terrainop.md](valheim-1.0-terrainop.md) for why the portal pad
is flattened through `TerrainComp` instead of a `TerrainOp` — read that before
changing `PortalTerrainLeveler`, and
[world-lifecycle.md](world-lifecycle.md) for why per-world state is cleared on
`ZNet.OnDestroy` rather than plugin teardown — read that before adding any static
that holds world-derived data.

## Language

**Group**:
A named set of players that share one Group Spawn and one Group Portal.

**Group Spawn**:
The world position where members of a Group spawn and respawn, unless a player has a bed or logout point. Must lie in a coastal Meadows Biome Patch with Black Forest and enough burial chambers within BlackForestProximity (default 350m), inside the world's inner 3000m radius.

**Biome Patch**:
A contiguous region of a single biome, produced by flood-filling sampled biome values over the world grid. Patches under MinPatchArea (default 5000m²) are absorbed into their largest neighbor, and same-biome land patches merge across water gaps up to BiomeSplitGapDistance (default 50m). Biomes are not stored by the game; patches are a mod-side construct.
_Avoid_: Region, zone (zone means the game's 64×64m grid cell)

**Nearby Black Forest**:
Any Black Forest land within BlackForestProximity of a Group Spawn (default 350m). Burial chambers in Black Forest within that same radius count toward the minimum chamber requirement.
_Avoid_: Adjacent Black Forest

**Eikthyr Altar**:
The functional `Eikthyrnir` summoning location. Every Group Spawn must have one within 200m; if none exists naturally, the mod places one within 200m, on Meadows terrain and never underwater, retrying up to 3 different locations if spawn fails.
_Avoid_: Eikthyr statue

**Unassigned Player**:
A player whose Platform User ID appears in no Group in the config. Assigned to a random Group when they first join; the assignment is written back to the config, which remains the single source of truth for membership.

**Candidate Spawn Point**:
A point on the 50m grid over the world's inner 3000m radius that passes every Group Spawn placement rule. The pool from which Layouts are built.

**Layout**:
An assignment of one Candidate Spawn Point to each Group, with all pairwise distances at least 500m. Up to 100,000 Layouts are randomly sampled and scored; the highest-scoring one becomes the world's Group Spawns.

**Layout Score**:
A Layout's quality, the sum of three components: different islands (weight 16), closest-spawn separation scaled from worst layout (0) to best layout (weight 10), larger Meadows patches (weight 9). Maximum 35.

**Island**:
A landmass surrounded by water. Landmasses whose closest water crossing is at most IslandSplitGapDistance (default 100m) count as the same island. A mod-side construct, like Biome Patch.

**Layout Report**:
An informational set of images (one per top-10 Layout, everything within the inner 3000m radius) showing spawn points, burial chambers, Eikthyr Altars, and the Sacrificial Stones, with scores. Lets a human verify Groups are spread out enough not to meet early; never blocks world creation.

**Seed Reroll**:
Discarding a world whose seed cannot produce any valid Layout and regenerating it with a new random seed. Only permitted on a world where no player has ever spawned; capped at 10 attempts, after which the mod fails loudly and players spawn vanilla.

**Platform User ID**:
The stable per-account identifier (e.g. `Steam_7656...`) used in the group config to identify a player.
_Avoid_: Character name, player name

**Group Portal**:
A mod-placed portal pair linking a Group Spawn to the Sacrificial Stones. Stones-end portals sit evenly on a circle around the stones. Starts inactive; a Group member activates the whole pair permanently by paying 2 surtling cores at the Group Spawn end. Only members of the owning Group may teleport through it (enforced at both ends). Indestructible, fixed pairing (no re-tagging), vanilla item-teleport rules apply. A portal a player crafts from the hammer is not a Group Portal: it stays hammer-removable and its tag can be changed. Placement retries up to 3 nearby spots if spawn fails.

**Sacrificial Stones**:
The vanilla world-center spawn location (the `StartTemple` location). Group Spawns must keep their distance from it; Group Portals lead to it.
_Avoid_: World spawn, start temple (in prose)

**Spawn Difficulty**:
How hard a Group Spawn is relative to the other spawns in the same chosen Layout. Each group gets a rank from 1 (easiest) through N (hardest), where N is the number of groups in that Layout. Raw score combines meadows size (0–4, smaller patch scores higher, scaled relative to other spawns in the Layout) and nearby danger biome within 200m (0, 2 for swamp, or 6 for plains — not both; plains wins if both are within range). Written to the group roster when the Layout is chosen and never recalculated after the world is frozen; missing values are backfilled once from the frozen spawn positions.
_Avoid_: Layout score, difficulty tier (when meaning 1–6 fixed scale)
