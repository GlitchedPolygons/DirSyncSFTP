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
using System.Threading.Tasks;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private void TrayIcon_OnClick(object? sender, EventArgs args)
    {
        notifyIcon.ContextMenuStrip.Show(System.Windows.Forms.Cursor.Position);
        notifyIcon.ContextMenuStrip.Focus();
    }

    private void TrayContextMenu_OnClickedQuit(object? sender, EventArgs e)
    {
        Quit();
        Environment.Exit(0);
    }

    private void TrayContextMenu_OnClickedForceSyncNow(object? sender, EventArgs e)
    {
        Task.Run(PerformSync);
    }

    private void TrayContextMenu_OnClickedOpen(object? sender, EventArgs e)
    {
        Open();
    }
}