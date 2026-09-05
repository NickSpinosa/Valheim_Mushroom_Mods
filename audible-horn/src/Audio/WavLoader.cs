using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AudibleHorn.Audio
{
    /// <summary>
    /// Turns the horn recording embedded in this DLL into an <see cref="AudioClip"/>.
    ///
    /// The recording is a WAV and the RIFF container is parsed here by hand. That is
    /// not a preference: Unity cannot decode Ogg or MP3 from a byte array without the
    /// UnityWebRequest audio modules this project does not reference, and shipping an
    /// asset bundle would mean a second file to locate at runtime. Only 16-bit PCM is
    /// accepted, mono or stereo, at any sample rate; anything else is refused with a
    /// LogError naming what was found, because a horn that is silently missing is a
    /// bug report nobody can act on.
    ///
    /// Nothing here runs at plugin load. The dedicated server loads the same DLL and
    /// must never touch Unity audio, so decoding happens lazily on the first Horn Call
    /// - which the server never makes.
    /// </summary>
    internal static class WavLoader
    {
        /// <summary>
        /// Decoded clips by resource name. A failed decode is cached as null on
        /// purpose: retrying it on every Horn Call would re-log the same error and
        /// re-read the same bytes, and the answer would not change.
        /// </summary>
        private static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>();

        /// <summary>
        /// Loads the embedded WAV at <paramref name="resourceName"/>, or returns null
        /// if it is missing or not 16-bit PCM. Decoded once per process.
        /// </summary>
        internal static AudioClip Load(string resourceName)
        {
            AudioClip cached;
            if (Cache.TryGetValue(resourceName, out cached))
                return cached;

            AudioClip clip = null;
            try
            {
                clip = Decode(resourceName);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to decode embedded clip '" + resourceName + "': " + e);
            }

            Cache[resourceName] = clip;
            return clip;
        }

        private static AudioClip Decode(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // The manifest name is <RootNamespace>.<path with separators as
                    // dots>, which is easy to get wrong after a file move. Listing what
                    // the assembly actually holds turns a mystery into a one-line fix.
                    Plugin.Log.LogError(
                        "Embedded resource '" + resourceName + "' is not in this assembly. It holds: " +
                        string.Join(", ", assembly.GetManifestResourceNames()));
                    return null;
                }

                byte[] bytes = ReadAll(stream);
                return Build(resourceName, bytes);
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            byte[] bytes = new byte[stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    break;
                offset += read;
            }
            return bytes;
        }

        private static AudioClip Build(string resourceName, byte[] bytes)
        {
            if (bytes.Length < 12 || !IsChunk(bytes, 0, "RIFF") || !IsChunk(bytes, 8, "WAVE"))
            {
                Plugin.Log.LogError("Embedded clip '" + resourceName + "' is not a RIFF/WAVE file (" +
                                    bytes.Length + " bytes).");
                return null;
            }

            int formatTag = -1;
            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataLength = 0;

            // Walk the chunk list by id and size. `data` is not required to follow
            // `fmt ` - editors routinely write LIST/INFO or `fact` in between, and the
            // placeholder from tools/make-placeholder-horn.ps1 deliberately does, so
            // this loop is exercised from day one instead of rotting until the real
            // recording lands.
            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                long size = ReadUInt32(bytes, position + 4);
                int body = position + 8;

                if (body > bytes.Length)
                    break;

                if (IsChunk(bytes, position, "fmt ") && size >= 16 && body + 16 <= bytes.Length)
                {
                    formatTag = ReadUInt16(bytes, body);
                    channels = ReadUInt16(bytes, body + 2);
                    sampleRate = (int)ReadUInt32(bytes, body + 4);
                    bitsPerSample = ReadUInt16(bytes, body + 14);
                }
                else if (IsChunk(bytes, position, "data"))
                {
                    dataOffset = body;
                    dataLength = (int)Math.Min(size, bytes.Length - body);
                }

                // Chunks are word-aligned: an odd size is followed by a pad byte that
                // the size field does not count. Computed as a long and bounds-checked
                // so a corrupt size field cannot overflow the cursor back on itself and
                // spin here forever.
                long next = body + size + (size & 1L);
                if (next <= position || next > bytes.Length)
                    break;
                position = (int)next;
            }

            // WAVE_FORMAT_PCM is 1. WAVE_FORMAT_EXTENSIBLE (0xFFFE) can also carry
            // plain 16-bit PCM, but reading its sub-format is more container parsing
            // than a horn is worth; the asset is ours to produce.
            if (formatTag != 1 || bitsPerSample != 16 || (channels != 1 && channels != 2) || sampleRate <= 0)
            {
                Plugin.Log.LogError(
                    "Embedded clip '" + resourceName + "' must be 16-bit PCM, mono or stereo. Found: " +
                    "format tag " + formatTag + ", " + bitsPerSample + "-bit, " + channels +
                    " channel(s), " + sampleRate + " Hz.");
                return null;
            }

            if (dataOffset < 0 || dataLength <= 0)
            {
                Plugin.Log.LogError("Embedded clip '" + resourceName + "' has no usable 'data' chunk.");
                return null;
            }

            int frames = dataLength / 2 / channels;
            if (frames <= 0)
            {
                Plugin.Log.LogError("Embedded clip '" + resourceName + "' holds no audio frames.");
                return null;
            }

            float[] samples = new float[frames * channels];
            for (int i = 0; i < samples.Length; i++)
            {
                int at = dataOffset + i * 2;
                short value = (short)(bytes[at] | (bytes[at + 1] << 8));
                // 32768 rather than 32767: int16 is asymmetric, and dividing by the
                // magnitude of the most negative sample is what keeps the result inside
                // [-1, 1] without a clamp.
                samples[i] = value / 32768f;
            }

            AudioClip clip = AudioClip.Create("SignalHorn", frames, channels, sampleRate, false);
            if (!SetData(clip, samples))
                return null;

            Plugin.Log.LogInfo(
                "Loaded horn clip from '" + resourceName + "': " + frames + " frames, " + channels +
                " channel(s), " + sampleRate + " Hz, " + clip.length.ToString("0.00") + " s.");

            return clip;
        }

        /// <summary>
        /// Fills <paramref name="clip"/> from <paramref name="samples"/>.
        ///
        /// Called by reflection, which looks gratuitous and is not.
        /// <c>AudioClip.SetData</c> is overloaded on <c>float[]</c> and on
        /// <c>ReadOnlySpan&lt;float&gt;</c>, and binding the call in C# makes the
        /// compiler resolve <c>ReadOnlySpan&lt;T&gt;</c>. net472 does not define it;
        /// the game's netstandard 2.1 (referenced to get past CS1705 on
        /// UnityEngine.AudioModule - see the csproj) only forwards it to a Mono
        /// mscorlib this project does not reference. So the call fails to compile with
        /// CS0518 even though the array overload is an exact match. Picking that
        /// overload by signature sidesteps the whole thing, once per process.
        /// </summary>
        private static bool SetData(AudioClip clip, float[] samples)
        {
            MethodInfo setData = typeof(AudioClip).GetMethod(
                "SetData", new[] { typeof(float[]), typeof(int) });

            if (setData == null)
            {
                Plugin.Log.LogError(
                    "AudioClip.SetData(float[], int) is missing from this Unity version; the horn " +
                    "cannot be built. The game has probably updated.");
                return false;
            }

            setData.Invoke(clip, new object[] { samples, 0 });
            return true;
        }

        /// <summary>
        /// Whether the four bytes at <paramref name="offset"/> are the ASCII chunk id
        /// <paramref name="id"/>. Compared byte by byte because RIFF ids are ASCII by
        /// definition and this allocates nothing.
        /// </summary>
        private static bool IsChunk(byte[] bytes, int offset, string id)
        {
            return bytes[offset] == (byte)id[0]
                   && bytes[offset + 1] == (byte)id[1]
                   && bytes[offset + 2] == (byte)id[2]
                   && bytes[offset + 3] == (byte)id[3];
        }

        private static long ReadUInt32(byte[] bytes, int offset)
        {
            return bytes[offset]
                   | ((long)bytes[offset + 1] << 8)
                   | ((long)bytes[offset + 2] << 16)
                   | ((long)bytes[offset + 3] << 24);
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8);
        }
    }
}
