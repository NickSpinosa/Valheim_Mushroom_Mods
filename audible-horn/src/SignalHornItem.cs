using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AudibleHorn
{
    /// <summary>
    /// Owns the Signal Horn prefab: cloning it from Odin's tankard, registering it
    /// with ObjectDB and ZNetScene, and adding its workbench recipe.
    ///
    /// The horn is a clone rather than a retexture of <c>TankardOdin</c> so that the
    /// vanilla tankard is left entirely alone - it stays craftable and keeps its own
    /// name. Everything that makes the tankard behave like a horn in the hand (its
    /// <c>ItemType.Tool</c>, its drink <c>m_attack</c>, its icons, its hold pose) is
    /// kept exactly as cloned; only the identity and the cost are ours.
    /// </summary>
    internal static class SignalHornItem
    {
        internal const string PrefabName = "SignalHorn";

        /// <summary>
        /// Vanilla item the horn is cloned from. <c>TankardOdin</c> (model
        /// <c>betahorn</c>) rather than <c>TankardAnniversary</c>: both are horn
        /// shaped, and this one is the plainer of the two.
        /// </summary>
        internal const string CloneSource = "TankardOdin";

        /// <summary>
        /// Name shown in the inventory, and how the mod recognises its own item.
        /// Fixed rather than configurable, for the reason the compass records: because
        /// <see cref="IsSignalHorn"/> matches on it, changing it would orphan every
        /// horn a player already holds, and two installs disagreeing would leave each
        /// side confidently right about a different name.
        /// </summary>
        internal const string DisplayName = "Signal Horn";

        internal const string Description =
            "A horn with a carrying voice. Sound it and those nearby will know where you are.";

        private const string RecipeName = "Recipe_SignalHorn";
        private const string WorkbenchPrefab = "piece_workbench";
        private const string PrefabContainerName = "AudibleHornPrefabs";

        /// <summary>
        /// Flip to true, run the game once, and the first successful prefab build
        /// dumps what came across from <see cref="CloneSource"/> at Info. The values
        /// belong in <c>docs/DESIGN.md</c> under "TankardOdin as cloned"; ticket 05
        /// needs the attack animation name and confirmation that the attack costs no
        /// stamina. Left off in the shipped build because it is a one-off answer to a
        /// question about the game's data, not something a player benefits from.
        /// </summary>
        private const bool DumpClonedAttack = false;

        /// <summary>ObjectDB.UpdateRegisters is private, and this project does not publicize.</summary>
        private static readonly MethodInfo UpdateRegistersMethod =
            AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");

        /// <summary>
        /// So is ZNetScene.m_namedPrefabs. Built in a static constructor rather than
        /// an initialiser because FieldRefAccess throws when the field is gone, and a
        /// throwing initialiser becomes a TypeInitializationException on every later
        /// touch of this class - turning a game update that renamed one field into a
        /// mod that cannot even log why it failed.
        /// </summary>
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabsRef;

        static SignalHornItem()
        {
            try
            {
                NamedPrefabsRef = AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");
            }
            catch (Exception)
            {
                NamedPrefabsRef = null;
            }
        }

        private static GameObject _prefab;
        private static GameObject _prefabContainer;
        private static bool _warnedNoStation;
        private static bool _warnedNoNamedPrefabs;

        /// <summary>
        /// True when the item is one of ours. Compared by shared name, which is unique
        /// to this mod and survives the horn being dropped, stored and picked back up.
        /// Null safe: callers reach this from patch bodies that see empty hands.
        /// </summary>
        internal static bool IsSignalHorn(ItemDrop.ItemData item)
        {
            return item?.m_shared != null
                && item.m_shared.m_name == DisplayName;
        }

        // --- Registration ---------------------------------------------------

        /// <summary>
        /// Adds the horn and its recipe to the given ObjectDB, building the prefab on
        /// first call. Safe to call repeatedly; ObjectDB.Awake and CopyOtherDB both
        /// fire more than once per session.
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
                UpdateRegisters(odb);
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }

            EnsureRecipe(odb);
        }

        /// <summary>
        /// Registers the prefab with ZNetScene so a dropped horn can exist as a
        /// networked object rather than vanishing on the next zone load.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null || _prefab == null) return;

            if (scene.m_prefabs != null && !scene.m_prefabs.Contains(_prefab))
            {
                scene.m_prefabs.Add(_prefab);
            }

            Dictionary<int, GameObject> named = NamedPrefabsRef?.Invoke(scene);
            if (named == null)
            {
                if (!_warnedNoNamedPrefabs)
                {
                    _warnedNoNamedPrefabs = true;
                    Plugin.Log.LogWarning(
                        "ZNetScene.m_namedPrefabs was not reachable; a dropped " + DisplayName +
                        " will not resolve by name. The game has probably changed.");
                }
                return;
            }

            int hash = PrefabName.GetStableHashCode();
            if (!named.ContainsKey(hash))
            {
                named[hash] = _prefab;
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ZNetScene.");
            }
        }

        /// <summary>
        /// ObjectDB keeps its own name and shared-data lookups, and only rebuilds them
        /// here. An item added to m_items without this is invisible to
        /// GetItemPrefab - which is how the inventory, the crafting UI and
        /// Inventory.AddItem all find it.
        /// </summary>
        private static void UpdateRegisters(ObjectDB odb)
        {
            if (UpdateRegistersMethod == null)
            {
                Plugin.Log.LogWarning(
                    "ObjectDB.UpdateRegisters was not found; " + PrefabName +
                    " may not be resolvable by name. The game has probably changed.");
                return;
            }

            UpdateRegistersMethod.Invoke(odb, null);
        }

        // --- The prefab -----------------------------------------------------

        private static GameObject BuildPrefab(ObjectDB odb)
        {
            GameObject source = odb.GetItemPrefab(CloneSource);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build the " + DisplayName + ": no item prefab named " +
                    CloneSource + " exists in this ObjectDB.");
                return null;
            }

            // Instantiating into an inactive parent keeps Unity from running Awake on
            // the clone, so it behaves as a prefab rather than a live scene object.
            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject(PrefabContainerName);
                _prefabContainer.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_prefabContainer);
            }

            GameObject clone = UnityEngine.Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PrefabName;

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogError("Item prefab " + CloneSource + " has no ItemDrop component.");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            // SharedData is a plain [Serializable] class, so Instantiate deep-copied it
            // and these edits cannot leak back into the tankard we cloned from.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;

            shared.m_name = DisplayName;
            shared.m_description = Description;
            shared.m_maxStackSize = 1;
            shared.m_value = 0;
            shared.m_teleportable = true;

            // A horn is not a tool that wears out. Repair must be off as well, or a
            // workbench would offer to mend an item that can never be damaged.
            shared.m_useDurability = false;
            shared.m_canBeReparied = false;

            // Deliberately kept as cloned: m_itemType (Tool), m_attack (the drink
            // emote ticket 05 rides on), m_icons, m_weight, m_equipEffect and
            // m_animationState. Those are what make it read and handle as a horn.

            // Stripped defensively. The tankard is a drink, and a Signal Horn that
            // quenched thirst or applied a status effect on equip would be a surprise.
            shared.m_food = 0f;
            shared.m_foodStamina = 0f;
            shared.m_foodEitr = 0f;
            shared.m_foodBurnTime = 0f;
            shared.m_foodRegen = 0f;
            shared.m_consumeStatusEffect = null;
            shared.m_equipStatusEffect = null;

            drop.m_itemData.m_stack = 1;

            // m_autoPickup is left exactly as cloned. The compass turns it off because
            // its carry rule can refuse a pickup and auto-pickup retries a refusal
            // every frame; nothing here ever refuses one, so there is no such trap.

            Plugin.Log.LogInfo("Built " + PrefabName + " from " + CloneSource + ".");

            // CS0162 suppressed rather than designed away: DumpClonedAttack is a const
            // so this call really is unreachable in the shipped build, which is the
            // point - the diagnostic costs nothing until a maintainer flips the flag.
            // Making it a static readonly bool would silence the warning by hiding the
            // value from the compiler, at the price of leaving the dump in the release.
#pragma warning disable CS0162
            if (DumpClonedAttack)
            {
                LogClonedAttack(shared);
            }
#pragma warning restore CS0162

            return clone;
        }

        /// <summary>
        /// The one-off diagnostic behind <see cref="DumpClonedAttack"/>. Reads only,
        /// and every field is reached defensively: this exists to answer questions
        /// about the game's data, so it must not itself throw and lose the answer.
        /// </summary>
        private static void LogClonedAttack(ItemDrop.ItemData.SharedData shared)
        {
            try
            {
                Plugin.Log.LogInfo("--- " + CloneSource + " as cloned ---");
                Plugin.Log.LogInfo("  m_itemType         = " + shared.m_itemType);
                Plugin.Log.LogInfo("  m_animationState   = " + shared.m_animationState);

                // Note: there is no m_holdAnimationState on SharedData in this build of
                // the game - see docs/DESIGN.md. m_attachOverride is the field that
                // actually decides how the item is carried, so it is dumped instead.
                Plugin.Log.LogInfo("  m_attachOverride   = " + shared.m_attachOverride);

                if (shared.m_attack == null)
                {
                    Plugin.Log.LogInfo("  m_attack           = <null>");
                }
                else
                {
                    Plugin.Log.LogInfo("  m_attack.m_attackAnimation = " + shared.m_attack.m_attackAnimation);
                    Plugin.Log.LogInfo("  m_attack.m_attackType      = " + shared.m_attack.m_attackType);
                    Plugin.Log.LogInfo("  m_attack.m_attackStamina   = " + shared.m_attack.m_attackStamina);
                }

                Plugin.Log.LogInfo("--- end " + CloneSource + " dump ---");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not dump the cloned attack: " + e.Message);
            }
        }

        // --- The recipe -----------------------------------------------------

        /// <summary>
        /// Adds the workbench recipe if it is not already there. Idempotent by
        /// checking m_item against our own ItemDrop rather than by name, since the
        /// recipe ScriptableObject we create is not the one a reloaded ObjectDB holds.
        /// </summary>
        private static void EnsureRecipe(ObjectDB odb)
        {
            if (odb.m_recipes == null || _prefab == null) return;

            ItemDrop drop = _prefab.GetComponent<ItemDrop>();
            if (drop == null) return;

            foreach (Recipe existing in odb.m_recipes)
            {
                if (existing != null && existing.m_item == drop) return;
            }

            CraftingStation station = FindWorkbench(odb);
            if (station == null)
            {
                // Not an error yet. EnsureRegistered runs again from ZNetScene.Awake
                // and Game.Start, and the ZNetScene fallback below resolves the bench
                // by then. Adding the recipe with a null station would silently make
                // the horn craftable bare-handed, which is worse than waiting.
                if (!_warnedNoStation)
                {
                    _warnedNoStation = true;
                    Plugin.Log.LogWarning(
                        "No " + WorkbenchPrefab + " CraftingStation found yet; deferring the " +
                        DisplayName + " recipe to a later registration pass.");
                }
                return;
            }

            List<Piece.Requirement> resources = new List<Piece.Requirement>();
            if (!TryAddRequirement(odb, resources, "BoneFragments", 4)) return;
            if (!TryAddRequirement(odb, resources, "LeatherScraps", 2)) return;
            if (!TryAddRequirement(odb, resources, "Resin", 1)) return;

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = RecipeName;
            recipe.m_item = drop;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = resources.ToArray();

            odb.m_recipes.Add(recipe);
            Plugin.Log.LogInfo("Registered the " + DisplayName + " recipe at the workbench.");
        }

        /// <summary>
        /// Resolves one ingredient. Returns false - and logs which one - if it is
        /// missing, so the caller can drop the whole recipe rather than register a
        /// cheaper one than intended.
        /// </summary>
        private static bool TryAddRequirement(
            ObjectDB odb, List<Piece.Requirement> into, string itemName, int amount)
        {
            ItemDrop resource = odb.GetItemPrefab(itemName)?.GetComponent<ItemDrop>();
            if (resource == null)
            {
                Plugin.Log.LogError(
                    "Recipe ingredient " + itemName + " was not found in ObjectDB; the " +
                    DisplayName + " recipe was not added.");
                return false;
            }

            into.Add(new Piece.Requirement
            {
                m_resItem = resource,
                m_amount = amount,
                m_amountPerLevel = 0,
                m_recover = true
            });
            return true;
        }

        /// <summary>
        /// The workbench CraftingStation, borrowed from whichever vanilla recipe
        /// already points at it.
        ///
        /// Scanning the recipes rather than the prefab list is what lets this work
        /// inside ObjectDB.Awake, which runs before ZNetScene exists - and a recipe
        /// whose station reference is a different object than the one vanilla recipes
        /// use would not be recognised as the same bench.
        /// </summary>
        private static CraftingStation FindWorkbench(ObjectDB odb)
        {
            foreach (Recipe recipe in odb.m_recipes)
            {
                if (recipe == null || recipe.m_craftingStation == null) continue;
                if (recipe.m_craftingStation.name == WorkbenchPrefab) return recipe.m_craftingStation;
            }

            // Fallback for the case where no vanilla recipe has loaded yet.
            GameObject prefab = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(WorkbenchPrefab)
                : null;
            return prefab != null ? prefab.GetComponent<CraftingStation>() : null;
        }
    }
}
