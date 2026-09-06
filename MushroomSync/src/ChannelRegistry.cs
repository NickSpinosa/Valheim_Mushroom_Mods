using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace MushroomSync
{
    /// <summary>
    /// Owns the ZNet patches on behalf of every <see cref="SyncChannel"/>.
    ///
    /// The point of doing this once: before the mods shared this code, four of them
    /// each patched the same four ZNet methods, so a four-mod server ran four copies
    /// of an identical handshake on every connection.
    /// </summary>
    internal static class ChannelRegistry
    {
        private static readonly List<SyncChannel> Channels = new List<SyncChannel>();
        private static readonly object Gate = new object();

        private static ManualLogSource _log;
        private static bool _patched;

        internal static void Initialize(Harmony harmony, ManualLogSource log)
        {
            _log = log;

            if (_patched)
                return;

            Patch(harmony, AccessTools.Method(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }),
                postfix: nameof(ZNetOnNewConnectionPostfix), required: true);
            Patch(harmony, AccessTools.Method(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }),
                postfix: nameof(ZNetRpcPeerInfoPostfix), required: true);
            Patch(harmony, AccessTools.Method(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) }),
                postfix: nameof(ZNetDisconnectPostfix), required: false);
            Patch(harmony, AccessTools.Method(typeof(ZNet), "OnDestroy", Type.EmptyTypes),
                prefix: nameof(ZNetOnDestroyPrefix), required: false);

            _patched = true;
        }

        internal static void Add(SyncChannel channel)
        {
            lock (Gate)
            {
                foreach (SyncChannel existing in Channels)
                {
                    if (string.Equals(existing.Id, channel.Id, StringComparison.Ordinal))
                    {
                        _log?.LogWarning(
                            "Two sync channels share the id '" + channel.Id
                            + "'. The second is ignored; give each mod its own id.");
                        return;
                    }
                }

                Channels.Add(channel);
            }
        }

        private static SyncChannel[] Snapshot()
        {
            lock (Gate)
            {
                return Channels.ToArray();
            }
        }

        private static void Patch(Harmony harmony, MethodInfo method, string postfix = null,
                                  string prefix = null, bool required = false)
        {
            if (method == null)
            {
                // Named so a game update that renames one of these is diagnosable
                // from the log rather than showing up as sync silently never firing.
                string what = postfix ?? prefix;
                if (required)
                    _log?.LogError("MushroomSync: could not find the ZNet method for " + what + ". Sync will not work.");
                else
                    _log?.LogWarning("MushroomSync: could not find the ZNet method for " + what + ".");
                return;
            }

            harmony.Patch(
                method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(ChannelRegistry), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(ChannelRegistry), postfix));
        }

        /// <summary>
        /// Defers network work out of <see cref="ZNet.RPC_PeerInfo"/> so RPCs are not
        /// invoked while another mod is still wrapping the socket during login.
        /// </summary>
        private static IEnumerator DeferFrames(int frameCount, Action action)
        {
            for (int i = 0; i < frameCount; i++)
                yield return null;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log?.LogWarning("MushroomSync: deferred action failed: " + ex.Message);
            }
        }

        public static void ZNetOnNewConnectionPostfix(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null)
                return;

            foreach (SyncChannel channel in Snapshot())
                channel.RegisterOn(peer.m_rpc);
        }

        public static void ZNetRpcPeerInfoPostfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || rpc == null)
                return;

            SyncChannel[] channels = Snapshot();
            if (channels.Length == 0)
                return;

            if (__instance.IsServer())
            {
                __instance.StartCoroutine(DeferFrames(channels[0].DeferredFrames, () =>
                {
                    foreach (SyncChannel channel in channels)
                        channel.SendTo(rpc);
                }));
            }
            else if (__instance.GetServerRPC() == rpc)
            {
                __instance.StartCoroutine(DeferFrames(channels[0].DeferredFrames, () =>
                {
                    foreach (SyncChannel channel in channels)
                        channel.RequestFromServer();
                }));
            }
        }

        public static void ZNetDisconnectPostfix(ZNet __instance, ZNetPeer peer)
        {
            if (__instance == null || __instance.IsServer() || peer == null || !peer.m_server)
                return;

            foreach (SyncChannel channel in Snapshot())
                channel.ClearClientState("disconnect");
        }

        public static void ZNetOnDestroyPrefix(ZNet __instance)
        {
            if (__instance == null || __instance.IsServer())
                return;

            foreach (SyncChannel channel in Snapshot())
                channel.ClearClientState("network shutdown");
        }
    }
}
