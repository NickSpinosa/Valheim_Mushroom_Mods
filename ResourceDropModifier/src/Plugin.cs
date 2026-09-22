using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MushroomMods;
using MushroomSync;
using UnityEngine;

namespace ResourceDropModifier
{
    /// <summary>
    /// Per-item drop multipliers, one config entry per item in this plugin's own
    /// .cfg, grouped into a section per biome, and pushed to every client through
    /// MushroomSync. The design is in <c>docs/DESIGN.md</c>.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(MushroomSyncPlugin.PluginGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "Gonfreecss.ResourceDropModifier";
        public const string PluginName = "ResourceDropModifier";
        public const string PluginVersion = "0.1.0";

        /// <summary>
        /// A slider drag fires SettingChanged per frame and an editor writes a file
        /// in several steps, so changes are coalesced into one rebuild this long
        /// after the last one.
        /// </summary>
        private const float RebuildDelaySeconds = 1f;

        /// <summary>
        /// Our own Save() trips the file watcher. Events inside this window after a
        /// save are ours and are ignored.
        /// </summary>
        private const double IgnoreOwnWriteSeconds = 2.0;

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        /// <summary>
        /// Host-side switch: when on, this machine publishes its multipliers to
        /// clients whenever it is the server. Same name and meaning as the setting
        /// in Haldor Expansion and Craftable Spawners.
        /// </summary>
        internal static ConfigEntry<bool> LockConfiguration;

        internal static ConfigEntry<bool> EnableDebugLogging;

        /// <summary>
        /// The multipliers in force on this machine: the server's on a synced client,
        /// this machine's own config entries everywhere else. Swapped whole, never
        /// mutated, so a patch mid-lookup sees one table or the other.
        /// </summary>
        internal static MultiplierTable Table = MultiplierTable.Empty;

        internal static MultiplierSync Sync;

        private Harmony _harmony;
        private FileSystemWatcher _watcher;
        private volatile bool _fileChanged;
        private float _rebuildDue = -1f;
        private DateTime _lastOwnWrite = DateTime.MinValue;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            LockConfiguration = Config.Bind(
                "General", "LockConfiguration", true,
                "If on, the server owns the multipliers and connected clients use the server's values.");

            EnableDebugLogging = Config.Bind(
                "General", "EnableDebugLogging", false,
                "Log every scaled drop. Noisy; for checking a multiplier is applied.");

            // One subscription per file, not per entry: the event is raised once per
            // change whichever entry changed, and a per-entry subscription would
            // fire once for every registered setting on a single change.
            Config.SettingChanged += OnSettingChanged;
            Config.ConfigReloaded += OnConfigReloaded;
            StartWatcher();

            // Sync is wired before the patches so a patch class whose target moved
            // cannot take it down; see Shared/PatchIsolation.cs.
            Sync = new MultiplierSync(PluginGuid, PluginVersion, Log);
            Sync.Start();

            _harmony = new Harmony(PluginGuid);
            int skipped = PatchIsolation.PatchAllIsolated(_harmony, typeof(Plugin).Assembly, Log);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded."
                + (skipped > 0 ? " " + skipped + " patch class(es) skipped - see the errors above." : ""));
        }

        private void OnDestroy()
        {
            _watcher?.Dispose();
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            if (_fileChanged)
            {
                _fileChanged = false;
                if ((DateTime.UtcNow - _lastOwnWrite).TotalSeconds > IgnoreOwnWriteSeconds)
                    ReloadFromDisk();
            }

            if (_rebuildDue >= 0f && Time.unscaledTime >= _rebuildDue)
            {
                _rebuildDue = -1f;
                RebuildTable();
            }
        }

        // ---- world load ---------------------------------------------------------

        /// <summary>
        /// Walks the catalog, binds any new entries, and puts the resulting table in
        /// force. Runs from <c>ZoneSystem.Start</c> on every world load and from
        /// <c>rdm_rescan</c>. Nothing here may throw out into the patch chain.
        /// </summary>
        internal void OnWorldReady()
        {
            try
            {
                ItemIndex.Rebuild(ObjectDB.instance);

                List<CatalogEntry> catalog = DropCatalog.Build(Log);

                _lastOwnWrite = DateTime.UtcNow;
                int bound = DropConfig.BindAll(Config, catalog, Log);
                if (bound > 0)
                    Log.LogInfo("Bound " + bound + " item setting(s) in " + Path.GetFileName(Config.ConfigFilePath)
                        + " (" + DropConfig.Count + " total).");

                RebuildTable();
            }
            catch (Exception ex)
            {
                Log.LogError("Building the drop catalog failed; multipliers from the last successful build stay in force. " + ex);
            }
        }

        /// <summary>
        /// Rebuilds the table from the config entries. Server authority puts it in
        /// force and broadcasts; a client keeps it as the fallback and only uses it
        /// when no server table is active.
        /// </summary>
        private void RebuildTable()
        {
            MultiplierTable table = DropConfig.BuildTable();
            Sync.LocalTable = table;

            if (MultiplierSync.IsServerAuthority())
            {
                Table = table;
                Sync.Broadcast();
                Debug("Table rebuilt: " + table.Count + " entries, " + CountChanged(table) + " not at 1; broadcast.");
            }
            else if (!Sync.ClientSyncActive)
            {
                Table = table;
                Debug("Table rebuilt from local config: " + table.Count + " entries (no server table active).");
            }
        }

        private static int CountChanged(MultiplierTable table)
        {
            int n = 0;
            foreach (KeyValuePair<string, float> entry in table.Entries)
            {
                if (entry.Value != 1f)
                    n++;
            }

            return n;
        }

        // ---- change tracking ----------------------------------------------------

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (args == null || args.ChangedSetting == null)
                return;

            if (DropConfig.IsItemSetting(args.ChangedSetting) || ReferenceEquals(args.ChangedSetting, LockConfiguration))
            {
                // BepInEx saves the file on every set, which trips the watcher.
                _lastOwnWrite = DateTime.UtcNow;
                ScheduleRebuild();
            }
        }

        private void OnConfigReloaded(object sender, EventArgs args)
        {
            ScheduleRebuild();
        }

        private void ScheduleRebuild()
        {
            _rebuildDue = Time.unscaledTime + RebuildDelaySeconds;
        }

        /// <summary>
        /// Re-reads the file. BepInEx raises SettingChanged for each entry that
        /// differs and ConfigReloaded once, both of which schedule a rebuild.
        /// </summary>
        internal string ReloadFromDisk()
        {
            try
            {
                Config.Reload();
                return "Reloaded " + Path.GetFileName(Config.ConfigFilePath) + "; applying in " + RebuildDelaySeconds + "s.";
            }
            catch (Exception ex)
            {
                Log.LogWarning("Config reload failed; keeping the current values. " + ex.Message);
                return "<color=red>Reload failed: " + ex.Message + "</color>";
            }
        }

        /// <summary>
        /// Editing the file on disk raises nothing by itself, and a dedicated server
        /// has no F1 window, so the file is watched. The callback runs on a thread
        /// pool thread; it only sets a flag that Update picks up on the main thread.
        /// </summary>
        private void StartWatcher()
        {
            try
            {
                string directory = Path.GetDirectoryName(Config.ConfigFilePath);
                string file = Path.GetFileName(Config.ConfigFilePath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(file))
                    return;

                _watcher = new FileSystemWatcher(directory, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                _watcher.Changed += (s, e) => _fileChanged = true;
                _watcher.Created += (s, e) => _fileChanged = true;
                _watcher.Renamed += (s, e) => _fileChanged = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                // Some Docker volume drivers and network shares refuse watchers.
                // rdm_reload covers those.
                Log.LogWarning("Could not watch the config file for changes (use rdm_reload instead): " + ex.Message);
                _watcher = null;
            }
        }

        internal static void Debug(string message)
        {
            if (EnableDebugLogging != null && EnableDebugLogging.Value)
                Log.LogInfo(message);
        }
    }
}
