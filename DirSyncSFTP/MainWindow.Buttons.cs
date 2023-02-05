/*
    DirSyncSFTP
    Copyright (C) 2023  Raphael Beck

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private async void ButtonAddNewSyncDir_OnClick(object sender, RoutedEventArgs e)
    {
        if (adding)
        {
            return;
        }

        adding = true;

        AppendLineToConsoleOutputTextBox("Adding new synchronized directory entry...");

        try
        {
            var dialog = new AddNewSynchronizedDirectoryDialog();

            if (dialog.ShowDialog() is true)
            {
                SynchronizedDirectory setup = dialog.SynchronizedDirectory;

                string newDictionaryKey = setup.GetDictionaryKey();

                if (setup.Host.NullOrEmpty())
                {
                    MessageBox.Show("ERROR: Host field empty. Please provide a synchronization remote host name (without schema, protocol or prefix - just the domain name)", "Invalid DirSync config");
                    return;
                }

                if (setup.Username.NullOrEmpty())
                {
                    MessageBox.Show("ERROR: Username field empty. Please provide valid credentials!", "Invalid DirSync config");
                    return;
                }

                if (setup.LocalDirectory.NullOrEmpty())
                {
                    MessageBox.Show("ERROR: Local directory field empty or points to invalid/nonexistent directory. Please choose a valid directory on your system to sync your files in!", "Invalid DirSync config");
                    return;
                }

                if (synchronizedDirectories.Dictionary.ContainsKey(newDictionaryKey))
                {
                    MessageBox.Show($"ERROR: You are already synchronizing the entry \"{newDictionaryKey}\".");
                    return;
                }

                if (synchronizedDirectories.Dictionary.Keys.Any(key => key[..key.LastIndexOf(':')].StartsWith(setup.LocalDirectory)))
                {
                    MessageBox.Show($"ERROR: You specified the local directory \"{setup.LocalDirectory}\", which is a subfolder of a directory that is already being synchronized by another entry.");
                    return;
                }

                AppendLineToConsoleOutputTextBox($"Requesting host key fingerprint from \"{setup.Host}:{setup.Port}\"...");

                string fingerprint = await ScanHostKeyFingerprint(setup.Host, setup.Port);

                if (fingerprint.NullOrEmpty())
                {
                    string errorMessage = $"ERROR: Couldn't retrieve host key fingerprint from \"{setup.Host}:{setup.Port}\" - it's safer (and actually required) to know the host's key fingerprint beforehand. Won't add the synchronized directory entry, for safety's sake!";
                    AppendLineToConsoleOutputTextBox(errorMessage);
                    MessageBox.Show(errorMessage);
                    return;
                }

                SaveFingerprintIfTrusted(setup.Host, setup.Port, fingerprint);

                synchronizedDirectories.Dictionary[newDictionaryKey] = setup;
                synchronizedDirectories.Save();

                ListBoxSyncDirs.Items.Add(newDictionaryKey);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(1024);
                    await PerformSync();
                });
            }
            else
            {
                AppendLineToConsoleOutputTextBox("Synchronized directory setup dialog cancelled.");
            }
        }
        catch (Exception exception)
        {
            AppendLineToConsoleOutputTextBox($"ERROR: Failed to add new synchronized directory entry. Thrown exception: {exception.ToString()}");
        }
        finally
        {
            adding = false;
        }
    }

    private void ButtonRemoveSelectedSyncDir_OnClick(object sender, RoutedEventArgs e)
    {
        if (ListBoxSyncDirs.SelectedIndex == -1)
        {
            return;
        }

        try
        {
            object selectedItem = ListBoxSyncDirs.SelectedItem;
            string selectedItemString = selectedItem.ToString() ?? string.Empty;

            if (synchronizedDirectories.Dictionary.ContainsKey(selectedItemString))
            {
                synchronizedDirectories.Dictionary.Remove(selectedItemString);
                synchronizedDirectories.Save();

                ListBoxSyncDirs.Items.Remove(selectedItem);
                ListBoxSyncDirs.UnselectAll();
            }
        }
        catch (Exception exception)
        {
            AppendLineToConsoleOutputTextBox($"ERROR: Failed to remove synchronized directory entry. Thrown exception: {exception.ToString()}");
        }
    }

    private void ButtonForceSyncNow_OnClick(object sender, RoutedEventArgs e)
    {
        ButtonForceSyncNow.IsEnabled = false;

        AppendLineToConsoleOutputTextBox("Force-sync initiated by user.");

        Task.Run(PerformSync).ContinueWith(async _ =>
        {
            await Task.Delay(4096);

            ExecuteOnUIThread(() => ButtonForceSyncNow.IsEnabled = true);
        });
    }

    private void ButtonTogglePause_OnClick(object sender, RoutedEventArgs e)
    {
        paused = !paused;

        ButtonTogglePause.IsEnabled = false;
        ButtonTogglePause.Content = paused ? "Resume" : "Pause";

        AppendLineToConsoleOutputTextBox($"User {(paused ? "paused" : "resumed")} automatic synchronization process.");

        Task.Run(async () =>
        {
            await Task.Delay(4096);

            ExecuteOnUIThread(() => ButtonTogglePause.IsEnabled = true);
        });
    }

    private void ButtonVisitGP_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://glitchedpolygons.com");
    }
}