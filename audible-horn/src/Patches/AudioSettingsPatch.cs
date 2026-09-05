using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Audio tab is Valheim.SettingsGui.AudioSettings, not the global AudioSettings
// the ticket names. UnityEngine.AudioSettings is a real, unrelated type, so an
// unqualified `AudioSettings` here would silently bind to Unity's and the patch
// would never fire. Alias it once and never write the bare name.
using VanillaAudioSettings = Valheim.SettingsGui.AudioSettings;

namespace AudibleHorn.Patches
{
    /// <summary>
    /// Adds a "Signal Horn" row to Settings -> Audio, under Music, driving
    /// <see cref="ModConfig.HornVolume"/>.
    ///
    /// The row is a clone of the vanilla music row rather than a hand-built widget,
    /// so it inherits the panel's fonts, colours, widths and slider art for free and
    /// keeps matching them when the game restyles the panel.
    ///
    /// The exact shape of that row is only knowable at runtime and this was written
    /// without the ability to launch the game, so every step is discovered from the
    /// two fields the game does expose (<c>m_musicVolumeSlider</c> and
    /// <c>m_musicVolumeText</c>) instead of being hard-coded to a path, and the whole
    /// hierarchy it walked is logged once at Info on the first Initialize. See
    /// docs/DESIGN.md, "Audio settings row hierarchy".
    ///
    /// The dedicated server never instantiates this MonoBehaviour, so the patches
    /// simply never fire there. Nothing in this file touches a UI type from a static
    /// initialiser that runs at load.
    /// </summary>
    [HarmonyPatch(typeof(VanillaAudioSettings))]
    internal static class AudioSettingsPatch
    {
        /// <summary>Name given to the cloned row, and the fallback way to find it again.</summary>
        internal const string RowName = "SignalHornVolumeRow";

        private const string LabelText = "Signal Horn";

        /// <summary>
        /// Per-panel state. Keyed weakly on the AudioSettings instance so a destroyed
        /// settings panel takes its entry with it; there is normally exactly one.
        /// </summary>
        private sealed class RowState
        {
            internal GameObject Row;
            internal Slider Slider;
            internal TMP_Text ValueText;

            /// <summary>What Back reverts to, mirroring the vanilla m_old* fields.</summary>
            internal float OldHornVolume;
        }

        private static readonly ConditionalWeakTable<VanillaAudioSettings, RowState> States =
            new ConditionalWeakTable<VanillaAudioSettings, RowState>();

        // AccessTools field refs, built on first use. Creating them costs nothing on a
        // headless server because nothing calls into this class there.
        private static AccessTools.FieldRef<VanillaAudioSettings, Slider> _musicSlider;
        private static AccessTools.FieldRef<VanillaAudioSettings, TMP_Text> _musicText;
        private static AccessTools.FieldRef<VanillaAudioSettings, Slider> _sfxSlider;
        private static AccessTools.FieldRef<VanillaAudioSettings, TMP_Text> _sfxText;
        private static AccessTools.FieldRef<VanillaAudioSettings, Slider> _masterSlider;
        private static AccessTools.FieldRef<VanillaAudioSettings, Toggle> _continousMusic;
        private static bool _fieldRefsReady;

        private static bool _loggedHierarchy;

        private static void EnsureFieldRefs()
        {
            if (_fieldRefsReady)
                return;

            _musicSlider = AccessTools.FieldRefAccess<VanillaAudioSettings, Slider>("m_musicVolumeSlider");
            _musicText = AccessTools.FieldRefAccess<VanillaAudioSettings, TMP_Text>("m_musicVolumeText");
            _sfxSlider = AccessTools.FieldRefAccess<VanillaAudioSettings, Slider>("m_sfxVolumeSlider");
            _sfxText = AccessTools.FieldRefAccess<VanillaAudioSettings, TMP_Text>("m_sfxVolumeText");
            _masterSlider = AccessTools.FieldRefAccess<VanillaAudioSettings, Slider>("m_volumeSlider");
            _continousMusic = AccessTools.FieldRefAccess<VanillaAudioSettings, Toggle>("m_continousMusic");
            _fieldRefsReady = true;
        }

        // ---------------------------------------------------------------- patches

        /// <summary>
        /// Vanilla Initialize loads the sliders from PlatformPrefs and stashes the
        /// revert values. We do the same for Horn Volume, building the row the first
        /// time and only refreshing it afterwards.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(VanillaAudioSettings.Initialize))]
        internal static void InitializePostfix(VanillaAudioSettings __instance)
        {
            try
            {
                EnsureFieldRefs();

                RowState state = GetOrBuildRow(__instance);
                if (state == null)
                    return;

                state.OldHornVolume = Plugin.Settings.HornVolume.Value;
                Refresh(state);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("AudioSettings.Initialize: building the Signal Horn row failed: " + e);
            }
        }

        /// <summary>
        /// Vanilla OnTabOpen only wires gamepad navigation between the last control and
        /// the Back/OK buttons, so this is where our row has to insert itself into that
        /// chain - Initialize runs before the buttons are known. We also re-read the
        /// config here in case it changed while the game ran.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(VanillaAudioSettings.OnTabOpen))]
        internal static void OnTabOpenPostfix(VanillaAudioSettings __instance)
        {
            try
            {
                EnsureFieldRefs();

                RowState state = GetOrBuildRow(__instance);
                if (state == null)
                    return;

                Refresh(state);
                LinkNavigation(__instance, state);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("AudioSettings.OnTabOpen: refreshing the Signal Horn row failed: " + e);
            }
        }

        /// <summary>
        /// OK commits. The value is already in the config file - our change listener
        /// writes through - so all that is left is to move the revert point, or a Back
        /// later in the same panel would undo an OK the player already confirmed.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(VanillaAudioSettings.OnOkAsync))]
        internal static void OnOkAsyncPostfix(VanillaAudioSettings __instance)
        {
            try
            {
                RowState state;
                if (States.TryGetValue(__instance, out state) && state != null)
                    state.OldHornVolume = Plugin.Settings.HornVolume.Value;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("AudioSettings.OnOkAsync: committing Horn Volume failed: " + e);
            }
        }

        /// <summary>Back restores the value Initialize (or the last OK) saw.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(VanillaAudioSettings.OnBack))]
        internal static void OnBackPostfix(VanillaAudioSettings __instance)
        {
            try
            {
                RowState state;
                if (!States.TryGetValue(__instance, out state) || state == null)
                    return;

                SetHornVolume(state.OldHornVolume);
                Refresh(state);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("AudioSettings.OnBack: reverting Horn Volume failed: " + e);
            }
        }

        // ------------------------------------------------------------ row building

        private static RowState GetOrBuildRow(VanillaAudioSettings settings)
        {
            RowState state;
            if (States.TryGetValue(settings, out state) && state != null && state.Row != null && state.Slider != null)
                return state;

            Slider musicSlider = _musicSlider(settings);
            TMP_Text musicText = _musicText(settings);
            if (musicSlider == null || musicText == null)
            {
                Plugin.Log.LogWarning(
                    "Audio settings has no music slider or music value text; skipping the Signal Horn row.");
                return null;
            }

            Transform musicRow = FindRow(musicSlider.transform, musicText.transform);
            if (musicRow == null)
            {
                Plugin.Log.LogWarning(
                    "Could not find a common ancestor of the music slider and its value text; skipping the Signal Horn row.");
                return null;
            }

            LogHierarchyOnce(settings, musicSlider, musicText, musicRow);

            // The walk stops at the first ancestor holding both, but if the panel puts
            // every control in one flat container that ancestor is the whole list.
            // Cloning it would duplicate every audio control, so refuse instead.
            if (RowIsTooBig(settings, musicRow))
            {
                Plugin.Log.LogError(
                    "The nearest ancestor of the music slider and its value text ('" + Path(musicRow) +
                    "') also contains other audio controls, so it is the whole list rather than one row. " +
                    "Not cloning it; the Signal Horn slider is unavailable. See docs/DESIGN.md, 'Audio settings row hierarchy'.");
                return null;
            }

            // A panel rebuilt without our state (or state collected) can still have our
            // row on it. Reuse it rather than stacking a second one.
            Transform existing = musicRow.parent == null ? null : musicRow.parent.Find(RowName);
            GameObject rowObject;
            bool cloned;
            if (existing != null)
            {
                rowObject = existing.gameObject;
                cloned = false;
            }
            else
            {
                rowObject = UnityEngine.Object.Instantiate(musicRow.gameObject, musicRow.parent, false);
                rowObject.name = RowName;
                CopyRectTransform(musicRow as RectTransform, rowObject.transform as RectTransform);
                rowObject.transform.SetSiblingIndex(musicRow.GetSiblingIndex() + 1);
                PlaceRow(settings, musicRow, rowObject.transform);

                // Localize re-runs Localization.Localize over its subtree on Start and
                // on every language change. Our label is a literal with no $token so it
                // would survive, but removing the components makes that a fact rather
                // than a bet - and nothing else in this row needs localizing.
                foreach (Localize localize in rowObject.GetComponentsInChildren<Localize>(true))
                    UnityEngine.Object.Destroy(localize);

                cloned = true;
            }

            state = new RowState { Row = rowObject };

            // Resolve the clone's parts by the child-index path of the originals inside
            // the source row. Instantiate preserves child order exactly, so this is
            // unambiguous where name matching is not - two rows can both hold a
            // "Text (TMP)".
            Transform cloneSlider = ResolveLike(musicRow, rowObject.transform, musicSlider.transform, "slider");
            state.Slider = cloneSlider == null ? null : cloneSlider.GetComponent<Slider>();
            if (state.Slider == null)
                state.Slider = rowObject.GetComponentInChildren<Slider>(true);

            Transform cloneValue = ResolveLike(musicRow, rowObject.transform, musicText.transform, "value text");
            state.ValueText = cloneValue == null ? null : cloneValue.GetComponent<TMP_Text>();

            if (state.Slider == null)
            {
                Plugin.Log.LogError("The cloned Signal Horn row has no Slider; destroying it.");
                if (cloned)
                    UnityEngine.Object.Destroy(rowObject);
                return null;
            }

            SetLabel(musicRow, rowObject.transform, musicText);

            // RemoveAllListeners only clears listeners added from code. The prefab wires
            // OnAudioChanged as a *persistent* listener, which survives that call, so
            // the whole event has to be replaced or our slider would keep driving the
            // game's music volume.
            state.Slider.onValueChanged = new Slider.SliderEvent();
            state.Slider.minValue = 0f;
            state.Slider.maxValue = 1f;
            state.Slider.wholeNumbers = false;
            state.Slider.value = Plugin.Settings.HornVolume.Value;

            RowState captured = state;
            state.Slider.onValueChanged.AddListener(v => OnSliderChanged(captured, v));

            States.Remove(settings);
            States.Add(settings, state);

            if (cloned)
            {
                Plugin.Log.LogInfo(
                    "Added the Signal Horn volume row to the audio settings at '" + Path(rowObject.transform) +
                    "' (sibling index " + rowObject.transform.GetSiblingIndex() + ").");
            }

            return state;
        }

        /// <summary>
        /// The row is the nearest ancestor-or-self of the slider that also contains the
        /// value text. <c>IsChildOf</c> is true for the transform itself, so a label
        /// parented under the slider is handled by the same test.
        /// </summary>
        private static Transform FindRow(Transform slider, Transform text)
        {
            for (Transform t = slider; t != null; t = t.parent)
            {
                if (text.IsChildOf(t))
                    return t;
            }

            return null;
        }

        private static bool RowIsTooBig(VanillaAudioSettings settings, Transform row)
        {
            return Contains(row, _sfxSlider(settings))
                || Contains(row, _sfxText(settings))
                || Contains(row, _masterSlider(settings))
                || Contains(row, _continousMusic(settings));
        }

        private static bool Contains(Transform row, Component other)
        {
            return other != null && other.transform.IsChildOf(row);
        }

        /// <summary>
        /// If the rows sit in a layout group the sibling index is the whole story. If
        /// they are absolutely positioned, step the clone down by the same gap that
        /// separates the music row from the SFX row.
        /// </summary>
        private static void PlaceRow(VanillaAudioSettings settings, Transform musicRow, Transform clone)
        {
            Transform parent = musicRow.parent;
            if (parent != null && parent.GetComponent<LayoutGroup>() != null)
            {
                Plugin.Log.LogInfo(
                    "Audio settings rows are laid out by " + parent.GetComponent<LayoutGroup>().GetType().Name +
                    " on '" + parent.name + "'; sibling order alone places the Signal Horn row.");
                return;
            }

            RectTransform musicRect = musicRow as RectTransform;
            RectTransform cloneRect = clone as RectTransform;
            Slider sfxSlider = _sfxSlider(settings);
            TMP_Text sfxText = _sfxText(settings);
            Transform sfxRow = sfxSlider == null || sfxText == null
                ? null
                : FindRow(sfxSlider.transform, sfxText.transform);
            RectTransform sfxRect = sfxRow as RectTransform;

            if (musicRect == null || cloneRect == null || sfxRect == null || sfxRect.parent != musicRect.parent)
            {
                Plugin.Log.LogWarning(
                    "Audio settings rows are not in a LayoutGroup and the SFX row could not be measured, " +
                    "so the Signal Horn row keeps the music row's position and will overlap it. " +
                    "Report the hierarchy logged above.");
                return;
            }

            Vector2 step = musicRect.anchoredPosition - sfxRect.anchoredPosition;
            if (step.sqrMagnitude < 0.0001f)
            {
                Plugin.Log.LogWarning(
                    "Audio settings rows are not in a LayoutGroup and the SFX and music rows share an " +
                    "anchored position, so no row spacing could be derived. The Signal Horn row will overlap.");
                return;
            }

            cloneRect.anchoredPosition = musicRect.anchoredPosition + step;
            Plugin.Log.LogInfo(
                "Audio settings rows are absolutely positioned; offset the Signal Horn row by " + step +
                " to " + cloneRect.anchoredPosition + ". Controls below the music row were not moved - " +
                "check the panel for overlap.");
        }

        /// <summary>
        /// The label is whichever TMP_Text in the row is not the value text. Matched in
        /// the clone by the original's child-index path, with its name checked, so an
        /// unexpected hierarchy shows up in the log rather than silently relabelling
        /// the wrong object.
        /// </summary>
        private static void SetLabel(Transform sourceRow, Transform clone, TMP_Text valueText)
        {
            Transform label = null;
            foreach (TMP_Text candidate in sourceRow.GetComponentsInChildren<TMP_Text>(true))
            {
                if (candidate == null || ReferenceEquals(candidate, valueText))
                    continue;

                label = candidate.transform;
                break;
            }

            if (label == null)
            {
                Plugin.Log.LogWarning(
                    "The music row has no TMP_Text besides its value text, so the Signal Horn row is unlabelled.");
                return;
            }

            Transform cloneLabel = ResolveLike(sourceRow, clone, label, "label");
            TMP_Text text = cloneLabel == null ? null : cloneLabel.GetComponent<TMP_Text>();
            if (text == null)
            {
                Plugin.Log.LogWarning("Could not find the label TMP_Text in the cloned row; it stays labelled 'Music'.");
                return;
            }

            text.text = LabelText;
        }

        /// <summary>
        /// Finds, in <paramref name="clone"/>, the object at the same child-index path
        /// that <paramref name="original"/> occupies in <paramref name="sourceRow"/>.
        /// </summary>
        private static Transform ResolveLike(Transform sourceRow, Transform clone, Transform original, string what)
        {
            List<int> path;
            if (!TryIndexPath(sourceRow, original, out path))
            {
                Plugin.Log.LogWarning("The music row's " + what + " is not inside the row; falling back to a name search.");
                return FindByName(clone, original.name);
            }

            Transform found = clone;
            foreach (int index in path)
            {
                if (found == null || index < 0 || index >= found.childCount)
                {
                    found = null;
                    break;
                }

                found = found.GetChild(index);
            }

            if (found == null)
                return FindByName(clone, original.name);

            if (found.name != original.name && found.name != original.name + "(Clone)")
            {
                Plugin.Log.LogWarning(
                    "Expected the cloned " + what + " to be named '" + original.name + "' but found '" + found.name +
                    "'. Using it anyway; the clone mirrors the original's child order.");
            }

            return found;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name)
                return root;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        private static bool TryIndexPath(Transform root, Transform target, out List<int> path)
        {
            path = new List<int>();
            Transform t = target;
            while (t != null && t != root)
            {
                path.Insert(0, t.GetSiblingIndex());
                t = t.parent;
            }

            if (t != root)
            {
                path = null;
                return false;
            }

            return true;
        }

        private static void CopyRectTransform(RectTransform from, RectTransform to)
        {
            if (from == null || to == null)
                return;

            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.sizeDelta = from.sizeDelta;
            to.anchoredPosition3D = from.anchoredPosition3D;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        // ------------------------------------------------------------------ values

        private static void OnSliderChanged(RowState state, float value)
        {
            try
            {
                SetHornVolume(value);
                UpdateValueText(state, value);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Applying the Signal Horn volume failed: " + e);
            }
        }

        /// <summary>
        /// Writing <c>ConfigEntry.Value</c> both applies live and persists, because
        /// BepInEx saves the file on set by default. The equality guard keeps a drag
        /// from rewriting the .cfg on frames where the value did not actually move.
        /// </summary>
        private static void SetHornVolume(float value)
        {
            if (Plugin.Settings.HornVolume.Value != value)
                Plugin.Settings.HornVolume.Value = value;
        }

        private static void Refresh(RowState state)
        {
            if (state == null || state.Slider == null)
                return;

            float value = Plugin.Settings.HornVolume.Value;
            if (state.Slider.value != value)
                state.Slider.value = value;

            UpdateValueText(state, value);
        }

        /// <summary>
        /// Matches OnAudioChanged exactly: Mathf.Round(v * 100f).ToString() + "%".
        /// </summary>
        private static void UpdateValueText(RowState state, float value)
        {
            if (state.ValueText == null)
                return;

            float percent = Mathf.Round(value * 100f);
            state.ValueText.text = percent.ToString() + "%";
        }

        // -------------------------------------------------------------- navigation

        /// <summary>
        /// Vanilla wires the Audio tab's gamepad chain explicitly in OnTabOpen
        /// (GuiUtils.SetNavigationDown/Up between the Continuous Music toggle and the
        /// Back/OK buttons), so the clone has to be spliced in by hand: whatever was
        /// below Music becomes what is below us instead.
        ///
        /// If the panel turns out to use Automatic navigation none of that applies -
        /// Unity finds the clone geometrically - so we leave it alone and say so.
        /// </summary>
        private static void LinkNavigation(VanillaAudioSettings settings, RowState state)
        {
            Slider music = _musicSlider(settings);
            Slider ours = state.Slider;
            if (music == null || ours == null)
                return;

            Navigation.Mode mode = music.navigation.mode;
            if (mode != Navigation.Mode.Explicit)
            {
                ours.navigation = music.navigation;
                Plugin.Log.LogInfo(
                    "Audio settings sliders use " + mode +
                    " navigation; the Signal Horn slider copies it and needs no explicit links.");
                return;
            }

            Selectable below = music.navigation.selectOnDown;

            // Idempotent: on a second OnTabOpen, Music already points at us.
            if (ReferenceEquals(below, ours))
                below = ours.navigation.selectOnDown;

            GuiUtils.SetNavigationDown(music, ours);
            GuiUtils.SetNavigationUp(ours, music);

            if (below != null && !ReferenceEquals(below, ours))
            {
                GuiUtils.SetNavigationDown(ours, below);
                GuiUtils.SetNavigationUp(below, ours);
            }
            else
            {
                GuiUtils.SetNavigationDown(ours, null);
            }

            Plugin.Log.LogInfo(
                "Gamepad navigation: Music -> Signal Horn -> " + (below == null ? "(nothing)" : below.name) + ".");
        }

        // ----------------------------------------------------------------- logging

        /// <summary>
        /// One Info block, once per session, describing exactly what the row walk
        /// found. This mod was written without being able to launch the game, so this
        /// is the evidence the maintainer needs to confirm - or correct - the strategy
        /// documented in docs/DESIGN.md.
        /// </summary>
        private static void LogHierarchyOnce(
            VanillaAudioSettings settings, Slider musicSlider, TMP_Text musicText, Transform musicRow)
        {
            if (_loggedHierarchy)
                return;

            _loggedHierarchy = true;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Audio settings row hierarchy (logged once; docs/DESIGN.md wants this):");
                sb.AppendLine("  music slider : " + Path(musicSlider.transform));
                sb.AppendLine("  music value  : " + Path(musicText.transform));
                sb.AppendLine("  chosen row   : " + Path(musicRow));
                sb.AppendLine("  row parent   : " +
                              (musicRow.parent == null ? "(none)" : Path(musicRow.parent) + " [" + Components(musicRow.parent) + "]"));

                if (musicRow.parent != null)
                {
                    sb.AppendLine("  row siblings :");
                    for (int i = 0; i < musicRow.parent.childCount; i++)
                        sb.AppendLine("    " + i + ": " + musicRow.parent.GetChild(i).name);
                }

                sb.AppendLine("  row subtree  :");
                AppendSubtree(sb, musicRow, 4);

                sb.AppendLine("  slider navigation mode: " + musicSlider.navigation.mode +
                              " (up=" + Name(musicSlider.navigation.selectOnUp) +
                              ", down=" + Name(musicSlider.navigation.selectOnDown) + ")");

                Slider sfxSlider = _sfxSlider(settings);
                TMP_Text sfxText = _sfxText(settings);
                Transform sfxRow = sfxSlider == null || sfxText == null
                    ? null
                    : FindRow(sfxSlider.transform, sfxText.transform);
                var musicRect = musicRow as RectTransform;
                var sfxRect = sfxRow as RectTransform;
                sb.Append("  sfx row      : " + (sfxRow == null ? "(not found)" : Path(sfxRow)));
                if (musicRect != null && sfxRect != null)
                {
                    sb.Append("  sfx@" + sfxRect.anchoredPosition +
                              " music@" + musicRect.anchoredPosition +
                              " step=" + (musicRect.anchoredPosition - sfxRect.anchoredPosition));
                }

                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not log the audio settings hierarchy: " + e);
            }
        }

        private static void AppendSubtree(StringBuilder sb, Transform t, int indent)
        {
            sb.Append(' ', indent).Append(t.name).Append(" [").Append(Components(t)).Append(']');

            TMP_Text text = t.GetComponent<TMP_Text>();
            if (text != null)
                sb.Append(" text=\"").Append(text.text).Append('"');

            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                AppendSubtree(sb, t.GetChild(i), indent + 2);
        }

        private static string Components(Transform t)
        {
            var names = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
                names.Add(c == null ? "<missing script>" : c.GetType().Name);

            return string.Join(", ", names.ToArray());
        }

        private static string Name(Selectable s)
        {
            return s == null ? "(none)" : s.name;
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");

            return sb.ToString();
        }
    }
}
