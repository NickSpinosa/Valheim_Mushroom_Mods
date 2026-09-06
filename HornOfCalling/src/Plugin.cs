using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace HornOfCalling
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "com.greg.hornofcalling";
        internal const string Name = "HornOfCalling";
        internal const string Version = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// How loud the blast is for the player running this copy of the mod.
        ///
        /// Deliberately not synced through MushroomSync. The blast prefab is
        /// instantiated locally on every peer that hears it, so this setting decides
        /// what <em>you</em> hear and never what anyone else does - the same shape as
        /// the game's own SFX slider. A host-authoritative version of it would let one
        /// player turn down horns in someone else's headphones.
        /// </summary>
        internal static ConfigEntry<float> BlastVolume;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            BlastVolume = Config.Bind(
                "Audio", "BlastVolume", 1f,
                new ConfigDescription(
                    "How loud the horn blast is, as a fraction of the recorded level. This multiplies " +
                    "Valheim's own sound-effects volume rather than replacing it, and applies to every " +
                    "blast you hear, whoever sounded it. Also on the Audio tab of the in-game settings " +
                    "menu, which writes back here.",
                    new AcceptableValueRange<float>(0f, 1f)));

            // Covers the config file being edited outside the game and the F1
            // ConfigurationManager overlay; the in-game slider applies its own value
            // live and lands here as well when OK is pressed.
            BlastVolume.SettingChanged += (sender, args) => HornSound.ApplyVolume();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Log.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
