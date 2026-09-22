using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class LayoutSync
    {
        private const string RpcName = "SeparateSpawns.SyncLayout";
        private static bool _registered;
        private static ZRoutedRpc _registeredInstance;

        // One warning per peer while the server has no layout; see DirectPeerSync.
        private static readonly System.Collections.Generic.HashSet<long> UnavailableWarned =
            new System.Collections.Generic.HashSet<long>();

        public static void ResetServerLogState()
        {
            UnavailableWarned.Clear();
        }

        public static void Register()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            if (_registered && ReferenceEquals(_registeredInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register<string>(RpcName, OnReceiveLayout);
            ZRoutedRpc.instance.Register<long>("SeparateSpawns.RequestLayout", OnRequestLayout);
            _registered = true;
            _registeredInstance = ZRoutedRpc.instance;
        }

        private static void OnRequestLayout(long sender, long peerId)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            if (Plugin.LayoutCache.Current == null)
            {
                var worldUid = ZNet.instance.GetWorldUID();
                if (worldUid != 0)
                {
                    var existing = WorldLayoutStore.Load(worldUid);
                    if (existing != null)
                    {
                        Plugin.LayoutCache.Set(existing);
                    }
                }
            }

            if (Plugin.LayoutCache.Current == null)
            {
                if (UnavailableWarned.Add(sender))
                {
                    ModLog.Warning($"Layout request from peer {sender} ignored; server layout is unavailable.");
                }

                return;
            }

            ModLog.Info($"Sending layout to peer {sender} ({Plugin.LayoutCache.Current.GroupSpawnPositions.Count} spawns).");
            SendToPeer(sender, Plugin.LayoutCache.Current);
        }

        /// <param name="direct">
        /// False when the caller has already sent the direct request this pass; one
        /// direct request returns both the roster and the layout.
        /// </param>
        public static void RequestLayoutFromServer(bool direct = true, bool quiet = false)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            if (!ClientSyncHelper.CanReachServer())
            {
                return;
            }

            if (!quiet)
            {
                ModLog.Info("Requesting world layout from server...");
            }

            if (direct)
            {
                DirectPeerSync.RequestFromServer(quiet);
            }

            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC("SeparateSpawns.RequestLayout", ZNet.GetUID());
            }
        }

        public static void Broadcast(WorldLayoutData layoutData)
        {
            if (!ZNet.instance.IsServer() || layoutData == null)
            {
                return;
            }

            SendToAll(layoutData);
        }

        public static void SendToPeer(long peerId, WorldLayoutData layoutData)
        {
            if (!ZNet.instance.IsServer() || layoutData == null)
            {
                return;
            }

            var payload = JsonConvert.SerializeObject(layoutData, JsonSettings.Compact);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcName, payload);
        }

        private static void SendToAll(WorldLayoutData layoutData)
        {
            var payload = JsonConvert.SerializeObject(layoutData, JsonSettings.Compact);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, payload);
        }

        public static void ApplyPayload(string payload, string source)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && Plugin.LayoutCache.Current != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload))
            {
                ModLog.Warning($"Ignored empty layout payload from {source}.");
                return;
            }

            try
            {
                var layout = JsonConvert.DeserializeObject<WorldLayoutData>(payload, JsonSettings.Compact);
                if (layout != null)
                {
                    Plugin.LayoutCache.Set(layout);
                    ModLog.Info(
                        $"Received world layout from server ({source}, {layout.GroupSpawnPositions.Count} group spawns).");
                }
            }
            catch (JsonException ex)
            {
                ModLog.Warning($"Failed to deserialize layout payload from {source}: {ex.Message}");
            }
        }

        private static void OnReceiveLayout(long sender, string payload)
        {
            ApplyPayload(payload, "routed RPC");
        }
    }
}
