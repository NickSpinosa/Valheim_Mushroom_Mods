# Night spawns ignore boss kills

Status: **v0.9.0 implemented** (`Spawns.IgnoreBossNightSpawns`, default
**true**, synced).

## What we ship

A boss kill sets a world key (`defeated_eikthyr`, `defeated_gdking`, …). Some
**night-only** world spawners require that key, so killing the boss starts
spawning those creatures in biomes that did not have them. With the option on,
those spawners are disabled. Killing the boss no longer changes the night
table.

Left alone:

- Night spawners with no required key. Black Forest greydwarfs, mountain
  wolves, plains fulings, swamp wraiths, and the rest of a biome's own night
  list still run.
- Anything that also spawns during the day. The gate is `m_spawnAtNight &&
  !m_spawnAtDay`.
- Raids. `RandEventSystem.GetCurrentSpawners()` is passed to the same method
  with `eventSpawners: true`. Boss-gated events are not night world spawns.
- Odin's night visit after the Elder. Same shape (night-only,
  `defeated_gdking`), not a combat spawn. Prefab name containing `odin`.

`defeated_serpent` is not a boss key and is excluded, so a future night
spawner gated on a serpent kill is not swept up. Any other `defeated_*` key
counts, including a Deep North spelling we have not confirmed
(`defeated_frozenking` is the provisional one in `docs/valheim-1.0.md`).

## Why it is a copy, not a delete

`SpawnSystem.UpdateSpawnList` stamps a zone-ZDO timer with
`(groupSalt + prefabName + index).GetStableHashCode()`, and `index` is the
1-based position in the list it was given. Removing a row shifts every later
hash: those creatures' spawn timers reset, and two different prefabs can share
a key.

The patch replaces the list argument with a copy and sets `m_enabled = false`
on the suppressed rows. Same length, same order, the prefab's own list is not
written. A disabled row is skipped after the index is incremented, which is
what the timer needs.

## Vanilla (verified against 1.0 `assembly_valheim.dll`)

`SpawnData` has `m_requiredGlobalKey` and no "blocked by key" field. The check
in `UpdateSpawnList` is: if the key is non-empty and `ZoneSystem` does not have
it, that attempt stops. Night and day are separate flags
(`m_spawnAtNight` / `m_spawnAtDay`). Cross-biome night invaders are the rows
with a boss key and night-only flags (wiki spawn table: meadows greydwarfs
after Eikthyr, brutes/shamans after the Elder, skeletons after Bonemass,
fulings after Yagluth, seekers after the Queen, charred after Fader). Draugr
in the mist also requires `defeated_gdking` and is night-only, so it is
suppressed too.
