using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace QuietNights
{
    /// <summary>
    /// <c>quietnights dump</c>. The spawn table lives in the game's asset bundles, not
    /// its code, so this is the only way to see what the rules are actually matched
    /// against - and the instrument the manual tests in docs/DESIGN.md read.
    /// </summary>
    internal static class DumpCommand
    {
        internal static void Run(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 2 || !string.Equals(args[1], "dump", StringComparison.OrdinalIgnoreCase))
            {
                args.Context?.AddString("Usage: quietnights dump");
                return;
            }

            if (ZoneSystem.instance == null)
            {
                args.Context?.AddString("Quiet Nights: load a world first - the spawn table belongs to it.");
                return;
            }

            string report = Build(out int covered, out int edited);
            string path = Path.Combine(Paths.ConfigPath, "QuietNights.dump.txt");

            try
            {
                File.WriteAllText(path, report);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not write " + path + ": " + ex.Message);
                path = "the BepInEx log only";
            }

            Plugin.Log.LogInfo("Spawn table dump:" + Environment.NewLine + report);
            args.Context?.AddString("Quiet Nights: " + covered + " boss-gated entries for covered creatures, "
                + edited + " currently suppressed. Written to " + path);
        }

        private static string Build(out int covered, out int edited)
        {
            covered = 0;
            edited = 0;

            Rules rules = Plugin.CurrentRules();
            var text = new StringBuilder();

            text.AppendLine("Quiet Nights " + Plugin.PluginVersion + " spawn table dump");
            text.AppendLine("Enabled       = " + Plugin.Enabled.Value);
            text.AppendLine("Creatures     = " + Plugin.Creatures.Value);
            text.AppendLine("BossKeys      = " + Plugin.BossKeys.Value);
            text.AppendLine("Host values   = " + (Plugin.Sync.TryGetSyncedValue(Plugin.Creatures, out _) ? "yes, following the server" : "no, local config"));
            text.AppendLine("World keys    = " + string.Join(", ", ZoneSystem.instance.GetGlobalKeys()));
            text.AppendLine("Spawn lists   = " + DescribeSharing());
            text.AppendLine();
            text.AppendLine("Listed: every entry gated on a global key, plus every entry for a covered creature.");
            text.AppendLine("biome= is the mask as shipped; now= appears when Quiet Nights has changed it.");
            text.AppendLine();

            IReadOnlyList<SpawnSystemList> prefabLists = SpawnSuppressor.KnownPrefabLists;
            for (int i = 0; i < prefabLists.Count; i++)
            {
                if (prefabLists[i] != null)
                    Append(text, "list" + i + ":" + prefabLists[i].name, prefabLists[i].m_spawners, rules, ref covered, ref edited);
            }

            foreach (AltBiome altBiome in AltBiomeList.m_altBiomes)
            {
                if (altBiome != null)
                    Append(text, "alt:" + altBiome.m_name, altBiome.m_spawn, rules, ref covered, ref edited);
            }

            return text.ToString();
        }

        private static void Append(StringBuilder text, string source, List<SpawnSystem.SpawnData> entries,
            Rules rules, ref int covered, ref int edited)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                SpawnSystem.SpawnData entry = entries[i];
                if (entry == null || entry.m_prefab == null)
                    continue;

                bool keyed = !string.IsNullOrEmpty(entry.m_requiredGlobalKey);
                bool coveredCreature = rules.Find(entry.m_prefab.name) != null;
                if (!keyed && !coveredCreature)
                    continue;

                Verdict verdict = SpawnSuppressor.Classify(entry, rules, out _);
                if (verdict != Verdict.NotCovered)
                    covered++;

                bool isEdited = SpawnSuppressor.IsEdited(entry);
                if (isEdited)
                    edited++;

                // idx is 1-based to match the index the game hashes into the timer key.
                text.Append(source).Append(" idx=").Append(i + 1)
                    .Append(" name=\"").Append(entry.m_name).Append('"')
                    .Append(" prefab=").Append(entry.m_prefab.name)
                    .Append(" biome=").Append(SpawnSuppressor.OriginalBiome(entry));

                if (isEdited)
                    text.Append(" now=").Append(entry.m_biome);

                text.Append(" key=").Append(keyed ? entry.m_requiredGlobalKey : "-")
                    .Append(" night=").Append(entry.m_spawnAtNight ? 1 : 0)
                    .Append(" day=").Append(entry.m_spawnAtDay ? 1 : 0)
                    .Append(" enabled=").Append(entry.m_enabled ? 1 : 0)
                    .Append(" max=").Append(entry.m_maxSpawned)
                    .Append(" interval=").Append(entry.m_spawnInterval.ToString(CultureInfo.InvariantCulture))
                    .Append(" chance=").Append(entry.m_spawnChance.ToString(CultureInfo.InvariantCulture))
                    .Append(" hunt=").Append(entry.m_huntPlayer ? 1 : 0)
                    .Append("  [").Append(verdict).Append(isEdited ? ", APPLIED" : "").AppendLine("]");
            }
        }

        /// <summary>
        /// Settles the one thing docs/DESIGN.md had to assume: whether every zone points
        /// at the prefab's list objects, or gets clones of them.
        /// </summary>
        private static string DescribeSharing()
        {
            IReadOnlyList<SpawnSystemList> prefabLists = SpawnSuppressor.KnownPrefabLists;
            if (prefabLists.Count == 0)
                return "none found on the zone control prefab";

            foreach (SpawnSystem spawnSystem in SpawnSuppressor.LiveSpawnSystems())
            {
                if (spawnSystem == null || spawnSystem.m_spawnLists.Count == 0 || spawnSystem.m_spawnLists[0] == null)
                    continue;

                return ReferenceEquals(spawnSystem.m_spawnLists[0], prefabLists[0])
                    ? "SHARED - live zones use the prefab's own list objects"
                    : "CLONED - each zone has its own copy (handled per zone from SpawnSystem.Awake)";
            }

            return "unknown - no zone is loaded to compare against";
        }
    }
}
