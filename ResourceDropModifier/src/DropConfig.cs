using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ResourceDropModifier
{
    /// <summary>
    /// Binds one <c>ConfigEntry&lt;float&gt;</c> per catalogued item into the plugin's
    /// own <see cref="ConfigFile"/>, section per biome, so the multipliers are edited
    /// with the BepInEx Configuration Manager or a mod manager's config editor like
    /// any other setting. Builds the <see cref="MultiplierTable"/> from their values.
    /// </summary>
    internal static class DropConfig
    {
        /// <summary>Upper bound of the Configuration Manager slider. Raise if a server wants more.</summary>
        internal const float MaxMultiplier = 10f;

        // What BepInEx's ConfigDefinition throws on. Prefab names are not expected
        // to contain any of these; the check turns a surprise into a log line
        // instead of an exception out of the bind loop.
        private static readonly char[] InvalidKeyChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        private static readonly Dictionary<string, ConfigEntry<float>> Entries =
            new Dictionary<string, ConfigEntry<float>>(StringComparer.Ordinal);

        internal static int Count => Entries.Count;

        /// <summary>
        /// Binds an entry for every catalog item that does not have one yet. Existing
        /// values in the file are kept; new items appear at 1. Saves once at the end:
        /// <c>Bind</c> rewrites the whole file per new entry when SaveOnConfigSet is
        /// on, and several hundred rewrites of a growing file on the main thread
        /// during world load is a visible hitch.
        /// </summary>
        /// <returns>How many entries were bound by this call (first bind in the process, not first ever in the file).</returns>
        internal static int BindAll(ConfigFile config, List<CatalogEntry> catalog, ManualLogSource log)
        {
            var bySection = new Dictionary<Heightmap.Biome, List<CatalogEntry>>();
            foreach (CatalogEntry entry in catalog)
            {
                Heightmap.Biome section = entry.Section;
                if (section == Heightmap.Biome.None)
                {
                    log.LogInfo("Drop catalog: " + entry.PrefabName + " has no biome and is not listed.");
                    continue;
                }

                List<CatalogEntry> list;
                if (!bySection.TryGetValue(section, out list))
                {
                    list = new List<CatalogEntry>();
                    bySection[section] = list;
                }

                list.Add(entry);
            }

            int added = 0;
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                // Progression order for the sections, alphabetical inside each, so the
                // file reads the same way every time it is regenerated.
                foreach (Heightmap.Biome biome in DropCatalog.BiomeOrder)
                {
                    List<CatalogEntry> list;
                    if (!bySection.TryGetValue(biome, out list))
                        continue;

                    list.Sort((a, b) => string.CompareOrdinal(a.PrefabName, b.PrefabName));
                    foreach (CatalogEntry entry in list)
                    {
                        if (Bind(config, biome.ToString(), entry, log))
                            added++;
                    }
                }
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }

            if (added > 0)
                config.Save();

            return added;
        }

        private static bool Bind(ConfigFile config, string section, CatalogEntry entry, ManualLogSource log)
        {
            if (Entries.ContainsKey(entry.PrefabName))
                return false;

            if (entry.PrefabName.IndexOfAny(InvalidKeyChars) >= 0)
            {
                log.LogWarning("Drop catalog: '" + entry.PrefabName + "' cannot be a config key and has no setting.");
                return false;
            }

            // Bind returns the existing entry when the file already has one, with
            // its value intact; only a genuinely new item lands at the default.
            ConfigEntry<float> bound = config.Bind(
                new ConfigDefinition(section, entry.PrefabName),
                1f,
                new ConfigDescription(Describe(entry), new AcceptableValueRange<float>(0f, MaxMultiplier)));

            Entries[entry.PrefabName] = bound;
            return true;
        }

        private static string Describe(CatalogEntry entry)
        {
            var text = new StringBuilder();
            text.Append(entry.DisplayName ?? entry.PrefabName);

            if (entry.Sources.Count > 0)
            {
                text.Append(". Drops from: ");
                text.Append(string.Join(", ", entry.Sources));
            }

            text.Append(". Vanilla amount times this; 0 removes the drop.");

            if (!entry.Scalable)
                text.Append(" Not scalable: vanilla exempts every drop of this item, so this setting does nothing.");

            text.Append(" While connected to a server, the server's value is the one in force.");
            return text.ToString();
        }

        /// <summary>The table of every bound entry's current value.</summary>
        internal static MultiplierTable BuildTable()
        {
            var byPrefab = new Dictionary<string, float>(Entries.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, ConfigEntry<float>> pair in Entries)
                byPrefab[pair.Key] = pair.Value.Value;
            return new MultiplierTable(byPrefab);
        }

        /// <summary>True when the setting belongs to an item, not to [General].</summary>
        internal static bool IsItemSetting(ConfigEntryBase setting)
        {
            ConfigEntry<float> entry;
            return setting != null
                && Entries.TryGetValue(setting.Definition.Key, out entry)
                && ReferenceEquals(entry, setting);
        }
    }
}
