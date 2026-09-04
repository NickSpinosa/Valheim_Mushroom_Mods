using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class ObjectDB_Awake_Patch
{
    private static void Postfix(ObjectDB __instance)
    {
        ShieldStats.ApplyToObjectDB(__instance);
        WeaponBlockStats.ApplyToObjectDB(__instance);
        FeastStats.ApplyToObjectDB(__instance);
    }
}

[HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
internal static class ObjectDB_CopyOtherDB_Patch
{
    private static void Postfix(ObjectDB __instance)
    {
        ShieldStats.ApplyToObjectDB(__instance);
        WeaponBlockStats.ApplyToObjectDB(__instance);
        FeastStats.ApplyToObjectDB(__instance);
    }
}

/// <summary>
/// Adds equipped-shield flat stagger capacity. Vanilla: maxHP * m_staggerDamageFactor.
/// </summary>
[HarmonyPatch(typeof(Character), "GetStaggerTreshold")]
internal static class Character_GetStaggerTreshold_Patch
{
    private static void Postfix(Character __instance, ref float __result)
    {
        if (!ShieldReworkPlugin.EnableStaggerGrant.Value)
            return;
        if (__instance is not Player player)
            return;

        __result += ShieldStats.GrantForEquippedShield(player);
    }
}

/// <summary>
/// Vanilla UpdateStagger drains maxHP*factor/5 and ignores GetStaggerTreshold.
/// Replace for players so the shield grant also speeds absolute drain (threshold/5).
/// </summary>
[HarmonyPatch(typeof(Character), "UpdateStagger")]
internal static class Character_UpdateStagger_Patch
{
    private static bool Prefix(Character __instance, float dt, ref float ___m_staggerDamage)
    {
        if (__instance is not Player player)
            return true;

        if (player.m_staggerDamageFactor <= 0f)
            return false;

        float threshold = Traverse.Create(player).Method("GetStaggerTreshold").GetValue<float>();
        ___m_staggerDamage -= threshold / 5f * dt;
        if (___m_staggerDamage < 0f)
            ___m_staggerDamage = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
internal static class Humanoid_EquipItem_Patch
{
    private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out float __state)
    {
        __state = StaggerPercent.CaptureIfShield(__instance, item);
    }

    private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result, float __state)
    {
        if (__state < 0f || !__result)
            return;
        StaggerPercent.Apply(__instance, __state);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
internal static class Humanoid_UnequipItem_Patch
{
    private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out float __state)
    {
        __state = StaggerPercent.CaptureIfShield(__instance, item);
    }

    private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, float __state)
    {
        if (__state < 0f)
            return;
        StaggerPercent.Apply(__instance, __state);
    }
}

/// <summary>
/// Keep stagger fill ratio when shield grant capacity changes (R hide/draw, swap, inventory).
/// Prevents dumping absolute fill onto a smaller bar then looking "empty" on a larger one.
/// </summary>
internal static class StaggerPercent
{
    private const float Skip = -1f;

    internal static float CaptureIfShield(Humanoid humanoid, ItemDrop.ItemData? item)
    {
        if (humanoid is not Player)
            return Skip;
        if (item?.m_shared == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            return Skip;

        return Capture(humanoid);
    }

    internal static float Capture(Character character)
    {
        float threshold = Traverse.Create(character).Method("GetStaggerTreshold").GetValue<float>();
        if (threshold <= 0f)
            return 0f;

        float current = Traverse.Create(character).Field("m_staggerDamage").GetValue<float>();
        return Mathf.Clamp01(current / threshold);
    }

    internal static void Apply(Character character, float percent)
    {
        float threshold = Traverse.Create(character).Method("GetStaggerTreshold").GetValue<float>();
        float fill = Mathf.Clamp(percent, 0f, 1f) * Mathf.Max(0f, threshold);
        Traverse.Create(character).Field("m_staggerDamage").SetValue(fill);
    }
}

/// <summary>
/// Append a magenta stagger grant line on shield tooltips (next to block stats).
/// </summary>
[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
    typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int))]
internal static class ItemData_GetTooltip_Patch
{
    private static void Postfix(ItemDrop.ItemData item, int qualityLevel, ref string __result)
    {
        if (item?.m_shared == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            return;

        // Temporarily apply the tooltip quality for grant scaling.
        int originalQuality = item.m_quality;
        item.m_quality = qualityLevel;
        float grant = ShieldStats.GrantForItem(item);
        item.m_quality = originalQuality;

        if (grant <= 0f)
            return;

        string line = $"\nStagger: <color=orange>+{grant:0}</color>";

        // Insert after block armor block if present; otherwise append.
        const string blockKey = "$item_blockarmor:";
        int blockIdx = __result.IndexOf(blockKey, System.StringComparison.Ordinal);
        if (blockIdx >= 0)
        {
            int lineEnd = __result.IndexOf('\n', blockIdx);
            if (lineEnd < 0)
                __result += line;
            else
                __result = __result.Insert(lineEnd, line);
        }
        else
        {
            __result += line;
        }
    }
}
