using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// Defers where a merchant settles until the player actually trades with them.
    ///
    /// Vanilla marks merchant camps unique: the first candidate any player wanders near
    /// is placed, and every other candidate is deleted on the spot. Where your trader
    /// lives is therefore decided by an idle walk through the Black Forest, long before
    /// you had any reason to care.
    ///
    /// The deferral cannot work by holding placement back. ZoneSystem.PlaceLocations runs
    /// exactly once per zone - SpawnZone gates it on !IsZoneGenerated and then calls
    /// SetZoneGenerated unconditionally - so a zone whose placement we skipped is marked
    /// generated with nothing in it, permanently, and that candidate site is gone for
    /// good. Nor is LocationInstance.m_placed a spawn switch: it is bookkeeping written
    /// during generation, and clearing it removes nothing, because the camp objects
    /// already exist as ZDOs that outlive the zone being unloaded.
    ///
    /// So placement is left entirely alone and the trader is managed instead. Every
    /// candidate camp places normally; the merchant standing in it is spawned when a
    /// player comes within vanilla's own range and removed again when they leave, which
    /// keeps the choice open without any camp becoming permanent. Opening the trade UI
    /// settles the site: the rest are destroyed for good and vanilla's cleanup is finally
    /// allowed to clear the spare candidates.
    ///
    /// Ported from Find Haldor by Gonfreecss, with permission, and reworked around the
    /// one-shot nature of zone generation.
    /// </summary>
    internal static class MerchantPlacement
    {
        /// <summary>Marks a world as one this system has managed from the start.</summary>
        private const string ActiveWorldKey = "VC_MerchantPlacement";

        /// <summary>Seconds between presence sweeps. Human-scale, so cheap.</summary>
        private const float PresenceInterval = 2f;

        /// <summary>
        /// How close two positions must be to count as the same trader. Generous enough
        /// to survive a merchant wandering a little way around their camp.
        /// </summary>
        private const float SameTraderRadius = 12f;

        internal enum SupportState
        {
            Unknown,
            Active,
            DisabledUnsupported,
        }

        internal static SupportState State { get; private set; } = SupportState.Unknown;

        internal static bool IsActive => State == SupportState.Active;

        private static bool _warnedUnsupported;
        private static float _nextPresenceCheck;
        private static readonly HashSet<string> _warnedMissingPrefab = new HashSet<string>();

        /// <summary>
        /// Where each merchant's trader was last seen standing, by camp zone, so one that
        /// has been despawned goes back exactly where it was rather than to the middle of
        /// the camp. Deliberately in memory only - it is a refinement, not state worth
        /// persisting, and a cold start simply falls back to the camp position.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<Vector2s, Pose>> _lastSeen
            = new Dictionary<string, Dictionary<Vector2s, Pose>>();

        /// <summary>Global key recording that this merchant's site is settled for good.</summary>
        private static string LockKey(MerchantDef def) => "VC_MerchantLocked_" + def.LocationName;

        internal static bool IsLocked(MerchantDef def)
        {
            return ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(LockKey(def));
        }

        internal static void Reset()
        {
            State = SupportState.Unknown;
            _warnedUnsupported = false;
            _nextPresenceCheck = 0f;
            _lastSeen.Clear();
            _warnedMissingPrefab.Clear();
        }

        // --- World support ---------------------------------------------------

        /// <summary>
        /// Decides whether this world can be managed. A world where vanilla already
        /// placed a merchant has had its candidates deleted, so there is nothing left to
        /// defer and the system stays out of the way rather than half-working.
        /// </summary>
        internal static void EvaluateWorldSupport(ZoneSystem zones)
        {
            if (State != SupportState.Unknown) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!zones.LocationsGenerated) return;

            if (zones.GetGlobalKey(ActiveWorldKey))
            {
                State = SupportState.Active;
                Plugin.Log.LogInfo("Merchant placement active on this world.");
                return;
            }

            if (AnyMerchantAlreadyPlaced(zones))
            {
                State = SupportState.DisabledUnsupported;
                if (!_warnedUnsupported)
                {
                    _warnedUnsupported = true;
                    Plugin.Log.LogWarning(
                        "Merchant placement disabled: a camp was already placed under vanilla rules on this " +
                        "world, so its other candidates are gone. Compasses still work; merchants simply stay " +
                        "wherever vanilla put them.");
                }
                return;
            }

            zones.SetGlobalKey(ActiveWorldKey);
            State = SupportState.Active;
            Plugin.Log.LogInfo("Merchant placement enabled on this world.");
        }

        /// <summary>Clients take the state from the world's global keys.</summary>
        internal static void SyncClientState(ZoneSystem zones)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;

            if (zones.GetGlobalKey(ActiveWorldKey))
            {
                State = SupportState.Active;
            }
            else if (State == SupportState.Unknown && zones.LocationsGenerated)
            {
                State = SupportState.DisabledUnsupported;
            }
        }

        private static bool AnyMerchantAlreadyPlaced(ZoneSystem zones)
        {
            foreach (MerchantDef def in MerchantCatalog.All)
            {
                List<ZoneSystem.LocationInstance> found = new List<ZoneSystem.LocationInstance>();
                if (!zones.FindLocations(def.LocationName, ref found) || found == null) continue;

                foreach (ZoneSystem.LocationInstance instance in found)
                {
                    if (instance.m_placed) return true;
                }
            }
            return false;
        }

        // --- Lock In ---------------------------------------------------------

        /// <summary>
        /// Settles a merchant where they stand, and clears every rival.
        ///
        /// Server-side only: the losing traders have to be destroyed, and a client owns
        /// none of those ZDOs. The client asks for this over an RPC.
        /// </summary>
        internal static void LockIn(MerchantDef def, Vector3 traderPosition)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!IsActive || ZoneSystem.instance == null) return;
            if (IsLocked(def)) return;

            ZoneSystem.instance.SetGlobalKey(LockKey(def));

            int removed = 0;
            foreach (ZDO zdo in CollectTraders(def))
            {
                if (CompassItem.HorizontalDistance(zdo.GetPosition(), traderPosition) <= SameTraderRadius)
                {
                    continue;
                }

                DestroyTrader(zdo);
                removed++;
            }

            // The site is settled, so vanilla's own uniqueness cleanup may finally run
            // and clear the candidate sites that were being kept alive for it.
            ZoneSystem.ZoneLocation location = ZoneSystem.instance.GetLocation(def.LocationName);
            if (location != null)
            {
                ZoneSystem.instance.RemoveUnplacedLocations(location);
            }

            _lastSeen.Remove(def.LocationName);

            Plugin.Log.LogInfo(
                $"{def.DisplayName} settled at {traderPosition}" +
                (removed > 0 ? $"; removed {removed} provisional merchant(s) elsewhere." : "."));
        }

        // --- Presence ---------------------------------------------------------

        internal static void ServerTick(ZoneSystem zones)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            EvaluateWorldSupport(zones);
            if (!IsActive) return;

            if (Time.time < _nextPresenceCheck) return;
            _nextPresenceCheck = Time.time + PresenceInterval;

            foreach (MerchantDef def in MerchantCatalog.All)
            {
                // A settled merchant is vanilla's business again.
                if (IsLocked(def)) continue;

                try { UpdateProvisionalTraders(zones, def); }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"Presence sweep for {def.DisplayName} failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Brings the world in line with who should be standing where: a trader at every
        /// camp a player is near, and none at the camps they are not.
        /// </summary>
        private static void UpdateProvisionalTraders(ZoneSystem zones, MerchantDef def)
        {
            List<Vector3> standing = new List<Vector3>();

            foreach (ZDO zdo in CollectTraders(def))
            {
                Vector3 position = zdo.GetPosition();

                if (IsWithinVanillaRange(position))
                {
                    Remember(def, ZoneSystem.GetZone(position), position, zdo.GetRotation());
                    standing.Add(position);
                    continue;
                }

                DestroyTrader(zdo);
                Plugin.Debug($"Despawned the provisional {def.DisplayName} at {position}; nobody is near.");
            }

            foreach (KeyValuePair<Vector2s, ZoneSystem.LocationInstance> pair in zones.m_locationInstances)
            {
                ZoneSystem.LocationInstance camp = pair.Value;

                // Only a camp vanilla has actually built can hold a merchant. An unplaced
                // candidate is still just a reserved spot on the map.
                if (!camp.m_placed) continue;
                if (PrefabName(camp) != def.LocationName) continue;
                if (!IsWithinVanillaRange(camp.m_position)) continue;
                if (AlreadyStanding(standing, camp.m_position)) continue;

                if (SpawnTrader(def, pair.Key, camp.m_position))
                {
                    standing.Add(camp.m_position);
                    Plugin.Debug($"Spawned a provisional {def.DisplayName} at the camp in zone {pair.Key}.");
                }
            }
        }

        private static bool AlreadyStanding(List<Vector3> standing, Vector3 campPosition)
        {
            foreach (Vector3 position in standing)
            {
                if (CompassItem.HorizontalDistance(position, campPosition) <= SameTraderRadius) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether any player is close enough that vanilla would have this camp loaded.
        ///
        /// The reach is asked of the game rather than reconstructed. ZNetScene.InActiveArea
        /// is the same test vanilla uses to decide what it instantiates, and since 1.0 the
        /// answer depends on a server-synced, player-configurable SimulationDistance whose
        /// shape is not a simple square of zones - at near distance 2 it clips the corners
        /// to a 1.75-zone radius. Any arithmetic of our own would be a second, drifting
        /// copy of that.
        ///
        /// Proximity comes from the peers and not from Player.GetAllPlayers: a dedicated
        /// server only ever loads zones around its own reference position, so no Player
        /// object exists for a remote client and that list is empty there.
        /// </summary>
        private static bool IsWithinVanillaRange(Vector3 position)
        {
            if (ZNet.instance == null || ZoneSystem.instance == null) return false;

            if (ZNetScene.InActiveArea(position, ZNet.instance.GetReferencePosition())) return true;

            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null) continue;
                if (ZNetScene.InActiveArea(position, peer.GetRefPos())) return true;
            }
            return false;
        }

        // --- Traders ----------------------------------------------------------

        /// <summary>
        /// Every trader of this kind in the world, loaded or not. The server holds all
        /// ZDOs, which is the same source vanilla's own "find" command searches.
        /// </summary>
        internal static List<ZDO> CollectTraders(MerchantDef def)
        {
            List<ZDO> found = new List<ZDO>();
            if (def == null || string.IsNullOrEmpty(def.TraderPrefabName)) return found;
            if (ZDOMan.instance == null) return found;

            try
            {
                int index = 0;
                // Iterative by design: it walks the ZDO table in chunks and returns true
                // only once it has been through the whole thing.
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(def.TraderPrefabName, found, ref index))
                {
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Trader ZDO query for '{def.TraderPrefabName}' failed: {e.Message}");
            }
            return found;
        }

        private static void DestroyTrader(ZDO zdo)
        {
            // DestroyZDO only acts on ZDOs this instance owns, and a client standing
            // nearby may well hold this one, so take ownership before asking.
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
        }

        private static bool SpawnTrader(MerchantDef def, Vector2s zoneId, Vector3 campPosition)
        {
            if (ZNetScene.instance == null) return false;

            GameObject prefab = ZNetScene.instance.GetPrefab(def.TraderPrefabName);
            if (prefab == null)
            {
                // Once per merchant: a wrong prefab name would otherwise log every sweep.
                if (_warnedMissingPrefab.Add(def.TraderPrefabName))
                {
                    Plugin.Log.LogWarning(
                        $"No prefab named '{def.TraderPrefabName}' exists, so {def.DisplayName} cannot be " +
                        "placed provisionally. Merchant placement will do nothing for this trader.");
                }
                return false;
            }

            Pose pose = Recall(def, zoneId, campPosition);
            Object.Instantiate(prefab, pose.position, pose.rotation);
            return true;
        }

        private static void Remember(MerchantDef def, Vector2s zoneId, Vector3 position, Quaternion rotation)
        {
            if (!_lastSeen.TryGetValue(def.LocationName, out Dictionary<Vector2s, Pose> byZone))
            {
                byZone = new Dictionary<Vector2s, Pose>();
                _lastSeen[def.LocationName] = byZone;
            }
            byZone[zoneId] = new Pose(position, rotation);
        }

        private static Pose Recall(MerchantDef def, Vector2s zoneId, Vector3 campPosition)
        {
            if (_lastSeen.TryGetValue(def.LocationName, out Dictionary<Vector2s, Pose> byZone)
                && byZone.TryGetValue(zoneId, out Pose pose))
            {
                return pose;
            }
            return new Pose(campPosition, Quaternion.identity);
        }

        // --- Lookup -----------------------------------------------------------

        /// <summary>Positions of every camp vanilla has actually built for this merchant.</summary>
        internal static void CollectPlacedCamps(ZoneSystem zones, MerchantDef def, List<Vector3> into)
        {
            foreach (KeyValuePair<Vector2s, ZoneSystem.LocationInstance> pair in zones.m_locationInstances)
            {
                if (!pair.Value.m_placed) continue;
                if (PrefabName(pair.Value) != def.LocationName) continue;
                into.Add(pair.Value.m_position);
            }
        }

        private static string PrefabName(ZoneSystem.LocationInstance instance)
        {
            if (instance.m_location == null) return "";
            if (!string.IsNullOrEmpty(instance.m_location.m_prefabName)) return instance.m_location.m_prefabName;
            return instance.m_location.m_name ?? "";
        }
    }
}
