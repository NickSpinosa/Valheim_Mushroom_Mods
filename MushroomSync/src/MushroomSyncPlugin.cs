using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MushroomSync
{
    /// <summary>
    /// Installs the shared patches the sync channels rely on.
    ///
    /// This plugin holds no game behaviour of its own. It exists so the ZNet
    /// handshake and the ConfigEntry value overlay are patched exactly once per
    /// process, however many Mushroom mods are installed. Mods depend on it with
    /// <c>[BepInDependency(MushroomSyncPlugin.PluginGuid)]</c>, which also makes
    /// BepInEx load it first.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class MushroomSyncPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mushroom.sync";
        public const string PluginName = "Mushroom Sync";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            _harmony = new Harmony(PluginGuid);
            ChannelRegistry.Initialize(_harmony, Logger);
            ConfigValueOverlay.Initialize(_harmony, Logger);

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }
}
