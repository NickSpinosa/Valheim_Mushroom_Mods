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
    /// supplies the material the hammer consumes when you place it.
    /// </summary>
    internal static class SuperMistTorch
    {
        internal const string PrefabName = "SuperMistTorch";

        private const string DisplayName = "Super Mist Torch";

        private const string Description =
            "A towering wisp torch. Clears mist in a vast circle around it.";

        /// <summary>Vanilla Wisp Torch piece. Not in ObjectDB — resolve from ZNetScene.</summary>
        private const string CloneSourcePiece = "piece_groundtorch_mist";

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

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": '" + CloneSourcePiece + "' has no ItemDrop.");
                Object.Destroy(clone);
                return null;
            }

            Piece piece = clone.GetComponent<Piece>();
            if (piece == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": '" + CloneSourcePiece + "' has no Piece.");
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
