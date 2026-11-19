using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// The OUTPUT tab: which driver creates the virtual controller (ViGEm by default, or the optional VHF driver), which VHF profile,
    /// whether the driver is installed / running / signed, and an explicit, checked install and removal. The app never bundles,
    /// builds, signs or silently installs a driver.
    /// </summary>
    public partial class MainWindow
    {
        private VhfDriverInspector vhfInspector;
        private VhfPackageVerifier vhfVerifier;
        private VhfPackageReport vhfPackage;          // the last package that passed the checks
        private VhfDriverStatus vhfStatus;
        private volatile VhfFeedback lastVhfFeedback;
        private bool suppressOutputChange, vhfBusy;
        private PadFamily? vhfProfileFamily;
        private VhfProfileId connectedVhfProfile;

        /// <summary>Lets the UI tests create the VHF pad over a fake driver instead of the real one.</summary>
        internal Func<IVhfTransport> VhfTransportFactoryForTests;

        /// <summary>True when the VHF backend is what Connect will use (never while sending to another PC, which has no local driver).</summary>
        private bool VhfSelected =>
            string.Equals(appSettings.OutputBackend, "vhf", StringComparison.OrdinalIgnoreCase) && !appSettings.RemoteSendEnabled;

        private VhfTarget CurrentVhfTarget()
        {
            connectedVhfProfile = VhfProfiles.Resolve(appSettings.VhfProfile, SelectedControllerLayout().Family);
            return new VhfTarget(connectedVhfProfile, VhfTransportFactoryForTests);
        }

        private Func<IVhfTransport> TransportFactory => VhfTransportFactoryForTests ?? (() => new Win32VhfTransport());

        // ── Startup ─────────────────────────────────────────────────────────────

        private void InitializeOutput()
        {
            vhfInspector = new VhfDriverInspector(new SetupApiDeviceInfoSource(), () => TransportFactory(), new WinTrustSignatureReader());
            vhfVerifier = new VhfPackageVerifier(new WinTrustSignatureReader());
            engine.FeedbackReceived += feedback => lastVhfFeedback = feedback;   // engine thread: only stores the reference

            suppressOutputChange = true;
            try
            {
                outputBackendBox.ItemsSource = new[]
                {
                    new ComboBoxItem { Content = "ViGEm (default)", Tag = "vigem" },
                    new ComboBoxItem { Content = "VHF driver (optional)", Tag = "vhf" }
                };
                outputBackendBox.SelectedIndex = string.Equals(appSettings.OutputBackend, "vhf", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            finally { suppressOutputChange = false; }

            RefillVhfProfiles();
            UpdateOutputRows();
            if (!string.IsNullOrWhiteSpace(appSettings.VhfPackagePath))
                vhfPackageLabel.Text = "Last package folder: " + appSettings.VhfPackagePath + ". It is checked again before every install; choose it again to verify it now.";
            UpdateVhfButtons();
        }

        private void UpdateOutputRows()
        {
            if (vhfProfileRow != null) vhfProfileRow.IsVisible = string.Equals(appSettings.OutputBackend, "vhf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The profile choices follow the layout family: Xbox layouts offer the Xbox profiles, PlayStation layouts the PlayStation ones.</summary>
        private void RefillVhfProfiles()
        {
            if (vhfProfileBox == null || appSettings == null) return;
            PadFamily family = SelectedControllerLayout().Family;
            if (vhfProfileFamily == family) return;
            vhfProfileFamily = family;

            var choices = VhfProfiles.For(family);
            string current = VhfProfiles.IdOf(VhfProfiles.Resolve(appSettings.VhfProfile, family));
            suppressOutputChange = true;
            try
            {
                vhfProfileBox.ItemsSource = choices.Select(c => new ComboBoxItem { Content = c.Name, Tag = c.Id }).ToList();
                vhfProfileBox.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(c => c.Id == current));
            }
            finally { suppressOutputChange = false; }
        }

        // ── Choices ─────────────────────────────────────────────────────────────

        private async void OutputBackend_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressOutputChange) return;
            string tag = (outputBackendBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "vigem";
            if (string.Equals(tag, appSettings.OutputBackend, StringComparison.OrdinalIgnoreCase)) return;
            appSettings.OutputBackend = tag;
            appSettings.Save();
            UpdateOutputRows();
            RefreshVhfStatus();
            RefreshShellStatus();
            // like changing the controller type: a connected pad is replaced in place, so what is exposed always matches the choice
            if (engine.IsConnected && !engine.IsRemote) await ConnectNowAsync();
            else UpdateActiveControllerLabel();
        }

        private async void VhfProfile_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || suppressOutputChange) return;
            string tag = (vhfProfileBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (tag == null || tag == appSettings.VhfProfile) return;
            appSettings.VhfProfile = tag;
            appSettings.Save();
            if (engine.IsConnected && engine.IsVhf) await ConnectNowAsync();
        }

        // ── Status ──────────────────────────────────────────────────────────────

        private void VhfRefresh_Click(object sender, RoutedEventArgs e) => RefreshVhfStatus();

        /// <summary>Asks Windows and the driver in the background (SetupAPI and the handshake can take a moment) and shows the answer.</summary>
        internal void RefreshVhfStatus()
        {
            if (vhfInspector == null) return;
            bool owned = engine.IsConnected && engine.IsVhf;
            vhfDriverBadge.Set(BadgeState.Warn, "Checking the driver...");
            Task.Run(() => vhfInspector.Inspect(owned)).ContinueWith(t =>
                Dispatcher.UIThread.Post(() => ApplyVhfStatus(t.IsFaulted
                    ? new VhfDriverStatus { State = VhfDriverState.Error, Headline = "The driver could not be checked: " + t.Exception.GetBaseException().Message }
                    : t.Result)));
        }

        /// <summary>Test hook: the same check, on this thread.</summary>
        internal void RefreshVhfStatusNow() => ApplyVhfStatus(vhfInspector.Inspect(engine.IsConnected && engine.IsVhf));

        private void ApplyVhfStatus(VhfDriverStatus status)
        {
            vhfStatus = status;
            BadgeState badge = status.State switch
            {
                VhfDriverState.Ready or VhfDriverState.InUse => BadgeState.Ok,
                VhfDriverState.InstalledNotRunning or VhfDriverState.ProtocolMismatch => BadgeState.Warn,
                VhfDriverState.Error => BadgeState.Error,
                _ => BadgeState.Off
            };
            vhfDriverBadge.Set(badge, status.Headline);
            vhfDriverVersionLabel.Text = status.VersionLine;
            vhfDriverSignatureLabel.Text = status.SignatureLine;
            UpdateVhfButtons();
            UpdateVhfControllerLabel();
        }

        private void UpdateVhfControllerLabel()
        {
            if (vhfControllerLabel == null) return;
            if (engine.IsConnected && engine.IsVhf)
            {
                vhfControllerLabel.Text = $"Virtual controller: {VhfProfiles.NameOf(connectedVhfProfile)} created through VHF.";
                VhfFeedback fb = lastVhfFeedback;
                vhfFeedbackLabel.Text = fb == null ? "No output from a game yet (rumble and lights are shown here, not forwarded to hardware)." : "Last output from a game: " + fb;
            }
            else
            {
                vhfControllerLabel.Text = VhfSelected
                    ? "Virtual controller: not connected. Press Connect to create it through VHF."
                    : "Virtual controller: VHF is not the selected output.";
                vhfFeedbackLabel.Text = "";
            }
        }

        private void UpdateVhfButtons()
        {
            if (vhfInstallButton == null) return;
            bool packageOk = vhfPackage != null && vhfPackage.Ok;
            vhfInstallButton.IsEnabled = packageOk && !vhfBusy;
            vhfRemoveButton.IsEnabled = packageOk && !vhfBusy && vhfStatus != null && vhfStatus.State != VhfDriverState.NotInstalled;
            vhfChooseFolderButton.IsEnabled = !vhfBusy;
            vhfChooseZipButton.IsEnabled = !vhfBusy;
        }

        // ── Choosing and checking a package ─────────────────────────────────────

        private async void VhfChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the driver package folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            string path = folders[0].TryGetLocalPath();
            if (path == null) { await Dialogs.ShowMessageAsync(this, "Driver package", "That location is not a folder on this PC."); return; }
            await CheckVhfPackageAsync(path);
        }

        private async void VhfChooseZip_Click(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose the driver package (.zip)",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Driver package") { Patterns = new[] { "*.zip" } } }
            });
            if (files.Count == 0) return;
            string zip = files[0].TryGetLocalPath();
            if (zip == null) { await Dialogs.ShowMessageAsync(this, "Driver package", "That location is not a file on this PC."); return; }

            string folder = Path.Combine(Path.GetTempPath(), "GamepadEnhancer-vhf-" + Guid.NewGuid().ToString("N"));
            try { VhfPackageVerifier.ExtractZip(zip, folder); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                await Dialogs.ShowMessageAsync(this, "Driver package", "The .zip could not be unpacked safely, so nothing was checked or installed.\n\n" + ex.Message);
                return;
            }
            await CheckVhfPackageAsync(folder);
        }

        private async Task CheckVhfPackageAsync(string folder)
        {
            VhfPackageReport report = VerifyVhfPackage(folder);
            if (!report.Ok) await Dialogs.ShowMessageAsync(this, "Driver package refused", report.Summary());
        }

        /// <summary>Checks a package folder and records the result (used by the buttons and the tests).</summary>
        internal VhfPackageReport VerifyVhfPackage(string folder)
        {
            VhfPackageReport report = vhfVerifier.Verify(folder);
            vhfPackage = report.Ok ? report : null;
            appSettings.VhfPackagePath = report.Ok ? folder : "";
            appSettings.Save();
            vhfPackageLabel.Text = report.Summary();
            UpdateVhfButtons();
            return report;
        }

        // ── Install and remove (always explicit, always through the Windows administrator prompt) ──

        private async void VhfInstall_Click(object sender, RoutedEventArgs e)
        {
            if (vhfBusy || vhfPackage == null) return;
            if (engine.IsConnected && engine.IsVhf) { await Dialogs.ShowMessageAsync(this, "Install driver", "Disconnect the virtual controller first."); return; }

            // checked again right now: the folder may have changed since it was chosen
            VhfPackageReport fresh = vhfVerifier.Verify(vhfPackage.Folder);
            if (!fresh.Ok)
            {
                vhfPackage = null;
                UpdateVhfButtons();
                await Dialogs.ShowMessageAsync(this, "Driver package refused", fresh.Summary());
                return;
            }
            bool yes = await Dialogs.ConfirmAsync(this, "Install the VHF driver",
                "Install the VHF virtual gamepad driver?\n\n" + fresh.Summary() + "\n\nSetup tool: " + fresh.SetupToolPath +
                "\n\nWindows will ask for administrator permission and shows who signed the setup tool. Secure Boot and driver signing settings are not changed.");
            if (!yes) return;
            await RunVhfSetupAsync(VhfInstaller.InstallStartInfo(fresh), "install");
        }

        private async void VhfRemove_Click(object sender, RoutedEventArgs e)
        {
            if (vhfBusy || vhfPackage == null) return;
            if (engine.IsConnected && engine.IsVhf) { await Dialogs.ShowMessageAsync(this, "Remove driver", "Disconnect the virtual controller first."); return; }
            VhfPackageReport fresh = vhfVerifier.Verify(vhfPackage.Folder);
            if (!fresh.Ok)
            {
                vhfPackage = null;
                UpdateVhfButtons();
                await Dialogs.ShowMessageAsync(this, "Driver package refused", fresh.Summary());
                return;
            }
            bool yes = await Dialogs.ConfirmAsync(this, "Remove the VHF driver",
                "Remove the VHF virtual gamepad device using this package's setup tool?\n\n" + fresh.SetupToolPath +
                "\n\nWindows will ask for administrator permission. Programs using the VHF controller will lose it. ViGEm is not affected.");
            if (!yes) return;
            await RunVhfSetupAsync(VhfInstaller.RemoveStartInfo(fresh), "remove");
        }

        private async Task RunVhfSetupAsync(ProcessStartInfo start, string what)
        {
            vhfBusy = true;
            UpdateVhfButtons();
            vhfPackageLabel.Text = "Running the setup tool (" + what + "). Answer the Windows administrator prompt...";
            string message;
            try
            {
                int code = await VhfInstaller.RunAsync(start);
                message = code == 0
                    ? "The setup tool finished (" + what + "). The driver status above shows the result; Windows may ask for a restart."
                    : "The setup tool reported error code " + code + " (" + what + "). Use Check driver to see the current state.";
            }
            catch (OperationCanceledException ex) { message = ex.Message; }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            { message = "The setup tool could not be run: " + ex.Message; }
            finally
            {
                vhfBusy = false;
                UpdateVhfButtons();
            }
            vhfPackageLabel.Text = message;
            RefreshVhfStatus();
            await Dialogs.ShowMessageAsync(this, "Driver " + what, message);
        }

        // ── Test hooks ──────────────────────────────────────────────────────────

        internal string OutputBackendForTests => appSettings.OutputBackend;
        internal void SelectOutputBackendForTests(string tag) =>
            outputBackendBox.SelectedItem = ((System.Collections.Generic.IEnumerable<ComboBoxItem>)outputBackendBox.ItemsSource).First(i => (string)i.Tag == tag);
        internal void ShowNetworkTabForTests(string tag) => ApplyNetworkTab(tag);
        internal bool OutputSectionVisibleForTests => netOutputSection.IsVisible;
        internal bool VhfProfileRowVisibleForTests => vhfProfileRow.IsVisible;
        internal string VhfBadgeTextForTests => vhfDriverBadge.Text;
        internal BadgeState VhfBadgeStateForTests => vhfDriverBadge.State;
        internal string VhfVersionTextForTests => vhfDriverVersionLabel.Text;
        internal string VhfSignatureTextForTests => vhfDriverSignatureLabel.Text;
        internal string VhfControllerTextForTests => vhfControllerLabel.Text;
        internal string VhfPackageTextForTests => vhfPackageLabel.Text;
        internal bool VhfInstallEnabledForTests => vhfInstallButton.IsEnabled;
        internal string[] VhfProfileChoicesForTests => ((System.Collections.Generic.IEnumerable<ComboBoxItem>)vhfProfileBox.ItemsSource).Select(i => (string)i.Tag).ToArray();
        internal string HeaderOutputTagForTests => tagOutputText.Text;
        internal string ActiveControllerTextForTests => activeControllerLabel.Text;
        internal Task ConnectForTests() => ConnectNowAsync();
        internal void DisconnectForTests() => DisconnectButton_Click(null, null);
        internal ControllerEngine EngineForTests => engine;
        internal AppSettings SettingsForTests => appSettings;
    }
}
