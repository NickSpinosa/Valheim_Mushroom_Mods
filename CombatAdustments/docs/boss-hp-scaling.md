# Boss / multiplayer difficulty scaling

Status: **v0.7.0 implemented** (`Difficulty.EnableUncapHealthScaling`, default
**true**).

## What we ship

HP-only uncap: effective enemy HP keeps growing with nearby players past
vanilla’s **5**-player ceiling. Enemy damage dealt to players stays at the
vanilla cap.

Config (synced via MushroomSync):

| Key | Default | Role |
| --- | --- | --- |
| `Difficulty.EnableUncapHealthScaling` | true | Opt out to restore vanilla HP cap |

## Vanilla (verified)

Source: local `assembly_valheim.dll` (IL via Mono.Cecil). Defaults from
`Game..ctor`:

| Field | Default |
| --- | --- |
| `m_difficultyScaleRange` | `100` (meters, XZ) |
| `m_difficultyScaleMaxPlayers` | `5` |
| `m_damageScalePerPlayer` | `0.04` (+4% damage per extra player) |
| `m_healthScalePerPlayer` | `0.3` (+30% effective HP per extra player) |

`Game.GetPlayerDifficulty(Vector3)`:

1. If `m_forcePlayers > 0` (console/`players` force), return that.
2. Else `Player.GetPlayersInRangeXZ(pos, m_difficultyScaleRange)`.
3. Clamp to at least `1`.
4. Clamp to at most `m_difficultyScaleMaxPlayers` (**5**).

There is **no boss-specific path**. Bosses and trash mobs share this.

```
n = GetPlayerDifficulty(pos)

// Damage enemies deal (Character.RPC_Damage):
GetDifficultyDamageScalePlayer = 1 + (n - 1) * m_damageScalePerPlayer

// Damage players deal (Character.ApplyDamage) — “HP”:
GetDifficultyDamageScaleEnemy = 1 / (1 + (n - 1) * m_healthScalePerPlayer)
```

Displayed floating numbers use the pre-modifier total, so the HP bump is a
**hidden damage reduction**, recomputed every hit.

`TriggerSpawner.Spawn` also uses `GetPlayerDifficulty` for extra spawn caps. We
do **not** touch that path.

## Implementation

Harmony **Postfix** on `Game.GetDifficultyDamageScaleEnemy`:

1. If disabled, leave the vanilla (or Valheim Plus–prefixed) result alone.
2. Else recount nearby players like `GetPlayerDifficulty` but **without** the
   max-players clamp (`DifficultyScaling.UncappedPlayerDifficulty`).
3. Recompute `1 / (1 + (n - 1) * m_healthScalePerPlayer)` so we still honour
   whatever is in `m_healthScalePerPlayer` (including V+’s per-player % when
   `[Game]` is on).

`GetDifficultyDamageScalePlayer` is untouched → damage stays capped at 5.

## Valheim Plus

V+ `[Game]` (when enabled) patches the same scale methods to set per-player %
and optionally fake/fixed counts / range. It does **not** remove the natural
5-player cap.

Our Postfix runs after V+’s Prefix (which only writes `m_healthScalePerPlayer`)
and after vanilla’s capped math, then overwrites the float result. Compatible
as long as we keep using the field V+ sets.

Caveats if V+ `[Game]` is also on:

- `setFixedPlayerCountTo` / `extraPlayerCountNearby` still affect **damage**
  (and spawners) via `GetPlayerDifficulty`. Our HP path recounts natural nearby
  players (plus `m_forcePlayers`) and does not apply those fake counts.
- V+ may transpile a different difficulty range into `GetPlayerDifficulty`
  without changing `m_difficultyScaleRange`; our HP recount reads the field
  (vanilla 100 m).

Safest: leave V+ `[Game]` off (current local default) and use this toggle alone.

## Rejected approaches

- Raising `m_difficultyScaleMaxPlayers` — would uncap **damage and** HP (and
  TriggerSpawner).
- Postfix on `GetPlayerDifficulty` — fights V+’s Postfix on the same method.
