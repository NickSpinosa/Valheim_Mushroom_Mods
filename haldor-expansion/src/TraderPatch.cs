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
        private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__instance == null || __result == null) return;

            Diagnostics.DumpOnce(__instance, __result);

            SuperMistTorch.RefreshFromConfig();

            if (PrefabName(__instance.gameObject) != TradeTable.HaldorPrefab) return;

            foreach (var entry in TradeTable.Haldor)
            {
                if (Plugin.Settings != null && !Plugin.Settings.IsEnabled(entry)) continue;
                if (!IsUnlocked(entry)) continue;

                var itemDrop = PrefabCache.Resolve(entry.PrefabName);
                if (itemDrop == null) continue; // Resolve already logged the failure.

                var price = Plugin.Settings != null
                    ? Plugin.Settings.GetPurchasePrice(entry)
                    : entry.Price;

                __result.Add(new Trader.TradeItem
                {
                    m_prefab = itemDrop,
                    m_stack = entry.Stack,
                    m_price = price,
                    m_requiredGlobalKey = "",
                });
            }
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
