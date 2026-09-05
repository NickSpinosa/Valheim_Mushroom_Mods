using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HornOfCalling
{
    /// <summary>
    /// The horn's blast: decoding the shipped audio into an AudioClip, and wiring it
    /// into the item so the game plays it when the horn is used.
    ///
    /// Nothing here plays the sound directly. The clip is hung off the item's
    /// <c>m_startEffect</c>, which Attack fires once when an attack actually begins -
    /// after the stamina and animation checks, so it cannot sound on a swing the game
    /// refused. Going through the game's own EffectList also buys 3D positioning, the
    /// SFX mixer group (so the player's volume slider applies) and cleanup, none of
    /// which a hand-rolled AudioSource would get right.
    /// </summary>
    internal static class HornSound
    {
        /// <summary>Set as LogicalName in the csproj, so it does not track the folder layout.</summary>
        private const string ClipResourceName = "HornOfCalling.viking.wav";

        private const string EffectPrefabName = "sfx_hornofcalling";

        /// <summary>
        /// Distance in metres the falloff curve is defined over.
        ///
        /// 64 m is not an audio decision - it is the radius a ZDO is guaranteed to reach.
        /// The blast is a networked object, and ZDOMan only sends non-distant ZDOs to
        /// peers whose active area covers them: ZoneSystem's `m_activeArea = 1` over
        /// 64 m zones, so a peer's own zone plus one ring. Past that the other player
        /// never receives the object at all and no volume setting can help.
        /// </summary>
        private const float MaxDistance = 64f;

        /// <summary>
        /// The falloff, as (metres, volume) points on a plateau curve: full volume out to
        /// 15 m, then 18 points off every 10 m until 28%, which holds from 45 m to the
        /// edge of the network range.
        ///
        /// The step is 18 points rather than a rounder 10 so the descent spans the
        /// audible range in whole tiers. At 10 points a tier the horn is still at 50%
        /// where the network cuts it off at <see cref="MaxDistance"/>, because nine
        /// tiers are needed to fall from 100% to a floor and nine of them reach 105 m.
        ///
        /// The doubled points half a metre past each boundary are what make the steps
        /// steps: with flat tangents on every key, two keys of equal value hold a level
        /// exactly, and the pair astride a boundary drops between them over half a metre.
        /// Deleting the second of each pair would turn the plateaus into ramps.
        /// </summary>
        private static readonly (float Metres, float Volume)[] Falloff =
        {
            (0f, 1.00f),
            (15f, 1.00f),
            (15.5f, 0.82f),
            (25f, 0.82f),
            (25.5f, 0.64f),
            (35f, 0.64f),
            (35.5f, 0.46f),
            (45f, 0.46f),
            (45.5f, 0.28f),
            (MaxDistance, 0.28f),
        };

        private static AudioClip _clip;

        /// <summary>The SFX prefab, once built. Registered with ZNetScene alongside the
        /// item: it is cloned from a vanilla effect that carries a ZNetView, so it gets
        /// a ZDO when spawned and other clients need to be able to resolve its hash.</summary>
        internal static GameObject EffectPrefab { get; private set; }

        /// <summary>
        /// Replaces the item's attack-start effects with the horn blast.
        ///
        /// Replaces rather than appends: the effects inherited from the clone source are
        /// a mead splash and a burp, neither of which belongs on a horn.
        /// </summary>
        internal static bool Attach(ItemDrop.ItemData.SharedData shared, Transform container)
        {
            if (shared == null) return false;

            if (EffectPrefab == null)
            {
                AudioClip clip = LoadClip();
                if (clip == null) return false;

                GameObject template = FindSfxTemplate(shared);
                if (template == null)
                {
                    Plugin.Log.LogError(
                        "No ZSFX effect on the clone source to build the horn blast from; the horn will be silent.");
                    return false;
                }

                EffectPrefab = UnityEngine.Object.Instantiate(template, container);
                EffectPrefab.name = EffectPrefabName;

                ZSFX sfx = EffectPrefab.GetComponent<ZSFX>();
                sfx.m_audioClips = new[] { clip };
                sfx.m_playOnAwake = true;
                // The template is the mead burp, which is tuned to sound like one: it
                // waits four to five seconds before playing, drops the pitch by a random
                // amount and plays at 40% volume. A horn answers immediately and as-recorded.
                sfx.m_minDelay = 0f;
                sfx.m_maxDelay = 0f;
                sfx.m_minPitch = 1f;
                sfx.m_maxPitch = 1f;
                sfx.m_minVol = 1f;
                sfx.m_maxVol = 1f;
                // Inherited as "$caption_burp". Cleared rather than replaced - the mod
                // ships no translation table, so any token here would display raw.
                sfx.m_closedCaptionToken = string.Empty;
                sfx.m_secondaryCaptionToken = string.Empty;
                // ZSFX groups concurrent sources by this hash; left alone, horns would
                // count against burps and cut each other off.
                sfx.m_hash = EffectPrefabName.GetStableHashCode();

                ShapeFalloff(EffectPrefab.GetComponent<AudioSource>());

                Plugin.Log.LogInfo(
                    "Built the horn blast from " + template.name + " (" +
                    clip.length.ToString("0.0") + "s, " + clip.frequency + " Hz), audible to " +
                    MaxDistance.ToString("0") + " m.");
            }

            // Field-for-field what the vanilla entries carry, so the effect is placed at
            // the attack origin the same way the sound it replaces was.
            shared.m_startEffect.m_effectPrefabs = new[]
            {
                new EffectList.EffectData
                {
                    m_prefab = EffectPrefab,
                    m_enabled = true,
                    m_variant = -1,
                    m_attach = true,
                    m_inheritParentRotation = true,
                },
            };
            return true;
        }

        /// <summary>
        /// Replaces the inherited falloff with the horn's own.
        ///
        /// The template is a burp, and carries a burp's reach: full volume to 2.5 m, 29%
        /// at 9.8 m, silent at 25 m.
        ///
        /// Unity normalises a custom rolloff curve over 0..maxDistance, so a point's
        /// distance in metres becomes its key time divided by <see cref="MaxDistance"/>.
        /// (ZSFX.GetVolumeModifierByDistance normalises over min..max instead, which
        /// disagrees - but that is only read for looping and concurrency decisions, and
        /// the blast is a one-shot with m_maxConcurrentSources of 0, so it never runs.)
        /// </summary>
        private static void ShapeFalloff(AudioSource source)
        {
            if (source == null)
            {
                Plugin.Log.LogWarning("The horn blast has no AudioSource; its range is whatever it was cloned with.");
                return;
            }

            source.rolloffMode = AudioRolloffMode.Custom;
            source.maxDistance = MaxDistance;

            var keys = new Keyframe[Falloff.Length];
            for (int i = 0; i < Falloff.Length; i++)
            {
                // Flat tangents: without them Unity smooths through the keys and the
                // plateaus bow.
                keys[i] = new Keyframe(Falloff[i].Metres / MaxDistance, Falloff[i].Volume, 0f, 0f);
            }
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(keys));
        }

        /// <summary>
        /// Picks a vanilla sound effect off the item to clone. Cloning rather than
        /// building a GameObject by hand is what gets the AudioSource already wired to
        /// the SFX mixer group and to Valheim's 3D falloff curve; neither is reachable
        /// from a plugin, and a bare AudioSource would ignore the player's volume slider.
        /// </summary>
        private static GameObject FindSfxTemplate(ItemDrop.ItemData.SharedData shared)
        {
            EffectList.EffectData[] effects = shared.m_startEffect?.m_effectPrefabs;
            if (effects == null) return null;

            foreach (EffectList.EffectData effect in effects)
            {
                if (effect?.m_prefab != null && effect.m_prefab.GetComponent<ZSFX>() != null)
                {
                    return effect.m_prefab;
                }
            }
            return null;
        }

        // --- Audio ---------------------------------------------------------------

        private static AudioClip LoadClip()
        {
            if (_clip != null) return _clip;

            byte[] wav = ReadResource();
            if (wav == null) return null;

            try
            {
                _clip = Decode(wav, "hornofcalling_blast");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not decode " + ClipResourceName + ": " + e);
            }
            return _clip;
        }

        private static byte[] ReadResource()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ClipResourceName))
            {
                if (stream == null)
                {
                    Plugin.Log.LogError(
                        "Embedded resource " + ClipResourceName + " is missing. Found: " +
                        string.Join(", ", assembly.GetManifestResourceNames()));
                    return null;
                }

                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return bytes;
            }
        }

        /// <summary>
        /// Turns a 16-bit PCM WAV into an AudioClip.
        ///
        /// Decoded here rather than handed to UnityWebRequestMultimedia because that
        /// route needs a file on disk, a coroutine and a format Unity is willing to
        /// decode at runtime. Uncompressed PCM costs about 430 KB in the assembly and
        /// removes all three problems: the clip exists synchronously, on the first call,
        /// with no I/O.
        /// </summary>
        private static AudioClip Decode(byte[] wav, string clipName)
        {
            if (wav.Length < 12 || Tag(wav, 0) != "RIFF" || Tag(wav, 8) != "WAVE")
            {
                Plugin.Log.LogError("Audio resource is not a RIFF/WAVE file.");
                return null;
            }

            int format = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
            int dataAt = -1, dataLength = 0;

            // Walked rather than read at fixed offsets: the chunks between "fmt " and
            // "data" are optional, and encoders slip metadata in there.
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string id = Tag(wav, pos);
                int size = BitConverter.ToInt32(wav, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > wav.Length) size = wav.Length - body;

                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToInt16(wav, body);
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bitsPerSample = BitConverter.ToInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    dataAt = body;
                    dataLength = size;
                }

                pos = body + size + (size & 1); // chunks are padded to an even length
            }

            if (dataAt < 0 || channels <= 0 || sampleRate <= 0)
            {
                Plugin.Log.LogError("Audio resource has no usable fmt/data chunks.");
                return null;
            }
            if (format != 1 || bitsPerSample != 16)
            {
                Plugin.Log.LogError(
                    "Audio resource must be 16-bit PCM; got format " + format +
                    " at " + bitsPerSample + " bits.");
                return null;
            }

            int sampleCount = dataLength / 2;
            int framesPerChannel = sampleCount / channels;
            if (framesPerChannel <= 0)
            {
                Plugin.Log.LogError("Audio resource holds no samples.");
                return null;
            }

            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int at = dataAt + i * 2;
                samples[i] = (short)(wav[at] | (wav[at + 1] << 8)) / 32768f;
            }

            AudioClip clip = AudioClip.Create(clipName, framesPerChannel, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static string Tag(byte[] bytes, int at)
        {
            return Encoding.ASCII.GetString(bytes, at, 4);
        }
    }
}
