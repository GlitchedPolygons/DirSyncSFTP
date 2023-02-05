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
using System.Windows;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private void SliderSyncIntervalMinutes_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int minutes = Math.Clamp((int)SliderSyncIntervalMinutes.Value, 1, 60);

        SliderSyncIntervalMinutes.Value = minutes;
        LabelSyncFrequencySlider.Content = $"Synchronization frequency: every {(minutes == 1 ? "minute" : $"{minutes} minutes")}";

        jsonPrefs?.SetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, minutes);
    }
}