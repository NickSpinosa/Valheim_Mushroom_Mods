using System;
using HarmonyLib;

namespace AudibleHorn
{
    /// <summary>
    /// Tears down whatever this mod is holding when a game session ends.
    ///
    /// ZNet.Shutdown is the one hook that fires for every way out of a world -
    /// logging out, being disconnected, the server stopping - and everything built on
    /// top of ZNet is thrown away with it. ZRoutedRpc in particular is rebuilt from
    /// scratch on the next join, so an RPC registration that is not forgotten here
    /// would look present and be attached to a dead object.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNetShutdownPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            // Ticket 05 adds its own Reset() call here for the Horn Cooldown clock.
            try
            {
                HornCall.Reset();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to reset the Horn Call RPC on shutdown: " + e);
            }
        }
    }
}
