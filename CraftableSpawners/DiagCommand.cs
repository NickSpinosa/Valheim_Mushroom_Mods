using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CraftableSpawners;

/// <summary>
/// <c>cs_diag</c>. Walks every gate vanilla <c>SpawnArea.UpdateSpawn</c> and
/// <c>SpawnOne</c> pass through, for each spawner near the player, and says which one
/// is closed - and, for a blocked spawn point, what is blocking it. Not a cheat: it
/// only reads. See docs/spawn-point-blocking.md for the case that motivated it.
/// </summary>
internal static class DiagCommand
{
    private const float SearchRadius = 40f;

    internal static void Run(Terminal.ConsoleEventArgs args)
    {
        Player player = Player.m_localPlayer;
        if (!player || ZNetScene.instance == null || ZoneSystem.instance == null)
        {
            args.Context?.AddString("cs_diag: load a world first.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"cs_diag at {Fmt(player.transform.position)}, " +
                          $"biome={WorldGenerator.instance?.GetBiome(player.transform.position)}, " +
                          $"SpawnSystem.m_nospawn={SpawnSystem.m_nospawn}, " +
                          $"session={ZDOMan.GetSessionID()}");

        int found = 0;
        foreach (SpawnArea area in UnityEngine.Object.FindObjectsByType<SpawnArea>(FindObjectsSortMode.None))
        {
            if (!area || Vector3.Distance(area.transform.position, player.transform.position) > SearchRadius)
                continue;

            found++;
            try
            {
                Describe(area, report);
            }
            catch (Exception ex)
            {
                report.AppendLine($"  !! diag threw on {area.name}: {ex}");
            }
        }

        if (found == 0)
            report.AppendLine($"  No SpawnArea within {SearchRadius} m. If a nest is standing here, " +
                              "its SpawnArea component is missing or its GameObject is inactive.");

        string text = report.ToString();
        CraftableSpawnersPlugin.Log.LogInfo(text);
        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length > 0)
                args.Context?.AddString(line.TrimEnd('\r'));
        }
    }

    private static void Describe(SpawnArea area, StringBuilder report)
    {
        Vector3 pos = area.transform.position;
        bool craftable = SpawnerSetup.IsCraftableSpawner(area);

        report.AppendLine($"- {area.transform.root.name}{(craftable ? " [craftable]" : " [vanilla]")} at {Fmt(pos)} " +
                          $"dist={Vector3.Distance(pos, Player.m_localPlayer.transform.position):0.0} " +
                          $"biome={WorldGenerator.instance?.GetBiome(pos)}");

        // Is UpdateSpawn even being called? Awake starts it with InvokeRepeating; an
        // inactive object or a missing ZNetView stops it before any gate.
        report.AppendLine($"  ticking: activeInHierarchy={area.gameObject.activeInHierarchy} enabled={area.enabled} " +
                          $"invoking UpdateSpawn={area.IsInvoking("UpdateSpawn")}");

        ZNetView nview = area.m_nview;
        if (!nview || !nview.IsValid())
        {
            report.AppendLine($"  GATE CLOSED: ZNetView {(nview ? "invalid (no ZDO)" : "missing")} - UpdateSpawn would throw or never pass IsOwner.");
            return;
        }

        long owner = nview.GetZDO().GetOwner();
        string ownerName = owner == ZDOMan.GetSessionID()
            ? "you"
            : ZNet.instance?.GetPeer(owner)?.m_playerName ?? (owner == 0 ? "nobody" : "unknown peer");
        Gate(report, "IsOwner", nview.IsOwner(), $"owner={owner} ({ownerName})");

        bool outside = ZNetScene.instance.OutsideActiveArea(pos);
        Gate(report, "inside active area", !outside, $"OutsideActiveArea={outside}");

        bool inRange = Player.IsPlayerInRange(pos, area.m_triggerDistance);
        Gate(report, "player in trigger range", inRange, $"triggerDistance={area.m_triggerDistance}");

        report.AppendLine($"  timer: {area.m_spawnTimer:0.0} / {area.m_spawnIntervalSec:0.0}s");

        Gate(report, "not m_nospawn", !SpawnSystem.m_nospawn, "");

        area.GetInstances(out int near, out int total);
        Gate(report, "near cap", near < area.m_maxNear, $"near={near}/{area.m_maxNear} within {area.m_nearRadius} m");
        Gate(report, "total cap", total < area.m_maxTotal, $"total={total}/{area.m_maxTotal} within {area.m_farRadius} m");
        if (near > 0 || total > 0)
            ListCounted(area, report);

        if (area.m_prefabs == null || area.m_prefabs.Count == 0)
        {
            report.AppendLine("  GATE CLOSED: m_prefabs is empty - nothing to select.");
        }
        else
        {
            foreach (SpawnArea.SpawnData data in area.m_prefabs)
                report.AppendLine($"  prefab: {(data?.m_prefab ? data.m_prefab.name : "NULL")} weight={data?.m_weight} " +
                                  $"level={data?.m_minLevel}-{data?.m_maxLevel}");
        }

        // FindSpawnPoint: ten random points within m_spawnRadius; any with a floor wins.
        int floors = 0, blocked = 0;
        const int samples = 40;
        for (int i = 0; i < samples; i++)
        {
            Vector3 point = pos + Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f)
                * Vector3.forward * UnityEngine.Random.Range(0f, area.m_spawnRadius);
            if (!ZoneSystem.instance.FindFloor(point, out _))
                continue;
            floors++;
            if (area.m_onGroundOnly && ZoneSystem.instance.IsBlocked(point))
                blocked++;
        }
        Gate(report, "spawn point", floors - blocked > 0,
            $"{floors}/{samples} samples found floor, {blocked} blocked (onGroundOnly={area.m_onGroundOnly}, radius={area.m_spawnRadius})");

        // IsBlocked is a single ray straight down from 2000 m above the point against
        // Default/static_solid/Default_small/piece. Name whatever it hits.
        var hits = new Dictionary<string, int>();
        int blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
        for (int i = 0; i < samples; i++)
        {
            Vector3 point = pos + Quaternion.Euler(0f, i * 360f / samples, 0f) * Vector3.forward * (area.m_spawnRadius * ((i % 4) + 1) / 4f);
            point.y += 2000f;
            if (!Physics.Raycast(point, Vector3.down, out RaycastHit hit, 10000f, blockMask))
                continue;
            string key = $"{hit.collider.transform.root.name} / {hit.collider.name} " +
                         $"layer={LayerMask.LayerToName(hit.collider.gameObject.layer)} " +
                         $"{hit.collider.GetType().Name} hitY~{Mathf.Round(hit.point.y)}";
            hits[key] = hits.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        foreach (KeyValuePair<string, int> pair in hits)
            report.AppendLine($"    blocked by: {pair.Key} x{pair.Value}");

        // Runs after the creature is instantiated, so a throw here would look like
        // "spawned then vanished" rather than "never spawned".
        try
        {
            report.AppendLine($"  levelUpChance={area.GetLevelUpChance():0.##}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"  !! GetLevelUpChance threw (runs after Instantiate in SpawnOne): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ListCounted(SpawnArea area, StringBuilder report)
    {
        var counts = new Dictionary<string, int>();
        Vector3 pos = area.transform.position;
        foreach (BaseAI ai in BaseAI.BaseAIInstances)
        {
            if (!ai || !area.IsSpawnPrefab(ai.gameObject)
                || Utils.DistanceXZ(ai.transform.position, pos) >= area.m_farRadius)
                continue;

            string key = ai.gameObject.name;
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        foreach (KeyValuePair<string, int> pair in counts)
            report.AppendLine($"    counted: {pair.Key} x{pair.Value}");
    }

    private static void Gate(StringBuilder report, string name, bool open, string detail)
    {
        report.AppendLine($"  {(open ? "ok    " : "CLOSED")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
}
