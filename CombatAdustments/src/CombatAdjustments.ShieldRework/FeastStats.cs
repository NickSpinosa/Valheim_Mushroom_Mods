using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Extra health / stamina / eitr on feast items. Bonuses are added to the vanilla
/// shared food values cached on first ObjectDB apply so config changes stay idempotent.
/// </summary>
internal static class FeastStats
{
    private static readonly Dictionary<string, (float health, float stamina, float eitr)> Originals =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyToObjectDB(ObjectDB db)
    {
        if (db?.m_items == null)
            return;

        int applied = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (GameObject go in db.m_items)
        {
            if (go == null)
                continue;

            ItemDrop? drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared == null)
                continue;

            string prefab = ShieldStats.PrefabName(go);
            if (!TryIdentify(prefab, out FeastKind kind))
                continue;

            seen.Add(CanonicalPrefab(kind));
            ApplyToShared(prefab, kind, drop.m_itemData.m_shared);
            applied++;
        }

        foreach (string expected in CanonicalPrefabs)
        {
            if (!seen.Contains(expected))
                ShieldReworkPlugin.Log.LogWarning($"Feast stats: prefab '{expected}' not found in ObjectDB.");
        }

        ShieldReworkPlugin.Log.LogInfo($"Applied feast bonuses to {applied} feast items.");
    }

    internal static void ApplyToShared(string prefab, FeastKind kind, ItemDrop.ItemData.SharedData shared)
    {
        if (!Originals.ContainsKey(prefab))
            Originals[prefab] = (shared.m_food, shared.m_foodStamina, shared.m_foodEitr);

        var orig = Originals[prefab];
        if (!ShieldReworkPlugin.EnableFeastStatBonuses.Value)
        {
            shared.m_food = orig.health;
            shared.m_foodStamina = orig.stamina;
            shared.m_foodEitr = orig.eitr;
            return;
        }

        shared.m_food = orig.health + HealthBonus(kind);
        shared.m_foodStamina = orig.stamina + StaminaBonus(kind);
        shared.m_foodEitr = orig.eitr + EitrBonus(kind);
    }

    /// <summary>
    /// Vanilla food values as they were before this mod first touched them, for the
    /// diagnostics dump. <see cref="ApplyToShared"/> caches them on the first ObjectDB
    /// apply, and the dump runs after that apply, so a feast row always has one.
    /// </summary>
    internal static bool TryGetOriginal(string prefab, out (float health, float stamina, float eitr) original) =>
        Originals.TryGetValue(prefab, out original);

    internal static bool TryIdentify(string prefab, out FeastKind kind)
    {
        if (PrefabKinds.TryGetValue(prefab, out kind))
            return true;

        kind = default;
        return false;
    }

    private static float HealthBonus(FeastKind kind) =>
        kind == FeastKind.Sailors
            ? ShieldReworkPlugin.SailorsFeastHealthBonus.Value
            : ShieldReworkPlugin.FeastHealthBonus.Value;

    private static float StaminaBonus(FeastKind kind) =>
        kind == FeastKind.Sailors
            ? ShieldReworkPlugin.SailorsFeastStaminaBonus.Value
            : ShieldReworkPlugin.FeastStaminaBonus.Value;

    private static float EitrBonus(FeastKind kind) => kind switch
    {
        FeastKind.Mistlands => ShieldReworkPlugin.MistlandsFeastEitrBonus.Value,
        FeastKind.Ashlands => ShieldReworkPlugin.AshlandsFeastEitrBonus.Value,
        FeastKind.DeepNorth => ShieldReworkPlugin.DeepNorthFeastEitrBonus.Value,
        _ => 0f,
    };

    private static string CanonicalPrefab(FeastKind kind) => kind switch
    {
        FeastKind.Meadows => "FeastMeadows",
        FeastKind.BlackForest => "FeastBlackforest",
        FeastKind.Swamp => "FeastSwamps",
        FeastKind.Sailors => "FeastOceans",
        FeastKind.Mountains => "FeastMountains",
        FeastKind.Plains => "FeastPlains",
        FeastKind.Mistlands => "FeastMistlands",
        FeastKind.Ashlands => "FeastAshlands",
        FeastKind.DeepNorth => "FeastDeepNorth",
        _ => string.Empty,
    };

    private static readonly string[] CanonicalPrefabs =
    {
        "FeastMeadows",
        "FeastBlackforest",
        "FeastSwamps",
        "FeastOceans",
        "FeastMountains",
        "FeastPlains",
        "FeastMistlands",
        "FeastAshlands",
        // Deep North (1.0). Spelling not confirmed in game yet — see docs/valheim-1.0.md.
        // Being in this list is what turns a wrong guess into a startup warning instead
        // of a feast that silently gets no bonus.
        "FeastDeepNorth",
    };

    /// <summary>Prefabs this table looks for, for the diagnostics coverage report.</summary>
    internal static IEnumerable<string> TrackedPrefabs => PrefabKinds.Keys;

    /// <summary>
    /// Deliberate singular-spelling aliases carried beside the real plural prefab names,
    /// so a table lookup still resolves if a prefab is ever spelled either way. ObjectDB
    /// is not expected to contain them, in this game version or any other — the coverage
    /// report labels them instead of calling them MISSING, so a clean dump means "zero
    /// MISSING lines" with nothing to explain away. Shared with
    /// <see cref="FeastUnlocks"/>, whose recipe gate carries the same aliases.
    /// </summary>
    internal static readonly HashSet<string> AliasPrefabs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "FeastBlackForest",
            "FeastSwamp",
            "FeastOcean",
            "FeastMountain",
        };

    // Singular spellings (FeastSwamp, FeastOcean, FeastMountain) are aliases, not
    // expected prefabs — see AliasPrefabs.
    private static readonly Dictionary<string, FeastKind> PrefabKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FeastMeadows"] = FeastKind.Meadows,
            ["FeastBlackforest"] = FeastKind.BlackForest,
            ["FeastBlackForest"] = FeastKind.BlackForest,
            ["FeastSwamps"] = FeastKind.Swamp,
            ["FeastSwamp"] = FeastKind.Swamp,
            ["FeastOceans"] = FeastKind.Sailors,
            ["FeastOcean"] = FeastKind.Sailors,
            ["FeastMountains"] = FeastKind.Mountains,
            ["FeastMountain"] = FeastKind.Mountains,
            ["FeastPlains"] = FeastKind.Plains,
            ["FeastMistlands"] = FeastKind.Mistlands,
            ["FeastAshlands"] = FeastKind.Ashlands,
            ["FeastDeepNorth"] = FeastKind.DeepNorth,
        };
}

internal enum FeastKind
{
    Meadows,
    BlackForest,
    Swamp,
    Sailors,
    Mountains,
    Plains,
    Mistlands,
    Ashlands,
    DeepNorth,
}
