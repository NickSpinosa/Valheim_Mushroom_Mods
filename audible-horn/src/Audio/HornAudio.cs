using UnityEngine;
using UnityEngine.Audio;

namespace AudibleHorn.Audio
{
    /// <summary>
    /// Plays a Horn Call as a spatialised 3D sound at a world position, optionally
    /// riding along with a transform so a Listener hears a moving boat rather than
    /// its wake.
    ///
    /// This is a bare <see cref="AudioSource"/> on a throwaway GameObject, not a
    /// ZSFX - see docs/DESIGN.md, "Why not ZSFX".
    /// </summary>
    internal static class HornAudio
    {
        /// <summary>
        /// Manifest name of the embedded WAV: &lt;RootNamespace&gt;.&lt;path with dots&gt;.
        /// Moving or renaming Assets/horn.wav changes this.
        /// </summary>
        internal const string ClipResource = "AudibleHorn.Assets.horn.wav";

        /// <summary>
        /// Distance within which a Horn Call is at full volume. Linear rolloff runs
        /// from here out to Hearing Range.
        /// </summary>
        private const float MinDistance = 2f;

        private static AudioMixerGroup _sfxGroup;
        private static bool _sfxGroupSettled;
        private static bool _sfxGroupWarned;
        private static bool _clipWarned;
        private static bool _sawAudioListener;

        /// <summary>
        /// True when this process can actually sound a Horn Call: it has an
        /// AudioListener (so it is a client, not the dedicated server) and the horn
        /// clip decoded.
        ///
        /// The SFX mixer group is deliberately not part of this. A missing group is
        /// specified to degrade to playing outside the game's Effects slider, not to
        /// silence, so gating on it would turn a cosmetic fallback into a mute.
        /// </summary>
        internal static bool IsReady
        {
            get { return HasAudioListener() && WavLoader.Load(ClipResource) != null; }
        }

        /// <summary>
        /// Plays a Horn Call at <paramref name="pos"/>. If <paramref name="follow"/> is
        /// non-null the sound rides with it instead and <paramref name="pos"/> is ignored.
        /// </summary>
        /// <param name="hearingRange">Metres at which the call reaches silence.</param>
        /// <param name="volume">Horn Volume, the player's personal multiplier, in [0, 1].</param>
        internal static void Play(Vector3 pos, Transform follow, float hearingRange, float volume)
        {
            // The dedicated server runs this same DLL with no AudioListener. There is
            // nothing to play into and nothing worth warning about, so leave silently -
            // and leave before the clip is touched, so the server never decodes it.
            if (!HasAudioListener())
                return;

            AudioClip clip = WavLoader.Load(ClipResource);
            if (clip == null)
            {
                if (!_clipWarned)
                {
                    _clipWarned = true;
                    Plugin.Log.LogWarning(
                        "No horn clip could be loaded, so Horn Calls will be silent on this client. " +
                        "The error above says why.");
                }
                return;
            }

            GameObject go = new GameObject("SignalHornCall");
            if (follow != null)
            {
                // worldPositionStays: false, then zero the local position - the call
                // comes from the Blower, not from wherever the object happened to be.
                go.transform.SetParent(follow, false);
                go.transform.localPosition = Vector3.zero;
            }
            else
            {
                go.transform.position = pos;
            }

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.spatialBlend = 1f;
            // Linear, not logarithmic: the glossary promises loudness falls off
            // linearly from full at the Blower to silence at Hearing Range, which is
            // what lets a Listener judge rough distance. Logarithmic rolloff never
            // quite reaches silence and would make the range gate audible as a cut-off.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinDistance;
            // Guarded only against a nonsensical range; the config floor is 10 m.
            source.maxDistance = Mathf.Max(hearingRange, MinDistance + 1f);
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.volume = Mathf.Clamp01(volume);
            source.outputAudioMixerGroup = SfxGroup;
            source.Play();

            // The clip is not looped, so the object has no reason to outlive it.
            Object.Destroy(go, clip.length + 0.1f);

            Plugin.Log.LogInfo(
                "Horn Call played at " + (follow != null ? follow.position : pos) +
                (follow != null ? " (following " + follow.name + ")" : string.Empty) +
                ", hearing range " + hearingRange.ToString("0.#") + " m, volume " +
                Mathf.Clamp01(volume).ToString("0.00") + ".");
        }

        /// <summary>
        /// The mixer group vanilla effects play through, found lazily on the first
        /// Horn Call. Null means "play outside the Effects slider", which is a
        /// degraded but audible outcome.
        /// </summary>
        private static AudioMixerGroup SfxGroup
        {
            get
            {
                if (_sfxGroupSettled)
                    return _sfxGroup;

                // AudioMan exposes m_masterMixer, m_ambientMixer and m_guiMixer, but
                // not the SFX group. Every vanilla ZSFX has an AudioSource already
                // routed to it, so borrowing one from a prefab is the way in.
                ZNetScene scene = ZNetScene.instance;
                if (scene == null || scene.m_prefabs == null)
                {
                    // Not settled: the prefab list only exists from ZNetScene.Awake
                    // onwards, so try again on the next Horn Call.
                    WarnNoSfxGroup("ZNetScene is not up yet");
                    return null;
                }

                foreach (GameObject prefab in scene.m_prefabs)
                {
                    if (prefab == null)
                        continue;
                    if (prefab.GetComponent<ZSFX>() == null)
                        continue;

                    AudioSource source = prefab.GetComponent<AudioSource>();
                    if (source == null || source.outputAudioMixerGroup == null)
                        continue;

                    _sfxGroup = source.outputAudioMixerGroup;
                    _sfxGroupSettled = true;
                    Plugin.Log.LogInfo(
                        "SFX mixer group '" + _sfxGroup.name + "' taken from prefab '" + prefab.name +
                        "'. Horn Calls follow the game's Effects slider.");
                    return _sfxGroup;
                }

                // ZNetScene's prefab list does not change after Awake, so a full scan
                // that found nothing will not find anything on a later call either.
                _sfxGroupSettled = true;
                WarnNoSfxGroup("no ZSFX prefab had an AudioSource with a mixer group");
                return null;
            }
        }

        private static void WarnNoSfxGroup(string why)
        {
            if (_sfxGroupWarned)
                return;
            _sfxGroupWarned = true;
            Plugin.Log.LogWarning(
                "Could not find the SFX mixer group (" + why + "). Horn Calls will still play, " +
                "but outside the game's Effects slider.");
        }

        /// <summary>
        /// Whether this process has an AudioListener, i.e. whether it is a client.
        ///
        /// The search is not cheap, but a Horn Call is a rare event and the answer is
        /// latched once it is true: a headless server never grows a listener, and a
        /// client keeps one for the life of the process.
        /// </summary>
        private static bool HasAudioListener()
        {
            if (_sawAudioListener)
                return true;

            // FindAnyObjectByType, not the deprecated FindObjectOfType: any listener
            // will do, and it skips the sort the ordered variants pay for.
            _sawAudioListener = Object.FindAnyObjectByType<AudioListener>() != null;
            return _sawAudioListener;
        }
    }
}
