using System;
using HarmonyLib;
using UnityEngine;

namespace AudibleHorn
{
    /// <summary>
    /// The Blower's side of a Horn Call: whether the local player is holding a Signal
    /// Horn at all, whether their body is free to play the drink animation, and
    /// whether the Horn Cooldown has expired.
    ///
    /// Everything here is about the local player only. The dedicated server never
    /// calls into it - there is no <see cref="Player.m_localPlayer"/> there and the
    /// one caller, the <c>Player.SetControls</c> prefix, guards on it.
    /// </summary>
    internal static class HornBlower
    {
        /// <summary>
        /// Shown centre-screen when the Horn Cooldown has not expired. A literal, not
        /// a <c>$token</c>: the game's localisation tables are vanilla's and adding to
        /// them is a whole mechanism this mod does not otherwise need.
        /// </summary>
        internal const string CooldownMessage = "The horn still rings.";

        /// <summary>Where a Signal Horn was found on the player.</summary>
        internal enum HornSlot
        {
            /// <summary>Not holding one.</summary>
            None,

            /// <summary>In the hand, drawn: <c>Humanoid.m_rightItem</c>.</summary>
            Visible,

            /// <summary>
            /// Stowed by the game rather than by the player:
            /// <c>Humanoid.m_hiddenRightItem</c>. Sitting at a rudder or a bench and
            /// swimming both put it here, and the horn must still be soundable.
            /// </summary>
            Hidden
        }

        /// <summary>
        /// <see cref="Time.time"/> of the last Horn Call this client made. Deliberately
        /// not a ZDO or network time: the Horn Cooldown is one player's own rate limit,
        /// it is only ever compared against this same clock, and Time.time is monotonic
        /// and needs nothing from the session to be readable.
        /// </summary>
        private static float _lastCall;

        /// <summary>
        /// False until the first Horn Call of the session. Without it the very first
        /// call would be measured against <c>_lastCall == 0</c>, which is a real
        /// instant - the moment the process started - and would silently swallow a
        /// horn sounded during the first few seconds of a run.
        /// </summary>
        private static bool _hasCalled;

        /// <summary>
        /// Both hand-item fields are reached by reflection, not just the hidden one:
        /// <c>m_rightItem</c> is <c>protected</c> on <c>Humanoid</c> and
        /// <c>GetRightItem()</c> is protected as well, so neither is reachable from
        /// another assembly without the publicizer this project deliberately does not
        /// use. Built in a static constructor rather than an initialiser for the reason
        /// <see cref="SignalHornItem"/> records: a throwing initialiser turns one
        /// renamed field into a TypeInitializationException on every later touch of the
        /// class, and the mod then cannot even log why it failed.
        /// </summary>
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItemRef;
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRightItemRef;

        private static bool _warnedNoFieldRefs;

        static HornBlower()
        {
            try
            {
                RightItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
                HiddenRightItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
            }
            catch (Exception)
            {
                RightItemRef = null;
                HiddenRightItemRef = null;
            }
        }

        // --- What the player is holding -------------------------------------

        /// <summary>
        /// True when the local player holds a Signal Horn, visible or hidden.
        /// </summary>
        internal static bool HasHornEquipped(Player p)
        {
            return FindHorn(p) != HornSlot.None;
        }

        /// <summary>
        /// Which of the two right-hand slots holds a Signal Horn, if either.
        ///
        /// Both have to be checked. Attaching to a seat with <c>hideWeapons</c> - the
        /// rudder and every bench - and swimming each move <c>m_rightItem</c> into
        /// <c>m_hiddenRightItem</c>, so a horn that is very much still carried reads as
        /// empty hands if only the visible slot is consulted. There is never one in
        /// each: an <c>ItemType.Tool</c> occupies the right hand alone, and
        /// <c>EquipItem</c> clears the hidden slots when it equips one.
        /// </summary>
        internal static HornSlot FindHorn(Player p)
        {
            if (p == null)
                return HornSlot.None;

            if (RightItemRef == null || HiddenRightItemRef == null)
            {
                if (!_warnedNoFieldRefs)
                {
                    _warnedNoFieldRefs = true;
                    Plugin.Log.LogWarning(
                        "Humanoid.m_rightItem / m_hiddenRightItem were not reachable, so the " +
                        SignalHornItem.DisplayName + " cannot be sounded. The game has probably changed.");
                }
                return HornSlot.None;
            }

            if (SignalHornItem.IsSignalHorn(RightItemRef(p)))
                return HornSlot.Visible;

            if (SignalHornItem.IsSignalHorn(HiddenRightItemRef(p)))
                return HornSlot.Hidden;

            return HornSlot.None;
        }

        // --- What the body is doing -----------------------------------------

        /// <summary>
        /// Whether the player's body is free to play the drink animation, and a short
        /// word for what it is doing instead. The two come back together so the log
        /// line and the decision it describes cannot drift apart.
        ///
        /// <c>IsAttached()</c> covers rudders, benches, beds and chairs; the swimming
        /// test excludes wading, where <c>IsOnGround()</c> is still true and the horn
        /// is still in hand.
        /// </summary>
        internal static bool IsBodyFree(Player p, out string doing)
        {
            if (p == null)
            {
                doing = "no player";
                return false;
            }

            bool attached = p.IsAttached();
            bool riding = p.IsRiding();
            bool swimming = p.IsSwimming() && !p.IsOnGround();

            if (!attached && !riding && !swimming)
            {
                doing = "standing";
                return true;
            }

            // Every reason, not the first one found. A player attached to a boat that
            // is taking on water can read as attached and swimming at once, and which
            // of the two the game believes is exactly what a surprising report needs.
            string what = attached ? "attached" : "";
            if (riding)
                what += what.Length > 0 ? "+riding" : "riding";
            if (swimming)
                what += what.Length > 0 ? "+swimming" : "swimming";

            doing = what;
            return false;
        }

        // --- The cooldown ----------------------------------------------------

        /// <summary>
        /// Seconds still to wait before the next Horn Call, or 0 when one can be made
        /// now. Read it <em>before</em> <see cref="TryBlow"/>: a successful call resets
        /// the clock, so asking afterwards always answers about the wrong attempt.
        /// </summary>
        internal static float RemainingCooldown()
        {
            if (!_hasCalled)
                return 0f;

            float remaining = Plugin.Settings.Cooldown.Value - (Time.time - _lastCall);
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// Attempts a Horn Call. Returns true if one was sent; false means the Horn
        /// Cooldown refused it and the Blower has been told so centre-screen.
        ///
        /// The comparison is strictly <c>&lt;</c> so a Cooldown of 0 never blocks -
        /// <c>Time.time - _lastCall</c> is 0 at its smallest, and <c>0 &lt; 0</c> is
        /// false. Making it <c>&lt;=</c> would turn "no cooldown" into "one call ever
        /// per tick", which is not what the setting says.
        /// </summary>
        internal static bool TryBlow(Player p)
        {
            if (p == null)
                return false;

            if (_hasCalled && Time.time - _lastCall < Plugin.Settings.Cooldown.Value)
            {
                p.Message(MessageHud.MessageType.Center, CooldownMessage);
                return false;
            }

            _hasCalled = true;
            _lastCall = Time.time;

            // HornCall.Send owns the broadcast, the Blower's own playback and the
            // "Horn Call sent" log line. Nothing about the wire belongs here.
            HornCall.Send(p);
            return true;
        }

        /// <summary>
        /// Forgets the cooldown clock. Called from the <c>ZNet.Shutdown</c> postfix so
        /// the first horn of the next session is never refused because of one sounded
        /// in the last one - a player who logs out and back in has waited at least as
        /// long as the loading screen took, and being told "the horn still rings" about
        /// a world they have left would be nonsense.
        /// </summary>
        internal static void Reset()
        {
            _lastCall = 0f;
            _hasCalled = false;
        }
    }
}
