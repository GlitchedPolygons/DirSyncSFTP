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
        private readonly string powershellSyncScriptFile;
        private readonly string powershellScanHostKeyFingerprintScriptFile;
        private readonly IDictionary<string, string> knownHosts = new ConcurrentDictionary<string, string>();
        private readonly IDictionary<string, ProcessStartInfo> processStartInfoCache = new ConcurrentDictionary<string, ProcessStartInfo>();

        // TODO: implement configurability for custom directory paths (local and remote, a list of those, allow unlimited entries here and sync unliiiimited directories).

        // TODO: implement private key auth and their passphrases

        public MainWindow()
        {
            InitializeComponent();

            var mutex = new Mutex(true, "DIRSYNCSFTP", out bool newInstance);

            if (!newInstance)
            {
                MessageBox.Show("There is already an instance of DirSyncSFTP.exe running... or crawling ;D");
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

            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", versionInfo.CompanyName ?? "Glitched Polygons", versionInfo.ProductName ?? "Temp");

            powershellSyncScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SYNC_SCRIPT_FILENAME);
            powershellScanHostKeyFingerprintScriptFile = Path.Combine(baseDir, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT_FILENAME);

            CreateScriptFileIfNotExistsOrWrong(powershellSyncScriptFile, Constants.POWERSHELL_SYNC_SCRIPT);
            CreateScriptFileIfNotExistsOrWrong(powershellScanHostKeyFingerprintScriptFile, Constants.POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT);

            if (!File.Exists(Constants.KNOWN_HOSTS_FILENAME))
            {
                File.WriteAllText(Constants.KNOWN_HOSTS_FILENAME, "{}");
            }

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

            SliderSyncInterval.Value = Math.Clamp(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES, 15), 1, 60);

            using NotifyIcon notifyIcon = new();

            notifyIcon.Icon = new System.Drawing.Icon("sftp.ico");
            notifyIcon.Visible = true;
            notifyIcon.Click += OnNotifyIconClick;

            _ = Task.Run(Sync);
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

        private void ExecuteOnUIThread(Action action)
        {
            Application.Current?.Dispatcher?.Invoke(action, DispatcherPriority.Normal);
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
                    TextBoxConsoleLog.Text += $"{line}\n\n";
                }

                TextBoxConsoleLog.ScrollToEnd();
            });
        }

        private async Task Sync()
        {
            await Task.Delay(1024);

            while (!quitting)
            {
                try
                {
                    jsonPrefs.SetLong(Constants.PrefKeys.LAST_SYNC_UTC, DateTime.UtcNow.ToUnixTimeSeconds());
                    await jsonPrefs.SaveAsync();

                    AppendLineToConsoleOutputTextBox("Synchronizing... Please be patient: depending on how big the directory trees are this might take a while!");

                    string args =
                        

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
                        goto endOfLoop;
                    }

                    while (!process.HasExited)
                    {
                        await Task.Delay(512);
                    }

                    string stderr = await process.StandardError.ReadToEndAsync();
                    string stdout = await process.StandardOutput.ReadToEndAsync();

                    AppendLineToConsoleOutputTextBox($"ERROR: {stderr}");
                    AppendLineToConsoleOutputTextBox(stdout);
                }
                catch (Exception e)
                {
                    AppendLineToConsoleOutputTextBox($"ERROR: {e.ToString()}");
                }

                endOfLoop:
                
                await Task.Delay(TimeSpan.FromSeconds(12)); // todo: replace with below line before releasing
                //await Task.Delay(TimeSpan.FromMinutes(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES)));
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
    }
}