using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CraftableSpawners.Configuration;
using HarmonyLib;
using MushroomSync;
using UnityEngine;

namespace CraftableSpawners;

public enum SpawnerId
{
    Skeleton,
    Greydwarf,
    Draugr,
    Surtling,
    TarBlob
}

[BepInPlugin(PluginID, PluginName, Version)]
[BepInDependency(MushroomSyncPlugin.PluginGuid)]
public sealed class CraftableSpawnersPlugin : BaseUnityPlugin
{
    public const string PluginID = "Gonfreecss.CraftableSpawners";
    public const string PluginName = "CraftableSpawners";
    public const string Version = "0.2.0";

    internal static ManualLogSource Log = new($" {PluginName}");
    internal static ModConfig ConfigSyncWrapper;
    internal static bool HammerRemoving;

    /// <summary>
    /// Server-authoritative settings. Created before <see cref="ModConfig"/> because
    /// binding a setting registers it here.
    /// </summary>
    internal static ConfigSync Sync;

    private readonly Harmony harmony = new(PluginID);

    internal void Awake()
    {
        BepInEx.Logging.Logger.Sources.Add(Log);

        ConfigFile configFile = ConfigPaths.CreateMergedConfig();

        // Spawner prefabs bake their drop tables from config, so a change on either
        // side has to rebuild them - hence OnApplied as well as the local hook.
        Sync = ConfigSync.Create(PluginID, Version, Log)
            .Protecting(configFile)
            .OnApplied(SpawnerSetup.RefreshFromConfig);

        ConfigSyncWrapper = new ModConfig(configFile);

        Sync.WatchForChanges(configFile).Start();

        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Dbgl($"Loaded {PluginName} {Version}");
    }

    internal static void Dbgl(string message, bool forceLog = false)
    {
        if (forceLog || ConfigSyncWrapper is { EnableDebugMessages: true })
            Log.LogInfo(message);
    }
}

internal sealed class SpawnerDef
{
    internal SpawnerId Id;
    internal string SourcePrefab;
    internal string CloneName;
    internal string DisplayName;
    internal string Description;
    internal string TrophyPrefab;
    internal GameObject Prefab;
}

internal static class SpawnerCatalog
{
    internal static readonly List<SpawnerDef> All =
    [
        new()
        {
            Id = SpawnerId.Skeleton,
            SourcePrefab = "BonePileSpawner",
            CloneName = "CS_BonePileSpawner",
            DisplayName = "Evil bone pile",
            Description = "Spawns skeletons",
            TrophyPrefab = "TrophySkeleton",
        },
        new()
        {
            Id = SpawnerId.Greydwarf,
            SourcePrefab = "Spawner_GreydwarfNest",
            CloneName = "CS_GreydwarfNest",
            DisplayName = "Greydwarf nest",
            Description = "Spawns greydwarves",
            TrophyPrefab = "TrophyGreydwarf",
        },
        new()
        {
            Id = SpawnerId.Draugr,
            SourcePrefab = "Spawner_DraugrPile",
            CloneName = "CS_DraugrPile",
            DisplayName = "Body pile",
            Description = "Spawns draugr",
            TrophyPrefab = "TrophyDraugr",
        },
        new()
        {
            Id = SpawnerId.Surtling,
            // Vanilla Spawner_imp_respawn is an invisible CreatureSpawner (1 mob, no mesh).
            // Build Fire pillar like the other craftables: SpawnArea + Destructible + visual.
            SourcePrefab = "BonePileSpawner",
            CloneName = "CS_FirePillar",
            DisplayName = "Fire pillar",
            Description = "Spawns surtlings",
            TrophyPrefab = "TrophySurtling",
        },
        new()
        {
            Id = SpawnerId.TarBlob,
            // Vanilla Spawner_BlobTar_respawn_30 is an invisible CreatureSpawner.
            // Use BonePileSpawner structure + BlobTar SpawnArea + lox_ribs (tar pit bones) visual.
            SourcePrefab = "BonePileSpawner",
            CloneName = "CS_TarBonePile",
            DisplayName = "Bone pile",
            Description = "Spawns tar blobs",
            TrophyPrefab = "TrophyGrowth",
        }
    ];

    internal static SpawnerDef FindByCloneName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        name = name.Replace("(Clone)", "").Trim();
        foreach (SpawnerDef def in All)
        {
            if (def.CloneName == name)
                return def;
        }

        return null;
    }

    internal static SpawnerDef FindByTrophyPrefab(string trophyPrefab)
    {
        foreach (SpawnerDef def in All)
        {
            if (def.TrophyPrefab == trophyPrefab)
                return def;
        }

        return null;
    }
}
