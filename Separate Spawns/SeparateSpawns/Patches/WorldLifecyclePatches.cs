using HarmonyLib;

namespace SeparateSpawns.Patches
{
    /// <summary>
    /// Ties this mod's per-world state to the world's own lifetime.
    ///
    /// Everything here exists because a plugin outlives a world. `Plugin.Awake` and
    /// `Plugin.Start` run once per process, but a player can load world A, return to
    /// the menu, and load world B without restarting - and before this, that second
    /// world reused the first world's group spawn coordinates under a different seed
    /// (issue #38). Clients hit it too, hopping between two modded servers.
    /// </summary>
    internal static class WorldLifecyclePatches
    {
        /// <summary>
        /// Every world builds a new ZoneSystem, and `GenerateLocationsCompleted` lives
        /// on that instance, so the subscription has to be made again each time.
        /// `Awake` assigns `s_instance` before anything else, so the postfix already
        /// sees `ZoneSystem.instance`.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), "Awake")]
        internal static class ZoneSystemAwake
        {
            private static void Postfix()
            {
                if (Plugin.Instance == null)
                {
                    return;
                }

                WorldBootstrap.Initialize(Plugin.Instance);
            }
        }

        /// <summary>
        /// World teardown. `OnDestroy` rather than `Shutdown` because
        /// `ShutdownWithoutSave` is a second exit path that never calls the first, and
        /// both end here.
        /// </summary>
        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        internal static class ZNetOnDestroy
        {
            private static void Postfix()
            {
                WorldBootstrap.Shutdown();
                ModLog.Info("World unloaded; Separate Spawns state cleared for the next one.");
            }
        }
    }
}
