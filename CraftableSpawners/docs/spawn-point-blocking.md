# Spawn points, `m_onGroundOnly`, and buried rock

## The symptom

A craftable Greydwarf nest in a Plains base placed fine and never spawned. The
same trap design in a Meadows base worked. Quiet Nights was the first suspect
and was not involved: it edits `SpawnSystem` (ambient) entries only, and
`SpawnArea` reads nothing from them but the global `SpawnSystem.m_nospawn`.

## What `m_onGroundOnly` actually checks

`SpawnArea.FindSpawnPoint` tries ten random points within `m_spawnRadius`. A
point needs a floor (`ZoneSystem.FindFloor`), and when `m_onGroundOnly` is set
it must also not be `ZoneSystem.IsBlocked`:

```csharp
public bool IsBlocked(Vector3 p)
{
    p.y += 2000f;
    return Physics.Raycast(p, Vector3.down, 10000f, m_blockRayMask);
}
// m_blockRayMask = Default, static_solid, Default_small, piece
```

The name suggests "is something standing here". It is really "is there
anything in those layers anywhere in the vertical line through this point".
**Terrain is not in the mask**, so the ray passes through the ground and keeps
going. A rock buried metres under the floor blocks the point exactly like a
build piece on top of it.

## The trap design this matters for

Players build a floor over the spawn radius with one open cell, so creatures
appear in a pit. With `m_onGroundOnly` on, the floor blocks every point but the
open cell. That is intended — do not turn the flag off to "fix" a stuck
spawner; the owner asked for spawning on build materials to stay impossible.

In the Plains case `cs_diag` reported 40/40 points blocked: the floor over the
covered cells, and `___MineRock5` cells (`static_solid`) 2–9 m below the open
cell. A Plains boulder had been buried by terrain raising or partial mining.

Digging it out has a hard floor: `TerrainComp` clamps `m_levelDelta` to ±8 m of
the *generated* height (smoothing adds about 1 m), so rock deeper than that
cannot be reached. Moving the trap is the practical fix. A possible code fix,
not taken: a `SpawnArea`-scoped check that only counts hits above the terrain
surface.

## Which spawners use it

- Greydwarf nest, Evil bone pile, Body pile: cloned from vanilla, which ships
  them with `m_onGroundOnly = true`. Not overridden.
- Bone pile (tar blob): set `true` explicitly, so it works in the same traps.
- Fire pillar: `false`.

Both custom spawners pass the value through `ConfigureCustomSpawnArea`; it used
to force `false` for both.

## Ownership

`SpawnArea.UpdateSpawn` returns unless `m_nview.IsOwner()`. The owning client's
copy of this mod decides the prefab's settings, so every client needs the same
build — the server's copy does not affect spawning. `cs_diag` prints the owner;
another player's ID shows as "unknown peer" on a client, which is normal.

## `cs_diag`

Console command, not a cheat. For every `SpawnArea` within 40 m it prints each
gate from `UpdateSpawn`/`SpawnOne` as `ok` or `CLOSED` (ownership, active area,
player range, `m_nospawn`, near/total caps, prefabs, spawn point) and, for the
spawn point, groups what the `IsBlocked` ray hits by object, collider, layer
and height. Output goes to the console and `LogOutput.log`.
