using System.Text;
using HarmonyLib;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class TerminalInitPatch
    {
        private static void Postfix()
        {
            // Terminal.ConsoleCommand's constructor has changed shape across game updates (1.0.7
            // inserted hideBehindDevCommands), and C# bakes optional-argument defaults into the
            // caller, so a DLL built against an older game throws MissingMethodException right
            // here. Losing `separatespawns` is survivable; letting the exception escape the
            // InitTerminal postfix chain takes every other mod's commands down with it.
            try
            {
                new Terminal.ConsoleCommand("separatespawns", "Separate Spawns status and debug info.", args =>
                {
                    var output = new StringBuilder();
                    output.AppendLine("Separate Spawns status");
                    output.AppendLine($"Config: {ModPaths.GetPluginConfigPath()}");
                    output.AppendLine($"Roster read: {GroupRoster.RosterPath}");
                    output.AppendLine($"Roster write: {GroupRoster.RosterWritePath}");
                    output.AppendLine($"Groups: {string.Join(", ", Plugin.Roster.GetGroupNames())}");
                    foreach (var pair in Plugin.Roster.Groups)
                    {
                        var difficulty = pair.Value != null && pair.Value.HasDifficulty
                            ? pair.Value.Difficulty.Value.ToString()
                            : "unset";
                        output.AppendLine($"  {pair.Key}: difficulty={difficulty}, players={pair.Value?.Players?.Count ?? 0}");
                    }

                    if (Plugin.LayoutCache.Current == null)
                    {
                        output.AppendLine("Layout: not loaded");
                    }
                    else if (Plugin.LayoutCache.Current.Failed)
                    {
                        output.AppendLine($"Layout: FAILED - {Plugin.LayoutCache.Current.FailureReason}");
                    }
                    else
                    {
                        output.AppendLine($"Layout: loaded ({Plugin.LayoutCache.Current.GroupSpawnPositions.Count} spawns, frozen={Plugin.LayoutCache.Current.Frozen})");
                        foreach (var pair in Plugin.LayoutCache.Current.GroupSpawnPositions)
                        {
                            output.AppendLine($"  {pair.Key}: ({pair.Value.x:F0}, {pair.Value.z:F0})");
                        }
                    }

                    Console.instance.Print(output.ToString());
                    return true;
                });
            }
            catch (System.Exception ex)
            {
                // System.Exception spelled out: `using System;` would make the `Console` above
                // ambiguous between System.Console and Valheim's.
                ModLog.Error($"Failed to register the separatespawns console command; continuing without it: {ex.Message}");
            }
        }
    }
}
