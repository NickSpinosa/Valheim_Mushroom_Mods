using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MushroomMods;
using MushroomSync;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(MushroomSyncPlugin.PluginGuid)]
public class ShieldReworkPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "Abortipus.CombatAdjustments.ShieldRework";
    public const string PluginName = "Combat Adjustments - Shield Rework";
    public const string PluginVersion = "0.7.1";

    // Design anchors (max quality). See docs/shield-rework-requirements.md.
    public const float FlametalTowerGrant = 70f;
    public const float FlametalRoundGrant = 45f;
    public const float CarapaceBucklerGrant = 20f;
    public const float TowerArmorMult = 1.05f;
    public const float DurabilityMult = 1.20f;

    /// <summary>
    /// Bump when designed StaggerGrants table changes so existing .cfg values are rewritten to seeds.
    /// </summary>
    public const int CurrentGrantTableVersion = 2;

    internal static ManualLogSource Log = null!;
    internal static ShieldReworkPlugin Instance = null!;
    internal static ConfigFile ModConfig = null!;

    internal static ConfigEntry<bool> EnableStaggerGrant = null!;
    internal static ConfigEntry<bool> SyncConfigInMultiplayer = null!;
    internal static ConfigEntry<bool> EnableTowerArmorBonus = null!;
    internal static ConfigEntry<bool> EnableDurabilityBonus = null!;
    internal static ConfigEntry<bool> EnableTwoHandedCombat = null!;
    internal static ConfigEntry<float> GreatswordPrimaryStaggerMultiplier = null!;
    internal static ConfigEntry<bool> AreaAdrenalinePerEnemy = null!;
    internal static ConfigEntry<bool> EnableWeaponBlockPerLevel = null!;
    internal static ConfigEntry<int> GrantTableVersion = null!;
    internal static ConfigEntry<string> TooltipColorHex = null!;

    internal static ConfigEntry<bool> EnableFeastStatBonuses = null!;
    internal static ConfigEntry<float> FeastHealthBonus = null!;
    internal static ConfigEntry<float> FeastStaminaBonus = null!;
    internal static ConfigEntry<float> SailorsFeastHealthBonus = null!;
    internal static ConfigEntry<float> SailorsFeastStaminaBonus = null!;
    internal static ConfigEntry<float> MistlandsFeastEitrBonus = null!;
    internal static ConfigEntry<float> AshlandsFeastEitrBonus = null!;

    internal static ConfigEntry<bool> EnableSailingWindCurve = null!;
    internal static ConfigEntry<float> SailingCalmForceFactor = null!;
    internal static ConfigEntry<float> SailingKneeForceFactor = null!;
    internal static ConfigEntry<float> SailingMaxForceFactor = null!;
    internal static ConfigEntry<float> SailingCalmWindCeiling = null!;
    internal static ConfigEntry<bool> EnableOceanStormChance = null!;
    internal static ConfigEntry<float> OceanThunderStormChance = null!;

    internal static ConfigEntry<bool> EnableUncapHealthScaling = null!;

    /// <summary>Server-authoritative settings, shared with the other Mushroom mods.</summary>
    internal static ConfigSync Sync = null!;

    private Harmony? _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        ModConfig = ConfigPaths.CreateMergedConfig(PluginGuid);

        // Created before any Bind so ShieldStats can register grant entries as it
        // discovers shields.
        // SyncConfigInMultiplayer means "do not sync at all", so it gates both
        // directions: this machine publishes nothing when hosting, and ignores host
        // values when connected. The old in-mod ConfigSync checked it on both sides
        // too.
        Sync = ConfigSync.Create(PluginGuid, PluginVersion, Logger)
            .Protecting(ModConfig)
            .GatedBy(() => SyncConfigInMultiplayer == null || SyncConfigInMultiplayer.Value)
            .AcceptedWhen(() => SyncConfigInMultiplayer == null || SyncConfigInMultiplayer.Value)
            .OnApplied(ApplyRuntimeFromConfig)
            .Notifying(NotifyPlayer);

        EnableStaggerGrant = ModConfig.Bind("General", "EnableStaggerGrant", true,
            "Add flat stagger-bar capacity while a shield is equipped.");
        SyncConfigInMultiplayer = ModConfig.Bind("General", "SyncConfigInMultiplayer", true,
            "When hosting or on a dedicated server, send this installation's config to joining clients. Clients use host values at runtime without overwriting their local .cfg.");
        EnableTowerArmorBonus = ModConfig.Bind("General", "EnableTowerArmorBonus", true,
            "Apply +5% block armor (rounded up) to tower shields.");
        EnableDurabilityBonus = ModConfig.Bind("General", "EnableDurabilityBonus", true,
            "Apply +20% durability (ceil to nearest 5) to tower and round shields.");
        EnableTwoHandedCombat = ModConfig.Bind("Two-Handed Combat", "Enable", true,
            "Enable stagger-only hyper armor and damage/stagger adjustments for two-handed melee weapons.");
        GreatswordPrimaryStaggerMultiplier = ModConfig.Bind("Two-Handed Combat", "GreatswordPrimaryStaggerMultiplier", 1.5f,
            "Final stagger multiplier for primary greatsword swings. 1.5 = +50%.");
        AreaAdrenalinePerEnemy = ModConfig.Bind("Two-Handed Combat", "AreaAdrenalinePerEnemy", true,
            "Two-handed club ground slams (Stagbreaker, Iron Sledge, Demolisher) grant adrenaline per enemy hit, like swing attacks, instead of once per slam.");
        EnableWeaponBlockPerLevel = ModConfig.Bind("Two-Handed Combat", "EnableWeaponBlockPerLevel", true,
            "Scale block armor per weapon quality for two-handed and dual-wield blocking weapons (replaces vanilla m_blockPowerPerLevel).");
        GrantTableVersion = ModConfig.Bind("General", "GrantTableVersion", 0,
            "Internal. Bump with plugin to re-seed StaggerGrants.* to designed max-quality values.");
        TooltipColorHex = ModConfig.Bind("Tooltip", "StaggerColorHex", "#E85AC8",
            "Hex color for the stagger grant tooltip line (matches HUD stagger pink).");

        EnableFeastStatBonuses = ModConfig.Bind("Feasts", "EnableStatBonuses", true,
            "Add extra health / stamina / eitr to feast foods. Boss unlocks are not configurable and always apply.");
        FeastHealthBonus = ModConfig.Bind("Feasts", "HealthBonus", 10f,
            "Extra max health added to every feast except Sailor's Bounty.");
        FeastStaminaBonus = ModConfig.Bind("Feasts", "StaminaBonus", 10f,
            "Extra max stamina added to every feast except Sailor's Bounty.");
        SailorsFeastHealthBonus = ModConfig.Bind("Feasts", "SailorsHealthBonus", 15f,
            "Extra max health added to Sailor's Bounty (instead of HealthBonus).");
        SailorsFeastStaminaBonus = ModConfig.Bind("Feasts", "SailorsStaminaBonus", 15f,
            "Extra max stamina added to Sailor's Bounty (instead of StaminaBonus).");
        MistlandsFeastEitrBonus = ModConfig.Bind("Feasts", "MistlandsEitrBonus", 7f,
            "Extra eitr added to Mushrooms Galore à la Mistlands (vanilla 33 → 40). Also receives HealthBonus / StaminaBonus.");
        AshlandsFeastEitrBonus = ModConfig.Bind("Feasts", "AshlandsEitrBonus", 12f,
            "Extra eitr added to Ashlands Gourmet Bowl (vanilla 38 → 50). Also receives HealthBonus / StaminaBonus.");

        EnableSailingWindCurve = ModConfig.Bind("Sailing", "EnableWindCurve", true,
            "Replace vanilla linear wind→sail force with a two-segment curve (calm matches vanilla to 60%, storms ramp higher).");
        SailingCalmForceFactor = ModConfig.Bind("Sailing", "CalmForceFactor", 0.287f,
            "Sail intensity factor at 0% wind. Vanilla uses ~0.287 at the 5% clamp floor.");
        SailingKneeForceFactor = ModConfig.Bind("Sailing", "KneeForceFactor", 0.7f,
            "Sail intensity factor at CalmWindCeiling (default 60%). Matches vanilla Lerp(0.25,1,0.6).");
        SailingMaxForceFactor = ModConfig.Bind("Sailing", "MaxForceFactor", 2f,
            "Sail intensity factor at 100% wind (vanilla tops out at 1).");
        SailingCalmWindCeiling = ModConfig.Bind("Sailing", "CalmWindCeiling", 0.6f,
            "Wind intensity (0–1) where the calm segment ends and the storm ramp begins. Clear weather tops out here.");
        EnableOceanStormChance = ModConfig.Bind("Sailing", "EnableOceanStormChance", true,
            "Raise Ocean biome ThunderStorm weight so storms occur more often while sailing.");
        OceanThunderStormChance = ModConfig.Bind("Sailing", "OceanThunderStormChance", 0.21f,
            "Target chance (0–1) of ThunderStorm on the Ocean biome. Vanilla is ~0.071 (7%). Default 0.21 = 21%.");

        EnableUncapHealthScaling = ModConfig.Bind("Difficulty", "EnableUncapHealthScaling", true,
            "Let effective enemy HP keep scaling with nearby players past vanilla's 5-player cap (+30% per extra player). Enemy damage dealt stays capped at 5.");

        Sync.Register(
            EnableStaggerGrant,
            EnableTowerArmorBonus,
            EnableDurabilityBonus,
            EnableTwoHandedCombat,
            GreatswordPrimaryStaggerMultiplier,
            AreaAdrenalinePerEnemy,
            EnableWeaponBlockPerLevel,
            TooltipColorHex,
            EnableFeastStatBonuses,
            FeastHealthBonus,
            FeastStaminaBonus,
            SailorsFeastHealthBonus,
            SailorsFeastStaminaBonus,
            MistlandsFeastEitrBonus,
            AshlandsFeastEitrBonus,
            EnableSailingWindCurve,
            SailingCalmForceFactor,
            SailingKneeForceFactor,
            SailingMaxForceFactor,
            SailingCalmWindCeiling,
            EnableOceanStormChance,
            OceanThunderStormChance,
            EnableUncapHealthScaling);

        // Deliberately not synced. GrantTableVersion is server-only reseed
        // bookkeeping, and SyncConfigInMultiplayer is the opt-out itself - a client
        // that switched syncing off must keep that answer.
        Sync.Exclude(GrantTableVersion)
            .Exclude(SyncConfigInMultiplayer);

        // Feast bonuses are baked into ObjectDB items, so editing one locally has to
        // rebuild them. Rebroadcasting to clients is handled by WatchForChanges, and
        // an incoming host config is handled by OnApplied; this covers only the
        // local edit.
        HookFeastConfigChange(EnableFeastStatBonuses);
        HookFeastConfigChange(FeastHealthBonus);
        HookFeastConfigChange(FeastStaminaBonus);
        HookFeastConfigChange(SailorsFeastHealthBonus);
        HookFeastConfigChange(SailorsFeastStaminaBonus);
        HookFeastConfigChange(MistlandsFeastEitrBonus);
        HookFeastConfigChange(AshlandsFeastEitrBonus);

        HookOceanWeatherConfigChange(EnableOceanStormChance);
        HookOceanWeatherConfigChange(OceanThunderStormChance);

        ApplyOverlayToStaticEntries();

        Sync.WatchForChanges(ModConfig).Start();

        // EnvMan may already be awake if this plugin loads late.
        OceanWeather.Apply();

        ConsoleCommands.Register(); // safe if Terminal not ready yet; patch also registers on InitTerminal

        // Last on purpose, and per class. Everything above has to happen whether or not
        // a patch target moved - in 1.0.7 a single changed GetTooltip signature took all
        // of it down (#5). See Shared/PatchIsolation.cs.
        _harmony = new Harmony(PluginGuid);
        int skipped = PatchIsolation.PatchAllIsolated(_harmony, Assembly.GetExecutingAssembly(), Log);

        Log.LogInfo($"{PluginName} {PluginVersion} loaded."
            + (skipped > 0 ? $" {skipped} patch class(es) skipped - see the errors above." : string.Empty));
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    internal static Color GetTooltipColor()
    {
        if (ColorUtility.TryParseHtmlString(TooltipColorHex.Value, out var color))
            return color;
        return new Color(0.91f, 0.35f, 0.78f);
    }

    internal static string ColorToHex(Color color) => $"#{ColorUtility.ToHtmlStringRGB(color)}";

    /// <summary>
    /// Shield, weapon and feast stats are baked into ObjectDB items, so a config
    /// change is not enough on its own - the values have to be pushed back into the
    /// database. Runs when host values arrive and again when they are dropped.
    /// </summary>
    private static void ApplyRuntimeFromConfig()
    {
        OceanWeather.Apply();

        if (ObjectDB.instance == null)
            return;

        ShieldStats.ApplyToObjectDB(ObjectDB.instance);
        WeaponBlockStats.ApplyToObjectDB(ObjectDB.instance);
        FeastStats.ApplyToObjectDB(ObjectDB.instance);
    }

    private static void NotifyPlayer(string message)
    {
        Player? player = Player.m_localPlayer;
        if (player != null)
            player.Message(MessageHud.MessageType.TopLeft, message, 0, null);
    }

    private static void HookFeastConfigChange<T>(ConfigEntry<T> entry) =>
        entry.SettingChanged += (_, __) =>
        {
            if (ObjectDB.instance != null)
                FeastStats.ApplyToObjectDB(ObjectDB.instance);
        };

    private static void HookOceanWeatherConfigChange<T>(ConfigEntry<T> entry) =>
        entry.SettingChanged += (_, __) => OceanWeather.Apply();

    private static void ApplyOverlayToStaticEntries() =>
        ConfigPaths.ApplyOverlayToStaticEntries(ModConfig);
}
