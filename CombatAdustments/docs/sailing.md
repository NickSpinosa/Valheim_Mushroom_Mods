# Sailing wind curve

Status: **v0.6.0 implemented** (`src/CombatAdjustments.ShieldRework`).

## Why

Vanilla `Ship.GetSailForce` scales sail push with a single
`Mathf.Lerp(0.25, 1, windIntensity)`. Clear weather already reaches ~70% of
vanilla max force, so ThunderStorm has little headroom left on that dial.

We keep the calm band feeling like vanilla, then give storms a second segment
that can climb past 1.0.

## What changed

Two linear segments (defaults):

| Wind | Factor | Notes |
| --- | --- | --- |
| 0% | **0.287** | CalmForceFactor |
| 60% | **0.70** | KneeForceFactor — same as vanilla at 60% |
| 70% | **1.025** | storm ramp |
| 80% | **1.35** | ThunderStorm floor is 80% |
| 90% | **1.675** | |
| 100% | **2.00** | MaxForceFactor |

Formula:

- `0 → CalmWindCeiling`: `Lerp(CalmForceFactor, KneeForceFactor, t)`
- `CalmWindCeiling → 1`: `Lerp(KneeForceFactor, MaxForceFactor, t)`

Angle handling, Moder (direction only), sail size, and per-ship
`m_sailForceFactor` are unchanged.

## Ocean storm frequency

Vanilla Ocean weather weights are Clear `1` and Rain / LightRain / Misty /
ThunderStorm `0.1` each → ThunderStorm ≈ **7.1%**.

This mod retargets Ocean ThunderStorm to **21%** by raising its weight to
`0.21 × 1.3 / 0.79 ≈ 0.346` (other weights left alone). Meadows / other biomes
are unchanged. Ashlands ocean is already locked to SeaStorm.

## Config (`Sailing.*`, synced)

| Key | Default | Role |
| --- | --- | --- |
| `EnableWindCurve` | true | Opt out to restore vanilla `Lerp(0.25, 1, …)` |
| `CalmForceFactor` | 0.287 | Factor at 0% wind |
| `KneeForceFactor` | 0.7 | Factor at `CalmWindCeiling` |
| `MaxForceFactor` | 2 | Factor at 100% wind |
| `CalmWindCeiling` | 0.6 | End of the calm segment (Clear weather's max wind) |
| `EnableOceanStormChance` | true | Retarget Ocean ThunderStorm chance |
| `OceanThunderStormChance` | 0.21 | Target fraction (vanilla ≈ 0.071) |

## Trap

Do not patch intensity by multiplying `m_sailForceFactor` globally — that would
scale every sail state the same way and ignore the weather curve. The intensity
lerp inside `GetSailForce` is the seam that already means “how hard is the wind
blowing.”

Ocean storm chance is a weight on `EnvMan` biome setup, applied in
`InitializeBiomeEnvSetup` and again when config sync arrives. Weather already in
progress is not interrupted — the next roll uses the new weights.

Existing installs that still have `StormCurveExponent` in `.cfg` can delete that
line; it is unused now.
