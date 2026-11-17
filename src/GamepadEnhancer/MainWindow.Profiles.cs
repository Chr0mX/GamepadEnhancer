using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;

namespace GamepadEnhancer
{
    /// <summary>
    /// The profile menu. One place (the header, plus a button on the Games page) holds every profile action:
    /// load, save, save as, duplicate, rename, delete, import, export, open folder. The header shows
    /// <c>Game · Profile</c> and a dot while the working setup differs from the loaded profile.
    /// </summary>
    public partial class MainWindow
    {
        private InputProfile profileSnapshot;     // what the loaded profile looks like once applied (to detect unsaved changes)
        private bool profileModified;

        private static readonly string ProtectedProfile = Path.GetFileNameWithoutExtension(DefaultProfile.FileName);

        private static bool IsProtectedProfile(string name) => string.Equals(name, ProtectedProfile, StringComparison.OrdinalIgnoreCase);

        private void InitializeProfileMenus()
        {
            headerConfigPill.Flyout = CreateProfileFlyout();
            profileMenuButton.Flyout = CreateProfileFlyout();
        }

        private MenuFlyout CreateProfileFlyout()
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            flyout.Opening += (_, _) => FillProfileMenu(flyout.Items);
            return flyout;
        }

        private string[] ProfileNames()
        {
            if (!Directory.Exists(profilesDirectory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(profilesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        private void FillProfileMenu(IList items)
        {
            items.Clear();
            bool has = !string.IsNullOrEmpty(loadedProfileName);
            items.Add(new MenuItem
            {
                Header = has ? $"Profile: {loadedProfileName}" + (profileModified ? "  (unsaved changes)" : "") : $"No profile loaded · working from {activeGame.DisplayName}",
                IsEnabled = false
            });
            items.Add(new Separator());
            string[] names = ProfileNames();
            if (names.Length == 0) items.Add(new MenuItem { Header = "No saved profiles yet", IsEnabled = false });
            foreach (string name in names)
            {
                string n = name;
                bool current = has && string.Equals(n, loadedProfileName, StringComparison.OrdinalIgnoreCase);
                items.Add(Item((current ? "● " : "    ") + n, () => LoadProfileByName(n)));
            }
            items.Add(new Separator());
            items.Add(Item("Save", () => SaveProfile(loadedProfileName), has));
            items.Add(Item("Save as…", () => SaveProfileAs()));
            items.Add(Item("Duplicate…", () => SaveProfileAs(loadedProfileName + " copy"), has));
            items.Add(Item("Rename…", () => RenameProfile(), has && !IsProtectedProfile(loadedProfileName)));
            items.Add(Item("Delete", () => DeleteProfile(), has && !IsProtectedProfile(loadedProfileName)));
            items.Add(new Separator());
            items.Add(Item("Import…", () => ImportProfile()));
            items.Add(Item("Export…", () => ExportProfile()));
            items.Add(Item("Open profiles folder", () => OpenProfilesFolderButton_Click(null, null)));
            items.Add(new Separator());
            items.Add(Item($"Reset {activeGame.DisplayName} to its defaults", () => RestoreGameDefaults_Click(null, null)));
        }

        /// <summary>Test hook: the menu's item headers as they would be shown now.</summary>
        internal List<string> ProfileMenuForTests()
        {
            var flyout = new MenuFlyout();
            FillProfileMenu(flyout.Items);
            return flyout.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToList();
        }

        internal string HeaderProfileTextForTests => statusBarConfig.Text;

        // ── State ────────────────────────────────────────────────────────────────

        /// <summary>Recomputes "differs from the loaded profile" and refreshes the header text.</summary>
        private void RefreshProfileState()
        {
            bool modified = false;
            if (!string.IsNullOrEmpty(loadedProfileName) && profileSnapshot != null && bindingControls.Count > 0)
            {
                try { modified = !GameProfiles.Equivalent(BuildProfileFromUi(loadedProfileName), profileSnapshot); }
                catch { modified = false; }
            }
            profileModified = modified;
            if (profileActiveLabel != null) UpdateProfileInfo();
            UpdateProfileHeaderText();
        }

        private void SetLoadedProfile(string name)
        {
            loadedProfileName = name;
            profileSnapshot = string.IsNullOrEmpty(name) ? null : BuildProfileFromUi(name);
            RefreshProfileState();
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private async void LoadProfileByName(string name)
        {
            string path = GetProfilePath(name);
            if (!File.Exists(path)) { await Dialogs.ShowMessageAsync(this, "Profiles", "That profile no longer exists."); return; }
            if (profileModified && !string.Equals(name, loadedProfileName, StringComparison.OrdinalIgnoreCase)
                && !await Dialogs.ConfirmAsync(this, "Unsaved changes", $"'{loadedProfileName}' has changes you have not saved. Load '{name}' and discard them?")) return;

            InputProfile profile;
            try { profile = JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(path)); }
            catch { profile = null; }
            if (profile == null) { await Dialogs.ShowMessageAsync(this, "Profiles", "Failed to read that profile."); return; }

            ApplyProfile(profile);
            SetLoadedProfile(name);
            statusLabel.Text = $"Status: Loaded profile '{name}'";
        }

        private async void SaveProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { SaveProfileAs(); return; }
            try { File.WriteAllText(GetProfilePath(name), JsonSerializer.Serialize(BuildProfileFromUi(name), ProfileJson)); }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Profiles", "Could not save the profile.\n\n" + ex.Message); return; }
            SetLoadedProfile(name);
            statusLabel.Text = $"Status: Saved profile '{name}'";
        }

        private async void SaveProfileAs(string suggested = null)
        {
            string name = await Dialogs.PromptAsync(this, "Save profile as", "Name for this profile:", suggested ?? loadedProfileName ?? "");
            if (name == null) return;
            name = SafeName(name, "Profile");
            if (File.Exists(GetProfilePath(name)) && !string.Equals(name, loadedProfileName, StringComparison.OrdinalIgnoreCase)
                && !await Dialogs.ConfirmAsync(this, "Replace profile", $"A profile named '{name}' already exists. Replace it?")) return;
            SaveProfile(name);
        }

        private async void RenameProfile()
        {
            string old = loadedProfileName;
            if (string.IsNullOrEmpty(old) || IsProtectedProfile(old)) return;
            string name = await Dialogs.PromptAsync(this, "Rename profile", $"New name for '{old}':", old);
            if (name == null) return;
            name = SafeName(name, old);
            if (string.Equals(name, old, StringComparison.Ordinal)) return;
            bool caseOnly = string.Equals(name, old, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && File.Exists(GetProfilePath(name)))
            { await Dialogs.ShowMessageAsync(this, "Rename", $"A profile named '{name}' already exists."); return; }
            try
            {
                string from = GetProfilePath(old), to = GetProfilePath(name);
                if (File.Exists(from)) File.Move(from, to);
                else File.WriteAllText(to, JsonSerializer.Serialize(BuildProfileFromUi(name), ProfileJson));
            }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Rename", "Could not rename the profile.\n\n" + ex.Message); return; }
            loadedProfileName = name;
            if (profileSnapshot != null) profileSnapshot.Name = name;
            RefreshProfileState();
            statusLabel.Text = $"Status: Renamed profile '{old}' to '{name}'";
        }

        private async void DeleteProfile()
        {
            string name = loadedProfileName;
            if (string.IsNullOrEmpty(name) || IsProtectedProfile(name)) return;
            if (!await Dialogs.ConfirmAsync(this, "Delete profile", $"Delete the profile '{name}'? Your current settings stay as they are, but no profile is loaded afterwards.")) return;
            try { File.Delete(GetProfilePath(name)); }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Profiles", "Could not delete the profile.\n\n" + ex.Message); return; }
            SetLoadedProfile(null);
            statusLabel.Text = $"Status: Deleted profile '{name}'";
        }

        /// <summary>Writes the current mapping to a file you choose, so it can be shared or edited by hand.</summary>
        private async void ExportProfile()
        {
            string name = string.IsNullOrEmpty(loadedProfileName) ? "profile" : loadedProfileName;
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
                statusLabel.Text = $"Status: Exported '{name}'";
            }
            catch (Exception ex) { await Dialogs.ShowMessageAsync(this, "Export", "Could not write the file.\n\n" + ex.Message); }
        }

        /// <summary>Reads a profile file, clears anything invalid (and says what), saves it into profiles/ and applies it.</summary>
        private async void ImportProfile()
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
            ApplyProfile(profile);
            SetLoadedProfile(name);
            statusLabel.Text = $"Status: Imported profile '{name}'" + (warnings.Count > 0 ? $" ({warnings.Count} value(s) cleared)" : "");
            if (warnings.Count > 0)
                await Dialogs.ShowMessageAsync(this, "Import", "The profile was imported, but some values were not valid and were cleared:\n\n" + string.Join("\n", warnings.Take(12)));
        }
    }
}
