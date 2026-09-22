using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace QuietNights
{
    internal enum Verdict
    {
        /// <summary>No required key, a key that is not a boss kill, or a creature no rule covers.</summary>
        NotCovered,
        /// <summary>Covered creature and boss key, but never spawns at night.</summary>
        DayOnly,
        /// <summary>Covered, but spawns by day as well, so it is not a "night spawn" to take away.</summary>
        SpawnsByDayToo,
        /// <summary>Covered, and only ever spawns where the creature belongs.</summary>
        NativeOnly,
        /// <summary>Covered, spans home and away: the away biomes are taken out of its mask.</summary>
        Narrowed,
        /// <summary>Covered and entirely away from home: switched off.</summary>
        Disabled
    }

    /// <summary>
    /// Edits the game's ambient spawn entries in place, and puts them back.
    /// <para>
    /// In place is the point. An entry's spawn timer is saved on the zone under a hash
    /// of its prefab name and its <em>position in the list</em>, so removing or
    /// reordering entries would hand every later entry a different timer in every
    /// saved zone. Only fields on existing entries are ever touched. See docs/DESIGN.md.
    /// </para>
    /// </summary>
    internal static class SpawnSuppressor
    {
        private sealed class Original
        {
            internal Heightmap.Biome Biome;
        }

        /// <summary>
        /// What each edited entry looked like before. Weak, because if the game turns
        /// out to clone the lists per zone these entries die with their zone, and a
        /// dictionary would keep every one of them alive for the session.
        /// </summary>
        private static readonly ConditionalWeakTable<SpawnSystem.SpawnData, Original> Originals =
            new ConditionalWeakTable<SpawnSystem.SpawnData, Original>();

        /// <summary>
        /// The zone-control prefab's lists. Held because a config change can arrive
        /// when <c>ZoneSystem.instance</c> is already gone (a disconnect drops the
        /// host's values on the way out), and the prefab is an asset that outlives it -
        /// edits left on it would otherwise wait in ambush for the next world.
        /// </summary>
        private static readonly List<SpawnSystemList> PrefabLists = new List<SpawnSystemList>();

        private static readonly FieldInfo InstancesField = AccessTools.Field(typeof(SpawnSystem), "m_instances");

        internal static IReadOnlyList<SpawnSystemList> KnownPrefabLists => PrefabLists;

        /// <summary>From <c>ZoneSystem.Start</c>: a world is up, find its lists and apply.</summary>
        internal static void OnWorldStart(ZoneSystem zoneSystem)
        {
            PrefabLists.Clear();

            SpawnSystem prefab = zoneSystem.m_zoneCtrlPrefab != null
                ? zoneSystem.m_zoneCtrlPrefab.GetComponent<SpawnSystem>()
                : null;

            if (prefab == null)
            {
                Plugin.Log.LogWarning("The zone control prefab has no SpawnSystem; nothing to suppress.");
            }
            else
            {
                foreach (SpawnSystemList list in prefab.m_spawnLists)
                {
                    if (list != null)
                        PrefabLists.Add(list);
                }
            }

            Apply();
        }

        /// <summary>
        /// From <c>SpawnSystem.Awake</c>. Does nothing when this zone's lists are the
        /// prefab's own - the expected case. It exists for the other one: if lists are
        /// cloned per zone, each new zone arrives unedited.
        /// </summary>
        internal static void OnZoneAwake(SpawnSystem spawnSystem)
        {
            if (!Plugin.Enabled.Value)
                return;

            Rules rules = null;

            foreach (SpawnSystemList list in spawnSystem.m_spawnLists)
            {
                if (list == null || PrefabLists.Contains(list))
                    continue;

                rules = rules ?? Plugin.CurrentRules();
                Suppress(list.m_spawners, rules);
            }
        }

        /// <summary>Puts every reachable entry back, then re-applies the current rules.</summary>
        internal static void Apply()
        {
            int restored = 0;
            int edited = 0;

            foreach (List<SpawnSystem.SpawnData> entries in ReachableLists())
                restored += Restore(entries);

            if (Plugin.Enabled.Value)
            {
                Rules rules = Plugin.CurrentRules();
                foreach (List<SpawnSystem.SpawnData> entries in ReachableLists())
                    edited += Suppress(entries, rules);
            }

            if (restored > 0 || edited > 0)
                Plugin.Log.LogInfo("Night spawns: " + edited + " entr" + (edited == 1 ? "y" : "ies")
                    + " suppressed away from home (" + restored + " restored first).");
        }

        /// <summary>
        /// What the rules make of an entry, judged on its values as the game shipped
        /// them. <paramref name="kept"/> is the biome mask it should be left with.
        /// </summary>
        internal static Verdict Classify(SpawnSystem.SpawnData entry, Rules rules, out Heightmap.Biome kept)
        {
            Heightmap.Biome biome = OriginalBiome(entry);
            kept = biome;

            if (entry.m_prefab == null
                || string.IsNullOrEmpty(entry.m_requiredGlobalKey)
                || !rules.IsBossKey(entry.m_requiredGlobalKey))
                return Verdict.NotCovered;

            Rules.Creature creature = rules.Find(entry.m_prefab.name);
            if (creature == null)
                return Verdict.NotCovered;

            if (!entry.m_spawnAtNight)
                return Verdict.DayOnly;

            // Taking the night half off an entry that also spawns by day would mean
            // splitting it in two, and entries cannot be added. None is known to exist;
            // the dump flags any that does.
            if (entry.m_spawnAtDay)
                return Verdict.SpawnsByDayToo;

            kept = biome & creature.Native;
            if (kept == biome)
                return Verdict.NativeOnly;

            return kept == Heightmap.Biome.None ? Verdict.Disabled : Verdict.Narrowed;
        }

        internal static Heightmap.Biome OriginalBiome(SpawnSystem.SpawnData entry)
        {
            return Originals.TryGetValue(entry, out Original original) ? original.Biome : entry.m_biome;
        }

        internal static bool IsEdited(SpawnSystem.SpawnData entry)
        {
            return Originals.TryGetValue(entry, out _);
        }

        private static int Suppress(List<SpawnSystem.SpawnData> entries, Rules rules)
        {
            int edited = 0;

            foreach (SpawnSystem.SpawnData entry in entries)
            {
                // Already off means someone else - the game, another mod - wants it off.
                // Leave it out of the books so restoring never switches it on.
                if (entry == null || !entry.m_enabled || IsEdited(entry))
                    continue;

                Verdict verdict = Classify(entry, rules, out Heightmap.Biome kept);
                if (verdict != Verdict.Disabled && verdict != Verdict.Narrowed)
                    continue;

                Originals.Add(entry, new Original { Biome = entry.m_biome });

                // Both cases are one edit. SpawnSystem skips an entry whose mask matches
                // none of the zone's corners before it reads the entry's timer, and
                // IsSpawnPointGood rejects any point outside the mask - so an empty mask
                // is a disabled entry, and m_enabled stays the game's to set.
                entry.m_biome = kept;
                edited++;
            }

            return edited;
        }

        private static int Restore(List<SpawnSystem.SpawnData> entries)
        {
            int restored = 0;

            foreach (SpawnSystem.SpawnData entry in entries)
            {
                if (entry == null || !Originals.TryGetValue(entry, out Original original))
                    continue;

                entry.m_biome = original.Biome;
                Originals.Remove(entry);
                restored++;
            }

            return restored;
        }

        /// <summary>
        /// Every list of entries that can be reached right now: the prefab's, the biome
        /// modifiers', and those of live zones in case they are clones. Deduplicated by
        /// reference, since in the expected case the live zones' are the prefab's.
        /// </summary>
        internal static List<List<SpawnSystem.SpawnData>> ReachableLists()
        {
            var lists = new List<List<SpawnSystem.SpawnData>>();

            void Add(List<SpawnSystem.SpawnData> entries)
            {
                if (entries != null && !lists.Contains(entries))
                    lists.Add(entries);
            }

            foreach (SpawnSystemList list in PrefabLists)
            {
                if (list != null)
                    Add(list.m_spawners);
            }

            foreach (AltBiome altBiome in AltBiomeList.m_altBiomes)
            {
                if (altBiome != null)
                    Add(altBiome.m_spawn);
            }

            foreach (SpawnSystem spawnSystem in LiveSpawnSystems())
            {
                if (spawnSystem == null)
                    continue;

                foreach (SpawnSystemList list in spawnSystem.m_spawnLists)
                {
                    if (list != null)
                        Add(list.m_spawners);
                }
            }

            return lists;
        }

        internal static List<SpawnSystem> LiveSpawnSystems()
        {
            return InstancesField?.GetValue(null) as List<SpawnSystem> ?? new List<SpawnSystem>();
        }
    }
}
