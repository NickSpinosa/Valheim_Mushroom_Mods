using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MushroomSync
{
    /// <summary>
    /// Makes <c>entry.Value</c> return the host's value on a synced client, so call
    /// sites read config the way they always did and need no changes.
    ///
    /// Two things make this safe rather than merely clever:
    ///
    /// The overlay is read-only. Nothing is written back into the
    /// <see cref="ConfigEntry{T}"/>, so a client's own .cfg is never edited and its
    /// settings return as soon as it disconnects.
    ///
    /// A re-entrancy depth suppresses the overlay while the server serialises its
    /// own values, and while BepInEx saves a config file. Without the second guard a
    /// client that changed any unrelated setting would write the host's values into
    /// its local .cfg permanently.
    /// </summary>
    internal static class ConfigValueOverlay
    {
        private static readonly Dictionary<ConfigEntryBase, ConfigSync> Owners =
            new Dictionary<ConfigEntryBase, ConfigSync>();

        private static readonly HashSet<Type> PatchedTypes = new HashSet<Type>();
        private static readonly HashSet<ConfigFile> ProtectedFiles = new HashSet<ConfigFile>();
        private static readonly object Gate = new object();

        private static Harmony _harmony;
        private static ManualLogSource _log;

        [ThreadStatic]
        private static int _readLocalDepth;

        internal static void Initialize(Harmony harmony, ManualLogSource log)
        {
            _harmony = harmony;
            _log = log;

            MethodInfo save = AccessTools.Method(typeof(ConfigFile), "Save", Type.EmptyTypes);
            if (save == null)
            {
                _log.LogWarning(
                    "MushroomSync: ConfigFile.Save not found; a synced client that saves its config "
                    + "could persist host values locally.");
                return;
            }

            harmony.Patch(
                save,
                prefix: new HarmonyMethod(typeof(ConfigValueOverlay), nameof(ConfigFileSavePrefix)),
                finalizer: new HarmonyMethod(typeof(ConfigValueOverlay), nameof(ConfigFileSaveFinalizer)));
        }

        /// <summary>Claims an entry for a sync, and patches its type's getter once.</summary>
        internal static void Claim(ConfigEntryBase entry, ConfigSync owner)
        {
            lock (Gate)
            {
                if (Owners.ContainsKey(entry))
                    return;

                Owners[entry] = owner;
            }

            EnsurePatchedFor(entry.SettingType);
        }

        internal static void Release(ConfigEntryBase entry)
        {
            lock (Gate)
            {
                Owners.Remove(entry);
            }
        }

        /// <summary>Protects a config file from having overlaid values written to disk.</summary>
        internal static void Protect(ConfigFile file)
        {
            if (file == null)
                return;

            lock (Gate)
            {
                ProtectedFiles.Add(file);
            }
        }

        /// <summary>
        /// Reads an entry's own value, ignoring any overlay. This is what the server
        /// serialises - without it a client-turned-host would rebroadcast values it
        /// received from somewhere else.
        /// </summary>
        internal static string ReadLocalSerialized(ConfigEntryBase entry)
        {
            _readLocalDepth++;
            try
            {
                return entry.GetSerializedValue() ?? string.Empty;
            }
            finally
            {
                _readLocalDepth--;
            }
        }

        /// <summary>
        /// Patches <c>ConfigEntry&lt;T&gt;.Value</c> for one setting type. Done per type
        /// on demand rather than for a fixed list, so enums and any other bound type
        /// are covered instead of just the common four.
        /// </summary>
        private static void EnsurePatchedFor(Type settingType)
        {
            if (settingType == null || _harmony == null)
                return;

            lock (Gate)
            {
                if (!PatchedTypes.Add(settingType))
                    return;
            }

            try
            {
                Type entryType = typeof(ConfigEntry<>).MakeGenericType(settingType);
                MethodInfo getter = entryType
                    .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetGetMethod();

                if (getter == null)
                {
                    _log?.LogWarning("MushroomSync: no Value getter on ConfigEntry<" + settingType.Name + ">.");
                    return;
                }

                MethodInfo postfix = AccessTools
                    .Method(typeof(ConfigValueOverlay), nameof(ValueGetterPostfix))
                    .MakeGenericMethod(settingType);

                _harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                lock (Gate)
                {
                    PatchedTypes.Remove(settingType);
                }

                _log?.LogWarning(
                    "MushroomSync: could not overlay ConfigEntry<" + settingType.Name + ">.Value: "
                    + ex.Message + ". TryGetSyncedValue still works for it.");
            }
        }

        public static void ValueGetterPostfix<T>(ConfigEntry<T> __instance, ref T __result)
        {
            if (_readLocalDepth > 0 || __instance == null)
                return;

            ConfigSync owner;
            lock (Gate)
            {
                if (!Owners.TryGetValue(__instance, out owner))
                    return;
            }

            T synced;
            if (owner.TryGetSyncedValue(__instance, out synced))
                __result = synced;
        }

        public static void ConfigFileSavePrefix(ConfigFile __instance, ref bool __state)
        {
            __state = false;

            lock (Gate)
            {
                if (!ProtectedFiles.Contains(__instance))
                    return;
            }

            _readLocalDepth++;
            __state = true;
        }

        public static Exception ConfigFileSaveFinalizer(Exception __exception, bool __state)
        {
            if (__state && _readLocalDepth > 0)
                _readLocalDepth--;

            return __exception;
        }
    }
}
