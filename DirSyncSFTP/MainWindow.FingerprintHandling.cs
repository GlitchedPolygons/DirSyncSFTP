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
using System.Threading.Tasks;
using System.Windows;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

public partial class MainWindow
{
    private async Task<string> ScanHostKeyFingerprint(string hostName, ushort portNumber = 22)
    {
        string args = $"-NoProfile -ExecutionPolicy ByPass & '{powershellScanHostKeyFingerprintScriptFile}' -assemblyPath '{jsonPrefs.GetString(Constants.PrefKeys.WINSCP_ASSEMBLY_PATH)}' -hostName {hostName} -portNumber {portNumber} ";

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
            AppendLineToConsoleOutputTextBox($"ERROR: Failed to fetch host key fingerprint for \"{hostName}:{portNumber}\". {stderr}");
        }

        return stdout.NotNullNotEmpty()
            ? stdout.Trim()
            : string.Empty;
    }

    private void SaveFingerprintIfTrusted(string host, ushort port, string fingerprint)
    {
        string dictionaryKey = $"{host}:{port}";

        if (knownHosts.Dictionary.TryGetValue(dictionaryKey, out string? storedFingerprint))
        {
            if (fingerprint == storedFingerprint)
            {
                AppendLineToConsoleOutputTextBox($"Fingerprint for {dictionaryKey} scanned and compared to locally stored entry. They match; everything OK!\n\nFingerprint: {fingerprint}");
                return;
            }

            ExecuteOnUIThread(() =>
            {
                if (MessageBox.Show(
                        $"The host \"{host}:{port}\" reported the following public key fingerprint:\n\n{fingerprint}\n\nDuring setup of the associated synchronized directory entry, you accepted the following as the trusted host key fingerprint:\n\n{storedFingerprint}\n\nThese two are different! This could either be due to the host having changed keys, or a man-in-the-middle attack (hopefully not!).\n\nHow should this be handled?\n\nClicking on \"Yes\" will accept the new host key fingerprint and overwrite the currently stored one; \"No\" will reject the key returned by the server and keep everything as it was (connection won't happen in this case).",
                        $"Fingerprint mismatch for \"{host}:{port}\"", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    AppendLineToConsoleOutputTextBox($"User rejected host \"{host}:{port}\"'s alleged public key fingerprint \"{fingerprint}\" - associated directory entries won't sync.");
                    return;
                }

                Task.Run(() =>
                {
                    knownHosts.Dictionary[$"{host}:{port}"] = fingerprint;
                    knownHosts.Save();
                });
            });
        }
        else
        {
            ExecuteOnUIThread(() =>
            {
                if (MessageBox.Show($"The host \"{host}:{port}\" reported the following public key fingerprint:\n\n{fingerprint}\n\nIs this correct? Do you trust it?", "Host key fingerprint check", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    AppendLineToConsoleOutputTextBox($"User rejected host \"{host}:{port}\"'s alleged public key fingerprint \"{fingerprint}\" - associated directory entries won't sync.");
                    return;
                }

                Task.Run(() =>
                {
                    knownHosts.Dictionary[$"{host}:{port}"] = fingerprint;
                    knownHosts.Save();
                });
            });
        }
    }
}