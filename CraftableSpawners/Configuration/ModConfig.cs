using System.Collections.Generic;
using BepInEx.Configuration;

namespace CraftableSpawners.Configuration;

sealed class ModConfig
{
    private readonly ConfigFile ConfigFile;

    private readonly ConfigEntry<bool> lockConfiguration;

    private readonly ConfigEntry<bool> enableSkeleton;
    private readonly ConfigEntry<bool> enableGreydwarf;
    private readonly ConfigEntry<bool> enableDraugr;
    private readonly ConfigEntry<bool> enableSurtling;
    private readonly ConfigEntry<bool> enableTarBlob;

    private readonly ConfigEntry<int> skeletonBoneFragments;
    private readonly ConfigEntry<int> skeletonTrophies;

    private readonly ConfigEntry<int> greydwarfEyes;
    private readonly ConfigEntry<int> greydwarfAncientSeeds;
    private readonly ConfigEntry<int> greydwarfTrophies;

    private readonly ConfigEntry<int> draugrEntrails;
    private readonly ConfigEntry<int> draugrTrophies;

    private readonly ConfigEntry<int> surtlingCores;
    private readonly ConfigEntry<int> surtlingCoal;
    private readonly ConfigEntry<int> surtlingTrophies;

    private readonly ConfigEntry<int> tarBlobTar;
    private readonly ConfigEntry<int> tarBlobTrophies;

    private readonly ConfigEntry<bool> enableDebugMessages;

    private ConfigEntry<T> Config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = ConfigFile.Bind(group, name, value, description);
        if (synchronizedSetting)
            CraftableSpawnersPlugin.Sync.Register(configEntry);
        return configEntry;
    }

    private ConfigEntry<T> Config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) =>
        Config(group, name, value, new ConfigDescription(description), synchronizedSetting);

    internal ModConfig(ConfigFile configFile)
    {
        ConfigFile = configFile;
        configFile.SaveOnConfigSet = false;

        lockConfiguration = Config(
            "Server",
            "LockConfiguration",
            true,
            "If on, connected clients use the server's spawner settings instead of their local values.",
            false);

        enableSkeleton = Config("Enable", "SkeletonSpawner", true, "Enable the craftable Evil bone pile (skeleton spawner).");
        enableGreydwarf = Config("Enable", "GreydwarfSpawner", true, "Enable the craftable Greydwarf nest.");
        enableDraugr = Config("Enable", "DraugrSpawner", true, "Enable the craftable Body pile (draugr spawner).");
        enableSurtling = Config("Enable", "SurtlingSpawner", true, "Enable the craftable Fire pillar (surtling spawner).");
        enableTarBlob = Config("Enable", "TarBlobSpawner", true, "Enable the craftable Bone pile (tar blob spawner).");

        skeletonBoneFragments = Config("Recipes.Skeleton", "BoneFragments", 40, "Bone fragments required for Evil bone pile.");
        skeletonTrophies = Config("Recipes.Skeleton", "TrophySkeleton", 5, "Skeleton trophies required for Evil bone pile.");

        greydwarfEyes = Config("Recipes.Greydwarf", "GreydwarfEye", 20, "Greydwarf eyes required for Greydwarf nest.");
        greydwarfAncientSeeds = Config("Recipes.Greydwarf", "AncientSeed", 10, "Ancient seeds required for Greydwarf nest.");
        greydwarfTrophies = Config("Recipes.Greydwarf", "TrophyGreydwarf", 5, "Greydwarf trophies required for Greydwarf nest.");

        draugrEntrails = Config("Recipes.Draugr", "Entrails", 40, "Entrails required for Body pile.");
        draugrTrophies = Config("Recipes.Draugr", "TrophyDraugr", 5, "Draugr trophies required for Body pile.");

        surtlingCores = Config("Recipes.Surtling", "SurtlingCore", 20, "Surtling cores required for Fire pillar.");
        surtlingCoal = Config("Recipes.Surtling", "Coal", 20, "Coal required for Fire pillar.");
        surtlingTrophies = Config("Recipes.Surtling", "TrophySurtling", 5, "Surtling trophies required for Fire pillar.");

        tarBlobTar = Config("Recipes.TarBlob", "Tar", 40, "Tar required for Bone pile (tar blob spawner).");
        tarBlobTrophies = Config("Recipes.TarBlob", "TrophyGrowth", 5, "Growth (tar blob) trophies required for Bone pile.");

        enableDebugMessages = Config(
            "Troubleshooting",
            "EnableDebugMessages",
            false,
            "Enable extra debug logging.",
            false);

        HookGameplaySettingChanged(enableSkeleton);
        HookGameplaySettingChanged(enableGreydwarf);
        HookGameplaySettingChanged(enableDraugr);
        HookGameplaySettingChanged(enableSurtling);
        HookGameplaySettingChanged(enableTarBlob);
        HookGameplaySettingChanged(skeletonBoneFragments);
        HookGameplaySettingChanged(skeletonTrophies);
        HookGameplaySettingChanged(greydwarfEyes);
        HookGameplaySettingChanged(greydwarfAncientSeeds);
        HookGameplaySettingChanged(greydwarfTrophies);
        HookGameplaySettingChanged(draugrEntrails);
        HookGameplaySettingChanged(draugrTrophies);
        HookGameplaySettingChanged(surtlingCores);
        HookGameplaySettingChanged(surtlingCoal);
        HookGameplaySettingChanged(surtlingTrophies);
        HookGameplaySettingChanged(tarBlobTar);
        HookGameplaySettingChanged(tarBlobTrophies);

        ConfigPaths.ApplyAlternateOverlay(configFile);

        configFile.Save();
        configFile.SaveOnConfigSet = true;
    }

    // Rebroadcasting to clients is handled by ConfigSync.WatchForChanges; this only
    // has to refresh the local spawner prefabs.
    private static void HookGameplaySettingChanged<T>(ConfigEntry<T> entry)
    {
        entry.SettingChanged += (_, _) => SpawnerSetup.RefreshFromConfig();
    }

    /// <summary>
    /// Host-side switch: whether this machine publishes its spawner settings when it
    /// is the server. Not synced itself, so a client keeps its own answer.
    /// </summary>
    internal bool LockConfiguration => lockConfiguration.Value;

    internal bool EnableDebugMessages => enableDebugMessages.Value;

    internal bool IsEnabled(SpawnerId id) => id switch
    {
        SpawnerId.Skeleton => enableSkeleton.Value,
        SpawnerId.Greydwarf => enableGreydwarf.Value,
        SpawnerId.Draugr => enableDraugr.Value,
        SpawnerId.Surtling => enableSurtling.Value,
        SpawnerId.TarBlob => enableTarBlob.Value,
        _ => false
    };

    internal List<(string item, int amount)> GetRecipe(SpawnerId id) => id switch
    {
        SpawnerId.Skeleton =>
        [
            ("BoneFragments", skeletonBoneFragments.Value),
            ("TrophySkeleton", skeletonTrophies.Value)
        ],
        SpawnerId.Greydwarf =>
        [
            ("GreydwarfEye", greydwarfEyes.Value),
            ("AncientSeed", greydwarfAncientSeeds.Value),
            ("TrophyGreydwarf", greydwarfTrophies.Value)
        ],
        SpawnerId.Draugr =>
        [
            ("Entrails", draugrEntrails.Value),
            ("TrophyDraugr", draugrTrophies.Value)
        ],
        SpawnerId.Surtling =>
        [
            ("SurtlingCore", surtlingCores.Value),
            ("Coal", surtlingCoal.Value),
            ("TrophySurtling", surtlingTrophies.Value)
        ],
        SpawnerId.TarBlob =>
        [
            ("Tar", tarBlobTar.Value),
            ("TrophyGrowth", tarBlobTrophies.Value)
        ],
        _ => []
    };
}
