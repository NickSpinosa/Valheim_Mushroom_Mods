using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Heavy-weapon adjustments:
/// - Balanced hyper armor for greatswords, battleaxes, and sledges only.
/// - +10% damage (bonus rounded down per damage type) for greatswords, battleaxes, and sledges.
/// - 1.5x final stagger on greatsword primary-chain swings.
///
/// Balanced hyper armor begins once Valheim enters an attack animation and ends
/// after that attack's single hit event. It blocks stagger bar fill and the stagger
/// animation. Incoming damage and knockback remain vanilla.
/// </summary>
internal static class TwoHandedCombat
{
    /// <summary>Flat +10% to each non-zero damage component; bonus amount is rounded down.</summary>
    internal const float TwoHandedDamageBonusFraction = 0.10f;

    internal static bool TooltipDamagePreview;

    private static readonly Dictionary<Player, Attack> ActiveHyperArmor = new();

    private static bool Enabled => ShieldReworkPlugin.EnableTwoHandedCombat.Value;

    internal static bool IsTwoHandedMelee(ItemDrop.ItemData? weapon)
    {
        if (weapon?.m_shared == null)
            return false;

        if (weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon)
            return false;

        Skills.SkillType skill = weapon.m_shared.m_skillType;
        return skill == Skills.SkillType.Swords
            || skill == Skills.SkillType.Axes
            || skill == Skills.SkillType.Clubs
            || skill == Skills.SkillType.Polearms;
    }

    internal static bool HasBalancedHyperArmor(ItemDrop.ItemData? weapon)
    {
        return HasTwoHandedDamageBonus(weapon);
    }

    /// <summary>
    /// Greatswords, battleaxes, and sledges only.
    /// Dual-wield axes/knives also use <see cref="ItemDrop.ItemData.ItemType.TwoHandedWeapon"/>,
    /// so animation state is the gate (excludes DualAxes / Knives).
    /// </summary>
    internal static bool HasTwoHandedDamageBonus(ItemDrop.ItemData? weapon)
    {
        if (!Enabled || weapon?.m_shared == null)
            return false;

        if (weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon)
            return false;

        return weapon.m_shared.m_animationState
            is ItemDrop.ItemData.AnimationState.Greatsword
            or ItemDrop.ItemData.AnimationState.TwoHandedAxe
            or ItemDrop.ItemData.AnimationState.TwoHandedClub;
    }

    /// <summary>
    /// Must take the struct by ref. <see cref="HitData.DamageTypes"/> is a value type;
    /// a by-value parameter mutates a copy and the tooltip / hit stay vanilla.
    /// </summary>
    internal static void ApplyRoundedTenPercentDamageBonus(ref HitData.DamageTypes damages)
    {
        damages.m_damage = AddRoundedPercentBonus(damages.m_damage);
        damages.m_blunt = AddRoundedPercentBonus(damages.m_blunt);
        damages.m_slash = AddRoundedPercentBonus(damages.m_slash);
        damages.m_pierce = AddRoundedPercentBonus(damages.m_pierce);
        damages.m_chop = AddRoundedPercentBonus(damages.m_chop);
        damages.m_pickaxe = AddRoundedPercentBonus(damages.m_pickaxe);
        damages.m_fire = AddRoundedPercentBonus(damages.m_fire);
        damages.m_frost = AddRoundedPercentBonus(damages.m_frost);
        damages.m_lightning = AddRoundedPercentBonus(damages.m_lightning);
        damages.m_poison = AddRoundedPercentBonus(damages.m_poison);
        damages.m_spirit = AddRoundedPercentBonus(damages.m_spirit);
    }

    private static float AddRoundedPercentBonus(float value) =>
        value <= 0f ? value : value + Mathf.Floor(value * TwoHandedDamageBonusFraction);

    internal static bool IsTwoHandedClub(ItemDrop.ItemData? weapon) =>
        IsTwoHandedMelee(weapon) && weapon!.m_shared.m_skillType == Skills.SkillType.Clubs;

    internal static bool IsGreatswordPrimary(Attack attack, ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared != null
            && weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon
            && weapon.m_shared.m_skillType == Skills.SkillType.Swords
            && attack.m_attackChainLevels > 1;
    }

    internal static void BeginBalancedHyperArmor(Attack attack)
    {
        if (!Enabled)
            return;

        ItemDrop.ItemData? weapon = attack.GetWeapon();
        if (AttackOwner(attack) is Player player
            && player.InAttack()
            && HasBalancedHyperArmor(weapon))
        {
            ActiveHyperArmor[player] = attack;
        }
    }

    internal static void EndBalancedHyperArmor(Attack attack)
    {
        if (AttackOwner(attack) is Player player
            && ActiveHyperArmor.TryGetValue(player, out Attack? active)
            && ReferenceEquals(active, attack))
        {
            ActiveHyperArmor.Remove(player);
        }
    }

    internal static bool HasActiveHyperArmor(Player player) =>
        Enabled
        && ActiveHyperArmor.TryGetValue(player, out Attack? attack)
        && player.InAttack()
        && ReferenceEquals(attack.GetWeapon(), player.GetCurrentWeapon());

    private static Humanoid? AttackOwner(Attack attack) =>
        Traverse.Create(attack).Field("m_character").GetValue<Humanoid>();
}

/// <summary>
/// Vanilla DoAreaAttack (sledge ground slam) awards adrenaline once per swing using
/// only the highest enemy multiplier. Swing attacks (DoMeleeAttack) pay per enemy.
/// For two-handed clubs we count every enemy damaged by the slam and add the
/// difference, so slams charge adrenaline like swings do.
/// </summary>
internal static class AreaAdrenaline
{
    private static Player? s_attacker;
    private static float s_multiplierSum;
    private static float s_multiplierMax;
    private static readonly HashSet<Character> s_counted = new();

    internal static bool Open(Attack attack, Humanoid? owner)
    {
        if (!ShieldReworkPlugin.EnableTwoHandedCombat.Value
            || !ShieldReworkPlugin.AreaAdrenalinePerEnemy.Value)
            return false;
        if (owner is not Player player || player != Player.m_localPlayer)
            return false;
        if (!TwoHandedCombat.IsTwoHandedClub(attack.GetWeapon()))
            return false;

        s_attacker = player;
        s_multiplierSum = 0f;
        s_multiplierMax = 0f;
        s_counted.Clear();
        return true;
    }

    internal static void RecordDamage(Character victim)
    {
        if (s_attacker == null || victim == s_attacker || !s_counted.Add(victim))
            return;

        // Same enemy test vanilla uses before tracking the adrenaline multiplier.
        bool isEnemy = BaseAI.IsEnemy(s_attacker, victim)
            || (victim.GetBaseAI() != null && victim.GetBaseAI().IsAggravatable());
        if (!isEnemy)
            return;

        s_multiplierSum += victim.m_enemyAdrenalineMultiplier;
        if (victim.m_enemyAdrenalineMultiplier > s_multiplierMax)
            s_multiplierMax = victim.m_enemyAdrenalineMultiplier;
    }

    internal static void CloseAndAward(Attack attack)
    {
        Player? attacker = s_attacker;
        s_attacker = null;
        s_counted.Clear();
        if (attacker == null)
            return;

        // Vanilla already granted m_attackAdrenaline * max inside DoAreaAttack.
        float extra = attack.m_attackAdrenaline * (s_multiplierSum - s_multiplierMax);
        if (extra > 0f)
            attacker.AddAdrenaline(extra);
    }

    internal static void Abort() => s_attacker = null;
}

[HarmonyPatch(typeof(Attack), "DoAreaAttack")]
internal static class Attack_DoAreaAttack_Adrenaline_Patch
{
    private static void Prefix(Attack __instance, Humanoid ___m_character)
    {
        AreaAdrenaline.Open(__instance, ___m_character);
    }

    private static void Postfix(Attack __instance)
    {
        AreaAdrenaline.CloseAndAward(__instance);
    }

    private static System.Exception? Finalizer(System.Exception __exception)
    {
        if (__exception != null)
            AreaAdrenaline.Abort();
        return __exception;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class Character_Damage_AreaAdrenaline_Patch
{
    private static void Prefix(Character __instance) => AreaAdrenaline.RecordDamage(__instance);
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Update))]
internal static class Attack_Update_HyperArmor_Patch
{
    private static void Postfix(Attack __instance) => TwoHandedCombat.BeginBalancedHyperArmor(__instance);
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class Attack_OnAttackTrigger_HyperArmor_Patch
{
    // Postfix keeps the protection active for the entire multi-target hit check,
    // then exposes the recovery animation.
    private static void Postfix(Attack __instance) => TwoHandedCombat.EndBalancedHyperArmor(__instance);
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
internal static class Attack_Stop_HyperArmor_Patch
{
    private static void Postfix(Attack __instance) => TwoHandedCombat.EndBalancedHyperArmor(__instance);
}

[HarmonyPatch(typeof(Character), "AddStaggerDamage")]
internal static class Character_AddStaggerDamage_HyperArmor_Patch
{
    private static bool Prefix(Character __instance, ref bool __result)
    {
        if (__instance is Player player && TwoHandedCombat.HasActiveHyperArmor(player))
        {
            __result = false;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Stagger))]
internal static class Character_Stagger_HyperArmor_Patch
{
    private static bool Prefix(Character __instance)
    {
        return __instance is not Player player || !TwoHandedCombat.HasActiveHyperArmor(player);
    }
}

[HarmonyPatch(typeof(Attack), "ModifyDamage")]
internal static class Attack_ModifyDamage_TwoHanded_Patch
{
    private static void Postfix(Attack __instance, HitData hitData)
    {
        if (!ShieldReworkPlugin.EnableTwoHandedCombat.Value)
            return;

        ItemDrop.ItemData? weapon = __instance.GetWeapon();
        if (!TwoHandedCombat.HasTwoHandedDamageBonus(weapon))
            return;

        TwoHandedCombat.ApplyRoundedTenPercentDamageBonus(ref hitData.m_damage);

        if (TwoHandedCombat.IsGreatswordPrimary(__instance, weapon))
            hitData.m_staggerMultiplier *= ShieldReworkPlugin.GreatswordPrimaryStaggerMultiplier.Value;
    }
}

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
internal static class ItemData_GetDamage_TwoHandedTooltip_Patch
{
    private static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
    {
        if (!TwoHandedCombat.TooltipDamagePreview
            || !ShieldReworkPlugin.EnableTwoHandedCombat.Value
            || !TwoHandedCombat.HasTwoHandedDamageBonus(__instance))
            return;

        TwoHandedCombat.ApplyRoundedTenPercentDamageBonus(ref __result);
    }
}

// See the note on ItemData_GetTooltip_Patch: the 1.0.7 static overload ends with
// `bool appending = false`, and the type list has to spell out that optional too.
[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
    typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
internal static class ItemData_GetTooltip_TwoHanded_Patch
{
    private static void Prefix() => TwoHandedCombat.TooltipDamagePreview = true;

    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        TwoHandedCombat.TooltipDamagePreview = false;

        if (!TwoHandedCombat.HasBalancedHyperArmor(item))
            return;

        const string line = "\n<color=orange>Hyper-armor</color>";
        if (__result.IndexOf("Hyper-armor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        // Sit with combat stats: after knockback if present, otherwise append.
        const string knockbackKey = "$item_knockback:";
        int knockIdx = __result.IndexOf(knockbackKey, System.StringComparison.Ordinal);
        if (knockIdx >= 0)
        {
            int lineEnd = __result.IndexOf('\n', knockIdx);
            if (lineEnd < 0)
                __result += line;
            else
                __result = __result.Insert(lineEnd, line);
        }
        else
        {
            __result += line;
        }
    }
}
