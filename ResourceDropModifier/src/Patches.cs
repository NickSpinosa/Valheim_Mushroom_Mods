using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ResourceDropModifier
{
    /// <summary>
    /// Where the multipliers are applied. Two seams cover every drop in the game -
    /// see "Where drops are decided" in docs/DESIGN.md for the table this was
    /// derived from.
    /// </summary>
    internal static class DropScaling
    {
        /// <summary>Most objects one creature death or one rock may produce, before and after scaling.</summary>
        internal const int MaxObjectsPerDrop = 100;

        /// <summary>Most entries a rebuilt <c>GetDropList</c> result may hold.</summary>
        internal const int MaxListEntries = 200;

        /// <summary>
        /// Applies the multiplier for an item's data to a count that vanilla has
        /// already scaled by the world resource rate. Stacked items clamp to their
        /// max stack, as vanilla does; non-stackable counts are separate objects and
        /// clamp to <see cref="MaxObjectsPerDrop"/>.
        /// </summary>
        internal static int Apply(ItemDrop.ItemData data, int vanilla)
        {
            string name = ItemIndex.NameOf(data);
            float multiplier;
            if (name == null || !Plugin.Table.TryGet(name, out multiplier) || multiplier == 1f)
                return vanilla;

            int scaled = MultiplierTable.Scale(vanilla, multiplier);

            int cap = data.m_shared != null && data.m_shared.m_maxStackSize > 1
                ? data.m_shared.m_maxStackSize
                : MaxObjectsPerDrop;
            if (scaled > cap)
                scaled = cap;

            Plugin.Debug("ScaleDrops " + name + ": " + vanilla + " x " + multiplier + " = " + scaled);
            return scaled;
        }
    }

    /// <summary>
    /// <c>Game.ScaleDrops</c> is vanilla's resource-rate hook, and every drop path
    /// except <c>DropTable.GetDropList()</c> reaches one of its overloads. The
    /// <c>ItemData</c> overloads carry the item, so they get the postfix. The
    /// <c>GameObject</c> overloads short-circuit when the world rate is 1 and never
    /// reach the <c>ItemData</c> overload, so they get a prefix that routes through it
    /// regardless of the rate: without that, creature and pickable drops would be
    /// skipped on a rate-1 world and double-applied on any other.
    /// </summary>
    [HarmonyPatch(typeof(Game))]
    internal static class Game_ScaleDrops_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Game.ScaleDrops), typeof(GameObject), typeof(int))]
        internal static bool RouteSingle(Game __instance, GameObject drop, int amount, ref int __result)
        {
            if (Game.m_resourceRate != 1f || drop == null)
                return true;

            ItemDrop item = drop.GetComponent<ItemDrop>();
            if (item == null)
                return true;

            __result = __instance.ScaleDrops(item.m_itemData, amount);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Game.ScaleDrops), typeof(GameObject), typeof(int), typeof(int))]
        internal static bool RouteRange(Game __instance, GameObject drop, int randomMin, int randomMax, ref int __result)
        {
            if (Game.m_resourceRate != 1f || drop == null)
                return true;

            ItemDrop item = drop.GetComponent<ItemDrop>();
            if (item == null)
                return true;

            __result = __instance.ScaleDrops(item.m_itemData, randomMin, randomMax);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Game.ScaleDrops), typeof(ItemDrop.ItemData), typeof(int))]
        internal static void ScaleSingle(ItemDrop.ItemData data, ref int __result)
        {
            __result = DropScaling.Apply(data, __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Game.ScaleDrops), typeof(ItemDrop.ItemData), typeof(int), typeof(int))]
        internal static void ScaleRange(ItemDrop.ItemData data, ref int __result)
        {
            __result = DropScaling.Apply(data, __result);
        }
    }

    /// <summary>
    /// Trees, logs, rocks, destructibles and loot piles take a <c>List&lt;GameObject&gt;</c>
    /// with one entry per unit and instantiate each as a stack of one, and none of
    /// them go through <c>ScaleDrops</c>. Multiplying is rebuilding that list.
    /// Entries flagged <c>m_dontScale</c> on the table are copied through unchanged,
    /// matching what the <c>ScaleDrops</c> paths do for free.
    /// </summary>
    [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
    internal static class DropTable_GetDropList_Patch
    {
        [HarmonyPostfix]
        internal static void Rescale(DropTable __instance, ref List<GameObject> __result)
        {
            if (__result == null || __result.Count == 0 || Plugin.Table.Count == 0)
                return;

            // Count per prefab, first-seen order preserved so the drop order is stable.
            var order = new List<GameObject>();
            var counts = new Dictionary<GameObject, int>();
            foreach (GameObject prefab in __result)
            {
                if (prefab == null)
                    continue;

                int n;
                if (!counts.TryGetValue(prefab, out n))
                    order.Add(prefab);
                counts[prefab] = n + 1;
            }

            bool changed = false;
            var rebuilt = new List<GameObject>(__result.Count);
            foreach (GameObject prefab in order)
            {
                int vanilla = counts[prefab];
                int count = vanilla;

                float multiplier;
                if (!IsExempt(__instance, prefab) && Plugin.Table.TryGet(prefab.name, out multiplier) && multiplier != 1f)
                {
                    count = MultiplierTable.Scale(vanilla, multiplier);
                    if (count > DropScaling.MaxObjectsPerDrop)
                        count = DropScaling.MaxObjectsPerDrop;
                    changed = true;
                    Plugin.Debug("GetDropList " + prefab.name + ": " + vanilla + " x " + multiplier + " = " + count);
                }

                for (int i = 0; i < count && rebuilt.Count < DropScaling.MaxListEntries; i++)
                    rebuilt.Add(prefab);
            }

            if (changed)
                __result = rebuilt;
        }

        private static bool IsExempt(DropTable table, GameObject prefab)
        {
            foreach (DropTable.DropData drop in table.m_drops)
            {
                if (drop.m_item == prefab)
                    return drop.m_dontScale;
            }

            return false;
        }
    }

    /// <summary>
    /// <c>GetDropListItems</c> stacks reach the <c>ItemData</c> postfix through
    /// <c>AddItemToList</c>, so a multiplier of 0 leaves a stack of 0 in the list.
    /// Chests would then hold an item of nothing and pickables would drop one, so
    /// empty stacks are removed here.
    /// </summary>
    [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropListItems))]
    internal static class DropTable_GetDropListItems_Patch
    {
        [HarmonyPostfix]
        internal static void DropEmptyStacks(List<ItemDrop.ItemData> __result)
        {
            if (__result != null && __result.Count > 0)
                __result.RemoveAll(item => item == null || item.m_stack <= 0);
        }
    }

    /// <summary>
    /// A pickable's main amount is <c>Max(m_minAmountScaled, ScaleDrops(...))</c>, a
    /// floor vanilla keeps so a low world rate never picks nothing. With a multiplier
    /// below 1 the admin has asked for exactly that, so the floor is lifted for the
    /// duration of the pick and restored afterwards. Left alone when the multiplier
    /// is 1 or more, so vanilla behaviour is untouched unless a value was changed.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
    internal static class Pickable_RPC_Pick_Patch
    {
        [HarmonyPrefix]
        internal static void LiftFloor(Pickable __instance, out int __state)
        {
            __state = __instance.m_minAmountScaled;

            float multiplier;
            if (!__instance.m_dontScale
                && __instance.m_itemPrefab != null
                && Plugin.Table.TryGet(__instance.m_itemPrefab.name, out multiplier)
                && multiplier < 1f)
            {
                __instance.m_minAmountScaled = 0;
            }
        }

        // A finalizer rather than a postfix so the floor comes back even when the
        // original throws.
        [HarmonyFinalizer]
        internal static Exception RestoreFloor(Pickable __instance, int __state, Exception __exception)
        {
            __instance.m_minAmountScaled = __state;
            return __exception;
        }
    }

    /// <summary>
    /// After <c>ObjectDB.CopyOtherDB</c> has replaced the main-menu database and
    /// <c>SetupLocations</c> has resolved the location prefabs: the earliest point the
    /// catalog walk sees everything. <c>ZoneSystem</c> is rebuilt on every world join,
    /// so this runs per world, which is what refreshes the table for the new world.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), "Start")]
    internal static class ZoneSystem_Start_Patch
    {
        [HarmonyPostfix]
        internal static void BuildCatalog()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null)
                plugin.OnWorldReady();
        }
    }

    /// <summary>Registers the console commands once the terminal exists.</summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Terminal_InitTerminal_Patch
    {
        [HarmonyPostfix]
        internal static void Register()
        {
            ConsoleCommands.Register();
        }
    }
}
