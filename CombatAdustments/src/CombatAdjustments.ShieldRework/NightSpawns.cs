using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Boss kills turn on extra night-only world spawners (meadows greydwarfs after
/// Eikthyr, skeletons after Bonemass, seekers after the Queen, charred after
/// Fader). Those entries carry a <c>defeated_*</c> required global key and do
/// not spawn during the day. Biome night spawns with no key are left alone, and
/// so are raids — those are <c>RandEventSystem</c> lists, not night world spawns.
/// </summary>
internal static class NightSpawns
{
    // Serpent trophy key, not a boss. No night world spawner uses it; excluded
    // so a future one is not swept up with the boss-defeat keys.
    private const string SerpentKey = "defeated_serpent";

    internal static bool IsBossDefeatKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        if (key.Equals(SerpentKey, StringComparison.OrdinalIgnoreCase))
            return false;
        return key.StartsWith("defeated_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Night-only, and only because a boss has been killed. Odin's night visit
    /// uses the same shape and is not a combat spawn.
    /// </summary>
    internal static bool ShouldSuppress(SpawnSystem.SpawnData spawner)
    {
        if (spawner == null || !spawner.m_enabled)
            return false;
        if (!spawner.m_spawnAtNight || spawner.m_spawnAtDay)
            return false;
        if (!IsBossDefeatKey(spawner.m_requiredGlobalKey))
            return false;
        string? name = spawner.m_prefab != null ? spawner.m_prefab.name : null;
        return name == null || name.IndexOf("odin", StringComparison.OrdinalIgnoreCase) < 0;
    }
}

/// <summary>
/// Filters a copy of the spawner list. The live prefab list must not be edited,
/// and entries must not be removed: <c>UpdateSpawnList</c> hashes the 1-based
/// index into the zone ZDO as that spawner's timer. Dropping a row would reset
/// every later timer and collide hashes with a different creature.
/// </summary>
[HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
internal static class SpawnSystem_UpdateSpawnList_Patch
{
    private static void Prefix(ref List<SpawnSystem.SpawnData> spawners, bool eventSpawners)
    {
        if (eventSpawners || spawners == null || spawners.Count == 0)
            return;
        if (!ShieldReworkPlugin.IgnoreBossNightSpawns.Value)
            return;

        List<SpawnSystem.SpawnData>? copy = null;
        for (int i = 0; i < spawners.Count; i++)
        {
            if (!NightSpawns.ShouldSuppress(spawners[i]))
                continue;

            copy ??= new List<SpawnSystem.SpawnData>(spawners);
            SpawnSystem.SpawnData disabled = spawners[i].Clone();
            disabled.m_enabled = false;
            copy[i] = disabled;
        }

        if (copy != null)
            spawners = copy;
    }
}
