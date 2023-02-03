using System;

namespace DirSyncSFTP;

[Serializable]
public class RemoteSyncMetadata
{
    public long LastPushTimestampUTC { get; set; } = 0;
    public string LastPushByClientId { get; set; } = string.Empty;
}