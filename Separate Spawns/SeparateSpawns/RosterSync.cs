using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class RosterSync
    {
        private const string SyncRpcName = "SeparateSpawns.SyncRoster";
        private const string RequestRpcName = "SeparateSpawns.RequestRoster";
        private const string AssignRpcName = "SeparateSpawns.RequestRosterAssignment";

        private static bool _registered;
        private static ZRoutedRpc _registeredInstance;

        public static bool ClientHasRoster { get; private set; }

        private static string _lastAssignmentRequest;
        private static string _lastAppliedPayload;

        public static void ResetClientState()
        {
            ClientHasRoster = false;
            _lastAssignmentRequest = null;
            _lastAppliedPayload = null;
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

            ZRoutedRpc.instance.Register<string>(SyncRpcName, OnReceiveRoster);
            ZRoutedRpc.instance.Register<long>(RequestRpcName, OnRequestRoster);
            ZRoutedRpc.instance.Register<string>(AssignRpcName, OnRequestAssignment);
            _registered = true;
            _registeredInstance = ZRoutedRpc.instance;
        }

        public static void LoadServerRosterFromDisk()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            var roster = GroupRoster.LoadFromDisk();
            Plugin.SetRoster(roster);
            ModLog.Info($"Loaded server roster from {GroupRoster.RosterPath} (write: {GroupRoster.RosterWritePath}).");
            LogRosterSummary();
        }

        /// <param name="direct">
        /// False when the caller has already sent the direct request this pass; one
        /// direct request returns both the roster and the layout.
        /// </param>
        public static void RequestFromServer(bool direct = true, bool quiet = false)
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
                ModLog.Info("Requesting group roster from server...");
            }

            if (direct)
            {
                DirectPeerSync.RequestFromServer(quiet);
            }

            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(RequestRpcName, ZNet.GetUID());
            }
        }

        public static void RequestAssignment(string platformUserId)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || string.IsNullOrEmpty(platformUserId))
            {
                return;
            }

            var normalized = PlatformIdHelper.Normalize(platformUserId);
            if (string.IsNullOrEmpty(normalized) || normalized == _lastAssignmentRequest)
            {
                return;
            }

            _lastAssignmentRequest = normalized;
            ZRoutedRpc.instance.InvokeRoutedRPC(AssignRpcName, normalized);
        }

        public static void Broadcast()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Plugin.Roster == null)
            {
                return;
            }

            var payload = Plugin.Roster.ToJson();
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, SyncRpcName, payload);
        }

        public static void SendToPeer(long peerId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Plugin.Roster == null)
            {
                return;
            }

            var payload = Plugin.Roster.ToJson();
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, SyncRpcName, payload);
        }

        private static void OnRequestRoster(long sender, long peerId)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            if (Plugin.Roster == null || Plugin.Roster.GetGroupNames().Count == 0)
            {
                LoadServerRosterFromDisk();
            }

            if (Plugin.Roster == null)
            {
                ModLog.Warning($"Roster request from peer {sender} ignored; server roster is unavailable.");
                return;
            }

            ModLog.Info($"Sending roster to peer {sender}.");
            var peer = FindPeer(sender);
            if (peer != null)
            {
                DirectPeerSync.SendToPeer(peer);
                return;
            }

            SendToPeer(sender);
        }

        private static void OnRequestAssignment(long sender, string platformUserId)
        {
            if (!ZNet.instance.IsServer() || Plugin.Roster == null || string.IsNullOrWhiteSpace(platformUserId))
            {
                return;
            }

            var normalized = PlatformIdHelper.Normalize(platformUserId);
            var group = Plugin.Roster.GetGroupForPlayer(normalized);
            if (string.IsNullOrEmpty(group))
            {
                group = Plugin.Roster.AssignRandomGroup(normalized);
                ModLog.Info($"Server assigned unlisted player {normalized} to {group}.");
            }
            else
            {
                ModLog.Info($"Server roster lookup: {normalized} -> {group}.");
            }

            SendToPeer(sender);
            Broadcast();
        }

        public static void ApplyPayload(string payload, string source)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                return;
            }

            if (string.IsNullOrEmpty(payload))
            {
                ModLog.Warning($"Ignored empty roster payload from {source}.");
                return;
            }

            // The same roster arrives over both channels and again on every retry while
            // the layout is outstanding. Re-applying it changes nothing, and printing
            // every member of every group each time was most of a client's log.
            if (ClientHasRoster && string.Equals(payload, _lastAppliedPayload, System.StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var roster = GroupRoster.FromJson(payload);
                Plugin.SetRoster(roster);
                ClientHasRoster = true;
                _lastAssignmentRequest = null;
                _lastAppliedPayload = payload;
                ModLog.Info($"Received group roster from server ({source}).");
                LogRosterSummary();
            }
            catch (JsonException ex)
            {
                ModLog.Warning($"Failed to deserialize roster payload from {source}: {ex.Message}");
            }
        }

        private static void OnReceiveRoster(long sender, string payload)
        {
            ApplyPayload(payload, "routed RPC");
        }

        private static ZNetPeer FindPeer(long peerId)
        {
            if (ZNet.instance == null)
            {
                return null;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_uid == peerId)
                {
                    return peer;
                }
            }

            return null;
        }

        private static void LogRosterSummary()
        {
            if (Plugin.Roster == null)
            {
                return;
            }

            ModLog.Info($"Configured groups: {string.Join(", ", Plugin.Roster.GetGroupNames())}");
            foreach (var pair in Plugin.Roster.Groups)
            {
                var players = pair.Value?.Players != null ? string.Join(", ", pair.Value.Players) : string.Empty;
                var difficulty = pair.Value != null && pair.Value.HasDifficulty
                    ? pair.Value.Difficulty.Value.ToString()
                    : "unset";
                ModLog.Info($"  {pair.Key}: [{players}] difficulty={difficulty}");
            }
        }
    }
}
