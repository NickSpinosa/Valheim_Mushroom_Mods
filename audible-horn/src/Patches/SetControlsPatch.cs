using System;
using HarmonyLib;
using UnityEngine;

namespace AudibleHorn
{
    /// <summary>
    /// Turns the attack input into a Horn Call while a Signal Horn is held.
    ///
    /// <c>Player.SetControls</c> is the interception point rather than
    /// <c>Humanoid.StartAttack</c> or <c>Attack.Start</c>, and that is the whole trick
    /// - see "Why the attack is intercepted in SetControls" in docs/DESIGN.md. The
    /// first thing vanilla SetControls does is stand an attached player up and stop
    /// their emote; by the time an attack method runs, the input that was meant for
    /// the horn has already been spent on getting out of the seat.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls), new[]
    {
        typeof(Vector3),    // movedir
        typeof(bool),       // attack
        typeof(bool),       // attackHold
        typeof(bool),       // secondaryAttack
        typeof(bool),       // secondaryAttackHold
        typeof(bool),       // block
        typeof(bool),       // blockHold
        typeof(bool),       // jump
        typeof(bool),       // crouch
        typeof(bool),       // run
        typeof(bool),       // autoRun
        typeof(bool)        // dodge = false
    })]
    internal static class PlayerSetControlsPatch
    {
        /// <summary>
        /// Wrapped like every other patch in this mod. A throw out of a prefix aborts
        /// the whole patch chain <em>and</em> skips the original, which on this method
        /// means the player stops responding to input entirely - the single worst
        /// failure this mod could cause. <see cref="Priority.First"/> so a mod that
        /// clears the attack flag for its own reasons cannot silently make the horn
        /// unsoundable; this prefix only ever clears input, never adds it, so running
        /// early costs nobody anything.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(Player __instance, ref bool attack)
        {
            try
            {
                Blow(__instance, ref attack);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sounding the " + SignalHornItem.DisplayName + " failed: " + e);
            }
        }

        private static void Blow(Player player, ref bool attack)
        {
            // Remote players run SetControls too, fed by input replicated from the
            // machine that owns them. Their horn reaches this client as a Horn Call
            // over the RPC and nothing else; acting here would sound it twice and
            // charge this client's cooldown for someone else's press.
            if (player == null || player != Player.m_localPlayer)
                return;

            // The edge, never the held state. PlayerController computes `attack` as
            // "held now and not held on the previous physics tick" (against its own
            // m_attackWasPressed), so it is true for exactly one tick per press;
            // `attackHold` stays true for as long as the button is down and would
            // sound the horn every tick.
            if (!attack)
                return;

            HornBlower.HornSlot slot = HornBlower.FindHorn(player);
            if (slot == HornBlower.HornSlot.None)
                return;

            string doing;
            bool bodyFree = HornBlower.IsBodyFree(player, out doing);

            // Read before TryBlow: a call that goes through resets the clock, so asking
            // afterwards would report the new cooldown rather than the one that just
            // refused this attempt.
            float remaining = HornBlower.RemainingCooldown();
            bool sent = HornBlower.TryBlow(player);

            // One line per attempted blow. An agent cannot hear the horn, so this and
            // the Horn Call lines are the evidence that the seated, swimming and riding
            // cases behave. It is not per-frame logging: `attack` is a one-tick edge and
            // the Horn Cooldown rate-limits it further.
            Plugin.Log.LogInfo(
                "Signal Horn sounded: horn equipped " +
                (slot == HornBlower.HornSlot.Visible ? "visibly" : "hidden") +
                ", body " + (bodyFree ? "free" : "not free") + " (" + doing + ") - " +
                (sent
                    ? "Horn Call sent."
                    : "blocked by the Horn Cooldown, " + remaining.ToString("0.0") + " s remaining.") +
                (bodyFree ? " Drink animation plays." : " Sound only; no animation, and the player stays put."));

            if (!bodyFree)
            {
                // The one line that keeps a seated Blower seated. Vanilla's opening
                // block calls StopEmote() and AttachStop() when an attached player
                // attacks, and the doodad block just after it calls StopDoodadControl()
                // and dismounts a rider. Both read `attack`; clearing it here means
                // neither fires. The vanilla animation path is unreachable while
                // attached anyway - StartEmote refuses on IsAttached() and StartAttack
                // on !CanMove() - so nothing of value is being suppressed.
                attack = false;
                return;
            }

            // Body free: `attack` is left alone so vanilla raises the horn and plays
            // the drink animation, whether or not the Horn Cooldown let a sound out.
            // During a cooldown that is the point - the player visibly sounds the horn
            // and the centre-screen message says why nobody heard it.
        }
    }
}
