using BepInEx.Configuration;

namespace AudibleHorn
{
    /// <summary>
    /// The mod's settings. Section and key names are public surface - players edit
    /// <c>BepInEx/config/mushroom.audiblehorn.cfg</c> by hand - so they are spelled
    /// out here verbatim and should not be renamed casually.
    ///
    /// Hearing Range and Horn Cooldown are registered with MushroomSync, so on a
    /// synced client <c>entry.Value</c> already returns the host's value. No call
    /// site needs to know which one it got, and the local .cfg is never rewritten.
    /// Horn Volume is excluded: it is a personal loudness multiplier and is never
    /// shared with other players.
    /// </summary>
    internal sealed class ModConfig
    {
        /// <summary>
        /// Metres from the Blower beyond which a Horn Call is silent. Straight-line;
        /// terrain does not block it.
        /// </summary>
        internal ConfigEntry<float> HearingRange { get; }

        /// <summary>
        /// Seconds a player must wait between their own Horn Calls.
        /// </summary>
        internal ConfigEntry<float> Cooldown { get; }

        /// <summary>
        /// Personal loudness multiplier for Horn Calls, on top of the game's effects
        /// volume. Never synced.
        /// </summary>
        internal ConfigEntry<float> HornVolume { get; }

        internal ModConfig(ConfigFile config)
        {
            HearingRange = config.Bind(
                "Horn",
                "HearingRange",
                100f,
                new ConfigDescription(
                    "Metres from the Blower beyond which a Horn Call is silent. Straight-line; terrain does not block it. Set by the host.",
                    new AcceptableValueRange<float>(10f, 2000f)));

            Cooldown = config.Bind(
                "Horn",
                "Cooldown",
                10f,
                new ConfigDescription(
                    "Seconds a player must wait between their own Horn Calls. Set by the host.",
                    new AcceptableValueRange<float>(0f, 120f)));

            HornVolume = config.Bind(
                "Audio",
                "HornVolume",
                1.0f,
                new ConfigDescription(
                    "Personal loudness multiplier for Horn Calls, on top of the game's effects volume. Never shared with other players.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Plugin.Sync.Register(HearingRange, Cooldown);
            Plugin.Sync.Exclude(HornVolume);
        }
    }
}
