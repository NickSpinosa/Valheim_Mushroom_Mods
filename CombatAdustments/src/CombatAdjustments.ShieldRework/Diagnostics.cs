using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// One-shot dump of the things this mod's tables have to be checked against, and
/// which cannot be read out of the game assemblies:
///
///   1. Every global key set on the world. Boss keys past Yagluth are data-driven —
///      they live on the boss prefab, not as string literals in assembly_valheim.dll —
///      so the only way to confirm the Deep North key is to kill the boss and look.
///   2. Every <see cref="ItemDrop"/> prefab name with its item type. Prefab spellings
///      for a new biome are otherwise guesswork; see <c>docs/valheim-1.0.md</c> for how
///      the Deep North rows currently in the tables were sourced.
///   3. A coverage report: for every prefab our tables name, whether ObjectDB has it.
///      A row that says MISSING is a typo or a renamed prefab, and it is silent in game.
///   4. Shield and food stat lines, because the seed formulas need real block armor and
///      real vanilla food values to be re-derived rather than extrapolated.
///
/// Off by default (<c>[Diagnostics] DumpObjectDb</c>) and local-only: never synced, so a
/// server switching it on does not make every client write a file. Fires on the first
/// ObjectDB apply after it is switched on, and can be forced from the console with
/// <c>cadump</c>.
/// </summary>
internal static class Diagnostics
{
    private const string FileName = "CombatAdjustments.ShieldRework.objectdb-dump.txt";

    private static bool _dumped;

    /// <summary>
    /// Boss keys the feast gate uses, plus the Deep North key it does not use yet.
    /// FrozenKing is listed so the dump answers "is the spelling right?" in one line.
    /// </summary>
    private static readonly (string Boss, string Key)[] BossKeys =
    {
        ("Eikthyr", FeastUnlocks.Eikthyr),
        ("Elder", FeastUnlocks.Elder),
        ("Bonemass", FeastUnlocks.Bonemass),
        ("Moder", FeastUnlocks.Moder),
        ("Yagluth", FeastUnlocks.Yagluth),
        ("Queen", FeastUnlocks.Queen),
        ("Fader", FeastUnlocks.Fader),
        ("FrozenKing", FeastUnlocks.FrozenKing),
    };

    /// <summary>Written beside the .cfg the plugin actually loaded, so the client and
    /// dedicated-server layouts each get the file next to their own config.</summary>
    internal static string DumpPath
    {
        get
        {
            string? dir = null;
            try
            {
                dir = Path.GetDirectoryName(ShieldReworkPlugin.ModConfig.ConfigFilePath);
            }
            catch (Exception)
            {
                // Fall through to the BepInEx config dir below.
            }

            if (string.IsNullOrEmpty(dir))
                dir = BepInEx.Paths.ConfigPath;

            return Path.Combine(dir!, FileName);
        }
    }

    /// <summary>Called from the ObjectDB patches. No-op unless the config flag is on.</summary>
    internal static void DumpOnceIfEnabled(ObjectDB db)
    {
        if (_dumped)
            return;
        if (ShieldReworkPlugin.DumpObjectDb == null || !ShieldReworkPlugin.DumpObjectDb.Value)
            return;

        _dumped = true;
        Write(db, "ObjectDB load, [Diagnostics] DumpObjectDb = true");
    }

    /// <summary>Writes the dump and returns the path, or null if it could not be written.</summary>
    internal static string? Write(ObjectDB? db, string reason)
    {
        string path = DumpPath;
        try
        {
            var sb = new StringBuilder();
            Header(sb, reason);
            GlobalKeys(sb);
            TableCoverage(sb, db);
            Shields(sb, db);
            Foods(sb, db);
            AllItems(sb, db);
            sb.AppendLine("===== END DUMP =====");

            File.WriteAllText(path, sb.ToString());
            ShieldReworkPlugin.Log.LogInfo($"Diagnostics: ObjectDB dump written to {path}");
            return path;
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogError($"Diagnostics: could not write {path}: {ex.Message}");
            return null;
        }
    }

    private static void Header(StringBuilder sb, string reason)
    {
        sb.AppendLine("===== COMBAT ADJUSTMENTS :: OBJECTDB DUMP =====");
        sb.AppendLine($"Plugin:   {ShieldReworkPlugin.PluginName} {ShieldReworkPlugin.PluginVersion}");
        // Version.CurrentVersion, not Version.GetVersionString(): that method's only
        // parameter is optional, and C# bakes optional defaults into the caller, so a
        // game update that adds a second one turns this line into a MissingMethodException.
        // See docs/valheim-1.0.md.
        sb.AppendLine($"Game:     {Version.CurrentVersion}");
        sb.AppendLine($"Written:  {DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Trigger:  {reason}");
        sb.AppendLine();
    }

    private static void GlobalKeys(StringBuilder sb)
    {
        sb.AppendLine("--- GLOBAL KEYS CURRENTLY SET ---");
        ZoneSystem? zs = ZoneSystem.instance;
        if (zs == null)
        {
            sb.AppendLine("  ZoneSystem not available (run this from inside a world).");
            sb.AppendLine();
            return;
        }

        bool any = false;
        foreach (string key in zs.GetGlobalKeys())
        {
            sb.AppendLine("  " + key);
            any = true;
        }

        if (!any)
            sb.AppendLine("  (none set on this world)");

        sb.AppendLine();
        sb.AppendLine("--- BOSS KEYS THE FEAST GATE USES ---");
        foreach ((string boss, string key) in BossKeys)
        {
            sb.AppendLine("  " + boss.PadRight(12) + "'" + key + "'".PadRight(24)
                          + " set: " + zs.GetGlobalKey(key));
        }

        sb.AppendLine("  NOTE: a boss you have killed whose row says set: False means the");
        sb.AppendLine("        spelling above is wrong. Copy the real key from the list at");
        sb.AppendLine("        the top of this section into FeastUnlocks.");
        sb.AppendLine();
    }

    private static void TableCoverage(StringBuilder sb, ObjectDB? db)
    {
        sb.AppendLine("--- MOD TABLE COVERAGE ---");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (db?.m_items != null)
        {
            foreach (GameObject go in db.m_items)
            {
                if (go == null)
                    continue;
                if (go.GetComponent<ItemDrop>() != null)
                    known.Add(ShieldStats.PrefabName(go));
            }
        }

        if (known.Count == 0)
        {
            sb.AppendLine("  ObjectDB not available; coverage cannot be checked.");
            sb.AppendLine();
            return;
        }

        Coverage(sb, known, "FeastUnlocks spice gates", FeastUnlocks.TrackedSpicePrefabs);
        Coverage(sb, known, "FeastUnlocks recipe gates", FeastUnlocks.TrackedRecipePrefabs);
        Coverage(sb, known, "FeastStats", FeastStats.TrackedPrefabs);
        Coverage(sb, known, "WeaponBlockStats", WeaponBlockStats.TrackedPrefabs);
        Coverage(sb, known, "ShieldStats tower seeds", ShieldStats.TrackedTowerSeedPrefabs);
        Coverage(sb, known, "ShieldStats grant steps", ShieldStats.TrackedGrantStepPrefabs);
        sb.AppendLine();
    }

    private static void Coverage(StringBuilder sb, HashSet<string> known, string label,
                                 IEnumerable<string> prefabs)
    {
        sb.AppendLine("  [" + label + "]");
        foreach (string prefab in prefabs)
        {
            sb.AppendLine("    " + prefab.PadRight(32)
                          + (known.Contains(prefab) ? "ok" : "MISSING — wrong spelling, or not in this game version"));
        }
    }

    private static void Shields(StringBuilder sb, ObjectDB? db)
    {
        sb.AppendLine("--- SHIELDS ---");
        sb.AppendLine("  name / kind / maxQ / blockPower / perLevel / maxBlock / parryBonus / our grant");
        if (db?.m_items == null)
        {
            sb.AppendLine("  ObjectDB not available.");
            sb.AppendLine();
            return;
        }

        foreach (GameObject go in db.m_items)
        {
            ItemDrop.ItemData.SharedData? shared = SharedOf(go);
            if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
                continue;

            string prefab = ShieldStats.PrefabName(go!);
            ShieldKind kind = ShieldStats.Classify(shared);
            sb.AppendLine("  " + prefab.PadRight(30)
                          + kind.ToString().PadRight(9)
                          + "q" + shared.m_maxQuality.ToString(CultureInfo.InvariantCulture).PadRight(4)
                          + Num(shared.m_blockPower).PadRight(9)
                          + Num(shared.m_blockPowerPerLevel).PadRight(9)
                          + Num(ShieldStats.MaxBaseBlockPower(shared)).PadRight(10)
                          + Num(shared.m_timedBlockBonus).PadRight(8)
                          + "+" + Num(ShieldStats.GetMaxGrant(prefab)));
        }
        sb.AppendLine();
    }

    private static void Foods(StringBuilder sb, ObjectDB? db)
    {
        sb.AppendLine("--- FOODS (health / stamina / eitr / burn time) ---");
        sb.AppendLine("  Values shown are the CURRENT shared values, i.e. vanilla plus this mod's");
        sb.AppendLine("  feast bonuses. Set Feasts.EnableStatBonuses = false to read vanilla numbers.");
        if (db?.m_items == null)
        {
            sb.AppendLine("  ObjectDB not available.");
            sb.AppendLine();
            return;
        }

        foreach (GameObject go in db.m_items)
        {
            ItemDrop.ItemData.SharedData? shared = SharedOf(go);
            if (shared == null)
                continue;
            if (shared.m_food <= 0f && shared.m_foodStamina <= 0f && shared.m_foodEitr <= 0f)
                continue;

            sb.AppendLine("  " + ShieldStats.PrefabName(go!).PadRight(32)
                          + Num(shared.m_food).PadRight(8)
                          + Num(shared.m_foodStamina).PadRight(8)
                          + Num(shared.m_foodEitr).PadRight(8)
                          + Num(shared.m_foodBurnTime));
        }
        sb.AppendLine();
    }

    private static void AllItems(StringBuilder sb, ObjectDB? db)
    {
        sb.AppendLine("--- ALL ITEMDROP PREFABS ---");
        if (db?.m_items == null)
        {
            sb.AppendLine("  ObjectDB not available.");
            return;
        }

        int count = 0;
        foreach (GameObject go in db.m_items)
        {
            ItemDrop.ItemData.SharedData? shared = SharedOf(go);
            if (shared == null)
                continue;

            sb.AppendLine("  " + ShieldStats.PrefabName(go!).PadRight(36) + shared.m_itemType);
            count++;
        }

        sb.AppendLine($"  ({count} item prefabs)");
    }

    private static ItemDrop.ItemData.SharedData? SharedOf(GameObject? go)
    {
        if (go == null)
            return null;
        ItemDrop? drop = go.GetComponent<ItemDrop>();
        return drop?.m_itemData?.m_shared;
    }

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
