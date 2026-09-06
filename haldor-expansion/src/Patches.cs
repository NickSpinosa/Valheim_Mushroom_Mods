using HarmonyLib;

namespace HaldorExpansion
{
    /// <summary>
    /// ObjectDB is built twice (main-menu stub, then the real world DB via CopyOtherDB
    /// which replaces the list). Both entry points re-add; never latch a "done" flag.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB))]
    internal static class ObjectDbPatches
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.Awake))]
        private static void AwakePostfix(ObjectDB __instance)
        {
            try
            {
                PrefabCache.Reset();
                SuperMistTorch.EnsureRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("ObjectDB.Awake SuperMistTorch registration failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        private static void CopyOtherDbPostfix(ObjectDB __instance)
        {
            try
            {
                PrefabCache.Reset();
                SuperMistTorch.EnsureRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("ObjectDB.CopyOtherDB SuperMistTorch registration failed: " + e);
            }
        }
    }

    /// <summary>
    /// Source piece lives in ZNetScene, so this is where the clone can actually be
    /// built. Priority.First so a throwing sibling postfix cannot skip us; Game.Start
    /// is the backstop if this chain still aborts.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(ZNetScene __instance)
        {
            try { SuperMistTorch.EnsureNetworkRegistered(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("ZNetScene.Awake SuperMistTorch registration failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix()
        {
            try
            {
                SuperMistTorch.EnsureNetworkRegistered(ZNetScene.instance);
                SuperMistTorch.EnsureRegistered(ObjectDB.instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Game.Start SuperMistTorch registration failed: " + e);
            }
        }
    }

    /// <summary>
    /// Hammer only lists known pieces. Teach every player the Super Mist Torch once
    /// the prefab exists so a Haldor purchase is placeable immediately.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerOnSpawnedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            try { SuperMistTorch.EnsureKnown(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to unlock SuperMistTorch for player: " + e);
            }
        }
    }
}
