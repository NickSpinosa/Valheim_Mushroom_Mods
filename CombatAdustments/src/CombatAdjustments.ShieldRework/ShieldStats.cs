using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using MushroomSync;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

internal static class ShieldStats
{
    /// <summary>Prefab name -> max-quality stagger grant config.</summary>
    internal static readonly Dictionary<string, ConfigEntry<float>> GrantConfigs =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static readonly Dictionary<string, float> RuntimeGrants =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static readonly Dictionary<string, float> SharedNameToGrant =
        new(System.StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (float power, float perLevel)> OriginalArmor =
        new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (float max, float perLevel)> OriginalDurability =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static ShieldKind Classify(ItemDrop.ItemData.SharedData shared)
    {
        if (shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            return ShieldKind.Unknown;

        float parry = shared.m_timedBlockBonus;
        if (parry >= 2.4f)
            return ShieldKind.Buckler;
        if (parry > 1.01f)
            return ShieldKind.Round;
        return ShieldKind.Tower;
    }

    internal static string PrefabName(GameObject go)
    {
        string name = go.name;
        int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return clone >= 0 ? name.Substring(0, clone).TrimEnd() : name;
    }

    internal static float MaxBaseBlockPower(ItemDrop.ItemData.SharedData shared)
    {
        int maxQ = Mathf.Max(1, shared.m_maxQuality);
        return shared.m_blockPower + Mathf.Max(0, maxQ - 1) * shared.m_blockPowerPerLevel;
    }

    /// <summary>
    /// Tower grants seeded from leftover-through-native-tower pressure, not block armor alone.
    /// leftover = d^2/(4B) against each shield's native medium hit; normalized so Flametal = 70
    /// (Ashlands warrior swing 150 through 152 block → leftover ~37).
    /// </summary>
    private static readonly Dictionary<string, float> TowerLeftoverSeeds =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Meadows/early BF medium ~14 through wood 22 → leftover ~2 → ~5
            ["ShieldWoodTower"] = 5f,
            // Meadows/BF: treated as early tower (not Swamp). Brute ~30 through 44 → leftover ~5;
            // hand-tuned to +15 as a step above wood (+5) without Swamp-tier power.
            ["ShieldBoneTower"] = 15f,
            // Swamp elite 58 through iron 64 → leftover ~13 → ~25
            ["ShieldIronTower"] = 25f,
            // Mountain fenring 85 through serpent 72 → leftover ~25 → ~50
            ["ShieldSerpentscale"] = 50f,
            // Plains/Mistlands: deathsquito 90 → ~33; seeker claw 120 → ~59.
            // Seed for Mistlands use (still the best tower until Flametal).
            ["ShieldBlackmetalTower"] = 55f,
            ["ShieldFlametalTower"] = ShieldReworkPlugin.FlametalTowerGrant,
        };

    internal static float SeedMaxGrant(ShieldKind kind, float maxBlockArmor, string? prefab = null)
    {
        if (kind == ShieldKind.Tower && prefab != null
            && TowerLeftoverSeeds.TryGetValue(prefab, out float leftoverSeed))
            return leftoverSeed;

        float raw = kind switch
        {
            // Unknown towers: fall back to block-armor ratio from the Flametal anchor.
            ShieldKind.Tower => maxBlockArmor * (ShieldReworkPlugin.FlametalTowerGrant / 152f),
            ShieldKind.Round => maxBlockArmor * (ShieldReworkPlugin.FlametalRoundGrant / 126f),
            ShieldKind.Buckler => maxBlockArmor * (ShieldReworkPlugin.CarapaceBucklerGrant / 90f),
            _ => 0f
        };

        // Round/buckler (and unknown towers): snap to nearest 5 for cleaner balance steps.
        if (kind == ShieldKind.Round || kind == ShieldKind.Buckler || kind == ShieldKind.Tower)
            return RoundToNearest5(raw);

        return Mathf.Round(raw);
    }

    internal static float RoundToNearest5(float value) => Mathf.Round(value / 5f) * 5f;

    internal static float RoundDurability(float value) =>
        Mathf.Ceil(value * ShieldReworkPlugin.DurabilityMult / 5f) * 5f;

    internal static float RoundArmorUp(float value) =>
        Mathf.Ceil(value * ShieldReworkPlugin.TowerArmorMult);

    internal static void EnsureGrantConfig(string prefab, float seed)
    {
        if (GrantConfigs.ContainsKey(prefab))
            return;

        float def = prefab switch
        {
            "ShieldFlametalTower" => ShieldReworkPlugin.FlametalTowerGrant,
            "ShieldFlametal" => ShieldReworkPlugin.FlametalRoundGrant,
            "ShieldCarapaceBuckler" => ShieldReworkPlugin.CarapaceBucklerGrant,
            _ => seed
        };

        if (ConfigPaths.TryGetOverlayDefault("StaggerGrants", prefab, out string overlaySerialized)
            && float.TryParse(
                overlaySerialized,
                System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture,
                out float overlayGrant))
            def = overlayGrant;

        GrantConfigs[prefab] = ShieldReworkPlugin.ModConfig.Bind(
            "StaggerGrants",
            prefab,
            def,
            $"Max-quality flat stagger grant for {prefab}. 0 disables.");
        // Rebroadcasting on change is handled by ConfigSync.WatchForChanges, which
        // watches the file rather than each entry - grants are bound lazily per
        // prefab, so per-entry hooks would multiply as shields are discovered.
        ShieldReworkPlugin.Sync.Register(GrantConfigs[prefab]);
    }

    internal static float GetMaxGrant(string prefab)
    {
        if (GrantConfigs.TryGetValue(prefab, out var entry))
            return entry.Value;
        return RuntimeGrants.TryGetValue(prefab, out float g) ? g : 0f;
    }

    /// <summary>
    /// Quality-scaled grant. Config / table values are the <b>max-quality</b> total.
    /// Each ★ adds a small even step (towers/rounds: +1 early / +2 mid / +3 flametal;
    /// bucklers: always +1) so a fresh iron tower is ~+22, not a fraction of +25.
    /// </summary>
    internal static float GrantForItem(ItemDrop.ItemData? item)
    {
        if (!ShieldReworkPlugin.EnableStaggerGrant.Value || item?.m_shared == null)
            return 0f;
        if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            return 0f;

        string? prefab = null;
        float maxGrant = 0f;
        if (item.m_dropPrefab != null)
        {
            prefab = PrefabName(item.m_dropPrefab);
            maxGrant = GetMaxGrant(prefab);
        }

        if (maxGrant <= 0f
            && !SharedNameToGrant.TryGetValue(item.m_shared.m_name, out maxGrant))
            return 0f;

        if (maxGrant <= 0f)
            return 0f;

        float step = GrantPerQualityStep(prefab, Classify(item.m_shared));
        return GrantAtQuality(maxGrant, item.m_quality, item.m_shared.m_maxQuality, step);
    }

    /// <summary>
    /// Per-★ grant step:
    /// bucklers always +1; towers/rounds use progression bands
    /// (wood→iron +1, silver/serpent→carapace +2, flametal +3).
    /// </summary>
    internal static float GrantPerQualityStep(string? prefab, ShieldKind kind = ShieldKind.Unknown)
    {
        if (kind == ShieldKind.Buckler)
            return 1f;

        if (string.IsNullOrEmpty(prefab))
            return 1f;

        string name = prefab!;
        if (GrantStep3.Contains(name))
            return 3f;
        if (GrantStep2.Contains(name))
            return 2f;
        return 1f;
    }

    private static readonly HashSet<string> GrantStep3 =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Flametal tower + round
            "ShieldFlametal",
            "ShieldFlametalTower",
        };

    private static readonly HashSet<string> GrantStep2 =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Mid towers
            "ShieldSerpentscale",
            "ShieldBlackmetalTower",
            // Mid rounds (silver → carapace); bucklers are forced to +1 via kind
            "ShieldSilver",
            "ShieldBlackmetal",
            "ShieldCarapace",
            "ShieldKnight",
        };

    /// <summary>
    /// Even steps ending at <paramref name="maxGrant"/>:
    /// grant = maxGrant - (maxQ-1)*step + (quality-1)*step.
    /// </summary>
    internal static float GrantAtQuality(float maxGrant, int quality, int maxQuality, float perStep)
    {
        if (maxGrant <= 0f)
            return 0f;

        int maxQ = Mathf.Max(1, maxQuality);
        int q = Mathf.Clamp(quality, 1, maxQ);
        if (maxQ == 1 || q >= maxQ)
            return maxGrant;

        float step = Mathf.Max(0f, perStep);
        // If step would push the base below 0, shrink step so Q1 stays non-negative.
        float maxStep = maxGrant / (maxQ - 1);
        if (step > maxStep)
            step = maxStep;

        float baseGrant = maxGrant - (maxQ - 1) * step;
        return Mathf.Round(baseGrant + (q - 1) * step);
    }

    internal static float GrantForEquippedShield(Humanoid humanoid)
    {
        ItemDrop.ItemData? left = humanoid.LeftItem;
        if (left == null || left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            return 0f;
        return GrantForItem(left);
    }

    internal static void ApplyToObjectDB(ObjectDB db)
    {
        if (db?.m_items == null)
            return;

        bool reseeding = ConfigSync.IsServerAuthority()
            && ShieldReworkPlugin.GrantTableVersion.Value
            < ShieldReworkPlugin.CurrentGrantTableVersion;
        if (reseeding)
        {
            ShieldReworkPlugin.Log.LogInfo(
                $"Re-seeding StaggerGrants to designed max-quality table (cfg v{ShieldReworkPlugin.GrantTableVersion.Value} → v{ShieldReworkPlugin.CurrentGrantTableVersion}).");
        }

        int shields = 0;
        foreach (GameObject go in db.m_items)
        {
            if (go == null)
                continue;
            ItemDrop? drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared == null)
                continue;

            var shared = drop.m_itemData.m_shared;
            ShieldKind kind = Classify(shared);
            if (kind == ShieldKind.Unknown)
                continue;

            string prefab = PrefabName(go);
            float maxBlock = MaxBaseBlockPower(shared);
            float seed = SeedMaxGrant(kind, maxBlock, prefab);
            EnsureGrantConfig(prefab, seed);

            if (reseeding && GrantConfigs.TryGetValue(prefab, out var entry))
                entry.Value = seed;

            float grant = GetMaxGrant(prefab);
            RuntimeGrants[prefab] = grant;
            SharedNameToGrant[shared.m_name] = grant;
            shields++;

            if (kind == ShieldKind.Tower && !OriginalArmor.ContainsKey(prefab))
                OriginalArmor[prefab] = (shared.m_blockPower, shared.m_blockPowerPerLevel);

            if (kind == ShieldKind.Tower || kind == ShieldKind.Round)
            {
                if (!OriginalDurability.ContainsKey(prefab))
                    OriginalDurability[prefab] = (shared.m_maxDurability, shared.m_durabilityPerLevel);
            }

            if (ShieldReworkPlugin.EnableTowerArmorBonus.Value && kind == ShieldKind.Tower)
            {
                var orig = OriginalArmor[prefab];
                shared.m_blockPower = RoundArmorUp(orig.power);
                shared.m_blockPowerPerLevel = RoundArmorUp(orig.perLevel);
            }
            else if (OriginalArmor.TryGetValue(prefab, out var armorOrig))
            {
                shared.m_blockPower = armorOrig.power;
                shared.m_blockPowerPerLevel = armorOrig.perLevel;
            }

            if (ShieldReworkPlugin.EnableDurabilityBonus.Value
                && (kind == ShieldKind.Tower || kind == ShieldKind.Round))
            {
                var orig = OriginalDurability[prefab];
                shared.m_maxDurability = RoundDurability(orig.max);
                shared.m_durabilityPerLevel = RoundDurability(orig.perLevel);
            }
            else if (OriginalDurability.TryGetValue(prefab, out var durOrig))
            {
                shared.m_maxDurability = durOrig.max;
                shared.m_durabilityPerLevel = durOrig.perLevel;
            }
        }

        if (reseeding)
            ShieldReworkPlugin.GrantTableVersion.Value = ShieldReworkPlugin.CurrentGrantTableVersion;

        ConfigPaths.ApplyOverlayAfterObjectDbLoad(ShieldReworkPlugin.ModConfig);

        ShieldReworkPlugin.Log.LogInfo($"Processed {shields} shields from ObjectDB.");
    }
}

internal enum ShieldKind
{
    Unknown,
    Tower,
    Round,
    Buckler
}
