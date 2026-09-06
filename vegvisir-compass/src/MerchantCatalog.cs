using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>A trader that lorestones can point toward.</summary>
    internal sealed class MerchantDef
    {
        /// <summary>Location the merchant's camp occupies, e.g. "Vendor_BlackForest".</summary>
        internal string LocationName;

        /// <summary>Shown on the compass: "Vegvisir Compass - Haldor".</summary>
        internal string DisplayName;

        /// <summary>Location names of the lorestones that point at this merchant.</summary>
        internal string[] RuneStoneLocationNames;

        /// <summary>
        /// Prefab name of the trader themselves, used to find where they have actually
        /// settled once spawned. Empty disables that lookup for this merchant.
        /// </summary>
        internal string TraderPrefabName;

        /// <summary>
        /// Stones nearer the world centre than this stay lore-only. Traders close to
        /// spawn are easy enough to stumble across; guidance is for the distant ones.
        /// </summary>
        internal float GuidanceMinDistanceFromCentre;
    }

    /// <summary>
    /// The merchants, and which lorestones lead to them.
    ///
    /// Vanilla lorestones carry no location of their own, so a stone is identified by
    /// the location it stands in rather than by anything on the stone itself.
    /// </summary>
    internal static class MerchantCatalog
    {
        internal static readonly MerchantDef[] All =
        {
            new MerchantDef
            {
                LocationName = "Vendor_BlackForest",
                DisplayName = "Haldor",
                RuneStoneLocationNames = new[] { "Runestone_BlackForest" },
                TraderPrefabName = "Haldor",
                GuidanceMinDistanceFromCentre = 750f,
            },
            new MerchantDef
            {
                LocationName = "Hildir_camp",
                DisplayName = "Hildir",
                RuneStoneLocationNames = new[] { "Runestone_Meadows", "Runestone_Plains" },
                TraderPrefabName = "Hildir",
                GuidanceMinDistanceFromCentre = 1500f,
            },
            new MerchantDef
            {
                LocationName = "BogWitch_Camp",
                DisplayName = "Bog Witch",
                RuneStoneLocationNames = new[] { "Runestone_Swamps" },
                TraderPrefabName = "BogWitch",
                GuidanceMinDistanceFromCentre = 1500f,
            },
        };

        /// <summary>
        /// Identifies the merchant a lorestone belongs to from the location it stands in.
        ///
        /// Deliberately uses Location.GetLocation rather than ZoneSystem lookups:
        /// m_locationInstances is empty on clients, so any location-list approach would
        /// silently never match on a dedicated server. The loaded Location around the
        /// player is available on both sides.
        /// </summary>
        internal static MerchantDef ResolveForStone(Vector3 stonePosition)
        {
            Location location = Location.GetLocation(stonePosition, true);
            if (location == null)
            {
                Plugin.Debug("Lorestone: no Location found around the stone.");
                return null;
            }

            string name = location.gameObject.name.Replace("(Clone)", "").Trim();

            foreach (MerchantDef def in All)
            {
                foreach (string stoneLocation in def.RuneStoneLocationNames)
                {
                    if (string.Equals(name, stoneLocation, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return def;
                    }
                }
            }

            // Logged rather than dropped quietly: the location's real name is the one
            // thing needed to fix a catalog entry that does not match.
            Plugin.Debug($"Lorestone: location '{name}' is not a merchant stone.");
            return null;
        }

        /// <summary>
        /// Identifies which merchant a trader is, by name. Traders carry a localization
        /// token such as "$npc_haldor", so both that and the plain name are accepted.
        /// </summary>
        internal static MerchantDef ResolveForTrader(Trader trader)
        {
            if (trader == null) return null;

            foreach (MerchantDef def in All)
            {
                if (NameMatches(trader.m_name, def) || NameMatches(trader.gameObject.name, def))
                {
                    return def;
                }
            }
            return null;
        }

        private static bool NameMatches(string name, MerchantDef def)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return name.IndexOf(def.TraderPrefabName, System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf(def.DisplayName, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Whether a stone at this position is far enough out to carry guidance.
        /// Measured on X/Z from the world centre, as elsewhere in this mod.
        /// </summary>
        internal static bool IsPastGuidanceGate(MerchantDef def, Vector3 stonePosition)
        {
            float distanceFromCentre = new Vector2(stonePosition.x, stonePosition.z).magnitude;
            return distanceFromCentre >= def.GuidanceMinDistanceFromCentre;
        }
    }
}
