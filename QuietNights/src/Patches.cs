using System;
using HarmonyLib;

namespace QuietNights
{
    [HarmonyPatch(typeof(ZoneSystem), "Start")]
    internal static class ZoneSystem_Start_Patch
    {
        private static void Postfix(ZoneSystem __instance)
        {
            SpawnSuppressor.OnWorldStart(__instance);
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), "Awake")]
    internal static class SpawnSystem_Awake_Patch
    {
        private static void Postfix(SpawnSystem __instance)
        {
            SpawnSuppressor.OnZoneAwake(__instance);
        }
    }

    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Terminal_InitTerminal_Patch
    {
        private static bool _registered;

        private static void Postfix()
        {
            if (_registered)
                return;
            _registered = true;

            // Terminal.ConsoleCommand's constructor changes shape across game updates,
            // and C# bakes optional-argument defaults into the caller, so a DLL built
            // against an older game throws MissingMethodException right here. Letting
            // that escape the InitTerminal postfix chain takes every other mod's
            // commands with it. See docs/devops.md, "After a Valheim update".
            try
            {
                // Not a cheat: it only reads, and an admin needs it on a live server.
                _ = new Terminal.ConsoleCommand(
                    "quietnights",
                    "dump - write every boss-gated spawn entry, and what Quiet Nights does to it, to the log and config folder",
                    DumpCommand.Run,
                    isCheat: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Could not register the quietnights console command: " + ex);
            }
        }
    }
}
