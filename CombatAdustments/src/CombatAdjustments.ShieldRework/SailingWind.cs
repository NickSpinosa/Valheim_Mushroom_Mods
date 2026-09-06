using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Replaces vanilla's single <c>Lerp(0.25, 1, intensity)</c> with a two-segment
/// map: calm band matches vanilla through 60% wind, then storms climb to 2×.
/// See <c>docs/sailing.md</c>.
/// </summary>
internal static class SailingWind
{
    /// <summary>
    /// Maps EnvMan wind intensity (0–1) to the sail intensity factor that
    /// multiplies <see cref="Ship.GetWindAngleFactor"/>.
    /// </summary>
    internal static float ForceFactor(float windIntensity)
    {
        float atZero = ShieldReworkPlugin.SailingCalmForceFactor.Value;
        float atKnee = ShieldReworkPlugin.SailingKneeForceFactor.Value;
        float atMax = ShieldReworkPlugin.SailingMaxForceFactor.Value;
        float knee = Mathf.Clamp01(ShieldReworkPlugin.SailingCalmWindCeiling.Value);

        windIntensity = Mathf.Clamp01(windIntensity);

        if (knee <= 0f)
            return Mathf.Lerp(atKnee, atMax, windIntensity);

        if (windIntensity <= knee)
            return Mathf.Lerp(atZero, atKnee, windIntensity / knee);

        if (knee >= 1f)
            return atKnee;

        return Mathf.Lerp(atKnee, atMax, (windIntensity - knee) / (1f - knee));
    }
}

/// <summary>
/// Vanilla bakes <c>Lerp(0.25, 1, intensity)</c> into GetSailForce. Replace the
/// whole method so the intensity factor comes from <see cref="SailingWind"/>.
/// </summary>
[HarmonyPatch(typeof(Ship), "GetSailForce")]
internal static class Ship_GetSailForce_Patch
{
    private static bool Prefix(
        Ship __instance,
        float sailSize,
        ref Vector3 __result,
        ref Vector3 ___m_sailForce,
        ref Vector3 ___m_windChangeVelocity)
    {
        if (!ShieldReworkPlugin.EnableSailingWindCurve.Value)
            return true;

        Vector3 windDir = EnvMan.instance.GetWindDir();
        float windIntensity = EnvMan.instance.GetWindIntensity();
        float intensityFactor = SailingWind.ForceFactor(windIntensity);
        float windAngleFactor = __instance.GetWindAngleFactor() * intensityFactor;
        Vector3 target = Vector3.Normalize(windDir + __instance.transform.forward)
            * (windAngleFactor * __instance.m_sailForceFactor * sailSize);
        ___m_sailForce = Vector3.SmoothDamp(
            ___m_sailForce, target, ref ___m_windChangeVelocity, 1f, 99f);
        __result = ___m_sailForce;
        return false;
    }
}
