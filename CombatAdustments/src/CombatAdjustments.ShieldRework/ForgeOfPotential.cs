using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Vanilla idol prefabs set <c>m_breakChance = 1</c>, so every failed Forge of
/// Potential roll destroys the item (65% success / 35% break). The code defaults
/// are 0.65 / 0.1, which would be 65% / 25% downgrade / 10% break. This restores
/// the code-default break chance on every Upgrader* idol when the config is on.
/// See <c>docs/forge-of-potential.md</c>.
/// </summary>
internal static class ForgeOfPotential
{
    /// <summary>Matches <see cref="ItemDrop.ItemData.SharedData.m_breakChance"/>'s field default.</summary>
    internal const float IntendedBreakChance = 0.1f;

    private static readonly Dictionary<string, float> OriginalBreakChance =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyToObjectDB(ObjectDB db)
    {
        if (db?.m_items == null || db.m_items.Count == 0)
            return;

        int touched = 0;
        foreach (GameObject go in db.m_items)
        {
            if (go == null || !IsIdolPrefab(go.name))
                continue;

            ItemDrop? drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData? shared = drop?.m_itemData?.m_shared;
            if (shared == null)
                continue;

            string prefab = go.name;
            if (!OriginalBreakChance.ContainsKey(prefab))
                OriginalBreakChance[prefab] = shared.m_breakChance;

            shared.m_breakChance = ShieldReworkPlugin.EnableForgeOfPotentialOdds.Value
                ? IntendedBreakChance
                : OriginalBreakChance[prefab];
            touched++;
        }

        if (touched > 0)
        {
            ShieldReworkPlugin.Log.LogInfo(
                ShieldReworkPlugin.EnableForgeOfPotentialOdds.Value
                    ? $"Forge of Potential: set break chance to {IntendedBreakChance:0.##} on {touched} idols (65% / 25% downgrade / 10% break)."
                    : $"Forge of Potential: restored vanilla break chance on {touched} idols.");
        }
    }

    internal static bool IsIdolPrefab(string? prefabName)
    {
        if (prefabName is not { Length: > 0 })
            return false;
        // SoftRef names are Upgrader0Weapon … Upgrader7Armor.
        return prefabName.StartsWith("Upgrader", StringComparison.Ordinal)
               && (prefabName.EndsWith("Weapon", StringComparison.Ordinal)
                   || prefabName.EndsWith("Armor", StringComparison.Ordinal));
    }
}

/// <summary>
/// A quality-1 downgrade writes quality 0 back into the inventory (vanilla bug).
/// While our odds patch is on, force every failure at quality 1 to break instead.
/// </summary>
[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
internal static class InventoryGui_DoCrafting_ForgeMinLevel_Patch
{
    private static void Prefix(InventoryGui __instance, out SharedBreakOverride? __state)
    {
        __state = null;
        if (!ShieldReworkPlugin.EnableForgeOfPotentialOdds.Value)
            return;

        CraftingStation? station = Player.m_localPlayer?.GetCurrentCraftingStation();
        if (station == null || !station.m_upgrader)
            return;

        ItemDrop.ItemData? upgradeItem = Traverse.Create(__instance)
            .Field("m_craftUpgradeItem")
            .GetValue<ItemDrop.ItemData>();
        if (upgradeItem == null || upgradeItem.m_quality > 1)
            return;

        Recipe? recipe = Traverse.Create(__instance).Field("m_craftRecipe").GetValue<Recipe>();
        ItemDrop.ItemData.SharedData? idolShared = FindIdolShared(recipe);
        if (idolShared == null)
            return;

        __state = new SharedBreakOverride(idolShared, idolShared.m_breakChance);
        // Cover the entire failure band so the quality-0 downgrade path is unreachable.
        idolShared.m_breakChance = 1f;
    }

    private static void Postfix(SharedBreakOverride? __state) => __state?.Restore();

    private static ItemDrop.ItemData.SharedData? FindIdolShared(Recipe? recipe)
    {
        if (recipe?.m_resources == null)
            return null;

        foreach (Piece.Requirement requirement in recipe.m_resources)
        {
            if (requirement == null || !requirement.m_upgraderResource)
                continue;
            return requirement.m_resItem?.m_itemData?.m_shared;
        }

        return null;
    }

    internal sealed class SharedBreakOverride
    {
        private readonly ItemDrop.ItemData.SharedData _shared;
        private readonly float _original;

        internal SharedBreakOverride(ItemDrop.ItemData.SharedData shared, float original)
        {
            _shared = shared;
            _original = original;
        }

        internal void Restore() => _shared.m_breakChance = _original;
    }
}

/// <summary>
/// Vanilla centre toast for a downgrade is <c>$msg_upgrader_failed</c> ("refinement
/// failed" style). Replace with an explicit level drop while our odds patch is on.
/// </summary>
[HarmonyPatch(typeof(Localization), nameof(Localization.Localize), typeof(string), typeof(string[]))]
internal static class Localization_Localize_UpgraderFailed_Patch
{
    private static bool Prefix(Localization __instance, string text, string[] words, ref string __result)
    {
        if (!ShieldReworkPlugin.EnableForgeOfPotentialOdds.Value)
            return true;
        if (text != "$msg_upgrader_failed" || words == null || words.Length < 2)
            return true;

        // words[0] is the item shared name ($item_…); words[1] is the new quality.
        // Single-arg Localize avoids re-entering this params overload.
        string itemName = __instance.Localize(words[0]);
        __result = $"{itemName} downgraded to level {words[1]}";
        return false;
    }
}
