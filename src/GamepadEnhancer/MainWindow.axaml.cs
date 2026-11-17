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
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    public partial class MainWindow : Window
    {
        private readonly string profilesDirectory = Path.Combine(AppContext.BaseDirectory, "profiles");

        private readonly Dictionary<string, BindingControlSet> bindingControls = new(StringComparer.OrdinalIgnoreCase);
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
        private MonitorPanel[] monitorPanels;   // physical, processed and final, shown side by side
        private MonitorWindow monitorWindow;
        private bool suppressMonitorChange;

        private bool ready;

        private sealed class BindingControlSet
        {
            public ComboBox KeySelector { get; init; }
            public TextBlock KindTag { get; init; }
            public CheckBox TurboToggle { get; init; }      // null for left-stick directions (they are not buttons)
            public NumericUpDown TurboHzBox { get; init; }
        }

        public MainWindow()
        {
            InitializeComponent();

            controllerTypeBox.ItemsSource = ControllerLayouts.All.ToList();   // shown by each layout's own short name
            controllerTypeBox.SelectedItem = ControllerLayouts.Get(null);
            foreach (string action in ControllerEngine.BindingActions)
                (InputChoices.IsStickAction(action) ? leftStickGrid : bindingsGrid).Children.Add(CreateBindingSelector(action));

            Directory.CreateDirectory(profilesDirectory);

            engine.StatusChanged += text => Dispatcher.UIThread.Post(() => statusLabel.Text = text);
            engine.Faulted += ex => Dispatcher.UIThread.Post(async () =>
            {
                SetConnectedUi(false);
                statusLabel.Text = "Status: Update failed";
                await Dialogs.ShowMessageAsync(this, "Update Error", "Failed while updating the virtual controller.\n\n" + ex.Message);
            });

            InitializeInputHook();
            // First run writes the shipped Default.json; after an upgrade that changed the shipped mapping the old file is
            // replaced once (and kept as Default.json.bak).
            if (DefaultProfile.PrepareStartup(profilesDirectory, appSettings.DefaultMappingVersion))
            {
                appSettings.DefaultMappingVersion = DefaultProfile.MappingVersion;
                appSettings.Save();
            }
            InitializeBlocking();
            InitializeNetworkApi();
            InitializeMonitor();
            InitializeGames();
            LoadProfileList();
            InitializeShell();

            ready = true;
            controllerTypeBox.SelectionChanged += ControllerTypeBox_SelectionChanged;
            UpdateActiveControllerLabel();
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateMonitorRunning(); };
            UpdateMonitorRunning();
            SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);
            Opened += (_, _) => UpdateResponsiveLayout(Bounds.Width);
            PushSettings();
        }

        // ── Responsive layout ────────────────────────────────────────────────────

        private void UpdateResponsiveLayout(double windowWidth)
        {
            ApplyShellLayout(windowWidth);
            double w = Math.Max(0, windowWidth - 48);
            bindingsGrid.Columns = Math.Clamp((int)(w / 380), 1, 4);
            leftStickGrid.Columns = Math.Clamp((int)(w / 380), 1, 4);
        }

        // ── Binding rows ─────────────────────────────────────────────────────────

        /// <summary>
        /// A picker whose list is split into labelled groups (keyboard keys, mouse buttons, mouse wheel). The stored value
        /// (the name profiles have always used) is kept in <c>Tag</c>; the visible text is friendlier. Left-stick directions
        /// only get the keyboard group.
        /// </summary>
        private static ComboBox CreatePicker(bool keyboardOnly)
        {
            var combo = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 0, 10, 4) };
            var items = new List<ComboBoxItem> { new ComboBoxItem { Content = "None (not bound)", Tag = InputChoices.None } };
            void Group(string title, IEnumerable<string> names)
            {
                items.Add(new ComboBoxItem { Content = "— " + title + " —", IsEnabled = false, Tag = null, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                items.AddRange(names.Select(n => new ComboBoxItem { Content = InputChoices.DisplayName(n), Tag = n }));
            }
            Group("Keyboard", InputChoices.Keyboard);
            if (!keyboardOnly)
            {
                Group("Mouse buttons", InputChoices.MouseButtons);
                Group("Mouse wheel", InputChoices.MouseWheel);
            }
            combo.ItemsSource = items;
            combo.SelectedIndex = 0;
            return combo;
        }

        private static string ChoiceValue(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? InputChoices.None;

        private static void SetChoice(ComboBox combo, string value)
        {
            var match = (combo.ItemsSource as IEnumerable<ComboBoxItem>)?.FirstOrDefault(i =>
                i.Tag is string t && string.Equals(t, value, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = match ?? (combo.ItemsSource as IEnumerable<ComboBoxItem>)?.First();
        }

        /// <summary>The binding's display name on the controller layout currently in use (Cross on PlayStation, A on Xbox).</summary>
        private string ActionTitle(string actionName) => ActiveLayout().Label(actionName);

        private Control CreateBindingSelector(string actionName)
        {
            bool stick = InputChoices.IsStickAction(actionName);
            ComboBox combo = CreatePicker(keyboardOnly: stick);
            var tag = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4), MinWidth = 90, Text = InputChoices.KindLabel(InputKind.None) };
            combo.SelectionChanged += (_, _) =>
            {
                tag.Text = InputChoices.KindLabel(InputChoices.KindOf(ChoiceValue(combo)));
                PushSettings();
                OnBindingChanged(actionName);
            };

            CheckBox turbo = null;
            NumericUpDown hz = null;
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(combo);
            row.Children.Add(tag);
            if (!stick)
            {
                turbo = new CheckBox { Content = "Turbo", Margin = new Thickness(0, 0, 8, 4) };
                hz = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 12, Increment = 1, FormatString = "0", MinWidth = 100, Margin = new Thickness(0, 0, 6, 4) };
                turbo.IsCheckedChanged += AnySettingChangedChecked;
                hz.ValueChanged += AnySettingChangedNumeric;
                row.Children.Add(turbo);
                row.Children.Add(hz);
                row.Children.Add(new TextBlock { Text = "Hz", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            }

            bindingControls[actionName] = new BindingControlSet { KeySelector = combo, KindTag = tag, TurboToggle = turbo, TurboHzBox = hz };

            var panel = new StackPanel { Spacing = 4 };
            var title = new TextBlock { Text = ActionTitle(actionName), FontWeight = Avalonia.Media.FontWeight.SemiBold };
            bindingTitleLabels[actionName] = title;
            panel.Children.Add(title);
            var gameHint = new TextBlock { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
            gameHintLabels[actionName] = gameHint;   // text is filled in per selected game (RefreshGameHints)
            panel.Children.Add(gameHint);
            panel.Children.Add(row);
            return new Border { Classes = { "card" }, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10), Child = panel };
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
                    Key = ChoiceValue(pair.Value.KeySelector),
                    Turbo = pair.Value.TurboToggle?.IsChecked == true,
                    TurboHz = (int)(pair.Value.TurboHzBox?.Value ?? 12)
                };
            }

            engine.Settings = new EngineSettings
            {
                MouseLookEnabled = mouseLookBox.IsChecked == true,
                MouseSensitivityX = (double)(mouseSensXBox.Value ?? 8m),
                MouseSensitivityY = (double)(mouseSensYBox.Value ?? 8m),
                MouseCurve = (double)(mouseCurveBox.Value ?? 1m),
                MouseGameDeadZonePercent = (int)(mouseDeadZoneBox.Value ?? 0m),
                MouseReleaseMs = (int)(mouseReleaseBox.Value ?? 30m),
                MouseInvertY = mouseInvertYBox.IsChecked == true,
                Paddles = (PaddleSetting[])paddleSettings.Clone(),
                PaddleSourceIndex = (int[])paddleSourceMap.Clone(),
                Bindings = bindings
            };
            MarkGameDirty();

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
            captureSink = new CapturingSink(engine);   // passes input to the engine, except while "press a key" is waiting
            inputManager = new InputStateManager(captureSink, (method, receiver) =>
                method == InputCaptureMethod.RawInput
                    ? new RawInputService(receiver)
                    : CreateHookCapture(receiver));
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
            networkApi = new NetworkApiServer(engine.Remote, () => appSettings.NetworkApiToken, () => engine.IsConnected, MouseButtonState.ReadMask);
            networkApi.StatusChanged += text => Dispatcher.UIThread.Post(() => netStatusLabel.Text = text);

            suppressNetworkChange = true;
            netEnabledBox.IsChecked = appSettings.NetworkApiEnabled;
            netBindAllBox.IsChecked = appSettings.NetworkApiBindAll;
            netPortBox.Text = appSettings.NetworkApiPort.ToString();
            netTokenBox.Text = appSettings.NetworkApiToken;
            remoteSendBox.IsChecked = appSettings.RemoteSendEnabled;
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
            netTokenBox.PasswordChar = netShowTokenBox.IsChecked == true ? '\0' : '●';

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
                bool xbox360 = SelectedControllerLayout().Family == PadFamily.Xbox360;
                if (appSettings.RemoteSendEnabled)
                {
                    SaveRemoteSettings();
                    if (string.IsNullOrEmpty(appSettings.RemoteHost) || string.IsNullOrEmpty(appSettings.RemoteToken))
                        throw new InvalidOperationException("Enter the other PC's address and token on the Network tab first.");
                    engine.Connect(xbox360, new RemoteTarget(appSettings.RemoteHost, appSettings.RemotePort, appSettings.RemoteToken));
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
            bool wantXbox = SelectedControllerLayout().Family == PadFamily.Xbox360;
            if (engine.IsConnected && wantXbox != (engine.Family == PadFamily.Xbox360)) await ConnectNowAsync();
            else UpdateActiveControllerLabel();
        }

        /// <summary>Shows which virtual controller is exposed right now (or that none is) in the header and the window title.</summary>
        private void UpdateActiveControllerLabel()
        {
            RefreshControllerLabels();
            SyncLayoutSwitch();
            string selected = SelectedControllerLayout().Title;
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
            connectButton.IsVisible = !connected;
            disconnectButton.IsVisible = connected;
            if (!connected) statusLabel.Text = "Status: Not connected";
            UpdateActiveControllerLabel();
            RefreshShellStatus();
            UpdateMonitorRunning();
        }

        // ── Theme ────────────────────────────────────────────────────────────────

        private void DarkModeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool dark = darkModeToggle.IsChecked == true;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // ── Profile files ────────────────────────────────────────────────────────

        private string GetProfilePath(string name) => Path.Combine(profilesDirectory, SafeName(name, "Profile") + ".json");

        private static string SafeName(string name, string fallback)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = string.Concat(name.Where(ch => !invalid.Contains(ch))).Trim();
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }

        // ── Profiles ─────────────────────────────────────────────────────────────

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
            UpdateProfileInfo();
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (profileComboBox.SelectedItem is string name) profileNameBox.Text = name;
            UpdateProfileInfo();
        }

        /// <summary>The current UI as a profile (controller type, mouse look and every binding).</summary>
        private InputProfile BuildProfileFromUi(string name) => new InputProfile
        {
            Name = name,
            Schema = 2,
            ControllerType = SelectedControllerLayout().Id,
            Game = activeGame.Id == GameCatalog.GenericId ? null : activeGame.Id,
            MouseLookEnabled = mouseLookBox.IsChecked == true,
            MouseSensitivityX = (double)(mouseSensXBox.Value ?? 8m),
            MouseSensitivityY = (double)(mouseSensYBox.Value ?? 8m),
            MouseCurve = (double)(mouseCurveBox.Value ?? 1m),
            MouseGameDeadZonePercent = (int)(mouseDeadZoneBox.Value ?? 0m),
            MouseReleaseMs = (int)(mouseReleaseBox.Value ?? 30m),
            MouseInvertY = mouseInvertYBox.IsChecked == true,
            Paddles = PaddleMappings.Build(paddleSettings),
            Mappings = ProfileMappings.Build(ControllerEngine.BindingActions.Select(a =>
            {
                BindingControlSet c = bindingControls[a];
                return (a, ChoiceValue(c.KeySelector), c.TurboToggle?.IsChecked == true, (int)(c.TurboHzBox?.Value ?? 12));
            }))
        };

        private static readonly JsonSerializerOptions ProfileJson = new() { WriteIndented = true };

        private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (profileNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { await Dialogs.ShowMessageAsync(this, "Profiles", "Enter a profile name first."); return; }

            File.WriteAllText(GetProfilePath(name), JsonSerializer.Serialize(BuildProfileFromUi(name), ProfileJson));
            loadedProfileName = name;
            LoadProfileList(name);
            statusLabel.Text = $"Status: Saved profile '{name}'";
        }

        private static readonly FilePickerFileType JsonFileType = new("Profile (JSON)") { Patterns = new[] { "*.json" } };

        /// <summary>Writes the current mapping to a file you choose, so it can be shared or edited by hand.</summary>
        private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (profileNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) name = "profile";
            IStorageFile file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export profile",
                SuggestedFileName = SafeName(name, "profile") + ".json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { JsonFileType }
            });
            if (file == null) return;
            try
            {
                await using Stream stream = await file.OpenWriteAsync();
                if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(JsonSerializer.Serialize(BuildProfileFromUi(name), ProfileJson));
                statusLabel.Text = $"Status: Exported profile '{name}'";
            }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Export", "Could not write the file.\n\n" + ex.Message); }
        }

        /// <summary>Reads a profile file, clears anything invalid (and says what), saves it into profiles/ and applies it.</summary>
        private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import profile",
                AllowMultiple = false,
                FileTypeFilter = new[] { JsonFileType }
            });
            if (files.Count == 0) return;

            string json;
            try
            {
                await using Stream stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                var buffer = new char[ProfileImport.MaxBytes + 1];
                int n = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                json = new string(buffer, 0, n);
            }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Import", "Could not read the file.\n\n" + ex.Message); return; }

            InputProfile profile = ProfileImport.Parse(json, out string error, out List<string> warnings);
            if (profile == null) { await Dialogs.ShowMessageAsync(this, "Import", error); return; }

            string baseName = SafeName(profile.Name, "Imported");
            string name = baseName;
            for (int i = 1; File.Exists(GetProfilePath(name)); i++) name = baseName + "_" + i;
            profile.Name = name;
            File.WriteAllText(GetProfilePath(name), JsonSerializer.Serialize(profile, ProfileJson));
            loadedProfileName = name;
            LoadProfileList(name);
            ApplyProfile(profile);
            statusLabel.Text = $"Status: Imported profile '{name}'" + (warnings.Count > 0 ? $" ({warnings.Count} value(s) cleared)" : "");
            if (warnings.Count > 0)
                await Dialogs.ShowMessageAsync(this, "Import", "The profile was imported, but some values were not valid and were cleared:\n\n" + string.Join("\n", warnings.Take(12)));
        }

        private void OpenProfilesFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(profilesDirectory);
            try { Process.Start(new ProcessStartInfo(profilesDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { statusLabel.Text = "Status: Could not open folder - " + ex.Message; }
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
            loadedProfileName = name;
            UpdateProfileInfo();
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

            if (!engine.IsConnected) controllerTypeBox.SelectedItem = ControllerLayouts.Get(profile.ControllerType);   // never swap a live pad just because a profile was loaded

            mouseLookBox.IsChecked = profile.MouseLookEnabled;
            mouseSensXBox.Value = (decimal)Math.Clamp(profile.MouseSensitivityX, 0.5, 100.0);
            mouseSensYBox.Value = (decimal)Math.Clamp(profile.MouseSensitivityY, 0.5, 100.0);
            mouseCurveBox.Value = (decimal)Math.Clamp(profile.MouseCurve, 0.5, 4.0);
            mouseDeadZoneBox.Value = Math.Clamp(profile.MouseGameDeadZonePercent, 0, 50);
            mouseReleaseBox.Value = Math.Clamp(profile.MouseReleaseMs, 0, 500);
            mouseInvertYBox.IsChecked = profile.MouseInvertY;

            paddleSettings = PaddleMappings.Resolve(profile);
            Dictionary<string, MappingEntry> map = ProfileMappings.Resolve(profile);
            foreach (string action in ControllerEngine.BindingActions)
            {
                if (!bindingControls.TryGetValue(action, out BindingControlSet controls)) continue;
                MappingEntry entry = map[action];
                SetChoice(controls.KeySelector, entry.Input);
                if (controls.TurboToggle != null) controls.TurboToggle.IsChecked = entry.Turbo;
                if (controls.TurboHzBox != null) controls.TurboHzBox.Value = entry.TurboHz;
            }
        }

        // ── Controller monitor ───────────────────────────────────────────────────

        private static readonly string[] MonitorHzChoices = { "30", "60", "90", "120", "144" };
        private static readonly PadView[] MonitorViews = { PadView.Final, PadView.Processed, PadView.Physical };

        private static string ViewHint(PadView v) => v switch
        {
            PadView.Physical => "Physical: what your keyboard, mouse and network commands ask for, before any processing (turbo is not applied). GamepadEnhancer does not read a physical gamepad.",
            PadView.Processed => "Processed: after mouse look and turbo, before the value is rounded into a report.",
            _ => "Final: exactly what is written to the virtual controller, including rounding and the pad's own D-pad rules. This is what the game receives."
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

            monitorPanels = new[]
            {
                new MonitorPanel(engine) { View = PadView.Physical },
                new MonitorPanel(engine) { View = PadView.Processed },
                new MonitorPanel(engine) { View = PadView.Final }
            };
            monitorHostPhysical.Child = monitorPanels[0];
            monitorHostProcessed.Child = monitorPanels[1];
            monitorHostFinal.Child = monitorPanels[2];
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
            int scale = Math.Min(appSettings.MonitorScalePercent, monitorFitPercent);
            foreach (MonitorPanel p in monitorPanels)
            {
                p.RefreshHz = appSettings.MonitorRefreshHz;
                p.ScalePercent = scale;
                p.Compact = appSettings.MonitorCompact;
            }
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
            if (monitorPanels == null) return;
            bool visible = currentPage == "monitor" && WindowState != WindowState.Minimized;
            foreach (MonitorPanel p in monitorPanels)
            {
                if (appSettings.MonitorEnabled && visible) p.Start();
                else p.Stop();
            }
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
            StopGames();
            try { SetScopeActive(false); } catch { }
            try { if (monitorPanels != null) foreach (MonitorPanel p in monitorPanels) p.Stop(); } catch { }
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
