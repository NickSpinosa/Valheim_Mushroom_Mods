using System;
using AudibleHorn.Audio;
using HarmonyLib;
using UnityEngine;

namespace AudibleHorn
{
    /// <summary>
    /// Console commands for testing the horn. They ship with the mod: all of them are
    /// cheat-only, so they need <c>devcommands</c> first and cannot be used to make
    /// noise on a server that has cheats off.
    ///
    /// An agent cannot hear audio, so these commands plus the Info line each Horn Call
    /// logs are the evidence the range, follow and volume behaviour actually works.
    /// </summary>
    internal static class DebugCommands
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            _ = new Terminal.ConsoleCommand(
                "hornsound",
                "play a Horn Call 5 m in front of you, at the configured Hearing Range and Horn Volume",
                HornSound,
                isCheat: true);

            _ = new Terminal.ConsoleCommand(
                "hornsoundfollow",
                "play a Horn Call that rides along with you, to check the sound follows a moving Blower",
                HornSoundFollow,
                isCheat: true);

            _ = new Terminal.ConsoleCommand(
                "horncall",
                "broadcast a real Horn Call from your position, as sounding the Signal Horn will",
                HornCallCommand,
                isCheat: true);

            Plugin.Log.LogInfo("Console commands registered: hornsound, hornsoundfollow, horncall");
        }

        /// <summary>Local playback only. Nothing is sent to anyone else.</summary>
        private static void HornSound(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                args.Context?.AddString("No local player.");
                return;
            }

            Transform transform = player.transform;
            Vector3 pos = transform.position + transform.forward * 5f;

            HornAudio.Play(pos, null, Plugin.Settings.HearingRange.Value, Plugin.Settings.HornVolume.Value);
            args.Context?.AddString(
                "Horn Call at " + pos + " (hearing range " +
                Plugin.Settings.HearingRange.Value.ToString("0.#") + " m, horn volume " +
                Plugin.Settings.HornVolume.Value.ToString("0.00") + ").");
        }

        /// <summary>
        /// The same call, parented to the local player, so walking while it sounds
        /// shows whether the sound rides along. Ticket 03's acceptance asks for this.
        /// </summary>
        private static void HornSoundFollow(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                args.Context?.AddString("No local player.");
                return;
            }

            HornAudio.Play(
                player.transform.position,
                player.transform,
                Plugin.Settings.HearingRange.Value,
                Plugin.Settings.HornVolume.Value);
            args.Context?.AddString("Horn Call following the local player. Walk while it sounds.");
        }

        /// <summary>
        /// The networked path: sends the broadcast every Listener within Hearing Range
        /// answers to, exactly as ticket 05's attack trigger will. No Horn Cooldown is
        /// applied - that lands with the trigger, and being able to sound twice in a
        /// row is useful for testing.
        /// </summary>
        private static void HornCallCommand(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                args.Context?.AddString("No local player.");
                return;
            }

            HornCall.Send(player);
            args.Context?.AddString(
                "Horn Call broadcast from " + player.transform.position + " (hearing range " +
                Plugin.Settings.HearingRange.Value.ToString("0.#") + " m). See the log for what each client did with it.");
        }
    }

    /// <summary>
    /// Registers the commands once the terminal has built its command table.
    ///
    /// Wrapped like the shared game hooks are: InitTerminal is a crowded patch point
    /// and a throw here would abort the postfix chain for every other mod that adds a
    /// command after us.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class TerminalInitTerminalPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            try
            {
                DebugCommands.Register();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to register Audible Horn console commands: " + e);
            }
        }
    }
}
