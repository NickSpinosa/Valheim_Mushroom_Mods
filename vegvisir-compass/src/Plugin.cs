using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MushroomMods;

namespace VegvisirCompass
{
    /// <summary>
    /// Entry point. Loaded by BepInEx on both clients and the dedicated server; the
    /// mod does not assume which side it is on here, because the item prefab has to
    /// exist on both.
    /// </summary>
    // Deliberately no [BepInProcess] filter. It is a whitelist, and the Linux
    // dedicated server runs as valheim_server.x86_64 rather than valheim_server.exe,
    // so naming the Windows binaries risks the plugin being silently skipped on a
    // Linux host. BepInEx only loads what is in Valheim's own plugins folder, so
    // there is nothing the filter usefully protects against.
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }

        /// <summary>
        /// Config file name. BepInEx names one after the plugin GUID by default, which
        /// is a mouthful on disk; the GUID itself has to stay reverse-domain because it
        /// is the plugin's identity and its Harmony ID, so the file is created by hand.
        /// </summary>
        private const string ConfigFileName = "vegvisircompass.cfg";

        private Harmony _harmony;
        private ConfigFile _config;

        // --- Configuration ---------------------------------------------------
        // Static so the rest of the mod can read values without threading a Plugin
        // reference through everything.
        //
        // Kept deliberately small. Anything that only ever had one sensible value is a
        // constant in the code instead - see CompassItem and CompassVariant - and
        // anything whose "off" setting simply disabled the mod is gone entirely. What
        // is left is either decided by the server or harmless to change.
        //
        // The first three are read only inside OnServerRequest, behind an IsServer()
        // guard, and their values are baked into each granted compass. A client editing
        // their own copy changes nothing.

        internal static ConfigEntry<float> LootCooldownSeconds;
        internal static ConfigEntry<int> UsesPerCompass;
        internal static ConfigEntry<float> RangeMeters;
        internal static ConfigEntry<float> LookSmoothing;
        internal static ConfigEntry<bool> VerboseLogging;

        /// <summary>
        /// Opens the config file under our own name, carrying an older GUID-named one
        /// over if it is still there.
        ///
        /// The inherited Config property is deliberately left alone rather than
        /// replaced: it is constructed with saveOnInit false, so as long as nothing
        /// binds to it, the GUID-named file is never written.
        /// </summary>
        private ConfigFile OpenConfigFile()
        {
            string path = Path.Combine(Paths.ConfigPath, ConfigFileName);
            string legacy = Path.Combine(Paths.ConfigPath, ModInfo.Guid + ".cfg");

            try
            {
                if (!File.Exists(path) && File.Exists(legacy))
                {
                    File.Move(legacy, path);
                    Log.LogInfo($"Renamed {ModInfo.Guid}.cfg to {ConfigFileName}; your settings were kept.");
                }
            }
            catch (System.Exception e)
            {
                // A failed migration costs the old settings, not the mod: binding below
                // simply writes a fresh file with the defaults.
                Log.LogWarning($"Could not carry {ModInfo.Guid}.cfg over to {ConfigFileName}: {e.Message}");
            }

            return new ConfigFile(path, saveOnInit: true, Info.Metadata);
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _config = OpenConfigFile();

            LootCooldownSeconds = _config.Bind(
                "Vegvisir", "LootCooldownSeconds", 0f,
                new ConfigDescription(
                    "Seconds after a Vegvisir is looted before ANY player can loot it again. Enforced by the server. " +
                    "Zero disables the cooldown entirely, and the server then tracks nothing at all.",
                    new AcceptableValueRange<float>(0f, 86400f)));

            UsesPerCompass = _config.Bind(
                "Compass", "UsesPerCompass", 1,
                new ConfigDescription(
                    "How many times a compass can be used before it is destroyed.",
                    new AcceptableValueRange<int>(1, 100)));

            RangeMeters = _config.Bind(
                "Compass", "RangeMeters", 350f,
                new ConfigDescription(
                    "How far from the Vegvisir it was looted from a compass still works, measured on the " +
                    "X/Z plane so height is ignored. Zero removes the limit. Server-authoritative: the value " +
                    "is baked into each compass when it is granted.",
                    new AcceptableValueRange<float>(0f, 100000f)));

            LookSmoothing = _config.Bind(
                "Compass", "LookSmoothing", 3.5f,
                new ConfigDescription(
                    "Seconds the camera takes to pan towards the target, matching the vanilla Vegvisir. " +
                    "Mouse look is held off for the duration of the pan: vanilla applies an eased turn and " +
                    "mouse input to the same field, so without that the pan is cancelled the moment the mouse " +
                    "moves. The lockout is capped at 6 seconds however high this is set, and expires on a " +
                    "deadline so an interrupted pan can never leave you without camera control. " +
                    "Zero turns instantly instead, with no lockout.",
                    new AcceptableValueRange<float>(0f, 20f)));

            VerboseLogging = _config.Bind(
                "Debug", "VerboseLogging", false,
                "Log detailed information about looting, RPCs and compass use.");

            // Per class, so one moved patch target cannot unwind the config binding
            // above. See Shared/PatchIsolation.cs.
            _harmony = new Harmony(ModInfo.Guid);
            int skipped = PatchIsolation.PatchAllIsolated(_harmony, typeof(Plugin).Assembly, Log);

            Log.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded."
                + (skipped > 0 ? $" {skipped} patch class(es) skipped - see the errors above." : string.Empty));
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Log?.LogInfo($"{ModInfo.Name} unloaded.");
        }

        /// <summary>Logs only when the VerboseLogging config option is enabled.</summary>
        internal static void Debug(string message)
        {
            if (VerboseLogging != null && VerboseLogging.Value)
            {
                Log.LogInfo(message);
            }
        }
    }

    internal static class ModInfo
    {
        internal const string Guid = "com.dhobbs.vegvisircompass";
        internal const string Name = "Vegvisir Compass";
        internal const string Version = "1.6.0";
    }
}
