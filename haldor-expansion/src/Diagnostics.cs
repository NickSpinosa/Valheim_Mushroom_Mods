using System;
using System.Collections.Generic;
using System.Text;

namespace HaldorExpansion
{
    /// <summary>
    /// One-shot dump of everything the design needs verified against the live game rather
    /// than against memory:
    ///
    ///   1. The real global key list -- the Ashlands boss key is not a string literal in
    ///      assembly_valheim.dll, so its spelling cannot be read out of the binary.
    ///   2. Vanilla Haldor's actual prices, to re-anchor the provisional table.
    ///   3. Candidate prefab spellings for the five items, with their max stack sizes.
    ///   4. Whether our configured stack sizes exceed the item's max stack, which would
    ///      silently deliver less than the player paid for.
    ///
    /// Fires on the first GetAvailableItems call, by which point ObjectDB and ZoneSystem
    /// are both guaranteed live.
    /// </summary>
    internal static class Diagnostics
    {
        private static bool _done;

        private static readonly string[] SearchTerms =
        {
            "stone", "wood", "surtling", "core", "graus", "ash",
            "mist", "torch", "wisp", "demister",
        };

        public static void DumpOnce(Trader trader, List<Trader.TradeItem> available)
        {
            if (_done) return;
            _done = true;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("===== HALDOR EXPANSION :: VERIFICATION DUMP =====");
            sb.AppendLine("Table hash: " + TradeTable.Hash);

            DumpGlobalKeys(sb);
            DumpVanillaStock(sb, trader, available);
            DumpTargets(sb);
            DumpCandidates(sb);

            sb.AppendLine("===== END VERIFICATION DUMP =====");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpGlobalKeys(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- GLOBAL KEYS CURRENTLY SET ---");
            var zs = ZoneSystem.instance;
            if (zs == null)
            {
                sb.AppendLine("  ZoneSystem not available.");
                return;
            }

            var any = false;
            foreach (var key in zs.GetGlobalKeys())
            {
                sb.AppendLine("  " + key);
                any = true;
            }
            if (!any) sb.AppendLine("  (none set on this world)");

            sb.AppendLine();
            sb.AppendLine("  --- boss keys we gate on ---");
            foreach (UnlockBoss boss in Enum.GetValues(typeof(UnlockBoss)))
            {
                if (boss == UnlockBoss.None) continue;
                var key = UnlockBossKeys.Get(boss);
                var set = zs.GetGlobalKey(key);
                sb.AppendLine("  " + boss.ToString().PadRight(10)
                              + " '" + key + "' set: " + set);
            }
        }

        private static void DumpVanillaStock(StringBuilder sb, Trader trader,
                                             List<Trader.TradeItem> available)
        {
            sb.AppendLine();
            sb.AppendLine("--- VANILLA TRADER STOCK (price anchors) ---");
            sb.AppendLine("  Trader GameObject: " + (trader != null ? trader.gameObject.name : "null"));

            if (trader == null || trader.m_items == null)
            {
                sb.AppendLine("  No configured item list.");
                return;
            }

            foreach (var item in trader.m_items)
            {
                if (item == null || item.m_prefab == null) continue;
                var key = string.IsNullOrEmpty(item.m_requiredGlobalKey) ? "-" : item.m_requiredGlobalKey;
                sb.AppendLine("  " + item.m_prefab.name.PadRight(24)
                              + " stack=" + item.m_stack.ToString().PadRight(5)
                              + " price=" + item.m_price.ToString().PadRight(7)
                              + " key=" + key);
            }
            sb.AppendLine("  (currently offered to this player: "
                          + (available != null ? available.Count : 0) + " rows)");
        }

        private static void DumpTargets(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- OUR TRADE TARGETS ---");
            foreach (var entry in TradeTable.Haldor)
            {
                var itemDrop = PrefabCache.Resolve(entry.PrefabName);
                if (itemDrop == null)
                {
                    sb.AppendLine("  " + entry.PrefabName.PadRight(20) + " UNRESOLVED -- wrong spelling?");
                    continue;
                }

                var maxStack = itemDrop.m_itemData.m_shared.m_maxStackSize;
                var overflow = entry.Stack > maxStack
                    ? "  <-- CONFIGURED STACK " + entry.Stack + " EXCEEDS MAX STACK " + maxStack
                    : "";

                var settings = Plugin.Settings;
                var enabled = settings == null || settings.IsEnabled(entry);
                var perUnit = settings == null ? entry.PricePerUnit : settings.GetCostPerUnit(entry);
                var price = settings == null ? entry.Price : settings.GetPurchasePrice(entry);
                var boss = settings == null ? entry.DefaultUnlockBoss : settings.GetUnlockBoss(entry);
                var key = UnlockBossKeys.Get(boss) ?? "-";

                sb.AppendLine("  " + entry.PrefabName.PadRight(20)
                              + (enabled ? " on " : " OFF")
                              + " boss=" + boss.ToString().PadRight(10)
                              + " key=" + key.PadRight(20)
                              + " maxStack=" + maxStack.ToString().PadRight(5)
                              + " ourStack=" + entry.Stack.ToString().PadRight(5)
                              + " price=" + price.ToString().PadRight(6)
                              + " perUnit=" + perUnit
                              + overflow);
            }
            sb.AppendLine("  NOTE: buy one of each in-game and confirm the delivered amount "
                          + "matches ourStack. If m_stack clamps to maxStack, a purchase "
                          + "silently shortchanges the buyer.");
        }

        private static void DumpCandidates(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- ITEM CANDIDATES (ObjectDB names matching our search terms) ---");
            var odb = ObjectDB.instance;
            if (odb == null || odb.m_items == null)
            {
                sb.AppendLine("  ObjectDB not available.");
                return;
            }

            foreach (var go in odb.m_items)
            {
                if (go == null) continue;
                var lower = go.name.ToLowerInvariant();
                foreach (var term in SearchTerms)
                {
                    if (!lower.Contains(term)) continue;

                    var drop = go.GetComponent<ItemDrop>();
                    var maxStack = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                        ? drop.m_itemData.m_shared.m_maxStackSize.ToString()
                        : "?";
                    sb.AppendLine("  " + go.name.PadRight(28) + " maxStack=" + maxStack);
                    break;
                }
            }
        }
    }
}
