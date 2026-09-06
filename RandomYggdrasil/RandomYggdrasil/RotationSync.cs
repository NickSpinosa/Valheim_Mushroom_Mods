using System;
using System.Collections.Generic;
using BepInEx.Logging;
using MushroomSync;
using UnityEngine;

namespace RandomYggdrasil
{
    /// <summary>
    /// World rotations, pushed from the server to clients.
    ///
    /// The handshake, versioning and peer lifecycle live in
    /// <see cref="SyncChannel"/>; all that is left here is what a rotation looks like
    /// on the wire. This used to be ~280 lines of transport copied from the other
    /// mods - the transport was never rotation-specific, only the payload was.
    /// </summary>
    internal static class RotationSync
    {
        private const int DegreesInCircle = 360;

        private static readonly Dictionary<string, int> ClientSyncedRotations =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static SyncChannel _channel;

        /// <summary>True on a client currently holding server rotations.</summary>
        internal static bool HasReceivedSync
        {
            get { return _channel != null && _channel.ClientSyncActive; }
        }

        internal static bool IsServerAuthority()
        {
            return SyncChannel.IsServerAuthority();
        }

        internal static bool TryGetSyncedRotation(string worldIdentifier, out int degrees)
        {
            if (HasReceivedSync && ClientSyncedRotations.TryGetValue(worldIdentifier, out degrees))
                return IsValid(degrees);

            degrees = -1;
            return false;
        }

        internal static void Initialize(ManualLogSource log)
        {
            _channel = SyncChannel.Create(
                "RandomYggdrasil.Rotations", RandomYggdrasilMod.PluginVersion, log);

            _channel.WritePayload = WritePayload;
            _channel.ReadPayload = ReadPayload;
            _channel.Cleared = OnCleared;
            _channel.Start();
        }

        /// <summary>Pushes current rotations to clients. No-op unless this is the server.</summary>
        internal static void Broadcast()
        {
            if (_channel != null)
                _channel.Broadcast();
        }

        private static bool IsValid(int degrees)
        {
            return degrees >= 0 && degrees < DegreesInCircle;
        }

        private static void WritePayload(ZPackage payload)
        {
            // A client that has never generated its own rotation would otherwise send
            // an empty set the moment it hosts.
            RandomYggdrasilMod.EnsureCurrentWorldRotation();

            Dictionary<string, int> snapshot = RandomYggdrasilMod.GetRotationSnapshot();
            payload.Write(snapshot.Count);
            foreach (KeyValuePair<string, int> pair in snapshot)
            {
                payload.Write(pair.Key);
                payload.Write(pair.Value);
            }
        }

        private static void ReadPayload(ZPackage payload)
        {
            int count = SyncChannel.ReadCount(payload);

            ClientSyncedRotations.Clear();
            for (int i = 0; i < count; i++)
            {
                string worldIdentifier = payload.ReadString();
                int degrees = payload.ReadInt();
                if (IsValid(degrees) && !string.IsNullOrEmpty(worldIdentifier))
                    ClientSyncedRotations[worldIdentifier] = degrees;
            }

            Debug.Log("RandomYggdrasil: received " + ClientSyncedRotations.Count
                      + " world rotation(s) from server");
            RandomYggdrasilMod.TryApplyStoredRotation();
        }

        private static void OnCleared(string reason)
        {
            ClientSyncedRotations.Clear();
            Debug.Log("RandomYggdrasil: server rotations dropped (" + reason + ")");
        }
    }
}
