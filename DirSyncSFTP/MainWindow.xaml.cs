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
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using GlitchedPolygons.ExtensionMethods;
using Path = System.IO.Path;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using System.Windows.Documents;

namespace DirSyncSFTP
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private JsonPrefs jsonPrefs;
        private volatile bool adding = false;
        private volatile bool paused = false;
        private volatile bool quitting = false;
        private volatile bool synchronizing = false;
        
        private DateTime lastLogFileTruncateOp = DateTime.MinValue; 

        private readonly string baseDir;
        private readonly string filesListDir;
        private readonly string assemblyLocation;
        private readonly string logFile;
        private readonly string powershellSyncScriptFile;
        private readonly string powershellScanHostKeyFingerprintScriptFile;

        private readonly NotifyIcon notifyIcon;
        private readonly KnownHosts knownHosts;
        private readonly SynchronizedDirectories synchronizedDirectories;
        private readonly IDictionary<string, ProcessStartInfo> processStartInfoCache = new ConcurrentDictionary<string, ProcessStartInfo>();

        private static Mutex mutex = null!;

        public MainWindow()
        {
            InitializeComponent();

            assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

            mutex = new Mutex(true, nameof(DirSyncSFTP), out bool newInstance);

            if (!newInstance)
            {
                MessageBox.Show("There is already an instance of DirSyncSFTP.exe running... or crawling ;D");
                Application.Current.Shutdown();
            }

            Closing += (_, e) =>
            {
                if (CheckBoxMinimizeOnClose.IsChecked == true)
                {
                    e.Cancel = true;
                    Hide();

                    if (jsonPrefs?.GetBool(Constants.PrefKeys.TRAY_BALLOON_SHOWN) is false)
                    {
                        notifyIcon?.ShowBalloonTip(1024, "It's still running", "If you want to quit DirSyncSFTP, you can do so by right-clicking on its taskbar tray icon.", ToolTipIcon.Info);
                        jsonPrefs?.SetBool(Constants.PrefKeys.TRAY_BALLOON_SHOWN, true);
                    }
                }
                else
                {
                    Quit();
                }
            };

            Application.Current.Exit += (_, _) => { quitting = true; };

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);

            string compDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", versionInfo.CompanyName ?? "Glitched Polygons");

            if (!Directory.Exists(compDir))
            {
                Directory.CreateDirectory(compDir);
            }

            baseDir = Path.Combine(compDir, versionInfo.ProductName ?? "Temp");

            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            filesListDir = Path.Combine(baseDir, Constants.FILE_LISTS_DIRECTORY);

            if (!Directory.Exists(filesListDir))
            {
                Directory.CreateDirectory(filesListDir);
            }

            logFile = Path.Combine(baseDir, Constants.LOG_FILENAME);

            knownHosts = new KnownHosts(Path.Combine(baseDir, Constants.KNOWN_HOSTS_FILENAME));

            powershellSyncScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SYNC_SCRIPT_FILENAME);
            powershellScanHostKeyFingerprintScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT_FILENAME);

            CreateScriptFileIfNotExistsOrWrong(powershellSyncScriptFile, Constants.POWERSHELL_SYNC_SCRIPT);
            CreateScriptFileIfNotExistsOrWrong(powershellScanHostKeyFingerprintScriptFile, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT);

            knownHosts.Load();

            jsonPrefs = JsonPrefs.FromFile(Constants.CONFIG_FILENAME, true, new JsonSerializerOptions { WriteIndented = true });

            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MAJOR, version?.Major.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MINOR, version?.Minor.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_PATCH, version?.Build.ToString() ?? string.Empty);

            LabelVersionNumber.Content = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
            
            if (!jsonPrefs.HasKey(Constants.PrefKeys.CLIENT_ID))
            {
                jsonPrefs.SetString(Constants.PrefKeys.CLIENT_ID, Guid.NewGuid().ToString("D").ToUpperInvariant());
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.MAX_CONSOLE_OUTPUT_LINECOUNT))
            {
                jsonPrefs.SetInt(Constants.PrefKeys.MAX_CONSOLE_OUTPUT_LINECOUNT, 1024);
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.SYNC_DIRECTORIES))
            {
                jsonPrefs.SetString(Constants.PrefKeys.SYNC_DIRECTORIES, "{}".Protect());
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.SYNC_INTERVAL_MINUTES))
            {
                jsonPrefs.SetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, 15);
            }

            if (jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH).NullOrEmpty() || !File.Exists(jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH)))
            {
                if (File.Exists(@"C:\Program Files (x86)\WinSCP\WinSCPnet.dll"))
                {
                    jsonPrefs.SetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH, @"C:\Program Files (x86)\WinSCP\WinSCPnet.dll");
                }
                else if (File.Exists(Path.Combine(assemblyLocation, "WinSCPnet.dll")))
                {
                    jsonPrefs.SetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH, Path.Combine(assemblyLocation, "WinSCPnet.dll"));
                }
                else
                {
                    var dialog = new SelectWinScpAssemblyDialog();

                    if (dialog.ShowDialog() is true && File.Exists(dialog.AssemblyFilePath))
                    {
                        jsonPrefs.SetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH, dialog.AssemblyFilePath);
                    }
                    else
                    {
                        Close();
                    }
                }
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.MINIMIZE_TO_TRAY_ON_CLOSE))
            {
                jsonPrefs.SetBool(Constants.PrefKeys.MINIMIZE_TO_TRAY_ON_CLOSE, CheckBoxMinimizeOnClose.IsChecked == true);
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.AUTOSTART))
            {
                jsonPrefs.SetBool(Constants.PrefKeys.AUTOSTART, CheckBoxAutostart.IsChecked == true);
            }

            if (!jsonPrefs.HasKey(Constants.PrefKeys.START_MINIMIZED))
            {
                jsonPrefs.SetBool(Constants.PrefKeys.START_MINIMIZED, CheckBoxStartMinimized.IsChecked == true);
            }

            jsonPrefs.Save();

            TextBoxConsoleLog.Document.Blocks.Add(new Paragraph(new Run("Copyright (C) 2023 Raphael Beck\nThis is free, GPLv3-licensed software. Enjoy :D\n\n")));

            synchronizedDirectories = new SynchronizedDirectories(jsonPrefs);
            synchronizedDirectories.Load();

            if (synchronizedDirectories.Dictionary.NotNullNotEmpty())
            {
                foreach (string key in synchronizedDirectories.Dictionary.Keys)
                {
                    ListBoxSyncDirs.Items.Add(key);
                }
            }

            CheckBoxMinimizeOnClose.IsChecked = jsonPrefs.GetBool(Constants.PrefKeys.MINIMIZE_TO_TRAY_ON_CLOSE, true);
            CheckBoxAutostart.IsChecked = jsonPrefs.GetBool(Constants.PrefKeys.AUTOSTART, true);
            CheckBoxStartMinimized.IsChecked = jsonPrefs.GetBool(Constants.PrefKeys.START_MINIMIZED, true);

            SliderSyncIntervalMinutes.Value = Math.Clamp(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, 15), 1, 60);

            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = Constants.TRAY_TOOLTIP_IDLE,
                Icon = new System.Drawing.Icon(Path.Combine(assemblyLocation, "sftp.ico")),
                ContextMenuStrip = new ContextMenuStrip
                {
                    Items =
                    {
                        new ToolStripMenuItem("Open", null, TrayContextMenu_OnClickedOpen),
                        new ToolStripMenuItem("Force sync now", null, TrayContextMenu_OnClickedForceSyncNow),
                        new ToolStripMenuItem("Quit", null, TrayContextMenu_OnClickedQuit),
                    }
                }
            };

            notifyIcon.DoubleClick += TrayContextMenu_OnClickedOpen;

            ListBoxSyncDirs.SelectionChanged += ListBoxSyncDirsOnSelectionChanged;

            if (CheckBoxStartMinimized.IsChecked == true)
            {
                Hide();
            }

            Task.Run(Sync);
        }

        private void ExecuteOnUIThread(Action action)
        {
            Application.Current?.Dispatcher?.Invoke(action, DispatcherPriority.Normal);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private void CreateScriptFileIfNotExistsOrWrong(string scriptFilePath, string script)
        {
            if (!File.Exists(scriptFilePath))
            {
                File.WriteAllText(scriptFilePath, script);

                new FileInfo(scriptFilePath).IsReadOnly = true;
            }
            else if (File.ReadAllText(scriptFilePath).SHA256() != script.SHA256())
            {
                FileInfo powershellScriptFileInfo = new(scriptFilePath);

                powershellScriptFileInfo.IsReadOnly = false;
                {
                    File.WriteAllText(scriptFilePath, script);
                }
                powershellScriptFileInfo.IsReadOnly = true;
            }
        }

        private void Open()
        {
            Show();

            WindowState = WindowState.Normal;
        }

        private void Quit()
        {
            AppendLineToConsoleOutputTextBox("Quitting...");
            jsonPrefs?.Save();
            quitting = true;
        }
    }
}