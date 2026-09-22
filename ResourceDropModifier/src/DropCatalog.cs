using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using SoftReferenceableAssets;
using UnityEngine;

namespace ResourceDropModifier
{
    /// <summary>
    /// One item as it appears in the config: its prefab name, the biome section it is
    /// filed under, and where it was seen dropping - written into the entry's
    /// description so an admin can tell "Resin" comes from beeches and greydwarves
    /// without opening the wiki.
    /// </summary>
    internal sealed class CatalogEntry
    {
        internal string PrefabName;
        internal string DisplayName;

        /// <summary>Every biome a source of this item was found in.</summary>
        internal Heightmap.Biome Biomes;

        /// <summary>
        /// False when every source of this item is flagged <c>m_dontScale</c>, in
        /// which case vanilla never routes it through the resource rate and the
        /// setting does nothing.
        /// </summary>
        internal bool Scalable;

        internal readonly SortedSet<string> Sources = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>The section the entry is filed under: the earliest biome in progression order.</summary>
        internal Heightmap.Biome Section
        {
            get
            {
                foreach (Heightmap.Biome biome in DropCatalog.BiomeOrder)
                {
                    if ((Biomes & biome) != 0)
                        return biome;
                }

                return Heightmap.Biome.None;
            }
        }
    }

    /// <summary>
    /// Walks the loaded game data and files every item that drops from something
    /// under a biome, so the config entries can be generated (and, after a game
    /// update, extended) rather than hand-written.
    /// <para>
    /// Three sources, each carrying a biome: the creature spawn lists, the
    /// vegetation list, and the location list (whose prefabs also lead to dungeon
    /// rooms, creature spawners and chests). <c>ObjectDB</c> is not a source: an item
    /// nothing drops gets no setting. See "Building the catalog" in docs/DESIGN.md.
    /// </para>
    /// </summary>
    internal static class DropCatalog
    {
        /// <summary>
        /// The order the biome sections are bound in, which is also the priority when
        /// an item drops in several biomes: it is filed under the earliest.
        /// </summary>
        internal static readonly Heightmap.Biome[] BiomeOrder =
        {
            Heightmap.Biome.Meadows,
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
            Heightmap.Biome.Ocean,
            Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands,
            Heightmap.Biome.DeepNorth,
            // No catch-all: an item no drop source claims - crafted gear, mostly -
            // is not listed. A chest that hands gear out is a drop source, so gear
            // found that way is listed under the chest's biome.
        };

        /// <summary>Builds the catalog from the currently loaded ObjectDB and ZoneSystem.</summary>
        internal static List<CatalogEntry> Build(ManualLogSource log)
        {
            var walk = new Walk(log);
            Stopwatch timer = Stopwatch.StartNew();

            int spawners = walk.SpawnLists();
            int vegetation = walk.Vegetation();
            int locations = walk.Locations();

            var entries = new List<CatalogEntry>(walk.Entries.Values);
            foreach (CatalogEntry entry in entries)
                entry.DisplayName = Localize(entry.PrefabName);

            log.LogInfo("Drop catalog: " + entries.Count + " item(s) from " + spawners + " spawner(s), "
                + vegetation + " vegetation prefab(s) and " + locations + " location(s) in "
                + timer.ElapsedMilliseconds + " ms.");

            return entries;
        }

        /// <summary>
        /// The in-game name of an item, or its prefab name when the token cannot be
        /// resolved. Localization lives in assembly_guiutils and may not initialise
        /// on a headless server; the fallback keeps the description readable.
        /// </summary>
        private static string Localize(string prefabName)
        {
            try
            {
                ObjectDB db = ObjectDB.instance;
                GameObject prefab = db != null ? db.GetItemPrefab(prefabName) : null;
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string token = item != null && item.m_itemData != null && item.m_itemData.m_shared != null
                    ? item.m_itemData.m_shared.m_name
                    : null;

                if (string.IsNullOrEmpty(token))
                    return prefabName;

                Localization localization = Localization.instance;
                string localized = localization != null ? localization.Localize(token) : null;
                if (string.IsNullOrEmpty(localized) || localized.StartsWith("[", StringComparison.Ordinal))
                    return token.TrimStart('$');

                return localized;
            }
            catch (Exception)
            {
                return prefabName;
            }
        }

        /// <summary>
        /// One pass over the game data. Holds the visited set so a prefab reached from
        /// several roots is walked once per root, not once per reference, and the
        /// entries being accumulated.
        /// </summary>
        private sealed class Walk
        {
            private const int MaxSourcesPerItem = 8;

            internal readonly Dictionary<string, CatalogEntry> Entries =
                new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);

            private readonly ManualLogSource _log;

            // Per root, so a Greydwarf reached from a Meadows spawner and a Black
            // Forest spawner records both biomes.
            private readonly HashSet<GameObject> _visited = new HashSet<GameObject>();

            internal Walk(ManualLogSource log)
            {
                _log = log;
            }

            // ---- sources --------------------------------------------------------

            internal int SpawnLists()
            {
                int count = 0;

                // The lists are serialised on the zone controller prefab. Resources
                // finds every loaded SpawnSystemList, prefab assets included, which
                // is what we want: the instance in the scene may not exist yet.
                foreach (SpawnSystemList list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
                {
                    if (list == null || list.m_spawners == null)
                        continue;

                    foreach (SpawnSystem.SpawnData spawner in list.m_spawners)
                    {
                        if (spawner == null || spawner.m_prefab == null || !spawner.m_enabled)
                            continue;

                        count++;
                        Root(spawner.m_prefab, spawner.m_biome);
                    }
                }

                return count;
            }

            internal int Vegetation()
            {
                int count = 0;
                ZoneSystem zones = ZoneSystem.instance;
                if (zones == null || zones.m_vegetation == null)
                    return 0;

                foreach (ZoneSystem.ZoneVegetation veg in zones.m_vegetation)
                {
                    if (veg == null || veg.m_prefab == null || !veg.m_enable)
                        continue;

                    count++;
                    Root(veg.m_prefab, veg.m_biome);
                }

                return count;
            }

            internal int Locations()
            {
                int count = 0;
                ZoneSystem zones = ZoneSystem.instance;
                if (zones == null || zones.m_locations == null)
                    return 0;

                foreach (ZoneSystem.ZoneLocation location in zones.m_locations)
                {
                    if (location == null || !location.m_enable)
                        continue;

                    GameObject prefab = Acquire(ref location.m_prefab, out bool loadedHere);
                    if (prefab == null)
                        continue;

                    try
                    {
                        count++;
                        Root(prefab, location.m_biome);
                    }
                    finally
                    {
                        if (loadedHere)
                            location.m_prefab.Release();
                    }
                }

                return count;
            }

            /// <summary>
            /// The asset behind a soft reference, loading it when it is not already
            /// resident. The caller releases it when <paramref name="loadedHere"/> is
            /// true, so the walk leaves memory the way it found it.
            /// </summary>
            private GameObject Acquire(ref SoftReference<GameObject> reference, out bool loadedHere)
            {
                loadedHere = false;
                if (!reference.IsValid)
                    return null;

                if (!reference.IsLoaded)
                {
                    try
                    {
                        if (reference.Load() != LoadResult.Succeeded)
                            return null;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning("Drop catalog: could not load " + reference.Name + ": " + ex.Message);
                        return null;
                    }

                    loadedHere = true;
                }

                return reference.Asset;
            }

            // ---- walking a prefab -----------------------------------------------

            private void Root(GameObject prefab, Heightmap.Biome biomes)
            {
                _visited.Clear();
                Follow(prefab, biomes);
            }

            private void Follow(GameObject prefab, Heightmap.Biome biomes)
            {
                if (prefab == null || !_visited.Add(prefab))
                    return;

                try
                {
                    Collect(prefab, biomes);
                }
                catch (Exception ex)
                {
                    // One odd prefab must not lose the rest of the catalog.
                    _log.LogWarning("Drop catalog: skipped " + prefab.name + ": " + ex.Message);
                }
            }

            private void Collect(GameObject root, Heightmap.Biome biomes)
            {
                foreach (CharacterDrop drops in root.GetComponentsInChildren<CharacterDrop>(true))
                {
                    foreach (CharacterDrop.Drop drop in drops.m_drops)
                        Add(drop.m_prefab, biomes, drops.gameObject, drop.m_dontScale);
                }

                foreach (DropOnDestroyed d in root.GetComponentsInChildren<DropOnDestroyed>(true))
                    AddTable(d.m_dropWhenDestroyed, biomes, d.gameObject);

                foreach (TreeBase tree in root.GetComponentsInChildren<TreeBase>(true))
                {
                    AddTable(tree.m_dropWhenDestroyed, biomes, tree.gameObject);
                    Follow(tree.m_logPrefab, biomes);
                }

                foreach (TreeLog log in root.GetComponentsInChildren<TreeLog>(true))
                {
                    AddTable(log.m_dropWhenDestroyed, biomes, log.gameObject);
                    Follow(log.m_subLogPrefab, biomes);
                }

                foreach (MineRock rock in root.GetComponentsInChildren<MineRock>(true))
                    AddTable(rock.m_dropItems, biomes, rock.gameObject);

                foreach (MineRock5 rock in root.GetComponentsInChildren<MineRock5>(true))
                    AddTable(rock.m_dropItems, biomes, rock.gameObject);

                foreach (Pickable pickable in root.GetComponentsInChildren<Pickable>(true))
                {
                    Add(pickable.m_itemPrefab, biomes, pickable.gameObject, pickable.m_dontScale);
                    AddTable(pickable.m_extraDrops, biomes, pickable.gameObject);
                }

                foreach (PickableItem pickable in root.GetComponentsInChildren<PickableItem>(true))
                {
                    if (pickable.m_itemPrefab != null)
                        Add(pickable.m_itemPrefab.gameObject, biomes, pickable.gameObject, false);

                    if (pickable.m_randomItemPrefabs != null)
                    {
                        // RandomItem is a struct, so no null check on the element.
                        foreach (PickableItem.RandomItem random in pickable.m_randomItemPrefabs)
                        {
                            if (random.m_itemPrefab != null)
                                Add(random.m_itemPrefab.gameObject, biomes, pickable.gameObject, false);
                        }
                    }
                }

                foreach (Container container in root.GetComponentsInChildren<Container>(true))
                    AddTable(container.m_defaultItems, biomes, container.gameObject);

                foreach (LootSpawner loot in root.GetComponentsInChildren<LootSpawner>(true))
                    AddTable(loot.m_items, biomes, loot.gameObject);

                foreach (Beehive hive in root.GetComponentsInChildren<Beehive>(true))
                {
                    if (hive.m_honeyItem != null)
                        Add(hive.m_honeyItem.gameObject, biomes, hive.gameObject, false);
                }

                foreach (SapCollector sap in root.GetComponentsInChildren<SapCollector>(true))
                {
                    if (sap.m_spawnItem != null)
                        Add(sap.m_spawnItem.gameObject, biomes, sap.gameObject, false);
                }

                foreach (Fish fish in root.GetComponentsInChildren<Fish>(true))
                    AddTable(fish.m_extraDrops, biomes, fish.gameObject);

                foreach (CreatureSpawner spawner in root.GetComponentsInChildren<CreatureSpawner>(true))
                    Follow(spawner.m_creaturePrefab, biomes);

                foreach (SpawnArea area in root.GetComponentsInChildren<SpawnArea>(true))
                {
                    if (area.m_prefabs == null)
                        continue;

                    foreach (SpawnArea.SpawnData data in area.m_prefabs)
                    {
                        if (data != null)
                            Follow(data.m_prefab, biomes);
                    }
                }

                foreach (Destructible destructible in root.GetComponentsInChildren<Destructible>(true))
                    Follow(destructible.m_spawnWhenDestroyed, biomes);

                foreach (Growup growup in root.GetComponentsInChildren<Growup>(true))
                {
                    Follow(growup.m_grownPrefab, biomes);
                    if (growup.m_altGrownPrefabs == null)
                        continue;

                    foreach (Growup.GrownEntry entry in growup.m_altGrownPrefabs)
                    {
                        if (entry != null)
                            Follow(entry.m_prefab, biomes);
                    }
                }

                foreach (DungeonGenerator generator in root.GetComponentsInChildren<DungeonGenerator>(true))
                    Rooms(generator, biomes);
            }

            /// <summary>
            /// Dungeon rooms are separate prefabs picked by theme, and they hold the
            /// crypt chests, loot piles and body piles. The location's biome is the
            /// room's biome; the generator's theme mask says which rooms it can use.
            /// </summary>
            private void Rooms(DungeonGenerator generator, Heightmap.Biome biomes)
            {
                List<DungeonDB.RoomData> rooms;
                try
                {
                    rooms = DungeonDB.GetRooms();
                }
                catch (Exception)
                {
                    return;
                }

                if (rooms == null)
                    return;

                foreach (DungeonDB.RoomData room in rooms)
                {
                    if (room == null || !room.m_enabled || (room.m_theme & generator.m_themes) == 0)
                        continue;

                    GameObject prefab = Acquire(ref room.m_prefab, out bool loadedHere);
                    if (prefab == null)
                        continue;

                    try
                    {
                        Follow(prefab, biomes);
                    }
                    finally
                    {
                        if (loadedHere)
                            room.m_prefab.Release();
                    }
                }
            }

            // ---- recording ------------------------------------------------------

            private void AddTable(DropTable table, Heightmap.Biome biomes, GameObject source)
            {
                if (table == null || table.m_drops == null)
                    return;

                foreach (DropTable.DropData drop in table.m_drops)
                    Add(drop.m_item, biomes, source, drop.m_dontScale);
            }

            /// <summary>
            /// Records a dropped prefab. A prefab without an <see cref="ItemDrop"/> is
            /// not an item - a creature that spawns another creature on death, say -
            /// and is followed instead.
            /// </summary>
            private void Add(GameObject prefab, Heightmap.Biome biomes, GameObject source, bool dontScale)
            {
                if (prefab == null)
                    return;

                if (prefab.GetComponent<ItemDrop>() == null)
                {
                    Follow(prefab, biomes);
                    return;
                }

                CatalogEntry entry;
                if (!Entries.TryGetValue(prefab.name, out entry))
                {
                    entry = new CatalogEntry { PrefabName = prefab.name };
                    Entries[prefab.name] = entry;
                }

                entry.Biomes |= biomes;
                entry.Scalable |= !dontScale;

                if (source != null && entry.Sources.Count < MaxSourcesPerItem)
                    entry.Sources.Add(SourceName(source));
            }

            private static string SourceName(GameObject source)
            {
                string name = source.name;
                if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - "(Clone)".Length);
                return name.Trim();
            }
        }
    }
}
