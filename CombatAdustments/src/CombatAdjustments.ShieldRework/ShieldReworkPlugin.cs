using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MushroomSync;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(MushroomSyncPlugin.PluginGuid)]
public class ShieldReworkPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "Abortipus.CombatAdjustments.ShieldRework";
    public const string PluginName = "Combat Adjustments - Shield Rework";
    public const string PluginVersion = "0.5.0";

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
    internal static ConfigEntry<float> HyperArmorDamageReduction = null!;
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
        Sync = ConfigSync.Create(PluginGuid, PluginVersion, Logger)
            .Protecting(ModConfig)
            .GatedBy(() => SyncConfigInMultiplayer == null || SyncConfigInMultiplayer.Value)
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
        HyperArmorDamageReduction = ModConfig.Bind("Two-Handed Combat", "HyperArmorDamageReduction", 0.25f,
            "Fraction of incoming damage ignored during hyper-armor (greatsword / battleaxe / sledge swings). 0.25 = 25% reduction. Stacks multiplicatively with Bonemass and other resists. Clamped to 0–1.");
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

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        Sync.Register(
            EnableStaggerGrant,
            EnableTowerArmorBonus,
            EnableDurabilityBonus,
            EnableTwoHandedCombat,
            GreatswordPrimaryStaggerMultiplier,
            HyperArmorDamageReduction,
            AreaAdrenalinePerEnemy,
            EnableWeaponBlockPerLevel,
            TooltipColorHex,
            EnableFeastStatBonuses,
            FeastHealthBonus,
            FeastStaminaBonus,
            SailorsFeastHealthBonus,
            SailorsFeastStaminaBonus,
            MistlandsFeastEitrBonus,
            AshlandsFeastEitrBonus);

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

        ApplyOverlayToStaticEntries();

        Sync.WatchForChanges(ModConfig).Start();

        ConsoleCommands.Register(); // safe if Terminal not ready yet; patch also registers on InitTerminal
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
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

    private static void ApplyOverlayToStaticEntries() =>
        ConfigPaths.ApplyOverlayToStaticEntries(ModConfig);
}
