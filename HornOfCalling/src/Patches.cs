using HarmonyLib;

namespace HornOfCalling
{
    /// <summary>
    /// ObjectDB is populated more than once - once for the stripped-down main-menu
    /// copy, then again when the world database is merged over it - so both entry
    /// points register, and registration is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB))]
    internal static class ObjectDbPatches
    {
        // Priority.First for the same reason as ZNetSceneAwakePatch below: these are
        // shared patch points, and another mod's throwing postfix aborts the rest of
        // the chain. Our own bodies are wrapped so we never do that to anyone else.

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.Awake))]
        internal static void AwakePostfix(ObjectDB __instance)
        {
            try
            {
                HornItem.EnsureRegistered(__instance);
                HornItem.EnsureRecipeRegistered(__instance);
            }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.Awake registration failed: " + e); }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        internal static void CopyOtherDbPostfix(ObjectDB __instance)
        {
            try
            {
                HornItem.EnsureRegistered(__instance);
                HornItem.EnsureRecipeRegistered(__instance);
            }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.CopyOtherDB registration failed: " + e); }
        }
    }

    /// <summary>
    /// The backstop, and the only patch point with a real guarantee behind it.
    ///
    /// The three registration events above each depend on ordering that is not
    /// promised: ObjectDB.CopyOtherDB can replace the recipe list at a moment when the
    /// crafting station prefabs are not loaded yet, and ZNetScene.Awake may already
    /// have run by then, leaving nothing to retry. This runs immediately before the
    /// game enumerates recipes, so if the recipe is missing it is put back in time to
    /// be seen. The presence check in EnsureRecipeRegistered makes the common case a
    /// single list scan.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
    internal static class UpdateKnownRecipesListPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            try
            {
                HornItem.EnsureRecipeRegistered(ObjectDB.instance);
            }
            catch (System.Exception e) { Plugin.Log.LogError("Recipe re-registration failed: " + e); }
        }
    }

    /// <summary>
    /// The point where both the item and its crafting station are certain to exist.
    /// ObjectDB.Awake can run before the station prefabs are loaded, which leaves the
    /// recipe unregistered - this is where that gets picked up.
    ///
    /// Runs at Priority.First because ZNetScene.Awake is a crowded patch point: a
    /// postfix from another mod that throws will abort the rest of the chain, and an
    /// outdated mod doing exactly that would otherwise silently skip our network
    /// registration. GameStartPatch is the second half of that guard.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix(ZNetScene __instance)
        {
            try
            {
                // ZNetScene.Awake can run before ObjectDB has been populated, so make
                // sure the item exists before registering it for networking.
                HornItem.EnsureRegistered(ObjectDB.instance);
                HornItem.EnsureRecipeRegistered(ObjectDB.instance);
                HornItem.EnsureNetworkRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to register " + HornItem.PrefabName + " with ZNetScene: " + e);
            }
        }
    }

    /// <summary>
    /// The safety net for network registration.
    ///
    /// ZNetScene.Awake is the only moment the horn and its blast get their prefab
    /// hashes registered, and it is a shared postfix chain - if a misbehaving mod
    /// aborts it before we run, nothing retries and the failure lasts the whole
    /// session: dropping the horn fails, and other clients cannot resolve the blast.
    /// The recipe is guarded three ways already; this gives the networking the same
    /// treatment. Game.Start is a separate patch chain, so it still gets a chance to
    /// put things right, and every Ensure* call is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            try
            {
                HornItem.EnsureRegistered(ObjectDB.instance);
                HornItem.EnsureRecipeRegistered(ObjectDB.instance);
                HornItem.EnsureNetworkRegistered(ZNetScene.instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Game.Start registration failed: " + e);
            }
        }
    }
}
