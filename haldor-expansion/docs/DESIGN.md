# Haldor Expansion — Design

A Valheim mod that adds five gathering materials to Haldor's trade stock.

## Purpose

Mitigate resource scarcity on a long-lived, heavily-populated dedicated server:
local depletion around bases, and true exhaustion of non-renewable surtling cores.

**Explicit non-goal:** this does *not* solve latecomer lockout. Every gated item is
priced in coins, and a new player has no coins and cannot farm Fulings. Solving that
would need a different mechanism (starter grant / cheap ungated tier) and is out of scope.

## Audience

Private. Nick + friends, one dedicated server. Never published.

## Item table

| Item | Gate | Coins/unit | Stock |
|---|---|---|---|
| Wood | Elder | 1 | infinite |
| Stone | Elder | 1 | infinite |
| Grausten | Queen | 2 | infinite |
| Blackwood (the Ashlands wood; there is no "Ashwood" in ObjectDB) | Queen | 2 | infinite |
| Surtling core | Bonemass | 100 | infinite |

Stack size per purchase is a **per-item field** on the table, not a global constant.

Prices above are anchored to recalled vanilla values (Megingjord ~950) and must be
re-anchored against Haldor's real price list before baking. The *ratios* are the
intent; the absolutes are provisional.

Stone and wood are priced as a **sustainable** faucet, not a one-time drawdown — they
are the everyday anti-tedium items and must outlive the server's legacy coin pile.

Known and accepted consequence: with stone at 1 coin, mining stone becomes optional for
anyone with a coin balance. On a server whose problem is that nearby stone is already
mined out, that is the point.

## Technical decisions

- **Bare BepInEx 5 + HarmonyX.** No Jötunn — all five items are vanilla prefabs already
  in `ObjectDB`, so Jötunn's custom-asset tooling would be an unused hard dependency.
- **Harmony postfix on `Trader.GetAvailableItems`.** The ZNet hooks for config sync
  are no longer patched here — MushroomSync owns them for every mod that syncs.
  Confirmed present in the current assembly. No installed plugin patches the trader
  method; ValheimPlus references `StoreGui` only (UI-level), so conflict risk is low.
- **No publicizer needed.** The design called for `BepInEx.AssemblyPublicizer.MSBuild`,
  but the first successful build proved every member we touch is already public:
  `Trader.m_items`, the nested `Trader.TradeItem` and its fields, `ObjectDB.instance` /
  `GetItemPrefab` / `m_items`, `ZoneSystem.GetGlobalKey` / `GetGlobalKeys`, and
  `ItemDrop.m_itemData.m_shared.m_maxStackSize`. Dependency dropped. Re-add only if a
  future change needs a genuinely private member.
- **Prefab IDs resolved at runtime** from `ObjectDB.instance`, logging loudly on a miss.
- **BepInEx config per added item** (`Enabled`, `Cost` in coins per unit, and
  `UnlockBoss`). Defaults: wood/stone = Elder, grausten/blackwood = Queen,
  surtling core = Bonemass. Stack size stays in C# — that is a design invariant,
  not a knob. `UnlockBoss` accepts `None` plus every vanilla boss so the gate can
  be moved without a rebuild.
- **Server-authoritative config sync**, now provided by the shared
  [MushroomSync](../../MushroomSync/README.md) plugin rather than owned here. This
  mod used to carry its own copy — the same ~300 lines Craftable Spawners, Combat
  Adjustments and Random Yggdrasil each also carried. It registers its settings and
  supplies a gate; MushroomSync owns the ZNet hooks and the handshake. See
  [MushroomSync/docs/DESIGN.md](../../MushroomSync/docs/DESIGN.md) for how the
  transport works and why it does not wrap login sockets the way ServerSync did.

  What stays true here: clients apply host values at runtime and never overwrite
  their local `.cfg`. `Server.LockConfiguration` (default on) remains a **host-side
  switch** — it decides whether this machine publishes its settings when it is the
  server, and does not stop this machine following a host when it is a client.
  `GetAvailableItems` is still client-side, so sync buys *consistency*, not
  *enforcement* — enough on a private server. Enabled, Cost, and UnlockBoss are all
  registered into that payload.

  `UnlockBoss` is an enum, and enums are the case the old copies got wrong: passed
  straight to `TomlTypeConverter` they convert to `null` and the setting is silently
  skipped. MushroomSync parses enums before falling back to the converter.
- **Table authored as C# source**, not embedded JSON — a mistyped prefab ID fails at
  build rather than silently dropping an item from Haldor's stock. Config overlays
  Enabled / Cost / UnlockBoss on those rows.
- **Table hash logged at startup and after a client sync.** It fingerprints the
  effective table (rows + live config). Comparing that line across logs is the fast
  check that everyone is looking at the same stock and prices.
- **Trader-keyed table.** Haldor only for now, but Hildir and the Bog Witch share the
  `Trader` component, so the structure supports adding them without a rewrite.
- Plugin GUID `nicks.haldorexpansion`, display name "Haldor Expansion", v0.3.0.
- Post-build copy into local `BepInEx\plugins`, with the path in a gitignored local
  props file.

## Deployment

Shared r2modman profile. The dedicated server gets the plugin too — it is inert there,
but "every machine runs the identical profile" is an enforceable rule and
"everything except the server" is how drift starts.

## Verification pass — do this before baking any values

1. Real spelling of the Ashlands global key via the `listkeys` console command.
   `defeated_queen` and `defeated_fader` are **not** string literals in the assembly;
   the newer boss keys are data-driven in the asset bundles. Do not assume the spelling.
2. Exact prefab IDs for all five items from `ObjectDB.instance`.
3. Vanilla Haldor's actual price list, to re-anchor the table above.
4. Whether `TradeItem.m_stack` clamps to an item's max stack size or overflows into
   multiple stacks. If it clamps, a large value silently delivers less than was paid for.

## Environment (as of 2026-08-31)

- Valheim at `E:\Games\Steam\steamapps\common\Valheim`, updated 2026-02-19, Unity 6000.0.61
- BepInEx 5.4.23.5, 12 plugins including ValheimPlus, ServerDevcommands, ServerSync
- .NET SDK 9.0.301, VS 2022
