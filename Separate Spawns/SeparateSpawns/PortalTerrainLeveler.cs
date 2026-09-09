using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PortalTerrainLeveler
    {
        // Radii of the flat pad and of the blend ring around it.
        private const float LevelRadius = 5f;
        private const float SmoothRadius = 7f;

        // One pass lands the pad on target; the extra passes only exist to absorb the
        // smoothing delta that a first pass adds on top of the level delta. The loop
        // stops as soon as the centre vertex is within tolerance, so it normally runs
        // twice. See docs/valheim-1.0-terrainop.md.
        private const int MaxLevelPasses = 4;
        private const float LevelTolerance = 0.05f;

        private struct TerrainJob
        {
            public float X;
            public float Z;
            public float GroundY;
            public string GroupName;
            public bool IsSpawnEnd;
        }

        private static readonly List<TerrainJob> Pending = new List<TerrainJob>();
        private static readonly List<Heightmap> TouchedMaps = new List<Heightmap>();

        private static readonly TerrainOp.Settings LevelSettings = new TerrainOp.Settings
        {
            m_level = true,
            m_levelRadius = LevelRadius,
            m_levelOffset = 0f,
            m_square = false,
            m_raise = false,
            m_smooth = true,
            m_smoothRadius = SmoothRadius,
            m_smoothPower = 3f,
            m_paintCleared = false
        };

        private static MethodInfo _doOperation;
        private static bool _doOperationLookupDone;
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

                    var passes = 0;
                    for (var pass = 0; pass < MaxLevelPasses; pass++)
                    {
                        if (!ApplyLevelOperation(position, job.GroundY))
                        {
                            break;
                        }

                        passes++;

                        // ApplyLevelOperation regenerates every heightmap it touched, so this
                        // reads the terrain as it now stands rather than as it stood before.
                        if (Heightmap.GetHeight(position, out var vertexY) &&
                            Mathf.Abs(vertexY - job.GroundY) <= LevelTolerance)
                        {
                            break;
                        }
                    }

                    Heightmap.ForceGenerateAll();
                    PortalObstacleClearer.ClearAt(position);

                    var settledGround = PortalGroundHelper.MeasureGroundAt(position, job.GroundY);
                    FinalizePortalPlacement(job, settledGround);

                    var end = job.IsSpawnEnd ? "spawn" : "stones";
                    if (passes > 0)
                    {
                        // Both heights are logged on purpose: a gap between the target and
                        // the measured ground is the signal that leveling did not take.
                        ModLog.Info(
                            $"Leveled terrain under {job.GroupName} {end} portal at ({job.X:F0}, {job.Z:F0}) " +
                            $"in {passes} pass(es); target y={job.GroundY:F2}, measured y={settledGround:F2}.");
                    }
                    else
                    {
                        ModLog.Warning(
                            $"Could not level terrain under {job.GroupName} {end} portal at ({job.X:F0}, {job.Z:F0}); " +
                            $"placing it on unmodified ground at y={settledGround:F1}.");
                    }
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

        /// <summary>
        /// Applies one level+smooth pass straight to the terrain compilers under the portal
        /// and regenerates the heightmaps it touched. Returns false when nothing was applied.
        /// </summary>
        /// <remarks>
        /// Deliberately does not go through <see cref="TerrainOp"/>. Since 1.0.7 a TerrainOp's
        /// settings travel as a prefab-name hash that the receiver resolves through
        /// <c>ObjectDB.TryGetTerrainOp</c>, so an op built at runtime is dropped with
        /// "Failed to deserialize TerrainOp settings". See docs/valheim-1.0-terrainop.md.
        /// </remarks>
        private static bool ApplyLevelOperation(Vector3 position, float groundY)
        {
            var doOperation = ResolveDoOperation();
            if (doOperation == null)
            {
                return false;
            }

            var opPosition = new Vector3(position.x, groundY, position.z);

            TouchedMaps.Clear();
            Heightmap.FindHeightmap(opPosition, LevelSettings.GetRadius(), TouchedMaps);
            if (TouchedMaps.Count == 0)
            {
                return false;
            }

            var applied = 0;
            foreach (var map in TouchedMaps)
            {
                if (map == null)
                {
                    continue;
                }

                var comp = map.GetAndCreateTerrainCompiler();
                if (comp == null)
                {
                    continue;
                }

                // TerrainComp only persists the change if it owns its ZDO. During world
                // bootstrap the server just created it, but a repair pass can meet one a
                // client already owns.
                var nview = comp.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner())
                {
                    nview.ClaimOwnership();
                }

                try
                {
                    doOperation.Invoke(comp, new object[] { opPosition, Vector3.zero, LevelSettings });
                    applied++;
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Terrain level operation failed at ({opPosition.x:F0}, {opPosition.z:F0}): {ex.Message}");
                }
            }

            // TerrainComp.DoOperation only queues a rebuild for the next LateUpdate. Nothing
            // in this coroutine yields between passes, so force the rebuild now: the next
            // pass and the convergence check both read heights off the regenerated map.
            foreach (var map in TouchedMaps)
            {
                if (map != null)
                {
                    map.Poke(0);
                }
            }

            TouchedMaps.Clear();
            return applied > 0;
        }

        private static MethodInfo ResolveDoOperation()
        {
            if (_doOperationLookupDone)
            {
                return _doOperation;
            }

            _doOperationLookupDone = true;
            _doOperation = AccessTools.Method(
                typeof(TerrainComp),
                "DoOperation",
                new[] { typeof(Vector3), typeof(Vector3), typeof(TerrainOp.Settings) });

            if (_doOperation == null)
            {
                ModLog.Error(
                    "TerrainComp.DoOperation(Vector3, Vector3, TerrainOp.Settings) was not found; " +
                    "portal terrain leveling is disabled. The game's terrain API changed - see " +
                    "Separate Spawns/docs/valheim-1.0-terrainop.md.");
            }

            return _doOperation;
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
            var markers = UnityEngine.Object.FindObjectsOfType<GroupPortalMarker>();
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
