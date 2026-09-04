using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace MushroomSync
{
    /// <summary>
    /// Server-authoritative config: the host sends its effective settings to joining
    /// clients, which use them at runtime without their own .cfg being touched.
    ///
    /// Built on <see cref="SyncChannel"/>, which owns the transport. This class only
    /// knows how to turn config entries into a payload and back.
    ///
    /// Typical use, in a plugin's Awake:
    /// <code>
    /// _sync = ConfigSync.Create(PluginGuid, PluginVersion, Logger)
    ///     .Protecting(Config)
    ///     .OnApplied(ApplyRuntimeSettings);
    /// _sync.Register(EnableThing);
    /// _sync.Start();
    /// </code>
    /// After that <c>EnableThing.Value</c> returns the host's value on a synced
    /// client. Use <see cref="TryGetSyncedValue{T}"/> where you need to know whether a
    /// value came from the host, or need the local one alongside it.
    /// </summary>
    public sealed class ConfigSync
    {
        // Unit Separator (0x1F): cannot occur in a BepInEx section or key, so no two
        // section/key pairs can ever collapse to the same path. Built from its code
        // point so the source carries no raw control character.
        private static readonly string PathSeparator = ((char)31).ToString();

        private readonly Dictionary<string, ConfigEntryBase> _entries =
            new Dictionary<string, ConfigEntryBase>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<ConfigEntryBase, object> _syncedValues =
            new Dictionary<ConfigEntryBase, object>();

        private readonly HashSet<ConfigEntryBase> _excluded = new HashSet<ConfigEntryBase>();

        private readonly HashSet<ConfigFile> _watchedFiles = new HashSet<ConfigFile>();

        private readonly SyncChannel _channel;
        private readonly ManualLogSource _log;
        private readonly string _id;

        private Action _applied;
        private Action<string> _notify;

        private ConfigSync(string id, string version, ManualLogSource log)
        {
            _id = id;
            _log = log;
            _channel = SyncChannel.Create(id + ".Config", version, log);
            _channel.WritePayload = WritePayload;
            _channel.ReadPayload = ReadPayload;
            _channel.Cleared = OnCleared;
        }

        /// <summary>True on a client currently using host values.</summary>
        public bool ClientSyncActive => _channel.ClientSyncActive;

        /// <summary>
        /// True when this side decides its own values: dedicated server, host, or
        /// single-player. Guard local changes with this.
        /// </summary>
        public static bool IsServerAuthority() => SyncChannel.IsServerAuthority();

        /// <summary>The underlying channel, for mods that also push their own data.</summary>
        public SyncChannel Channel => _channel;

        public static ConfigSync Create(string id, string version, ManualLogSource log)
        {
            return new ConfigSync(id, version, log);
        }

        /// <summary>
        /// Stops the client's own .cfg from being rewritten with host values while a
        /// sync is active. Pass the plugin's ConfigFile - usually <c>Config</c>.
        /// </summary>
        public ConfigSync Protecting(ConfigFile file)
        {
            ConfigValueOverlay.Protect(file);
            return this;
        }

        /// <summary>
        /// Runs after values are applied and after they are dropped, on clients. Use it
        /// to push config into whatever caches the mod keeps - the config changing is
        /// not by itself enough for mods that bake settings into game objects.
        /// </summary>
        public ConfigSync OnApplied(Action applied)
        {
            _applied = applied;
            return this;
        }

        /// <summary>Optional player-facing message on first sync and on failure.</summary>
        public ConfigSync Notifying(Action<string> notify)
        {
            _notify = notify;
            return this;
        }

        /// <summary>
        /// Gates syncing on a setting, e.g. an opt-out toggle. Returning false makes
        /// the server send nothing and a client ignore anything that arrives.
        /// </summary>
        public ConfigSync GatedBy(Func<bool> gate)
        {
            _channel.SendGate = gate;
            return this;
        }

        /// <summary>Begins listening. Call once, after registering entries.</summary>
        public ConfigSync Start()
        {
            _channel.Start();
            return this;
        }

        /// <summary>
        /// Registers one entry for syncing. Its <c>Value</c> getter starts returning
        /// host values on a synced client.
        /// </summary>
        public ConfigSync Register(ConfigEntryBase entry)
        {
            if (entry == null)
                return this;

            string path = BuildPath(entry.Definition.Section, entry.Definition.Key);
            if (_entries.ContainsKey(path))
                return this;

            _entries[path] = entry;
            ConfigValueOverlay.Claim(entry, this);
            return this;
        }

        public ConfigSync Register(params ConfigEntryBase[] entries)
        {
            if (entries != null)
            {
                foreach (ConfigEntryBase entry in entries)
                    Register(entry);
            }

            return this;
        }

        /// <summary>
        /// Rebroadcasts whenever a registered setting in this file changes on the
        /// server, so a live edit reaches connected clients.
        ///
        /// Subscribes to the file once however many times it is called - the event is
        /// per <see cref="ConfigFile"/>, not per entry, so subscribing per entry would
        /// broadcast once for every registered setting on a single change.
        /// </summary>
        public ConfigSync WatchForChanges(ConfigFile file)
        {
            if (file == null || !_watchedFiles.Add(file))
                return this;

            file.SettingChanged += OnSettingChanged;
            return this;
        }

        /// <summary>
        /// Keeps an entry local: never sent, never overlaid. For machine-specific
        /// settings, keybinds, and the toggle that controls syncing itself.
        /// </summary>
        public ConfigSync Exclude(ConfigEntryBase entry)
        {
            if (entry == null)
                return this;

            _excluded.Add(entry);
            ConfigValueOverlay.Release(entry);
            _entries.Remove(BuildPath(entry.Definition.Section, entry.Definition.Key));
            return this;
        }

        /// <summary>
        /// The host's value for an entry, when one is in force. Returns false on the
        /// server, when disconnected, and for entries the host did not send.
        /// </summary>
        public bool TryGetSyncedValue<T>(ConfigEntry<T> entry, out T value)
        {
            object stored;
            if (_channel.ClientSyncActive
                && entry != null
                && _syncedValues.TryGetValue(entry, out stored)
                && stored is T)
            {
                value = (T)stored;
                return true;
            }

            value = default(T);
            return false;
        }

        /// <summary>
        /// Pushes current values to every connected client. Safe to call anywhere; it
        /// does nothing unless this side is the server.
        /// </summary>
        public void Broadcast()
        {
            if (!IsServerAuthority())
                return;

            _channel.Broadcast();
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (args == null || args.ChangedSetting == null)
                return;

            string path = BuildPath(args.ChangedSetting.Definition.Section, args.ChangedSetting.Definition.Key);
            if (!_entries.ContainsKey(path))
                return;

            Broadcast();
        }

        private static string BuildPath(string section, string key)
        {
            return (section ?? string.Empty) + PathSeparator + (key ?? string.Empty);
        }

        private bool ShouldSync(ConfigEntryBase entry)
        {
            return entry != null && !_excluded.Contains(entry);
        }

        private void WritePayload(ZPackage payload)
        {
            var list = new List<ConfigEntryBase>();
            foreach (ConfigEntryBase entry in _entries.Values)
            {
                if (ShouldSync(entry))
                    list.Add(entry);
            }

            // Deterministic order so an unchanged config produces an identical
            // payload, which keeps logs and packet dumps comparable between runs.
            list.Sort(CompareEntries);

            payload.Write(list.Count);
            foreach (ConfigEntryBase entry in list)
            {
                payload.Write(entry.Definition.Section ?? string.Empty);
                payload.Write(entry.Definition.Key ?? string.Empty);
                payload.Write(ConfigValueOverlay.ReadLocalSerialized(entry));
            }
        }

        private static int CompareEntries(ConfigEntryBase a, ConfigEntryBase b)
        {
            int bySection = StringComparer.OrdinalIgnoreCase.Compare(
                a.Definition.Section, b.Definition.Section);
            return bySection != 0
                ? bySection
                : StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Key, b.Definition.Key);
        }

        private void ReadPayload(ZPackage payload)
        {
            int count = SyncChannel.ReadCount(payload);

            var typed = new Dictionary<ConfigEntryBase, object>();
            var unknown = 0;

            for (int i = 0; i < count; i++)
            {
                string section = payload.ReadString();
                string key = payload.ReadString();
                string serialized = payload.ReadString();

                ConfigEntryBase entry;
                if (!_entries.TryGetValue(BuildPath(section, key), out entry) || !ShouldSync(entry))
                {
                    // A host running a build with settings this client does not have.
                    // Not fatal: apply what we recognise.
                    unknown++;
                    continue;
                }

                object value;
                if (!TryConvert(entry, serialized, out value))
                {
                    _log.LogWarning(
                        _id + ": could not parse [" + section + "] " + key + " = '" + serialized
                        + "' as " + entry.SettingType.Name + "; keeping the local value.");
                    continue;
                }

                typed[entry] = value;
            }

            bool firstActivation = !_channel.ClientSyncActive;

            _syncedValues.Clear();
            foreach (KeyValuePair<ConfigEntryBase, object> pair in typed)
                _syncedValues[pair.Key] = pair.Value;

            RaiseApplied();

            if (firstActivation)
            {
                _log.LogInfo(_id + ": using host configuration (" + typed.Count + " settings).");
                Notify(_id + ": using host configuration.");
            }
            else
            {
                _log.LogInfo(_id + ": host configuration updated (" + typed.Count + " settings).");
            }

            if (unknown > 0)
                _log.LogInfo(_id + ": ignored " + unknown + " setting(s) this build does not have.");
        }

        private void OnCleared(string reason)
        {
            _syncedValues.Clear();
            RaiseApplied();
            _log.LogInfo(_id + ": host configuration dropped (" + reason + "); using local settings.");

            if (reason == "version mismatch" || reason == "invalid payload")
                Notify(_id + ": host config unavailable (" + reason + "); using your local settings.");
        }

        private static bool TryConvert(ConfigEntryBase entry, string serialized, out object value)
        {
            try
            {
                // Enums first: TomlTypeConverter does not handle every enum shape, and
                // silently returning null there is how a mod ends up quietly ignoring
                // a synced setting.
                if (entry.SettingType.IsEnum)
                {
                    value = Enum.Parse(entry.SettingType, serialized ?? string.Empty, true);
                    return true;
                }

                value = TomlTypeConverter.ConvertToValue(serialized, entry.SettingType);
                return value != null;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        private void RaiseApplied()
        {
            if (_applied == null)
                return;

            try
            {
                _applied();
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": OnApplied handler failed: " + ex.Message);
            }
        }

        private void Notify(string message)
        {
            if (_notify == null)
                return;

            try
            {
                _notify(message);
            }
            catch (Exception ex)
            {
                _log.LogWarning(_id + ": notify handler failed: " + ex.Message);
            }
        }
    }
}
