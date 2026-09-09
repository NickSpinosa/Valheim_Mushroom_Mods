using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace MushroomMods
{
    /// <summary>
    /// Applies a plugin's Harmony patch classes one at a time, so a patch whose target
    /// moved costs that one class instead of the whole plugin.
    /// <para>
    /// <c>Harmony.PatchAll(Assembly)</c> walks exactly the same types
    /// (<c>AccessTools.GetTypesFromAssembly</c> then <c>CreateClassProcessor(type).Patch()</c>)
    /// but lets the first failure escape. That exception unwinds <c>Awake</c>, so
    /// everything after the patch call never runs either. Valheim 1.0.7 demonstrated
    /// it: one changed <c>GetTooltip</c> signature took Combat Adjustments' console
    /// commands, config hooks and sync registration down with it, none of which had
    /// anything to do with tooltips (issues #5, #18).
    /// </para>
    /// <para>
    /// This is a linked source file rather than a shared assembly on purpose. Three of
    /// the seven plugins do not reference MushroomSync, and hardening how patches are
    /// applied is not a reason to make them stop being standalone DLLs. Edit it once in
    /// <c>Shared/</c>; every plugin compiles that same file into itself.
    /// </para>
    /// </summary>
    internal static class PatchIsolation
    {
        /// <summary>
        /// Patches every <c>[HarmonyPatch]</c> class in <paramref name="assembly"/>,
        /// logging and skipping any that throw. Returns how many were skipped.
        /// <para>
        /// Call this last in <c>Awake</c>. Isolation keeps a bad patch class from
        /// unwinding the method, but ordering is the other half of the same fix: work
        /// that has to happen whatever the patches do - config binding, sync
        /// registration, coroutines - belongs above the call, not below it.
        /// </para>
        /// </summary>
        internal static int PatchAllIsolated(Harmony harmony, Assembly assembly, ManualLogSource log)
        {
            int skipped = 0;

            foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    skipped++;

                    // Full exception rather than just the message: a MissingMethodException
                    // names the signature it wanted, which is the whole diagnosis.
                    log.LogError("Harmony patch class " + type.FullName + " was skipped and the rest of "
                        + harmony.Id + " is still active. " + ex);
                }
            }

            return skipped;
        }
    }
}
