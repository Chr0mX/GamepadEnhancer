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
        private readonly List<TextBlock> navTexts = new();
        private DispatcherTimer shellTimer;
        private string loadedProfileName;
        private string activeConfigSummary = "";
        private bool activeConfigModified;
        private bool suppressLayoutSwitch;
        private bool sidebarCompact;
        private bool wideLayout = true;

        // ── Startup ─────────────────────────────────────────────────────────────

        private void InitializeShell()
        {
            pages["dashboard"] = pageDashboard;
            pages["controller"] = pageController;
            pages["games"] = pageGames;
            pages["paddles"] = pagePaddles;
            pages["mouse"] = pageMouse;
            pages["profiles"] = pageProfiles;
            pages["monitor"] = pageMonitor;
            pages["network"] = pageNetwork;
            pages["settings"] = pageSettings;

            foreach (RadioButton rb in sidebar.GetLogicalDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("nav")))
            {
                navButtons.Add(rb);
                if (ToolTip.GetTip(rb) is string tip) AutomationProperties.SetName(rb, tip);
            }
            navTexts.AddRange(sidebar.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("navtext")));

            // the shared editor lives in a host on whichever page is showing
            if (inspectorCard.Parent is Panel holder) holder.Children.Remove(inspectorCard);
            inspectorCard.IsVisible = true;
            inspectorHostController.Child = inspectorCard;

            BuildLayoutSwitch();
            BuildPaddleSummary();
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

        internal static readonly string[] PageKeys = { "dashboard", "controller", "games", "paddles", "mouse", "profiles", "monitor", "network", "settings" };
        internal string CurrentPageKey => currentPage;
        internal void ShowPageForTests(string key) => NavigateTo(key);

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

        // ── Responsive layout ───────────────────────────────────────────────────

        private void ApplyShellLayout(double windowWidth)
        {
            if (sidebar == null) return;
            bool compact = windowWidth < 900;
            if (compact != sidebarCompact || sidebar.Width < 1)
            {
                sidebarCompact = compact;
                sidebar.Width = compact ? 56 : 232;
                foreach (TextBlock t in navTexts) t.IsVisible = !compact;
                sidebarFooter.IsVisible = !compact;
                foreach (RadioButton rb in navButtons) rb.Padding = compact ? new Thickness(0) : new Thickness(10, 0);
                foreach (RadioButton rb in navButtons) rb.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            }

            double content = windowWidth - sidebar.Width;
            bool wide = content >= 980;
            wideLayout = wide;
            dashTiles.Columns = content >= 1000 ? 4 : 2;
            Arrange2(controllerGrid, diagramPanel, inspectorHostController, wide, "*,16,380");
            Arrange2(paddleGrid, paddleDiagramPanel, paddleSidePanel, wide, "*,16,380");
            Arrange2(dashLower, dashConfigSection, dashQuickSection, content >= 900, "*,16,*");
            Arrange2(gamesGrid, gameListSection, gameDetailSection, content >= 900, "360,16,*");
            Arrange2(profilesGrid, profileListSection, profileEditSection, content >= 900, "360,16,*");
            Arrange2(networkGrid, netReceiveSection, netSendSection, content >= 900, "*,16,*");
            double h = Math.Clamp(content * 0.5, 360, 580);
            diagramHost.Height = wide ? Math.Clamp(content - 380 - 100, 360, 600) * 0.95 : Math.Clamp(content * 0.62, 320, 560);
            paddleDiagramHost.Height = wide ? Math.Clamp(content - 380 - 100, 320, 560) * 0.9 : Math.Clamp(content * 0.56, 300, 520);
            _ = h;
        }

        /// <summary>Lays two children side by side (wide) or one above the other (narrow) in the same grid.</summary>
        private static void Arrange2(Grid grid, Control first, Control second, bool wide, string wideColumns)
        {
            grid.ColumnDefinitions = new ColumnDefinitions(wide ? wideColumns : "*");
            grid.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            Grid.SetRow(first, 0); Grid.SetColumn(first, 0);
            Grid.SetRow(second, wide ? 0 : 1); Grid.SetColumn(second, wide ? 2 : 0);
            second.Margin = wide ? new Thickness(0) : new Thickness(0, 14, 0, 0);
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
                case "network": RefreshNetworkBadges(); break;
                case "settings": RefreshBlockBadge(); break;
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
            statusBarConfig.Text = activeGame.DisplayName + (string.IsNullOrEmpty(loadedProfileName) ? "" : $" · profile {loadedProfileName}");
            dashConnectButton.IsEnabled = !engine.IsConnected;
            dashDisconnectButton.IsEnabled = engine.IsConnected;
        }

        private void RefreshDashboard()
        {
            ControllerLayoutDef layout = SelectedControllerLayout();
            if (engine.IsConnected)
            {
                dashVirtualBadge.Set(BadgeState.Ok, "Connected");
                dashVirtualDetail.Text = ControllerLayouts.ForDevice(engine.Family).Title;
                dashOutputBadge.Set(BadgeState.Ok, engine.IsRemote ? "Sending to another PC" : "ViGEm (this PC)");
                dashOutputDetail.Text = engine.IsRemote ? $"{appSettings.RemoteHost}:{appSettings.RemotePort}" : "ViGEmBus virtual device";
            }
            else
            {
                dashVirtualBadge.Set(BadgeState.Off, "Not connected");
                dashVirtualDetail.Text = $"{layout.Title} selected";
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
            dashConfigDetail.Text = $"Loaded: {activeConfigSummary}";
            dashProfileText.Text = string.IsNullOrEmpty(loadedProfileName) ? "Profile: none loaded (profiles are files you load on the Profiles page)" : $"Profile: {loadedProfileName}";
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
            string key = $"{activeGame.Id}|{activeSource}|{activeConfigModified}|{ActiveLayout().Id}|{gameStore?.HasUser(activeGame.Id)}";
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
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = layout.Label(input), Classes = { "caption" }, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                var action = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                Grid.SetColumn(action, 1);
                row.Children.Add(action);
                rows.Add(row);
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

        private void UpdateProfileInfo()
        {
            if (profileActiveLabel == null) return;
            profileActiveLabel.Text = string.IsNullOrEmpty(loadedProfileName)
                ? $"Active profile: none (working from the {activeGame.DisplayName} config)"
                : $"Active profile: {loadedProfileName}";

            string name = profileComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { profileInfoLabel.Text = "Select a profile to see its details."; return; }
            try
            {
                string path = GetProfilePath(name);
                var info = new FileInfo(path);
                if (!info.Exists) { profileInfoLabel.Text = "That file no longer exists."; return; }
                string detail = "";
                try
                {
                    InputProfile p = JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(path));
                    if (p != null)
                        detail = $"Controller: {ControllerLayouts.Get(p.ControllerType).Title}\nGame config: {(string.IsNullOrEmpty(p.Game) ? "any (not tied to a game)" : GameCatalog.Get(p.Game).DisplayName)}\n";
                }
                catch { detail = "Could not read the profile contents.\n"; }
                profileInfoLabel.Text = detail + $"{info.Name} · {info.Length / 1024.0:0.#} KB · saved {info.LastWriteTime:g}";
            }
            catch (Exception ex) { profileInfoLabel.Text = ex.Message; }
        }
    }
}
