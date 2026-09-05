using System;
using System.Collections;
using AudibleHorn.Audio;
using UnityEngine;

namespace AudibleHorn
{
    /// <summary>
    /// A Horn Call on the wire: a routed RPC broadcast carrying the Blower's ZDOID
    /// and world position, which every Listener judges against its own local
    /// player's position.
    ///
    /// It deliberately is not a sound attached to the Blower's prefab. Valheim only
    /// loads objects a few zones around each player, so a Listener at the edge of a
    /// large Hearing Range may not have the Blower's <see cref="Player"/> loaded at
    /// all - there would be nothing to attach to. The position travels in the
    /// message instead, and the Blower's transform is used only as an optional
    /// refinement when it happens to be loaded here.
    /// </summary>
    internal static class HornCall
    {
        /// <summary>
        /// Wire name of the broadcast. Hashed by the game, so it must match exactly
        /// on both ends; changing it breaks compatibility with older clients.
        /// </summary>
        internal const string RpcName = "AudibleHorn.HornCall";

        /// <summary>
        /// How long the first Horn Call of the process waits for its own echo before
        /// concluding there is not going to be one. See "Does Everybody include the
        /// sender?" in docs/DESIGN.md.
        /// </summary>
        private const float EchoWindowSeconds = 1f;

        private static bool _registered;
        private static ZRoutedRpc _registeredInstance;

        // --- Self-echo detection state (see docs/DESIGN.md) -------------------

        /// <summary>True once this process has observed whether it hears its own broadcast.</summary>
        private static bool _selfEchoSettled;

        /// <summary>Meaningful only when <see cref="_selfEchoSettled"/> is true.</summary>
        private static bool _selfEchoExists;

        /// <summary>A Horn Call is in flight and we are watching for its echo.</summary>
        private static bool _probeActive;
        private static ZDOID _probeZdoid;
        private static Vector3 _probePos;

        /// <summary>Sends made while the probe was still unresolved. Warned about once.</summary>
        private static int _probeSuppressedSends;

        /// <summary>
        /// How many of our own calls this client has already played by itself and must
        /// therefore drop if the network echo turns up afterwards. Guards the one
        /// ordering the fallback cannot rule out: a slow echo arriving after the
        /// one-second window expired.
        /// </summary>
        private static int _pendingLocalPlays;
        private static ZDOID _pendingLocalPlayZdoid;

        private static bool _noRpcWarned;

        /// <summary>
        /// Hooks the broadcast up to the current <see cref="ZRoutedRpc"/>. Idempotent
        /// per instance: <c>Game.Start</c> runs once per session but the RPC object is
        /// rebuilt with every ZNet, so identity - not a plain bool - is what decides
        /// whether registration is still live.
        /// </summary>
        internal static void Register()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null)
                return;

            if (_registered && ReferenceEquals(_registeredInstance, rpc))
                return;

            rpc.Register<ZDOID, Vector3>(RpcName, Receive);
            _registered = true;
            _registeredInstance = rpc;

            Plugin.Log.LogInfo("Horn Call RPC '" + RpcName + "' registered.");
        }

        /// <summary>
        /// Forgets the session. Called from the <c>ZNet.Shutdown</c> postfix so the
        /// next session registers against its new <see cref="ZRoutedRpc"/> rather than
        /// believing the dead one is still listening.
        ///
        /// The self-echo answer is deliberately kept across a rejoin: it is a property
        /// of the game's own RPC plumbing, identical whether this process joins a
        /// dedicated server or hosts, so re-probing would only re-impose the
        /// one-second wait on the first call of every session.
        /// </summary>
        internal static void Reset()
        {
            _registered = false;
            _registeredInstance = null;

            // In-flight state is session-scoped and must not leak into the next one:
            // the echo we were waiting for is never arriving now.
            _probeActive = false;
            _probeSuppressedSends = 0;
            _pendingLocalPlays = 0;
            _pendingLocalPlayZdoid = ZDOID.None;
        }

        /// <summary>
        /// Broadcasts a Horn Call from <paramref name="blower"/>. Ticket 05 calls this
        /// from the attack trigger; the <c>horncall</c> console command calls it too.
        /// </summary>
        internal static void Send(Player blower)
        {
            if (blower == null)
                return;

            // Defensive: Game.Start has normally done this already, but a mod that
            // aborted that postfix chain would otherwise leave us silently unhooked.
            Register();

            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                if (!_noRpcWarned)
                {
                    _noRpcWarned = true;
                    Plugin.Log.LogWarning(
                        "A Horn Call was made with no ZRoutedRpc, so nothing was sent. " +
                        "This should only be possible outside a game session.");
                }
                return;
            }

            ZDOID zdoid = blower.GetZDOID();
            Vector3 pos = blower.transform.position;

            Plugin.Log.LogInfo(
                "Horn Call sent: blower " + zdoid + " at " + pos + ".");

            if (_selfEchoSettled)
            {
                SendSettled(rpc, zdoid, pos);
                return;
            }

            SendProbing(rpc, zdoid, pos);
        }

        /// <summary>The steady state, once the self-echo question has been answered.</summary>
        private static void SendSettled(ZRoutedRpc rpc, ZDOID zdoid, Vector3 pos)
        {
            // Read once, up front. An echo arriving inside the Invoke below flips
            // _selfEchoExists to true as it suppresses itself, and re-reading the
            // field afterwards would then skip the local play the suppression was
            // counting on - the Blower would hear nothing at all.
            bool playLocally = !_selfEchoExists;

            if (playLocally)
            {
                // Arm the late-echo guard before sending, because if the answer we
                // settled on is somehow wrong the echo can arrive inside the Invoke
                // call below, synchronously.
                _pendingLocalPlays++;
                _pendingLocalPlayZdoid = zdoid;
            }

            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, zdoid, pos);

            if (playLocally)
                PlayOwnCall(zdoid, pos);
        }

        /// <summary>
        /// The first Horn Call of the process, plus any made before it resolved.
        ///
        /// The probe flag is raised <em>before</em> the broadcast: the local dispatch,
        /// if there is one, happens synchronously inside InvokeRoutedRPC, so setting
        /// the flag afterwards would miss the very echo it exists to catch.
        /// </summary>
        private static void SendProbing(ZRoutedRpc rpc, ZDOID zdoid, Vector3 pos)
        {
            if (_probeActive)
            {
                // Only reachable by sounding twice inside one second, which needs
                // Horn Cooldown at 0 or the console command. If the echo turns out
                // not to exist, this second call is heard by everyone but the Blower.
                _probeSuppressedSends++;
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, zdoid, pos);
                return;
            }

            _probeActive = true;
            _probeZdoid = zdoid;
            _probePos = pos;

            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, zdoid, pos);

            if (!_probeActive)
            {
                // Receive already fired during the call above and settled the answer.
                return;
            }

            // No synchronous echo. It may still arrive over the network a frame or
            // two later, so wait a beat before concluding there is none.
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
            {
                // No MonoBehaviour to run the timer on, which should be impossible
                // while BepInEx is alive. Degrade to hearing the call rather than to
                // silence, and settle the question the pessimistic way.
                SettleNoEcho("no coroutine host was available to wait for one");
                return;
            }

            plugin.StartCoroutine(WaitForEcho());
        }

        /// <summary>
        /// Frame-by-frame wait, not <c>WaitForSeconds</c>: the game's time scale is
        /// zero while a menu is up and a scaled wait would never finish there.
        /// </summary>
        private static IEnumerator WaitForEcho()
        {
            float deadline = Time.realtimeSinceStartup + EchoWindowSeconds;
            while (_probeActive && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!_probeActive)
                yield break;

            SettleNoEcho("none arrived within " + EchoWindowSeconds.ToString("0.#") + " s");
        }

        /// <summary>
        /// Records "the sender does not hear its own broadcast" and plays the call
        /// that was waiting on the answer.
        /// </summary>
        private static void SettleNoEcho(string why)
        {
            ZDOID zdoid = _probeZdoid;
            Vector3 pos = _probePos;

            _probeActive = false;
            _selfEchoSettled = true;
            _selfEchoExists = false;

            Plugin.Log.LogInfo(
                "Self-echo detection: a broadcast to ZRoutedRpc.Everybody is NOT delivered to the " +
                "sender's own handler (" + why + "). Horn Calls made here will be played locally. " +
                "Record this under \"Does Everybody include the sender?\" in docs/DESIGN.md.");

            if (_probeSuppressedSends > 0)
            {
                Plugin.Log.LogWarning(
                    _probeSuppressedSends + " Horn Call(s) were sounded while self-echo detection was " +
                    "still running and were heard by everyone except the Blower. This can only happen " +
                    "in the first second of the first call of a session.");
                _probeSuppressedSends = 0;
            }

            _pendingLocalPlays++;
            _pendingLocalPlayZdoid = zdoid;
            PlayOwnCall(zdoid, pos);
        }

        /// <summary>
        /// Plays the Blower's own call on this client, taking the same delivery path a
        /// received call would so the range gate, the follow transform and the log
        /// line are identical. <see cref="ZNet.GetUID"/> is passed as the sender
        /// because that is what the echo would have carried.
        ///
        /// It goes in as <c>fromNetwork: false</c>, which is not a detail: the
        /// late-echo guard is armed before this runs, and a call that went through the
        /// guard would suppress the very play the guard exists to protect.
        /// </summary>
        private static void PlayOwnCall(ZDOID zdoid, Vector3 pos)
        {
            Deliver(ZNet.instance != null ? ZNet.GetUID() : 0L, zdoid, pos, fromNetwork: false);
        }

        /// <summary>
        /// The broadcast handler. Runs on every peer the message reaches, including
        /// the dedicated server, which has no local player and nothing to play.
        /// </summary>
        private static void Receive(long sender, ZDOID blower, Vector3 pos)
        {
            Deliver(sender, blower, pos, fromNetwork: true);
        }

        private static void Deliver(long sender, ZDOID blower, Vector3 pos, bool fromNetwork)
        {
            try
            {
                DeliverCore(sender, blower, pos, fromNetwork);
            }
            catch (Exception e)
            {
                // A throw here would surface inside the game's RPC dispatch, where it
                // is nobody's to catch. A missed horn is better than a broken session.
                Plugin.Log.LogError("Failed to handle a Horn Call from peer " + sender + ": " + e);
            }
        }

        private static void DeliverCore(long sender, ZDOID blower, Vector3 pos, bool fromNetwork)
        {
            string prefix = fromNetwork
                ? "Horn Call received from peer " + sender + ": blower " + blower + " at " + pos + ", "
                : "Horn Call played locally by the Blower: " + blower + " at " + pos + ", ";

            // Echo bookkeeping happens before every early return, and only for
            // messages that actually came off the wire. A dedicated server never
            // sends, so this is inert there, and doing it first means the answer is
            // still observed on a client whose audio failed to load.
            // A local play carries no information about the echo - it *is* the thing
            // the bookkeeping decided to do - so it skips this block entirely.
            if (fromNetwork)
            {
                if (_probeActive && blower == _probeZdoid)
                {
                    _probeActive = false;
                    _probeSuppressedSends = 0;
                    if (!_selfEchoSettled)
                    {
                        _selfEchoSettled = true;
                        _selfEchoExists = true;
                        Plugin.Log.LogInfo(
                            "Self-echo detection: a broadcast to ZRoutedRpc.Everybody IS delivered to the " +
                            "sender's own handler. Horn Calls will not be played locally as well. " +
                            "Record this under \"Does Everybody include the sender?\" in docs/DESIGN.md.");
                    }
                    // Fall through and play it: this echo is the Blower's own call.
                }
                else if (_pendingLocalPlays > 0 && blower == _pendingLocalPlayZdoid)
                {
                    // We already played this one ourselves. Dropping the echo is the
                    // whole point of the counter; without it the Blower would hear a
                    // double horn.
                    _pendingLocalPlays--;

                    if (_selfEchoSettled && !_selfEchoExists)
                    {
                        // The one-second window was too short. Correct the answer so no
                        // later call is played twice, and say so - the doc section is
                        // wrong if this ever appears.
                        _selfEchoExists = true;
                        Plugin.Log.LogWarning(
                            "Self-echo detection was wrong: the sender's own broadcast did come back, " +
                            "just later than " + EchoWindowSeconds.ToString("0.#") + " s. The duplicate " +
                            "was suppressed and local playback is now off for the rest of the session.");
                    }

                    Plugin.Log.LogInfo(prefix + "skipped (this client already played its own call).");
                    return;
                }
            }

            Player local = Player.m_localPlayer;
            if (local == null)
            {
                // The dedicated server runs this DLL and relays the broadcast without
                // needing to do anything with it. Logged because it is the evidence
                // that the relay works at all.
                Plugin.Log.LogInfo(prefix + "skipped (no local player; this is the server relaying it).");
                return;
            }

            if (!HornAudio.IsReady)
            {
                Plugin.Log.LogInfo(prefix + "skipped (horn audio is not ready on this client).");
                return;
            }

            float range = Plugin.Settings.HearingRange.Value;
            float distance = Vector3.Distance(local.transform.position, pos);

            // Straight-line 3D distance, which also keeps dungeons and the surface
            // apart for free: interiors are generated thousands of metres up.
            if (distance > range)
            {
                Plugin.Log.LogInfo(
                    prefix + "distance " + distance.ToString("0.#") + " m, hearing range " +
                    range.ToString("0.#") + " m - skipped (out of range).");
                return;
            }

            Transform follow = ResolveBlowerTransform(blower);

            Plugin.Log.LogInfo(
                prefix + "distance " + distance.ToString("0.#") + " m, hearing range " +
                range.ToString("0.#") + " m - played" +
                (follow != null ? " (following the Blower)." : " (fixed position; Blower not loaded here)."));

            HornAudio.Play(pos, follow, range, Plugin.Settings.HornVolume.Value);
        }

        /// <summary>
        /// The Blower's object on this client, or null when it is out of loaded range,
        /// which is the documented fallback: a far Listener hears a fixed position.
        /// </summary>
        private static Transform ResolveBlowerTransform(ZDOID blower)
        {
            if (blower == ZDOID.None)
                return null;

            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
                return null;

            // Deliberately not `FindInstance(blower)?.transform`. The null-conditional
            // operator uses a plain reference check and bypasses UnityEngine.Object's
            // overloaded ==, so it would treat a destroyed GameObject as alive and hand
            // back a transform that throws on use.
            GameObject go = scene.FindInstance(blower);
            return go != null ? go.transform : null;
        }
    }
}
