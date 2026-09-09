using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MushroomMods;
using UnityEngine;

namespace SeparateSpawns
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "abortipus.separatespawns";
        public const string PluginName = "Separate Spawns";
        public const string PluginVersion = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static ModConfig ConfigValues { get; private set; }
        internal static GroupRoster Roster { get; private set; } = GroupRoster.CreateEmpty();
        internal static WorldLayoutCache LayoutCache { get; private set; }

        private Harmony _harmony;
        private ConfigFile _modConfigFile;

        private void Awake()
        {
            Instance = this;
            var configPath = ModPaths.GetPluginConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            _modConfigFile = new ConfigFile(configPath, true);
            ConfigValues = ModConfig.Bind(_modConfigFile);
            LayoutCache = new WorldLayoutCache();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
            Logger.LogInfo($"Config layout: {(ModPaths.UseDedicatedConfigLayout() ? "dedicated server" : "client")}");
            Logger.LogInfo($"Config file: {configPath}");
            Logger.LogInfo($"Client config root: {ModPaths.GetClientConfigRoot()}");
            Logger.LogInfo($"Dedicated config root: {ModPaths.GetDedicatedConfigRoot()}");
            Logger.LogInfo($"Default config root: {ModPaths.GetDefaultConfigRoot()}");
            Logger.LogInfo($"Server roster path: {GroupRoster.RosterPath} (write: {GroupRoster.RosterWritePath})");

            StartCoroutine(InitializeRosterAuthority());
            StartCoroutine(LogLocalPlatformIdWhenReady());
            StartCoroutine(ClientSyncHelper.RunSyncRetry(this));

            // Last, and per class. The roster and sync coroutines above have to start
            // whatever the patches do. See Shared/PatchIsolation.cs.
            _harmony = new Harmony(PluginGuid);
            int skipped = PatchIsolation.PatchAllIsolated(_harmony, typeof(Plugin).Assembly, Logger);
            if (skipped > 0)
            {
                Logger.LogWarning($"{skipped} patch class(es) skipped - see the errors above.");
            }
        }

        internal static void SetRoster(GroupRoster roster)
        {
            Roster = roster ?? GroupRoster.CreateEmpty();
        }

        private System.Collections.IEnumerator InitializeRosterAuthority()
        {
            while (ZRoutedRpc.instance == null || ZNet.instance == null)
            {
                yield return null;
            }

            RosterSync.Register();
            PortalActivationSync.Register();

            if (ZNet.instance.IsServer())
            {
                RosterSync.LoadServerRosterFromDisk();
            }
            else
            {
                ModLog.Info("Client mode: roster and layout will sync after server connection.");
            }
        }

        private System.Collections.IEnumerator LogLocalPlatformIdWhenReady()
        {
            while (ZNet.instance == null)
            {
                yield return null;
            }

            if (PlatformIdHelper.IsHeadlessServerContext())
            {
                Logger.LogInfo("Dedicated server: no local platform user id (roster uses connected player Steam IDs).");
                yield break;
            }

            for (var i = 0; i < 120; i++)
            {
                var id = PlatformIdHelper.GetLocalPlatformUserId();
                if (!string.IsNullOrEmpty(id))
                {
                    Logger.LogInfo($"Local platform user id: {id}");
                    yield break;
                }

                yield return new WaitForSeconds(1f);
            }

            Logger.LogWarning("Local platform user id was still empty after waiting; group roster matching may fail.");
        }

        private System.Collections.IEnumerator Start()
        {
            while (ZoneSystem.instance == null)
            {
                yield return null;
            }

            WorldBootstrap.Initialize(this);
        }

        internal void LogInfo(string message) => Logger.LogInfo(message);
        internal void LogWarning(string message) => Logger.LogWarning(message);
        internal void LogError(string message) => Logger.LogError(message);

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            WorldBootstrap.Shutdown();
        }
    }
}
