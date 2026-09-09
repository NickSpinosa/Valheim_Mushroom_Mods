using System.Collections.Generic;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Biome-scaled block armor per quality for two-handed and dual-wield blocking weapons.
/// Sets <see cref="ItemDrop.ItemData.SharedData.m_blockPowerPerLevel"/> (replaces vanilla).
/// Base <see cref="ItemDrop.ItemData.SharedData.m_blockPower"/> at Q1 is unchanged.
/// </summary>
internal static class WeaponBlockStats
{
    /// <summary>Prefab name → block armor gained per quality step above Q1.</summary>
    private static readonly Dictionary<string, float> BlockPerLevelByPrefab =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Meadows (+1) — median greydwarf ~14
            ["THSwordWood"] = 1f,
            ["BattleaxeWood"] = 1f,
            ["SledgeWood"] = 1f,
            ["AtgeirWood"] = 1f,

            // Black Forest (+1) — median brute ~30
            ["SledgeStagbreaker"] = 1f,
            ["AtgeirBronze"] = 1f,
            ["AxeEarly"] = 1f,
            ["FistBjornClaw"] = 1f,

            // Swamp (+2) — median draugr elite ~58
            ["Battleaxe"] = 2f,
            ["SledgeIron"] = 2f,
            ["AtgeirIron"] = 2f,

            // Mountain (+3) — median fenring ~85
            ["BattleaxeCrystal"] = 3f,

            // Plains (+4) — median deathsquito ~90
            ["BattleaxeBlackmetal"] = 4f,
            ["AtgeirBlackmetal"] = 4f,
            ["AxeBerzerkr"] = 4f,
            ["AxeBerzerkrBlood"] = 4f,
            ["AxeBerzerkrLightning"] = 4f,
            ["AxeBerzerkrNature"] = 4f,
            ["FistBjornUndeadClaw"] = 4f,

            // Mistlands (+5) — median seeker claw ~120
            ["AtgeirHimminAfl"] = 5f,
            ["KnifeSkollAndHati"] = 5f,
            ["FistFenrirClaw"] = 5f,

            // Ashlands (+6) — median charred warrior swing ~150 (Krom anchor)
            ["THSwordKrom"] = 6f,
            ["THSwordSlayer"] = 6f,
            ["THSwordSlayerBlood"] = 6f,
            ["THSwordSlayerLightning"] = 6f,
            ["THSwordSlayerNature"] = 6f,
            ["BattleaxeSkullSplittur"] = 6f,
            ["SledgeDemolisher"] = 6f,

            // Deep North (+7) — one step past Ashlands. The native medium hit for the
            // biome is not known yet (nobody has run 1.0 with this mod), so this is the
            // progression continued rather than a measured value; docs/valheim-1.0.md
            // says which rows the in-game dump still has to confirm.
            //
            // The Gold line is the Deep North tier the way Flametal was Ashlands':
            // <Weapon>Gold, plus _FrostFire and _BloodLightning elemental variants (the
            // 1.0 equivalent of THSwordSlayerBlood / Lightning / Nature). <Weapon>GoldUncooked
            // is the crafting intermediate and deliberately absent — it is a Material,
            // not a weapon.
            ["THSwordGold"] = 7f,
            ["THSwordGold_FrostFire"] = 7f,
            ["THSwordGold_BloodLightning"] = 7f,
            ["BattleaxeGold"] = 7f,
            ["BattleaxeGold_FrostFire"] = 7f,
            ["BattleaxeGold_BloodLightning"] = 7f,
            ["SledgeGold"] = 7f,
            ["SledgeGold_FrostFire"] = 7f,
            ["SledgeGold_BloodLightning"] = 7f,
            ["AtgeirGold"] = 7f,
            ["AtgeirGold_FrostFire"] = 7f,
            ["AtgeirGold_BloodLightning"] = 7f,
            // Dual-wield fists, following FistFenrirClaw.
            ["FistGold"] = 7f,
            ["FistGold_FrostFire"] = 7f,
            ["FistGold_BloodLightning"] = 7f,
            // Named Deep North axe, the tier's AxeBerzerkr / THSwordKrom. Whether it is
            // two-handed or dual-wield is unconfirmed; if it turns out to be a plain
            // one-hander this row is inert rather than wrong, because one-handers pair
            // with a shield and never reach this code path in a way that matters.
            ["AxeJotunBane"] = 7f,
        };

    /// <summary>Prefabs this table looks for, for the diagnostics coverage report.</summary>
    internal static System.Collections.Generic.IEnumerable<string> TrackedPrefabs =>
        BlockPerLevelByPrefab.Keys;

    private static readonly Dictionary<string, float> OriginalPerLevel =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static void ApplyToObjectDB(ObjectDB db)
    {
        if (db?.m_items == null)
            return;

        int weapons = 0;
        foreach (GameObject go in db.m_items)
        {
            if (go == null)
                continue;

            ItemDrop? drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared == null)
                continue;

            string prefab = ShieldStats.PrefabName(go);
            if (!BlockPerLevelByPrefab.TryGetValue(prefab, out float perLevel))
                continue;

            var shared = drop.m_itemData.m_shared;
            if (!OriginalPerLevel.ContainsKey(prefab))
                OriginalPerLevel[prefab] = shared.m_blockPowerPerLevel;

            shared.m_blockPowerPerLevel = ShieldReworkPlugin.EnableWeaponBlockPerLevel.Value
                ? perLevel
                : OriginalPerLevel[prefab];

            weapons++;
        }

        if (weapons > 0)
            ShieldReworkPlugin.Log.LogInfo(
                $"Weapon block per-level: applied table to {weapons} prefabs (enabled={ShieldReworkPlugin.EnableWeaponBlockPerLevel.Value}).");
    }
}
