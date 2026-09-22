using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// The client/server exchange behind looting a compass.
    ///
    /// This deliberately does not reuse Game.DiscoverClosestLocation. That path ends
    /// in Minimap.DiscoverLocation, which calls AddPin(save: true) unconditionally -
    /// even with showMap false - and would permanently write boss pins into every
    /// player save. On a no-map playthrough that is exactly what we are avoiding.
    ///
    /// Resolving the location has to happen on the server: ZoneSystem.m_locationInstances
    /// is only populated there, which is why vanilla round-trips too. The resolved
    /// position is then baked into the item, so using the compass later needs no
    /// further server contact.
    /// </summary>
    internal static class CompassRpc
    {
        private const string RequestName = "VC_RequestCompass";
        private const string ResponseName = "VC_GrantCompass";
        private const string LockInName = "VC_LockMerchant";

        private const int StatusGranted = 0;
        private const int StatusCooldown = 1;
        private const int StatusNotFound = 2;

        private static bool _registered;

        internal static void Register()
        {
            if (_registered || ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.Register<ZPackage>(RequestName, OnServerRequest);
            ZRoutedRpc.instance.Register<ZPackage>(ResponseName, OnClientResponse);
            ZRoutedRpc.instance.Register<ZPackage>(LockInName, OnServerLockIn);
            _registered = true;

            Plugin.Debug("Registered compass RPCs.");
        }

        internal static void Reset()
        {
            _registered = false;
        }

        // --- Client -> server ------------------------------------------------

        /// <summary>Asks the server for a compass keyed to the given Vegvisir.</summary>
        internal static void RequestCompass(Vegvisir stone, Player player)
        {
            List<string> targets = new List<string>();
            foreach (Vegvisir.VegvisrLocation location in stone.m_locations)
            {
                if (!string.IsNullOrEmpty(location.m_locationName))
                {
                    targets.Add(Encode(location.m_locationName, location.m_pinName));
                }
            }

            if (targets.Count == 0)
            {
                Plugin.Log.LogWarning("Vegvisir has no usable locations; ignoring.");
                return;
            }

            RequestCompass(stone.transform.position, targets, player);
        }

        /// <summary>Packs a target as "locationName|label" for transport.</summary>
        internal static string Encode(string locationName, string label)
        {
            return locationName + "|" + (label ?? locationName);
        }

        /// <summary>
        /// Asks the server for a compass. The origin is whatever the player interacted
        /// with - a Vegvisir, a merchant lorestone, Hildir's map table - and becomes the
        /// point the compass's range is measured from. Where several targets are given
        /// the server picks whichever is nearest the player, so one request yields one
        /// compass; ask repeatedly for several.
        /// </summary>
        internal static void RequestCompass(Vector3 originPosition, List<string> encodedTargets, Player player)
        {
            if (ZRoutedRpc.instance == null || encodedTargets == null || encodedTargets.Count == 0) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(originPosition);
            pkg.Write(player.transform.position);
            pkg.Write(string.Join(",", encodedTargets.ToArray()));

            // The no-target overload routes to the server, which is how vanilla sends its
            // own RPC_DiscoverClosestLocation. Avoids touching a private member.
            ZRoutedRpc.instance.InvokeRoutedRPC(RequestName, pkg);
            Plugin.Debug($"Requested compass for {encodedTargets.Count} location(s).");
        }

        // --- Lock In ---------------------------------------------------------

        /// <summary>
        /// Asks the server to settle a guided site where its actor stands. Sent when a
        /// trader's shop or the Forge of Potential craft UI opens; the server does the
        /// work because it owns the rival actors' ZDOs.
        /// </summary>
        internal static void RequestLockIn(MerchantDef def, Vector3 traderPosition)
        {
            if (ZRoutedRpc.instance == null || def == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(def.LocationName);
            pkg.Write(traderPosition);

            ZRoutedRpc.instance.InvokeRoutedRPC(LockInName, pkg);
            Plugin.Debug($"Asked the server to settle {def.DisplayName} at {traderPosition}.");
        }

        /// <summary>Runs on the server. Settles the merchant and clears every rival.</summary>
        private static void OnServerLockIn(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            string locationName = pkg.ReadString();
            Vector3 traderPosition = pkg.ReadVector3();

            MerchantDef def = MerchantLocator.ForLocation(locationName);
            if (def == null)
            {
                Plugin.Log.LogWarning($"Lock-in requested for an unknown merchant location '{locationName}'.");
                return;
            }

            MerchantPlacement.LockIn(def, traderPosition);
        }

        /// <summary>Runs on the server. Enforces the cooldown and resolves the target.</summary>
        private static void OnServerRequest(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            Vector3 stonePosition = pkg.ReadVector3();
            Vector3 playerPosition = pkg.ReadVector3();
            string encoded = pkg.ReadString();

            float cooldown = Plugin.LootCooldownSeconds.Value;
            if (!LootCooldownRegistry.TryClaim(stonePosition, cooldown, out float remaining))
            {
                Plugin.Debug($"Vegvisir on cooldown for {remaining:0}s; refusing peer {sender}.");
                Respond(sender, StatusCooldown, Vector3.zero, "", remaining, 0, stonePosition, 0f, "");
                return;
            }

            // Use count and range are decided here rather than on the client, so a
            // player editing their own config cannot grant themselves extra uses or
            // carry the compass further from the stone than intended.
            int uses = Plugin.UsesPerCompass.Value;

            // A flat radius around the stone. The compass is meant to be read at the
            // Vegvisir, not carried across the map, so the range is independent of how
            // far away the target happens to be.
            float range = Plugin.RangeMeters.Value;

            // One compass per location the stone names, rather than only the nearest.
            // Hildir's map table is a Vegvisir carrying all three of her quest dungeons,
            // and collapsing that to the closest handed out one compass instead of three.
            int granted = 0;
            foreach (string entry in encoded.Split(','))
            {
                string[] parts = entry.Split('|');
                string locationName = parts[0];
                string label = parts.Length > 1 ? parts[1] : locationName;

                // A merchant who has already settled beats their candidate sites: vanilla
                // scatters several possible camps and only commits to one when a player
                // gets close, so the nearest candidate may be somewhere they never appear.
                Vector3 targetPosition;
                MerchantDef merchant = MerchantLocator.ForLocation(locationName);

                if (merchant != null && MerchantLocator.TryGetSettledPosition(merchant, stonePosition, out Vector3 settled))
                {
                    targetPosition = settled;
                }
                else if (ZoneSystem.instance.FindClosestLocation(locationName, stonePosition, out ZoneSystem.LocationInstance closest))
                {
                    targetPosition = closest.m_position;
                }
                else
                {
                    Plugin.Debug($"No location instance found for {locationName}.");
                    continue;
                }

                // Vanilla labels several distinct destinations with a single shared pin
                // token - "$hud_pin_hildir3" for Hildir's dungeons, "$placeofmystery"
                // for all three Ashlands locations - which would leave compasses to
                // different places wearing the same name. The catalogs replace those.
                string questLabel = HildirQuestCatalog.LabelFor(locationName);
                if (string.IsNullOrEmpty(questLabel)) questLabel = MysteryLocationCatalog.LabelFor(locationName);
                if (!string.IsNullOrEmpty(questLabel)) label = questLabel;

                Plugin.Debug($"Granting compass to peer {sender}, target {label} ({locationName}) " +
                             $"at {targetPosition}, {uses} use(s), range {range:0}m.");

                Respond(sender, StatusGranted, targetPosition, label, 0f, uses, stonePosition, range, locationName);
                granted++;
            }

            if (granted == 0)
            {
                Plugin.Log.LogWarning("Could not resolve any location for this stone.");
                Respond(sender, StatusNotFound, Vector3.zero, "", 0f, 0, stonePosition, 0f, "");
            }
        }

        private static void Respond(long peer, int status, Vector3 target, string bossName, float retryAfter,
                                    int uses, Vector3 origin, float range, string locationName)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(status);
            pkg.Write(target);
            pkg.Write(bossName ?? "");
            pkg.Write(retryAfter);
            pkg.Write(uses);
            pkg.Write(origin);
            pkg.Write(range);
            pkg.Write(locationName ?? "");

            ZRoutedRpc.instance.InvokeRoutedRPC(peer, ResponseName, pkg);
        }

        // --- Server -> client ------------------------------------------------

        /// <summary>Runs on the client that asked. Creates the item or explains why not.</summary>
        private static void OnClientResponse(long sender, ZPackage pkg)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            int status = pkg.ReadInt();
            Vector3 target = pkg.ReadVector3();
            string bossName = pkg.ReadString();
            float retryAfter = pkg.ReadSingle();
            int uses = pkg.ReadInt();
            Vector3 origin = pkg.ReadVector3();
            float range = pkg.ReadSingle();
            string locationName = pkg.ReadString();

            switch (status)
            {
                case StatusGranted:
                    if (CompassItem.Grant(player, target, bossName, uses, origin, range, locationName))
                    {
                        // Name the target here too - it is the moment the player most
                        // wants to know which boss this one points at.
                        string localizedBoss = Localization.instance != null
                            ? Localization.instance.Localize(bossName)
                            : bossName;
                        player.Message(MessageHud.MessageType.Center,
                            string.IsNullOrEmpty(localizedBoss)
                                ? CompassItem.DisplayName + " acquired"
                                : CompassItem.DisplayName + " - " + localizedBoss + " acquired");
                    }
                    else
                    {
                        player.Message(MessageHud.MessageType.Center, "$inventory_full");
                    }
                    break;

                case StatusCooldown:
                    player.Message(MessageHud.MessageType.Center,
                        $"The runes are dormant ({FormatDuration(retryAfter)})");
                    break;

                default:
                    player.Message(MessageHud.MessageType.Center, "The runes reveal nothing");
                    break;
            }
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = total / 60;
            int remainder = total % 60;
            return minutes > 0 ? $"{minutes}m {remainder}s" : $"{remainder}s";
        }
    }
}
