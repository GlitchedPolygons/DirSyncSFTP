using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

            jsonPrefs = JsonPrefs.FromFile(Constants.CONFIG_FILENAME, true, new JsonSerializerOptions { WriteIndented = true });

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MAJOR, version?.Major.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_MINOR, version?.Minor.ToString() ?? string.Empty);
            jsonPrefs.SetString(Constants.PrefKeys.VERSION_NUMBER_PATCH, version?.Revision.ToString() ?? string.Empty);

            if (!jsonPrefs.HasKey(Constants.PrefKeys.CLIENT_ID))
            {
                jsonPrefs.SetString(Constants.PrefKeys.CLIENT_ID, Guid.NewGuid().ToString("D").ToUpperInvariant());
            }

            if (jsonPrefs.GetString(Constants.PrefKeys.WINSCP_EXE_PATH).NullOrEmpty() || !File.Exists(jsonPrefs.GetString(Constants.PrefKeys.WINSCP_EXE_PATH)))
            {
                var dialog = new SelectWinScpExeDialog();

                if (dialog.ShowDialog() is true && File.Exists(dialog.ExeFilePath))
                {
                    jsonPrefs.SetString(Constants.PrefKeys.WINSCP_EXE_PATH, dialog.ExeFilePath);
                }
                else
                {
                    Close();
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

        private async Task Sync()
        {
            await Task.Delay(1024);

            while (!quitting)
            {
                jsonPrefs.SetLong(Constants.PrefKeys.LAST_SYNC_UTC, DateTime.UtcNow.ToUnixTimeSeconds());
                await jsonPrefs.SaveAsync();

                // TODO: impl. here

                await Task.Delay(TimeSpan.FromMinutes(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES)));
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