using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PortalTerrainLeveler
    {
        private struct TerrainJob
        {
            public float X;
            public float Z;
            public float GroundY;
            public string GroupName;
            public bool IsSpawnEnd;
        }

        private static readonly List<TerrainJob> Pending = new List<TerrainJob>();
        private static bool _running;

        public static void Queue(Vector3 pivotPosition, float groundY, string groupName, bool isSpawnEnd)
        {
            Pending.Add(new TerrainJob
            {
                X = pivotPosition.x,
                Z = pivotPosition.z,
                GroundY = groundY,
                GroupName = groupName,
                IsSpawnEnd = isSpawnEnd
            });
            EnsureRunning();
        }

        private static void EnsureRunning()
        {
            if (_running || Plugin.Instance == null)
            {
                return;
            }

            _running = true;
            Plugin.Instance.StartCoroutine(ProcessQueue());
        }

        private static IEnumerator ProcessQueue()
        {
            const int maxLevelPasses = 10;
            var attempts = 0;

            while (Pending.Count > 0 && attempts < 240)
            {
                attempts++;
                var remaining = new List<TerrainJob>();

                foreach (var job in Pending)
                {
                    var position = new Vector3(job.X, job.GroundY, job.Z);
                    EnsureZonesLoaded(position);

                    var heightmap = Heightmap.FindHeightmap(position);
                    if (heightmap == null)
                    {
                        remaining.Add(job);
                        continue;
                    }

                    if (Heightmap.HaveQueuedRebuild(position, 8f))
                    {
                        Heightmap.ForceGenerateAll();
                    }

                    for (var pass = 0; pass < maxLevelPasses; pass++)
                    {
                        ApplyLevelOperation(position, job.GroundY);
                    }

                    heightmap.Poke(0);
                    Heightmap.ForceGenerateAll();
                    PortalObstacleClearer.ClearAt(position);

                    var settledGround = PortalGroundHelper.MeasureGroundAt(position, job.GroundY);
                    FinalizePortalPlacement(job, settledGround);
                    ModLog.Info(
                        $"Leveled terrain under {job.GroupName} {(job.IsSpawnEnd ? "spawn" : "stones")} portal at ({job.X:F0}, {settledGround:F1}, {job.Z:F0}).");
                }

                Pending.Clear();
                Pending.AddRange(remaining);

                if (Pending.Count == 0)
                {
                    break;
                }

                if (ZNet.instance != null && Pending.Count > 0)
                {
                    var next = Pending[0];
                    ZNet.instance.SetReferencePosition(new Vector3(next.X, next.GroundY, next.Z));
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (Pending.Count > 0)
            {
                ModLog.Warning($"Timed out leveling terrain under {Pending.Count} portal(s); heightmaps never became ready.");
                foreach (var job in Pending)
                {
                    FinalizePortalPlacement(job, job.GroundY);
                }

                Pending.Clear();
            }

            _running = false;
        }

        private static void EnsureZonesLoaded(Vector3 position)
        {
            if (ZNet.instance == null || ZoneSystem.instance == null)
            {
                return;
            }

            ZNet.instance.SetReferencePosition(position);

            var createLocalZones = AccessTools.Method(typeof(ZoneSystem), "CreateLocalZones", new[] { typeof(Vector3) });
            createLocalZones?.Invoke(ZoneSystem.instance, new object[] { position });

            if (ZNet.instance.IsServer())
            {
                var createGhostZones = AccessTools.Method(typeof(ZoneSystem), "CreateGhostZones", new[] { typeof(Vector3) });
                createGhostZones?.Invoke(ZoneSystem.instance, new object[] { position });
            }
        }

        private static void ApplyLevelOperation(Vector3 position, float groundY)
        {
            var go = new GameObject("SeparateSpawns_PortalTerrainLevel");
            go.SetActive(false);
            go.transform.position = new Vector3(position.x, groundY, position.z);
            var op = go.AddComponent<TerrainOp>();
            op.m_settings.m_level = true;
            op.m_settings.m_levelRadius = 5f;
            op.m_settings.m_levelOffset = 0f;
            op.m_settings.m_square = false;
            op.m_settings.m_smooth = true;
            op.m_settings.m_smoothRadius = 7f;
            op.m_settings.m_smoothPower = 3f;
            op.m_settings.m_paintCleared = false;
            go.SetActive(true);
        }

        private static void FinalizePortalPlacement(TerrainJob job, float groundY)
        {
            var prefab = Game.instance?.m_portalPrefabs != null && Game.instance.m_portalPrefabs.Count > 0
                ? Game.instance.m_portalPrefabs[0]
                : null;

            var marker = FindPortalMarker(job.GroupName, job.IsSpawnEnd);
            if (marker != null)
            {
                PortalGroundHelper.AlignInstanceToGround(marker.gameObject, prefab, groundY);
                PortalObstacleClearer.ClearAt(marker.transform.position);
                return;
            }

            var zdo = FindPortalZdo(job.GroupName, job.IsSpawnEnd);
            if (zdo != null)
            {
                PortalGroundHelper.AlignZdoToGround(zdo, prefab, groundY);
            }
        }

        private static GroupPortalMarker FindPortalMarker(string groupName, bool isSpawnEnd)
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

        private static ZDO FindPortalZdo(string groupName, bool isSpawnEnd)
        {
            if (ZDOMan.instance == null)
            {
                return null;
            }

            foreach (var zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo.GetString(GroupPortalMarker.ZdoGroupKey) == groupName &&
                    zdo.GetBool(GroupPortalMarker.ZdoSpawnEndKey) == isSpawnEnd)
                {
                    return zdo;
                }
            }

            return null;
        }
    }
}
