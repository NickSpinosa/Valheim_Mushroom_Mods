using System;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class GroupPortalMarker : MonoBehaviour
    {
        internal const string ActivateRpcName = "SeparateSpawns_ActivatePortal";
        public string GroupName;
        public bool IsSpawnEnd;
        public bool Activated;

        public const string ZdoGroupKey = "separate_spawns_group";
        public const string ZdoSpawnEndKey = "separate_spawns_spawn_end";
        public const string ZdoActivatedKey = "separate_spawns_activated";

        private ZNetView _nview;
        private bool _activateRpcRegistered;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            EnsureActivateRpcRegistered();
            LoadFromZdo();
        }

        internal void EnsureActivateRpcRegistered()
        {
            if (_activateRpcRegistered || _nview == null)
            {
                return;
            }

            _nview.Register(ActivateRpcName, new Action<long>(RPC_ActivatePortal));
            _activateRpcRegistered = true;
        }

        public void RPC_ActivatePortal(long sender)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            HandleLegacyActivateRpc(sender);
        }

        internal void HandleLegacyActivateRpc(long sender)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            PortalActivationSync.HandleActivation(sender, _nview.GetZDO().m_uid);
        }

        public void SyncFromZdo()
        {
            LoadFromZdo();
        }

        public static bool TryReadFromZdo(ZDO zdo, out string groupName, out bool isSpawnEnd, out bool activated)
        {
            groupName = null;
            isSpawnEnd = false;
            activated = false;

            if (zdo == null)
            {
                return false;
            }

            groupName = zdo.GetString(ZdoGroupKey);
            if (string.IsNullOrEmpty(groupName))
            {
                groupName = null;
                return false;
            }

            isSpawnEnd = zdo.GetBool(ZdoSpawnEndKey);
            activated = zdo.GetBool(ZdoActivatedKey);
            return true;
        }

        public static bool IsGroupPortal(string groupName)
        {
            return !string.IsNullOrEmpty(groupName);
        }

        // The marker's GroupName is not enough. That component can be copied
        // onto every crafted portal from the shared prefab, and a copied name
        // would lock the tag editor and the hammer. Only the instance ZDO key
        // means this portal was placed by the mod.
        public static bool IsProtectedPortal(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            return zdo != null && IsGroupPortal(zdo.GetString(ZdoGroupKey));
        }

        public static void StripMarkerFromSharedPrefabs()
        {
            var prefabs = Game.instance != null ? Game.instance.m_portalPrefabs : null;
            if (prefabs == null)
            {
                return;
            }

            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                var leaked = prefab.GetComponent<GroupPortalMarker>();
                if (leaked == null)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(leaked, allowDestroyingAssets: true);
                ModLog.Info("Removed GroupPortalMarker from the shared portal prefab so crafted portals stay ordinary pieces.");
            }
        }

        // Instance only. Never write Piece fields on Game.m_portalPrefabs — that
        // object is the piece players craft from the hammer.
        public static void ApplyHammerRemoval(GameObject go)
        {
            var piece = go != null ? go.GetComponent<Piece>() : null;
            if (piece == null)
            {
                return;
            }

            piece.m_canBeRemoved = !IsProtectedPortal(go);
        }

        public static GroupPortalMarker AttachFromZdoIfNeeded(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var existing = go.GetComponent<GroupPortalMarker>();
            if (existing != null)
            {
                var existingZdo = go.GetComponent<ZNetView>()?.GetZDO();
                if (existingZdo == null || !IsGroupPortal(existingZdo.GetString(ZdoGroupKey)))
                {
                    // Valid ZDO and no group key: this is a crafted portal that
                    // inherited a marker. Drop the copied name so it cannot lock
                    // the tag editor. No ZDO yet means wait — do not guess.
                    if (existingZdo != null)
                    {
                        existing.GroupName = null;
                    }

                    return null;
                }

                existing.LoadFromZdo();
                ApplyHammerRemoval(go);
                return existing;
            }

            var nview = go.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null)
            {
                return null;
            }

            var groupName = zdo.GetString(ZdoGroupKey);
            if (string.IsNullOrEmpty(groupName))
            {
                return null;
            }

            var marker = go.AddComponent<GroupPortalMarker>();
            marker.EnsureActivateRpcRegistered();
            marker.LoadFromZdo();
            ApplyHammerRemoval(go);
            return marker;
        }

        public void LoadFromZdo()
        {
            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            if (_nview?.GetZDO() == null)
            {
                return;
            }

            var zdo = _nview.GetZDO();
            var storedGroup = zdo.GetString(ZdoGroupKey);
            if (storedGroup.Length == 0)
            {
                return;
            }

            GroupName = storedGroup;
            IsSpawnEnd = zdo.GetBool(ZdoSpawnEndKey);
            Activated = zdo.GetBool(ZdoActivatedKey);
        }

        public void Initialize(string groupName, bool isSpawnEnd, bool activated)
        {
            GroupName = groupName;
            IsSpawnEnd = isSpawnEnd;
            Activated = activated;
            // Before the owner check. Awake already ran and would have treated
            // this instance as a crafted portal, because the group key is not
            // on the ZDO yet.
            ApplyHammerRemoval(gameObject);

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            if (_nview?.GetZDO() == null || !_nview.IsOwner())
            {
                return;
            }

            var zdo = _nview.GetZDO();
            zdo.Set(ZdoGroupKey, GroupName);
            zdo.Set(ZdoSpawnEndKey, IsSpawnEnd);
            zdo.Set(ZdoActivatedKey, Activated);
            PortalManager.ApplyGroupTag(zdo, GroupName);
        }

        public void SetActivated(bool activated)
        {
            Activated = activated;
            if (_nview?.GetZDO() != null && _nview.IsOwner())
            {
                _nview.GetZDO().Set(ZdoActivatedKey, activated);
            }
        }
    }
}
