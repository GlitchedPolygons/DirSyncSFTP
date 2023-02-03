namespace DirSyncSFTP;

public class SynchronizedDirectory
{
    public string LocalDirectory { get; set; }
    public string RemoteDirectory { get; set; }
    public string SshKeyFilePath { get; set; }
    public string SshKeyPassphrase { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Host { get; set; }
    public ushort Port { get; set; }
}