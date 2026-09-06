# Haldor Expansion — Design

A Valheim mod that adds gathering materials — and one custom placeable — to
Haldor's trade stock.

## Purpose

Mitigate resource scarcity on a long-lived, heavily-populated dedicated server:
local depletion around bases, and true exhaustion of non-renewable surtling cores.
The Super Mist Torch is a late-game convenience after the Queen: a wide-area demist
without farming wisps for a carpet of vanilla Wisp Torches.

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
| Super Mist Torch (custom; see below) | Queen | 100 | infinite |

Stack size per purchase is a **per-item field** on the table, not a global constant.
The Super Mist Torch stack is 1 — it is a placeable, not a bulk material.

Prices above are anchored to recalled vanilla values (Megingjord ~950) and must be
re-anchored against Haldor's real price list before baking. The *ratios* are the
intent; the absolutes are provisional.

Stone and wood are priced as a **sustainable** faucet, not a one-time drawdown — they
are the everyday anti-tedium items and must outlive the server's legacy coin pile.

Known and accepted consequence: with stone at 1 coin, mining stone becomes optional for
anyone with a coin balance. On a server whose problem is that nearby stone is already
mined out, that is the point.

## Super Mist Torch

Cloned at runtime from the vanilla Wisp Torch (`piece_groundtorch_mist`). No AssetBundle,
no Jötunn.

| Property | Value |
|---|---|
| Prefab name | `SuperMistTorch` |
| Visual scale | 2× the vanilla piece |
| Demist radius | 100 m |
| Default cost | 100 coins |
| Default gate | Queen (`defeated_queen`) |

**How placement works.** The same prefab is the inventory item Haldor sells *and* the
hammer piece. Its `Piece.m_resources` costs one of itself, so buying from Haldor is
what stocks the material the hammer consumes. Hammer-remove refunds the item
(`m_recover = true`). The piece is added to the Hammer Misc table and taught to every
player on spawn so a purchase is immediately placeable.

**Demist radius vs scale.** `Demister` clears mist through a `ParticleSystemForceField`
on the same hierarchy; `endRange` is in the force field's *local* space. Scaling the
root by 2× would otherwise double the world-space reach, so the prefab stores
`endRange = 100 / 2` and the world radius stays 100 m. Start range keeps the vanilla
ratio so the falloff shape still looks right.

**Registration order.** The source piece lives in `ZNetScene`, not `ObjectDB`. Build
on `ZNetScene.Awake` (and again from `Game.Start` as a backstop); also re-add to
`ObjectDB` on `Awake` / `CopyOtherDB` because `CopyOtherDB` replaces `m_items`
wholesale — never latch a "registered" boolean. Same lifecycle lessons as
HornOfCalling / vegvisir-compass; see those mods' docs for the full failure modes.

## Technical decisions

- **Bare BepInEx 5 + HarmonyX.** Gathering rows are vanilla prefabs already in
  `ObjectDB`. The Super Mist Torch is a hand-registered clone — Jötunn would only
  shrink that registration, at the cost of a hard dependency every player would need.
- **Harmony postfix on `Trader.GetAvailableItems`.** The ZNet hooks for config sync
  are no longer patched here — MushroomSync owns them for every mod that syncs.
  Confirmed present in the current assembly. No installed plugin patches the trader
  method; ValheimPlus references `StoreGui` only (UI-level), so conflict risk is low.
- **Publicizer only for the custom piece.** Gathering rows touch only public members.
  Super Mist Torch needs `ObjectDB.UpdateRegisters` and `ZNetScene.m_namedPrefabs`
  (private), via `BepInEx.AssemblyPublicizer.MSBuild` at build time — no runtime
  dependency. Same package HornOfCalling / vegvisir-compass already use.
- **Prefab IDs resolved at runtime** from `ObjectDB.instance`, logging loudly on a miss.
  Super Mist Torch is registered into that DB before the trader is usable.
- **BepInEx config per added item** (`Enabled`, `Cost` in coins per unit, and
  `UnlockBoss`). Defaults: wood/stone = Elder, grausten/blackwood/Super Mist Torch =
  Queen, surtling core = Bonemass. Stack size stays in C# — that is a design invariant,
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
- Plugin GUID `nicks.haldorexpansion`, display name "Haldor Expansion", v0.4.0.
- Post-build copy into local `BepInEx\plugins`, with the path in a gitignored local
  props file.

## Deployment

Shared r2modman profile. The dedicated server gets the plugin too — it is inert there,
but "every machine runs the identical profile" is an enforceable rule and
"everything except the server" is how drift starts. Clients *do* need the Super Mist
Torch prefab registered locally (placement and demist are client-visible), so the
plugin is not server-only for that row.

## Verification pass — do this before baking any values

1. Real spelling of the Ashlands global key via the `listkeys` console command.
   `defeated_queen` and `defeated_fader` are **not** string literals in the assembly;
   the newer boss keys are data-driven in the asset bundles. Do not assume the spelling.
2. Exact prefab IDs for all five gathering items from `ObjectDB.instance`.
3. Vanilla Haldor's actual price list, to re-anchor the table above.
4. Whether `TradeItem.m_stack` clamps to an item's max stack size or overflows into
   multiple stacks. If it clamps, a large value silently delivers less than was paid for.
5. Super Mist Torch: buy one after Queen, place with the hammer, confirm demist reach
   feels like ~100 m and the model is visibly larger than a vanilla Wisp Torch.
   Deconstruct with the hammer and confirm the item is refunded.

## Environment (as of 2026-08-31)

- Valheim at `E:\Games\Steam\steamapps\common\Valheim`, updated 2026-02-19, Unity 6000.0.61
- BepInEx 5.4.23.5, 12 plugins including ValheimPlus, ServerDevcommands, ServerSync
- .NET SDK 9.0.301, VS 2022
