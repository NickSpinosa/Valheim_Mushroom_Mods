# Leveling terrain on Valheim 1.0

Why `PortalTerrainLeveler` no longer builds a `TerrainOp`, and what 1.0.7
changed to force that. Read before touching the portal pad leveling. Everything
here was read out of a decompile of `assembly_valheim.dll` (issue #25).

## What broke

`TerrainOp.Settings` used to travel over the wire field by field. On 1.0.7 it
does not:

- `Settings.Serialize(ZPackage, GameObject prefab)` writes exactly one value —
  `Utils.GetPrefabName(prefab.name).GetStableHashCode()`.
- `Settings.Deserialize(ZPackage)` reads that hash and answers with
  `ObjectDB.instance.TryGetTerrainOp(hash, out prefab)`, returning
  `prefab.m_settings`. A miss logs `Failed to deserialize TerrainOp settings for
  prefab hash …, cancelling TerrainOp` and returns null, and
  `TerrainComp.RPC_ApplyOperation` drops the operation.

The lookup is `ObjectDB.m_terrainOpsByHash`, built in `ObjectDB.UpdateRegisters`
from the `m_terrainOps` list. So on 1.0.7 the *only* settings a TerrainOp can
carry across `ApplyOperation` are the settings of a prefab ObjectDB already
knows. The mod's ad-hoc `new GameObject("SeparateSpawns_PortalTerrainLevel")`
with a `TerrainOp` added at runtime is not in that list, so every op it raised
was cancelled — ten per portal, one per pass — while the mod logged success.

Note that the hop happens even when the server is alone in the world:
`TerrainComp.ApplyOperation` always goes `Serialize` → `InvokeRPC` →
`RPC_ApplyOperation` → `Deserialize`, including when the caller owns the
TerrainComp. There is no local shortcut to fall back on.

## The approach taken: drive TerrainComp directly

`PortalTerrainLeveler.ApplyLevelOperation` now finds the heightmaps within the
op radius, calls `Heightmap.GetAndCreateTerrainCompiler()` on each — the same
call `TerrainOp.Awake` makes — and invokes the private
`TerrainComp.DoOperation(Vector3 pos, Vector3 rot, TerrainOp.Settings)` through
`AccessTools`, passing a `TerrainOp.Settings` the mod owns. No prefab, no
ObjectDB entry, no ZPackage.

This is not a local-only hack. `DoOperation` is the method
`RPC_ApplyOperation` itself calls, and it ends in `Save()`, which writes the
compiler's height and paint deltas to `ZDOVars.s_TCData` on the TerrainComp's
ZDO. That is the same persistence and the same replication path a hoe swing
produces: clients pick the change up in `TerrainComp.CheckLoad` when the ZDO's
data revision moves. Only the *settings* stop travelling, and nothing needs
them once the deltas exist.

`Save()` is a no-op unless the compiler owns its ZDO, so the leveler claims
ownership (`ZNetView.ClaimOwnership`) before applying. During bootstrap the
server created the compiler moments earlier and already owns it; the claim
matters for a later repair pass, where a client could own the zone.

### Why not register a prefab with ObjectDB (the issue's first option)

It works, and the repo has the pattern for it (`ObjectDB.Awake` +
`ObjectDB.CopyOtherDB` postfixes in haldor-expansion, `m_prefabs` /
`m_namedPrefabs` in CraftableSpawners). It was rejected for three reasons, in
order of weight:

1. **It leaves the outcome in someone else's ObjectDB.** The deserialize runs
   on whoever *owns* the TerrainComp, not on whoever raised the op. Separate
   Spawns is installed on clients too, but nothing guarantees a given client's
   ObjectDB was patched in the same order, and a vanilla-ish client owning a
   zone would drop the op with the exact error this ticket is about. Approach B
   cannot fail that way: the deltas are computed before anything is sent.
2. **Two rebuild hooks and an ordering trap for no gain.** `UpdateRegisters` is
   private and is called from both `Awake` and `CopyOtherDB`, so the
   registration has to be re-applied on each; a prefab added after
   `ZNetScene.Awake` builds `m_namedPrefabs` is unknown to ZNetScene. All of
   that machinery would exist only to hand a hash back to ourselves.
3. **Varying the radius means varying the prefab.** Settings are now a property
   of the prefab, so a per-portal radius would mean a prefab per radius. The
   settings object in approach B is just a field.

The one thing approach A buys is avoiding reflection on a private method. That
is a real cost: if `DoOperation` is renamed, leveling stops. It is bounded — the
lookup is resolved once and logs a specific error naming this file when it
fails, and the leveler then reports honestly instead of claiming a pad it never
made.

## The pass loop, and why ten passes were wrong

The old code ran `ApplyLevelOperation` ten times in a row inside one frame. Once
the ops actually apply, that is actively harmful, and the reason is worth
writing down:

`TerrainComp.LevelTerrain` computes its delta as `target - m_hmap.GetHeight(x, y)`
and *accumulates* it into `m_levelDelta`. `GetHeight` reads `m_heights`, which
only reflects the compiler's deltas after `Heightmap.Regenerate()` has run.
`DoOperation` ends in `Poke(1)`, which regenerates in `LateUpdate` — not inside
a synchronous loop. So passes two through ten would each read the same stale
height and add the same full delta again, ten times over, until
`m_levelDelta` hit its ±8 m clamp. Ten passes in a frame do not converge; they
build a pillar or a pit.

The loop now regenerates between passes (`Heightmap.Poke(0)` on every map the
op touched, which calls `Regenerate` immediately) and stops as soon as the
centre vertex is within 5 cm of target, capped at four passes. Two is the
normal count, because of how the two deltas interact:

- Pass 1: `LevelTerrain` puts the pad on target, then `SmoothTerrain` adds a
  further delta computed from the pre-level height, so the pad overshoots.
- Pass 2: `LevelTerrain` folds `m_smoothDelta` back into `m_levelDelta` and
  zeroes it (`num4 += m_smoothDelta[i]; m_smoothDelta[i] = 0f`), which — since
  the regenerated height already contained that smooth delta — lands the total
  at exactly `target - base`. The smoothing pass that follows now measures a
  pad already on target and contributes ~0.

The blend ring between the level radius (5 m) and the smooth radius (7 m) keeps
whatever smoothing it accumulated; `m_smoothDelta` is clamped to ±1 m, so the
ring cannot be dragged further than that regardless of pass count.

## Traps found along the way

- **`Heightmap.ForceGenerateAll()` does almost nothing on 1.0.7.** It only
  regenerates maps where `HaveQueuedRebuild()` is true, and that is
  `m_doLateUpdate == 2`. Nothing in the game sets 2 except
  `TerrainModifier.PokeHeightmaps`; every TerrainComp path uses `Poke(1)`.
  The pre-existing `ForceGenerateAll` calls in this file are therefore only
  useful for TerrainModifier-driven rebuilds (a location's own terrain mods),
  never for the mod's own ops. Do not rely on it to flush a level op — poke the
  specific heightmaps.
- **`Poke` is `Poke(int delayed = 0, bool paintOnly = false)`.** `Poke(0)`
  regenerates now, `Poke(1)` defers to this frame's `LateUpdate`. Already noted
  in `valheim-1.0-save-system.md`; it is the reason the immediate rebuild is
  available at all.
- **`TerrainComp` is created by plain `Object.Instantiate` of
  `Heightmap.m_terrainCompilerPrefab`**, not through `ZNetScene.CreateObject`,
  and its `Awake` runs synchronously inside that call — so the compiler is
  usable on the same line. It refuses to initialise if
  `Heightmap.FindHeightmap(transform.position)` misses, which is why the
  leveler still defers a job whose zone has not streamed in yet.
- **The op position is `(x, groundY, z)`, and `y` is load-bearing.**
  `LevelTerrain` levels to `worldPos.y` relative to the compiler transform. The
  measured settle value that the portal is then aligned to still comes from
  `PortalGroundHelper.MeasureGroundAt`, i.e. a physics raycast, so it reflects
  the rebuilt collision mesh rather than what we asked for — keep those two
  numbers in the log line, a divergence between them is the signal that
  leveling silently failed.
