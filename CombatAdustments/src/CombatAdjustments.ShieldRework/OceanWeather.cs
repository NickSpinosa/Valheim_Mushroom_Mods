using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Raises Ocean <c>ThunderStorm</c> weight so storms roll ~21% instead of vanilla
/// ~7%. See <c>docs/sailing.md</c>.
/// </summary>
internal static class OceanWeather
{
    private const string ThunderStormName = "ThunderStorm";

    /// <summary>Vanilla Ocean ThunderStorm weight, restored when the feature is off.</summary>
    private static readonly Dictionary<EnvEntry, float> OriginalWeights = new();

    internal static void Apply()
    {
        if (EnvMan.instance == null)
            return;

        foreach (BiomeEnvSetup biome in EnvMan.instance.m_biomes)
            ApplyToBiome(biome);
    }

    internal static void ApplyToBiome(BiomeEnvSetup biome)
    {
        if (biome.m_biome != Heightmap.Biome.Ocean)
            return;

        EnvEntry? storm = null;
        float otherWeight = 0f;
        foreach (EnvEntry entry in biome.m_environments)
        {
            if (entry.m_ashlandsOverride || entry.m_deepnorthOverride)
                continue;

            if (entry.m_environment == ThunderStormName)
            {
                storm = entry;
                if (!OriginalWeights.ContainsKey(entry))
                    OriginalWeights[entry] = entry.m_weight;
                continue;
            }

            otherWeight += entry.m_weight;
        }

        if (storm == null)
            return;

        if (!ShieldReworkPlugin.EnableOceanStormChance.Value)
        {
            storm.m_weight = OriginalWeights[storm];
            return;
        }

        float chance = Mathf.Clamp01(ShieldReworkPlugin.OceanThunderStormChance.Value);
        if (chance <= 0f)
        {
            storm.m_weight = 0f;
            return;
        }

        if (chance >= 1f)
        {
            // Dominate the roll without deleting other entries.
            storm.m_weight = Mathf.Max(otherWeight * 100f, 1f);
            return;
        }

        // P = w / (other + w)  →  w = P * other / (1 - P)
        storm.m_weight = chance * otherWeight / (1f - chance);
    }
}

[HarmonyPatch(typeof(EnvMan), "InitializeBiomeEnvSetup")]
internal static class EnvMan_InitializeBiomeEnvSetup_Patch
{
    private static void Postfix(BiomeEnvSetup biome) => OceanWeather.ApplyToBiome(biome);
}
