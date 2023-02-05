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
using System.Diagnostics;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private void AppendLineToConsoleOutputTextBox(string line)
    {
        ExecuteOnUIThread(() =>
        {
            if (TextBoxConsoleLog.Text.Split('\n').Length > jsonPrefs.GetInt(Constants.PrefKeys.MAX_CONSOLE_OUTPUT_LINECOUNT, 1024))
            {
                TextBoxConsoleLog.Text = "(truncated old entries...)\n\n";
            }

            if (line.EndsWith('\n'))
            {
                line = line.TrimEnd('\n');
            }

            if (line.NotNullNotEmpty())
            {
                TextBoxConsoleLog.Text += $"[{DateTime.Now.ToString("dd. MMM. yyyy HH:mm:ss")}]\n{line}\n\n";
            }

            TextBoxConsoleLog.ScrollToEnd();
        });
    }

    private void OnProcessOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is not null)
        {
            AppendLineToConsoleOutputTextBox(eventArgs.Data);
        }
    }

    private void OnProcessErrorDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is not null)
        {
            AppendLineToConsoleOutputTextBox($"ERROR: {eventArgs.Data}");
        }
    }
}