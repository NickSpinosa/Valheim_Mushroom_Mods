using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    internal static class GameFindSpawnPointPatch
    {
        private const float SpawnSyncTimeoutSeconds = 300f;
        private static float _spawnWaitStartedAt = -1f;
        private static bool _loggedSpawnSyncTimeout;

        /// <summary>
        /// Per-world state, so it is cleared on world teardown like the rest
        /// (docs/world-lifecycle.md). Left set, a wait that began in one session kept
        /// counting through the main menu: a player who gave up after three minutes and
        /// rejoined was dropped at the vanilla spawn seventeen seconds into the new
        /// session, with a "timed out after 300s" that was true only of the wall clock.
        /// </summary>
        internal static void ResetSpawnWait()
        {
            _spawnWaitStartedAt = -1f;
            _loggedSpawnSyncTimeout = false;
        }

        private static void Postfix(
            Game __instance,
            ref Vector3 point,
            ref bool __result,
            ref bool usedLogoutPoint,
            bool ___m_respawnAfterDeath)
        {
            // Only replace vanilla's StartTemple fallback. Leave logout and bed alone —
            // including while their zones are still streaming in (usedLogoutPoint is
            // false until logout succeeds; diverting early sends the player to Group Spawn).
            var profile = __instance.GetPlayerProfile();
            if (SpawnOverrideDecision.ShouldLeaveVanillaAlone(
                    ___m_respawnAfterDeath,
                    profile.HaveLogoutPoint(),
                    usedLogoutPoint,
                    profile.HaveCustomSpawnPoint()))
            {
                return;
            }

            if (Plugin.LayoutCache.Current?.Failed == true)
            {
                return;
            }

            var groupSpawn = GroupSpawnResolver.GetSpawnForLocalPlayer();
            if (!groupSpawn.HasValue)
            {
                if (GroupSpawnResolver.IsSeparateSpawnPending())
                {
                    if (_spawnWaitStartedAt < 0f)
                    {
                        _spawnWaitStartedAt = Time.time;
                    }

                    if (Time.time - _spawnWaitStartedAt < SpawnSyncTimeoutSeconds)
                    {
                        __result = false;
                        point = Vector3.zero;
                        return;
                    }

                    if (!_loggedSpawnSyncTimeout)
                    {
                        _loggedSpawnSyncTimeout = true;
                        ModLog.Error(
                            $"Timed out after {SpawnSyncTimeoutSeconds:F0}s waiting for roster/layout sync; falling back to vanilla spawn.");
                    }
                }
                else
                {
                    _spawnWaitStartedAt = -1f;
                }

                return;
            }

            _spawnWaitStartedAt = -1f;
            _loggedSpawnSyncTimeout = false;

            // Force the streaming system to load the group spawn zone instead of the stones.
            ZNet.instance.SetReferencePosition(groupSpawn.Value);
            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(groupSpawn.Value))
            {
                __result = false;
                point = Vector3.zero;
                return;
            }

            if (!ZoneSystem.instance.GetGroundHeight(groupSpawn.Value, out var height))
            {
                __result = false;
                point = Vector3.zero;
                return;
            }

            point = groupSpawn.Value;
            if (point.y < height)
            {
                point.y = height;
            }

            point.y += 0.25f;
            __result = true;
            WorldBootstrap.MarkWorldFrozen();
            ModLog.Info($"Spawning local player at group spawn ({point.x:F0}, {point.y:F1}, {point.z:F0}).");
        }
    }

    [HarmonyPatch(typeof(Game), "SpawnPlayer")]
    internal static class GameSpawnPlayerPatch
    {
        private static void Postfix()
        {
            WorldBootstrap.MarkWorldFrozen();
        }
    }
}
