using System.Collections.Generic;
using UnityEngine;

namespace HaldorExpansion
{
    /// <summary>
    /// Custom Wisp Torch sold by Haldor after the Queen: twice the visual size, 100 m
    /// demist radius. Cloned from <c>piece_groundtorch_mist</c> — no AssetBundle.
    ///
    /// **Two prefabs, not one.** The item Haldor sells and the thing that gets placed
    /// are separate objects:
    ///
    /// - <c>SuperMistTorch</c> — the item. Cloned from a vanilla item, so it has an
    ///   ItemDrop and a Rigidbody and no Piece. It is a Tool carrying its own one-entry
    ///   <see cref="PieceTable"/> on m_buildPieces, which is the only mechanism the game
    ///   has for entering build mode - Humanoid.SetupEquipment reads it off the
    ///   right-hand item and calls SetPlaceMode. The Hammer, Hoe and Cultivator are the
    ///   same shape. This is what keeps the torch placeable from the inventory (#39).
    /// - <c>SuperMistTorchPiece</c> — the piece. The scaled Wisp Torch clone, with no
    ///   ItemDrop at all. Its m_resources is the item x1, recoverable, so placing
    ///   consumes the purchase and hammer-removal refunds it.
    ///
    /// An earlier revision made one prefab both halves, the way vanilla food and mead
    /// do. That is a real vanilla shape, but it cannot work here, and the reason is
    /// structural rather than a detail that could be tuned - see the "Why one prefab
    /// cannot be both" section of <c>docs/placeable-item.md</c>. In short: a placed
    /// torch would satisfy <c>ItemDrop.IsPiece()</c>, and Player.UpdatePlacement routes
    /// anything that does down the Hammer's m_canRemoveFeasts branch, which is false -
    /// so it could not be removed with a hammer at all (#42).
    /// </summary>
    internal static class SuperMistTorch
    {
        /// <summary>
        /// The item. Kept as the mod's public name because the trade table and every
        /// saved config refer to it, and because the item is what Haldor sells.
        /// </summary>
        internal const string PrefabName = "SuperMistTorch";

        /// <summary>The placed object. Never sold, never in ObjectDB.</summary>
        internal const string PiecePrefabName = "SuperMistTorchPiece";

        private const string DisplayName = "Super Mist Torch";

        private const string Description =
            "A towering wisp torch. Clears mist in a vast circle around it.";

        /// <summary>Vanilla Wisp Torch piece. Not in ObjectDB — resolve from ZNetScene.</summary>
        private const string CloneSourcePiece = "piece_groundtorch_mist";

        /// <summary>
        /// Vanilla item the torch item is cloned from. Only its shape is wanted - every
        /// field that matters is overwritten below - so the one requirement is that it
        /// always exists. Wood does: EnsureRegistered already treats its absence as
        /// "this is the main-menu ObjectDB, come back later".
        ///
        /// Cloning a whole item prefab rather than copying ItemData onto the piece is
        /// what gives the item its Rigidbody and ZSyncTransform, so a dropped one falls
        /// and can be walked over. The cost is cosmetic: a dropped torch wears the
        /// donor's model until it is picked up. Purchases go straight to the inventory,
        /// so reaching that state means deliberately dropping a 100-coin placeable.
        /// </summary>
        private const string ItemCloneSource = "Wood";

        /// <summary>
        /// One per slot. Equippable items are not stacked anywhere in vanilla, and this
        /// one is consumed out of the inventory while it is equipped, so a stack would
        /// be testing two unusual things at once. The trade row sells one at a time.
        /// </summary>
        private const int MaxStackSize = 1;

        private const float ItemWeight = 10f;

        private const float VisualScale = 2f;

        /// <summary>
        /// Desired demist radius in metres. ParticleSystemForceField.endRange is local
        /// to the force-field transform, so after scaling the root we store
        /// WorldRadius / VisualScale and the world-space reach stays WorldRadius.
        /// </summary>
        private const float WorldDemistRadius = 100f;

        private static GameObject _itemPrefab;
        private static GameObject _piecePrefab;
        private static GameObject _prefabContainer;
        private static PieceTable _pieceTable;

        internal static GameObject Prefab => _itemPrefab;

        internal static GameObject PiecePrefab => _piecePrefab;

        internal static ItemDrop ItemDrop =>
            _itemPrefab != null ? _itemPrefab.GetComponent<ItemDrop>() : null;

        /// <summary>
        /// Re-applies the live Enabled flag to the torch's own piece table. Call after a
        /// config sync or any time the shop is queried, so turning the torch off
        /// mid-session also stops anyone still holding one from placing it.
        /// </summary>
        internal static void RefreshFromConfig()
        {
            ApplyEnabledToPieceTable();
        }

        /// <summary>
        /// Builds both prefabs once their sources are loadable, then registers the item
        /// with ObjectDB. Idempotent: CopyOtherDB replaces m_items, so we re-add every
        /// time. The piece is deliberately not registered here - it is not an item and
        /// has no business in the item database.
        /// </summary>
        internal static void EnsureRegistered(ObjectDB odb)
        {
            if (odb == null || odb.m_items == null) return;
            if (!EnsureBuilt()) return;

            if (!odb.m_items.Contains(_itemPrefab))
            {
                odb.m_items.Add(_itemPrefab);
                odb.UpdateRegisters();
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }

            ApplyEnabledToPieceTable();
        }

        /// <summary>
        /// Network registration for both prefabs: the piece so a placed torch
        /// replicates, the item so a dropped purchase does.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null) return;
            if (!EnsureBuilt()) return;

            RegisterWithScene(scene, _piecePrefab);
            RegisterWithScene(scene, _itemPrefab);

            EnsureRegistered(ObjectDB.instance);
        }

        private static void RegisterWithScene(ZNetScene scene, GameObject prefab)
        {
            if (prefab == null) return;

            if (!scene.m_prefabs.Contains(prefab))
                scene.m_prefabs.Add(prefab);

            int hash = prefab.name.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_namedPrefabs[hash] = prefab;
                Plugin.Log.LogInfo("Registered " + prefab.name + " with ZNetScene.");
            }
        }

        /// <summary>
        /// Teaches the piece so the torch's own build menu will list it. Without this a
        /// bought torch equips into an empty piece table.
        /// </summary>
        internal static void EnsureKnown(Player player)
        {
            if (player == null || _piecePrefab == null) return;
            if (Plugin.Settings != null && !Plugin.Settings.IsEnabled(TradeEntry))
                return;

            Piece piece = _piecePrefab.GetComponent<Piece>();
            if (piece == null) return;
            if (player.IsRecipeKnown(piece.m_name)) return;

            player.AddKnownPiece(piece);
        }

        /// <summary>
        /// The TradeTable row this prefab backs. Looked up by prefab name so config
        /// Enabled can hide it from the build menu as well as from Haldor.
        /// </summary>
        private static TradeEntry TradeEntry
        {
            get
            {
                foreach (var entry in TradeTable.Haldor)
                {
                    if (entry.PrefabName == PrefabName) return entry;
                }
                return null;
            }
        }

        // --- Building ---------------------------------------------------------

        /// <summary>
        /// Builds the piece, then the item, then wires them to each other. Returns false
        /// while either source is unavailable, which is an ordering state rather than a
        /// failure - a later registration pass retries.
        /// </summary>
        private static bool EnsureBuilt()
        {
            if (_itemPrefab != null && _piecePrefab != null) return true;

            EnsureContainer();

            if (_piecePrefab == null)
            {
                _piecePrefab = BuildPiecePrefab();
                if (_piecePrefab == null) return false;
            }

            if (_itemPrefab == null)
            {
                _itemPrefab = BuildItemPrefab();
                if (_itemPrefab == null) return false;
            }

            WirePieceToItem();
            return true;
        }

        private static void EnsureContainer()
        {
            if (_prefabContainer != null) return;

            _prefabContainer = new GameObject("HaldorExpansionPrefabs");
            _prefabContainer.SetActive(false);
            Object.DontDestroyOnLoad(_prefabContainer);
        }

        /// <summary>
        /// The placed object: the vanilla Wisp Torch, scaled up, demister widened, and
        /// carrying no ItemDrop. The absence is the point - see the class summary.
        /// </summary>
        private static GameObject BuildPiecePrefab()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return null;

            GameObject source = scene.GetPrefab(CloneSourcePiece);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PiecePrefabName + ": ZNetScene has no '" + CloneSourcePiece + "'.");
                return null;
            }

            GameObject clone = Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PiecePrefabName;
            clone.transform.localScale = source.transform.localScale * VisualScale;

            Piece piece = clone.GetComponent<Piece>();
            if (piece == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PiecePrefabName + ": '" + CloneSourcePiece + "' has no Piece.");
                Object.Destroy(clone);
                return null;
            }

            piece.m_name = DisplayName;
            piece.m_description = Description;
            // Vanilla Wisp Torch already places without a nearby station; keep that.
            piece.m_craftingStation = null;

            ConfigureDemisterRadius(clone);

            Plugin.Log.LogInfo(
                "Built " + PiecePrefabName + " from " + CloneSourcePiece
                + " (scale x" + VisualScale + ", demist " + WorldDemistRadius + " m).");
            return clone;
        }

        /// <summary>
        /// The item Haldor sells: a vanilla item clone carrying the torch's own name,
        /// icon and build menu.
        /// </summary>
        private static GameObject BuildItemPrefab()
        {
            ObjectDB odb = ObjectDB.instance;
            GameObject template = odb != null ? odb.GetItemPrefab(ItemCloneSource) : null;
            if (template == null)
            {
                // Not an error: the main-menu ObjectDB has no Wood either, and this runs
                // from ZNetScene.Awake, which can land before ObjectDB is populated. The
                // Game.Start backstop retries. Debug rather than silence so a build that
                // never happens can still be traced to the reason.
                Plugin.Log.LogDebug(
                    "Deferring " + PrefabName + " build: ObjectDB has no '"
                    + ItemCloneSource + "' to clone the item from yet.");
                return null;
            }

            if (template.GetComponent<ItemDrop>() == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": the '" + ItemCloneSource
                    + "' prefab has no ItemDrop to clone.");
                return null;
            }

            GameObject clone = Object.Instantiate(template, _prefabContainer.transform);
            clone.name = PrefabName;

            ItemDrop drop = clone.GetComponent<ItemDrop>();

            // ItemData and SharedData are plain [Serializable] classes, so Instantiate
            // deep-copied them and these edits cannot leak back into Wood. Building a
            // SharedData with `new` instead is the trap docs/trade-item-1.0.7.md
            // describes from the other side - Unity authors every array and string on a
            // serialized class non-null, and C# does not.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;

            // Tool, not Material: Humanoid.EquipItem only routes Tool (and weapons) to
            // the right hand, and SetupEquipment reads m_buildPieces off the right-hand
            // item to decide whether to enter build mode. A Material can never be held,
            // so a Material torch can never be placed from the inventory. See #39.
            shared.m_itemType = ItemDrop.ItemData.ItemType.Tool;
            shared.m_buildPieces = EnsurePieceTable();

            // Nothing about this item wears out or swings, and EquipItem refuses an item
            // whose durability has hit zero - which a Wood-derived template would never
            // set up correctly anyway.
            shared.m_useDurability = false;
            shared.m_maxQuality = 1;

            shared.m_maxStackSize = MaxStackSize;
            shared.m_weight = ItemWeight;

            // ItemData.GetIcon() indexes m_icons[m_variant] with no length check, so an
            // empty array is a throw in the shop list rather than a missing picture. The
            // piece's own build-menu icon is a picture of this torch, which is exactly
            // what the shop row and the inventory slot want; Wood's icon is the fallback.
            Piece piece = _piecePrefab != null ? _piecePrefab.GetComponent<Piece>() : null;
            if (piece != null && piece.m_icon != null) shared.m_icons = new[] { piece.m_icon };

            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_stack = 1;

            Plugin.Log.LogInfo("Built " + PrefabName + " from " + ItemCloneSource + ".");
            return clone;
        }

        /// <summary>
        /// Placing consumes the bought item; hammer-removal refunds it. Deferred until
        /// both prefabs exist because the requirement points at the item's ItemDrop.
        /// </summary>
        private static void WirePieceToItem()
        {
            Piece piece = _piecePrefab != null ? _piecePrefab.GetComponent<Piece>() : null;
            ItemDrop drop = ItemDrop;
            if (piece == null || drop == null) return;

            piece.m_resources = new[]
            {
                new Piece.Requirement
                {
                    m_resItem = drop,
                    m_amount = 1,
                    m_amountPerLevel = 0,
                    m_recover = true,
                },
            };
        }

        /// <summary>
        /// The torch's own build menu: one piece, its own table, nobody else's list.
        ///
        /// A PieceTable is a MonoBehaviour, so it needs a GameObject to live on; it goes
        /// under the inactive prefab container so nothing ticks. m_hideAdvancedMenu is
        /// what a one-entry table wants - tags, favourites and recents over a single
        /// piece are noise.
        ///
        /// m_canRemovePieces stays false: this table is for placing, and removal is the
        /// hammer's job. That costs nothing now that the placed object is a plain piece
        /// - Player.UpdatePlacement only diverts removal away from the hammer for
        /// objects whose ItemDrop.IsPiece() is true, and the piece prefab has no
        /// ItemDrop at all.
        /// </summary>
        private static PieceTable EnsurePieceTable()
        {
            if (_pieceTable != null)
            {
                return _pieceTable;
            }

            var host = new GameObject(PrefabName + "PieceTable");
            host.transform.SetParent(_prefabContainer.transform);

            Piece piece = _piecePrefab != null ? _piecePrefab.GetComponent<Piece>() : null;

            _pieceTable = host.AddComponent<PieceTable>();
            _pieceTable.m_pieces = new List<GameObject>();
            _pieceTable.m_categories = new List<Piece.PieceCategory>
            {
                piece != null ? piece.m_category : Piece.PieceCategory.Misc,
            };
            _pieceTable.m_canRemovePieces = false;
            _pieceTable.m_canRemoveFeasts = false;
            _pieceTable.m_hideAdvancedMenu = true;
            _pieceTable.m_skill = Skills.SkillType.None;

            return _pieceTable;
        }

        /// <summary>
        /// Enabled = false has to take the piece out of the torch's build menu, not just
        /// out of Haldor's stock - otherwise anyone already holding one keeps placing
        /// them after the setting is turned off.
        /// </summary>
        private static void ApplyEnabledToPieceTable()
        {
            if (_piecePrefab == null || _pieceTable == null) return;

            TradeEntry entry = TradeEntry;
            bool enabled = entry == null
                || Plugin.Settings == null
                || Plugin.Settings.IsEnabled(entry);

            bool inTable = _pieceTable.m_pieces.Contains(_piecePrefab);
            if (enabled && !inTable)
            {
                _pieceTable.m_pieces.Add(_piecePrefab);
            }
            else if (!enabled && inTable)
            {
                _pieceTable.m_pieces.Remove(_piecePrefab);
            }
        }

        /// <summary>
        /// Drops the torch out of the hand once the last one has been placed.
        ///
        /// Placing consumes the item that is doing the placing, which is a shape vanilla
        /// never has: no code path unequips an item that leaves the inventory, so the
        /// player would otherwise keep an unplaceable ghost and a phantom held item until
        /// they switched tools. Cheap because it returns immediately unless the right
        /// hand is a torch.
        /// </summary>
        internal static void UnequipIfDepleted(Player player)
        {
            if (player == null) return;

            ItemDrop.ItemData right = player.GetRightItem();
            if (right?.m_shared == null || right.m_shared.m_name != DisplayName) return;

            Inventory inventory = player.GetInventory();
            if (inventory == null || inventory.ContainsItem(right)) return;

            player.UnequipItem(right, false);
        }

        private static void ConfigureDemisterRadius(GameObject clone)
        {
            float localRadius = WorldDemistRadius / VisualScale;
            int tuned = 0;

            foreach (ParticleSystemForceField field in
                     clone.GetComponentsInChildren<ParticleSystemForceField>(true))
            {
                if (field == null) continue;
                // Keep the vanilla start/end ratio so the falloff shape still looks right.
                if (field.endRange > 0f)
                {
                    float ratio = field.startRange / field.endRange;
                    field.endRange = localRadius;
                    field.startRange = localRadius * ratio;
                }
                else
                {
                    field.endRange = localRadius;
                }
                tuned++;
            }

            if (tuned == 0)
            {
                Plugin.Log.LogWarning(
                    PiecePrefabName + " has no ParticleSystemForceField; demist radius was not changed.");
            }
        }
    }
}
