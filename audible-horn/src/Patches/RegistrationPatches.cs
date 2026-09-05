using HarmonyLib;

namespace AudibleHorn
{
    /// <summary>
    /// Injects the Signal Horn into the item database as it is built.
    ///
    /// Every body here is wrapped in try/catch so a failure of ours can never abort
    /// the postfix chain for the other mods sharing these patch points, and every one
    /// runs at <see cref="Priority.First"/> so an outdated mod that throws cannot
    /// silently skip our registration either.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB))]
    internal static class SignalHornObjectDbPatches
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch("Awake")]
        internal static void AwakePostfix(ObjectDB __instance)
        {
            try { SignalHornItem.EnsureRegistered(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.Awake registration failed: " + e); }
        }

        /// <summary>
        /// Fires when the world ObjectDB is merged over the main-menu one, which is
        /// where a prefab registered too early would otherwise be dropped: CopyOtherDB
        /// replaces m_items and m_recipes wholesale with the other database's lists.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        internal static void CopyOtherDbPostfix(ObjectDB __instance)
        {
            try { SignalHornItem.EnsureRegistered(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.CopyOtherDB registration failed: " + e); }
        }
    }

    /// <summary>
    /// Makes the horn a networkable object so it can be dropped on the ground and
    /// survive a zone unload.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class SignalHornZNetSceneAwakePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix(ZNetScene __instance)
        {
            try
            {
                // ZNetScene.Awake can run before ObjectDB has been populated, so make
                // sure the prefab exists before registering it for networking. This
                // pass is also the one that usually resolves the workbench station, so
                // the recipe deferred during ObjectDB.Awake lands here.
                SignalHornItem.EnsureRegistered(ObjectDB.instance);
                SignalHornItem.EnsureNetworkRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to register the " + SignalHornItem.DisplayName + " with ZNetScene: " + e);
            }
        }
    }

    /// <summary>
    /// The safety net. If a misbehaving mod aborted the ZNetScene.Awake postfix chain
    /// before we got there, the prefab would not be networkable and dropping a horn on
    /// the ground would fail. This is a separate patch chain, so it still gets a
    /// chance to put things right - on the dedicated server as well, which runs
    /// Game.Start with no local player.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class SignalHornGameStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            try
            {
                SignalHornItem.EnsureRegistered(ObjectDB.instance);
                SignalHornItem.EnsureNetworkRegistered(ZNetScene.instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to initialise the " + SignalHornItem.DisplayName + " on game start: " + e);
            }
        }
    }
}
