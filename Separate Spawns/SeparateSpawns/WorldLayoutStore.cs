using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class WorldLayoutStore
    {
        private static string GetPath(long worldUid)
        {
            var relative = Path.Combine("SeparateSpawns", "worlds", $"{worldUid}.json");
            return ModPaths.ResolveConfigPath(relative);
        }

        private static string GetWritePath(long worldUid)
        {
            var relative = Path.Combine("SeparateSpawns", "worlds", $"{worldUid}.json");
            return ModPaths.GetWriteConfigPath(relative);
        }

        public static WorldLayoutData Load(long worldUid)
        {
            var path = GetPath(worldUid);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<WorldLayoutData>(json, JsonSettings.Serializer);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to load world layout state: {ex}");
                return null;
            }
        }

        public static void Save(long worldUid, WorldLayoutData data)
        {
            try
            {
                var path = GetWritePath(worldUid);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(data, JsonSettings.Serializer));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to save world layout state: {ex.Message}");
            }
        }

        /// <summary>
        /// Drops the layout file for a world uid. Used by the seed reroll: recreating a
        /// world mints a new m_uid, so the doomed world's file would never be read again.
        /// </summary>
        public static void Delete(long worldUid)
        {
            try
            {
                var path = GetPath(worldUid);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to delete world layout state for {worldUid}: {ex.Message}");
            }
        }
    }

    internal static class SeedRerollStore
    {
        private static string GetPath(string worldName)
        {
            var relative = Path.Combine("SeparateSpawns", "rerolls", $"{worldName}.json");
            return ModPaths.ResolveConfigPath(relative);
        }

        private static string GetWritePath(string worldName)
        {
            var relative = Path.Combine("SeparateSpawns", "rerolls", $"{worldName}.json");
            return ModPaths.GetWriteConfigPath(relative);
        }

        public static int GetAttemptCount(string worldName)
        {
            var path = GetPath(worldName);
            if (!File.Exists(path))
            {
                return 0;
            }

            try
            {
                var state = JsonConvert.DeserializeObject<RerollState>(File.ReadAllText(path));
                return state?.Attempts ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void IncrementAttempt(string worldName)
        {
            var count = GetAttemptCount(worldName) + 1;
            var path = GetWritePath(worldName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(new RerollState { Attempts = count }, Formatting.Indented));
        }

        private sealed class RerollState
        {
            public int Attempts;
        }
    }
}
