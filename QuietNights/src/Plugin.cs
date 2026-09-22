using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MushroomMods;
using MushroomSync;

namespace QuietNights
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(MushroomSyncPlugin.PluginGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mushroom.quietnights";
        public const string PluginName = "QuietNights";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// Server-authoritative settings. The spawn roll for a zone runs on whichever
        /// <em>client</em> owns that zone's ZDO, never on a dedicated server, so a rule
        /// that lived only in the server's config file would suppress nothing. Sync is
        /// what makes the host's choice the one every zone owner applies. See
        /// docs/DESIGN.md.
        /// </summary>
        internal static ConfigSync Sync;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Creatures;
        internal static ConfigEntry<string> BossKeys;

        private Harmony _harmony;

        private static Rules _rules;
        private static string _rulesSource;

        /// <summary>
        /// Reads go through the sync overlay, so this is the host's list on a client.
        /// Re-parsed only when the text changes, so a typo is reported once rather than
        /// on every zone load.
        /// </summary>
        internal static Rules CurrentRules()
        {
            string source = Creatures.Value + "\n" + BossKeys.Value;
            if (_rules == null || source != _rulesSource)
            {
                _rules = Rules.Parse(Creatures.Value, BossKeys.Value);
                _rulesSource = source;
            }

            return _rules;
        }

        private void Awake()
        {
            Log = Logger;

            // OnApplied covers both directions - host values arriving, and being
            // dropped on disconnect. The second is the restore path.
            Sync = ConfigSync.Create(PluginGuid, PluginVersion, Log)
                .Protecting(Config)
                .OnApplied(SpawnSuppressor.Apply);

            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Master switch. When off, every night spawn behaves as in the unmodded game. " +
                "Set on the server; connected clients follow it.");

            Creatures = Config.Bind(
                "Rules", "Creatures", "Goblin*:Plains, Seeker*:Mistlands, Charred*:AshLands",
                "Creatures whose boss-unlocked night spawns are removed everywhere except at home. " +
                "Comma-separated Prefab:Biome pairs. A trailing * matches a family of prefabs " +
                "(Goblin* covers Goblin, GoblinArcher, GoblinShaman, GoblinBrute). Join several home " +
                "biomes with +, as in Plains+Meadows. Run 'quietnights dump' in the console to see the " +
                "real prefab names and what each rule catches.");

            BossKeys = Config.Bind(
                "Rules", "BossKeys", "defeated_*",
                "Global keys that count as a boss kill. Only spawn entries requiring one of these are " +
                "touched. Comma-separated; a trailing * matches a prefix.");

            Sync.Register(Enabled, Creatures, BossKeys);

            // The host and single-player: no sync message ever arrives for them.
            Enabled.SettingChanged += (sender, args) => SpawnSuppressor.Apply();
            Creatures.SettingChanged += (sender, args) => SpawnSuppressor.Apply();
            BossKeys.SettingChanged += (sender, args) => SpawnSuppressor.Apply();

            Sync.WatchForChanges(Config).Start();

            // Per class, so one moved patch target cannot unwind the rest of Awake.
            // See Shared/PatchIsolation.cs.
            _harmony = new Harmony(PluginGuid);
            int skipped = PatchIsolation.PatchAllIsolated(_harmony, typeof(Plugin).Assembly, Log);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded."
                + (skipped > 0 ? " " + skipped + " patch class(es) skipped - see the errors above." : ""));
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
