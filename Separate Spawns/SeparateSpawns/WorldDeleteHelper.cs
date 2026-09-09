using System;
using System.IO;
using HarmonyLib;

namespace SeparateSpawns
{
    /// <summary>
    /// Deleting and recreating the world for a seed reroll, against the Valheim 1.0 save system.
    ///
    /// 1.0 stores a world as a folder (<c>&lt;savedata&gt;/worlds_local/&lt;saveName&gt;/</c>)
    /// holding <c>_main.&lt;n&gt;.fwl2</c> plus chunk files, not the old
    /// <c>&lt;name&gt;.fwl</c> + <c>&lt;name&gt;.db</c> pair. The folder name is
    /// <see cref="World.m_worldName"/>; <see cref="World.m_name"/> is the name stored
    /// inside the .fwl2 and the two can differ if a player renames the folder.
    /// <see cref="SaveSystem"/> indexes saves by the folder name, so every delete and
    /// recreate has to key on <see cref="World.m_worldName"/>.
    /// </summary>
    internal static class WorldDeleteHelper
    {
        /// <summary>
        /// The name SaveSystem indexes this world under, i.e. its save folder name.
        /// Everything that has to survive a delete-and-recreate keys on this, not on
        /// <see cref="World.m_name"/>, because the server finds a world by folder name.
        /// </summary>
        public static string GetSaveName(World world)
        {
            if (world == null)
            {
                return null;
            }

            return string.IsNullOrEmpty(world.m_worldName) ? world.m_name : world.m_worldName;
        }

        public static FileHelpers.FileSource GetWorldFileSource(World world)
        {
            return world?.m_fileSource ?? FileHelpers.FileSource.Local;
        }

        /// <summary>
        /// Deletes the world's save folder and the alt-biome cache that sits outside it.
        /// </summary>
        public static bool RemoveLoadedWorld(World world)
        {
            if (world == null)
            {
                return false;
            }

            var saveName = GetSaveName(world);
            if (string.IsNullOrEmpty(saveName))
            {
                ModLog.Error("Seed reroll could not determine the world's save name; refusing to delete anything.");
                return false;
            }

            var fileSource = GetWorldFileSource(world);
            var saveDirectory = World.GetSaveDirectory(fileSource, saveName);

            World.RemoveWorld(saveName, fileSource);

            // SaveSystem.Delete only clears the alt-biome cache when the save is chunked,
            // and it keys that cleanup on the save (folder) name while
            // AltBiomeWorldData reads and writes it under World.m_name. Clear both names
            // ourselves: the cache lives in <savedata>/cache/, outside the world folder,
            // so a stale one survives the delete and would be handed to the *new* seed.
            // TryLoadCache only compares Version.World, never the seed, so a surviving
            // cache is silently wrong rather than loudly wrong.
            RemoveBiomeCache(world.m_name);
            if (!string.Equals(world.m_name, saveName, StringComparison.Ordinal))
            {
                RemoveBiomeCache(saveName);
            }

            var removed = !DirectoryStillExists(saveDirectory);
            if (!removed)
            {
                ModLog.Error($"Seed reroll deleted world '{saveName}' but its save folder still exists at {saveDirectory}.");
            }

            return removed;
        }

        private static void RemoveBiomeCache(string worldName)
        {
            if (string.IsNullOrEmpty(worldName))
            {
                return;
            }

            try
            {
                AltBiomeWorldData.RemoveCache(worldName);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to remove the alt-biome cache for '{worldName}': {ex.Message}");
            }
        }

        private static bool DirectoryStillExists(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Writes the replacement world's .fwl2, following <c>World.GetCreateWorld</c> exactly.
        /// </summary>
        /// <remarks>
        /// The save number reset is the part that is easy to miss. The .fwl2 filename embeds
        /// <c>SaveSystem.GetSaveNumber()</c> (<c>_main.&lt;n&gt;.fwl2</c>), and the chunk files
        /// for a save carry the same number. A brand-new world has no chunk files at all, so it
        /// must be written at number 0 the way vanilla world creation does; writing it at
        /// whatever number the doomed world happened to reach leaves a .fwl2 pointing at chunks
        /// that were just deleted.
        /// </remarks>
        public static void SaveNewWorldMetadata(World world)
        {
            SaveSystem.SetSaveNumber(0u);
            world.SaveWorldFWLData(DateTime.Now);
        }

        /// <summary>
        /// Shuts networking down without writing the doomed world, and blocks every other
        /// save path that could rewrite it before the process exits.
        /// </summary>
        /// <remarks>
        /// 1.0 added <c>SaveSystemSessionFlags.DontSaveWorld</c>, which <c>ZNet.Save</c> checks
        /// before doing anything else. That is the supported kill switch and it covers the
        /// autosave timer and any other caller, so set it first. <c>Game.m_shuttingDown</c> is
        /// still worth setting on top: it is what makes <c>Game.OnApplicationQuit</c> skip its
        /// <c>Shutdown(saveWorld)</c>, which would otherwise also write the player profile.
        /// The flag is sticky (SetSessionFlags ORs, it never clears), which is fine because the
        /// only caller quits immediately afterwards.
        /// </remarks>
        public static void ShutdownGameWithoutSaving()
        {
            try
            {
                SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveWorld);

                if (Game.instance != null)
                {
                    AccessTools.Field(typeof(Game), "m_shuttingDown")?.SetValue(Game.instance, true);
                }

                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.Shutdown();
                }

                if (ZNet.instance != null)
                {
                    ZNet.instance.ShutdownWithoutSave(false);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"Shutdown without save during seed reroll failed: {ex.Message}");
            }
        }
    }
}
