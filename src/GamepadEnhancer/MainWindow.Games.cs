using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// Game profiles, the interactive controller diagram and the rear paddles, wired into the main window. The input pipeline never
    /// depends on this file: it only hands the engine an immutable <see cref="EngineSettings"/> (through PushSettings) and reads
    /// a few volatile status values for display.
    /// </summary>
    public partial class MainWindow
    {
        private ControllerConfigView diagram;
        private GameProfileStore gameStore;
        private GameDefinition activeGame = GameCatalog.Generic;
        private ProfileSource activeSource = ProfileSource.Global;
        private bool loadingProfile;          // a profile is being applied to the widgets: no per-game saving meanwhile
        private bool suppressGameChange;
        private bool suppressEditor;
        private DispatcherTimer gameSaveTimer;
        private DispatcherTimer diagramTimer;

        private PaddleSetting[] paddleSettings = NewPaddleSettings();
        private int[] paddleSourceMap = { 0, 1, 2, 3 };
        private readonly IPaddleSource paddleSource = new WgiPaddleSource();
        private int paddleSeenMask;
        private PaddleReading lastReading = PaddleReading.None;
        private int availableMask;
        private int learnSlot = -1;

        private string selectedSlot;
        private readonly Dictionary<string, TextBlock> gameHintLabels = new(StringComparer.OrdinalIgnoreCase);
        private ComboBox buttonPicker, stickPicker, paddlePicker;
        private TextBlock buttonKindTag;
        private CapturingSink captureSink;
        private DispatcherTimer captureTimer;
        private bool capturing;

        private static PaddleSetting[] NewPaddleSettings()
        {
            var a = new PaddleSetting[PaddleSlots.Count];
            for (int i = 0; i < a.Length; i++) a[i] = PaddleSetting.Off;
            return a;
        }

        /// <summary>The controller layout chosen in the dropdown (the default layout while nothing is selected yet).</summary>
        private ControllerLayoutDef SelectedControllerLayout() =>
            controllerTypeBox.SelectedItem as ControllerLayoutDef ?? ControllerLayouts.Get(null);

        /// <summary>The layout whose labels are shown: the connected pad's, else the selected one.</summary>
        private ControllerLayoutDef ActiveLayout() =>
            engine.IsConnected ? ControllerLayouts.ForDevice(engine.Family) : SelectedControllerLayout();

        private PadFamily SelectedFamily() => ActiveLayout().Family;

        private readonly Dictionary<string, TextBlock> bindingTitleLabels = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Re-resolves every controller-specific label (binding titles, diagram) for the layout now in use.</summary>
        private void RefreshControllerLabels()
        {
            foreach (var pair in bindingTitleLabels) pair.Value.Text = ActionTitle(pair.Key);
            if (diagram != null) { diagram.Family = SelectedFamily(); diagram.InvalidateVisual(); }
        }

        /// <summary>The global fallback: the Default config (your saved changes to it, else the built-in default).</summary>
        private Func<InputProfile> GlobalProfile => () => GameProfiles.Resolve(GameCatalog.Generic, gameStore, null, out _);

        // ── Startup ─────────────────────────────────────────────────────────────

        private void InitializeGames()
        {
            gameStore = new GameProfileStore(Path.Combine(profilesDirectory, "games"));
            paddleSourceMap = PaddleSlots.NormalizeSourceMap(appSettings.PaddleSourceIndex);
            engine.PaddleSource = paddleSource;
            engine.Paddles.Injector = new SendInputInjector();

            diagram = new ControllerConfigView
            {
                MappingText = SlotMappingText,
                NativeText = id => activeGame.NativeLabel(id),
                IsAvailable = SlotAvailable,
                IsSlotEnabled = id => PaddleSlots.TryParseId(id, out int slot) && paddleSettings[slot].Enabled,
                Family = SelectedFamily()
            };
            diagram.SlotSelected += OnSlotSelected;
            diagramHost.Child = diagram;
            BuildGameList();

            buttonPicker = CreatePicker(keyboardOnly: false);
            stickPicker = CreatePicker(keyboardOnly: true);
            paddlePicker = CreatePaddlePicker();
            buttonKindTag = new TextBlock { Classes = { "muted" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MinWidth = 90, Margin = new Thickness(0, 0, 12, 4) };
            slotPickerRow.Children.Add(buttonPicker);
            slotPickerRow.Children.Add(stickPicker);
            slotPickerRow.Children.Add(paddlePicker);
            slotPickerRow.Children.Add(buttonKindTag);
            buttonPicker.SelectionChanged += OnActionPickerChanged;
            stickPicker.SelectionChanged += OnActionPickerChanged;
            paddlePicker.SelectionChanged += (_, _) => ApplyPaddleEdit();
            paddleEnableBox.IsCheckedChanged += (_, _) => ApplyPaddleEdit();
            paddleLearnButton.Click += PaddleLearnButton_Click;
            slotCaptureButton.Click += SlotCaptureButton_Click;
            captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            captureTimer.Tick += (_, _) => EndCapture("Nothing was pressed; cancelled.");
            slotTurboBox.IsCheckedChanged += OnSlotTurboChanged;
            slotTurboHz.ValueChanged += (_, _) => OnSlotTurboChanged(null, null);

            gameComboBox.ItemsSource = GameCatalog.All.Select(g => new ComboBoxItem { Content = g.Verified ? g.DisplayName : g.DisplayName + " (defaults not verified yet)", Tag = g.Id }).ToList();

            gameSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            gameSaveTimer.Tick += (_, _) => SaveActiveGameNow();
            diagramTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            diagramTimer.Tick += (_, _) => UpdateDiagramLive();
            diagramTimer.Start();

            LoadGame(appSettings.ActiveGame, saveCurrent: false);
        }

        // ── Game switching and persistence ──────────────────────────────────────

        private void GameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressGameChange || gameStore == null) return;
            if (gameComboBox.SelectedItem is ComboBoxItem item && item.Tag is string id) LoadGame(id, saveCurrent: true);
        }

        /// <summary>
        /// Switches to a game: the current game's customization is saved first, then the new game's profile is resolved
        /// (saved customization, else built-in default, else the global fallback) and applied.
        /// </summary>
        private void LoadGame(string id, bool saveCurrent)
        {
            if (saveCurrent) SaveActiveGameNow();
            GameDefinition game = GameCatalog.Get(id);
            InputProfile profile = GameProfiles.Resolve(game, gameStore, GlobalProfile, out ProfileSource source);
            ApplyGameProfile(game, profile, source);
        }

        private void ApplyGameProfile(GameDefinition game, InputProfile profile, ProfileSource source)
        {
            if (!string.Equals(activeGame.Id, game.Id, StringComparison.Ordinal)) { loadedProfileName = null; profileSnapshot = null; }   // a profile belongs to the game it was loaded in
            activeGame = game;
            activeSource = source;
            loadingProfile = true;
            try { ApplyProfile(profile); }
            finally { loadingProfile = false; }
            PushSettings();

            suppressGameChange = true;
            try { SyncGameList(game.Id); gameComboBox.SelectedItem = (gameComboBox.ItemsSource as IEnumerable<ComboBoxItem>)?.FirstOrDefault(i => (string)i.Tag == game.Id); }
            finally { suppressGameChange = false; }

            if (!string.Equals(appSettings.ActiveGame, game.Id, StringComparison.Ordinal))
            {
                appSettings.ActiveGame = game.Id;
                appSettings.Save();
            }
            diagram.Family = SelectedFamily();
            RefreshGameHints();
            UpdateGameLabels();
            diagram.Refresh();
            SyncEditor();
        }

        /// <summary>Shows what the selected game does on each pad button under the matching row of the list view.</summary>
        private void RefreshGameHints()
        {
            foreach (var pair in gameHintLabels)
            {
                string native = activeGame.NativeLabel(pair.Key);
                pair.Value.Text = native == null ? "" : activeGame.DisplayName + ": " + native;
                pair.Value.IsVisible = native != null;
            }
        }

        /// <summary>Called from PushSettings: schedules a (debounced) save of the selected game's customization.</summary>
        private void MarkGameDirty()
        {
            if (loadingProfile || gameSaveTimer == null) return;
            gameSaveTimer.Stop();
            gameSaveTimer.Start();
            UpdateGameLabels();
            diagram?.Refresh();
        }

        /// <summary>
        /// Stores the working configuration as the selected game's customization, or removes the stored file when it is identical to
        /// the built-in default (so a game that was never changed has no saved file, and the built-in data stays separate from user edits).
        /// </summary>
        private void SaveActiveGameNow()
        {
            gameSaveTimer?.Stop();
            if (gameStore == null || !GameCatalog.IsValidId(activeGame.Id)) return;
            try
            {
                InputProfile current = BuildProfileFromUi(activeGame.DisplayName);
                if (GameProfiles.Equivalent(current, GameProfiles.Defaults(activeGame, GlobalProfile))) gameStore.DeleteUser(activeGame.Id);
                else gameStore.SaveUser(activeGame.Id, current);
            }
            catch { /* the working configuration is still in the UI; saving is retried on the next change */ }
        }

        private async void RestoreGameDefaults_Click(object sender, RoutedEventArgs e)
        {
            bool yes = await Dialogs.ConfirmAsync(this, "Restore defaults",
                $"Discard your changes to {activeGame.DisplayName} and go back to its built-in defaults? Other games and your saved profiles are not affected.");
            if (!yes) return;
            gameSaveTimer.Stop();
            gameStore.DeleteUser(activeGame.Id);
            InputProfile defaults = GameProfiles.Defaults(activeGame, GlobalProfile);
            ApplyGameProfile(activeGame, defaults, GameProfiles.BuiltIn(activeGame) != null ? ProfileSource.BuiltIn : ProfileSource.Global);
            statusLabel.Text = $"Status: {activeGame.DisplayName} restored to its defaults";
        }

        private void UpdateGameLabels()
        {
            bool modified = false;
            try { modified = !GameProfiles.Equivalent(BuildProfileFromUi(activeGame.DisplayName), GameProfiles.Defaults(activeGame, GlobalProfile)); }
            catch { }
            string src = activeSource switch
            {
                ProfileSource.User => "your saved customization",
                ProfileSource.BuiltIn => "built-in default",
                _ => "the Default profile (no layout of its own for this game)"
            };
            activeGameLabel.Text = $"Game: {activeGame.DisplayName}   ·   Loaded: {src}" + (modified ? "   ·   modified" : "   ·   unchanged");
            activeGameSourceLabel.Text = activeGame.Verified
                ? (string.IsNullOrEmpty(activeGame.Layout) || activeGame.Layout == "n/a" ? activeGame.Source : $"Native layout: {activeGame.Layout}. Source: {activeGame.Source}")
                : activeGame.Source;
            activeConfigSummary = $"{src}" + (modified ? " · modified" : " · unchanged");
            activeConfigModified = modified;
            RefreshGameViews();
            RefreshProfileState();
        }

        // ── Diagram data ────────────────────────────────────────────────────────

        private string SlotMappingText(string id)
        {
            if (id == ControllerLayout.RightStickId) return mouseLookBox.IsChecked == true ? "Mouse movement" : "Mouse look off";
            if (PaddleSlots.TryParseId(id, out int slot))
            {
                PaddleSetting s = paddleSettings[slot];
                return s.HasTarget ? PaddleTargetText(s) : "Not set";
            }
            if (bindingControls.TryGetValue(id, out BindingControlSet c))
            {
                string v = ChoiceValue(c.KeySelector);
                return v == InputChoices.None ? "Not set" : InputChoices.DisplayName(v);
            }
            return "";
        }

        private string PaddleTargetText(PaddleSetting s)
        {
            if (s.Kind == PaddleTargetKind.PadButton)
            {
                int i = Array.IndexOf(ControllerEngine.BindingActions, s.Input);
                return "Pad: " + (i >= 0 ? PadLabels.For(SelectedFamily(), i) : s.Input);
            }
            return InputChoices.DisplayName(s.Input);
        }

        private bool SlotAvailable(string id) =>
            !PaddleSlots.TryParseId(id, out int slot) || PaddleCaps.SlotAvailable(availableMask, paddleSourceMap, slot);

        private void UpdateDiagramLive()
        {
            if (dashPad != null && dashPad.IsEffectivelyVisible)
            {
                dashPad.Family = SelectedFamily();
                dashPad.PressedActionMask = engine.IsConnected ? engine.LastMask : 0;
                dashPad.Refresh();
            }
            if (diagram == null || !diagram.IsEffectivelyVisible) return;
            lastReading = paddleSource.Read();
            paddleSeenMask |= lastReading.Mask;
            availableMask = PaddleCaps.AvailableMask(lastReading, paddleSeenMask);

            if (learnSlot >= 0 && lastReading.Connected && lastReading.Mask != 0) CompletePaddleLearn();

            int physicalSlots = 0;
            for (int i = 0; i < PaddleSlots.Count; i++)
                if (((lastReading.Mask >> paddleSourceMap[i]) & 1) != 0) physicalSlots |= 1 << i;

            diagram.Family = SelectedFamily();
            diagram.PressedPaddleSlots = physicalSlots;
            diagram.PressedActionMask = engine.IsConnected ? engine.LastMask : 0;
            diagram.Refresh();
            UpdatePaddleStatus();
            UpdatePaddleSummaryLive(physicalSlots);
        }

        private void UpdatePaddleStatus()
        {
            if (learnSlot >= 0) return;   // the "press a paddle" prompt stays until it is answered
            if (!lastReading.Connected) paddleStatusLabel.Text = "No physical controller detected by Windows.Gaming.Input; paddles are inactive.";
            else
            {
                int n = Enumerable.Range(0, PaddleSlots.Count).Count(i => ((availableMask >> i) & 1) != 0);
                paddleStatusLabel.Text = n > 0
                    ? $"Controller: {lastReading.DeviceName} · {n} paddle(s) available"
                    : $"Controller: {lastReading.DeviceName} · no paddles detected (press a paddle to confirm one works)";
            }
        }

        // ── Editor ──────────────────────────────────────────────────────────────

        private void OnSlotSelected(string id)
        {
            EndCapture(null);
            selectedSlot = id;
            SyncEditor();
        }

        /// <summary>Shows the selected control in the editor card, with the picker that fits it.</summary>
        private void SyncEditor()
        {
            if (diagram == null) return;
            suppressEditor = true;
            try
            {
                string id = selectedSlot;
                LayoutSlot slot = id == null ? null : ControllerLayout.Find(diagram.Family, id);
                bool isPaddle = PaddleSlots.TryParseId(id, out int paddleIndex);
                bool isRight = id == ControllerLayout.RightStickId;
                bool isAction = id != null && bindingControls.ContainsKey(id);
                bool isStick = isAction && InputChoices.IsStickAction(id);

                slotTitle.Text = slot?.Title ?? "Select a control";
                string native = isAction ? activeGame.NativeLabel(id) : null;
                slotNative.Text = native != null ? $"In {activeGame.DisplayName}: {native}"
                    : isAction && !activeGame.Verified && activeGame.Id != GameCatalog.GenericId ? $"{activeGame.DisplayName}: native layout not verified yet, so no game action is shown."
                    : "";

                buttonPicker.IsVisible = isAction && !isStick;
                stickPicker.IsVisible = isStick;
                paddlePicker.IsVisible = isPaddle;
                buttonKindTag.IsVisible = isAction;
                slotTurboRow.IsVisible = isAction && !isStick;
                slotCaptureRow.IsVisible = isAction || isPaddle;
                paddleRow.IsVisible = isPaddle;

                if (isAction)
                {
                    BindingControlSet c = bindingControls[id];
                    string value = ChoiceValue(c.KeySelector);
                    SetChoice(isStick ? stickPicker : buttonPicker, value);
                    buttonKindTag.Text = InputChoices.KindLabel(InputChoices.KindOf(value));
                    if (c.TurboToggle != null) { slotTurboBox.IsChecked = c.TurboToggle.IsChecked; slotTurboHz.Value = c.TurboHzBox.Value; }
                    slotHint.Text = isStick ? "Left stick direction: keyboard keys only. Opposite keys cancel; diagonals are not faster." : "";
                }
                else if (isPaddle)
                {
                    PaddleSetting s = paddleSettings[paddleIndex];
                    paddleEnableBox.IsChecked = s.Enabled;
                    SetPaddleChoice(paddlePicker, s);
                    slotHint.Text = "A paddle can press a virtual controller button, a keyboard key or a mouse button. Two paddles cannot share a target. Paddles only work while a physical controller that has them is connected.";
                }
                else if (isRight)
                {
                    slotHint.Text = "The right stick is mouse movement. Turn it on or off and set sensitivity, curve and dead zone in the Right stick card below.";
                }
                else slotHint.Text = "";
            }
            finally { suppressEditor = false; }
        }

        private void OnActionPickerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressEditor || selectedSlot == null || !bindingControls.TryGetValue(selectedSlot, out BindingControlSet c)) return;
            SetChoice(c.KeySelector, ChoiceValue((ComboBox)sender));   // the list picker does the rest (engine settings, kind tag, saving)
            diagram.Flash(selectedSlot);
            SyncEditor();
        }

        private void OnSlotTurboChanged(object sender, RoutedEventArgs e)
        {
            if (suppressEditor || selectedSlot == null || !bindingControls.TryGetValue(selectedSlot, out BindingControlSet c) || c.TurboToggle == null) return;
            c.TurboToggle.IsChecked = slotTurboBox.IsChecked;
            c.TurboHzBox.Value = slotTurboHz.Value;
            diagram.Flash(selectedSlot);
        }

        /// <summary>Called when a list picker changes (from CreateBindingSelector): keeps the diagram and editor in step.</summary>
        private void OnBindingChanged(string action)
        {
            diagram?.Refresh();
            if (!suppressEditor && string.Equals(action, selectedSlot, StringComparison.OrdinalIgnoreCase)) SyncEditor();
        }

        // ── Rear paddles ────────────────────────────────────────────────────────

        private static ComboBox CreatePaddlePicker()
        {
            var combo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 10, 4), IsVisible = false };
            var items = new List<ComboBoxItem> { new ComboBoxItem { Content = "None (not set)", Tag = "none" } };
            void Header(string title) => items.Add(new ComboBoxItem { Content = "— " + title + " —", IsEnabled = false, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            Header("Virtual controller buttons");
            foreach (string a in PaddleMappings.PadButtonTargets)
            {
                int i = Array.IndexOf(ControllerEngine.BindingActions, a);
                items.Add(new ComboBoxItem { Content = $"{PadLabels.For(PadFamily.DualShock4, i)} / {PadLabels.For(PadFamily.Xbox360, i)}", Tag = "pad:" + a });
            }
            Header("Keyboard");
            items.AddRange(InputChoices.Keyboard.Select(n => new ComboBoxItem { Content = InputChoices.DisplayName(n), Tag = "key:" + n }));
            Header("Mouse buttons");
            items.AddRange(InputChoices.MouseButtons.Select(n => new ComboBoxItem { Content = InputChoices.DisplayName(n), Tag = "mouse:" + n }));
            combo.ItemsSource = items;
            combo.SelectedIndex = 0;
            return combo;
        }

        private static (PaddleTargetKind Kind, string Input) ParsePaddleChoice(string tag)
        {
            if (tag == null) return (PaddleTargetKind.None, "None");
            if (tag.StartsWith("pad:", StringComparison.Ordinal)) return (PaddleTargetKind.PadButton, tag.Substring(4));
            if (tag.StartsWith("key:", StringComparison.Ordinal)) return (PaddleTargetKind.Key, tag.Substring(4));
            if (tag.StartsWith("mouse:", StringComparison.Ordinal)) return (PaddleTargetKind.MouseButton, tag.Substring(6));
            return (PaddleTargetKind.None, "None");
        }

        private static void SetPaddleChoice(ComboBox combo, PaddleSetting s)
        {
            string tag = !s.HasTarget ? "none"
                : s.Kind == PaddleTargetKind.PadButton ? "pad:" + s.Input
                : s.Kind == PaddleTargetKind.Key ? "key:" + s.Input
                : "mouse:" + s.Input;
            var items = combo.ItemsSource as IEnumerable<ComboBoxItem>;
            combo.SelectedItem = items?.FirstOrDefault(i => i.Tag is string t && t == tag) ?? items?.First();
        }

        /// <summary>Applies the paddle editor to the selected paddle; a duplicate target is refused and explained.</summary>
        private void ApplyPaddleEdit()
        {
            if (suppressEditor || !PaddleSlots.TryParseId(selectedSlot, out int slot)) return;
            var (kind, input) = ParsePaddleChoice((paddlePicker.SelectedItem as ComboBoxItem)?.Tag as string);
            var copy = (PaddleSetting[])paddleSettings.Clone();
            if (!PaddleMappings.TryAssign(copy, slot, paddleEnableBox.IsChecked == true, kind, input, out string error))
            {
                statusLabel.Text = "Status: " + error;
                SyncEditor();   // put the editor back to what is really set
                return;
            }
            paddleSettings = copy;
            PushSettings();
            diagram.Flash(selectedSlot);
            SyncEditor();
        }

        private void PaddleLearnButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PaddleSlots.TryParseId(selectedSlot, out int slot)) return;
            if (!lastReading.Connected) { paddleStatusLabel.Text = "No physical controller detected, so there is nothing to detect."; return; }
            learnSlot = slot;
            paddleStatusLabel.Text = $"Press the physical paddle you want for the {PaddleSlots.Titles[slot]}...";
        }

        /// <summary>Assigns the paddle that was just pressed to the slot being set up (swapping if another slot used it).</summary>
        private void CompletePaddleLearn()
        {
            int hardware = Enumerable.Range(0, PaddleSlots.Count).First(i => ((lastReading.Mask >> i) & 1) != 0);
            int slot = learnSlot;
            learnSlot = -1;
            int other = Array.IndexOf(paddleSourceMap, hardware);
            int[] map = (int[])paddleSourceMap.Clone();
            if (other >= 0 && other != slot) map[other] = map[slot];
            map[slot] = hardware;
            paddleSourceMap = PaddleSlots.NormalizeSourceMap(map);
            appSettings.PaddleSourceIndex = (int[])paddleSourceMap.Clone();
            appSettings.Save();
            PushSettings();
            paddleStatusLabel.Text = $"{PaddleSlots.Titles[slot]} is now hardware paddle {hardware + 1}.";
            diagram.Flash(PaddleSlots.Id(slot));
        }

        // ── "Press a key" ───────────────────────────────────────────────────────

        private void SlotCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (capturing) { EndCapture("Cancelled."); return; }
            if (selectedSlot == null || captureSink == null) return;
            bool isPaddle = PaddleSlots.TryParseId(selectedSlot, out _);
            if (!isPaddle && !bindingControls.ContainsKey(selectedSlot)) return;

            capturing = true;
            slotCaptureButton.Content = "Press it now... (Esc cancels)";
            slotHint.Text = InputChoices.IsStickAction(selectedSlot ?? "") ? "Press the keyboard key you want." : "Press the keyboard key, mouse button or wheel notch you want.";
            captureTimer.Stop();
            captureTimer.Start();
            captureSink.BeginCapture(c => Dispatcher.UIThread.Post(() => OnCaptured(c)));
        }

        private void EndCapture(string message)
        {
            if (!capturing && message == null) return;
            captureSink?.EndCapture();
            captureTimer?.Stop();
            capturing = false;
            slotCaptureButton.Content = "Press a key or button...";
            if (message != null) statusLabel.Text = "Status: " + message;
            SyncEditor();
        }

        private void OnCaptured(CapturedInput input)
        {
            if (!capturing) return;
            if (input.Kind == CapturedKind.Key && input.Name == "Escape") { EndCapture("Cancelled."); return; }

            string slot = selectedSlot;
            if (PaddleSlots.TryParseId(slot, out int paddle))
            {
                if (!InputCapture.TryAcceptForPaddle(input.Kind, input.Name, out PaddleTargetKind kind, out string value, out string error))
                { EndCapture(error); return; }
                var copy = (PaddleSetting[])paddleSettings.Clone();
                if (!PaddleMappings.TryAssign(copy, paddle, true, kind, value, out error)) { EndCapture(error); return; }
                paddleSettings = copy;
                PushSettings();
                diagram.Flash(slot);
                EndCapture($"{PaddleSlots.Titles[paddle]} = {InputChoices.DisplayName(value)}");
                return;
            }

            if (slot != null && bindingControls.TryGetValue(slot, out BindingControlSet c))
            {
                if (!InputCapture.TryAcceptForAction(slot, input.Kind, input.Name, out string value, out string error)) { EndCapture(error); return; }
                SetChoice(c.KeySelector, value);   // the list picker does the rest (engine settings, kind tag, saving)
                diagram.Flash(slot);
                EndCapture($"{ActionTitle(slot)} = {InputChoices.DisplayName(value)}");
                return;
            }
            EndCapture(null);
        }

        private void StopGames()
        {
            EndCapture(null);
            try { diagramTimer?.Stop(); } catch { }
            SaveActiveGameNow();
        }
    }
}
