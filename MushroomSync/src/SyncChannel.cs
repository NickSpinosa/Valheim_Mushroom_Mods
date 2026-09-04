using System;
using BepInEx.Logging;

namespace MushroomSync
{
    /// <summary>
    /// A server-authoritative one-way push of arbitrary data to clients, over the
    /// peer <see cref="ZRpc"/>.
    ///
    /// The channel owns the handshake and the peer lifecycle; the caller owns the
    /// payload. That split is what lets config sync and Random Yggdrasil's world
    /// rotations share one implementation - before this they were the same 300
    /// lines copied four times, differing only in what went into the package.
    ///
    /// Deliberately does not wrap login sockets the way ServerSync did, so it
    /// survives game updates that change the socket layer.
    /// </summary>
    public sealed class SyncChannel
    {
        /// <summary>
        /// Wire format version. Bump only for a change the version handshake
        /// cannot already catch - mod versions are compared per channel, so a mod
        /// changing its own payload shape needs no bump here.
        /// </summary>
        public const int ProtocolVersion = 2;

        private const int DeferFrames = 2;
        private const int MaxEntries = 10000;

        private readonly string _id;
        private readonly string _version;
        private readonly ManualLogSource _log;

        private bool _clientSyncActive;
        private bool _started;

        private SyncChannel(string id, string version, ManualLogSource log)
        {
            _id = id;
            _version = version ?? string.Empty;
            _log = log;
        }

        /// <summary>Channel id, also the RPC name prefix. Must be unique per mod.</summary>
        public string Id => _id;

        /// <summary>RPC the server invokes on a client to deliver a payload.</summary>
        public string RpcSync => _id + ".Sync";

        /// <summary>RPC a client invokes on the server to ask for one.</summary>
        public string RpcSyncRequest => _id + ".SyncRequest";

        /// <summary>True on a client that currently holds server-sent data.</summary>
        public bool ClientSyncActive => _clientSyncActive;

        /// <summary>
        /// Writes the payload to send. Called on the server only. A channel with no
        /// writer sends nothing.
        /// </summary>
        public Action<ZPackage> WritePayload { get; set; }

        /// <summary>
        /// Reads a payload received from the server. Called on clients only. Throwing
        /// from here is treated as a corrupt payload: the client falls back to local
        /// state and <see cref="Cleared"/> fires.
        /// </summary>
        public Action<ZPackage> ReadPayload { get; set; }

        /// <summary>
        /// Client-side fallback to local state, with the reason: "disconnect",
        /// "network shutdown", "version mismatch", "invalid payload", or a caller
        /// supplied reason. Fires only when a sync was actually active.
        /// </summary>
        public Action<string> Cleared { get; set; }

        /// <summary>
        /// Gates sending, on the server. Return false and the host publishes
        /// nothing - a host-side switch such as haldor-expansion's
        /// LockConfiguration. Defaults to always sending.
        /// </summary>
        public Func<bool> SendGate { get; set; }

        /// <summary>
        /// Gates accepting, on a client. Return false and incoming payloads are
        /// ignored and any current sync dropped - a client-side opt-out such as
        /// Combat Adjustments' SyncConfigInMultiplayer. Defaults to always
        /// accepting.
        ///
        /// Deliberately separate from <see cref="SendGate"/>: a host-side switch
        /// that also silently refused incoming values would stop a client
        /// following a server just because that client would not have shared its
        /// own settings when hosting.
        /// </summary>
        public Func<bool> AcceptGate { get; set; }

        /// <summary>
        /// True when this side decides its own values: a dedicated server, a host,
        /// or single-player. The common guard before applying a local change.
        /// </summary>
        public static bool IsServerAuthority()
        {
            ZNet net = ZNet.instance;
            return net == null || net.IsServer();
        }

        public static SyncChannel Create(string id, string version, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A sync channel needs an id.", nameof(id));
            if (log == null)
                throw new ArgumentNullException(nameof(log));

            return new SyncChannel(id, version, log);
        }

        /// <summary>
        /// Starts listening. Idempotent. The ZNet patches themselves are installed
        /// once by the MushroomSync plugin, not per channel.
        /// </summary>
        public SyncChannel Start()
        {
            if (_started)
                return this;

            _started = true;
            ChannelRegistry.Add(this);
            return this;
        }

        /// <summary>
        /// Sends the current payload to every connected, ready peer. Server-side;
        /// a no-op elsewhere. Call after changing something clients should see.
        /// </summary>
        public void Broadcast()
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer())
                return;

            foreach (ZNetPeer peer in net.GetConnectedPeers())
            {
                if (peer != null && peer.IsReady() && peer.m_rpc != null)
                    SendTo(peer.m_rpc);
            }
        }

        /// <summary>Drops server-sent data and falls back to local state.</summary>
        public void ClearClientState(string reason)
        {
            if (!_clientSyncActive)
                return;

            _clientSyncActive = false;
            InvokeCleared(reason);
        }

        // ---- called by ChannelRegistry ----------------------------------------

        internal void RegisterOn(ZRpc rpc)
        {
            try
            {
                rpc.Register<ZPackage>(RpcSync, ReceiveSync);
                rpc.Register<ZPackage>(RpcSyncRequest, ReceiveSyncRequest);
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": RPC registration failed: " + ex.Message);
            }
        }

        internal void SendTo(ZRpc rpc)
        {
            if (rpc == null || !rpc.IsConnected())
                return;
            if (WritePayload == null)
                return;
            if (SendGate != null && !SendGate())
                return;

            try
            {
                rpc.Invoke(RpcSync, BuildEnvelope());
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": send failed: " + ex.Message);
            }
        }

        internal void RequestFromServer()
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer())
                return;

            ZRpc rpc = net.GetServerRPC();
            if (rpc == null || !rpc.IsConnected())
                return;

            try
            {
                var pkg = new ZPackage();
                pkg.Write(ProtocolVersion);
                pkg.Write(_version);
                rpc.Invoke(RpcSyncRequest, pkg);
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": sync request failed: " + ex.Message);
            }
        }

        internal int DeferredFrames => DeferFrames;

        // ---- wire -------------------------------------------------------------

        private ZPackage BuildEnvelope()
        {
            var envelope = new ZPackage();
            envelope.Write(ProtocolVersion);
            envelope.Write(_version);

            var payload = new ZPackage();
            WritePayload(payload);

            // Compressed because config payloads are highly repetitive strings, and
            // this runs during login when the connection is busiest.
            envelope.WriteCompressed(payload);
            return envelope;
        }

        private void ReceiveSyncRequest(ZRpc rpc, ZPackage request)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || rpc == null || request == null)
                return;

            try
            {
                int guestProtocol = request.ReadInt();
                string guestVersion = request.ReadString();
                if (guestProtocol != ProtocolVersion
                    || !string.Equals(guestVersion, _version, StringComparison.Ordinal))
                {
                    _log.LogWarning(
                        _id + ": version mismatch for a guest (guest " + guestVersion
                        + " / protocol " + guestProtocol + "; host " + _version
                        + " / protocol " + ProtocolVersion + "). Sending host values anyway.");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": sync request read failed: " + ex.Message);
            }

            SendTo(rpc);
        }

        private void ReceiveSync(ZRpc rpc, ZPackage envelope)
        {
            ZNet net = ZNet.instance;

            // Only ever accept from the server we are connected to.
            if (net == null || net.IsServer() || rpc == null || envelope == null || net.GetServerRPC() != rpc)
                return;

            if (ReadPayload == null)
                return;

            if (AcceptGate != null && !AcceptGate())
            {
                ClearClientState("declined locally");
                return;
            }

            try
            {
                int protocol = envelope.ReadInt();
                string hostVersion = envelope.ReadString();
                if (protocol != ProtocolVersion
                    || !string.Equals(hostVersion, _version, StringComparison.Ordinal))
                {
                    ClearClientState("version mismatch");
                    _log.LogWarning(
                        _id + ": host mod version differs (" + hostVersion + " vs " + _version
                        + "). Using local values.");
                    return;
                }

                ZPackage payload = envelope.ReadCompressedPackage();

                // Set before the read, not after. Callers apply values from inside
                // ReadPayload - baking them into ObjectDB, say - and both the
                // ConfigEntry overlay and TryGetSyncedValue gate on this flag.
                // Setting it afterwards means the first payload after a join is
                // applied while entry.Value still returns the client's own config,
                // leaving that baked state wrong until some later broadcast.
                _clientSyncActive = true;
                ReadPayload(payload);
            }
            catch (Exception ex)
            {
                // Roll back. Because the flag goes up first, a throw part-way
                // through can leave the caller holding a mix of host and local
                // values, so clear unconditionally rather than only when a sync was
                // already established.
                _clientSyncActive = false;
                InvokeCleared("invalid payload");
                _log.LogWarning(_id + ": sync failed: " + ex.Message);
            }
        }

        private void InvokeCleared(string reason)
        {
            if (Cleared == null)
                return;

            try
            {
                Cleared(reason);
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": cleared handler failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Guard for caller payload loops. A corrupt length prefix would otherwise
        /// allocate without bound before the read failed.
        /// </summary>
        public static int ReadCount(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            if (count < 0 || count > MaxEntries)
                throw new InvalidOperationException("Invalid sync entry count: " + count);
            return count;
        }
    }
}
