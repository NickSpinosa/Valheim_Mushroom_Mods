# Forge of Potential odds

Status: **v0.8.2 implemented** (`Forge of Potential.EnableIntendedOdds`, default
**true**).

## What we ship

Restore the odds implied by `ItemDrop.ItemData.SharedData`'s field defaults:

| Outcome | Chance |
|---|---|
| Success (+1 quality) | 65% |
| Downgrade (−1 quality) | 25% |
| Destroy (partial material refund) | 10% |

Opt out with `EnableIntendedOdds = false` when IronGate ships a real fix (or if
you want vanilla’s current “every fail destroys” behaviour).

Synced via MushroomSync with the rest of Combat Adjustments.

## What vanilla actually does

`InventoryGui.DoCrafting` rolls once:

```
r = Random.Range(0, 1)
if upgradeChance >= r          → success
else if breakChance >= 1 - r   → destroy
else                           → downgrade (quality − 1)
```

`SharedData` field defaults are `m_upgradeChance = 0.65`, `m_breakChance = 0.1`
— which is the table above. **Every idol prefab overrides `m_breakChance` to
`1.0`**, so the break branch covers the entire failure band and downgrade never
runs. Observed play (many destroys, zero downgrades) matches that, not the
defaults.

Material refund on break is a separate field (`m_breakReturnIngreientsAmount`);
idols set it to `0.35`. This patch does not touch it.

## Quality-1 edge case

The downgrade path writes `quality - 1`. At quality 1 that is **0**, which is
not a real item level. While the odds patch is on, refining a quality-1 piece
temporarily forces `m_breakChance = 1` for that craft so every failure destroys
instead of producing a quality-0 ghost. Quality 2+ keep the 65 / 25 / 10 split.

## Downgrade toast

Vanilla posts `$msg_upgrader_failed` on a level drop. While the odds patch is
on, that localisation is replaced with
`<item name> downgraded to level <new quality>` so the outcome is obvious next
to the success / broke messages.

## Why ObjectDB, not a DoCrafting rewrite

Same pattern as feast / shield stats: mutate the idol’s shared data when
ObjectDB loads, cache the vanilla originals, and push again on config change /
host sync. The roll logic itself stays vanilla — only the idol numbers change.
Turning the config off restores the prefab values and you are back on IronGate’s
behaviour with no leftover state.
