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

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private void ListBoxSyncDirsOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ButtonRemoveSelectedSyncDir.IsEnabled = ListBoxSyncDirs.SelectedItem is not null;
    }

    private void SynchronizedDirectory_ContextMenu_OnClickOpenDir(object sender, RoutedEventArgs e)
    {
        if (ListBoxSyncDirs.SelectedIndex == -1)
        {
            return;
        }

        object selectedItem = ListBoxSyncDirs.SelectedItem;
        string selectedItemString = selectedItem.ToString() ?? string.Empty;

        if (synchronizedDirectories.Dictionary.TryGetValue(selectedItemString, out SynchronizedDirectory? synchronizedDirectory) && Directory.Exists(synchronizedDirectory.LocalDirectory))
        {
            Process.Start("Explorer.exe", synchronizedDirectory.LocalDirectory);
        }
    }

    private void SynchronizedDirectory_ContextMenu_OnClickScanFingerprint(object sender, RoutedEventArgs e)
    {
        if (ListBoxSyncDirs.SelectedIndex == -1)
        {
            return;
        }

        object selectedItem = ListBoxSyncDirs.SelectedItem;
        string selectedItemString = selectedItem.ToString() ?? string.Empty;

        Task.Run(async () =>
        {
            if (!synchronizedDirectories.Dictionary.TryGetValue(selectedItemString, out SynchronizedDirectory? synchronizedDirectory))
            {
                return;
            }

            string fingerprint = await ScanHostKeyFingerprint(synchronizedDirectory.Host, synchronizedDirectory.Port);

            SaveFingerprintIfTrusted(synchronizedDirectory.Host, synchronizedDirectory.Port, fingerprint);
        });
    }
}