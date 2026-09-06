using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
// Valheim.SettingsGui.AudioSettings collides with UnityEngine.AudioSettings, the global
// audio-system class, and UnityEngine is imported for everything else in this file.
using AudioSettings = Valheim.SettingsGui.AudioSettings;

namespace HornOfCalling
{
    /// <summary>
    /// The horn's row on the Audio tab of the game's own settings menu, cloned from the
    /// SFX volume row beside it.
    ///
    /// Cloned rather than built, for the same reason the blast prefab is: the vanilla
    /// row already carries the panel's fonts, colours, sprites, layout element and the
    /// slider's own handle and fill rects, none of which a hand-assembled RectTransform
    /// would match and all of which would drift the next time the panel is restyled.
    ///
    /// The settings panel is destroyed on both OK and Back (Settings.CloseSettings), so
    /// every one of these is short-lived: the row is rebuilt each time the menu opens,
    /// and the fields here are re-pointed at the new one by <see cref="Build"/>.
    /// </summary>
    internal static class VolumeSlider
    {
        private const string RowName = "HornOfCallingVolume";

        /// <summary>
        /// Plain text, not a "$settings_" token like the row it is cloned from. The mod
        /// ships no translation table, and Localization leaves an unknown token on
        /// screen verbatim rather than failing, so a token here would read as
        /// "$settings_hornvol" to every player. Text without a "$" passes through
        /// Localize unchanged, so this survives however often the panel is localized.
        /// </summary>
        private const string Label = "Horn of Calling";

        private static Slider _slider;
        private static TMP_Text _value;
        private static VolumePreview _preview;

        /// <summary>
        /// Clones the SFX row into the audio list and wires it to the config entry.
        ///
        /// Called from a postfix on AudioSettings.Initialize, which Settings.Awake runs
        /// once per opening of the menu - the one point where the tab's own serialized
        /// references are guaranteed to be assigned and no user input has happened yet.
        /// </summary>
        internal static void Build(AudioSettings tab)
        {
            Slider template = tab.m_sfxVolumeSlider;
            TMP_Text templateValue = tab.m_sfxVolumeText;
            if (template == null || templateValue == null)
            {
                Plugin.Log.LogWarning(
                    "The Audio settings tab has no SFX volume row to clone; the horn volume slider " +
                    "will not appear. The setting is still editable in the config file.");
                return;
            }

            // Tested against the live list rather than latched behind a bool, for the
            // reason the item registration is: this runs once per opening of the menu
            // today, but the cost of being wrong is a second row stacked on the first,
            // and the check is one child lookup.
            if (template.transform.parent.Find(RowName) != null) return;

            // The Slider component sits on the row GameObject itself, with the caption
            // and the "50%" readout as children - so cloning the slider's GameObject
            // clones the whole row. Verified against AudioTab.prefab, where the list is
            // MasterVolume / SfxVolume / MusicVolume / ContinuosMusic and each row is
            // shaped Background, Fill Area, Handle Slide Area, Label, Value.
            GameObject row = Object.Instantiate(template.gameObject, template.transform.parent);
            row.name = RowName;

            // Below the music slider, so the three vanilla volumes stay together and the
            // horn reads as an addition to them. Falls back to sitting under the row we
            // cloned if the music slider is not wired up.
            Transform anchor = tab.m_musicVolumeSlider != null
                ? tab.m_musicVolumeSlider.transform
                : template.transform;
            row.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);

            _slider = row.GetComponent<Slider>();
            _value = FindValueText(row.transform, templateValue.name);
            SetLabel(row.transform, _value);

            // The clone inherits the SFX slider's persistent call to
            // AudioSettings.OnAudioChanged, which would re-apply the three vanilla
            // volumes every time this slider moved. RemoveAllListeners does NOT clear a
            // persistent call - it only drops ones added from code - so the event object
            // is replaced outright.
            _slider.onValueChanged = new Slider.SliderEvent();
            _slider.minValue = 0f;
            _slider.maxValue = 1f;
            _slider.wholeNumbers = false;
            // Assigned before the listener so seeding the row does not count as a change.
            _slider.value = Mathf.Clamp01(Plugin.BlastVolume.Value);
            _slider.onValueChanged.AddListener(OnSliderChanged);

            // On the row rather than on a GameObject of our own, so the panel being
            // destroyed on OK or Back takes the AudioSource with it - which is what stops
            // a preview still sounding after the menu closes, with no teardown to write.
            _preview = row.AddComponent<VolumePreview>();
            _preview.Initialise();

            SpliceIntoNavigation(anchor.GetComponent<Selectable>(), _slider);
            UpdateValueText();

            Plugin.Log.LogDebug("Added the horn volume row to the Audio settings tab.");
        }

        /// <summary>
        /// Writes the slider back to the config entry, from a postfix on
        /// AudioSettings.OnOkAsync. Assigning the entry saves the config file and raises
        /// SettingChanged, which is what actually applies the value.
        /// </summary>
        internal static void Commit()
        {
            if (_slider == null) return;
            Plugin.BlastVolume.Value = Mathf.Clamp01(_slider.value);
            Forget();
        }

        /// <summary>
        /// Throws away an uncommitted drag, from a postfix on AudioSettings.OnBack -
        /// which is how the vanilla rows behave, since they apply live too.
        /// </summary>
        internal static void Revert()
        {
            HornSound.ApplyVolume();
            Forget();
        }

        private static void Forget()
        {
            _slider = null;
            _value = null;
            _preview = null;
        }

        private static void OnSliderChanged(float value)
        {
            // Applied live rather than only on OK, matching the vanilla volume rows.
            // Deliberately not written to the config yet: Back has to be able to undo it.
            HornSound.SetVolume(value);
            UpdateValueText();
            if (_preview != null) _preview.Schedule(value);
        }

        private static void UpdateValueText()
        {
            if (_value == null || _slider == null) return;
            // Mathf.Round and a bare "%" is exactly what AudioSettings.OnAudioChanged
            // prints, so the four rows read the same.
            _value.text = Mathf.Round(_slider.value * 100f) + "%";
        }

        /// <summary>
        /// Finds the clone's percentage readout by the name the tab's own value text
        /// carries, rather than hardcoding "Value": the tab hands us the original, and
        /// Instantiate copies names, so the original is a better source for the name than
        /// this file is.
        /// </summary>
        private static TMP_Text FindValueText(Transform row, string valueName)
        {
            Transform found = row.Find(valueName);
            TMP_Text text = found != null ? found.GetComponent<TMP_Text>() : null;
            if (text == null)
            {
                Plugin.Log.LogWarning(
                    "No '" + valueName + "' text under the cloned volume row; the horn slider will " +
                    "show the SFX percentage instead of its own.");
            }
            return text;
        }

        /// <summary>
        /// Retitles the clone. The caption is whichever text in the row is not the
        /// percentage readout - the row has exactly the two.
        /// </summary>
        private static void SetLabel(Transform row, TMP_Text value)
        {
            var texts = new List<TMP_Text>(row.GetComponentsInChildren<TMP_Text>(true));
            foreach (TMP_Text text in texts)
            {
                if (text == value) continue;
                text.text = Label;
                return;
            }

            Plugin.Log.LogWarning(
                "No caption text under the cloned volume row; the horn slider will be labelled " +
                "as the SFX one. Texts found: " + texts.Count + ".");
        }

        /// <summary>
        /// Puts the new row into the tab's keyboard and gamepad navigation chain.
        ///
        /// The vanilla rows use Navigation.Mode.Explicit, not Automatic - each one names
        /// the row above and below it by hand. So a clone arrives pointing at the SFX
        /// row's neighbours, and the row it was dropped after still points past it: a
        /// gamepad walking down the list would skip the horn entirely and a player
        /// without a mouse could never reach it. Splicing means fixing three links, not
        /// one, because the row below has to point back up at us.
        /// </summary>
        private static void SpliceIntoNavigation(Selectable previous, Selectable inserted)
        {
            if (previous == null || inserted == null) return;

            Selectable next = previous.navigation.selectOnDown;

            GuiUtils.SetNavigationDown(previous, inserted);
            GuiUtils.SetNavigationUp(inserted, previous);
            GuiUtils.SetNavigationDown(inserted, next);
            if (next != null) GuiUtils.SetNavigationUp(next, inserted);
        }
    }

    /// <summary>
    /// Sounds the blast once the player has finished choosing a level, so the slider can
    /// be judged by ear rather than by the number beside it.
    ///
    /// "Finished" is a settle timer rather than a mouse-up, because the row is also
    /// driven by arrow keys and a gamepad stick, which produce a stream of value changes
    /// and no release event at all - a mouse-only trigger would leave the preview silent
    /// for exactly the players who cannot see the handle move under a cursor. The pointer
    /// handlers only suppress the timer while the handle is actually held, so a drag that
    /// pauses mid-way does not fire; letting go does, immediately, because by then the
    /// deadline has usually already passed.
    ///
    /// Times on <see cref="Time.unscaledTime"/>: the settings menu pauses the game in
    /// single-player, and a scaled timer would never come due.
    /// </summary>
    internal class VolumePreview : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>
        /// Long enough that dragging across the row is one preview rather than a stutter
        /// of them, short enough not to feel like a lag behind the handle.
        /// </summary>
        private const float SettleSeconds = 0.35f;

        private AudioSource _source;
        private float _dueAt = -1f;
        private float _level = 1f;
        private bool _held;

        /// <summary>
        /// Called explicitly rather than from Awake, which has already run by the time
        /// AddComponent returns on an active row and would not have run at all on an
        /// inactive one - so neither ordering is worth depending on.
        /// </summary>
        internal void Initialise()
        {
            _source = gameObject.AddComponent<AudioSource>();
            if (HornSound.PrepareToPreview(_source)) return;

            // Nothing to play: no blast prefab (the main menu) or the audio never
            // decoded. The slider is still usable, so this is a debug line, not a warning.
            Destroy(_source);
            _source = null;
            Plugin.Log.LogDebug("No horn blast to preview; the volume slider will be silent.");
        }

        internal void Schedule(float level)
        {
            if (_source == null) return;
            _level = level;
            _dueAt = Time.unscaledTime + SettleSeconds;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _held = false;
        }

        private void Update()
        {
            if (_dueAt < 0f || _held || Time.unscaledTime < _dueAt) return;
            _dueAt = -1f;

            // Restarted rather than layered. The blast is nearly five seconds long, so
            // without the Stop a player working the slider would stack overlapping copies
            // of it and hear something louder than any setting they picked.
            _source.Stop();
            _source.volume = Mathf.Clamp01(_level);
            _source.Play();
        }
    }

    /// <summary>
    /// The three moments the settings menu offers: built once when it opens, saved on
    /// OK, discarded on Back. Every body is wrapped, because these are shared postfix
    /// chains and a throw here would abort whatever other mods have on them - the same
    /// reasoning as the registration patches.
    /// </summary>
    [HarmonyPatch(typeof(AudioSettings))]
    internal static class AudioSettingsPatches
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(AudioSettings.Initialize))]
        internal static void InitializePostfix(AudioSettings __instance)
        {
            try { VolumeSlider.Build(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("Could not add the horn volume slider: " + e); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(AudioSettings.OnOkAsync))]
        internal static void OnOkAsyncPostfix()
        {
            try { VolumeSlider.Commit(); }
            catch (System.Exception e) { Plugin.Log.LogError("Could not save the horn volume: " + e); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(AudioSettings.OnBack))]
        internal static void OnBackPostfix()
        {
            try { VolumeSlider.Revert(); }
            catch (System.Exception e) { Plugin.Log.LogError("Could not restore the horn volume: " + e); }
        }
    }
}
