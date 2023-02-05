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

using System.Windows;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private void CheckBoxMinimizeOnClose_OnChecked(object sender, RoutedEventArgs e)
    {
        jsonPrefs?.SetBool(Constants.PrefKeys.MINIMIZE_TO_TRAY_ON_CLOSE, CheckBoxMinimizeOnClose.IsChecked == true);
        jsonPrefs?.Save();
    }

    private void CheckBoxAutostart_OnChecked(object sender, RoutedEventArgs e)
    {
        jsonPrefs?.SetBool(Constants.PrefKeys.AUTOSTART, CheckBoxAutostart.IsChecked == true);
        jsonPrefs?.Save();
    }

    private void CheckBoxStartMinimized_OnChecked(object sender, RoutedEventArgs e)
    {
        jsonPrefs?.SetBool(Constants.PrefKeys.START_MINIMIZED, CheckBoxStartMinimized.IsChecked == true);
        jsonPrefs?.Save();
    }
}