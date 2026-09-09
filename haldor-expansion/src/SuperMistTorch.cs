using System.Collections.Generic;
using UnityEngine;

namespace HaldorExpansion
{
    /// <summary>
    /// Custom Wisp Torch sold by Haldor after the Queen: twice the visual size, 100 m
    /// demist radius. Cloned from <c>piece_groundtorch_mist</c> — no AssetBundle.
    ///
    /// Placement: the prefab is both the inventory item (ObjectDB / trader) and the
    /// hammer piece. Its recipe costs one of itself, so buying from Haldor is what
    /// supplies the material the hammer consumes when you place it. That is not a trick -
    /// it is how every vanilla food and mead is built, and the game drives the handover
    /// itself: Player.PlacePiece calls ItemDrop.MakePiece() on what it just placed.
    ///
    /// The clone source supplies only the piece half. <c>piece_groundtorch_mist</c> is a
    /// pure build piece and has no ItemDrop, so the item half is added here - see
    /// <c>docs/placeable-item.md</c>.
    /// </summary>
    internal static class SuperMistTorch
    {
        internal const string PrefabName = "SuperMistTorch";

        private const string DisplayName = "Super Mist Torch";

        private const string Description =
            "A towering wisp torch. Clears mist in a vast circle around it.";

        /// <summary>Vanilla Wisp Torch piece. Not in ObjectDB — resolve from ZNetScene.</summary>
        private const string CloneSourcePiece = "piece_groundtorch_mist";

        /// <summary>
        /// Vanilla item the torch's ItemData is copied from. Only its shape is wanted -
        /// every field is either overwritten below or is a sane default Unity authored -
        /// so the one requirement is that it always exists. Wood does: EnsureRegistered
        /// already treats its absence as "this is the main-menu ObjectDB, come back later".
        /// </summary>
        private const string ItemDataTemplate = "Wood";

        /// <summary>How many fit in an inventory slot. A judgment call, not a design
        /// requirement: the trade row sells one at a time either way.</summary>
        private const int MaxStackSize = 10;

        private const float ItemWeight = 10f;

        private const float VisualScale = 2f;

        /// <summary>
        /// Desired demist radius in metres. ParticleSystemForceField.endRange is local
        /// to the force-field transform, so after scaling the root we store
        /// WorldRadius / VisualScale and the world-space reach stays WorldRadius.
        /// </summary>
        private const float WorldDemistRadius = 100f;

        private static GameObject _prefab;
        private static GameObject _prefabContainer;

        internal static GameObject Prefab => _prefab;

        internal static ItemDrop ItemDrop =>
            _prefab != null ? _prefab.GetComponent<ItemDrop>() : null;

        /// <summary>
        /// Re-applies hammer membership from the live Enabled flag. Call after a
        /// config sync or any time the shop is queried so an in-session toggle
        /// does not leave a stale hammer entry behind.
        /// </summary>
        internal static void RefreshFromConfig()
        {
            EnsureInHammer();
        }

        /// <summary>
        /// Builds the prefab once the vanilla piece is loadable, then registers it with
        /// ObjectDB. Idempotent: CopyOtherDB replaces m_items, so we re-add every time.
        /// </summary>
        internal static void EnsureRegistered(ObjectDB odb)
        {
            if (odb == null || odb.m_items == null) return;
            if (odb.GetItemPrefab("Wood") == null) return;

            if (_prefab == null)
            {
                _prefab = BuildPrefab();
                if (_prefab == null) return;
            }

            if (!odb.m_items.Contains(_prefab))
            {
                odb.m_items.Add(_prefab);
                odb.UpdateRegisters();
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }

            EnsureInHammer();
        }

        /// <summary>
        /// Network registration so a placed torch (and a dropped purchase) replicate.
        /// Source piece lives in ZNetScene, so this is also the earliest moment we can
        /// build the clone if ObjectDB somehow ran first without it.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null) return;

            if (_prefab == null)
            {
                _prefab = BuildPrefab();
                if (_prefab == null) return;
            }

            if (!scene.m_prefabs.Contains(_prefab))
                scene.m_prefabs.Add(_prefab);

            int hash = _prefab.name.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_namedPrefabs[hash] = _prefab;
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ZNetScene.");
            }

            EnsureRegistered(ObjectDB.instance);
            EnsureInHammer();
        }

        /// <summary>
        /// Makes the hammer show the piece. Without this, a bought torch sits in the
        /// inventory with no way to place it.
        /// </summary>
        internal static void EnsureKnown(Player player)
        {
            if (player == null || _prefab == null) return;
            if (Plugin.Settings != null && !Plugin.Settings.IsEnabled(TradeEntry))
                return;

            Piece piece = _prefab.GetComponent<Piece>();
            if (piece == null) return;
            if (player.IsRecipeKnown(piece.m_name)) return;

            player.AddKnownPiece(piece);
        }

        /// <summary>
        /// The TradeTable row this prefab backs. Looked up by prefab name so config
        /// Enabled can hide it from the hammer as well as from Haldor.
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

        private static void EnsureInHammer()
        {
            if (_prefab == null) return;

            TradeEntry entry = TradeEntry;
            bool enabled = entry == null
                || Plugin.Settings == null
                || Plugin.Settings.IsEnabled(entry);

            foreach (PieceTable table in GetHammerTables())
            {
                bool inTable = table.m_pieces.Contains(_prefab);
                if (enabled && !inTable)
                {
                    table.m_pieces.Add(_prefab);
                    Plugin.Log.LogInfo("Added " + PrefabName + " to the Hammer piece table.");
                }
                else if (!enabled && inTable)
                {
                    table.m_pieces.Remove(_prefab);
                }
            }
        }

        private static List<PieceTable> GetHammerTables()
        {
            var tables = new List<PieceTable>();
            var seen = new HashSet<int>();

            void TryAdd(GameObject hammer)
            {
                PieceTable table = hammer != null
                    ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces
                    : null;
                if (table == null) return;
                if (seen.Add(table.GetInstanceID())) tables.Add(table);
            }

            if (ZNetScene.instance != null)
                TryAdd(ZNetScene.instance.GetPrefab("Hammer"));
            if (ObjectDB.instance != null)
                TryAdd(ObjectDB.instance.GetItemPrefab("Hammer"));

            return tables;
        }

        private static GameObject BuildPrefab()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return null;

            GameObject source = scene.GetPrefab(CloneSourcePiece);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": ZNetScene has no '" + CloneSourcePiece + "'.");
                return null;
            }

            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject("HaldorExpansionPrefabs");
                _prefabContainer.SetActive(false);
                Object.DontDestroyOnLoad(_prefabContainer);
            }

            GameObject clone = Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PrefabName;
            clone.transform.localScale = source.transform.localScale * VisualScale;

            Piece piece = clone.GetComponent<Piece>();
            if (piece == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": '" + CloneSourcePiece + "' has no Piece.");
                Object.Destroy(clone);
                return null;
            }

            // The source is a pure build piece and carries no ItemDrop, so the item half
            // is added here. See docs/placeable-item.md for why one prefab is both.
            ItemDrop drop = clone.GetComponent<ItemDrop>() ?? AddItemHalf(clone, piece);
            if (drop == null)
            {
                Object.Destroy(clone);
                return null;
            }

            // SharedData is deep-copied by Instantiate; edits stay on the clone.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;
            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_stack = 1;

            piece.m_name = DisplayName;
            piece.m_description = Description;
            // Placement consumes the bought item; hammer-remove refunds it.
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
            // Vanilla Wisp Torch already places without a nearby station; keep that.
            piece.m_craftingStation = null;

            ConfigureDemisterRadius(clone);

            Plugin.Log.LogInfo(
                "Built " + PrefabName + " from " + CloneSourcePiece
                + " (scale x" + VisualScale + ", demist " + WorldDemistRadius + " m).");
            return clone;
        }

        /// <summary>
        /// Gives the piece clone its item half.
        ///
        /// Returns null while ObjectDB has nothing to copy from, which is an ordering
        /// state rather than a failure - a later registration pass builds the prefab.
        /// </summary>
        private static ItemDrop AddItemHalf(GameObject clone, Piece piece)
        {
            ObjectDB odb = ObjectDB.instance;
            GameObject template = odb != null ? odb.GetItemPrefab(ItemDataTemplate) : null;
            if (template == null)
            {
                // Not an error: the main-menu ObjectDB has no Wood either, and this runs
                // from ZNetScene.Awake, which can land before ObjectDB is populated. The
                // Game.Start backstop retries. Debug rather than silence so a build that
                // never happens can still be traced to the reason.
                Plugin.Log.LogDebug(
                    "Deferring " + PrefabName + " build: ObjectDB has no '"
                    + ItemDataTemplate + "' to copy item data from yet.");
                return null;
            }

            if (template.GetComponent<ItemDrop>() == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": the '" + ItemDataTemplate
                    + "' prefab has no ItemDrop to copy item data from.");
                return null;
            }

            // Instantiate is what makes the copy safe to edit: ItemData and SharedData are
            // plain [Serializable] classes, so Unity deep-copies them, and the scratch
            // object can be thrown away while the data lives on. Building a SharedData with
            // `new` is the trap docs/trade-item-1.0.7.md describes from the other side -
            // Unity authors every array and string on a serialized class non-null, and C#
            // does not, so a hand-built one throws somewhere far from here.
            GameObject scratch = Object.Instantiate(template, _prefabContainer.transform);
            ItemDrop.ItemData data = scratch.GetComponent<ItemDrop>().m_itemData;
            Object.Destroy(scratch);

            ItemDrop drop = clone.AddComponent<ItemDrop>();
            drop.m_itemData = data;

            ItemDrop.ItemData.SharedData shared = data.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_maxStackSize = MaxStackSize;
            shared.m_weight = ItemWeight;

            // ItemData.GetIcon() indexes m_icons[m_variant] with no length check, so an
            // empty array is a throw in the shop list rather than a missing picture. The
            // piece's own hammer icon is a picture of this torch, which is exactly what the
            // shop row and the inventory slot want; the template's icon is the fallback.
            if (piece.m_icon != null) shared.m_icons = new[] { piece.m_icon };

            return drop;
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
                    PrefabName + " has no ParticleSystemForceField; demist radius was not changed.");
            }
        }
    }
}
