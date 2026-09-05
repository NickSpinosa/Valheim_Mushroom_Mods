using System.Collections.Generic;
using UnityEngine;

namespace HornOfCalling
{
    /// <summary>
    /// Owns the mod's item: cloning it from a vanilla prefab, and registering it and
    /// its recipe with ObjectDB and ZNetScene.
    ///
    /// The item is cloned from the Horn of Celebration, which supplies the horn model,
    /// the icon and the one-handed hold pose. What the clone changes is its identity,
    /// and the sound it makes when used - see <see cref="HornSound"/>.
    /// </summary>
    internal static class HornItem
    {
        internal const string PrefabName = "HornOfCalling";

        /// <summary>Name shown in the inventory. Plain text, not a localization token -
        /// the mod ships no translation table, so a token would display raw.</summary>
        private const string DisplayName = "Horn of Calling";

        private const string Description = "Sound it, and be heard.";

        /// <summary>
        /// Vanilla item the horn borrows its model, icon and hold pose from: the Horn of
        /// Celebration. The prefab name does not resemble the in-game one - it is the
        /// anniversary tankard, and its localization token is $item_tankard_anniversary.
        /// </summary>
        private const string CloneSourceItem = "TankardAnniversary";

        /// <summary>Prefab name of the crafting station the recipe is bound to.</summary>
        private const string StationPrefabName = "piece_workbench";

        private const int MinStationLevel = 1;

        /// <summary>Craft cost: item name, amount for the first craft, extra per upgrade level.</summary>
        private static readonly (string Item, int Amount, int PerLevel)[] Cost =
        {
            // Prefab names, not display names, and the near-misses are real items:
            // "Bronze" is the bar ("BronzeScrap" is Scrap Bronze), and "DeerHide" is
            // specifically a deer's ("Hide" is the generic one).
            ("Bronze", 1, 1),
            ("DeerHide", 1, 1),
        };

        private static GameObject _prefab;
        private static GameObject _prefabContainer;
        private static Recipe _recipe;

        // --- Registration -------------------------------------------------------

        /// <summary>
        /// Adds the item to the given ObjectDB, building the prefab on first call.
        /// Safe to call repeatedly; ObjectDB.Awake and CopyOtherDB both fire more than once.
        /// </summary>
        internal static void EnsureRegistered(ObjectDB odb)
        {
            if (odb == null || odb.m_items == null) return;

            // ObjectDB also exists in the main menu in a stripped-down form. Anything
            // cloned from it there would be incomplete, so wait for the real one.
            if (odb.GetItemPrefab("Wood") == null) return;

            if (_prefab == null)
            {
                _prefab = BuildPrefab(odb);
                if (_prefab == null) return;
            }

            if (!odb.m_items.Contains(_prefab))
            {
                odb.m_items.Add(_prefab);
                odb.UpdateRegisters();
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }
        }

        /// <summary>
        /// Adds the crafting recipe. Separate from EnsureRegistered because it needs the
        /// crafting station, which is a *piece* and so only exists once ZNetScene has
        /// built its prefab list - later than the first ObjectDB.Awake.
        ///
        /// Deliberately tests the live recipe list rather than latching a "done" flag:
        /// ObjectDB.CopyOtherDB does `m_recipes = other.m_recipes`, replacing the list
        /// wholesale, so a recipe registered against the main-menu database is gone by
        /// the time a world finishes loading and has to be added again.
        /// </summary>
        internal static void EnsureRecipeRegistered(ObjectDB odb)
        {
            if (_prefab == null || odb == null || odb.m_recipes == null) return;

            ItemDrop item = _prefab.GetComponent<ItemDrop>();
            if (item == null) return;

            foreach (Recipe existing in odb.m_recipes)
            {
                if (existing != null && existing.m_item == item) return;
            }

            CraftingStation station = FindStation();
            if (station == null) return; // Not up yet; a later patch will retry.

            // Rebuilt rather than re-added, because the requirements reference ItemDrops
            // out of whichever ObjectDB is current, and that is the thing that just changed.
            if (_recipe != null) Object.Destroy(_recipe);

            var resources = new List<Piece.Requirement>();
            foreach ((string name, int amount, int perLevel) in Cost)
            {
                GameObject resourcePrefab = odb.GetItemPrefab(name);
                ItemDrop resource = resourcePrefab != null ? resourcePrefab.GetComponent<ItemDrop>() : null;
                if (resource == null)
                {
                    Plugin.Log.LogError(
                        "Cannot build the " + PrefabName + " recipe: no item prefab named " + name + ".");
                    return;
                }

                resources.Add(new Piece.Requirement
                {
                    m_resItem = resource,
                    m_amount = amount,
                    m_amountPerLevel = perLevel,
                    m_recover = true,
                });
            }

            _recipe = ScriptableObject.CreateInstance<Recipe>();
            _recipe.name = "Recipe_" + PrefabName;
            _recipe.m_item = item;
            _recipe.m_amount = 1;
            _recipe.m_enabled = true;
            _recipe.m_craftingStation = station;
            _recipe.m_repairStation = station;
            _recipe.m_minStationLevel = MinStationLevel;
            _recipe.m_resources = resources.ToArray();

            odb.m_recipes.Add(_recipe);
            Plugin.Log.LogInfo(
                "Registered the " + PrefabName + " recipe at " + StationPrefabName +
                " (" + odb.m_recipes.Count + " recipes).");
        }

        /// <summary>
        /// Registers the prefabs with ZNetScene so they can exist as networked objects:
        /// the item once dropped on the ground, and the blast effect once sounded.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null || _prefab == null) return;

            RegisterPrefab(scene, _prefab);

            // The blast effect is cloned from a vanilla sound and keeps its ZNetView, so
            // it takes a ZDO the moment it spawns. Unregistered, that ZDO reaches every
            // other client as a prefab hash they cannot resolve.
            if (HornSound.EffectPrefab != null) RegisterPrefab(scene, HornSound.EffectPrefab);
        }

        private static void RegisterPrefab(ZNetScene scene, GameObject prefab)
        {
            if (!scene.m_prefabs.Contains(prefab))
            {
                scene.m_prefabs.Add(prefab);
            }

            int hash = prefab.name.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_namedPrefabs[hash] = prefab;
                Plugin.Log.LogInfo("Registered " + prefab.name + " with ZNetScene.");
            }
        }

        // --- Construction -------------------------------------------------------

        /// <summary>
        /// Locates the crafting station component by prefab name.
        ///
        /// Searched over every loaded CraftingStation rather than pulled from
        /// ZNetScene, because the station is needed from several patch points and this
        /// works at all of them. Logs what it did find on failure - a station name that
        /// changed between game versions is otherwise a silent missing recipe.
        /// </summary>
        private static CraftingStation FindStation()
        {
            CraftingStation[] stations = Resources.FindObjectsOfTypeAll<CraftingStation>();
            if (stations == null || stations.Length == 0) return null;

            foreach (CraftingStation station in stations)
            {
                if (station != null && station.name == StationPrefabName) return station;
            }

            var names = new List<string>();
            foreach (CraftingStation station in stations)
            {
                if (station != null) names.Add(station.name);
            }
            Plugin.Log.LogWarning(
                "No crafting station prefab named '" + StationPrefabName + "'. Found: " +
                string.Join(", ", names.ToArray()));
            return null;
        }

        /// <summary>
        /// Zeroes the combat statistics inherited from the clone source.
        ///
        /// The horn stays an <c>ItemType.OneHandedWeapon</c>, because that is what makes
        /// it equip to the hand and run an attack on left click. The cost is that
        /// <c>ItemDrop.GetTooltip</c> renders a weapon stat block for that type - and
        /// every line of it is printed only when its field is above zero, so zeroing the
        /// fields is what removes the lines. There is no "no stats" flag.
        ///
        /// Switching to <c>ItemType.Tool</c> would skip the stat block, but Tool is
        /// two-handed as far as <c>AddHandedTip</c> is concerned, so it trades a stat
        /// block for a wrong line and a wrong hold.
        ///
        /// Written as a sweep rather than as "the fields that happen to be set today",
        /// so a value changed in a future game version cannot quietly put a line back.
        /// </summary>
        private static void StripWeaponStats(ItemDrop.ItemData.SharedData shared)
        {
            // Already zero on the Horn of Celebration; set so the sweep is complete.
            shared.m_damages = new HitData.DamageTypes();
            shared.m_damagesPerLevel = new HitData.DamageTypes();

            shared.m_attackForce = 0f;   // $item_knockback
            shared.m_backstabBonus = 0f; // $item_backstab

            // AddBlockTooltip gates each of its lines on its own field, so clearing
            // m_blockPower alone would leave the block force and parry lines behind.
            shared.m_blockPower = 0f;
            shared.m_blockPowerPerLevel = 0f;
            shared.m_deflectionForce = 0f;
            shared.m_deflectionForcePerLevel = 0f;
            shared.m_timedBlockBonus = 0f;
            shared.m_perfectBlockAdrenaline = 0f;
            shared.m_damageModifiers?.Clear();

            // Sounding a horn should not train the sword skill.
            shared.m_skillType = Skills.SkillType.None;
        }

        private static GameObject BuildPrefab(ObjectDB odb)
        {
            GameObject source = odb.GetItemPrefab(CloneSourceItem);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": no item prefab named " + CloneSourceItem + ".");
                return null;
            }

            // Instantiating into an inactive parent keeps Unity from running Awake on
            // the clone, so it behaves as a prefab rather than a live scene object.
            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject("HornOfCallingPrefabs");
                _prefabContainer.SetActive(false);
                Object.DontDestroyOnLoad(_prefabContainer);
            }

            GameObject clone = Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PrefabName;

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogError("Item prefab " + CloneSourceItem + " has no ItemDrop component.");
                Object.Destroy(clone);
                return null;
            }

            // SharedData is a plain [Serializable] class, so Instantiate deep-copied it
            // and these edits cannot leak back into the item we cloned from.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;

            // The Horn of Celebration is a weapon whose *ammunition is mead*. With
            // m_ammoType set, Attack.HaveAmmo refuses the swing outright unless a mead
            // is in the inventory, and Attack.UseAmmo then drinks one - the horn was
            // silent without mead and handed out a mead buff with it. Cleared, all three
            // ammo checks short-circuit to "fine": the horn sounds on its own and
            // consumes nothing.
            shared.m_ammoType = string.Empty;

            StripWeaponStats(shared);

            HornSound.Attach(shared, _prefabContainer.transform);

            Plugin.Log.LogInfo("Built " + PrefabName + " from " + CloneSourceItem + ".");
            return clone;
        }
    }
}
