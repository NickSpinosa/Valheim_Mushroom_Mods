using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(TeleportWorld), "Awake")]
    internal static class TeleportWorldAwakePatch
    {
        private static void Postfix(TeleportWorld __instance)
        {
            GroupPortalMarker.StripMarkerFromSharedPrefabs();
            GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            // Awake runs before a freshly placed group portal writes its ZDO key.
            // Initialize sets the flag afterwards. Loaded portals already have the
            // key, so this is what makes a crafted portal hammer-removable again
            // even if the shared prefab's Piece flag was flipped.
            GroupPortalMarker.ApplyHammerRemoval(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorldInteractPatch
    {
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, ref bool __result)
        {
            if (hold)
            {
                return true;
            }

            if (!GroupPortalMarker.IsProtectedPortal(__instance.gameObject))
            {
                return true;
            }

            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            // Group portals never open the vanilla tag editor.
            if (marker.IsSpawnEnd && !marker.Activated)
            {
                __result = PortalManager.TryActivatePortal(marker, human);
                return false;
            }

            if (!marker.Activated)
            {
                __result = false;
                human.Message(MessageHud.MessageType.Center, "This group portal is inactive.");
                return false;
            }

            __result = false;
            human.Message(MessageHud.MessageType.Center, "This portal's tag is fixed.");
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorldGetHoverTextPatch
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            if (!GroupPortalMarker.IsProtectedPortal(__instance.gameObject))
            {
                return;
            }

            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return;
            }

            var status = marker.Activated ? "active" : "inactive";
            var end = marker.IsSpawnEnd ? "spawn" : "stones";
            var action = marker.IsSpawnEnd && !marker.Activated
                ? "\n[<color=yellow><b>Use</b></color>] Activate with surtling cores"
                : "\nTag is fixed";
            __result = $"Portal tag:\"{marker.GroupName}\" ({end}, {status}){action}";
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.SetText))]
    internal static class TeleportWorldSetTextPatch
    {
        private static bool Prefix(TeleportWorld __instance)
        {
            return !GroupPortalMarker.IsProtectedPortal(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "RPC_SetTag")]
    internal static class TeleportWorldRpcSetTagPatch
    {
        private static bool Prefix(TeleportWorld __instance)
        {
            if (!GroupPortalMarker.IsProtectedPortal(__instance.gameObject))
            {
                return true;
            }

            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            // Keep the fixed group tag if something tries to overwrite it.
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                PortalManager.ApplyGroupTag(zdo, marker.GroupName);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorldTeleportPatch
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            if (!GroupPortalMarker.IsProtectedPortal(__instance.gameObject))
            {
                return true;
            }

            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            if (!marker.Activated)
            {
                player.Message(MessageHud.MessageType.Center, "This group portal is inactive.");
                return false;
            }

            if (!PortalManager.CanUsePortal(marker, player))
            {
                player.Message(MessageHud.MessageType.Center, "This portal belongs to another group.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
    internal static class WearNTearApplyDamagePatch
    {
        private static bool Prefix(WearNTear __instance, ref bool __result)
        {
            if (!GroupPortalMarker.IsProtectedPortal(__instance.gameObject))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    // Hammer dismantle is Player.RemovePiece, not WearNTear.ApplyDamage. The
    // damage prefix cannot stop or allow a middle-click / build-menu remove.
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    internal static class PlayerRemovePiecePatch
    {
        private static bool Prefix(Player __instance, int ___m_removeRayMask, Transform ___m_eye, ref bool __result)
        {
            if (GameCamera.instance == null || ___m_eye == null)
            {
                return true;
            }

            var camera = GameCamera.instance.transform;
            if (!Physics.Raycast(camera.position, camera.forward, out var hit, 50f, ___m_removeRayMask) ||
                Vector3.Distance(hit.point, ___m_eye.position) >= __instance.m_maxPlaceDistance)
            {
                return true;
            }

            var piece = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
            if (piece == null || !GroupPortalMarker.IsProtectedPortal(piece.gameObject))
            {
                return true;
            }

            __result = false;
            __instance.Message(MessageHud.MessageType.Center, "This group portal cannot be removed.");
            return false;
        }
    }
}
