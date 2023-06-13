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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private async Task Sync()
    {
        await Task.Delay(1024);

        while (!quitting)
        {
            if (paused)
            {
                goto endOfLoop;
            }

            await PerformSync();

            endOfLoop:
#if DEBUG
            await Task.Delay(TimeSpan.FromSeconds(15));
#else
            await Task.Delay(TimeSpan.FromMinutes(jsonPrefs.GetInt(Constants.PrefKeys.SYNC_INTERVAL_MINUTES)));
#endif
        }
    }

    private async Task PerformSync()
    {
        if (synchronizing || adding)
        {
            return;
        }

        SetSynchronizingState(true);

        jsonPrefs.SetLong(Constants.PrefKeys.LAST_SYNC_UTC, DateTime.UtcNow.ToUnixTimeSeconds());
        await jsonPrefs.SaveAsync();

        if (synchronizedDirectories.Dictionary.NullOrEmpty())
        {
            AppendLineToConsoleOutputTextBox($"List of synchronized directories is empty. Add a new entry to get started!");
        }
        else
        {
            foreach (var (_, synchronizedDirectory) in synchronizedDirectories.Dictionary)
            {
                await PerformSyncForDirectory(synchronizedDirectory);
            }
        }

        SetSynchronizingState(false);
    }

    private async Task PerformSyncForDirectory(SynchronizedDirectory synchronizedDirectory)
    {
        string key = synchronizedDirectory.GetDictionaryKey();

        try
        {
            AppendLineToConsoleOutputTextBox($"Synchronizing {key}... Please be patient: depending on how big the directory trees are this might take a while!");

            if (!Directory.Exists(synchronizedDirectory.LocalDirectory))
            {
                AppendLineToConsoleOutputTextBox($"ERROR: Local directory \"{synchronizedDirectory.LocalDirectory}\" not found!");
                return;
            }

            string host = $"{synchronizedDirectory.Host}:{synchronizedDirectory.Port}";

            if (!knownHosts.Dictionary.TryGetValue(host, out string? storedFingerprint))
            {
                AppendLineToConsoleOutputTextBox($"ERROR: Key fingerprint for host \"{host}\" not found in local cache!");
                return;
            }

            string fingerprint = await ScanHostKeyFingerprint(synchronizedDirectory.Host, synchronizedDirectory.Port);

            if (storedFingerprint != fingerprint)
            {
                AppendLineToConsoleOutputTextBox($"ERROR: The host key fingerprint for \"{host}\" (DirSync: \"{synchronizedDirectory.GetDictionaryKey()}\" - fingerprint: \"{fingerprint}\") does not match the locally stored one to which you agreed during setup of the synchronized directory: \"{storedFingerprint}\". The host either changed its key or, well... Let's hope it's not a MITM attack!");
                return;
            }

            var argsStringBuilder = new StringBuilder(1024);

            argsStringBuilder.Append("-NoProfile -ExecutionPolicy ByPass & '");
            argsStringBuilder.Append(powershellSyncScriptFile);

            argsStringBuilder.Append("' -assemblyPath '");
            argsStringBuilder.Append(jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH));

            argsStringBuilder.Append("' -hostName '");
            argsStringBuilder.Append(synchronizedDirectory.Host);

            argsStringBuilder.Append("' -portNumber '");
            argsStringBuilder.Append(synchronizedDirectory.Port);

            argsStringBuilder.Append("' -username '");
            argsStringBuilder.Append(synchronizedDirectory.Username.UTF8GetBytes().ToBase64String());

            argsStringBuilder.Append("' -fingerprint '");
            argsStringBuilder.Append(storedFingerprint.UTF8GetBytes().ToBase64String());

            argsStringBuilder.Append("' -localPath '");
            argsStringBuilder.Append(synchronizedDirectory.LocalDirectory.UTF8GetBytes().ToBase64String());

            argsStringBuilder.Append("' -remotePath '");
            argsStringBuilder.Append(synchronizedDirectory.RemoteDirectory.UTF8GetBytes().ToBase64String());

            argsStringBuilder.Append("' -listPath '");
            argsStringBuilder.Append(Path.Combine(filesListDir, synchronizedDirectory.GetDictionaryKey().SHA256()).UTF8GetBytes().ToBase64String());

            if (synchronizedDirectory.Password.NotNullNotEmpty())
            {
                argsStringBuilder.Append("' -password '");
                argsStringBuilder.Append(synchronizedDirectory.Password.UTF8GetBytes().ToBase64String());
            }

            if (synchronizedDirectory.SshKeyFilePath.NotNullNotEmpty())
            {
                argsStringBuilder.Append("' -sshKey '");
                argsStringBuilder.Append(synchronizedDirectory.SshKeyFilePath.UTF8GetBytes().ToBase64String());

                argsStringBuilder.Append("' -sshKeyPassphrase '");
                argsStringBuilder.Append(synchronizedDirectory.SshKeyPassphrase.UTF8GetBytes().ToBase64String());
            }

            argsStringBuilder.Append("' ");

            string args = argsStringBuilder.ToString();
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
                return;
            }

            process.ErrorDataReceived += OnProcessErrorDataReceived;
            process.OutputDataReceived += OnProcessOutputDataReceived;

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            while (!process.HasExited)
            {
                if (quitting)
                {
                    process.Kill();
                    return;
                }
                
                await Task.Delay(512);
            }

            if (process.ExitCode != 0)
            {
                AppendLineToConsoleOutputTextBox($"ERROR: Something went wrong during the synchronization of \"{key}\"! Please check the logs and try redoing the setup process for the synchronized directory entry (if applicable).");
            }
        }
        catch (Exception e)
        {
            AppendLineToConsoleOutputTextBox($"ERROR while synchronizing \"{key}\" => {e.ToString()}");
        }
    }

    private void SetSynchronizingState(bool nowSynchronizing)
    {
        ExecuteOnUIThread(() =>
        {
            if (nowSynchronizing)
            {
                synchronizing = true;
                Title = "SFTP Directory Synchronizer (currently synchronizing...)";
                notifyIcon.Text = Constants.TRAY_TOOLTIP_SYNCING;
            }
            else
            {
                synchronizing = false;
                Title = "SFTP Directory Synchronizer";
                notifyIcon.Text = Constants.TRAY_TOOLTIP_IDLE;
            }
        });
    }
}