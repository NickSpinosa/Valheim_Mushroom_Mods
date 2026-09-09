# `Trader.TradeItem` in Valheim 1.0.7

Why `TraderPatch` fills in fields it never used to touch.

## What changed

1.0.7 grew `Trader.TradeItem` from four fields to eleven. The old four
(`m_prefab`, `m_stack`, `m_price`, `m_requiredGlobalKey`) still mean what they
meant; the new ones exist to support Hildir-style purchases that grant a *player
key* instead of an item — inventory rows, one-off unlocks — plus a purchase
flourish:

| Field | What it is for |
|---|---|
| `m_buyPlayerEffects` | EffectList played on the buyer when a purchase completes |
| `m_levelUpEffect` | Runs the skill-levelup flourish on the buyer |
| `m_icon`, `m_name`, `m_tooltip` | Display for rows that have no `m_prefab` to read them from |
| `m_buyKey` | Unique player key granted; also hides the row once owned |
| `m_incrementKey`, `m_incrementAmount` | Counter key a repeatable row bumps (inventory rows use `invrows`) |

## Why that broke us

Vanilla rows are authored on a prefab, so Unity's serializer hands every one of
them a live `EffectList` and empty strings — never null. A row built in C# with
an object initializer gets `null` for both, and `StoreGui` dereferences several
of them with no guard. Both of these are in the shipped 1.0.7 code:

- `BuySelectedItem` calls `m_selectedItem.m_buyPlayerEffects.Create(...)` and
  then logs `m_buyPlayerEffects.m_effectPrefabs.Length`. This is the one the
  ticket described: it throws *after* the coins are taken and the item granted,
  and the `FillList()` that would refresh the shop is on the far side of the
  throw.
- `FillList` itself reads `tradeItem.m_tooltip.Length > 0` before falling back to
  the prefab tooltip. That throws while the list is being *rendered*, not
  bought — so a null `m_tooltip` breaks the store the moment one of our rows is
  in it, which is worse than the ticket assumed. Only the fields StoreGui reads
  through a `bool` cast (`m_icon`, `m_prefab`) and through
  `string.IsNullOrEmpty` (`m_buyKey`, `m_incrementKey`) are null-tolerant.

The rule to carry forward: **anything on a Valheim serialized class is
non-null in the editor and null in our constructor.** Filling every field is
cheaper than auditing which call sites happen to guard.

## Why the values we chose

- **`m_buyPlayerEffects` copied by reference from a vanilla row on the same
  trader**, not `new EffectList()`. An empty list is legal — `Create` loops over
  a zero-length array — but then modded rows are the only ones in the shop that
  buy in silence, and that reads as a bug to a player. `BuyEffectsFrom` picks
  the first ordinary row (prefab-backed, no `m_buyKey`) that actually has
  effects, so we skip the player-key rows whose flourish is deliberately
  different. Empty list only if the trader has no such row at all. Sharing the
  reference is safe because `Create` only reads it, and it keeps us tracking the
  row if a game update re-authors those effects.
- **`m_levelUpEffect = false`, deliberately not copied.** It is on the buy path
  (`Player.OnSkillLevelup`), so it is tempting to treat it as part of "match the
  vanilla row". It is not a purchase sound — it is the ceremony for buying an
  inventory upgrade. Fifty wood should not level-up-flash.
- **`m_buyKey = ""`.** A non-empty buy key makes
  `Trader.GetAvailableItems` drop the row once the player owns the key: our
  stock is repeatable, so it has to stay empty. Same for `m_incrementKey`.
- **`m_name = entry.PrefabName`.** With `m_prefab` set, `FillList` reads the
  display name off the item and never looks at `m_name` — but the purchase
  ZLog line does, and a nameless "Player bought item 0: " in a log is a wasted
  breadcrumb.
- **`m_tooltip = ""`** so `FillList` takes the prefab-tooltip branch, which is
  the tooltip we actually want.
- **`m_icon` left null.** `FillList` null-checks it and falls back to the
  prefab's icon, which is correct for every row we add.

## What this does not change

The `Trade table hash` line. That hash fingerprints `TradeTable.Haldor` plus
live config — prefab name, enabled, stack, price, boss gate. It has never
included anything from `Trader.TradeItem`, and MushroomSync syncs config
entries, not trade rows, so nothing here is on the wire either. A log from
before this fix and one from after must show the same hash; if they differ,
something else moved.

## Adjacent, not fixed here

The Super Mist Torch row still depends on `piece_groundtorch_mist` resolving to
an `ItemDrop` (issue #24). When it does not, `PrefabCache.Resolve` returns null
and `TraderPatch` skips the row before any of the above matters.
