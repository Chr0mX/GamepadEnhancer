using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// The Bindings page: the list of advanced bindings with their execution state. The list is the user's settings (the
    /// <see cref="advancedBindings"/> model, saved with the profile); what each row shows as "running" comes only from
    /// <see cref="BindingRuntime.Snapshot"/>, the real state of the engine, never from whether a binding is merely enabled.
    /// </summary>
    public partial class MainWindow
    {
        private sealed class BindingRowView
        {
            public Border Card;
            public TextBlock Status;
            public TextBlock Physical;
        }

        private readonly List<BindingDef> advancedBindings = new();
        private readonly Dictionary<string, BindingRowView> bindingRows = new();
        private BindingDef copiedBinding;
        private string expandedBindingId;
        private string learningBindingId;
        private DispatcherTimer bindingStatusTimer, bindingLearnTimer;
        private bool rebuildingBindings;

        // ── Persistence glue ────────────────────────────────────────────────────

        /// <summary>Replaces the list with a profile's bindings (loading a profile or a game).</summary>
        private void LoadAdvancedBindings(IEnumerable<BindingEntry> entries)
        {
            advancedBindings.Clear();
            advancedBindings.AddRange(BindingEntries.Resolve(entries));
            CancelLearn();
            if (bindingsList != null) RebuildBindingList();
            PushSettings();   // the engine needs the new list even when no other control changed
        }

        private void InitializeBindingsPage()
        {
            bindingStatusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            bindingStatusTimer.Tick += (_, _) => UpdateBindingStatus();
            bindingLearnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            bindingLearnTimer.Tick += (_, _) => CancelLearn("Nothing was pressed; cancelled.");
            RebuildBindingList();
        }

        private void SetBindingsPageActive(bool on)
        {
            if (bindingStatusTimer == null) return;
            if (on) { RebuildBindingList(); bindingStatusTimer.Start(); UpdateBindingStatus(); }
            else { bindingStatusTimer.Stop(); CancelLearn(); }
        }

        /// <summary>The list changed: save it with the profile, hand it to the engine and redraw the rows.</summary>
        private void BindingsChanged(bool rebuild)
        {
            PushSettings();
            if (!rebuild) return;
            // not from inside the control that raised the event: replacing it there would pull the rug from under its handler
            Dispatcher.UIThread.Post(RebuildBindingList);
        }

        private void ReplaceBinding(BindingDef updated)
        {
            int i = advancedBindings.FindIndex(b => b.Id == updated.Id);
            if (i >= 0) advancedBindings[i] = updated;
        }

        // ── Commands ────────────────────────────────────────────────────────────

        private void AddBinding_Click(object sender, RoutedEventArgs e)
        {
            if (advancedBindings.Count >= BindingEntries.MaxBindings) { statusLabel.Text = $"Status: At most {BindingEntries.MaxBindings} bindings"; return; }
            var def = new BindingDef
            {
                Input = "None",
                Condition = BindingCondition.Held,
                Outputs = new[] { new BindingOutput { Kind = BindingOutputKind.PadButton, Name = ControllerEngine.ActionACross } }
            };
            advancedBindings.Add(def);
            expandedBindingId = def.Id;
            BindingsChanged(rebuild: true);
            StartLearn(def.Id);
        }

        private void PasteBinding_Click(object sender, RoutedEventArgs e)
        {
            if (copiedBinding == null || advancedBindings.Count >= BindingEntries.MaxBindings) return;
            advancedBindings.Add(copiedBinding.Clone());
            BindingsChanged(rebuild: true);
        }

        // ── Learn an input ──────────────────────────────────────────────────────

        private void StartLearn(string id)
        {
            if (captureSink == null) return;
            if (capturing) EndCapture(null);   // the controller page's own "press a key" and this one never wait at the same time
            CancelLearn();
            learningBindingId = id;
            captureSink.BeginCapture(c => Dispatcher.UIThread.Post(() => OnBindingLearned(c)));
            bindingLearnTimer.Stop();
            bindingLearnTimer.Start();
            statusLabel.Text = "Status: Press the key, mouse button or wheel notch for this binding (Esc cancels)";
            Dispatcher.UIThread.Post(RebuildBindingList);
        }

        private void CancelLearn(string message = null)
        {
            if (learningBindingId == null) return;
            learningBindingId = null;
            captureSink?.EndCapture();
            bindingLearnTimer?.Stop();
            if (message != null) statusLabel.Text = "Status: " + message;
            if (bindingsList != null) Dispatcher.UIThread.Post(RebuildBindingList);
        }

        private void OnBindingLearned(CapturedInput c)
        {
            string id = learningBindingId;
            if (id == null) return;
            if (c.Kind == CapturedKind.Key && c.Name == "Escape") { CancelLearn("Cancelled."); return; }
            string input = BindingInputs.Canonical(c.Name);
            BindingDef def = advancedBindings.FirstOrDefault(b => b.Id == id);
            learningBindingId = null;
            bindingLearnTimer.Stop();
            if (input == null || def == null) { statusLabel.Text = "Status: That input cannot be used for a binding"; RebuildBindingList(); return; }
            ReplaceBinding(def.With(input: input, outputs: DropSelfOutputs(input, def.Outputs)));
            statusLabel.Text = $"Status: Binding input is now {InputChoices.DisplayName(input)}";
            BindingsChanged(rebuild: true);
        }

        /// <summary>An output that presses its own input would feed back into the binding; such an output is removed.</summary>
        private static IReadOnlyList<BindingOutput> DropSelfOutputs(string input, IReadOnlyList<BindingOutput> outputs) =>
            outputs.Where(o => !(o.Kind == BindingOutputKind.Key || o.Kind == BindingOutputKind.MouseButton) || !string.Equals(o.Name, input, StringComparison.OrdinalIgnoreCase)).ToList();

        // ── Rows ────────────────────────────────────────────────────────────────

        private void RebuildBindingList()
        {
            if (bindingsList == null || rebuildingBindings) return;
            rebuildingBindings = true;
            try
            {
                bindingsList.Children.Clear();
                bindingRows.Clear();
                foreach (BindingDef d in advancedBindings) bindingsList.Children.Add(BuildBindingRow(d));
                bindingsEmpty.IsVisible = advancedBindings.Count == 0;
                pasteBindingButton.IsEnabled = copiedBinding != null;
                RefreshAutomaticBindingsText();
            }
            finally { rebuildingBindings = false; }
            UpdateBindingStatus();
        }

        private static ComboBoxItem ChoiceItem(string text, string tag) => new() { Content = text, Tag = tag };

        private IEnumerable<ComboBoxItem> InputItems() =>
            new[] { ChoiceItem("None (not set)", "None") }
                .Concat(InputChoices.Keyboard.Select(k => ChoiceItem(InputChoices.DisplayName(k), k)))
                .Concat(InputChoices.MouseButtons.Select(k => ChoiceItem(InputChoices.DisplayName(k), k)))
                .Concat(InputChoices.MouseWheel.Select(k => ChoiceItem(InputChoices.DisplayName(k), k)))
                .Concat(BindingInputs.Analog.Select(k => ChoiceItem(k + " (analog)", k)));

        private Control BuildBindingRow(BindingDef d)
        {
            bool learning = learningBindingId == d.Id;
            var card = new Border { Classes = { "card", "bindrow" }, Padding = new Thickness(12, 10) };
            var stack = new StackPanel { Spacing = 8 };
            var top = new WrapPanel();

            var status = new TextBlock { Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150, Text = "" };
            top.Children.Add(WrapCell(status));

            // input: pick from the list, or press it
            var inputBox = new ComboBox { Width = 190, ItemsSource = InputItems().ToList() };
            inputBox.SelectedItem = ((List<ComboBoxItem>)inputBox.ItemsSource).FirstOrDefault(i => (string)i.Tag == d.Input);
            inputBox.SelectionChanged += (_, _) =>
            {
                if (rebuildingBindings || inputBox.SelectedItem is not ComboBoxItem it || it.Tag is not string name || name == d.Input) return;
                ReplaceBinding(d.With(input: name, outputs: DropSelfOutputs(name, d.Outputs)));
                BindingsChanged(rebuild: true);
            };
            top.Children.Add(WrapCell(inputBox));
            var learn = new Button { Content = learning ? "Press it now..." : "Press a key...", Classes = { "soft" } };
            ToolTip.SetTip(learn, "Click, then press the key, mouse button or wheel notch to use as this binding's input. Esc cancels.");
            learn.Click += (_, _) => { if (learningBindingId == d.Id) CancelLearn("Cancelled."); else StartLearn(d.Id); };
            top.Children.Add(WrapCell(learn));
            top.Children.Add(WrapCell(new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center }));
            top.Children.Add(WrapCell(new TextBlock { Text = OutputsText(d), VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 }));

            // condition selector, toggle, enabled
            var cond = new ComboBox { Width = 120, ItemsSource = BindingConditions.All.Select(c => ChoiceItem(BindingConditions.Title(c), c.ToString())).ToList() };
            cond.SelectedItem = ((List<ComboBoxItem>)cond.ItemsSource).First(i => (string)i.Tag == d.Condition.ToString());
            ToolTip.SetTip(cond, BindingConditions.Description(d.Condition));
            cond.SelectionChanged += (_, _) =>
            {
                if (rebuildingBindings || cond.SelectedItem is not ComboBoxItem it || !BindingConditions.TryParse(it.Tag as string, out BindingCondition c) || c == d.Condition) return;
                ReplaceBinding(d.With(condition: c));
                BindingsChanged(rebuild: true);
            };
            top.Children.Add(WrapCell(cond));

            bool toggleApplies = BindingConditions.IsRunOnce(d.Condition) && !d.IsAnalog;
            var toggle = new ToggleButton { Content = "Toggle", IsChecked = d.Toggle, IsEnabled = toggleApplies };
            ToolTip.SetTip(toggle, toggleApplies
                ? "Latch the binding: the first activation starts it, it keeps running after you let go, the next activation stops it."
                : "Toggle needs a run-once condition (Press, Release, Double, Tap or Hold).");
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (rebuildingBindings || (toggle.IsChecked == true) == d.Toggle) return;
                ReplaceBinding(d.With(toggle: toggle.IsChecked == true));
                BindingsChanged(rebuild: true);
            };
            top.Children.Add(WrapCell(toggle));

            var layerBox = new ComboBox { Width = 130, ItemsSource = LayerItems(), IsEnabled = d.ActivatesLayer == 0 };
            layerBox.SelectedItem = ((List<ComboBoxItem>)layerBox.ItemsSource).First(i => (string)i.Tag == d.Layer.ToString());
            ToolTip.SetTip(layerBox, d.ActivatesLayer == 0
                ? "Base layer bindings always run. A binding on Layer 1-3 runs only while that layer is switched on, and replaces a base binding of the same input meanwhile (with no outputs it un-maps the input there)."
                : "A binding that switches a layer on stays on the base layer.");
            layerBox.SelectionChanged += (_, _) =>
            {
                if (rebuildingBindings || layerBox.SelectedItem is not ComboBoxItem it || !int.TryParse(it.Tag as string, out int n) || n == d.Layer) return;
                ReplaceBinding(d.With(layer: n));
                BindingsChanged(rebuild: true);
            };
            top.Children.Add(WrapCell(layerBox));

            var enabled = new ToggleSwitch { OnContent = "On", OffContent = "Off", IsChecked = d.Enabled };
            ToolTip.SetTip(enabled, "Enabled means eligible to run. It does not mean running; see the status on the left.");
            enabled.IsCheckedChanged += (_, _) =>
            {
                if (rebuildingBindings || (enabled.IsChecked == true) == d.Enabled) return;
                ReplaceBinding(d.With(enabled: enabled.IsChecked == true));
                BindingsChanged(rebuild: true);
            };
            top.Children.Add(WrapCell(enabled));

            var edit = new Button { Content = expandedBindingId == d.Id ? "Hide details" : "Details", Classes = { "ghost" } };
            edit.Click += (_, _) => { expandedBindingId = expandedBindingId == d.Id ? null : d.Id; Dispatcher.UIThread.Post(RebuildBindingList); };
            top.Children.Add(WrapCell(edit));

            var copy = new Button { Classes = { "ghost", "icon" }, Content = new Icon { Kind = "copy" } };
            ToolTip.SetTip(copy, "Copy this binding; use Paste binding to add a copy.");
            copy.Click += (_, _) => { copiedBinding = d; pasteBindingButton.IsEnabled = true; statusLabel.Text = "Status: Binding copied"; };
            top.Children.Add(WrapCell(copy));
            var delete = new Button { Classes = { "ghost", "icon" }, Content = new Icon { Kind = "trash" } };
            ToolTip.SetTip(delete, "Delete this binding (anything it was holding is released).");
            delete.Click += (_, _) => { advancedBindings.RemoveAll(b => b.Id == d.Id); BindingsChanged(rebuild: true); };
            top.Children.Add(WrapCell(delete));
            stack.Children.Add(top);

            var physical = new TextBlock { Classes = { "caption" }, IsVisible = false };
            stack.Children.Add(physical);
            string warn = BindingEntries.CompatibilityWarning(d, ActiveLayout());
            if (warn != null) stack.Children.Add(new TextBlock { Text = "Warning: " + warn, Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            if (d.IsAnalog)
                stack.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = "Analog input: always running while enabled, no condition applies. The mouse already drives the right stick through Mouse look; outputs are not used for analog inputs." });

            if (expandedBindingId == d.Id) stack.Children.Add(BuildBindingDetails(d));

            card.Child = stack;
            bindingRows[d.Id] = new BindingRowView { Card = card, Status = status, Physical = physical };
            return card;
        }

        private static Control WrapCell(Control c) { c.Margin = new Thickness(0, 0, 10, 6); c.VerticalAlignment = VerticalAlignment.Center; return c; }

        private string OutputsText(BindingDef d)
        {
            if (d.IsAnalog) return "(analog)";
            if (d.Outputs.Count == 0) return d.Layer > 0 ? "un-map this input" : "no output";
            ControllerLayoutDef layout = ActiveLayout();
            return string.Join(" + ", d.Outputs.Select(o => o.Kind == BindingOutputKind.PadButton
                ? layout.Label(o.Name) + (o.IsTrigger && o.Magnitude < 100 ? $" {o.Magnitude}%" : "")
                : o.IsLayer ? "Layer " + o.Name
                : InputChoices.DisplayName(o.Name))) + (d.TurboHz > 0 ? $" (turbo {d.TurboHz}/s)" : "");
        }

        // ── Details: timing and outputs ─────────────────────────────────────────

        private Control BuildBindingDetails(BindingDef d)
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(new TextBlock { Text = BindingConditions.Description(d.Condition), Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

            if (!d.IsAnalog) panel.Children.Add(TimeBox("Turbo (pulses per second, 0 = off)", d.TurboHz, 0, BindingDef.MaxTurboHz, v => d.With(turboHz: v), 1));
            if (d.Condition == BindingCondition.Double) panel.Children.Add(TimeBox("Double press window (ms)", d.DoubleWindowMs, BindingEntries.MinDoubleMs, BindingEntries.MaxDoubleMs, v => d.With(doubleWindowMs: v)));
            if (d.Condition == BindingCondition.Tap) panel.Children.Add(TimeBox("Tap: release within (ms)", d.TapMaxMs, BindingEntries.MinTapMs, BindingEntries.MaxTapMs, v => d.With(tapMaxMs: v)));
            if (d.Condition == BindingCondition.Hold) panel.Children.Add(TimeBox("Hold time (ms)", d.HoldMs, BindingEntries.MinHoldMs, BindingEntries.MaxHoldMs, v => d.With(holdMs: v)));

            if (!d.IsAnalog)
            {
                panel.Children.Add(new TextBlock { Text = "OUTPUTS", Classes = { "tilelab" } });
                for (int i = 0; i < d.Outputs.Count; i++) panel.Children.Add(OutputRow(d, i));
                var add = new Button { Content = "Add output", Classes = { "soft" }, HorizontalAlignment = HorizontalAlignment.Left };
                add.IsEnabled = d.Outputs.Count < BindingEntries.MaxOutputs;
                add.Click += (_, _) =>
                {
                    var list = d.Outputs.ToList();
                    list.Add(new BindingOutput { Kind = BindingOutputKind.PadButton, Name = ControllerEngine.ActionBCircle });
                    ReplaceBinding(d.With(outputs: list));
                    BindingsChanged(rebuild: true);
                };
                panel.Children.Add(add);
            }
            return panel;
        }

        private Control TimeBox(string label, int value, int min, int max, Func<int, BindingDef> make, int increment = 10)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 190 });
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = increment, FormatString = "0", MinWidth = 130 };
            box.ValueChanged += (_, _) =>
            {
                if (rebuildingBindings || box.Value == null || (int)box.Value.Value == value) return;
                BindingDef updated = make((int)box.Value.Value);
                ReplaceBinding(updated);
                BindingsChanged(rebuild: false);
            };
            row.Children.Add(box);
            return row;
        }

        private static List<ComboBoxItem> LayerItems() => new()
        {
            ChoiceItem("Base layer", "0"), ChoiceItem("Layer 1", "1"), ChoiceItem("Layer 2", "2"), ChoiceItem("Layer 3", "3")
        };

        private static readonly (BindingOutputKind Kind, string Title)[] OutputKinds =
        {
            (BindingOutputKind.PadButton, "Pad button"), (BindingOutputKind.Key, "Keyboard key"), (BindingOutputKind.MouseButton, "Mouse button"),
            (BindingOutputKind.Layer, "Layer (shift)")
        };

        private Control OutputRow(BindingDef d, int index)
        {
            BindingOutput o = d.Outputs[index];
            var row = new WrapPanel();
            var kind = new ComboBox { Width = 140, ItemsSource = OutputKinds.Select(k => ChoiceItem(k.Title, k.Kind.ToString())).ToList() };
            kind.SelectedItem = ((List<ComboBoxItem>)kind.ItemsSource).First(i => (string)i.Tag == o.Kind.ToString());
            kind.SelectionChanged += (_, _) =>
            {
                if (rebuildingBindings || kind.SelectedItem is not ComboBoxItem it || !Enum.TryParse(it.Tag as string, out BindingOutputKind k) || k == o.Kind) return;
                string first = OutputNames(k).First();
                SetOutput(d, index, new BindingOutput { Kind = k, Name = first });
            };
            row.Children.Add(WrapCell(kind));

            var name = new ComboBox { Width = 190, ItemsSource = OutputNames(o.Kind).Select(n => ChoiceItem(OutputLabel(o.Kind, n), n)).ToList() };
            name.SelectedItem = ((List<ComboBoxItem>)name.ItemsSource).FirstOrDefault(i => string.Equals((string)i.Tag, o.Name, StringComparison.OrdinalIgnoreCase));
            name.SelectionChanged += (_, _) =>
            {
                if (rebuildingBindings || name.SelectedItem is not ComboBoxItem it || it.Tag is not string n || n == o.Name) return;
                if ((o.Kind == BindingOutputKind.Key || o.Kind == BindingOutputKind.MouseButton) && string.Equals(n, d.Input, StringComparison.OrdinalIgnoreCase))
                { statusLabel.Text = "Status: An output cannot press the binding's own input"; Dispatcher.UIThread.Post(RebuildBindingList); return; }
                SetOutput(d, index, new BindingOutput { Kind = o.Kind, Name = n, Magnitude = o.Magnitude });
            };
            row.Children.Add(WrapCell(name));

            if (o.IsTrigger)
            {
                var mag = new NumericUpDown { Minimum = 1, Maximum = 100, Value = o.Magnitude, Increment = 5, FormatString = "0", MinWidth = 120 };
                ToolTip.SetTip(mag, "How far the trigger is pulled, in percent.");
                mag.ValueChanged += (_, _) =>
                {
                    if (rebuildingBindings || mag.Value == null || (int)mag.Value.Value == o.Magnitude) return;
                    var list = d.Outputs.ToList();
                    list[index] = new BindingOutput { Kind = o.Kind, Name = o.Name, Magnitude = (int)mag.Value.Value };
                    ReplaceBinding(d.With(outputs: list));
                    BindingsChanged(rebuild: false);
                };
                row.Children.Add(WrapCell(mag));
                row.Children.Add(WrapCell(new TextBlock { Text = "%" }));
            }

            var remove = new Button { Classes = { "ghost", "icon" }, Content = new Icon { Kind = "x" } };
            ToolTip.SetTip(remove, "Remove this output.");
            remove.Click += (_, _) =>
            {
                var list = d.Outputs.ToList();
                list.RemoveAt(index);
                ReplaceBinding(d.With(outputs: list));
                BindingsChanged(rebuild: true);
            };
            row.Children.Add(WrapCell(remove));
            return row;
        }

        private void SetOutput(BindingDef d, int index, BindingOutput replacement)
        {
            var list = d.Outputs.ToList();
            if (list.Where((x, i) => i != index).Any(x => x.Key == replacement.Key)) { statusLabel.Text = "Status: That output is already in this binding"; Dispatcher.UIThread.Post(RebuildBindingList); return; }
            list[index] = replacement;
            ReplaceBinding(d.With(outputs: list, layer: list.Any(x => x.IsLayer) ? (int?)0 : null));   // only a base-layer binding can switch a layer on
            BindingsChanged(rebuild: true);
        }

        private static IEnumerable<string> OutputNames(BindingOutputKind kind) => kind switch
        {
            BindingOutputKind.PadButton => PaddleMappings.PadButtonTargets,
            BindingOutputKind.Key => InputChoices.Keyboard,
            BindingOutputKind.Layer => new[] { "1", "2", "3" },
            _ => InputChoices.MouseButtons
        };

        private string OutputLabel(BindingOutputKind kind, string name) =>
            kind == BindingOutputKind.PadButton ? ActiveLayout().Label(name)
            : kind == BindingOutputKind.Layer ? "Layer " + name
            : InputChoices.DisplayName(name);

        // ── Live state ──────────────────────────────────────────────────────────

        private static string StateText(BindingState s) => s switch
        {
            BindingState.Disabled => "Disabled",
            BindingState.Running => "Running",
            BindingState.ToggledActive => "Toggled active",
            BindingState.WaitingHold => "Waiting for hold",
            BindingState.WaitingDouble => "Waiting for double press",
            BindingState.OtherLayer => "On another layer",
            _ => "Waiting for input"
        };

        /// <summary>Paints each row from the engine's own snapshot. A row is highlighted only while its binding is actually executing.</summary>
        private void UpdateBindingStatus()
        {
            if (bindingRows.Count == 0) { if (bindingsSummary != null) bindingsSummary.Text = ""; return; }
            bool live = liveOverrideForTests ?? engine.IsConnected;
            BindingStatus[] snap = engine.Bindings.Snapshot();
            int running = 0;
            foreach (BindingDef d in advancedBindings)
            {
                if (!bindingRows.TryGetValue(d.Id, out BindingRowView row)) continue;
                BindingStatus s = default;
                bool found = false;
                for (int i = 0; i < snap.Length; i++) if (snap[i].Id == d.Id) { s = snap[i]; found = true; break; }

                bool isRunning = live && found && s.Running;
                bool latched = isRunning && s.Latched;
                if (isRunning) running++;
                row.Card.Classes.Set("running", isRunning && !latched);
                row.Card.Classes.Set("latched", latched);
                row.Status.Text = !d.Enabled ? "Disabled"
                    : !live ? "Not connected"
                    : found ? StateText(s.State) : "Waiting for input";
                bool down = live && found && s.PhysicalDown;
                row.Physical.IsVisible = latched && !down;
                row.Physical.Text = latched ? "Latched on: it keeps running until the next activation." : "";
            }
            if (bindingsSummary != null)
                bindingsSummary.Text = live
                    ? $"{running} of {advancedBindings.Count(b => b.Enabled)} enabled running now · active layer: {(engine.Bindings.ActiveLayer == 0 ? "base" : engine.Bindings.ActiveLayer.ToString())}"
                    : "Connect the virtual controller to run bindings.";
        }

        private void RefreshAutomaticBindingsText()
        {
            if (bindingsAutoText == null) return;
            var lines = new List<string>();
            foreach (var pair in bindingControls)
            {
                string key = ChoiceValue(pair.Value.KeySelector);
                if (key == InputChoices.None) continue;
                if (advancedBindings.Any(b => b.Enabled && !b.IsAnalog && string.Equals(b.Input, key, StringComparison.OrdinalIgnoreCase)))
                    lines.Add($"{ActionTitle(pair.Key)} ({InputChoices.DisplayName(key)}) is off while a binding uses {InputChoices.DisplayName(key)}");
            }
            bindingsAutoText.Text = lines.Count == 0
                ? "Every automatic mapping is active. A binding on the same input turns that mapping off, so one input never does both."
                : string.Join("\n", lines);
        }

        // ── Test hooks ──────────────────────────────────────────────────────────

        private bool? liveOverrideForTests;
        internal void SetLiveForTests(bool? live) => liveOverrideForTests = live;
        internal BindingRuntime BindingRuntimeForTests => engine.Bindings;
        internal IReadOnlyList<BindingDef> AdvancedBindingsForTests => advancedBindings;
        internal void AddBindingForTests(BindingDef def) { advancedBindings.Add(def); BindingsChanged(rebuild: false); RebuildBindingList(); }
        internal bool RowHasClassForTests(string id, string cls) => bindingRows.TryGetValue(id, out BindingRowView r) && r.Card.Classes.Contains(cls);
        internal string RowStatusForTests(string id) => bindingRows.TryGetValue(id, out BindingRowView r) ? r.Status.Text : null;
        internal void RefreshBindingStatusForTests() => UpdateBindingStatus();
    }
}
