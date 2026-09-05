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
    /// would look present and be attached to a dead object. The Horn Cooldown clock
    /// is cleared for a softer reason: it is measured against Time.time, which does
    /// not restart with the world, so a horn sounded just before logging out would
    /// otherwise still be ringing on the other side of the loading screen.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNetShutdownPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            try
            {
                HornCall.Reset();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to reset the Horn Call RPC on shutdown: " + e);
            }

            // Separate try block on purpose: the two resets are independent, and a
            // failure of one must not leave the other still holding last session's
            // state.
            try
            {
                HornBlower.Reset();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to reset the Horn Cooldown on shutdown: " + e);
            }
        }
    }
}
