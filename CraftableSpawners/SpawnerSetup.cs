using System.Collections.Generic;
using CraftableSpawners.Configuration;
using UnityEngine;

namespace CraftableSpawners;

internal static class SpawnerSetup
{
    private static GameObject cloneRoot;
    private static bool initialized;
    private static readonly HashSet<int> refundedInstanceIds = [];

    internal static void EnsureInitialized()
    {
        if (initialized)
        {
            RefreshFromConfig();
            return;
        }

        ZNetScene scene = ZNetScene.instance;
        if (!scene || ObjectDB.instance == null)
            return;

        cloneRoot = new GameObject("CS_SpawnerPrefabs");
        Object.DontDestroyOnLoad(cloneRoot);
        cloneRoot.SetActive(false);

        FindPlaceEffectSource(scene, out Piece placeEffectSource);

        foreach (SpawnerDef def in SpawnerCatalog.All)
        {
            GameObject source = scene.GetPrefab(def.SourcePrefab);
            if (!source)
            {
                CraftableSpawnersPlugin.Log.LogError($"Could not find source prefab '{def.SourcePrefab}'");
                continue;
            }

            GameObject clone = Object.Instantiate(source, cloneRoot.transform);
            clone.name = def.CloneName;
            def.Prefab = clone;

            ConfigurePiece(def, placeEffectSource);

            if (def.Id == SpawnerId.Surtling)
                ConfigureFirePillar(def, scene);
            else if (def.Id == SpawnerId.TarBlob)
                ConfigureTarBonePile(def, scene);

            // BonePileSpawner clones keep a Sphere on the hitbox layer. Hammer ghosts of
            // that prefab can leak it onto the player and eat melee SphereCasts.
            RetargetHitboxColliders(clone);

            RegisterPrefab(scene, clone);
        }

        RefreshFromConfig();
        initialized = true;
        CraftableSpawnersPlugin.Dbgl("Craftable spawners initialized.", true);
    }

    private static void FindPlaceEffectSource(ZNetScene scene, out Piece piece)
    {
        piece = null;

        foreach (string name in new[] { "wood_wall", "wood_floor", "wood_door", "piece_chest_wood" })
        {
            GameObject go = scene.GetPrefab(name);
            if (!go)
                continue;

            piece = go.GetComponent<Piece>();
            if (piece)
                return;
        }
    }

    private static void ConfigurePiece(SpawnerDef def, Piece placeEffectSource)
    {
        Piece piece = def.Prefab.GetComponent<Piece>() ?? def.Prefab.AddComponent<Piece>();
        piece.m_name = def.DisplayName;
        piece.m_description = def.Description;
        piece.m_category = Piece.PieceCategory.Misc;
        // 1.0's build UI opens on the "by usage" tab, which lists only pieces carrying the
        // selected usage flag. UsageTagFlags has no zero member, so a piece left at the
        // default 0 matches no tag and appears under "All" and nowhere else.
        piece.m_usage = Piece.UsageTagFlags.Misc;
        piece.m_craftingStation = null;
        piece.m_groundOnly = true;
        piece.m_groundPiece = true;
        piece.m_canBeRemoved = true;
        piece.m_primaryTarget = false;
        piece.m_randomTarget = true;
        piece.m_targetNonPlayerBuilt = false;
        piece.m_enabled = true;

        if (placeEffectSource != null)
            piece.m_placeEffect = placeEffectSource.m_placeEffect;

        // Spawners already use ZNetView RPCs (e.g. Destructible.RPC_Damage). Adding WearNTear
        // registers the same RPC names and throws, which breaks hammer remove.
        WearNTear existingWnt = def.Prefab.GetComponent<WearNTear>();
        if (existingWnt)
            Object.DestroyImmediate(existingWnt);

        ApplyRecipeAndIcon(def);
    }

    private static void RetargetHitboxColliders(GameObject prefab)
    {
        int hitboxLayer = LayerMask.NameToLayer("hitbox");
        int pieceLayer = LayerMask.NameToLayer("piece");
        if (hitboxLayer < 0)
            return;

        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (!collider || collider.gameObject.layer != hitboxLayer)
                continue;

            if (pieceLayer >= 0)
                collider.gameObject.layer = pieceLayer;
            else
                collider.enabled = false;
        }
    }

    private static void ConfigureFirePillar(SpawnerDef def, ZNetScene scene)
    {
        ConfigureCustomSpawnArea(
            def,
            scene,
            mobPrefabName: "Surtling",
            vanillaCreatureSpawnerName: "Spawner_imp_respawn",
            label: "Fire pillar");

        AttachBorrowedVisual(
            def.Prefab,
            scene,
            visualName: "CS_FireVisual",
            candidatePrefabs: ["bonfire", "fire_pit", "piece_groundtorch", "piece_groundtorch_wood", "hearth"],
            keepBehaviours: behaviour => behaviour is LightFlicker,
            missingVisualWarning: "No fire visual prefab found; Fire pillar will use bone pile collision only");
    }

    private static void ConfigureTarBonePile(SpawnerDef def, ZNetScene scene)
    {
        ConfigureCustomSpawnArea(
            def,
            scene,
            mobPrefabName: "BlobTar",
            vanillaCreatureSpawnerName: "Spawner_BlobTar_respawn_30",
            label: "Bone pile (tar blob)");

        // Tar pit bones — not the Evil bone pile mesh.
        AttachBorrowedVisual(
            def.Prefab,
            scene,
            visualName: "CS_TarBoneVisual",
            candidatePrefabs: ["lox_ribs"],
            keepBehaviours: _ => false,
            missingVisualWarning: "lox_ribs visual not found; Bone pile will keep Evil bone pile mesh");
    }

    private static void ConfigureCustomSpawnArea(
        SpawnerDef def,
        ZNetScene scene,
        string mobPrefabName,
        string vanillaCreatureSpawnerName,
        string label)
    {
        GameObject mob = scene.GetPrefab(mobPrefabName);
        if (!mob)
        {
            CraftableSpawnersPlugin.Log.LogError($"Could not find {mobPrefabName} prefab for {label}");
            return;
        }

        SpawnArea spawnArea = def.Prefab.GetComponent<SpawnArea>();
        if (!spawnArea)
        {
            CraftableSpawnersPlugin.Log.LogError($"{label} base is missing SpawnArea");
            return;
        }

        // Prefer Greydwarf nest multi-spawn feel; fall back to bone pile values.
        SpawnArea nest = scene.GetPrefab("Spawner_GreydwarfNest")?.GetComponent<SpawnArea>();
        SpawnArea bone = scene.GetPrefab("BonePileSpawner")?.GetComponent<SpawnArea>();
        SpawnArea template = nest ? nest : bone;

        CreatureSpawner vanillaSpawner = scene.GetPrefab(vanillaCreatureSpawnerName)?.GetComponent<CreatureSpawner>();

        if (template)
        {
            spawnArea.m_spawnIntervalSec = template.m_spawnIntervalSec;
            spawnArea.m_triggerDistance = template.m_triggerDistance;
            spawnArea.m_spawnRadius = template.m_spawnRadius;
            spawnArea.m_nearRadius = template.m_nearRadius;
            spawnArea.m_farRadius = template.m_farRadius;
            spawnArea.m_maxNear = template.m_maxNear;
            spawnArea.m_maxTotal = template.m_maxTotal;
            spawnArea.m_setPatrolSpawnPoint = template.m_setPatrolSpawnPoint;
            spawnArea.m_levelupChance = template.m_levelupChance;
            spawnArea.m_spawnEffects = template.m_spawnEffects;
        }

        // Player bases often mark ground as blocked; don't require unblocked ground.
        spawnArea.m_onGroundOnly = false;

        // First spawn ~20s after place, then every 20s (SpawnArea timer ≈ real seconds).
        spawnArea.m_spawnIntervalSec = 20f;

        if (spawnArea.m_triggerDistance <= 0f)
            spawnArea.m_triggerDistance = 60f;
        if (spawnArea.m_maxNear <= 0)
            spawnArea.m_maxNear = 3;
        if (spawnArea.m_maxTotal <= 0)
            spawnArea.m_maxTotal = 3;
        if (spawnArea.m_spawnRadius <= 0f)
            spawnArea.m_spawnRadius = 4f;

        int minLevel = vanillaSpawner ? vanillaSpawner.m_minLevel : 1;
        int maxLevel = vanillaSpawner ? vanillaSpawner.m_maxLevel : 1;

        spawnArea.m_prefabs =
        [
            new SpawnArea.SpawnData
            {
                m_prefab = mob,
                m_weight = 1f,
                m_minLevel = minLevel,
                m_maxLevel = Mathf.Max(minLevel, maxLevel)
            }
        ];

        HoverText hover = def.Prefab.GetComponent<HoverText>();
        if (hover)
            hover.m_text = def.DisplayName;

        CraftableSpawnersPlugin.Log.LogInfo(
            $"[DEBUG-unlock] {label} SpawnArea: interval={spawnArea.m_spawnIntervalSec}s " +
            $"trigger={spawnArea.m_triggerDistance} maxNear={spawnArea.m_maxNear} maxTotal={spawnArea.m_maxTotal} " +
            $"prefab={(spawnArea.m_prefabs[0].m_prefab ? spawnArea.m_prefabs[0].m_prefab.name : "null")}");
    }

    private static void AttachBorrowedVisual(
        GameObject host,
        ZNetScene scene,
        string visualName,
        string[] candidatePrefabs,
        System.Func<MonoBehaviour, bool> keepBehaviours,
        string missingVisualWarning)
    {
        // Hide the borrowed bone-pile meshes; keep colliders for Destructible hits.
        foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        GameObject visualSource = null;
        foreach (string name in candidatePrefabs)
        {
            visualSource = scene.GetPrefab(name);
            if (visualSource)
                break;
        }

        if (!visualSource)
        {
            CraftableSpawnersPlugin.Log.LogWarning($"[DEBUG-unlock] {missingVisualWarning}");
            foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
            return;
        }

        GameObject visual = Object.Instantiate(visualSource, host.transform);
        visual.name = visualName;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        // Strip gameplay components so this is visuals-only; hits use parent Destructible.
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(body);

        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!behaviour)
                continue;

            if (keepBehaviours(behaviour))
                continue;

            Object.DestroyImmediate(behaviour);
        }

        visual.SetActive(true);
    }

    private static void ApplyRecipeAndIcon(SpawnerDef def)
    {
        if (!def.Prefab || ObjectDB.instance == null)
            return;

        Piece piece = def.Prefab.GetComponent<Piece>();
        if (!piece)
            return;

        ModConfig config = CraftableSpawnersPlugin.Settings;
        List<(string item, int amount)> recipe = config.GetRecipe(def.Id);
        List<Piece.Requirement> requirements = [];

        foreach ((string item, int amount) in recipe)
        {
            if (amount <= 0)
                continue;

            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item);
            if (!itemPrefab)
            {
                CraftableSpawnersPlugin.Log.LogWarning($"Missing recipe item prefab '{item}' for {def.CloneName}");
                continue;
            }

            requirements.Add(new Piece.Requirement
            {
                m_resItem = itemPrefab.GetComponent<ItemDrop>(),
                m_amount = amount,
                m_recover = true
            });
        }

        piece.m_resources = requirements.ToArray();

        GameObject trophy = ObjectDB.instance.GetItemPrefab(def.TrophyPrefab);
        if (trophy && trophy.TryGetComponent(out ItemDrop trophyDrop))
            piece.m_icon = trophyDrop.m_itemData.GetIcon();
    }

    private static void RegisterPrefab(ZNetScene scene, GameObject prefab)
    {
        if (!scene.m_prefabs.Contains(prefab))
            scene.m_prefabs.Add(prefab);

        int hash = scene.GetPrefabHash(prefab);
        scene.m_namedPrefabs[hash] = prefab;
    }

    internal static void RefreshFromConfig()
    {
        if (!initialized)
            return;

        EnsurePiecesInHammerTables();

        if (Player.m_localPlayer)
            UnlockKnownSpawners(Player.m_localPlayer);
    }

    internal static void EnsurePiecesInHammerTables()
    {
        if (!initialized)
            return;

        List<PieceTable> tables = GetHammerTables();
        if (tables.Count == 0)
        {
            CraftableSpawnersPlugin.Log.LogWarning("[DEBUG-unlock] No Hammer PieceTable found");
            return;
        }

        ModConfig config = CraftableSpawnersPlugin.Settings;

        foreach (SpawnerDef def in SpawnerCatalog.All)
        {
            if (!def.Prefab)
                continue;

            ApplyRecipeAndIcon(def);
            bool enabled = config.IsEnabled(def.Id);

            foreach (PieceTable table in tables)
            {
                bool inTable = table.m_pieces.Contains(def.Prefab);
                if (enabled && !inTable)
                {
                    table.m_pieces.Add(def.Prefab);
                    CraftableSpawnersPlugin.Log.LogInfo($"[DEBUG-unlock] Added {def.CloneName} to Hammer PieceTable ({table.m_pieces.Count} pieces)");
                }
                else if (!enabled && inTable)
                {
                    table.m_pieces.Remove(def.Prefab);
                }
            }
        }
    }

    private static List<PieceTable> GetHammerTables()
    {
        List<PieceTable> tables = [];
        HashSet<int> seen = [];

        void TryAdd(GameObject hammer)
        {
            PieceTable table = hammer?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (!table)
                return;

            int id = table.GetInstanceID();
            if (seen.Add(id))
                tables.Add(table);
        }

        if (ZNetScene.instance)
            TryAdd(ZNetScene.instance.GetPrefab("Hammer"));

        if (ObjectDB.instance)
            TryAdd(ObjectDB.instance.GetItemPrefab("Hammer"));

        // Only the equipped Hammer — hoe/cultivator tables must not receive spawners.
        if (IsHoldingHammer(Player.m_localPlayer))
        {
            TryAdd(Player.m_localPlayer.GetRightItem()?.m_dropPrefab);
            PieceTable equipped = Player.m_localPlayer.m_buildPieces;
            if (equipped)
            {
                int id = equipped.GetInstanceID();
                if (seen.Add(id))
                    tables.Add(equipped);
            }
        }

        return tables;
    }

    private static bool IsHoldingHammer(Player player)
    {
        ItemDrop.ItemData right = player?.GetRightItem();
        if (right == null)
            return false;

        string prefabName = right.m_dropPrefab ? right.m_dropPrefab.name.Replace("(Clone)", "").Trim() : null;
        if (prefabName == "Hammer")
            return true;

        return right.m_shared?.m_name == "$item_hammer";
    }

    internal static void UnlockKnownSpawners(Player player)
    {
        if (!player || ObjectDB.instance == null)
            return;

        ModConfig config = CraftableSpawnersPlugin.Settings;
        bool unlockedAny = false;

        foreach (SpawnerDef def in SpawnerCatalog.All)
        {
            if (!def.Prefab || !config.IsEnabled(def.Id))
                continue;

            Piece piece = def.Prefab.GetComponent<Piece>();
            if (!piece)
                continue;

            if (!PlayerKnowsTrophy(player, def.TrophyPrefab) || player.IsRecipeKnown(piece.m_name))
                continue;

            player.AddKnownPiece(piece);
            unlockedAny = true;
            CraftableSpawnersPlugin.Dbgl($"Retro-unlocked {def.DisplayName}");
        }

        // UpdateAvailablePiecesList recreates the hammer ghost. Only do that while
        // actually holding the hammer, and only when a piece was newly learned.
        if (unlockedAny && player == Player.m_localPlayer && IsHoldingHammer(player))
            player.UpdateAvailablePiecesList();
    }

    internal static void TryUnlockFromItem(Player player, ItemDrop.ItemData item)
    {
        if (!player || item?.m_shared == null || ObjectDB.instance == null)
            return;

        foreach (SpawnerDef def in SpawnerCatalog.All)
        {
            GameObject trophyPrefab = ObjectDB.instance.GetItemPrefab(def.TrophyPrefab);
            if (!trophyPrefab || !trophyPrefab.TryGetComponent(out ItemDrop trophyDrop))
                continue;

            if (item.m_shared.m_name != trophyDrop.m_itemData.m_shared.m_name)
                continue;

            if (!CraftableSpawnersPlugin.Settings.IsEnabled(def.Id) || !def.Prefab)
                continue;

            Piece piece = def.Prefab.GetComponent<Piece>();
            if (!piece || player.IsRecipeKnown(piece.m_name))
                return;

            player.AddKnownPiece(piece);
            CraftableSpawnersPlugin.Dbgl($"Unlocked {def.DisplayName} from trophy {def.TrophyPrefab}");

            if (player == Player.m_localPlayer && IsHoldingHammer(player))
                player.UpdateAvailablePiecesList();
            return;
        }
    }

    private static bool PlayerKnowsTrophy(Player player, string trophyPrefabName)
    {
        GameObject trophyPrefab = ObjectDB.instance.GetItemPrefab(trophyPrefabName);
        if (!trophyPrefab || !trophyPrefab.TryGetComponent(out ItemDrop trophyDrop))
            return false;

        return player.IsMaterialKnown(trophyDrop.m_itemData.m_shared.m_name);
    }

    internal static void OnCraftableSpawnerPlaced(Piece piece)
    {
        if (!IsCraftableSpawner(piece))
            return;

        RetargetHitboxColliders(piece.gameObject);

        SpawnArea spawnArea = piece.GetComponent<SpawnArea>();
        if (!spawnArea)
            return;

        // Fire pillar / tar bone pile: start the 20s countdown from placement (don't prime an instant spawn).
        SpawnerDef def = SpawnerCatalog.FindByCloneName(piece.transform.root.name);
        if (def?.Id is SpawnerId.Surtling or SpawnerId.TarBlob)
        {
            spawnArea.m_spawnIntervalSec = 20f;
            spawnArea.m_spawnTimer = 0f;
        }
        else
        {
            // Other spawners: allow a first spawn attempt on the next tick.
            spawnArea.m_spawnTimer = spawnArea.m_spawnIntervalSec;
        }

        CraftableSpawnersPlugin.Log.LogInfo(
            $"[DEBUG-unlock] Placed {piece.name}: interval={spawnArea.m_spawnIntervalSec}s " +
            $"timer={spawnArea.m_spawnTimer} prefabs={spawnArea.m_prefabs?.Count ?? 0}");
    }

    internal static bool IsCraftableSpawner(Component component)
    {
        if (!component)
            return false;

        return SpawnerCatalog.FindByCloneName(component.transform.root.name) != null;
    }

    internal static bool TryHammerRemoveCraftableSpawner(Player player)
    {
        if (!player || !GameCamera.instance)
            return false;

        Transform cam = GameCamera.instance.transform;
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 50f, player.m_removeRayMask))
            return false;

        if (Vector3.Distance(hit.point, player.m_eye.position) >= player.m_maxPlaceDistance)
            return false;

        Piece piece = hit.collider.GetComponentInParent<Piece>();
        if (!IsCraftableSpawner(piece) || !piece.m_canBeRemoved)
            return false;

        if (Location.IsInsideNoBuildLocation(piece.transform.position))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
            return true;
        }

        if (!PrivateArea.CheckAccess(piece.transform.position))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            return true;
        }

        if (!player.CheckCanRemovePiece(piece))
            return true;

        ZNetView nview = piece.GetComponent<ZNetView>();
        if (!nview)
            return true;

        if (!piece.CanBeRemoved())
        {
            player.Message(MessageHud.MessageType.Center, "$msg_cantremovenow");
            return true;
        }

        CraftableSpawnersPlugin.HammerRemoving = true;
        try
        {
            // Broken WearNTear from older builds blocks vanilla remove — strip it.
            WearNTear wnt = piece.GetComponent<WearNTear>();
            if (wnt)
                Object.DestroyImmediate(wnt);

            nview.ClaimOwnership();
            RefundToInventory(player, piece);

            if (piece.m_placeEffect != null)
                piece.m_placeEffect.Create(piece.transform.position, piece.transform.rotation, piece.transform);

            player.m_removeEffects.Create(piece.transform.position, Quaternion.identity);
            ZNetScene.instance.Destroy(piece.gameObject);

            ItemDrop.ItemData rightItem = player.GetRightItem();
            if (rightItem != null)
            {
                player.FaceLookDirection();
                player.m_zanim.SetTrigger(rightItem.m_shared.m_attack.m_attackAnimation);
            }

            CraftableSpawnersPlugin.Log.LogInfo($"[DEBUG-unlock] Hammer-removed {piece.name}");
        }
        finally
        {
            CraftableSpawnersPlugin.HammerRemoving = false;
        }

        return true;
    }

    private static void RefundToInventory(Player player, Piece piece)
    {
        if (piece?.m_resources == null)
            return;

        Inventory inventory = player.GetInventory();
        Vector3 dropPos = piece.transform.position + Vector3.up * 0.5f;

        foreach (Piece.Requirement req in piece.m_resources)
        {
            if (req?.m_resItem == null || !req.m_recover || req.m_amount <= 0)
                continue;

            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(req.m_resItem.name);
            if (!itemPrefab)
                continue;

            int remaining = req.m_amount;
            while (remaining > 0)
            {
                ItemDrop.ItemData data = itemPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
                int stack = Mathf.Min(remaining, data.m_shared.m_maxStackSize);
                data.m_stack = stack;
                data.m_dropPrefab = itemPrefab;

                if (inventory.AddItem(data))
                {
                    remaining -= stack;
                    continue;
                }

                ItemDrop.DropItem(data, stack, dropPos, Quaternion.identity);
                remaining -= stack;
            }
        }

        // Prevent any later DropResources from duplicating refunds.
        piece.m_resources = [];
    }

    internal static void DropRecipeAsWorldPickups(GameObject go)
    {
        if (!go || ObjectDB.instance == null)
            return;

        int id = go.GetInstanceID();
        if (!refundedInstanceIds.Add(id))
            return;

        SpawnerDef def = SpawnerCatalog.FindByCloneName(go.transform.root.name);
        if (def == null)
            return;

        Piece piece = go.GetComponentInParent<Piece>();
        if (piece?.m_resources == null || piece.m_resources.Length == 0)
            return;

        Vector3 pos = go.transform.position + Vector3.up * 0.5f;
        Piece.Requirement[] requirements = piece.m_resources;

        // Prevent vanilla DropResources from duplicating the refund on this instance.
        piece.m_resources = [];

        try
        {
            foreach (Piece.Requirement req in requirements)
            {
                if (req?.m_resItem == null || req.m_amount <= 0)
                    continue;

                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(req.m_resItem.name);
                if (!itemPrefab)
                {
                    CraftableSpawnersPlugin.Log.LogWarning($"[DEBUG-unlock] Missing drop prefab for {req.m_resItem.name}");
                    continue;
                }

                int remaining = req.m_amount;
                while (remaining > 0)
                {
                    ItemDrop.ItemData shared = itemPrefab.GetComponent<ItemDrop>().m_itemData;
                    int stack = Mathf.Min(remaining, shared.m_shared.m_maxStackSize);

                    GameObject spawn = Object.Instantiate(itemPrefab, pos, Quaternion.identity);
                    ItemDrop itemDrop = spawn.GetComponent<ItemDrop>();
                    itemDrop.SetStack(stack);
                    ItemDrop.OnCreateNew(itemDrop);

                    remaining -= stack;
                }
            }

            CraftableSpawnersPlugin.Dbgl($"Dropped world refund for {def.CloneName}");
        }
        catch (System.Exception ex)
        {
            // Never block Destructible/WearNTear destroy because refund failed.
            CraftableSpawnersPlugin.Log.LogError($"[DEBUG-unlock] Combat refund failed for {def.CloneName}: {ex}");
        }
    }
}
