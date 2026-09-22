using System.Collections;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class ClientSyncHelper
    {
        private const float FirstRetrySeconds = 2f;
        private const float MaxRetrySeconds = 30f;

        public static bool CanReachServer()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
            {
                return false;
            }

            return ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        public static void ResetClientState()
        {
            RosterSync.ResetClientState();
        }

        /// <summary>
        /// Asks the server for the roster and layout until both have arrived.
        ///
        /// Backs off, and says what it is doing once rather than every pass. A server
        /// can legitimately have no layout for minutes (a new world is still generating
        /// locations) or for ever (it has the mod disabled), and the original fixed 2 s
        /// loop logged a dozen lines per pass on the client and two on the server for as
        /// long as that lasted, burying everything else in both logs.
        ///
        /// One direct request per pass fetches both payloads; the routed requests ride
        /// along as the fallback channel. They used to each send a direct request of
        /// their own, so every pass asked three times and got the roster back twice.
        /// </summary>
        public static IEnumerator RunSyncRetry(MonoBehaviour host)
        {
            var wasConnected = false;
            var retryDelay = FirstRetrySeconds;
            var waitingSince = -1f;

            while (host != null)
            {
                if (!CanReachServer())
                {
                    if (wasConnected)
                    {
                        ResetClientState();
                        ModLog.Info("Server disconnected; waiting to resync Separate Spawns data.");
                    }

                    wasConnected = false;
                    retryDelay = FirstRetrySeconds;
                    waitingSince = -1f;
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (!wasConnected)
                {
                    ModLog.Info("Connected to server; syncing Separate Spawns roster and layout.");
                    wasConnected = true;
                }

                RosterSync.Register();
                LayoutSync.Register();
                PortalActivationSync.Register();

                var needRoster = !RosterSync.ClientHasRoster;
                var needLayout = Plugin.LayoutCache.Current == null;
                if (!needRoster && !needLayout)
                {
                    retryDelay = FirstRetrySeconds;
                    waitingSince = -1f;
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                var firstAsk = waitingSince < 0f;
                if (firstAsk)
                {
                    waitingSince = Time.realtimeSinceStartup;
                }
                else if (retryDelay >= MaxRetrySeconds)
                {
                    var missing = needRoster && needLayout ? "roster and layout" : needRoster ? "roster" : "layout";
                    ModLog.Info(
                        $"Still waiting for the server's Separate Spawns {missing} ({Time.realtimeSinceStartup - waitingSince:F0}s).");
                }

                DirectPeerSync.RequestFromServer(quiet: !firstAsk);
                if (needRoster)
                {
                    RosterSync.RequestFromServer(direct: false, quiet: !firstAsk);
                }

                if (needLayout)
                {
                    LayoutSync.RequestLayoutFromServer(direct: false, quiet: !firstAsk);
                }

                yield return new WaitForSeconds(retryDelay);
                retryDelay = Mathf.Min(retryDelay * 2f, MaxRetrySeconds);
            }
        }
    }
}
