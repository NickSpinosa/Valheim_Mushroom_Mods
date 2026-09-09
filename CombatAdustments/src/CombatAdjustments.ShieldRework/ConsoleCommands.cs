using System;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

internal static class ConsoleCommands
{
    private static bool _registered;

    internal static void Register()
    {
        if (_registered)
            return;
        _registered = true;

        // Terminal.ConsoleCommand's constructor has changed shape across game updates (1.0.7
        // inserted hideBehindDevCommands), and C# bakes optional-argument defaults into the
        // caller, so a DLL built against an older game throws MissingMethodException right here.
        // Losing these commands is survivable; letting the exception escape the InitTerminal
        // postfix chain takes every other mod's commands down with it.
        try
        {
            // Not a cheat — available without `devcommands` so you can inspect balance mid-fight.
            _ = new Terminal.ConsoleCommand(
                "shieldstagger",
                "print current stagger bar breakdown (base HP + shield grant)",
                PrintStagger,
                isCheat: false);

            _ = new Terminal.ConsoleCommand(
                "sstagger",
                "alias for shieldstagger",
                PrintStagger,
                isCheat: false);

            _ = new Terminal.ConsoleCommand(
                "staggerhud",
                "toggle on-screen stagger current/total under the stagger bar (optional: on|off)",
                ToggleStaggerHud,
                isCheat: false);

            _ = new Terminal.ConsoleCommand(
                "shud",
                "alias for staggerhud",
                ToggleStaggerHud,
                isCheat: false);

            _ = new Terminal.ConsoleCommand(
                "cadump",
                "write the ObjectDB / global-key diagnostics dump next to the mod config",
                DumpObjectDb,
                isCheat: false);

            ShieldReworkPlugin.Log.LogInfo("Console commands registered: shieldstagger, sstagger, staggerhud, shud, cadump");
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogError(
                $"Failed to register Shield Rework console commands; continuing without them: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces the diagnostics dump without the config flag or a restart. The flag exists
    /// for dedicated servers, which have no console to type into.
    /// </summary>
    private static void DumpObjectDb(Terminal.ConsoleEventArgs args)
    {
        if (ObjectDB.instance == null)
        {
            args.Context?.AddString("<color=red>ObjectDB not loaded yet — join a world first.</color>");
            return;
        }

        string? path = Diagnostics.Write(ObjectDB.instance, "cadump console command");
        args.Context?.AddString(path == null
            ? "<color=red>Dump failed; see the BepInEx log.</color>"
            : "Dump written to " + path);
    }

    private static void ToggleStaggerHud(Terminal.ConsoleEventArgs args)
    {
        if (args.Length >= 2)
        {
            string arg = args[1].ToLowerInvariant();
            if (arg is "on" or "1" or "true")
            {
                StaggerDebugHud.SetVisible(true, args);
                return;
            }
            if (arg is "off" or "0" or "false")
            {
                StaggerDebugHud.SetVisible(false, args);
                return;
            }
        }

        StaggerDebugHud.Toggle(args);
    }

    private static void PrintStagger(Terminal.ConsoleEventArgs args)
    {
        Player? player = Player.m_localPlayer;
        if (player == null)
        {
            args.Context?.AddString("<color=red>No local player.</color>");
            return;
        }

        float maxHp = player.GetMaxHealth();
        float baseBar = maxHp * player.m_staggerDamageFactor;
        float grant = ShieldStats.GrantForEquippedShield(player);
        float total = Traverse.Create((Character)player).Method("GetStaggerTreshold").GetValue<float>();
        float current = Traverse.Create((Character)player).Field("m_staggerDamage").GetValue<float>();
        float pct = player.GetStaggerPercentage() * 100f;

        ItemDrop.ItemData? shield = player.LeftItem;
        string shieldName = "none";
        if (shield?.m_shared != null && shield.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
        {
            string prefab = shield.m_dropPrefab != null
                ? ShieldStats.PrefabName(shield.m_dropPrefab)
                : shield.m_shared.m_name;
            shieldName = $"{prefab} (q{shield.m_quality})";
        }

        args.Context?.AddString("=== Shield Rework: stagger ===");
        args.Context?.AddString($"Max HP:            {maxHp:0.#}");
        args.Context?.AddString($"Base bar (40% HP): {baseBar:0.#}");
        args.Context?.AddString($"Shield equipped:   {shieldName}");
        args.Context?.AddString($"Shield grant:      +{grant:0}");
        args.Context?.AddString($"<color=#E85AC8>Total threshold:   {total:0.#}</color>");
        args.Context?.AddString($"Current fill:      {current:0.#}  ({pct:0.#}%)");
        args.Context?.AddString($"Drain rate:        {total / 5f:0.#}/s  (full bar empties in 5s)");

        if (Player.m_localPlayer != null && MessageHud.instance != null)
        {
            MessageHud.instance.ShowMessage(
                MessageHud.MessageType.Center,
                $"Stagger {current:0}/{total:0}  (+{grant:0} from shield)");
        }
    }
}

[HarmonyPatch(typeof(Terminal), "InitTerminal")]
internal static class Terminal_InitTerminal_Patch
{
    private static void Postfix()
    {
        // Second belt: nothing this postfix does is worth aborting the rest of the chain for.
        try
        {
            ConsoleCommands.Register();
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogError($"Terminal.InitTerminal postfix failed: {ex.Message}");
        }
    }
}
