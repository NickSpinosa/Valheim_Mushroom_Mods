using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class CandidateSpawnPoint
    {
        public Vector3 Position;
        public int MeadowsPatchId;
        public int AdjacentForestPatchId;
        public int IslandId;
        public int NearbyBurialChambers;
        public float MeadowsAreaSquareMeters;
        public Vector3? ExistingEikthyr;
    }

    internal sealed class LayoutAssignment
    {
        public Dictionary<string, CandidateSpawnPoint> GroupSpawns = new Dictionary<string, CandidateSpawnPoint>();
        public float Score;
        public float IslandScore;
        public float DistanceScore;
        public float MeadowsSizeScore;
        public float ClosestSpawnDistance;
        public float AverageMeadowsAreaSquareMeters;
        public bool Complete;
        public int GroupsPlaced;
    }

    internal sealed class LayoutGenerationResult
    {
        public List<LayoutAssignment> Layouts = new List<LayoutAssignment>();
        public int TotalAttempts;
        public int ValidLayouts;
        public LayoutAssignment LastAttempt = new LayoutAssignment();
        public LayoutAssignment BestPartialAttempt = new LayoutAssignment();
    }

    internal sealed class WorldLayoutData
    {
        public bool Frozen;
        public bool Failed;
        public string FailureReason;
        public Dictionary<string, Vector3> GroupSpawnPositions = new Dictionary<string, Vector3>();
        public Dictionary<string, Vector3> SpawnedEikthyrPositions = new Dictionary<string, Vector3>();
        public Dictionary<string, bool> PortalActivated = new Dictionary<string, bool>();
        public List<LayoutAssignment> TopLayouts = new List<LayoutAssignment>();
        public Vector3 SacrificialStonesPosition;
        public List<Vector3> BurialChamberPositions = new List<Vector3>();
        public List<Vector3> EikthyrAltarPositions = new List<Vector3>();
    }

    /// <summary>
    /// Holds the layout for the world that is loaded right now, and only that world.
    ///
    /// The cache lives for the whole process, but a layout does not: group spawn
    /// positions are coordinates under one seed and are meaningless under another. The
    /// world UID is recorded when a layout is set and checked on every read, so a
    /// layout that outlives its world stops being served rather than placing players at
    /// another world's coordinates. See issue #38.
    /// </summary>
    internal sealed class WorldLayoutCache
    {
        private WorldLayoutData _data;
        private long _worldUid;
        private bool _loggedWrongWorld;

        /// <summary>
        /// The layout for the loaded world, or null when there is none - including when
        /// a layout is held for a different world. Null makes callers fall back to
        /// vanilla spawning, which is the right answer for a world this mod has not
        /// laid out.
        /// </summary>
        public WorldLayoutData Current
        {
            get
            {
                if (_data == null)
                {
                    return null;
                }

                var uid = CurrentWorldUid();
                if (uid.HasValue && uid.Value == _worldUid)
                {
                    return _data;
                }

                LogWrongWorldOnce(uid);
                return null;
            }
        }

        /// <summary>World UID the held layout belongs to; 0 when nothing is held.</summary>
        public long WorldUid => _data == null ? 0L : _worldUid;

        public void Set(WorldLayoutData data)
        {
            _data = data;
            _worldUid = CurrentWorldUid() ?? 0L;
            _loggedWrongWorld = false;
        }

        /// <summary>Drops the held layout. Called when a world unloads.</summary>
        public void Clear()
        {
            _data = null;
            _worldUid = 0L;
            _loggedWrongWorld = false;
        }

        public Vector3? GetSpawnForGroup(string groupName)
        {
            var current = Current;
            if (current == null || string.IsNullOrEmpty(groupName))
            {
                return null;
            }

            if (current.GroupSpawnPositions.TryGetValue(groupName, out var position))
            {
                return position;
            }

            return null;
        }

        /// <summary>
        /// The loaded world's UID, or null when no world is loaded. Goes through
        /// <c>GetWorldName</c> first because <c>ZNet.GetWorldUID</c> dereferences
        /// <c>m_world</c> without a null check.
        /// </summary>
        private static long? CurrentWorldUid()
        {
            var znet = ZNet.instance;
            if (znet == null || znet.GetWorldName() == null)
            {
                return null;
            }

            var uid = znet.GetWorldUID();
            return uid == 0L ? (long?)null : uid;
        }

        private void LogWrongWorldOnce(long? currentUid)
        {
            if (_loggedWrongWorld)
            {
                return;
            }

            _loggedWrongWorld = true;
            ModLog.Error(
                $"Held a layout for world {_worldUid} while world {(currentUid.HasValue ? currentUid.Value.ToString() : "<none>")} "
                + "is loaded, so it will not be used - players spawn vanilla instead of at another world's coordinates. "
                + "The layout should have been cleared when the previous world unloaded; see issue #38.");
        }
    }
}
