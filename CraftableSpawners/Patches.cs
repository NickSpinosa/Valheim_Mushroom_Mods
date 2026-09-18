using HarmonyLib;
using UnityEngine;

namespace CraftableSpawners;

static class Patches
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    static class ZNetSceneAwake
    {
        static void Postfix()
        {
            SpawnerSetup.EnsureInitialized();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    static class ObjectDBAwake
    {
        static void Postfix()
        {
            SpawnerSetup.EnsureInitialized();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    static class ObjectDBCopyOtherDB
    {
        static void Postfix()
        {
            SpawnerSetup.EnsureInitialized();
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    static class PlayerOnSpawned
    {
        static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                SpawnerSetup.UnlockKnownSpawners(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "AddKnownItem")]
    static class PlayerAddKnownItem
    {
        static void Postfix(Player __instance, ItemDrop.ItemData item)
        {
            SpawnerSetup.TryUnlockFromItem(__instance, item);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateAvailablePiecesList")]
    static class PlayerUpdateAvailablePiecesList
    {
        static void Prefix()
        {
            SpawnerSetup.EnsurePiecesInHammerTables();
        }
    }

    [HarmonyPatch(typeof(Piece), "SetCreator")]
    static class PieceSetCreator
    {
        static void Postfix(Piece __instance)
        {
            SpawnerSetup.OnCraftableSpawnerPlaced(__instance);
        }
    }

    [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
    static class SpawnAreaSpawnOne
    {
        static void Postfix(SpawnArea __instance, bool __result)
        {
            // Every spawn attempt of every craftable spawner, every ten seconds: a
            // diagnostic, so it is off unless debug messages are on. Checked first so the
            // common case costs nothing - this postfix sees vanilla spawners too.
            if (CraftableSpawnersPlugin.Settings is not { EnableDebugMessages: true })
                return;

            if (!SpawnerSetup.IsCraftableSpawner(__instance))
                return;

            CraftableSpawnersPlugin.Log.LogInfo(
                $"SpawnOne on {__instance.name} => {__result} " +
                $"(prefabs={__instance.m_prefabs?.Count ?? 0}, interval={__instance.m_spawnIntervalSec})");
        }
    }

    [HarmonyPatch(typeof(Player), "RemovePiece")]
    static class PlayerRemovePiece
    {
        static bool Prefix(Player __instance, ref bool __result)
        {
            if (!SpawnerSetup.TryHammerRemoveCraftableSpawner(__instance))
                return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    static class WearNTearDestroy
    {
        static void Prefix(WearNTear __instance)
        {
            if (CraftableSpawnersPlugin.HammerRemoving)
                return;

            if (SpawnerSetup.IsCraftableSpawner(__instance))
                SpawnerSetup.DropRecipeAsWorldPickups(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Destructible), "Destroy")]
    static class DestructibleDestroy
    {
        static void Prefix(Destructible __instance)
        {
            if (CraftableSpawnersPlugin.HammerRemoving)
                return;

            if (SpawnerSetup.IsCraftableSpawner(__instance))
                SpawnerSetup.DropRecipeAsWorldPickups(__instance.gameObject);
        }
    }
}
