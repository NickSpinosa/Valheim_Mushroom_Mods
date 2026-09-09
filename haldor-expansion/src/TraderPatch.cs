using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HaldorExpansion
{
    /// <summary>
    /// Adds the configured table to Haldor's stock.
    ///
    /// Runs as a postfix on GetAvailableItems, which builds a fresh list on every call,
    /// so appending each time is correct and needs no de-duplication. Enabled/Cost/
    /// UnlockBoss are read live (including a server sync), so an in-session config
    /// change shows up the next time the shop is queried.
    ///
    /// Note that vanilla's own global-key filtering has already run by the time we get here,
    /// so gated rows must be filtered by us -- see IsUnlocked.
    /// </summary>
    [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
    internal static class TraderGetAvailableItemsPatch
    {
        /// <summary>
        /// Shared fallback for <see cref="Trader.TradeItem.m_buyPlayerEffects"/> when the trader
        /// has no vanilla row to copy from. One instance for every row we ever add: EffectList.Create
        /// only reads m_effectPrefabs, so sharing is safe, and a default-constructed EffectList
        /// already has an empty (never null) array.
        /// </summary>
        private static readonly EffectList NoBuyEffects = new EffectList();

        private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__instance == null || __result == null) return;

            Diagnostics.DumpOnce(__instance, __result);

            SuperMistTorch.RefreshFromConfig();

            if (PrefabName(__instance.gameObject) != TradeTable.HaldorPrefab) return;

            var buyEffects = BuyEffectsFrom(__instance);

            foreach (var entry in TradeTable.Haldor)
            {
                if (Plugin.Settings != null && !Plugin.Settings.IsEnabled(entry)) continue;
                if (!IsUnlocked(entry)) continue;

                var itemDrop = PrefabCache.Resolve(entry.PrefabName);
                if (itemDrop == null) continue; // Resolve already logged the failure.

                var price = Plugin.Settings != null
                    ? Plugin.Settings.GetPurchasePrice(entry)
                    : entry.Price;

                // Every field StoreGui touches is filled in, including the ones 1.0.7 added.
                // Unity's serializer hands vanilla rows non-null objects and empty strings;
                // a row built in C# gets null for both, and StoreGui dereferences several of
                // them unguarded. See docs/trade-item-1.0.7.md for which call sites and why.
                __result.Add(new Trader.TradeItem
                {
                    m_prefab = itemDrop,
                    m_stack = entry.Stack,
                    m_price = price,
                    m_requiredGlobalKey = "",

                    m_buyPlayerEffects = buyEffects,
                    m_levelUpEffect = false,
                    m_name = entry.PrefabName,
                    m_tooltip = "",
                    m_buyKey = "",
                    m_incrementKey = "",
                });
            }
        }

        /// <summary>
        /// The purchase effect our rows play, borrowed from a vanilla row on the same trader so a
        /// bought stack of wood feels like a bought Megingjord. Picks the first ordinary item row --
        /// prefab-backed and not a one-off player-key purchase -- that actually has effects, and
        /// falls back to an empty list if the trader has none.
        ///
        /// The vanilla EffectList is shared by reference rather than copied: Create only reads it,
        /// and a copy would silently stop tracking a row the game later re-authors.
        ///
        /// Recomputed per call rather than cached. GetAvailableItems runs a few times a frame while
        /// the store is open, but a trader's m_items is a couple of dozen rows, and a cache keyed on
        /// the trader would have to be invalidated when another mod rewrites the stock -- Combat
        /// Adjustments already prefixes this same method.
        /// </summary>
        private static EffectList BuyEffectsFrom(Trader trader)
        {
            var items = trader.m_items;
            if (items == null) return NoBuyEffects;

            foreach (var item in items)
            {
                if (item == null || item.m_prefab == null) continue;
                if (!string.IsNullOrEmpty(item.m_buyKey)) continue;
                if (item.m_buyPlayerEffects == null || !item.m_buyPlayerEffects.HasEffects()) continue;
                return item.m_buyPlayerEffects;
            }

            return NoBuyEffects;
        }

        private static bool IsUnlocked(TradeEntry entry)
        {
            var key = Plugin.Settings != null
                ? Plugin.Settings.GetRequiredGlobalKey(entry)
                : UnlockBossKeys.Get(entry.DefaultUnlockBoss);

            if (string.IsNullOrEmpty(key)) return true;
            var zs = ZoneSystem.instance;
            if (zs == null) return false; // Fail closed rather than handing out gated stock.
            return zs.GetGlobalKey(key);
        }

        /// <summary>
        /// Strips the "(Clone)" suffix Unity appends to instantiated prefabs. Hand-rolled to
        /// avoid depending on Valheim's Utils class, whose name collides easily.
        /// </summary>
        private static string PrefabName(GameObject go)
        {
            if (go == null) return string.Empty;
            var name = go.name;
            var idx = name.IndexOf("(Clone)");
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
