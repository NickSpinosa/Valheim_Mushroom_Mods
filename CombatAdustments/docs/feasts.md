# Feasts

Status: **v0.5.0 implemented** (`src/CombatAdjustments.ShieldRework`).
Boss unlocks are hardcoded. Extra health / stamina / eitr are BepInEx-configurable
and server-synced with the rest of the plugin.

## What changed

Vanilla unlocks each biome feast after that biome's boss (the Bog Witch will not
sell the matching spice until the key is set). This mod moves that gate to the
**previous** biome boss, so the Plains feast is available after Moder instead of
Yagluth. Sailor's Bounty stays on the vanilla serpent key.

Placed / eaten feasts also get extra food stats (defaults):

| Feast | Health | Stamina | Eitr |
| --- | --- | --- | --- |
| All except the four rows below | vanilla +10 | vanilla +10 | vanilla |
| Sailor's Bounty | vanilla +15 | vanilla +15 | vanilla |
| Mushrooms Galore à la Mistlands | vanilla +10 | vanilla +10 | vanilla 33 +7 = **40** |
| Ashlands Gourmet Bowl | vanilla +10 | vanilla +10 | vanilla 38 +12 = **50** |
| Deep North (`FeastDeepNorth`) | vanilla +10 | vanilla +10 | vanilla 43 +17 = **60** |

The Deep North eitr number was an extrapolation when it was written — the ceiling
steps 40 → 50 over the two previous tiers, so 60 continues it, and +17 is what
reaches 60 *if* vanilla is 43, itself extrapolated from 33 → 38. The 1.0.7 dump
says vanilla **is** 43, so the value stands.

Getting that out of the first dump took arithmetic it should not have taken. Its
FOODS section read live `SharedData`, which the mod has already written to, so
`FeastDeepNorth` showed eitr 60 — the mod's own target, not the answer to "what is
vanilla?". Vanilla only fell out by subtracting the configured +17 back off, which
is circular-looking even when it is sound (60 − 17 = 43 is only informative because
the mod adds 17 to whatever it found, so 43 is what it found). The fix is in the
dump itself: `FeastStats` already caches every feast's pre-bonus values so a config
change can be re-applied idempotently, and the FOODS section now prints that cached
vanilla triple beside the current one. No `Feasts.EnableStatBonuses = false` round
trip, and no subtraction.

The general trap: a diagnostic that reads state the diagnosing mod has already
modified reports the mod's intent back at itself. If the original is kept anywhere,
print both.

## Unlock table

Vanilla gates feasts through Bog Witch spices (`Trader.TradeItem.m_requiredGlobalKey`),
not through `Recipe` fields. Woodland Herb Blend (`SpiceForests`) is the ingredient
for Meadows, Black Forest, **and** Swamp, so a spice-only shift would unlock those
three together. Black Forest and Swamp recipes are therefore also keyed so each
feast can follow "previous biome boss" on its own. Meadows has no previous boss,
so Woodland Herb Blend is ungated.

| Feast | Vanilla spice gate | This mod |
| --- | --- | --- |
| Whole Roasted Meadow Boar | Elder (`SpiceForests`) | no boss (woodland blend always in stock) |
| Black Forest Buffet Platter | Elder (shared woodland) | Eikthyr (`defeated_eikthyr`), recipe-gated |
| Swamp Dweller's Delight | Elder (shared woodland) | Elder (`defeated_gdking`), recipe-gated |
| Sailor's Bounty | Serpent (`SpiceOceans`) | unchanged |
| Hearty Mountain Logger's Stew | Moder (`SpiceMountains`) | Bonemass |
| Plains Pie Picnic | Yagluth (`SpicePlains`) | Moder |
| Mushrooms Galore à la Mistlands | Queen (`SpiceMistlands`) | Yagluth |
| Ashlands Gourmet Bowl | Fader (`SpiceAshlands`) | Queen |
| Deep North feast | Deep North boss (`SpiceDeepNorth`) | Fader |

Yagluth's world key is `defeated_goblinking`, not `defeated_goblin`. Queen / Fader
keys (`defeated_queen`, `defeated_fader`) are data-driven and do not appear as
string literals in `assembly_valheim.dll`.

The Deep North row is the reason the new boss's key does **not** need to be
correct for feasts to work: every feast moves to the *previous* boss, so the Deep
North feast gates on Fader and the new key is only the one being moved off.
`FeastUnlocks.FrozenKing` (`defeated_frozenking`) is carried anyway, unused by the
gate, purely so the dump can print it beside the world's real key list — it is
the same "data-driven, not in the binary" problem as Queen and Fader, one tier
later. `SpiceDeepNorth` and `FeastDeepNorth` are confirmed against the 1.0.7 dump;
`defeated_frozenking` is not, and cannot be until someone kills the boss — see
`valheim-1.0.md`.

Sailor's Bounty is omitted from both the spice remap and the recipe gate.

## Implementation notes

- Food numbers live on `ItemDrop.ItemData.SharedData` (`m_food` / `m_foodStamina` /
  `m_foodEitr`). `Player.EatFood` snapshots them, but `Player.UpdateFood` re-reads
  the shared values every tick, so mutating ObjectDB updates tooltips and already-eaten
  feasts. Originals are cached so a config change can add or restore cleanly.
- Spice remap runs as a prefix on `Trader.GetAvailableItems` and writes the new key
  onto the trader's `m_items` list. Vanilla's own key filter then uses the shifted
  values. Identify by spice prefab name, not by assuming the NPC is named BogWitch.
- Both feast tables carry singular spellings (`FeastSwamp`, `FeastOcean`,
  `FeastMountain`) beside the real plural prefabs, so a lookup resolves either way.
  They are listed in `FeastStats.AliasPrefabs` so the diagnostics coverage report
  labels them `alias` instead of `MISSING` — an expected-failure line in a report
  is how a real failure gets skimmed past.
- Recipe lock is a postfix on `Player.HaveRequirements(Recipe, …)`. Discover-mode
  calls hide the recipe from the food-prep list; craft-mode calls block cooking
  even if someone already has the spice (needed for Black Forest / Swamp).
