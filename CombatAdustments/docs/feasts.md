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
| All except the three rows below | vanilla +10 | vanilla +10 | vanilla |
| Sailor's Bounty | vanilla +15 | vanilla +15 | vanilla |
| Mushrooms Galore à la Mistlands | vanilla +10 | vanilla +10 | vanilla 33 +7 = **40** |
| Ashlands Gourmet Bowl | vanilla +10 | vanilla +10 | vanilla 38 +12 = **50** |

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

Yagluth's world key is `defeated_goblinking`, not `defeated_goblin`. Queen / Fader
keys (`defeated_queen`, `defeated_fader`) are data-driven and do not appear as
string literals in `assembly_valheim.dll`.

Sailor's Bounty is omitted from both the spice remap and the recipe gate.

## Implementation notes

- Food numbers live on `ItemDrop.ItemData.SharedData` (`m_food` / `m_foodStamina` /
  `m_foodEitr`). `Player.EatFood` snapshots them, but `Player.UpdateFood` re-reads
  the shared values every tick, so mutating ObjectDB updates tooltips and already-eaten
  feasts. Originals are cached so a config change can add or restore cleanly.
- Spice remap runs as a prefix on `Trader.GetAvailableItems` and writes the new key
  onto the trader's `m_items` list. Vanilla's own key filter then uses the shifted
  values. Identify by spice prefab name, not by assuming the NPC is named BogWitch.
- Recipe lock is a postfix on `Player.HaveRequirements(Recipe, …)`. Discover-mode
  calls hide the recipe from the food-prep list; craft-mode calls block cooking
  even if someone already has the spice (needed for Black Forest / Swamp).
