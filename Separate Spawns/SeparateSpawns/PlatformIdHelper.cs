using System;
using System.Reflection;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PlatformIdHelper
    {
        private static bool _loggedSteamUnavailable;
        private static bool _loggedPlatformManagerFailure;
        private static bool _loggedPlayerListFailure;
        private static bool _loggedHostNameFailure;
        private static bool _loggedIdSource;

        /// <summary>
        /// Dedicated/headless servers have no local Steam user; roster matching uses peer host names instead.
        ///
        /// Decided by the graphics device, which is what the game itself uses to tell a
        /// headless process from one with a local player (see ZNet.UpdatePlayerList).
        /// It must not depend on Player.m_localPlayer: a client hosting its own world is a
        /// server with no local player until it spawns, and the spawn itself waits on this
        /// id, so keying on the player deadlocks the loading screen. ZNet.IsDedicated() is
        /// a stub that returns false in the client build, so it cannot decide either.
        /// </summary>
        public static bool IsHeadlessServerContext()
        {
            return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        public static string GetLocalPlatformUserId()
        {
            if (IsHeadlessServerContext())
            {
                return string.Empty;
            }

            var fromPlatform = TryGetPlatformManagerUserId();
            if (!string.IsNullOrEmpty(fromPlatform))
            {
                return Resolved("PlatformManager", fromPlatform);
            }

            var fromPlayerList = TryGetLocalUserIdFromPlayerList();
            if (!string.IsNullOrEmpty(fromPlayerList))
            {
                return Resolved("player list", fromPlayerList);
            }

            var fromSteam = TryGetSteamUserId();
            if (!string.IsNullOrEmpty(fromSteam))
            {
                return Resolved("Steamworks", fromSteam);
            }

            var fromHostName = TryGetHostNameUserId();
            if (!string.IsNullOrEmpty(fromHostName))
            {
                return Resolved("host name", fromHostName);
            }

            return string.Empty;
        }

        /// <summary>
        /// Logs once which lookup produced the id. When a game update silently breaks
        /// the primary path, the fallback that carried it is the thing worth knowing.
        /// </summary>
        private static string Resolved(string source, string id)
        {
            if (!_loggedIdSource)
            {
                _loggedIdSource = true;
                ModLog.Info($"Local platform user id {id} resolved via {source}.");
            }

            return id;
        }

        /// <summary>
        /// The game's platform layer moved into the Splatform namespace in 1.0, so the
        /// bare name no longer resolves; it is kept as a second attempt for older builds.
        /// </summary>
        private static readonly string[] PlatformManagerTypeNames =
        {
            "Splatform.PlatformManager",
            "PlatformManager",
        };

        private static string TryGetPlatformManagerUserId()
        {
            if (IsHeadlessServerContext())
            {
                return string.Empty;
            }

            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type platformManager = null;
                    try
                    {
                        foreach (var typeName in PlatformManagerTypeNames)
                        {
                            platformManager = assembly.GetType(typeName);
                            if (platformManager != null)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Dynamic assemblies can throw; skip them.
                    }

                    if (platformManager == null)
                    {
                        continue;
                    }

                    var distributionPlatform = platformManager
                        .GetProperty("DistributionPlatform", BindingFlags.Public | BindingFlags.Static)
                        ?.GetValue(null, null);
                    var localUser = distributionPlatform?.GetType().GetProperty("LocalUser")?.GetValue(distributionPlatform, null);
                    var platformUserId = localUser?.GetType().GetProperty("PlatformUserID")?.GetValue(localUser, null);
                    var normalized = NormalizeFromObject(platformUserId);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        return normalized;
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _loggedPlatformManagerFailure, $"Failed to read local platform user id: {ex.Message}");
            }

            return string.Empty;
        }

        private static string TryGetLocalUserIdFromPlayerList()
        {
            try
            {
                if (ZNet.instance == null)
                {
                    return string.Empty;
                }

                var players = ZNet.instance.GetPlayerList();
                var localCharacterId = ZNet.instance.LocalPlayerCharacterID;
                var localName = Game.instance?.GetPlayerProfile()?.GetName();

                foreach (var player in players)
                {
                    var normalized = NormalizeFromPlayerInfo(player);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    if (player.m_characterID == localCharacterId ||
                        (!string.IsNullOrEmpty(localName) &&
                         string.Equals(player.m_name, localName, StringComparison.Ordinal)))
                    {
                        return normalized;
                    }
                }

                if (players.Count == 1)
                {
                    return NormalizeFromPlayerInfo(players[0]);
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _loggedPlayerListFailure, $"Failed player-list platform id fallback: {ex.Message}");
            }

            return string.Empty;
        }

        private static string NormalizeFromPlayerInfo(ZNet.PlayerInfo player)
        {
            try
            {
                var userInfo = player.m_userInfo;
                var userInfoType = userInfo.GetType();
                var platformUserId = userInfoType.GetField("m_id")?.GetValue(userInfo) ??
                                     userInfoType.GetProperty("m_id")?.GetValue(userInfo, null);
                return NormalizeFromObject(platformUserId);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryGetSteamUserId()
        {
            if (IsHeadlessServerContext())
            {
                return string.Empty;
            }

            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var steamUser = assembly.GetType("Steamworks.SteamUser");
                    if (steamUser == null)
                    {
                        continue;
                    }

                    var getSteamId = steamUser.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static);
                    var steamId = getSteamId?.Invoke(null, null);
                    if (steamId == null)
                    {
                        continue;
                    }

                    var value = steamId.GetType().GetProperty("Value")?.GetValue(steamId, null);
                    var normalized = Normalize(value?.ToString());
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        return normalized;
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _loggedSteamUnavailable, $"Failed Steam platform id fallback: {ex.Message}");
            }

            return string.Empty;
        }

        private static string TryGetHostNameUserId()
        {
            try
            {
                if (ZNet.instance == null || IsHeadlessServerContext())
                {
                    return string.Empty;
                }

                var hostName = ZNet.instance.GetPeer(ZNet.GetUID())?.m_socket?.GetHostName();
                return Normalize(hostName);
            }
            catch (Exception ex)
            {
                LogOnce(ref _loggedHostNameFailure, $"Failed host-name platform id fallback: {ex.Message}");
            }

            return string.Empty;
        }

        private static void LogOnce(ref bool logged, string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            ModLog.Warning(message);
        }

        private static string NormalizeFromObject(object platformUserId)
        {
            if (platformUserId == null)
            {
                return string.Empty;
            }

            if (IsInvalidPlatformIdObject(platformUserId))
            {
                return string.Empty;
            }

            return Normalize(platformUserId.ToString());
        }

        private static bool IsInvalidPlatformIdObject(object platformUserId)
        {
            try
            {
                var isValid = platformUserId.GetType().GetProperty("IsValid")?.GetValue(platformUserId, null);
                if (isValid is bool valid && !valid)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore reflection failures and fall back to string checks.
            }

            return false;
        }

        /// <summary>
        /// Routed RPC senders are peer UIDs, not PlayerProfile character IDs.
        /// </summary>
        public static string GetPlatformUserIdFromPeer(long peerId)
        {
            if (peerId == 0L || ZNet.instance == null)
            {
                return string.Empty;
            }

            var peer = ZNet.instance.GetPeer(peerId);
            if (peer == null)
            {
                return string.Empty;
            }

            return Normalize(peer.m_socket.GetHostName());
        }

        /// <summary>
        /// Routed RPC senders are peer UIDs, not PlayerProfile character IDs.
        /// </summary>
        public static Player GetPlayerFromPeerId(long peerId)
        {
            if (peerId == 0L)
            {
                return null;
            }

            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(peerId);
                if (peer != null && !peer.m_characterID.IsNone() &&
                    ZDOMan.instance != null && ZNetScene.instance != null)
                {
                    var characterZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                    var characterView = characterZdo != null ? ZNetScene.instance.FindInstance(characterZdo) : null;
                    var playerFromCharacter = characterView?.GetComponent<Player>();
                    if (playerFromCharacter != null)
                    {
                        return playerFromCharacter;
                    }
                }
            }

            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null)
                {
                    continue;
                }

                var nview = player.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.GetZDO().GetOwner() == peerId)
                {
                    return player;
                }
            }

            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(peerId);
                if (peer != null)
                {
                    var closest = Player.GetClosestPlayer(peer.GetRefPos(), 32f);
                    if (closest != null)
                    {
                        return closest;
                    }

                    foreach (var player in Player.GetAllPlayers())
                    {
                        if (player != null &&
                            string.Equals(player.GetPlayerName(), peer.m_playerName, StringComparison.Ordinal))
                        {
                            return player;
                        }
                    }
                }
            }

            // Legacy fallback if a build happens to store the peer id as player id.
            return Player.GetPlayer(peerId);
        }

        public static string GetPlatformUserIdForPlayer(Player player)
        {
            if (player == null)
            {
                return string.Empty;
            }

            if (player == Player.m_localPlayer)
            {
                return GetLocalPlatformUserId();
            }

            if (ZNet.instance == null)
            {
                return string.Empty;
            }

            var nview = player.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                var ownerPeerId = nview.GetZDO().GetOwner();
                var ownerPeer = ZNet.instance.GetPeer(ownerPeerId);
                if (ownerPeer != null)
                {
                    return Normalize(ownerPeer.m_socket.GetHostName());
                }
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (string.Equals(player.GetPlayerName(), peer.m_playerName, StringComparison.Ordinal))
                {
                    return Normalize(peer.m_socket.GetHostName());
                }
            }

            foreach (var info in ZNet.instance.GetPlayerList())
            {
                var playerView = player.GetComponent<ZNetView>();
                if (playerView != null && playerView.IsValid() &&
                    playerView.GetZDO().m_uid == info.m_characterID)
                {
                    return NormalizeFromPlayerInfo(info);
                }

                if (string.Equals(info.m_name, player.GetPlayerName(), StringComparison.Ordinal))
                {
                    return NormalizeFromPlayerInfo(info);
                }
            }

            if (ZNet.instance.IsServer() && player.GetPlayerID() == ZNet.GetUID())
            {
                return GetLocalPlatformUserId();
            }

            return string.Empty;
        }

        public static string Normalize(string platformUserId)
        {
            if (string.IsNullOrWhiteSpace(platformUserId))
            {
                return string.Empty;
            }

            var id = platformUserId.Trim();
            if (id.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("0:0", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Accept raw Steam64 or "Steam_7656..."
            if (ulong.TryParse(id, out var rawNumeric) && rawNumeric > 0)
            {
                return "Steam_" + id;
            }

            var underscore = id.IndexOf('_');
            if (underscore > 0)
            {
                var prefix = id.Substring(0, underscore);
                var suffix = id.Substring(underscore + 1);
                if (ulong.TryParse(suffix, out var suffixNumeric) && suffixNumeric > 0)
                {
                    return prefix + "_" + suffix;
                }
            }

            if (IsInvalidNormalizedId(id))
            {
                return string.Empty;
            }

            return id;
        }

        private static bool IsInvalidNormalizedId(string id)
        {
            if (id.Equals("0:0", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var numericSuffix = ExtractNumericSuffix(id);
            return numericSuffix == "0";
        }

        public static bool IdsMatch(string a, string b)
        {
            var left = Normalize(a);
            var right = Normalize(b);
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Compare numeric suffix only (Steam_X vs X).
            return ExtractNumericSuffix(left) is string leftNum &&
                   ExtractNumericSuffix(right) is string rightNum &&
                   leftNum == rightNum;
        }

        private static string ExtractNumericSuffix(string id)
        {
            var underscore = id.LastIndexOf('_');
            var candidate = underscore >= 0 ? id.Substring(underscore + 1) : id;
            return ulong.TryParse(candidate, out _) ? candidate : null;
        }
    }
}
