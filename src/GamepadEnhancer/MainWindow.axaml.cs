using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    public partial class MainWindow : Window
    {
        private readonly string profilesDirectory = Path.Combine(AppContext.BaseDirectory, "profiles");
        private readonly string patternsDirectory = Path.Combine(AppContext.BaseDirectory, "patterns");

        private readonly Dictionary<string, BindingControlSet> bindingControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CheckBox> disableKeyChecks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<RecoilStep> recoilSteps = new();
        private readonly RecoilPreview recoilPreview = new();
        private readonly ControllerEngine engine = new();
        private InputStateManager inputManager;
        private RawInputService mouseMoveSource;
        private readonly BlockPolicy blockPolicy = new();
        private readonly ForegroundWindow foregroundWindow = new();
        private Avalonia.Threading.DispatcherTimer blockTimer;
        private List<string> blockProcessList = new();
        private bool blockToggledOn = true;
        private bool suppressBlockChange;
        private AppSettings appSettings;
        private NetworkApiServer networkApi;
        private bool suppressNetworkChange;
        private bool suppressInputMethodChange;
        private MonitorPanel monitorPanel;
        private MonitorWindow monitorWindow;
        private bool suppressMonitorChange;

        private bool ready;
        private bool syncingDrift;
        private bool syncingJitter;
        private bool syncingRecoil;
        private bool suppressPatternAutoLoad;
        private bool wheelDisengageEnabled = true;

        private sealed class BindingControlSet
        {
            public ComboBox KeySelector { get; init; }
            public CheckBox TurboToggle { get; init; }
            public NumericUpDown TurboHzBox { get; init; }
        }

        public MainWindow()
        {
            InitializeComponent();

            controllerTypeBox.ItemsSource = new[] { "Xbox 360", "PS4" };
            controllerTypeBox.SelectedItem = "PS4";
            pullDirectionBox.ItemsSource = PullDirections.All;
            pullDirectionBox.SelectedItem = "Down";
            recoilTriggerKeyBox.ItemsSource = new[] { "Left", "Right", "Middle", "Off" };
            recoilTriggerKeyBox.SelectedItem = "Left";
            recoilTriggerModeBox.ItemsSource = new[] { "Hold", "Toggle" };
            recoilTriggerModeBox.SelectedItem = "Hold";
            jitterAlgorithmBox.ItemsSource = AlgorithmLabels;
            jitterAlgorithmBox.SelectedItem = AlgorithmLabels[0];
            ApplyJitterAlgorithmText();
            jitterButtonBox.ItemsSource = new[] { "Off", "Left", "Right", "Middle" };
            jitterButtonBox.SelectedItem = "Off";
            jitterModeBox.ItemsSource = new[] { "Hold", "Toggle" };
            jitterModeBox.SelectedItem = "Hold";
            jitterRateBox.ItemsSource = RateLabels;
            jitterRateBox.SelectedItem = RateLabels[0];
            recoilRadiusModeBox.ItemsSource = RadiusModeLabels;
            recoilRadiusModeBox.SelectedItem = RadiusModeLabels[0];

            foreach (string k in new[] { "Tab", "E", "F", "Q", "R", "Shift", "Ctrl", "Alt", "Space", "C", "V" })
            {
                var cb = new CheckBox { Content = k, IsChecked = k == "Tab" || k == "E", MinWidth = 90, Margin = new Thickness(0, 0, 8, 4) };
                cb.IsCheckedChanged += AnySettingChangedChecked;
                disableKeyChecks[k] = cb;
                disableKeysPanel.Children.Add(cb);
            }

            foreach (string action in ControllerEngine.BindingActions)
                bindingsGrid.Children.Add(CreateBindingSelector(action));

            previewHost.Child = recoilPreview;
            recoilStepsGrid.ItemsSource = recoilSteps;

            Directory.CreateDirectory(profilesDirectory);
            DefaultProfile.EnsureFile(profilesDirectory);   // first run: write the shipped Default.json into profiles/
            Directory.CreateDirectory(patternsDirectory);

            engine.StatusChanged += text => Dispatcher.UIThread.Post(() => statusLabel.Text = text);
            engine.Faulted += ex => Dispatcher.UIThread.Post(async () =>
            {
                SetConnectedUi(false);
                statusLabel.Text = "Status: Update failed";
                await Dialogs.ShowMessageAsync(this, "Update Error", "Failed while updating the virtual controller.\n\n" + ex.Message);
            });

            InitializeInputHook();
            InitializeBlocking();
            InitializeNetworkApi();
            InitializeMonitor();
            ApplyDefaults();
            LoadProfileList();
            LoadPatternList();

            ready = true;
            controllerTypeBox.SelectionChanged += ControllerTypeBox_SelectionChanged;
            UpdateActiveControllerLabel();
            mainTabs.SelectionChanged += (_, _) => UpdateMonitorRunning();
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateMonitorRunning(); };
            UpdateMonitorRunning();
            SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);
            Opened += (_, _) => UpdateResponsiveLayout(Bounds.Width);
            PushSettings();
        }

        // ── Responsive layout ────────────────────────────────────────────────────

        private void UpdateResponsiveLayout(double windowWidth)
        {
            double w = Math.Max(0, windowWidth - 48);
            LayoutControllerCards(w >= 840);
            recoilEditorGrid.Columns = Math.Clamp((int)(w / 460), 1, 2);
            bindingsGrid.Columns = Math.Clamp((int)(w / 380), 1, 4);
            ApplyGutters(recoilEditorGrid, 0);
        }

        /// <summary>
        /// Wide: drift | RS filtering | disengage in one row (drift and filtering share the left half).
        /// Narrow: drift | RS filtering on top, disengage full width below.
        /// </summary>
        private void LayoutControllerCards(bool wide)
        {
            controllerGrid.ColumnDefinitions.Clear();
            controllerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            controllerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            if (wide) controllerGrid.ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star));

            Grid.SetRow(driftCard, 0); Grid.SetColumn(driftCard, 0); Grid.SetColumnSpan(driftCard, 1);
            Grid.SetRow(jitterCard, 0); Grid.SetColumn(jitterCard, 1); Grid.SetColumnSpan(jitterCard, 1);
            Grid.SetRow(disengageCard, wide ? 0 : 1); Grid.SetColumn(disengageCard, wide ? 2 : 0);
            Grid.SetColumnSpan(disengageCard, wide ? 1 : 2);

            driftCard.Margin = new Thickness(0, 0, 6, 12);
            jitterCard.Margin = new Thickness(6, 0, wide ? 6 : 0, 12);
            disengageCard.Margin = new Thickness(wide ? 6 : 0, 0, 0, 12);
        }

        /// <summary>Gives side-by-side cards a gutter between them; single-column cards just stack.</summary>
        private static void ApplyGutters(UniformGrid grid, double bottom)
        {
            bool single = grid.Columns <= 1;
            for (int i = 0; i < grid.Children.Count; i++)
            {
                double left = single || i % grid.Columns == 0 ? 0 : 6;
                double right = single || i % grid.Columns == grid.Columns - 1 ? 0 : 6;
                grid.Children[i].Margin = new Thickness(left, 0, right, single && i < grid.Children.Count - 1 ? 12 : bottom);
            }
        }

        // ── Binding rows ─────────────────────────────────────────────────────────

        private static string[] GetKeyChoices()
        {
            var keys = new List<string>
            {
                "None", "Shift", "Ctrl", "Alt", "Space", "Tab", "Enter", "Escape", "Backspace",
                "Up", "Down", "Left", "Right"
            };
            keys.AddRange(Enumerable.Range(0, 10).Select(i => i.ToString(CultureInfo.InvariantCulture)));
            for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
            for (int i = 1; i <= 12; i++) keys.Add("F" + i.ToString(CultureInfo.InvariantCulture));
            keys.AddRange(MouseInputNames.All);   // mouse buttons and wheel can be bound to pad actions too
            return keys.ToArray();
        }

        private Control CreateBindingSelector(string actionName)
        {
            var combo = new ComboBox { ItemsSource = GetKeyChoices(), SelectedItem = "None", MinWidth = 100, Margin = new Thickness(0, 0, 10, 4) };
            var turbo = new CheckBox { Content = "Turbo", Margin = new Thickness(0, 0, 8, 4) };
            var hz = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 12, Increment = 1, FormatString = "0", MinWidth = 100, Margin = new Thickness(0, 0, 6, 4) };
            combo.SelectionChanged += AnySettingChanged;
            turbo.IsCheckedChanged += AnySettingChangedChecked;
            hz.ValueChanged += AnySettingChangedNumeric;

            bindingControls[actionName] = new BindingControlSet { KeySelector = combo, TurboToggle = turbo, TurboHzBox = hz };

            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(combo);
            row.Children.Add(turbo);
            row.Children.Add(hz);
            row.Children.Add(new TextBlock { Text = "Hz", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = actionName, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            panel.Children.Add(row);

            var card = new Border { Classes = { "card" }, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10), Child = panel };
            return card;
        }

        // ── Settings snapshot → engine ───────────────────────────────────────────

        private void AnySettingChanged(object sender, SelectionChangedEventArgs e) => PushSettings();
        private void AnySettingChangedChecked(object sender, RoutedEventArgs e) => PushSettings();
        private void AnySettingChangedNumeric(object sender, NumericUpDownValueChangedEventArgs e) => PushSettings();

        private void PushSettings()
        {
            if (!ready) return;
            var bindings = new Dictionary<string, BindingSetting>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in bindingControls)
            {
                bindings[pair.Key] = new BindingSetting
                {
                    Key = pair.Value.KeySelector.SelectedItem as string ?? "None",
                    Turbo = pair.Value.TurboToggle.IsChecked == true,
                    TurboHz = (int)(pair.Value.TurboHzBox.Value ?? 12)
                };
            }

            engine.Settings = new EngineSettings
            {
                DriftValue = (int)Math.Round(driftSlider.Value),
                JitterValue = (int)Math.Round(jitterSlider.Value),
                JitterAlgorithm = AlgorithmToStored(jitterAlgorithmBox.SelectedItem as string),
                JitterTriggerKey = TriggerToStored(jitterButtonBox.SelectedItem as string),
                JitterToggleMode = jitterModeBox.SelectedItem as string == "Toggle",
                FilterPeriodMs = RateToMs(jitterRateBox.SelectedItem as string),
                PullDirection = PullDirections.Normalize(pullDirectionBox.SelectedItem as string),
                RecoilEnabled = recoilEnabledCheckBox.IsChecked == true,
                RecoilLoop = recoilLoopCheckBox.IsChecked == true,
                RecoilTriggerKey = TriggerToStored(recoilTriggerKeyBox.SelectedItem as string),
                RecoilToggleMode = recoilTriggerModeBox.SelectedItem as string == "Toggle",
                RecoilStartDelayMs = (int)(recoilStartDelayBox.Value ?? 0),
                RecoilCenterRadius = (int)(recoilCenterRadiusBox.Value ?? 0),
                RecoilCenterRadiusMode = RadiusModeToStored(recoilRadiusModeBox.SelectedItem as string),
                RecoilSteps = recoilSteps.Select(s => new RecoilStep { X = s.X, Y = s.Y, IntervalMs = s.IntervalMs }).ToArray(),
                WheelDisengageEnabled = wheelDisengageEnabled,
                DisengageMs = (int)(scrollDisengageBox.Value ?? 0),
                MouseLookEnabled = mouseLookBox.IsChecked == true,
                MouseSensitivityX = (double)(mouseSensXBox.Value ?? 8m),
                MouseSensitivityY = (double)(mouseSensYBox.Value ?? 8m),
                MouseCurve = (double)(mouseCurveBox.Value ?? 1m),
                MouseGameDeadZonePercent = (int)(mouseDeadZoneBox.Value ?? 0m),
                MouseReleaseMs = (int)(mouseReleaseBox.Value ?? 30m),
                MouseInvertY = mouseInvertYBox.IsChecked == true,
                DisableKeys = new HashSet<string>(
                    disableKeyChecks.Where(p => p.Value.IsChecked == true).Select(p => p.Key),
                    StringComparer.OrdinalIgnoreCase),
                Bindings = bindings
            };

            UpdateMouseMoveSource(mouseLookBox.IsChecked == true);
            RefreshBlockPolicy();
        }

        /// <summary>Starts or stops the movement-only Raw Input source that feeds mouse-look.</summary>
        private void UpdateMouseMoveSource(bool wanted)
        {
            if (wanted && mouseMoveSource == null && inputManager != null)
            {
                var source = new RawInputService(inputManager, movementOnly: true);
                source.Failed += message => Dispatcher.UIThread.Post(() => statusLabel.Text = "Status: Mouse look stopped - " + message);
                if (source.Start(out string error)) mouseMoveSource = source;
                else
                {
                    source.Dispose();
                    statusLabel.Text = "Status: Mouse look unavailable - " + error;
                }
            }
            else if (!wanted && mouseMoveSource != null)
            {
                try { mouseMoveSource.Dispose(); } catch { }
                mouseMoveSource = null;
            }
        }

        // ── Input hooks ──────────────────────────────────────────────────────────

        private void InitializeInputHook()
        {
            appSettings = AppSettings.Load();
            inputManager = new InputStateManager(engine, (method, receiver) =>
                method == InputCaptureMethod.RawInput
                    ? new RawInputService(receiver)
                    : CreateHookCapture(receiver));
            engine.BlockedInputProbe = name => blockPolicy.Settings.Active && blockPolicy.Settings.BlockedMouseInputs.Contains(name);
            inputManager.Faulted += (method, message) => Dispatcher.UIThread.Post(() => OnCaptureFaulted(method, message));

            inputMethodBox.ItemsSource = InputCaptureMethods.Labels;
            InputCaptureMethod saved = InputCaptureMethods.FromStored(appSettings.InputCaptureMethod);
            if (!TryActivateInputMethod(saved) && saved != InputCaptureMethod.LowLevelHooks)
            {
                // The saved method is unavailable (for example Raw Input failed to start): fall back to the hooks.
                string reason = statusLabel.Text;
                TryActivateInputMethod(InputCaptureMethod.LowLevelHooks);
                statusLabel.Text = reason + "; using Low-Level Hooks";
            }
        }

        /// <summary>Activates a method, updates the dropdown and status text. Returns false (and keeps the old method) on failure.</summary>
        private bool TryActivateInputMethod(InputCaptureMethod method)
        {
            bool ok = inputManager.SetMethod(method, out string error);
            InputCaptureMethod shown = inputManager.ActiveMethod ?? InputCaptureMethod.LowLevelHooks;

            suppressInputMethodChange = true;
            inputMethodBox.SelectedItem = InputCaptureMethods.ToLabel(shown);
            suppressInputMethodChange = false;

            statusLabel.Text = ok
                ? $"Status: Input capture: {InputCaptureMethods.ToLabel(method)}"
                : $"Status: Input capture '{InputCaptureMethods.ToLabel(method)}' unavailable - {error}";
            return ok;
        }

        private void InputMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressInputMethodChange || inputManager == null) return;
            InputCaptureMethod method = InputCaptureMethods.FromLabel(inputMethodBox.SelectedItem as string);
            if (method == InputCaptureMethod.RawInput && appSettings.BlockEnabled)
            {
                // Raw Input can't swallow input; keep the hooks while blocking is on.
                suppressInputMethodChange = true;
                inputMethodBox.SelectedItem = InputCaptureMethods.ToLabel(InputCaptureMethod.LowLevelHooks);
                suppressInputMethodChange = false;
                statusLabel.Text = "Status: Input blocking needs Low-Level Hooks; turn blocking off to use Raw Input";
                return;
            }
            if (!TryActivateInputMethod(method)) return;
            appSettings.InputCaptureMethod = InputCaptureMethods.ToStored(method);
            if (!appSettings.Save()) statusLabel.Text += " (settings.json could not be saved)";
        }

        private LowLevelHookCapture CreateHookCapture(IInputEventReceiver receiver)
        {
            var capture = new LowLevelHookCapture(receiver, blockPolicy);
            capture.HotkeyPressed += OnBlockHotkey;   // raised on the hook thread = the UI thread
            return capture;
        }

        // ── Input blocking ───────────────────────────────────────────────────────

        private void InitializeBlocking()
        {
            suppressBlockChange = true;
            blockEnabledBox.IsChecked = appSettings.BlockEnabled;
            blockKeyboardBox.IsChecked = appSettings.BlockKeyboard;
            blockMouseBox.IsChecked = appSettings.BlockMouse;
            blockProcessesBox.Text = appSettings.BlockProcesses ?? "";
            blockHotkeyBox.ItemsSource = BlockHotkeys.Names;
            blockHotkeyBox.SelectedItem = BlockHotkeys.FromVk(appSettings.BlockHotkeyVk);
            suppressBlockChange = false;
            blockProcessList = ProcessMatcher.ParseList(appSettings.BlockProcesses);
            RefreshRunningPrograms();

            blockTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            blockTimer.Tick += (_, _) => RefreshBlockPolicy();
            blockTimer.Start();
        }

        /// <summary>
        /// Recomputes whether blocking is active (enabled, toggled on, pad connected, hooks in use, a listed program focused)
        /// and publishes the blocked set to the hook. Cheap; runs on a timer and whenever a setting changes.
        /// </summary>
        private void RefreshBlockPolicy()
        {
            if (inputManager == null) return;
            bool hooks = inputManager.ActiveMethod == InputCaptureMethod.LowLevelHooks;
            string focused = null;
            bool match = false;
            if (foregroundWindow.TryGet(out int pid, out string name) && pid != Environment.ProcessId)
            {
                focused = name;
                match = ProcessMatcher.Matches(name, blockProcessList);
            }

            bool active = appSettings.BlockEnabled && blockToggledOn && engine.IsConnected && hooks && match;
            blockPolicy.Update(BlockSettings.From(engine.Settings, active, appSettings.BlockHotkeyVk,
                appSettings.BlockKeyboard, appSettings.BlockMouse));
            blockRestoreButton.IsEnabled = appSettings.BlockEnabled;
            blockRestoreButton.Content = blockToggledOn ? "Restore input now" : "Resume blocking";

            string text;
            if (!appSettings.BlockEnabled) text = "Input blocking is off";
            else if (!hooks) text = "Input blocking needs the Low-Level Hooks capture method";
            else if (!engine.IsConnected) text = "Input blocking waits for the controller to be connected";
            else if (!blockToggledOn) text = $"Input blocking is paused ({BlockHotkeys.FromVk(appSettings.BlockHotkeyVk)} to resume)";
            else if (blockProcessList.Count == 0) text = "Add a program to block input for";
            else if (!appSettings.BlockKeyboard && !appSettings.BlockMouse) text = "Nothing to block: both keyboard and mouse blocking are off";
            else if (active) text = $"Input blocked ({BlockedDevicesText()}): {focused} ({BlockHotkeys.FromVk(appSettings.BlockHotkeyVk)} pauses)";
            else text = $"Input blocking is ready: waiting for {string.Join(", ", blockProcessList)}";
            if (blockStatusLabel.Text != text) blockStatusLabel.Text = text;
        }

        private string BlockedDevicesText() =>
            appSettings.BlockKeyboard && appSettings.BlockMouse ? "keyboard and mouse" : appSettings.BlockKeyboard ? "keyboard" : "mouse";

        /// <summary>Emergency release: stops swallowing input immediately (same as the hotkey); pressing it again resumes.</summary>
        private void BlockRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!appSettings.BlockEnabled) return;
            blockToggledOn = !blockToggledOn;
            RefreshBlockPolicy();
        }

        private void OnBlockHotkey()
        {
            if (!ready || !appSettings.BlockEnabled) return;
            blockToggledOn = !blockToggledOn;
            RefreshBlockPolicy();
        }

        private void SaveBlockSettings()
        {
            appSettings.BlockEnabled = blockEnabledBox.IsChecked == true;
            appSettings.BlockKeyboard = blockKeyboardBox.IsChecked != false;
            appSettings.BlockMouse = blockMouseBox.IsChecked != false;
            appSettings.BlockProcesses = (blockProcessesBox.Text ?? "").Trim();
            appSettings.BlockHotkeyVk = BlockHotkeys.ToVk(blockHotkeyBox.SelectedItem as string);
            blockProcessList = ProcessMatcher.ParseList(appSettings.BlockProcesses);
            appSettings.Save();
            blockToggledOn = true;

            // Blocking needs the hook backend: switch away from Raw Input when it is turned on.
            if (appSettings.BlockEnabled && inputManager?.ActiveMethod == InputCaptureMethod.RawInput)
            {
                if (TryActivateInputMethod(InputCaptureMethod.LowLevelHooks))
                {
                    appSettings.InputCaptureMethod = InputCaptureMethods.ToStored(InputCaptureMethod.LowLevelHooks);
                    appSettings.Save();
                }
            }
            RefreshBlockPolicy();
        }

        private void BlockSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressBlockChange) return;
            SaveBlockSettings();
        }

        private void BlockSettings_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressBlockChange) return;
            SaveBlockSettings();
        }

        private void BlockHotkeyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressBlockChange) return;
            SaveBlockSettings();
        }

        private void RefreshRunningPrograms()
        {
            try
            {
                blockRunningBox.ItemsSource = Process.GetProcesses()
                    .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                    .Select(p => { try { return p.ProcessName; } catch { return null; } })
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { /* the list is only a convenience */ }
        }

        private void BlockRefresh_Click(object sender, RoutedEventArgs e) => RefreshRunningPrograms();

        private void BlockAdd_Click(object sender, RoutedEventArgs e)
        {
            if (blockRunningBox.SelectedItem is not string name) return;
            var list = ProcessMatcher.ParseList(blockProcessesBox.Text);
            string n = ProcessMatcher.Normalize(name);
            if (!list.Contains(n)) list.Add(n);
            blockProcessesBox.Text = string.Join(", ", list);
            SaveBlockSettings();
        }

        private void OnCaptureFaulted(InputCaptureMethod method, string message)
        {
            // The failed backend already had its held inputs released; recover with the always-available hooks.
            TryActivateInputMethod(InputCaptureMethod.LowLevelHooks);
            statusLabel.Text = $"Status: {InputCaptureMethods.ToLabel(method)} failed ({message}); switched to Low-Level Hooks";
        }

        // ── Network API ──────────────────────────────────────────────────────────

        private void InitializeNetworkApi()
        {
            networkApi = new NetworkApiServer(engine.Remote, () => appSettings.NetworkApiToken, () => engine.IsConnected);
            networkApi.StatusChanged += text => Dispatcher.UIThread.Post(() => netStatusLabel.Text = text);

            suppressNetworkChange = true;
            netEnabledBox.IsChecked = appSettings.NetworkApiEnabled;
            netBindAllBox.IsChecked = appSettings.NetworkApiBindAll;
            netPortBox.Text = appSettings.NetworkApiPort.ToString();
            netTokenBox.Text = appSettings.NetworkApiToken;
            remoteSendBox.IsChecked = appSettings.RemoteSendEnabled;
            remoteSendMouseBox.IsChecked = appSettings.RemoteSendMouse;
            remoteHostBox.Text = appSettings.RemoteHost;
            remotePortBox.Text = appSettings.RemotePort.ToString();
            remoteTokenBox.Text = appSettings.RemoteToken;
            suppressNetworkChange = false;
            ApplyNetworkApi();
        }

        /// <summary>Reads the Network tab into app settings, saves, and (re)starts or stops the server.</summary>
        private void ApplyNetworkApi()
        {
            if (networkApi == null) return;
            bool enable = netEnabledBox.IsChecked == true;
            if (!int.TryParse(netPortBox.Text, out int port) || port < 1 || port > 65535) port = appSettings.NetworkApiPort;
            netPortBox.Text = port.ToString();

            if (enable && string.IsNullOrEmpty(appSettings.NetworkApiToken)) appSettings.NetworkApiToken = AppSettings.NewToken();
            appSettings.NetworkApiEnabled = enable;
            appSettings.NetworkApiPort = port;
            appSettings.NetworkApiBindAll = netBindAllBox.IsChecked == true;
            netTokenBox.Text = appSettings.NetworkApiToken;
            appSettings.Save();

            try
            {
                if (enable) networkApi.Start(port, appSettings.NetworkApiBindAll);
                else { networkApi.Stop(); netStatusLabel.Text = "Network API is off"; }
            }
            catch (Exception ex)
            {
                netStatusLabel.Text = "Network API could not start: " + ex.Message;
            }
        }

        private void NetworkSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressNetworkChange) return;
            ApplyNetworkApi();
        }

        private void NetworkSettings_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressNetworkChange) return;
            if (netEnabledBox.IsChecked == true || int.TryParse(netPortBox.Text, out _)) ApplyNetworkApi();
        }

        /// <summary>Reads the "Send to another PC" fields into app settings and saves them.</summary>
        private void SaveRemoteSettings()
        {
            appSettings.RemoteSendEnabled = remoteSendBox.IsChecked == true;
            appSettings.RemoteSendMouse = remoteSendMouseBox.IsChecked == true;
            appSettings.RemoteHost = (remoteHostBox.Text ?? "").Trim();
            appSettings.RemotePort = int.TryParse(remotePortBox.Text, out int p) && p >= 1 && p <= 65535 ? p : appSettings.RemotePort;
            appSettings.RemoteToken = (remoteTokenBox.Text ?? "").Trim();
            remotePortBox.Text = appSettings.RemotePort.ToString();
            appSettings.Save();
        }

        private void RemoteSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressNetworkChange) return;
            SaveRemoteSettings();
        }

        private void RemoteSettings_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressNetworkChange) return;
            SaveRemoteSettings();
        }

        private void NetShowToken_Changed(object sender, RoutedEventArgs e) =>
            netTokenBox.PasswordChar = netShowTokenBox.IsChecked == true ? '\0' : '*';

        private async void NetCopyToken_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(appSettings.NetworkApiToken)) { netStatusLabel.Text = "No token yet: enable the Network API first"; return; }
            try { await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(appSettings.NetworkApiToken) ?? System.Threading.Tasks.Task.CompletedTask); netStatusLabel.Text = "Token copied"; }
            catch { netStatusLabel.Text = "Could not copy to the clipboard"; }
        }

        private void NetNewToken_Click(object sender, RoutedEventArgs e)
        {
            appSettings.NetworkApiToken = AppSettings.NewToken();
            netTokenBox.Text = appSettings.NetworkApiToken;
            appSettings.Save();
            netStatusLabel.Text = "New token created: connected clients must reconnect with it";
            if (networkApi.IsRunning) ApplyNetworkApi();
        }

        // ── Connection ───────────────────────────────────────────────────────────

        private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectNowAsync();

        /// <summary>Creates the virtual pad for the type chosen in the dropdown (tearing down any previous one first).</summary>
        private async System.Threading.Tasks.Task ConnectNowAsync()
        {
            try
            {
                PushSettings();
                bool xbox360 = controllerTypeBox.SelectedItem as string == "Xbox 360";
                if (appSettings.RemoteSendEnabled)
                {
                    SaveRemoteSettings();
                    if (string.IsNullOrEmpty(appSettings.RemoteHost) || string.IsNullOrEmpty(appSettings.RemoteToken))
                        throw new InvalidOperationException("Enter the other PC's address and token on the Network tab first.");
                    engine.Connect(xbox360, new RemoteTarget(appSettings.RemoteHost, appSettings.RemotePort, appSettings.RemoteToken), appSettings.RemoteSendMouse);
                }
                else engine.Connect(xbox360);
                SetConnectedUi(true);
            }
            catch (Exception ex)
            {
                SetConnectedUi(false);
                await Dialogs.ShowMessageAsync(this, "Connection Error",
                    (appSettings.RemoteSendEnabled
                        ? "Failed to connect to the other PC.\n\nCheck its address and port, that its Network API is enabled and connected, the token, and the firewall.\n\n"
                        : "Failed to connect to ViGEm.\n\nMake sure ViGEmBus is installed.\n\n") + ex.Message);
            }
        }

        /// <summary>
        /// Changing the output type while connected replaces the virtual pad in place: the old one is disconnected and its
        /// ViGEm client disposed before the new one is created, so only the selected type is ever exposed. No restart needed.
        /// </summary>
        private async void ControllerTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready) return;
            bool wantXbox = controllerTypeBox.SelectedItem as string == "Xbox 360";
            if (engine.IsConnected && wantXbox != (engine.Family == PadFamily.Xbox360)) await ConnectNowAsync();
            else UpdateActiveControllerLabel();
        }

        /// <summary>Shows which virtual controller is exposed right now (or that none is) in the header and the window title.</summary>
        private void UpdateActiveControllerLabel()
        {
            string selected = PadLabels.FamilyName(controllerTypeBox.SelectedItem as string == "Xbox 360" ? PadFamily.Xbox360 : PadFamily.DualShock4);
            if (engine.IsConnected)
            {
                string name = PadLabels.FamilyName(engine.Family);
                activeControllerLabel.Text = engine.IsRemote ? $"Output: {name} (sent to the other PC)" : $"Output: {name} (ViGEm)";
                Title = "GamepadEnhancer - " + name;
            }
            else
            {
                activeControllerLabel.Text = $"Output: none (selected: {selected})";
                Title = "GamepadEnhancer";
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            engine.Disconnect();
            SetConnectedUi(false);
        }

        private void SetConnectedUi(bool connected)
        {
            connectButton.IsEnabled = !connected;
            disconnectButton.IsEnabled = connected;
            if (!connected) statusLabel.Text = "Status: Not connected";
            UpdateActiveControllerLabel();
            UpdateMonitorRunning();
        }

        // ── Drift / jitter slider ────────────────────────────────────────────────

        private string DriftText()
        {
            int d = (int)Math.Round(driftSlider.Value);
            return PullDirections.Normalize(pullDirectionBox.SelectedItem as string) switch
            {
                "Up" => $"Base upward drift: {d}%",
                "Left" => $"Base drift: {d}% to the left",
                "Right" => $"Base drift: {d}% to the right",
                _ => $"Base downward drift: {d}%"
            };
        }

        private static string DriftMoreText(string pull) => PullDirections.Normalize(pull) switch
        {
            "Up" => "More upward drift",
            "Left" or "Right" => "More drift",
            _ => "More downward drift"
        };

        private void PullDirectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready) return;
            driftMoreLabel.Text = DriftMoreText(pullDirectionBox.SelectedItem as string);
            OnDriftChanged();   // refreshes the status text and pushes the settings
        }

        private void DriftSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!ready || syncingDrift) return;
            OnDriftChanged();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) => SetDrift(0);

        private void SetDrift(int value)
        {
            value = Math.Clamp(value, StickMath.MinDrift, StickMath.MaxDrift);
            syncingDrift = true;
            driftSlider.Value = value;
            syncingDrift = false;
            OnDriftChanged();
        }

        private void OnDriftChanged()
        {
            valueLabel.Text = DriftText();
            PushSettings();
        }

        private void JitterSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!ready || syncingJitter) return;
            syncingJitter = true;
            jitterBox.Value = (int)Math.Round(jitterSlider.Value);
            syncingJitter = false;
            OnDriftChanged();
        }

        private void JitterBox_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!ready || syncingJitter) return;
            syncingJitter = true;
            jitterSlider.Value = Math.Clamp((double)(jitterBox.Value ?? 0), jitterSlider.Minimum, jitterSlider.Maximum);
            syncingJitter = false;
            OnDriftChanged();
        }

        private void JitterResetButton_Click(object sender, RoutedEventArgs e) => SetJitter(0);

        private void SetJitter(int value)
        {
            value = Math.Clamp(value, StickMath.MinJitter, StickMath.MaxJitter);
            syncingJitter = true;
            jitterSlider.Value = value;
            jitterBox.Value = value;
            syncingJitter = false;
            OnDriftChanged();
        }

        private void WheelDisengageToggle_Click(object sender, RoutedEventArgs e)
        {
            SetWheelDisengage(wheelDisengageToggle.IsChecked == true);
            PushSettings();
        }

        private void SetWheelDisengage(bool on)
        {
            wheelDisengageEnabled = on;
            wheelDisengageToggle.IsChecked = on;
            wheelDisengageToggle.Content = on ? "Wheel disengage: ON" : "Wheel disengage: OFF";
        }

        // ── Theme ────────────────────────────────────────────────────────────────

        private void DarkModeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool dark = darkModeToggle.IsChecked == true;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // ── Recoil steps ─────────────────────────────────────────────────────────

        private void ReplaceSteps(IEnumerable<RecoilStep> steps)
        {
            syncingRecoil = true;
            recoilSteps.Clear();
            foreach (RecoilStep s in steps) recoilSteps.Add(s);
            syncingRecoil = false;
            AfterStepsChanged(updateText: true);
        }

        private void AfterStepsChanged(bool updateText)
        {
            recoilPreview.SetSteps(recoilSteps.ToList());
            if (updateText)
            {
                syncingRecoil = true;
                recoilPatternTextBox.Text = RecoilPattern.ToText(recoilSteps);
                syncingRecoil = false;
            }
            PushSettings();
        }

        private void RecoilStepsGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
        {
            if (syncingRecoil) return;
            foreach (RecoilStep s in recoilSteps)
            {
                s.X = StickMath.ClampPercent(s.X);
                s.Y = StickMath.ClampPercent(s.Y);
                s.IntervalMs = Math.Max(0.1, s.IntervalMs);
            }
            AfterStepsChanged(updateText: true);
        }

        private void AddRecoilStepButton_Click(object sender, RoutedEventArgs e)
        {
            RecoilStep last = recoilSteps.Count > 0 ? recoilSteps[^1] : null;
            recoilSteps.Add(new RecoilStep { X = last?.X ?? 0, Y = last?.Y ?? 0, IntervalMs = last?.IntervalMs ?? 80 });
            AfterStepsChanged(updateText: true);
        }

        private void RemoveRecoilStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (recoilStepsGrid.SelectedItem is RecoilStep s)
            {
                recoilSteps.Remove(s);
                AfterStepsChanged(updateText: true);
            }
        }

        private void ClearRecoilButton_Click(object sender, RoutedEventArgs e) => ReplaceSteps(Array.Empty<RecoilStep>());
        private void VerticalPresetButton_Click(object sender, RoutedEventArgs e) => ReplaceSteps(RecoilPattern.VerticalPreset());
        private void CirclePresetButton_Click(object sender, RoutedEventArgs e) => ReplaceSteps(RecoilPattern.CirclePreset());

        private void RecoilPatternTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!ready || syncingRecoil) return;
            syncingRecoil = true;
            recoilSteps.Clear();
            foreach (RecoilStep s in RecoilPattern.ParseText(recoilPatternTextBox.Text)) recoilSteps.Add(s);
            syncingRecoil = false;
            AfterStepsChanged(updateText: false);
        }

        // Filtering algorithms (docs/AIM_TRIGGER.md): stored as "Oscillation" / "LowPass".
        private static readonly string[] AlgorithmLabels =
        {
            "RC-Style Micro-Oscillation",
            "Exponential Low-Pass Filter"
        };

        private static string AlgorithmToStored(string label) => label == AlgorithmLabels[1] ? "LowPass" : "Oscillation";
        private static string AlgorithmFromStored(string stored) => stored == "LowPass" ? AlgorithmLabels[1] : AlgorithmLabels[0];

        // Filter rate (docs/AIM_TRIGGER.md): how often the output runs while filtering is active. Experimental.
        private static readonly string[] RateLabels = { "2 ms (500/s)", "4 ms (250/s)", "8 ms (125/s)", "16 ms (62/s)" };
        private static readonly int[] RateMs = { 2, 4, 8, 16 };

        private static int RateToMs(string label)
        {
            int i = Array.IndexOf(RateLabels, label);
            return i >= 0 ? RateMs[i] : 2;
        }

        private static string RateFromMs(int ms)
        {
            int i = Array.IndexOf(RateMs, ms);
            return RateLabels[i >= 0 ? i : 0];
        }

        private const string ButtonHelp = " Off = always on; Left/Right/Middle = only while that button is held (Hold) or after pressing it (Toggle).";

        private void ApplyJitterAlgorithmText()
        {
            bool lowPass = AlgorithmToStored(jitterAlgorithmBox.SelectedItem as string) == "LowPass";
            jitterMoreLabel.Text = lowPass ? "More smoothing" : "More jitter";
            jitterNoteText.Text = (lowPass
                ? "The stick wiggles softly +/-N on the axis across the drift (Y for Left/Right, X for Up/Down; one rounded wave every 16 ms x N), and changes on X and Y are smoothed by an exponential low-pass with time constant 4 ms x N (-50 = 200 ms)."
                : "Swings the stick +/-N around its target hundreds of times per second, on the axis across the drift (Y for Left/Right, X for Up/Down; -10 on PS4: 118 / 138 around 128).")
                + ButtonHelp;
        }

        private void JitterAlgorithmBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready) return;
            ApplyJitterAlgorithmText();
            PushSettings();
        }

        // Center Radius modes (docs/CENTER_RADIUS.md): stored as "Lift" / "Shake" / "Circle".
        private static readonly string[] RadiusModeLabels =
        {
            "New: dead-zone lift",
            "Old: Shake wobble",
            "Old: Circle wobble"
        };

        private static string RadiusModeToStored(string label) => label switch
        {
            "Old: Shake wobble" => "Shake",
            "Old: Circle wobble" => "Circle",
            _ => "Lift"
        };

        private static string RadiusModeFromStored(string stored) => stored switch
        {
            "Shake" => RadiusModeLabels[1],
            "Circle" => RadiusModeLabels[2],
            _ => RadiusModeLabels[0]
        };

        // Profiles keep the original "Mouse Left" / "Mouse Right" / "None" strings so older builds can still read them.
        private static string TriggerToStored(string display) => display switch
        {
            "Left" => "Mouse Left",
            "Right" => "Mouse Right",
            "Middle" => "Mouse Middle",
            _ => "None"
        };

        // Keyboard triggers from older profiles are no longer offered and load as Off.
        private static string TriggerFromStored(string stored) => stored switch
        {
            "Mouse Left" => "Left",
            "Mouse Right" => "Right",
            "Mouse Middle" => "Middle",
            _ => "Off"
        };

        // ── Pattern files ────────────────────────────────────────────────────────

        private string GetPatternPath(string name) => Path.Combine(patternsDirectory, SafeName(name, "Pattern") + ".config");
        private string GetProfilePath(string name) => Path.Combine(profilesDirectory, SafeName(name, "Profile") + ".json");

        private static string SafeName(string name, string fallback)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = string.Concat(name.Where(ch => !invalid.Contains(ch))).Trim();
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }

        private void LoadPatternList(string selectedName = null)
        {
            suppressPatternAutoLoad = true;
            string[] names = Directory.EnumerateFiles(patternsDirectory, "*.config")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            recoilPatternComboBox.ItemsSource = names;
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                recoilPatternComboBox.SelectedItem = names.FirstOrDefault(n => string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase));
                patternNameBox.Text = selectedName;
            }
            else if (names.Length > 0)
            {
                recoilPatternComboBox.SelectedIndex = 0;
                patternNameBox.Text = names[0];
            }
            suppressPatternAutoLoad = false;
        }

        private async void SavePatternButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (patternNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Patterns", "Enter a pattern name first."); return; }
            File.WriteAllLines(GetPatternPath(name), RecoilPattern.ToFileLines(recoilSteps));
            LoadPatternList(name);
            statusLabel.Text = $"Status: Saved pattern '{name}' ({recoilSteps.Count} steps)";
        }

        private async void LoadPatternButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (patternNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Patterns", "Choose a pattern to load."); return; }
            string path = GetPatternPath(name);
            if (!File.Exists(path)) { await Dialogs.ShowMessageAsync(this, "Patterns", "That pattern does not exist yet."); return; }
            List<RecoilStep> loaded = RecoilPattern.ParseFileLines(File.ReadAllLines(path));
            if (loaded.Count == 0) { await Dialogs.ShowMessageAsync(this, "Patterns", "Pattern file is empty or invalid."); return; }
            ReplaceSteps(loaded);
            statusLabel.Text = $"Status: Loaded pattern '{name}' ({loaded.Count} steps)";
        }

        private async void DeletePatternButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (patternNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Patterns", "Choose a pattern to delete."); return; }
            string path = GetPatternPath(name);
            if (File.Exists(path)) File.Delete(path);
            LoadPatternList();
            statusLabel.Text = $"Status: Deleted pattern '{name}'";
        }

        private void RecoilPatternComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressPatternAutoLoad) return;
            string name = (recoilPatternComboBox.SelectedItem as string ?? string.Empty).Trim();
            if (name.Length == 0) return;
            patternNameBox.Text = name;
            string path = GetPatternPath(name);
            if (!File.Exists(path)) return;
            List<RecoilStep> loaded = RecoilPattern.ParseFileLines(File.ReadAllLines(path));
            if (loaded.Count == 0) return;
            ReplaceSteps(loaded);
            statusLabel.Text = $"Status: Loaded pattern '{name}' ({loaded.Count} steps)";
        }

        private void OpenPatternFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(patternsDirectory);
            try { Process.Start(new ProcessStartInfo(patternsDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { statusLabel.Text = "Status: Could not open folder - " + ex.Message; }
        }

        // ── Profiles ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Startup state: profiles/Default.json if it loads, otherwise the default profile shipped inside the app
        /// (see <see cref="DefaultProfile"/>), applied like a loaded profile.
        /// </summary>
        private void ApplyDefaults()
        {
            ApplyProfile(DefaultProfile.LoadStartupProfile(profilesDirectory));
            ApplyJitterAlgorithmText();   // the algorithm note/caption (the selection handler doesn't run before the window is ready)
        }

        private void LoadProfileList(string selectedName = null)
        {
            string[] names = Directory.EnumerateFiles(profilesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            profileComboBox.ItemsSource = names;
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                profileComboBox.SelectedItem = names.FirstOrDefault(n => string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase));
                profileNameBox.Text = selectedName;
            }
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (profileComboBox.SelectedItem is string name) profileNameBox.Text = name;
        }

        private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (profileNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Profiles", "Enter a profile name first."); return; }

            var profile = new InputProfile
            {
                Name = name,
                ControllerType = controllerTypeBox.SelectedItem as string ?? "PS4",
                BaseDriftPercent = (int)Math.Round(driftSlider.Value),
                RightStickJitter = (int)Math.Round(jitterSlider.Value),
                RightStickJitterAlgorithm = AlgorithmToStored(jitterAlgorithmBox.SelectedItem as string),
                RightStickJitterButton = TriggerToStored(jitterButtonBox.SelectedItem as string),
                RightStickJitterMode = jitterModeBox.SelectedItem as string ?? "Hold",
                RightStickJitterRateMs = RateToMs(jitterRateBox.SelectedItem as string),
                BasePullDirection = PullDirections.Normalize(pullDirectionBox.SelectedItem as string),
                ScrollDisengageMs = (int)(scrollDisengageBox.Value ?? 0),
                WheelDisengageEnabled = wheelDisengageEnabled,
                MouseLookEnabled = mouseLookBox.IsChecked == true,
                MouseSensitivityX = (double)(mouseSensXBox.Value ?? 8m),
                MouseSensitivityY = (double)(mouseSensYBox.Value ?? 8m),
                MouseCurve = (double)(mouseCurveBox.Value ?? 1m),
                MouseGameDeadZonePercent = (int)(mouseDeadZoneBox.Value ?? 0m),
                MouseReleaseMs = (int)(mouseReleaseBox.Value ?? 30m),
                MouseInvertY = mouseInvertYBox.IsChecked == true,
                RecoilEnabled = recoilEnabledCheckBox.IsChecked == true,
                RecoilLoop = recoilLoopCheckBox.IsChecked == true,
                RecoilTriggerKey = TriggerToStored(recoilTriggerKeyBox.SelectedItem as string),
                RecoilTriggerMode = recoilTriggerModeBox.SelectedItem as string ?? "Hold",
                RecoilStartDelayMs = (int)(recoilStartDelayBox.Value ?? 0),
                RecoilCenterRadiusPercent = (int)(recoilCenterRadiusBox.Value ?? 0),
                RecoilCenterRadiusMode = RadiusModeToStored(recoilRadiusModeBox.SelectedItem as string),
                RecoilPatternText = RecoilPattern.ToText(recoilSteps),
                DisableKeys = disableKeyChecks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(),
                KeyBindings = bindingControls.ToDictionary(p => p.Key, p => p.Value.KeySelector.SelectedItem as string ?? "None", StringComparer.OrdinalIgnoreCase),
                TurboEnabled = bindingControls.ToDictionary(p => p.Key, p => p.Value.TurboToggle.IsChecked == true, StringComparer.OrdinalIgnoreCase),
                TurboHz = bindingControls.ToDictionary(p => p.Key, p => (int)(p.Value.TurboHzBox.Value ?? 12), StringComparer.OrdinalIgnoreCase)
            };

            File.WriteAllText(GetProfilePath(name), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            LoadProfileList(name);
            statusLabel.Text = $"Status: Saved profile '{name}'";
        }

        private async void LoadProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (profileNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Profiles", "Choose or type a profile name."); return; }
            string path = GetProfilePath(name);
            if (!File.Exists(path)) { await Dialogs.ShowMessageAsync(this, "Profiles", "That profile does not exist yet."); return; }

            InputProfile profile;
            try { profile = JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(path)); }
            catch { profile = null; }
            if (profile == null) { await Dialogs.ShowMessageAsync(this, "Profiles", "Failed to read that profile."); return; }

            ApplyProfile(profile);
            statusLabel.Text = $"Status: Loaded profile '{profile.Name}'";
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (profileNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Profiles", "Choose a profile to delete."); return; }
            string path = GetProfilePath(name);
            if (File.Exists(path)) File.Delete(path);
            LoadProfileList();
            statusLabel.Text = $"Status: Deleted profile '{name}'";
        }

        private void ApplyProfile(InputProfile profile)
        {
            profileNameBox.Text = profile.Name;

            string type = profile.ControllerType == "Xbox 360" ? "Xbox 360" : "PS4";
            if (!engine.IsConnected) controllerTypeBox.SelectedItem = type;   // never swap a live pad just because a profile was loaded

            // Early 1.1 betas stored jitter as a negative base drift; move it to the RS jitter field.
            int jitter = profile.RightStickJitter != 0 ? profile.RightStickJitter
                : (profile.BaseDriftPercent < 0 ? profile.BaseDriftPercent : 0);
            SetDrift(Math.Max(0, profile.BaseDriftPercent));
            SetJitter(jitter);
            jitterAlgorithmBox.SelectedItem = AlgorithmFromStored(profile.RightStickJitterAlgorithm);
            jitterButtonBox.SelectedItem = TriggerFromStored(profile.RightStickJitterButton);
            jitterModeBox.SelectedItem = string.Equals(profile.RightStickJitterMode, "Toggle", StringComparison.OrdinalIgnoreCase) ? "Toggle" : "Hold";
            jitterRateBox.SelectedItem = RateFromMs(profile.RightStickJitterRateMs);

            string pull = PullDirections.Normalize(profile.BasePullDirection);   // old profiles say "Center" = Down
            pullDirectionBox.SelectedItem = pull;
            driftMoreLabel.Text = DriftMoreText(pull);
            valueLabel.Text = DriftText();

            scrollDisengageBox.Value = Math.Clamp(profile.ScrollDisengageMs, 0, 5000);
            SetWheelDisengage(profile.WheelDisengageEnabled);

            mouseLookBox.IsChecked = profile.MouseLookEnabled;
            mouseSensXBox.Value = (decimal)Math.Clamp(profile.MouseSensitivityX, 0.5, 100.0);
            mouseSensYBox.Value = (decimal)Math.Clamp(profile.MouseSensitivityY, 0.5, 100.0);
            mouseCurveBox.Value = (decimal)Math.Clamp(profile.MouseCurve, 0.5, 4.0);
            mouseDeadZoneBox.Value = Math.Clamp(profile.MouseGameDeadZonePercent, 0, 50);
            mouseReleaseBox.Value = Math.Clamp(profile.MouseReleaseMs, 0, 500);
            mouseInvertYBox.IsChecked = profile.MouseInvertY;

            foreach (var pair in disableKeyChecks)
                pair.Value.IsChecked = profile.DisableKeys?.Any(k => string.Equals(k, pair.Key, StringComparison.OrdinalIgnoreCase)) == true;

            foreach (string action in ControllerEngine.BindingActions)
            {
                if (!bindingControls.TryGetValue(action, out BindingControlSet controls)) continue;
                string key = "None";
                if (profile.KeyBindings != null && profile.KeyBindings.TryGetValue(action, out string saved) && !string.IsNullOrWhiteSpace(saved))
                    key = saved;
                if (!((string[])controls.KeySelector.ItemsSource).Contains(key)) key = "None";
                controls.KeySelector.SelectedItem = key;
                controls.TurboToggle.IsChecked = profile.TurboEnabled?.TryGetValue(action, out bool te) == true && te;
                int hz = 12;
                if (profile.TurboHz?.TryGetValue(action, out int savedHz) == true) hz = savedHz;
                controls.TurboHzBox.Value = Math.Clamp(hz, 1, 30);
            }

            recoilEnabledCheckBox.IsChecked = profile.RecoilEnabled;
            recoilLoopCheckBox.IsChecked = profile.RecoilLoop;
            recoilTriggerKeyBox.SelectedItem = TriggerFromStored(profile.RecoilTriggerKey);
            recoilTriggerModeBox.SelectedItem = string.Equals(profile.RecoilTriggerMode, "Toggle", StringComparison.OrdinalIgnoreCase) ? "Toggle" : "Hold";
            recoilStartDelayBox.Value = Math.Clamp(profile.RecoilStartDelayMs, 0, 5000);
            recoilCenterRadiusBox.Value = Math.Clamp(profile.RecoilCenterRadiusPercent, 0, 100);
            recoilRadiusModeBox.SelectedItem = RadiusModeFromStored(profile.RecoilCenterRadiusMode);
            ReplaceSteps(RecoilPattern.ParseText(string.IsNullOrWhiteSpace(profile.RecoilPatternText)
                ? RecoilPattern.DefaultText : profile.RecoilPatternText));
        }

        // ── Controller monitor ───────────────────────────────────────────────────

        private static readonly string[] MonitorHzChoices = { "30", "60", "90", "120", "144" };
        private static readonly PadView[] MonitorViews = { PadView.Final, PadView.Processed, PadView.Physical };

        private static string ViewHint(PadView v) => v switch
        {
            PadView.Physical => "Physical: what your keyboard, mouse and network commands ask for, before any processing (turbo, drift, recoil, filtering, disengage are not applied). GamepadEnhancer does not read a physical gamepad.",
            PadView.Processed => "Processed: after drift, recoil, right stick filtering, mouse look, turbo and disengage, before the value is rounded into a report.",
            _ => "Final: exactly what is written to the virtual controller, including rounding, the stick oscillation and the pad's own D-pad rules. This is what the game receives."
        };

        private PadView SelectedMonitorView() =>
            monitorViewBox.SelectedIndex >= 0 && monitorViewBox.SelectedIndex < MonitorViews.Length
                ? MonitorViews[monitorViewBox.SelectedIndex] : PadView.Final;

        private void InitializeMonitor()
        {
            suppressMonitorChange = true;
            monitorViewBox.ItemsSource = MonitorViews.Select(PadLabels.ViewName).ToArray();
            PadView saved = Enum.TryParse(appSettings.MonitorView, true, out PadView parsed) ? parsed : PadView.Final;
            monitorViewBox.SelectedIndex = Array.IndexOf(MonitorViews, saved);
            monitorHzBox.ItemsSource = MonitorHzChoices;
            int hz = Math.Clamp(appSettings.MonitorRefreshHz, 30, 144);
            monitorHzBox.SelectedItem = MonitorHzChoices.Contains(hz.ToString(CultureInfo.InvariantCulture)) ? hz.ToString(CultureInfo.InvariantCulture) : "60";
            monitorScaleBox.Value = Math.Clamp(appSettings.MonitorScalePercent, 50, 200);
            monitorEnabledBox.IsChecked = appSettings.MonitorEnabled;
            monitorCompactBox.IsChecked = appSettings.MonitorCompact;
            monitorTopmostBox.IsChecked = appSettings.MonitorAlwaysOnTop;
            monitorOverlayBox.IsChecked = appSettings.MonitorOverlayMode;
            suppressMonitorChange = false;

            monitorPanel = new MonitorPanel(engine);
            monitorHost.Child = monitorPanel;
            ApplyMonitorSettings();
        }

        /// <summary>Copies the monitor controls into the saved settings and applies them to the tab and any open pop-out.</summary>
        private void ApplyMonitorSettings()
        {
            appSettings.MonitorView = SelectedMonitorView().ToString();
            appSettings.MonitorRefreshHz = int.TryParse(monitorHzBox.SelectedItem as string, out int hz) ? hz : 60;
            appSettings.MonitorScalePercent = (int)(monitorScaleBox.Value ?? 100m);
            appSettings.MonitorEnabled = monitorEnabledBox.IsChecked != false;
            appSettings.MonitorCompact = monitorCompactBox.IsChecked == true;
            appSettings.MonitorAlwaysOnTop = monitorTopmostBox.IsChecked == true;
            appSettings.MonitorOverlayMode = monitorOverlayBox.IsChecked == true;

            PadView view = SelectedMonitorView();
            monitorPanel.View = view;
            monitorPanel.RefreshHz = appSettings.MonitorRefreshHz;
            monitorPanel.ScalePercent = appSettings.MonitorScalePercent;
            monitorPanel.Compact = appSettings.MonitorCompact;
            monitorViewHint.Text = ViewHint(view);
            monitorPopoutButton.IsEnabled = appSettings.MonitorEnabled;

            if (monitorWindow != null)
            {
                if (!appSettings.MonitorEnabled || monitorWindow.OverlayMode != appSettings.MonitorOverlayMode)
                {
                    // The frame/transparency can't change on an open window: close it (and reopen as the new kind when still enabled).
                    bool reopen = appSettings.MonitorEnabled;
                    monitorWindow.Close();
                    if (reopen) OpenMonitorWindow();
                }
                else monitorWindow.ApplyLive(appSettings, view);
            }
            UpdateMonitorRunning();
        }

        private void MonitorSettings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressMonitorChange) return;
            ApplyMonitorSettings();
            appSettings.Save();
        }

        private void MonitorSettings_Numeric(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!ready || suppressMonitorChange) return;
            ApplyMonitorSettings();
            appSettings.Save();
        }

        private void MonitorSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (!ready || suppressMonitorChange) return;
            ApplyMonitorSettings();
            appSettings.Save();
        }

        /// <summary>The tab view only draws (and only asks the engine for snapshots) while it can be seen.</summary>
        private void UpdateMonitorRunning()
        {
            if (monitorPanel == null) return;
            bool visible = monitorTab.IsSelected && WindowState != WindowState.Minimized;
            if (appSettings.MonitorEnabled && visible) monitorPanel.Start();
            else monitorPanel.Stop();
        }

        private void MonitorPopout_Click(object sender, RoutedEventArgs e)
        {
            if (monitorWindow != null) { monitorWindow.Close(); return; }
            OpenMonitorWindow();
        }

        private void OpenMonitorWindow()
        {
            var w = new MonitorWindow(engine, appSettings, SelectedMonitorView());
            monitorWindow = w;
            w.Closed += (_, _) =>
            {
                // Only clear our reference if it is still this window (a reopen may already have replaced it).
                if (!ReferenceEquals(monitorWindow, w)) return;
                monitorWindow = null;
                monitorPopoutButton.Content = "Open pop-out window";
            };
            monitorPopoutButton.Content = "Close pop-out window";
            w.Show();
        }

        // ── Shutdown ─────────────────────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            try { monitorPanel?.Stop(); } catch { }
            try { monitorWindow?.Close(); } catch { }
            try { networkApi?.Dispose(); } catch { }
            try { blockTimer?.Stop(); } catch { }
            blockPolicy.Update(BlockSettings.Off);
            try { mouseMoveSource?.Dispose(); } catch { }
            try { inputManager?.Dispose(); } catch { }
            engine.Dispose();
            base.OnClosed(e);
        }
    }
}
