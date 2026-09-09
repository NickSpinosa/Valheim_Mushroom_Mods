using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Moves biome feast unlocks one boss earlier than vanilla. Vanilla gates feasts
/// through Bog Witch spice stock; Meadows / Black Forest / Swamp share Woodland
/// Herb Blend, so those two later recipes are also key-gated. Sailor's Bounty
/// (serpent) is left on vanilla rules. Not configurable.
/// </summary>
internal static class FeastUnlocks
{
    // Vanilla Yagluth key is defeated_goblinking (not defeated_goblin).
    internal const string Eikthyr = "defeated_eikthyr";
    internal const string Elder = "defeated_gdking";
    internal const string Bonemass = "defeated_bonemass";
    internal const string Moder = "defeated_dragon";
    internal const string Yagluth = "defeated_goblinking";
    internal const string Queen = "defeated_queen";
    internal const string Fader = "defeated_fader";

    /// <summary>
    /// PROVISIONAL. The Deep North boss is FrozenKing (the localisation table has
    /// enemy_boss_frozenking_deathmessage and ach_boss8frozenking), and every boss key
    /// through Fader matches its localisation id, so defeated_frozenking is the obvious
    /// spelling — but like Queen and Fader it is set from prefab data, not a string
    /// literal in assembly_valheim.dll, so it cannot be read out of the binary.
    ///
    /// Nothing gates on it: the mod shifts each feast to the PREVIOUS boss, so the Deep
    /// North feast gates on Fader and the FrozenKing key is only ever the one we moved
    /// off. It is here so the diagnostics dump can print it next to the world's real key
    /// list and settle the spelling. See docs/valheim-1.0.md.
    /// </summary>
    internal const string FrozenKing = "defeated_frozenking";

    /// <summary>
    /// Bog Witch spice prefab → required global key after the shift.
    /// Empty string = always in stock. SpiceOceans is omitted (vanilla serpent gate).
    /// </summary>
    private static readonly Dictionary<string, string> SpiceKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Shared by Meadows / BF / Swamp. Ungated so Meadows (no previous boss)
            // is craftable as soon as the witch is found; BF/Swamp recipes are keyed below.
            ["SpiceForests"] = "",
            ["SpiceMountains"] = Bonemass,
            ["SpicePlains"] = Moder,
            ["SpiceMistlands"] = Yagluth,
            ["SpiceAshlands"] = Queen,
            // Deep North (1.0). Vanilla gates this on the Deep North boss; one biome
            // earlier is Fader.
            ["SpiceDeepNorth"] = Fader,
        };

    /// <summary>
    /// Feast recipe output prefab → previous-biome boss key.
    /// Meadows and Sailors omitted (Meadows has no previous boss; Sailors stay vanilla).
    /// </summary>
    private static readonly Dictionary<string, string> RecipeKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FeastBlackforest"] = Eikthyr,
            ["FeastBlackForest"] = Eikthyr,
            ["FeastSwamps"] = Elder,
            ["FeastSwamp"] = Elder,
            ["FeastMountains"] = Bonemass,
            ["FeastMountain"] = Bonemass,
            ["FeastPlains"] = Moder,
            ["FeastMistlands"] = Yagluth,
            ["FeastAshlands"] = Queen,
            ["FeastDeepNorth"] = Fader,
        };

    /// <summary>Prefabs the spice remap looks for, for the diagnostics coverage report.</summary>
    internal static IEnumerable<string> TrackedSpicePrefabs => SpiceKeys.Keys;

    /// <summary>Prefabs the recipe gate looks for, for the diagnostics coverage report.</summary>
    internal static IEnumerable<string> TrackedRecipePrefabs => RecipeKeys.Keys;

    private static readonly HashSet<int> RemappedTraders = new();

    internal static void ApplySpiceKeys(Trader trader)
    {
        if (trader?.m_items == null)
            return;

        int changed = 0;
        foreach (Trader.TradeItem item in trader.m_items)
        {
            if (item?.m_prefab == null)
                continue;

            string prefab = ShieldStats.PrefabName(item.m_prefab.gameObject);
            if (!SpiceKeys.TryGetValue(prefab, out string key))
                continue;

            if (!string.Equals(item.m_requiredGlobalKey, key, StringComparison.Ordinal))
            {
                item.m_requiredGlobalKey = key;
                changed++;
            }
        }

        if (changed == 0)
            return;

        int id = trader.GetInstanceID();
        if (RemappedTraders.Add(id))
        {
            ShieldReworkPlugin.Log.LogInfo(
                $"Feast unlocks: remapped {changed} Bog Witch spice gate(s) on '{ShieldStats.PrefabName(trader.gameObject)}'.");
        }
    }

    internal static bool IsRecipeLocked(Recipe? recipe)
    {
        ItemDrop? item = recipe?.m_item;
        if (item == null)
            return false;

        string prefab = ShieldStats.PrefabName(item.gameObject);
        if (!RecipeKeys.TryGetValue(prefab, out string key) || string.IsNullOrEmpty(key))
            return false;

        ZoneSystem? zs = ZoneSystem.instance;
        if (zs == null)
            return true;

        return !zs.GetGlobalKey(key);
    }
}

[HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
internal static class Trader_GetAvailableItems_FeastUnlocks_Patch
{
    private static void Prefix(Trader __instance) => FeastUnlocks.ApplySpiceKeys(__instance);
}

[HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements),
    typeof(Recipe), typeof(bool), typeof(int), typeof(int))]
internal static class Player_HaveRequirements_FeastUnlocks_Patch
{
    private static void Postfix(Recipe recipe, ref bool __result)
    {
        if (!__result)
            return;
        if (FeastUnlocks.IsRecipeLocked(recipe))
            __result = false;
    }
}
