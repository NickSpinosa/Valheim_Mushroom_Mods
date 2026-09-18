using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    /// <summary>
    /// Server/client sync over peer ZRpc (same channel as RemotePrint, PlayerList, etc.).
    /// ZRoutedRpc alone was not reliably delivering payloads to joining clients.
    /// </summary>
    internal static class DirectPeerSync
    {
        private const string RequestRpc = "SeparateSpawns.RequestDirectSync";
        private const string SyncRosterRpc = "SeparateSpawns.SyncRosterDirect";
        private const string SyncLayoutRpc = "SeparateSpawns.SyncLayoutDirect";

        public static void RegisterClientHandlers(ZRpc serverRpc)
        {
            if (serverRpc == null)
            {
                return;
            }

            serverRpc.Register<string>(SyncRosterRpc, (_, json) => RosterSync.ApplyPayload(json, "direct ZRpc"));
            serverRpc.Register<string>(SyncLayoutRpc, (_, json) => LayoutSync.ApplyPayload(json, "direct ZRpc"));
            ModLog.Info("Registered direct Separate Spawns sync handlers on server connection.");
        }

        public static void RegisterServerPeer(ZNetPeer peer)
        {
            if (peer?.m_rpc == null)
            {
                return;
            }

            peer.m_rpc.Register(RequestRpc, _ => SendToPeer(peer));
        }

        // Peers already told about, so a client that keeps asking produces one line,
        // not one per request. Per world: cleared by WorldBootstrap.Shutdown.
        private static readonly HashSet<long> RosterSentLogged = new HashSet<long>();
        private static readonly HashSet<long> LayoutUnavailableWarned = new HashSet<long>();

        public static void ResetServerLogState()
        {
            RosterSentLogged.Clear();
            LayoutUnavailableWarned.Clear();
        }

        /// <param name="quiet">
        /// Set by the retry loop on every pass after the first, which would otherwise
        /// repeat the same two lines for as long as the server has nothing to send.
        /// </param>
        public static void RequestFromServer(bool quiet = false)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || !ClientSyncHelper.CanReachServer())
            {
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_server && peer.m_rpc != null && peer.m_rpc.IsConnected())
                {
                    if (!quiet)
                    {
                        ModLog.Info("Requesting Separate Spawns sync via direct ZRpc...");
                    }

                    peer.m_rpc.Invoke(RequestRpc);
                    return;
                }
            }

            if (!quiet)
            {
                ModLog.Warning("Could not find connected server peer for direct Separate Spawns sync.");
            }
        }

        public static void SendToPeer(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || !peer.m_rpc.IsConnected() || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            if (Plugin.Roster == null || Plugin.Roster.GetGroupNames().Count == 0)
            {
                RosterSync.LoadServerRosterFromDisk();
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

            if (Plugin.Roster != null)
            {
                peer.m_rpc.Invoke(SyncRosterRpc, Plugin.Roster.ToJson());
                if (RosterSentLogged.Add(peer.m_uid))
                {
                    ModLog.Info($"Sent roster to peer {peer.m_uid} via direct ZRpc.");
                }
            }
            else
            {
                ModLog.Warning($"Direct roster sync skipped for peer {peer.m_uid}; server roster unavailable.");
            }

            if (Plugin.LayoutCache.Current != null)
            {
                var payload = JsonConvert.SerializeObject(Plugin.LayoutCache.Current, JsonSettings.Compact);
                peer.m_rpc.Invoke(SyncLayoutRpc, payload);
                ModLog.Info(
                    $"Sent layout to peer {peer.m_uid} via direct ZRpc ({Plugin.LayoutCache.Current.GroupSpawnPositions.Count} spawns).");
            }
            else if (LayoutUnavailableWarned.Add(peer.m_uid))
            {
                ModLog.Warning(
                    $"Direct layout sync skipped for peer {peer.m_uid}; server layout unavailable. " +
                    "The peer will keep asking; this is logged once per peer.");
            }
        }
    }
}
