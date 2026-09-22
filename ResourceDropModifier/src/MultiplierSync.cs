using System.Collections.Generic;
using BepInEx.Logging;
using MushroomSync;

namespace ResourceDropModifier
{
    /// <summary>
    /// Pushes the server's multiplier table to clients over a raw MushroomSync
    /// channel. Drops are generated on whichever peer owns the dying creature or
    /// broken rock, which is usually a client, so every client needs the table -
    /// a server-only patch would only scale drops near the server's own player.
    /// <para>
    /// Only entries other than 1 travel; the receiving side treats anything missing
    /// as 1. On disconnect or version mismatch the client falls back to its own
    /// config entries.
    /// </para>
    /// <para>
    /// A raw channel rather than MushroomSync's config sync, although the values are
    /// config entries: config sync discards any path the client has not bound, and
    /// the client binds its entries from its own catalog walk during world load,
    /// which can be after the host's payload arrives. See "Why not config sync" in
    /// docs/DESIGN.md.
    /// </para>
    /// </summary>
    internal sealed class MultiplierSync
    {
        private readonly SyncChannel _channel;
        private readonly ManualLogSource _log;

        /// <summary>The table built from this machine's own config entries, kept for the fallback.</summary>
        internal MultiplierTable LocalTable = MultiplierTable.Empty;

        internal MultiplierSync(string pluginGuid, string pluginVersion, ManualLogSource log)
        {
            _log = log;
            _channel = SyncChannel.Create(pluginGuid + ".Multipliers", pluginVersion, log);
            _channel.WritePayload = Write;
            _channel.ReadPayload = Read;
            _channel.Cleared = OnCleared;
            _channel.SendGate = () => Plugin.LockConfiguration == null || Plugin.LockConfiguration.Value;
        }

        internal bool ClientSyncActive => _channel.ClientSyncActive;

        internal static bool IsServerAuthority() => SyncChannel.IsServerAuthority();

        internal void Start() => _channel.Start();

        /// <summary>Server: call after the table is rebuilt from config.</summary>
        internal void Broadcast() => _channel.Broadcast();

        private void Write(ZPackage pkg)
        {
            var entries = new List<KeyValuePair<string, float>>();
            foreach (KeyValuePair<string, float> entry in Plugin.Table.Entries)
            {
                if (entry.Value != 1f)
                    entries.Add(entry);
            }

            pkg.Write(entries.Count);
            foreach (KeyValuePair<string, float> entry in entries)
            {
                pkg.Write(entry.Key);
                pkg.Write(entry.Value);
            }
        }

        private void Read(ZPackage pkg)
        {
            int count = SyncChannel.ReadCount(pkg);
            var byPrefab = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                string prefab = pkg.ReadString();
                float multiplier = pkg.ReadSingle();
                byPrefab[prefab] = multiplier;
            }

            Plugin.Table = new MultiplierTable(byPrefab);
            _log.LogInfo("Using " + count + " drop multiplier(s) from the server.");
        }

        private void OnCleared(string reason)
        {
            Plugin.Table = LocalTable;
            _log.LogInfo("Server multipliers dropped (" + reason + "); using the local config.");
        }
    }
}
