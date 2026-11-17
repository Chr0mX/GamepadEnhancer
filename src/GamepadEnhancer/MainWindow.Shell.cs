using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// The application shell: sidebar navigation, page switching, the dashboard, the game and profile lists, the paddle overview and
    /// the status bar. It only presents state that already exists (engine, settings, game store) and calls the same handlers the
    /// controls always used; nothing here sits on the controller input path.
    /// </summary>
    public partial class MainWindow
    {
        private bool shellReady;
        private string currentPage = "dashboard";
        private readonly Dictionary<string, Control> pages = new();
        private readonly List<RadioButton> navButtons = new();
        private ControllerConfigView dashPad;
        private DispatcherTimer scopeTimer;
        private bool scopeViewer;
        private int monitorFitPercent = 100;
        private DispatcherTimer shellTimer;
        private string loadedProfileName;
        private string activeConfigSummary = "";
        private bool activeConfigModified;
        private bool suppressLayoutSwitch;
        private bool wideLayout = true;

        // ── Startup ─────────────────────────────────────────────────────────────

        private void InitializeShell()
        {
            pages["dashboard"] = pageDashboard;
            pages["controller"] = pageController;
            pages["games"] = pageGames;
            pages["paddles"] = pagePaddles;
            pages["mouse"] = pageMouse;
            pages["monitor"] = pageMonitor;
            pages["network"] = pageNetwork;

            foreach (RadioButton rb in sidebar.GetLogicalDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("nav")))
            {
                navButtons.Add(rb);
                if (ToolTip.GetTip(rb) is string tip) AutomationProperties.SetName(rb, tip);
            }

            // the shared editor lives in a host on whichever page is showing
            if (inspectorCard.Parent is Panel holder) holder.Children.Remove(inspectorCard);
            inspectorCard.IsVisible = true;
            inspectorHostController.Child = inspectorCard;

            dashPad = new ControllerConfigView { Overview = true, IsHitTestVisible = false, MinWidth = 0, MinHeight = 0, Family = SelectedFamily() };
            dashPadHost.Child = dashPad;
            BuildLayoutSwitch();
            BuildPaddleSummary();
            foreach (NumericUpDown n in new[] { mouseSensXBox, mouseSensYBox, mouseCurveBox, mouseDeadZoneBox, mouseReleaseBox, monitorScaleBox }) PairWithSlider(n);
            mouseCurveBox.ValueChanged += (_, _) => RefreshCurvePlot();
            mouseDeadZoneBox.ValueChanged += (_, _) => RefreshCurvePlot();
            RefreshCurvePlot();
            scopeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            scopeTimer.Tick += (_, _) => UpdateScope();
            dashTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(66) };
            dashTimer.Tick += (_, _) => UpdateDash();
            versionText.Text = "v" + (typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "").Split('+')[0];
            if (versionText.Text.EndsWith(".0", StringComparison.Ordinal) && versionText.Text.Count(c => c == '.') == 2) versionText.Text = versionText.Text[..^2];   // v2.3.0 is shown as v2.3 (the release tag)
            dataPathLabel.Text = $"Profiles: {profilesDirectory}{Environment.NewLine}Settings: {AppSettings.DefaultPath}";

            shellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            shellTimer.Tick += (_, _) => RefreshShell();
            shellTimer.Start();

            shellReady = true;
            ShowPage("dashboard");
            SyncLayoutSwitch();
            RefreshGameViews();
            UpdateProfileInfo();
            RefreshShell();
        }

        // ── Navigation ──────────────────────────────────────────────────────────

        internal static readonly string[] PageKeys = { "dashboard", "controller", "games", "paddles", "mouse", "monitor", "network" };
        private static readonly Dictionary<string, string> PageTitles = new()
        {
            ["dashboard"] = "DASHBOARD", ["controller"] = "CONTROLLER", ["games"] = "GAMES & PROFILES", ["paddles"] = "REAR PADDLES",
            ["mouse"] = "MOUSE LOOK", ["monitor"] = "MONITOR", ["network"] = "NETWORK & INPUT"
        };
        internal string CurrentPageKey => currentPage;
        internal void ShowPageForTests(string key) => NavigateTo(key);
        internal ControllerConfigView DiagramForTests => diagram;
        internal ScrollViewer CurrentPageScroller => pages[currentPage] as ScrollViewer;
        internal void SelectLayoutForTests(string layoutId) => controllerTypeBox.SelectedItem = ControllerLayouts.Get(layoutId);
        internal PadFamily FamilyForTests => SelectedFamily();

        private void Nav_Changed(object sender, RoutedEventArgs e)
        {
            if (!shellReady) return;
            if (sender is RadioButton rb && rb.IsChecked == true && rb.Tag is string key) ShowPage(key);
        }

        private void GoTo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string key) NavigateTo(key);
        }

        private void NavigateTo(string key)
        {
            RadioButton rb = navButtons.FirstOrDefault(r => r.Tag is string t && t == key);
            if (rb != null) rb.IsChecked = true;   // its change handler shows the page
            else ShowPage(key);
        }

        private void ShowPage(string key)
        {
            if (!pages.TryGetValue(key, out Control page)) return;
            EndCapture(null);
            foreach (var pair in pages) pair.Value.IsVisible = ReferenceEquals(pair.Value, page);
            currentPage = key;
            if (PageTitles.TryGetValue(key, out string title)) pageTitleText.Text = title;

            if (key == "controller")
            {
                AttachDiagram(diagramHost, DiagramMode.Controls);
                inspectorHostPaddles.Child = null;
                inspectorHostController.Child = inspectorCard;
            }
            else if (key == "paddles")
            {
                AttachDiagram(paddleDiagramHost, DiagramMode.Paddles);
                inspectorHostController.Child = null;
                inspectorHostPaddles.Child = inspectorCard;
                if (!PaddleSlots.TryParseId(selectedSlot, out _)) { selectedSlot = PaddleSlots.Id(0); diagram.SelectedId = selectedSlot; SyncEditor(); }
            }
            SetScopeActive(key == "mouse");
            SetDashActive(key == "dashboard");
            UpdateMonitorRunning();
            RefreshShell();
        }

        /// <summary>Moves the one diagram into the host of the page now showing and switches it to that page's mode.</summary>
        private void AttachDiagram(Border host, DiagramMode mode)
        {
            if (diagram == null) return;
            if (diagram.Parent is Border old && !ReferenceEquals(old, host)) old.Child = null;
            if (!ReferenceEquals(host.Child, diagram)) host.Child = diagram;
            diagram.Mode = mode;
            if (mode == DiagramMode.Controls && PaddleSlots.TryParseId(selectedSlot, out _)) { selectedSlot = null; SyncEditor(); }
            diagram.SelectedId = selectedSlot;
            diagram.Refresh();
        }

        // ── In-page tabs ────────────────────────────────────────────────────────

        private void CTab_Changed(object sender, RoutedEventArgs e)
        {
            if (!shellReady || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
            controllerMapPane.IsVisible = tag == "map";
            allBindingsPane.IsVisible = tag == "all";
        }

        private void NTab_Changed(object sender, RoutedEventArgs e)
        {
            if (!shellReady || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
            netReceiveSection.IsVisible = tag == "recv";
            netSendSection.IsVisible = tag == "send";
            netInputSection.IsVisible = tag == "input";
            appearanceSection.IsVisible = tag == "look";
        }

        // ── Live dashboard readouts (real engine snapshots; registered as a viewer only while the dashboard shows) ──

        private DispatcherTimer dashTimer;
        private bool dashViewer;
        private long dashCursor, dashRateBase;
        private double dashRateMs;
        private readonly PadEvent[] dashEventBuffer = new PadEvent[64];
        private readonly LinkedList<(string Time, string Label, bool Pressed)> dashLines = new();
        private readonly System.Diagnostics.Stopwatch dashClock = System.Diagnostics.Stopwatch.StartNew();

        private void SetDashActive(bool on)
        {
            if (on == dashViewer) return;
            dashViewer = on;
            if (on)
            {
                engine.Snapshots.AddViewer();
                dashCursor = engine.Snapshots.EventCount;
                dashRateBase = engine.Snapshots.ReportsSent;
                dashRateMs = dashClock.Elapsed.TotalMilliseconds;
                dashTimer.Start();
            }
            else { dashTimer.Stop(); engine.Snapshots.RemoveViewer(); }
        }

        private Avalonia.Media.IBrush Token(string key, Avalonia.Media.IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out object v) && v is Avalonia.Media.IBrush b ? b : fallback;

        private void UpdateDash()
        {
            if (!dashViewer) return;
            PadMonitor m = engine.Snapshots;
            PadState st = m.Get(PadView.Final);
            PadFamily fam = m.Connected ? m.Family : SelectedFamily();
            bool xbox = fam == PadFamily.Xbox360;
            dashLtLabel.Text = xbox ? "LT" : "L2";
            dashRtLabel.Text = xbox ? "RT" : "R2";
            barLt.Fraction = st.Lt;
            barRt.Fraction = st.Rt;
            plotL.Add(st.Lx, st.Ly);
            plotR.Add(st.Rx, st.Ry);
            dashLeftStick.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.00;-0.00;+0.00}  {1:+0.00;-0.00;+0.00}", st.Lx, st.Ly);
            dashRightStick.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.00;-0.00;+0.00}  {1:+0.00;-0.00;+0.00}", st.Rx, st.Ry);

            double now = dashClock.Elapsed.TotalMilliseconds;
            if (now - dashRateMs >= 500)
            {
                long reports = m.ReportsSent;
                dashRateText.Text = ((reports - dashRateBase) * 1000.0 / (now - dashRateMs)).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                dashRateBase = reports;
                dashRateMs = now;
            }

            int n = m.CopyEventsSince(ref dashCursor, dashEventBuffer);
            if (n == 0 && dashEventLog.Inlines != null && dashEventLog.Inlines.Count > 0) return;
            for (int i = 0; i < n; i++)
            {
                PadEvent e = dashEventBuffer[i];
                if (e.View != PadView.Final) continue;
                dashLines.AddFirst((TimeSpan.FromMilliseconds(e.TickMs).ToString(@"mm\:ss\.f", System.Globalization.CultureInfo.InvariantCulture), PadLabels.For(fam, e.Action), e.Pressed));
                if (dashLines.Count > 12) dashLines.RemoveLast();
            }
            dashEventLog.Inlines?.Clear();
            if (dashLines.Count == 0)
            {
                dashEventLog.Inlines?.Add(new Avalonia.Controls.Documents.Run("No events yet. Press a bound key while the virtual controller is connected.") { Foreground = Token("FaintTextBrush", Avalonia.Media.Brushes.Gray) });
                return;
            }
            foreach (var (time, label, pressed) in dashLines)
            {
                dashEventLog.Inlines.Add(new Avalonia.Controls.Documents.Run(time + "  ") { Foreground = Token("FaintTextBrush", Avalonia.Media.Brushes.Gray) });
                dashEventLog.Inlines.Add(new Avalonia.Controls.Documents.Run("[BTN] ") { Foreground = Token("ValueOrangeBrush", Avalonia.Media.Brushes.Orange) });
                dashEventLog.Inlines.Add(new Avalonia.Controls.Documents.Run(label.PadRight(12)) { Foreground = Token("TextBrush", Avalonia.Media.Brushes.White) });
                dashEventLog.Inlines.Add(new Avalonia.Controls.Documents.Run(pressed ? "pressed" : "released") { Foreground = Token(pressed ? "ValueGreenBrush" : "MutedTextBrush", Avalonia.Media.Brushes.Gray) });
                dashEventLog.Inlines.Add(new Avalonia.Controls.Documents.LineBreak());
            }
        }

        // ── Slider + number pairs, curve plot, live stick scope ────────────────

        /// <summary>
        /// Puts a slider beside a number box and keeps the two in step (the number box stays the one the settings code reads).
        /// The slider snaps to the box's increment.
        /// </summary>
        private static void PairWithSlider(NumericUpDown num)
        {
            double min = (double)num.Minimum, max = (double)num.Maximum, step = Math.Max(1e-6, (double)num.Increment);
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Width = 150, SmallChange = step, LargeChange = step * 10,
                Value = (double)(num.Value ?? num.Minimum), VerticalAlignment = VerticalAlignment.Center
            };
            bool syncing = false;
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Slider.ValueProperty || syncing) return;
                syncing = true;
                try { num.Value = (decimal)(Math.Round(slider.Value / step) * step); }
                finally { syncing = false; }
            };
            num.ValueChanged += (_, e) =>
            {
                if (syncing || !e.NewValue.HasValue) return;
                syncing = true;
                try { slider.Value = (double)e.NewValue.Value; }
                finally { syncing = false; }
            };
            num.Width = 96;
            if (num.Parent is ContentControl host)
            {
                host.Content = null;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(slider);
                row.Children.Add(num);
                host.Content = row;
            }
        }

        private void RefreshCurvePlot()
        {
            if (curvePlot == null) return;
            curvePlot.Curve = (double)(mouseCurveBox.Value ?? 1m);
            double dz = (double)(mouseDeadZoneBox.Value ?? 0m) / 100.0;
            curvePlot.DeadZone = dz;
            stickScope.DeadZone = dz;
        }

        private void SetScopeActive(bool on)
        {
            if (on == scopeViewer) return;
            scopeViewer = on;
            if (on) { engine.Snapshots.AddViewer(); scopeTimer.Start(); }
            else { scopeTimer.Stop(); engine.Snapshots.RemoveViewer(); }
        }

        private void UpdateScope()
        {
            if (!scopeViewer) return;
            PadState st = engine.Snapshots.Get(PadView.Final);
            stickScope.SetPosition(st.Rx, st.Ry);
        }

        // ── Responsive layout ───────────────────────────────────────────────────

        private void ApplyShellLayout(double windowWidth)
        {
            if (sidebar == null) return;
            double content = windowWidth - sidebar.Width;
            brandText.IsVisible = windowWidth >= 760;
            versionText.IsVisible = windowWidth >= 900;
            headerConfigPill.IsVisible = windowWidth >= 1100;   // narrower windows use the Profile menu button on the Games page
            tagCapture.IsVisible = windowWidth >= 1000;
            tagNetwork.IsVisible = windowWidth >= 1000;
            bool wide = content >= 980;
            wideLayout = wide;
            ArrangeDashboard(content);
            Arrange2(mouseGrid, mouseLeft, mouseRight, content >= 1000, "*,12,360");
            Arrange2(controllerGrid, diagramPanel, inspectorHostController, wide, "*,12,310");
            Arrange2(paddleGrid, paddleDiagramPanel, paddleSidePanel, wide, "*,12,310");
            Arrange2(gamesGrid, gamesLeft, gameDetailSection, content >= 900, "320,12,*");
            ArrangeN(monitorStages, new Control[] { monitorStagePhysical, monitorStageProcessed, monitorStageFinal }, content >= 1000, 12);

            // The diagram is drawn on an 880 x 620 canvas scaled to fit its host. Give the host that same aspect ratio so the scale
            // stays close to 1 (a short host shrank every label); the page scrolls if the result is tall.
            double diagramW = Math.Max(300, wide ? content - 32 - 12 - 310 : content - 32);
            double diagramH = Math.Clamp(diagramW * ControllerLayout.Height / ControllerLayout.Width, 300, 660);
            diagramHost.Height = diagramH;
            paddleDiagramHost.Height = diagramH;

            // three monitor stages across: shrink the drawn controllers to fit the column
            int fit = 100;
            if (content >= 1000)
            {
                double col = (content - 40 - 2 * 12 - 2 * 24) / 3;
                fit = (int)Math.Clamp(col / ControllerView.CanvasW * 100, 50, 100);
            }
            if (fit != monitorFitPercent)
            {
                monitorFitPercent = fit;
                if (monitorPanels != null) ApplyMonitorSettings();
            }
        }

        /// <summary>
        /// The dashboard card grid on twelve columns: wide = four cards across then three; medium = two then two then one then two;
        /// narrow = a single column.
        /// </summary>
        private void ArrangeDashboard(double content)
        {
            // Live state leads: the pad picture and the sticks come first, the settings-like cards (device, details) last.
            var cards = new[] { cardPad, cardStickL, cardStickR, cardAxes, cardEvents, cardVirtual, cardConfig };
            if (content >= 1180)
            {
                Place(cardPad, 0, 0, 6); Place(cardStickL, 6, 0, 3); Place(cardStickR, 9, 0, 3);
                Place(cardAxes, 0, 1, 3); Place(cardEvents, 3, 1, 3); Place(cardVirtual, 6, 1, 3); Place(cardConfig, 9, 1, 3);
            }
            else if (content >= 760)
            {
                Place(cardPad, 0, 0, 12);
                Place(cardStickL, 0, 1, 6); Place(cardStickR, 6, 1, 6);
                Place(cardAxes, 0, 2, 6); Place(cardEvents, 6, 2, 6);
                Place(cardVirtual, 0, 3, 6); Place(cardConfig, 6, 3, 6);
            }
            else
            {
                for (int i = 0; i < cards.Length; i++) Place(cards[i], 0, i, 12);
            }
            int rows = content >= 1180 ? 2 : content >= 760 ? 4 : cards.Length;
            dashGrid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows)));
        }

        private static void Place(Control c, int col, int row, int colSpan)
        {
            Grid.SetColumn(c, col); Grid.SetRow(c, row); Grid.SetColumnSpan(c, colSpan);
            c.Margin = new Thickness(0, 0, 12, 12);   // the 12 px card gap (the grid itself has 4 px so the page padding stays 16)
        }

        /// <summary>Lays three or more children in one row (wide) or one above the other (narrow), keeping any existing gap columns.</summary>
        private static void ArrangeN(Grid grid, Control[] items, bool wide, double gap = 16)
        {
            string cols = wide ? string.Join("," + gap + ",", items.Select(_ => "*")) : "*";
            grid.ColumnDefinitions = new ColumnDefinitions(cols);
            grid.RowDefinitions = new RowDefinitions(wide ? "Auto" : string.Join(",", items.Select(_ => "Auto")));
            for (int i = 0; i < items.Length; i++)
            {
                Grid.SetColumn(items[i], wide ? i * 2 : 0);
                Grid.SetRow(items[i], wide ? 0 : i);
                items[i].Margin = wide || i == 0 ? new Thickness(0) : new Thickness(0, 12, 0, 0);
            }
        }

        /// <summary>Lays two children side by side (wide) or one above the other (narrow) in the same grid.</summary>
        private static void Arrange2(Grid grid, Control first, Control second, bool wide, string wideColumns)
        {
            grid.ColumnDefinitions = new ColumnDefinitions(wide ? wideColumns : "*");
            grid.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            Grid.SetRow(first, 0); Grid.SetColumn(first, 0);
            Grid.SetRow(second, wide ? 0 : 1); Grid.SetColumn(second, wide ? 2 : 0);
            second.Margin = wide ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        }

        // ── Controller layout switch (Xbox 360 / PS4) ───────────────────────────

        private void BuildLayoutSwitch()
        {
            foreach (ControllerLayoutDef layout in ControllerLayouts.All)
            {
                var rb = new RadioButton { Classes = { "seg" }, GroupName = "layoutSwitch", Content = layout.Name, Tag = layout };
                ToolTip.SetTip(rb, layout.Title);
                rb.IsCheckedChanged += (_, _) =>
                {
                    if (suppressLayoutSwitch || rb.IsChecked != true) return;
                    controllerTypeBox.SelectedItem = layout;   // the same path the sidebar dropdown takes
                };
                layoutSwitch.Children.Add(rb);
            }
        }

        private void SyncLayoutSwitch()
        {
            if (layoutSwitch == null) return;
            ControllerLayoutDef current = SelectedControllerLayout();
            suppressLayoutSwitch = true;
            try
            {
                foreach (RadioButton rb in layoutSwitch.Children.OfType<RadioButton>())
                    rb.IsChecked = ReferenceEquals(rb.Tag, current);
            }
            finally { suppressLayoutSwitch = false; }
        }

        // ── Dashboard and status bar ────────────────────────────────────────────

        private void RefreshShell()
        {
            if (!shellReady) return;
            RefreshShellStatus();
            switch (currentPage)
            {
                case "dashboard": RefreshDashboard(); break;
                case "paddles": RefreshPaddlePage(); break;
                case "network": RefreshNetworkBadges(); RefreshBlockBadge(); break;
            }
        }

        /// <summary>The status bar: always shows whether the virtual controller exists and what is loaded.</summary>
        private void RefreshShellStatus()
        {
            if (statusBarBadge == null) return;
            ControllerLayoutDef layout = SelectedControllerLayout();
            if (engine.IsConnected)
                statusBarBadge.Set(BadgeState.Ok, $"Connected · {ControllerLayouts.ForDevice(engine.Family).Title}");
            else
                statusBarBadge.Set(BadgeState.Off, $"Not connected · {layout.Name} selected");
            UpdateProfileHeaderText();
            connectButton.IsVisible = !engine.IsConnected;
            disconnectButton.IsVisible = engine.IsConnected;
            tagOutputText.Text = engine.IsConnected && engine.IsRemote ? "REMOTE PC" : "VIGEM";
            tagOutput.Classes.Set("on", engine.IsConnected);
            tagCaptureText.Text = string.Equals(appSettings.InputCaptureMethod, "RawInput", StringComparison.OrdinalIgnoreCase) ? "RAW INPUT" : "HOOKS";
            tagCapture.Classes.Set("on", true);
            bool netOn = networkApi != null && networkApi.IsRunning;
            tagNetworkText.Text = netOn ? $"NET API :{networkApi.Port}" : "NET API";
            tagNetwork.Classes.Set("on", netOn);
        }

        private void RefreshDashboard()
        {
            ControllerLayoutDef layout = SelectedControllerLayout();
            if (engine.IsConnected)
            {
                dashVirtualBadge.Set(BadgeState.Ok, "Connected");
                dashVirtualTitle.Text = ControllerLayouts.ForDevice(engine.Family).Title;
                dashVirtualDetail.Text = engine.IsRemote ? "Sending to another PC" : "ViGEmBus on this PC";
                dashOutputBadge.Set(BadgeState.Ok, engine.IsRemote ? "Sending to another PC" : "ViGEm (this PC)");
                dashOutputDetail.Text = engine.IsRemote ? $"{appSettings.RemoteHost}:{appSettings.RemotePort}" : "ViGEmBus virtual device";
            }
            else
            {
                dashVirtualBadge.Set(BadgeState.Off, "Not connected");
                dashVirtualTitle.Text = layout.Title;
                dashVirtualDetail.Text = "Selected · not connected";
                dashOutputBadge.Set(BadgeState.Off, "Idle");
                dashOutputDetail.Text = appSettings.RemoteSendEnabled ? $"Will send to {appSettings.RemoteHost}:{appSettings.RemotePort}" : "Will use ViGEmBus on this PC (it must be installed)";
            }

            PaddleReading reading = paddleSource.Read();
            if (reading.Connected)
            {
                dashPhysicalBadge.Set(BadgeState.Ok, "Detected");
                dashPhysicalDetail.Text = string.IsNullOrEmpty(reading.DeviceName) ? "Xbox-class controller" : reading.DeviceName;
            }
            else
            {
                dashPhysicalBadge.Set(BadgeState.Off, "None detected");
                dashPhysicalDetail.Text = "Read through Windows.Gaming.Input; only needed for rear paddles.";
            }

            if (networkApi != null && networkApi.IsRunning)
            {
                dashNetworkBadge.Set(BadgeState.Ok, $"Listening on {networkApi.Port}");
                dashNetworkDetail.Text = appSettings.NetworkApiBindAll ? "Reachable from the LAN" : "This PC only";
            }
            else
            {
                dashNetworkBadge.Set(BadgeState.Off, "Off");
                dashNetworkDetail.Text = "Other PCs cannot control this controller.";
            }

            dashConfigText.Text = activeGame.DisplayName;
            dashPad.Family = SelectedFamily();
            dashConfigDetail.Text = activeConfigSummary;
            dashProfileTitle.Text = string.IsNullOrEmpty(loadedProfileName) ? "None loaded" : loadedProfileName;
            dashProfileText.Text = string.IsNullOrEmpty(loadedProfileName) ? "load one from the header menu" : (profileModified ? "unsaved changes" : "saved profile");
            dashMouseText.Text = mouseLookBox.IsChecked == true ? "On" : "Off";
            int assigned = paddleSettings.Count(p => p.IsActive);
            dashPaddleText.Text = assigned == 0 ? "None assigned" : $"{assigned} of {PaddleSlots.Count} active";
            dashBlockText.Text = blockEnabledBox.IsChecked == true ? "On" : "Off";
            dashCaptureText.Text = appSettings.InputCaptureMethod;
        }

        private void RefreshNetworkBadges()
        {
            if (networkApi != null && networkApi.IsRunning) netStatusBadge.Set(BadgeState.Ok, $"Receiving enabled · port {networkApi.Port}");
            else netStatusBadge.Set(BadgeState.Off, "Receiving is off");

            if (engine.IsConnected && engine.IsRemote) remoteStatusBadge.Set(BadgeState.Ok, "Sending to the other PC");
            else if (appSettings.RemoteSendEnabled) remoteStatusBadge.Set(BadgeState.Warn, "Enabled · press Connect to start sending");
            else remoteStatusBadge.Set(BadgeState.Off, "Sending is off");
        }

        private void RefreshBlockBadge()
        {
            if (blockEnabledBox.IsChecked != true) blockStatusBadge.Set(BadgeState.Off, "Blocking is off");
            else if (blockToggledOn) blockStatusBadge.Set(BadgeState.Ok, "Blocking is on");
            else blockStatusBadge.Set(BadgeState.Warn, "Blocking is paused (hotkey)");
        }

        // ── Games ───────────────────────────────────────────────────────────────

        private sealed class GameEntry
        {
            public string Id;
            public string Name;
            public string Info;
        }

        private List<GameEntry> gameEntries = new();

        private void BuildGameList()
        {
            gameList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GameEntry>((e, _) =>
            {
                var panel = new StackPanel { Spacing = 2 };
                panel.Children.Add(new TextBlock { Text = e?.Name, Classes = { "label" } });
                panel.Children.Add(new TextBlock { Text = e?.Info, Classes = { "caption" } });
                return panel;
            });
            RebuildGameEntries();
        }

        private void RebuildGameEntries()
        {
            if (gameList == null || gameStore == null) return;
            gameEntries = GameCatalog.All.Select(g => new GameEntry
            {
                Id = g.Id,
                Name = g.DisplayName,
                Info = !g.Verified ? "Defaults not verified yet"
                    : gameStore.HasUser(g.Id) ? "Built-in default · your changes saved"
                    : "Built-in default"
            }).ToList();
            bool previous = suppressGameChange;
            suppressGameChange = true;
            try
            {
                gameList.ItemsSource = gameEntries;
                gameList.SelectedItem = gameEntries.FirstOrDefault(e => e.Id == activeGame.Id);
            }
            finally { suppressGameChange = previous; }
        }

        /// <summary>Selects the active game in the list without rebuilding it (this runs inside the list's own selection event).</summary>
        private void SyncGameList(string id)
        {
            if (gameList == null || gameEntries == null) return;
            GameEntry entry = gameEntries.FirstOrDefault(e => e.Id == id);
            if (entry != null && !ReferenceEquals(gameList.SelectedItem, entry)) gameList.SelectedItem = entry;
        }

        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressGameChange || gameStore == null) return;
            if (gameList.SelectedItem is GameEntry entry && entry.Id != activeGame.Id) LoadGame(entry.Id, saveCurrent: true);
        }

        private string lastGameViewKey;

        /// <summary>Updates the Games page detail panel and the list's "your changes saved" notes when the active config's state changed.</summary>
        private void RefreshGameViews()
        {
            if (!shellReady || gameDetailName == null) return;
            string key = $"{activeGame.Id}|{activeSource}|{activeConfigModified}|{ActiveLayout().Id}|{gameStore?.HasUser(activeGame.Id)}|{string.Join(",", Inputs.Buttons.Select(SlotMappingText))}";
            if (key == lastGameViewKey) return;
            lastGameViewKey = key;

            gameDetailName.Text = activeGame.DisplayName;
            gameDetailBadges.Children.Clear();
            void Badge(BadgeState s, string text) => gameDetailBadges.Children.Add(new StatusBadge { Margin = new Thickness(0, 0, 16, 4), State = s, Text = text });
            Badge(activeGame.Verified ? BadgeState.Ok : BadgeState.Warn, activeGame.Verified ? "Defaults verified" : "Defaults not verified");
            Badge(activeSource == ProfileSource.User ? BadgeState.Warn : BadgeState.Ok,
                activeSource switch { ProfileSource.User => "Your saved changes", ProfileSource.BuiltIn => "Built-in default", _ => "Default config fallback" });
            Badge(activeConfigModified ? BadgeState.Warn : BadgeState.Off, activeConfigModified ? "Differs from the built-in default" : "Matches the built-in default");

            gameDetailSource.Text = activeGame.Verified
                ? (string.IsNullOrEmpty(activeGame.Layout) || activeGame.Layout == "n/a" ? activeGame.Source : $"Native layout: {activeGame.Layout}. Source: {activeGame.Source}")
                : activeGame.Source;

            var rows = new List<Control>();
            ControllerLayoutDef layout = ActiveLayout();
            foreach (string input in Inputs.Buttons)
            {
                string text = activeGame.NativeLabel(input);
                if (text == null) continue;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), Margin = new Thickness(0, 0, 0, 0) };
                row.Children.Add(new TextBlock { Text = layout.Label(input), FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var action = new TextBlock { Text = text, Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(action, 1);
                row.Children.Add(action);
                var keyChip = new Border { Classes = { "kbd" }, Child = new TextBlock { Text = SlotMappingText(input), Classes = { "mono" } } };
                Grid.SetColumn(keyChip, 2);
                row.Children.Add(keyChip);
                rows.Add(new Border { Classes = { "rowline" }, Child = row });
            }
            gameNativeList.ItemsSource = rows;
            gameNativeEmpty.IsVisible = rows.Count == 0;
            gameNativeEmpty.Text = !activeGame.Verified && activeGame.Id != GameCatalog.GenericId
                ? "Not verified yet: nothing is shown or bound for this game rather than guessing. It will appear once the in-game controller layout is transcribed."
                : "This config has no game-specific layout; it is a plain keyboard and mouse mapping.";
            Dispatcher.UIThread.Post(RebuildGameEntries);   // never rebuild the list from inside its own selection event
        }

        // ── Rear paddles ────────────────────────────────────────────────────────

        private readonly List<Ellipse> paddleDots = new();
        private readonly List<TextBlock> paddleTargets = new();
        private readonly List<TextBlock> paddleHardware = new();

        private void BuildPaddleSummary()
        {
            for (int i = 0; i < PaddleSlots.Count; i++)
            {
                int slot = i;
                var dot = new Ellipse { Classes = { "dot" }, VerticalAlignment = VerticalAlignment.Center };
                var title = new TextBlock { Text = PaddleSlots.Titles[i], Classes = { "label" } };
                var target = new TextBlock { Classes = { "caption" } };
                var hw = new TextBlock { Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                paddleDots.Add(dot); paddleTargets.Add(target); paddleHardware.Add(hw);

                var text = new StackPanel { Spacing = 1, Margin = new Thickness(10, 0, 0, 0) };
                text.Children.Add(title);
                text.Children.Add(target);
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                grid.Children.Add(dot);
                Grid.SetColumn(text, 1); grid.Children.Add(text);
                Grid.SetColumn(hw, 2); grid.Children.Add(hw);

                var button = new Button { Classes = { "ghost" }, Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 8) };
                button.Click += (_, _) =>
                {
                    selectedSlot = PaddleSlots.Id(slot);
                    diagram.SelectedId = selectedSlot;
                    OnSlotSelected(selectedSlot);
                };
                paddleSummary.Children.Add(button);
            }
        }

        private void RefreshPaddlePage()
        {
            bool connected = lastReading.Connected;
            if (!connected) paddleHwBadge.Set(BadgeState.Off, "No physical controller detected");
            else
            {
                int n = Enumerable.Range(0, PaddleSlots.Count).Count(i => ((availableMask >> i) & 1) != 0);
                paddleHwBadge.Set(n > 0 ? BadgeState.Ok : BadgeState.Warn, n > 0 ? $"{lastReading.DeviceName} · {n} paddle(s)" : $"{lastReading.DeviceName} · no paddles detected yet");
            }
            for (int i = 0; i < PaddleSlots.Count; i++)
            {
                PaddleSetting s = paddleSettings[i];
                paddleTargets[i].Text = s.Enabled && s.HasTarget ? PaddleTargetText(s) : s.HasTarget ? PaddleTargetText(s) + " (disabled)" : "Not set";
                paddleHardware[i].Text = $"Hardware paddle {paddleSourceMap[i] + 1}" + (((availableMask >> i) & 1) != 0 ? "" : " · not detected");
            }
        }

        private void UpdatePaddleSummaryLive(int physicalSlots)
        {
            if (currentPage != "paddles") return;
            for (int i = 0; i < paddleDots.Count; i++)
                paddleDots[i].Classes.Set("ok", ((physicalSlots >> i) & 1) != 0);
        }

        // ── Profiles ────────────────────────────────────────────────────────────

        private void UpdateProfileHeaderText()
        {
            if (statusBarConfig == null) return;
            statusBarConfig.Text = activeGame.DisplayName + " · " + (string.IsNullOrEmpty(loadedProfileName) ? "no profile" : loadedProfileName + (profileModified ? " ●" : ""));
        }

        private void UpdateProfileInfo()
        {
            if (profileActiveLabel == null) return;
            string name = loadedProfileName;
            profileActiveLabel.Text = string.IsNullOrEmpty(name)
                ? $"No profile loaded. You are working from the {activeGame.DisplayName} defaults."
                : $"Profile: {name}" + (profileModified ? "  ·  unsaved changes" : "  ·  saved");
            if (string.IsNullOrEmpty(name)) { profileInfoLabel.Text = "Use the menu to load a saved profile, or Save as to keep what you have now."; return; }
            try
            {
                var info = new FileInfo(GetProfilePath(name));
                profileInfoLabel.Text = info.Exists
                    ? $"{info.Name} · {info.Length / 1024.0:0.#} KB · saved {info.LastWriteTime:g}"
                    : "The file for this profile no longer exists. Save it again to recreate it.";
            }
            catch (Exception ex) { profileInfoLabel.Text = ex.Message; }
        }
    }
}
