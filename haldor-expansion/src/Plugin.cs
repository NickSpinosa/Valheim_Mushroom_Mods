using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MushroomSync;

namespace HaldorExpansion
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInDependency(MushroomSyncPlugin.PluginGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginId = "nicks.haldorexpansion";
        public const string PluginName = "Haldor Expansion";
        public const string PluginVersion = "0.4.0";

        internal static ManualLogSource Log;
        internal static ModConfig Settings;

        /// <summary>
        /// Server-authoritative item settings. Created before <see cref="ModConfig"/>
        /// because binding a setting registers it here.
        /// </summary>
        internal static ConfigSync Sync;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Sync = ConfigSync.Create(PluginId, PluginVersion, Logger)
                .Protecting(Config)
                .OnApplied(() =>
                {
                    SuperMistTorch.RefreshFromConfig();
                    Log.LogInfo("Trade table hash: " + TradeTable.Hash);
                });

            Settings = new ModConfig(Config);

            // LockConfiguration is a host-side switch: it decides whether this
            // machine publishes its item settings when it is the server. It is
            // deliberately not an accept-side gate - a client with it off still
            // follows a host that has it on, which is what the old in-mod sync did
            // and what docs/DESIGN.md describes. It is never itself synced, so a
            // client keeps its own answer.
            Sync.GatedBy(() => Settings == null || Settings.LockConfiguration)
                .WatchForChanges(Config)
                .Start();

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Log.LogInfo("Trade table hash: " + TradeTable.Hash
                        + " -- on a server, clients should match this after config sync.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
