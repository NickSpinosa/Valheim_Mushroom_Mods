using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PortalManager
    {
        private const int MaxPlacementAttempts = 3;
        // Valheim portal pieces face -Z for interaction/teleport exit; offset so the usable side points at the target.
        private const float PortalFacingYOffset = 180f;

        public static void PlacePortals(WorldLayoutData layoutData, ModConfig config)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            if (Game.instance == null || Game.instance.m_portalPrefabs == null || Game.instance.m_portalPrefabs.Count == 0)
            {
                ModLog.Error("Cannot place group portals: Game.m_portalPrefabs is missing.");
                return;
            }

            if (layoutData.GroupSpawnPositions.Count == 0)
            {
                ModLog.Warning("Cannot place group portals: no group spawn positions.");
                return;
            }

            var stones = layoutData.SacrificialStonesPosition;
            if (stones == Vector3.zero)
            {
                ModLog.Error("Cannot place group portals: sacrificial stones position is unknown.");
                return;
            }

            var prefab = Game.instance.m_portalPrefabs[0];
            var groups = new List<string>(layoutData.GroupSpawnPositions.Keys);
            groups.Sort();

            RepairExistingPortals(layoutData, groups, stones, config);

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var spawnPos = layoutData.GroupSpawnPositions[group];
                var activated = layoutData.PortalActivated.TryGetValue(group, out var isActivated) && isActivated;

                if (PortalZdoExists(group, isSpawnEnd: true) && PortalZdoExists(group, isSpawnEnd: false))
                {
                    ModLog.Info($"Group portals for {group} already exist; skipping placement.");
                    continue;
                }

                GameObject spawnPortal = null;
                if (PortalZdoExists(group, isSpawnEnd: true))
                {
                    spawnPortal = FindExistingPortal(group, isSpawnEnd: true)?.gameObject;
                    if (spawnPortal != null)
                    {
                        FacePortalToward(spawnPortal, stones);
                    }

                    ModLog.Info($"Spawn portal ZDO for {group} already exists.");
                }
                else
                {
                    spawnPortal = TryCreatePortalNear(
                        prefab,
                        spawnPos,
                        group,
                        isSpawnEnd: true,
                        activated,
                        MaxPlacementAttempts,
                        preferredY: spawnPos.y,
                        lookAt: stones);
                }

                GameObject stonesPortal = null;
                if (PortalZdoExists(group, isSpawnEnd: false))
                {
                    stonesPortal = FindExistingPortal(group, isSpawnEnd: false)?.gameObject;
                    if (stonesPortal != null)
                    {
                        FacePortalToward(stonesPortal, stones);
                    }

                    ModLog.Info($"Stones portal ZDO for {group} already exists.");
                }
                else
                {
                    var stonesTarget = GetStonesCirclePosition(stones, groupIndex, groups.Count, config.PortalStonesRadius.Value);
                    stonesPortal = TryCreatePortalNear(
                        prefab,
                        stonesTarget,
                        group,
                        isSpawnEnd: false,
                        activated,
                        MaxPlacementAttempts,
                        preferredY: stones.y,
                        lookAt: stones);
                }

                if ((spawnPortal == null && !PortalZdoExists(group, isSpawnEnd: true)) ||
                    (stonesPortal == null && !PortalZdoExists(group, isSpawnEnd: false)))
                {
                    ModLog.Error(
                        $"Failed to place full portal pair for {group} (spawn={(spawnPortal != null || PortalZdoExists(group, true))}, stones={(stonesPortal != null || PortalZdoExists(group, false))}).");
                    continue;
                }

                if (spawnPortal != null && stonesPortal != null)
                {
                    FacePortalToward(spawnPortal, stones);
                    FacePortalToward(stonesPortal, stones);
                    ConnectPortals(spawnPortal, stonesPortal, activated);
                    ModLog.Info(
                        $"Placed portal pair for {group}: spawn=({spawnPortal.transform.position.x:F0}, {spawnPortal.transform.position.y:F1}, {spawnPortal.transform.position.z:F0}), stones=({stonesPortal.transform.position.x:F0}, {stonesPortal.transform.position.y:F1}, {stonesPortal.transform.position.z:F0}).");
                }
                else
                {
                    ModLog.Info($"Portal pair for {group} is present as ZDOs (zone may not be loaded yet).");
                }
            }
        }

        public static bool TryActivatePortal(GroupPortalMarker marker, Humanoid user)
        {
            if (!marker.IsSpawnEnd || marker.Activated)
            {
                return false;
            }

            var player = user as Player ?? Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            if (!IsPlayerInGroup(player, marker.GroupName))
            {
                user.Message(MessageHud.MessageType.Center, "This portal belongs to another group.");
                return true;
            }

            if (!ZNet.instance.IsServer())
            {
                return PortalActivationSync.RequestActivation(marker);
            }

            return TryActivatePortalServer(marker, player);
        }

        public static bool TryActivatePortalServer(GroupPortalMarker marker, Player player)
        {
            if (marker == null || player == null)
            {
                return false;
            }

            var nview = marker.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo != null)
            {
                return TryActivatePortalServer(zdo, player);
            }

            if (!marker.IsSpawnEnd || marker.Activated || !ZNet.instance.IsServer())
            {
                return false;
            }

            return TryActivatePortalServerForGroup(marker.GroupName, player);
        }

        public static bool TryActivatePortalServer(ZDO portalZdo, Player player)
        {
            if (portalZdo == null || player == null || !ZNet.instance.IsServer())
            {
                return false;
            }

            if (!GroupPortalMarker.TryReadFromZdo(portalZdo, out var groupName, out var isSpawnEnd, out var activated))
            {
                return false;
            }

            if (!isSpawnEnd || activated)
            {
                return false;
            }

            return TryActivatePortalServerForGroup(groupName, player);
        }

        private static bool TryActivatePortalServerForGroup(string groupName, Player player)
        {
            if (string.IsNullOrEmpty(groupName) || player == null || !ZNet.instance.IsServer())
            {
                return false;
            }

            if (!IsPlayerInGroup(player, groupName))
            {
                var platformId = PlatformIdHelper.GetPlatformUserIdForPlayer(player);
                ModLog.Info(
                    $"Portal activation rejected for {player.GetPlayerName()} ({platformId}): not in group {groupName}.");
                player.Message(MessageHud.MessageType.Center, "This portal belongs to another group.");
                return true;
            }

            if (!TryConsumePortalCost(player, out var failureMessage))
            {
                if (!string.IsNullOrEmpty(failureMessage))
                {
                    player.Message(MessageHud.MessageType.Center, failureMessage);
                }

                return true;
            }

            FinalizePortalActivation(groupName, GetPlayerPeerId(player));
            return true;
        }

        internal static long GetPlayerPeerId(Player player)
        {
            if (player == null || ZNet.instance == null)
            {
                return 0L;
            }

            var nview = player.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                return nview.GetZDO().GetOwner();
            }

            return 0L;
        }

        public static bool CanUsePortal(GroupPortalMarker marker, Player player)
        {
            if (marker == null || player == null)
            {
                return false;
            }

            if (!marker.Activated)
            {
                return false;
            }

            return IsPlayerInGroup(player, marker.GroupName);
        }

        public static void ApplyGroupTag(ZDO zdo, string groupName)
        {
            if (zdo == null || string.IsNullOrEmpty(groupName))
            {
                return;
            }

            zdo.Set(ZDOVars.s_tag, groupName);
        }

        private static void RepairExistingPortals(WorldLayoutData layoutData, List<string> groups, Vector3 stones, ModConfig config)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                foreach (var zdo in ZDOMan.instance.GetPortalList())
                {
                    if (zdo.GetString(GroupPortalMarker.ZdoGroupKey) != group)
                    {
                        continue;
                    }

                    ApplyGroupTag(zdo, group);

                    var isSpawnEnd = zdo.GetBool(GroupPortalMarker.ZdoSpawnEndKey);
                    var prefab = Game.instance.m_portalPrefabs[0];
                    float? preferredY;
                    Vector3 target;
                    if (isSpawnEnd)
                    {
                        if (!layoutData.GroupSpawnPositions.TryGetValue(group, out var spawnPos))
                        {
                            continue;
                        }

                        preferredY = spawnPos.y;
                        target = spawnPos;
                    }
                    else
                    {
                        preferredY = stones.y;
                        target = GetStonesCirclePosition(stones, groupIndex, groups.Count, config.PortalStonesRadius.Value);
                    }

                    var groundY = PortalGroundHelper.ResolveGroundY(target, preferredY);
                    var desiredPivot = PortalGroundHelper.PivotForGround(prefab, target, groundY);
                    var position = zdo.GetPosition();
                    var xzDrift = Vector2.Distance(
                        new Vector2(position.x, position.z),
                        new Vector2(desiredPivot.x, desiredPivot.z));
                    var yDrift = Mathf.Abs(position.y - desiredPivot.y);

                    if (xzDrift > (isSpawnEnd ? 8f : config.PortalStonesRadius.Value * 0.75f) || yDrift > 0.5f)
                    {
                        PortalGroundHelper.AlignZdoToGround(zdo, prefab, groundY);
                        ModLog.Info(
                            $"Repaired {group} {(isSpawnEnd ? "spawn" : "stones")} portal position to ({desiredPivot.x:F0}, {groundY:F1}, {desiredPivot.z:F0}).");

                        var marker = FindExistingPortal(group, isSpawnEnd);
                        if (marker != null)
                        {
                            PortalGroundHelper.AlignInstanceToGround(marker.gameObject, prefab, groundY);
                        }

                        LevelTerrainUnderPortal(target, groundY, group, isSpawnEnd);
                    }
                    else
                    {
                        LevelTerrainUnderPortal(target, groundY, group, isSpawnEnd);
                    }
                }

                if (layoutData.PortalActivated.TryGetValue(group, out var groupActivated) && groupActivated)
                {
                    ReconnectActivatedGroupPortals(group);
                }
            }
        }

        private static void ReconnectActivatedGroupPortals(string groupName)
        {
            if (ZDOMan.instance == null || string.IsNullOrEmpty(groupName))
            {
                return;
            }

            ZDO spawnZdo = null;
            ZDO stonesZdo = null;
            foreach (var zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo.GetString(GroupPortalMarker.ZdoGroupKey) != groupName)
                {
                    continue;
                }

                ApplyGroupTag(zdo, groupName);
                zdo.Set(GroupPortalMarker.ZdoActivatedKey, true);
                if (zdo.GetBool(GroupPortalMarker.ZdoSpawnEndKey))
                {
                    spawnZdo = zdo;
                }
                else
                {
                    stonesZdo = zdo;
                }
            }

            if (spawnZdo != null && stonesZdo != null)
            {
                ConnectPortalZdos(spawnZdo, stonesZdo);
                ModLog.Info($"Reconnected activated portal pair for {groupName}.");
            }
        }

        private static bool IsPlayerInGroup(Player player, string groupName)
        {
            var platformId = PlatformIdHelper.GetPlatformUserIdForPlayer(player);
            return IsPlatformUserInGroup(platformId, groupName);
        }

        internal static bool IsPeerInGroup(long peerId, string groupName)
        {
            var platformId = PlatformIdHelper.GetPlatformUserIdFromPeer(peerId);
            return IsPlatformUserInGroup(platformId, groupName);
        }

        private static bool IsPlatformUserInGroup(string platformId, string groupName)
        {
            var group = GroupSpawnResolver.GetGroupNameForPlatformUser(platformId);
            return !string.IsNullOrEmpty(group) && group == groupName;
        }

        internal static void MessagePeer(long peerId, MessageHud.MessageType type, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var player = PlatformIdHelper.GetPlayerFromPeerId(peerId);
            if (player != null)
            {
                player.Message(type, message);
                return;
            }

            if (ZNet.instance == null)
            {
                return;
            }

            var peer = ZNet.instance.GetPeer(peerId);
            if (peer?.m_rpc != null)
            {
                ZNet.instance.RemotePrint(peer.m_rpc, message);
            }
        }

        internal static bool TryConsumePortalCost(Player player, out string failureMessage)
        {
            failureMessage = null;
            if (player == null)
            {
                return false;
            }

            var cost = Plugin.ConfigValues.PortalCoreCost.Value;
            if (TryConsumeSurtlingCores(player, cost))
            {
                return true;
            }

            failureMessage = $"Requires {cost} surtling cores.";
            return false;
        }

        internal static void FinalizePortalActivation(string groupName, long peerId)
        {
            if (string.IsNullOrEmpty(groupName) || !ZNet.instance.IsServer())
            {
                return;
            }

            ModLog.Info($"Activating group portal {groupName} for peer {peerId}.");
            ActivateGroupPortals(groupName);
            MessagePeer(peerId, MessageHud.MessageType.Center, "Group portal activated.");
            if (peerId != 0L && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, PortalActivationSync.CommittedRpcName, groupName);
            }
        }

        internal static void RefreshClientPortalState(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            foreach (var marker in Object.FindObjectsOfType<GroupPortalMarker>())
            {
                if (marker.GroupName != groupName)
                {
                    continue;
                }

                marker.SyncFromZdo();
            }
        }

        private static bool TryConsumeSurtlingCores(Player player, int amount)
        {
            if (player == null)
            {
                return false;
            }

            var itemName = ResolveSurtlingCoreItemName(Plugin.ConfigValues.SurtlingCoreItemName.Value);
            var inventory = player.GetInventory();
            var available = CountSurtlingCores(inventory, itemName);
            if (available < amount)
            {
                ModLog.Info(
                    $"Portal activation rejected for {player.GetPlayerName()}: have {available}/{amount} {itemName} (world level {Game.m_worldLevel}).");
                return false;
            }

            inventory.RemoveItem(itemName, amount, -1, worldLevelBased: false);
            return true;
        }

        private static string ResolveSurtlingCoreItemName(string configuredName)
        {
            if (ObjectDB.instance == null || string.IsNullOrWhiteSpace(configuredName))
            {
                return configuredName;
            }

            var prefab = ObjectDB.instance.GetItemPrefab(configuredName.Trim());
            if (prefab == null)
            {
                return configuredName.Trim();
            }

            var itemDrop = prefab.GetComponent<ItemDrop>();
            return itemDrop?.m_itemData.m_shared.m_name ?? configuredName.Trim();
        }

        private static int CountSurtlingCores(Inventory inventory, string itemName)
        {
            if (inventory == null || string.IsNullOrEmpty(itemName))
            {
                return 0;
            }

            // Ignore world level so older cores still count (same pattern as boss offerings).
            return inventory.CountItems(itemName, -1, matchWorldLevel: false);
        }

        private static void ActivateGroupPortals(string groupName)
        {
            ZDO spawnZdo = null;
            ZDO stonesZdo = null;

            if (ZDOMan.instance != null)
            {
                foreach (var zdo in ZDOMan.instance.GetPortalList())
                {
                    if (zdo.GetString(GroupPortalMarker.ZdoGroupKey) != groupName)
                    {
                        continue;
                    }

                    ApplyGroupTag(zdo, groupName);
                    zdo.Set(GroupPortalMarker.ZdoActivatedKey, true);

                    if (zdo.GetBool(GroupPortalMarker.ZdoSpawnEndKey))
                    {
                        spawnZdo = zdo;
                    }
                    else
                    {
                        stonesZdo = zdo;
                    }
                }
            }

            if (spawnZdo != null && stonesZdo != null)
            {
                ConnectPortalZdos(spawnZdo, stonesZdo);
            }
            else
            {
                ModLog.Warning(
                    $"Activated group {groupName} but portal pair is incomplete (spawn={spawnZdo != null}, stones={stonesZdo != null}).");
            }

            foreach (var marker in Object.FindObjectsOfType<GroupPortalMarker>())
            {
                if (marker.GroupName != groupName)
                {
                    continue;
                }

                marker.Activated = true;
                marker.SetActivated(true);
            }

            if (Plugin.LayoutCache.Current != null)
            {
                Plugin.LayoutCache.Current.PortalActivated[groupName] = true;
                if (ZNet.instance.GetWorldUID() != 0)
                {
                    WorldLayoutStore.Save(ZNet.instance.GetWorldUID(), Plugin.LayoutCache.Current);
                }
            }
        }

        private static Vector3 GetStonesCirclePosition(Vector3 stones, int groupIndex, int groupCount, float radius)
        {
            var angle = groupIndex * Mathf.PI * 2f / Mathf.Max(1, groupCount);
            return stones + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        private static GameObject TryCreatePortalNear(GameObject prefab, Vector3 center, string groupName, bool isSpawnEnd,
            bool activated, int maxAttempts, float? preferredY, Vector3? lookAt)
        {
            var candidates = BuildPlacementCandidates(center, maxAttempts);
            for (var attempt = 0; attempt < candidates.Count; attempt++)
            {
                var candidate = candidates[attempt];
                var groundY = PortalGroundHelper.ResolveGroundY(candidate, preferredY);
                if (!IsValidPortalGround(groundY))
                {
                    ModLog.Warning(
                        $"Portal placement attempt {attempt + 1}/{candidates.Count} for {groupName} ({(isSpawnEnd ? "spawn" : "stones")}) rejected at ({candidate.x:F0}, {groundY:F1}, {candidate.z:F0}) (underwater/invalid).");
                    continue;
                }

                var portal = CreatePortal(prefab, candidate, groundY, groupName, isSpawnEnd, activated, lookAt);
                if (portal != null)
                {
                    if (attempt > 0)
                    {
                        ModLog.Info(
                            $"Placed {groupName} {(isSpawnEnd ? "spawn" : "stones")} portal on attempt {attempt + 1}/{candidates.Count}.");
                    }

                    return portal;
                }

                ModLog.Warning(
                    $"Portal placement attempt {attempt + 1}/{candidates.Count} for {groupName} ({(isSpawnEnd ? "spawn" : "stones")}) failed to instantiate.");
            }

            return null;
        }

        private static List<Vector3> BuildPlacementCandidates(Vector3 center, int maxAttempts)
        {
            var candidates = new List<Vector3> { center };
            var offsets = new[]
            {
                new Vector3(4f, 0f, 0f),
                new Vector3(-4f, 0f, 0f),
                new Vector3(0f, 0f, 4f),
                new Vector3(0f, 0f, -4f),
                new Vector3(6f, 0f, 6f),
                new Vector3(-6f, 0f, 6f),
                new Vector3(6f, 0f, -6f),
                new Vector3(-6f, 0f, -6f)
            };

            foreach (var offset in offsets)
            {
                if (candidates.Count >= maxAttempts)
                {
                    break;
                }

                candidates.Add(center + offset);
            }

            return candidates;
        }

        private static bool IsValidPortalGround(float groundY)
        {
            return groundY > ValheimHeights.WaterSurface;
        }

        private static GameObject CreatePortal(GameObject prefab, Vector3 xzPosition, float groundY, string groupName, bool isSpawnEnd, bool activated,
            Vector3? lookAt)
        {
            var position = PortalGroundHelper.PivotForGround(prefab, xzPosition, groundY);
            var rotation = GetFacingRotation(position, lookAt);
            var go = Object.Instantiate(prefab, position, rotation);
            if (go == null)
            {
                return null;
            }

            var marker = go.GetComponent<GroupPortalMarker>() ?? go.AddComponent<GroupPortalMarker>();
            marker.Initialize(groupName, isSpawnEnd, activated);

            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                ApplyGroupTag(zdo, groupName);
                zdo.SetRotation(rotation);
            }

            LevelTerrainUnderPortal(xzPosition, groundY, groupName, isSpawnEnd);
            PortalObstacleClearer.ClearAt(position);
            return go;
        }

        private static void FacePortalToward(GameObject portal, Vector3 lookAt)
        {
            if (portal == null)
            {
                return;
            }

            var rotation = GetFacingRotation(portal.transform.position, lookAt);
            portal.transform.rotation = rotation;
            var zdo = portal.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                zdo.SetRotation(rotation);
            }

            PortalObstacleClearer.ClearAt(portal.transform.position);
        }

        private static Quaternion GetFacingRotation(Vector3 from, Vector3? lookAt)
        {
            if (!lookAt.HasValue)
            {
                return Quaternion.identity;
            }

            var flat = lookAt.Value - from;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(flat.normalized, Vector3.up) *
                   Quaternion.Euler(0f, PortalFacingYOffset, 0f);
        }

        private static void LevelTerrainUnderPortal(Vector3 xzPosition, float groundY, string groupName, bool isSpawnEnd)
        {
            PortalTerrainLeveler.Queue(xzPosition, groundY, groupName, isSpawnEnd);
        }

        private static GroupPortalMarker FindExistingPortal(string groupName, bool isSpawnEnd)
        {
            var markers = Object.FindObjectsOfType<GroupPortalMarker>();
            foreach (var marker in markers)
            {
                if (marker.GroupName == groupName && marker.IsSpawnEnd == isSpawnEnd)
                {
                    return marker;
                }
            }

            return null;
        }

        private static bool PortalZdoExists(string groupName, bool isSpawnEnd)
        {
            if (ZDOMan.instance == null)
            {
                return false;
            }

            foreach (var zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo.GetString(GroupPortalMarker.ZdoGroupKey) == groupName &&
                    zdo.GetBool(GroupPortalMarker.ZdoSpawnEndKey) == isSpawnEnd)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConnectPortals(GameObject spawnPortal, GameObject stonesPortal, bool activated)
        {
            if (!activated)
            {
                return;
            }

            var spawnView = spawnPortal.GetComponent<ZNetView>();
            var stonesView = stonesPortal.GetComponent<ZNetView>();
            if (spawnView?.GetZDO() == null || stonesView?.GetZDO() == null)
            {
                return;
            }

            ConnectPortalZdos(spawnView.GetZDO(), stonesView.GetZDO());
        }

        private static void ConnectPortalZdos(ZDO spawnZdo, ZDO stonesZdo)
        {
            PortalConnectionHelper.ConnectPair(spawnZdo, stonesZdo);
        }
    }
}
