using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class ModPaths
    {
        public static string RosterFile => "SeparateSpawns.groups.json";
        public static string PluginConfigFile => "abortipus.separatespawns.cfg";
        // Optional override file in BepInEx/config. Set ConfigRoot=default or ConfigRoot=legacy.
        // The command-line option -separatespawns-config=default|legacy takes precedence.
        public const string ConfigPathOverrideFile = "SeparateSpawns.paths.cfg";
        private static bool? _useDedicatedLayout;
        private static string _configRootMode;
        private static string _configRootModeSource;
        /// <summary>
        /// Client default: Valheim/BepInEx/config
        /// </summary>
        public static string GetClientConfigRoot()
        {
            return Paths.ConfigPath;
        }
        /// <summary>
        /// Dedicated server default: Valheim/BepInEx/config.
        /// </summary>
        public static string GetDedicatedConfigRoot()
        {
            if (string.Equals(GetConfigRootMode(), "legacy", StringComparison.OrdinalIgnoreCase))
            {
                return GetLegacyDedicatedConfigRoot();
            }
            var bepinExRoot = Paths.BepInExRootPath;
            if (string.IsNullOrEmpty(bepinExRoot))
            {
                return null;
            }
            return Path.Combine(bepinExRoot, "config");
        }
        public static string GetConfigRootMode()
        {
            if (!string.IsNullOrEmpty(_configRootMode))
            {
                return _configRootMode;
            }
            var commandLineMode = GetCommandLineConfigRootMode();
            if (!string.IsNullOrEmpty(commandLineMode))
            {
                _configRootMode = commandLineMode;
                _configRootModeSource = "command line";
                return _configRootMode;
            }
            var fileMode = GetFileConfigRootMode();
            if (!string.IsNullOrEmpty(fileMode))
            {
                _configRootMode = fileMode;
                _configRootModeSource = ConfigPathOverrideFile;
                return _configRootMode;
            }
            _configRootMode = "default";
            _configRootModeSource = "default";
            return _configRootMode;
        }
        public static string GetConfigRootModeSource()
        {
            GetConfigRootMode();
            return _configRootModeSource;
        }
        /// <summary>
        /// Back-compat alias for logging/tools.
        /// </summary>
        public static string GetAlternateConfigRoot() => GetDedicatedConfigRoot();

        public static bool UseDedicatedConfigLayout()
        {
            if (_useDedicatedLayout.HasValue)
            {
                return _useDedicatedLayout.Value;
            }

            if (HasDedicatedLaunchFlag() || PlatformIdHelper.IsHeadlessServerContext())
            {
                _useDedicatedLayout = true;
                return true;
            }

            _useDedicatedLayout = false;
            return false;
        }
        private static string GetLegacyDedicatedConfigRoot()
        {
            if (string.IsNullOrEmpty(Paths.BepInExRootPath))
            {
                return null;
            }
            var gameRoot = Path.GetDirectoryName(Paths.BepInExRootPath);
            return string.IsNullOrEmpty(gameRoot)
                ? null
                : Path.Combine(gameRoot, "config", "bepinex");
        }
        private static string GetCommandLineConfigRootMode()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length; i++)
                {
                    const string prefix = "-separatespawns-config=";
                    if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return NormalizeConfigRootMode(args[i].Substring(prefix.Length));
                    }
                    if (args[i].Equals("-separatespawns-config", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length)
                    {
                        return NormalizeConfigRootMode(args[i + 1]);
                    }
                }
            }
            catch
            {
                // Ignore malformed command-line access and use the external file or default.
            }
            return null;
        }
        private static string GetFileConfigRootMode()
        {
            var configRoot = GetClientConfigRoot();
            if (string.IsNullOrEmpty(configRoot))
            {
                return null;
            }
            var path = Path.Combine(configRoot, ConfigPathOverrideFile);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var setting = line.Trim();
                    if (setting.Length == 0 || setting.StartsWith("#") || setting.StartsWith(";"))
                    {
                        continue;
                    }
                    var separator = setting.IndexOf('=');
                    if (separator < 0 ||
                        !setting.Substring(0, separator).Trim().Equals("ConfigRoot", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    return NormalizeConfigRootMode(setting.Substring(separator + 1));
                }
            }
            catch
            {
                // Ignore an unreadable override file and use the default path.
            }
            return null;
        }
        private static string NormalizeConfigRootMode(string value)
        {
            var mode = value == null ? null : value.Trim();
            if (string.Equals(mode, "default", StringComparison.OrdinalIgnoreCase))
            {
                return "default";
            }
            if (string.Equals(mode, "legacy", StringComparison.OrdinalIgnoreCase))
            {
                return "legacy";
            }
            return null;
        }
        public static IEnumerable<string> GetConfigRoots()
        {
            var clientRoot = GetClientConfigRoot();
            var dedicatedRoot = GetDedicatedConfigRoot();

            if (UseDedicatedConfigLayout())
            {
                if (!string.IsNullOrEmpty(dedicatedRoot))
                {
                    yield return dedicatedRoot;
                }

                if (!string.IsNullOrEmpty(clientRoot) &&
                    !PathsEqual(clientRoot, dedicatedRoot))
                {
                    yield return clientRoot;
                }

                yield break;
            }

            if (!string.IsNullOrEmpty(clientRoot))
            {
                yield return clientRoot;
            }

            if (!string.IsNullOrEmpty(dedicatedRoot) &&
                !PathsEqual(clientRoot, dedicatedRoot))
            {
                yield return dedicatedRoot;
            }
        }

        public static string GetDefaultConfigRoot()
        {
            if (UseDedicatedConfigLayout())
            {
                var dedicatedRoot = GetDedicatedConfigRoot();
                if (!string.IsNullOrEmpty(dedicatedRoot))
                {
                    return dedicatedRoot;
                }
            }

            return GetClientConfigRoot();
        }

        /// <summary>
        /// Returns the first existing config path among supported roots, or the default write path.
        /// </summary>
        public static string ResolveConfigPath(string relativePath)
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, relativePath);
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(GetDefaultConfigRoot(), relativePath);
        }

        /// <summary>
        /// Prefer an existing file's directory; otherwise use the default root for this host type.
        /// </summary>
        public static string GetWriteConfigPath(string relativePath)
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, relativePath);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            var parentRelative = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(parentRelative))
            {
                foreach (var root in GetConfigRoots())
                {
                    var parent = Path.Combine(root, parentRelative);
                    if (Directory.Exists(parent))
                    {
                        return Path.Combine(root, relativePath);
                    }
                }
            }

            return Path.Combine(GetDefaultConfigRoot(), relativePath);
        }

        /// <summary>
        /// Resolves abortipus.separatespawns.cfg from the first root that contains it, otherwise the default write path.
        /// </summary>
        public static string GetPluginConfigPath()
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, PluginConfigFile);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return GetWriteConfigPath(PluginConfigFile);
        }
        private static bool HasDedicatedLaunchFlag()
        {
            try
            {
                foreach (var arg in Environment.GetCommandLineArgs())
                {
                    if (arg.Equals("-dedicated", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and fall back to runtime detection.
            }

            return false;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
