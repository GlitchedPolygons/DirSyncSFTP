namespace DirSyncSFTP;

public static class Constants
{
    public static class PrefKeys
    {
        public const string CLIENT_ID = "ClientId";
        public const string WINSCP_EXE_PATH = "WinScpExePath";
        public const string LAST_SYNC_UTC = "LastSyncTimestampUTC";
        public const string SYNC_INTERVAL_MINUTES = "SyncIntervalMinutes";
        public const string VERSION_NUMBER_MAJOR = "VersionNumberMajor";
        public const string VERSION_NUMBER_MINOR = "VersionNumberMinor";
        public const string VERSION_NUMBER_PATCH = "VersionNumberPatch";
    }

    public const string CONFIG_FILENAME = "Config.json";
    public const string REMOTE_METADATA_FILENAME = ".dirsyncsftp";
    public const string REMOTE_LOCK_FILENAME = ".dirsyncsftplock";

    public const string POWERSHELL_SCRIPT = @"
param (
    $sessionUrl = ""sftp://user:mypassword;fingerprint=ssh-rsa-xxxxxxxxxxx...@example.com/"",
    [Parameter(Mandatory = $True)]
    $localPath,
    [Parameter(Mandatory = $True)]
    $remotePath,
    [Parameter(Mandatory = $True)]
    $listPath,
    [Parameter(Mandatory = $False)]
    $sshKey,
    [Parameter(Mandatory = $False)]
    $sshKeyPassphrase,
    $sessionLogPath = $Null
)
 
try
{
    $assemblyPath = if ($env:WINSCP_PATH) { $env:WINSCP_PATH } else { $PSScriptRoot }
    Add-Type -Path (Join-Path $assemblyPath ""WinSCPnet.dll"")
 
    $sessionOptions = New-Object WinSCP.SessionOptions
    $sessionOptions.ParseUrl($sessionUrl)
    $sessionOptions.SshPrivateKeyPath = $sshKey
    $sessionOptions.PrivateKeyPassphrase = $sshKeyPassphrase
 
    $listPath = [Environment]::ExpandEnvironmentVariables($listPath)
    $listDir = (Split-Path -Parent $listPath) 
    New-Item -ItemType directory -Path $listDir -Force | Out-Null 
 
    if (Test-Path $listPath)
    {
        Write-Host ""Loading list of previous local files...""
        [string[]]$previousFiles = Get-Content $listPath
    }
    else
    {
        Write-Host ""No list of previous local files""
        $previousFiles = @()
    }
 
    $needRefresh = $False
 
    $session = New-Object WinSCP.Session
 
    try
    {
        $session.SessionLogPath = $sessionLogPath
 
        Write-Host ""Connecting...""
        $session.Open($sessionOptions)
 
        Write-Host ""Comparing files...""
        $differences =
            $session.CompareDirectories(
                [WinSCP.SynchronizationMode]::Both, $localPath, $remotePath, $False)
 
        if ($differences.Count -eq 0)
        {
            Write-Host ""No changes found.""   
        }
        else
        {
            Write-Host ""Synchronizing $($differences.Count) change(s)...""
 
            foreach ($difference in $differences)
            {
                $action = $difference.Action
                if ($action -eq [WinSCP.SynchronizationAction]::UploadNew)
                {
                    if ($previousFiles -contains $difference.Local.FileName)
                    {
                        $difference.Reverse()
                    }
                    else
                    {
                        $needRefresh = $True
                    }
                }
                elseif ($action -eq [WinSCP.SynchronizationAction]::DownloadNew)
                {
                    $localFilePath =
                        [WinSCP.RemotePath]::TranslateRemotePathToLocal(
                            $difference.Remote.FileName, $remotePath, $localPath)
                    if ($previousFiles -contains $localFilePath)
                    {
                        $difference.Reverse()
                        $needRefresh = $True
                    }
                    else
                    {
                        # noop
                    }
                }
                elseif ($action -eq [WinSCP.SynchronizationAction]::DownloadUpdate)
                {
                    # noop
                }
                elseif ($action -eq [WinSCP.SynchronizationAction]::UploadUpdate)
                {
                    $needRefresh = $True
                }
                else
                {
                    throw ""Unexpected difference $action""
                }
 
                Write-Host -NoNewline ""$difference ...""
                try
                {
                    $difference.Resolve($session) | Out-Null
                    Write-Host -NoNewline "" Done.""
                }
                finally
                {
                    Write-Host
                }
            }
        }
    }
    finally
    {
        # Disconnect, clean up
        $session.Dispose()
    }
 
    Write-Host ""Saving current local file list...""
    $localFiles =
        Get-ChildItem -Recurse -Path $localPath |
        Select-Object -ExpandProperty FullName
    Set-Content $listPath $localFiles
 
    Write-Host ""Done.""
 
    $result = 0
}
catch
{
    Write-Host ""Error: $($_.Exception.Message)""
    $result = 1
}
 
exit $result
";
}