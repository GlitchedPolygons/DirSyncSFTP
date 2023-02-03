namespace DirSyncSFTP;

public static class Constants
{
    public static class PrefKeys
    {
        public const string CLIENT_ID = "ClientId";
        public const string WINSCP_ASSEMBLY_PATH = "WinScpAssemblyPath";
        public const string LAST_SYNC_UTC = "LastSyncTimestampUTC";
        public const string SYNC_INTERVAL_MINUTES = "SyncIntervalMinutes";
        public const string VERSION_NUMBER_MAJOR = "VersionNumberMajor";
        public const string VERSION_NUMBER_MINOR = "VersionNumberMinor";
        public const string VERSION_NUMBER_PATCH = "VersionNumberPatch";
        public const string MAX_CONSOLE_OUTPUT_LINECOUNT = "MaxConsoleOutputLineCount";
    }

    public const string CONFIG_FILENAME = "Config.json";
    public const string KNOWN_HOSTS_FILENAME = "KnownHosts.json";
    public const string POWERSHELL_SYNC_SCRIPT_FILENAME = "Sync.ps1";
    public const string POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT_FILENAME = "ScanHostKeyFingerprint.ps1";

    public const string POWERSHELL_SCAN_HOST_KEY_FP_SCRIPT = @"
param (
    [Parameter(Mandatory = $True)]
    [string] $assemblyPath = ""C:\Program Files (x86)\WinSCP\WinSCPnet.dll"",

    [Parameter(Mandatory = $True)]
    [string] $hostName = ""sftp.example.com"",

    [Parameter(Mandatory = $True)]
    [int] $portNumber = 22
)

try
{
    Add-Type -Path $assemblyPath
 
    $sessionOptions = New-Object WinSCP.SessionOptions -Property @{
        Protocol = [WinSCP.Protocol]::Sftp
        HostName = $hostName
        PortNumber = $portNumber
    }

    $session = New-Object WinSCP.Session
    try
    {
        $fingerprint = $session.ScanFingerprint($sessionOptions, ""SHA-256"")
        Write-Host -NoNewline ($fingerprint)
        $result = 0
    }
    finally
    {
        $session.Dispose()
    }
}
catch
{
    Write-Host ""Error: $($_.Exception.Message)""
    $result = 1
}
 
exit $result

";

    public const string POWERSHELL_SYNC_SCRIPT = @"
param (
    [Parameter(Mandatory = $True)]
    [string] $assemblyPath = ""C:\Program Files (x86)\WinSCP\WinSCPnet.dll"",

    [Parameter(Mandatory = $True)]
    [string] $sessionUrl = ""sftp://user:mypassword;fingerprint=ssh-rsa-xxxxxxxxxxx...@example.com/"",

    [Parameter(Mandatory = $True)]
    [string] $localPath,

    [Parameter(Mandatory = $True)]
    [string] $remotePath,

    [Parameter(Mandatory = $True)]
    [string] $listPath,

    [Parameter(Mandatory = $False)]
    [string] $sshKey,

    [Parameter(Mandatory = $False)]
    [string] $sshKeyPassphrase,

    [Parameter(Mandatory = $False)]
    [string] $sessionLogPath = $Null
)
 
try
{
    Add-Type -Path $assemblyPath
 
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