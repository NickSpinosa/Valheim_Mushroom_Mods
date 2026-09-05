using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MushroomSync;

namespace AudibleHorn
{
    /// <summary>
    /// Entry point for Audible Horn: a craftable Signal Horn whose Horn Call is
    /// heard by every Listener within Hearing Range of the Blower.
    ///
    /// The same DLL runs on the dedicated server, which has no local player, no
    /// AudioListener and no UI, so every code path must tolerate their absence.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(MushroomSyncPlugin.PluginGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mushroom.audiblehorn";
        public const string PluginName = "Audible Horn";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ModConfig Settings;

        /// <summary>
        /// The plugin's own MonoBehaviour, which BepInEx keeps alive for the life of
        /// the process. It is the only component this mod owns, so it is also the only
        /// thing available to run a coroutine on - <see cref="HornCall"/> uses it to
        /// time the self-echo detection window.
        /// </summary>
        internal static Plugin Instance;

        /// <summary>
        /// Server-authoritative settings. Created before <see cref="ModConfig"/>
        /// because binding a setting registers it here.
        /// </summary>
        internal static ConfigSync Sync;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Sync = ConfigSync.Create(PluginGuid, PluginVersion, Logger)
                .Protecting(Config);

            Settings = new ModConfig(Config);

            // Deliberately no GatedBy and no AcceptedWhen. Hearing Range and Horn
            // Cooldown are not a matter of taste: the host publishes them and every
            // client follows, always. Two players disagreeing on Hearing Range means
            // one hears a Horn Call the other believes it never sent, and a client
            // that kept its own Cooldown could sound the horn more often than the
            // server allows. If you are here to add an opt-out, that is why there
            // isn't one. Horn Volume stays personal instead - see ModConfig.
            Sync.WatchForChanges(Config)
                .Start();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
