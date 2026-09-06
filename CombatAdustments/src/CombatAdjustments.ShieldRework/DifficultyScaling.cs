using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Uncaps multiplayer effective-HP scaling past vanilla's 5-player ceiling while
/// leaving enemy damage scaling capped. See <c>docs/boss-hp-scaling.md</c>.
/// </summary>
internal static class DifficultyScaling
{
    /// <summary>
    /// Same as <see cref="Game.GetPlayerDifficulty"/> but without
    /// <c>m_difficultyScaleMaxPlayers</c>. Honours <c>m_forcePlayers</c> and
    /// <c>m_difficultyScaleRange</c>.
    /// </summary>
    internal static int UncappedPlayerDifficulty(Game game, Vector3 pos)
    {
        int forced = Traverse.Create(game).Field("m_forcePlayers").GetValue<int>();
        if (forced > 0)
            return forced;

        int players = Player.GetPlayersInRangeXZ(pos, game.m_difficultyScaleRange);
        return players < 1 ? 1 : players;
    }

    /// <summary>
    /// Vanilla <c>GetDifficultyDamageScaleEnemy</c>:
    /// <c>1 / (1 + (n - 1) * m_healthScalePerPlayer)</c>.
    /// </summary>
    internal static float EnemyDamageTakenScale(Game game, int players)
    {
        float healthScale = 1f + (players - 1) * game.m_healthScalePerPlayer;
        return 1f / healthScale;
    }
}

/// <summary>
/// Vanilla (and Valheim Plus prefixes) still run first. We only replace the
/// result so HP uses an uncapped nearby count; damage stays on
/// <see cref="Game.GetDifficultyDamageScalePlayer"/> → capped
/// <see cref="Game.GetPlayerDifficulty"/>.
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScaleEnemy))]
internal static class Game_GetDifficultyDamageScaleEnemy_Patch
{
    private static void Postfix(Game __instance, Vector3 pos, ref float __result)
    {
        if (!ShieldReworkPlugin.EnableUncapHealthScaling.Value)
            return;

        int players = DifficultyScaling.UncappedPlayerDifficulty(__instance, pos);
        __result = DifficultyScaling.EnemyDamageTakenScale(__instance, players);
    }
}
