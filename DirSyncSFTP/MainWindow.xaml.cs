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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using GlitchedPolygons.ExtensionMethods;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace DirSyncSFTP
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private JsonPrefs jsonPrefs;
        private volatile bool quitting = false;

        private readonly string baseDir;
        private readonly string filesListDir;
        private readonly string powershellSyncScriptFile;
        private readonly string powershellScanHostKeyFingerprintScriptFile;

        private readonly KnownHosts knownHosts;
        private readonly SynchronizedDirectories synchronizedDirectories;
        private readonly IDictionary<string, ProcessStartInfo> processStartInfoCache = new ConcurrentDictionary<string, ProcessStartInfo>();

        public MainWindow()
        {
            InitializeComponent();

            var mutex = new Mutex(true, nameof(DirSyncSFTP), out bool newInstance);

            if (!newInstance)
            {
                MessageBox.Show("There is already an instance of DirSyncSFTP.exe running... or crawling ;D"); // TODO: use dialog here that blocks everything!
                Application.Current.Shutdown();
            }

            Closing += (_, _) =>
            {
                jsonPrefs?.Save();
                quitting = true;
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

            knownHosts = new KnownHosts(Path.Combine(baseDir, Constants.KNOWN_HOSTS_FILENAME));
            powershellSyncScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SYNC_SCRIPT_FILENAME);
            powershellScanHostKeyFingerprintScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT_FILENAME);

            CreateScriptFileIfNotExistsOrWrong(powershellSyncScriptFile, Constants.POWERSHELL_SYNC_SCRIPT);
            CreateScriptFileIfNotExistsOrWrong(powershellScanHostKeyFingerprintScriptFile, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT);

            knownHosts.Load();

            jsonPrefs = JsonPrefs.FromFile(Constants.CONFIG_FILENAME, true, new JsonSerializerOptions { WriteIndented = true });

            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MAJOR, version?.Major.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MINOR, version?.Minor.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_PATCH, version?.Revision.ToString() ?? string.Empty);

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
                jsonPrefs.SetString(Constants.PrefKeys.SYNC_DIRECTORIES, "[]".Protect());
            }

            if (jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH).NullOrEmpty() || !File.Exists(jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH)))
            {
                if (File.Exists(@"C:\Program Files (x86)\WinSCP\WinSCPnet.dll"))
                {
                    jsonPrefs.SetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH, @"C:\Program Files (x86)\WinSCP\WinSCPnet.dll");
                }
                else if (File.Exists("WinSCPnet.dll"))
                {
                    jsonPrefs.SetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH, "WinSCPnet.dll");
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

            jsonPrefs.Save();

            TextBoxConsoleLog.Text = "Copyright (C) 2023 Raphael Beck\nThis is free, GPLv3-licensed software. Enjoy :D\n\n";

            synchronizedDirectories = new SynchronizedDirectories(jsonPrefs);
            synchronizedDirectories.Load();

            SliderSyncInterval.Value = Math.Clamp(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, 15), 1, 60);

            using NotifyIcon notifyIcon = new();

            notifyIcon.Icon = new System.Drawing.Icon("sftp.ico");
            notifyIcon.Visible = true;
            notifyIcon.Click += OnNotifyIconClick;

            _ = Task.Run(Sync);
        }

        private void ExecuteOnUIThread(Action action)
        {
            Application.Current?.Dispatcher?.Invoke(action, DispatcherPriority.Normal);
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

        private void OnNotifyIconClick(object? sender, EventArgs args)
        {
            Show();

            WindowState = WindowState.Normal;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private async Task<string> ScanHostKeyFingerprint(string hostName, int portNumber = 22)
        {
            string args =
                $"-NoProfile -ExecutionPolicy ByPass & '{powershellScanHostKeyFingerprintScriptFile}' -assemblyPath '{jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH)}' -hostName {hostName} -portNumber {portNumber} ";

            string argsHash = args.SHA1();

            if (!processStartInfoCache.TryGetValue(argsHash, out var processStartInfo))
            {
                processStartInfoCache[argsHash] = processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
            }

            using var process = Process.Start(processStartInfo);

            if (process is null)
            {
                return string.Empty;
            }

            await process.WaitForExitAsync();

            string stderr = await process.StandardError.ReadToEndAsync();
            string stdout = await process.StandardOutput.ReadToEndAsync();

            if (stderr.NotNullNotEmpty())
            {
                TextBoxConsoleLog.Text += stderr; // todo: fix
            }

            return stdout.NotNullNotEmpty()
                ? stdout.Trim()
                : string.Empty;
        }

        private void AppendLineToConsoleOutputTextBox(string line)
        {
            ExecuteOnUIThread(() =>
            {
                if (TextBoxConsoleLog.Text.Split('\n').Length > jsonPrefs.GetInt(Constants.PrefKeys.MAX_CONSOLE_OUTPUT_LINECOUNT, 1024))
                {
                    TextBoxConsoleLog.Text = string.Empty;
                }

                if (line.EndsWith('\n'))
                {
                    line = line.TrimEnd('\n');
                }

                if (line.NotNullNotEmpty())
                {
                    TextBoxConsoleLog.Text += $"[{DateTime.Now.ToString("dd. MMM. yyyy HH:mm")}]\n{line}\n\n";
                }

                TextBoxConsoleLog.ScrollToEnd();
            });
        }

        private async Task Sync()
        {
            await Task.Delay(1024);

            while (!quitting)
            {
                jsonPrefs.SetLong(Constants.PrefKeys.LAST_SYNC_UTC, DateTime.UtcNow.ToUnixTimeSeconds());
                await jsonPrefs.SaveAsync();

                if (synchronizedDirectories.Dictionary.NullOrEmpty())
                {
                    AppendLineToConsoleOutputTextBox($"List of synchronized directories is empty. Add a new entry to get started!");
                }
                else
                {
                    foreach ((string key, var synchronizedDirectory) in synchronizedDirectories.Dictionary)
                    {
                        try
                        {
                            AppendLineToConsoleOutputTextBox($"Synchronizing {key}");

                            if (!Directory.Exists(synchronizedDirectory.LocalDirectory))
                            {
                                AppendLineToConsoleOutputTextBox($"ERROR: Local directory \"{synchronizedDirectory.LocalDirectory}\" not found!");
                                continue;
                            }

                            if (!knownHosts.Dictionary.TryGetValue($"{synchronizedDirectory.Host}:{synchronizedDirectory.Port}", out string? fingerprint))
                            {
                                AppendLineToConsoleOutputTextBox($"ERROR: Key fingerprint for host \"{synchronizedDirectory.Host}:{synchronizedDirectory.Port}\" not found in local cache!");
                                continue;
                            }

                            AppendLineToConsoleOutputTextBox("Synchronizing... Please be patient: depending on how big the directory trees are this might take a while!");

                            string args =
                                $"-NoProfile -ExecutionPolicy ByPass & '{powershellSyncScriptFile}' -assemblyPath '{jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH)}' -hostName '{synchronizedDirectory.Host}' -portNumber '{synchronizedDirectory.Port}' -username '{synchronizedDirectory.Username}' -password '{synchronizedDirectory.Password}' -fingerprint '{fingerprint}' -localPath '{synchronizedDirectory.LocalDirectory}' -remotePath '{synchronizedDirectory.RemoteDirectory}' -listPath '{Path.Combine(filesListDir, synchronizedDirectory.GetDictionaryKey().SHA256())}' -sshKey '{synchronizedDirectory.SshKeyFilePath}' -sshKeyPassphrase '{synchronizedDirectory.SshKeyPassphrase}' ";

                            string argsHash = args.SHA256();

                            if (!processStartInfoCache.TryGetValue(argsHash, out var processStartInfo))
                            {
                                processStartInfoCache[argsHash] = processStartInfo = new ProcessStartInfo
                                {
                                    FileName = "powershell.exe",
                                    Arguments = args,
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                    RedirectStandardError = true,
                                    RedirectStandardOutput = true,
                                };
                            }

                            using var process = Process.Start(processStartInfo);

                            if (process is null)
                            {
                                continue;
                            }

                            process.ErrorDataReceived += OnProcessErrorDataReceived;
                            process.OutputDataReceived += OnProcessOutputDataReceived;

                            process.BeginErrorReadLine();
                            process.BeginOutputReadLine();

                            while (!process.HasExited)
                            {
                                await Task.Delay(512);
                            }
                        }
                        catch (Exception e)
                        {
                            AppendLineToConsoleOutputTextBox($"ERROR while synchronizing \"{key}\" => {e.ToString()}");
                        }
                    }
                }
#if DEBUG
                await Task.Delay(TimeSpan.FromSeconds(15));
#else
                await Task.Delay(TimeSpan.FromMinutes(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES)));
#endif
            }
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

        private void RangeBase_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int minutes = Math.Clamp((int)SliderSyncInterval.Value, 1, 60);

            SliderSyncInterval.Value = minutes;
            LabelSyncFrequencySlider.Content = $"Synchronization frequency: every {(minutes == 1 ? "minute" : $"{minutes} minutes")}";

            jsonPrefs?.SetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, minutes);
        }

        private void ButtonVisitGP_OnClick(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://glitchedpolygons.com");
        }

        /// <summary>
        /// Opens a URL in the browser.
        /// </summary>
        /// <param name="url">URL to open with browser.</param>
        /// <remarks>Source: https://stackoverflow.com/a/43232486/10291689 (last accessed: 03. Feb. 2023, 14:25 Swiss time)</remarks>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");

                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        private void ButtonAddNewSyncDir_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new AddNewSynchronizedDirectoryDialog();

            if (dialog.ShowDialog() is true)
            {
                // todo: check if dir is already tracked and show err dialog popup if so
            }
            else
            {
                AppendLineToConsoleOutputTextBox("Synchronized directory setup dialog cancelled.");
            }
        }

        private void ButtonRemoveSelectedSyncDir_OnClick(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}