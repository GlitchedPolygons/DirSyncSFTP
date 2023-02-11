# DirSyncSFTP

## Convert a folder on your Windows system into a dropbox. Kinda.

Windows Explorer integration for SFTP directories. Raw and simple. GPL-3.0 license. Enjoy.

This is a WPF application that can run in the background and synchronize one or more directories on your system with a remote SFTP server's directory.

It behaves kinda like dropbox, except it doesn't handle conflicts at all (the newest writer wins).

Makes use of [WinSCP](https://github.com/winscp/winscp) and its PowerShell interface for contacting the server and synchronizing.

---

![Screenshot](https://api.files.glitchedpolygons.com/api/v1/files/dirsyncsftp-screenshot.png)

---

Example setup of an SFTP server + DirSyncSFTP:

https://www.youtube.com/watch?v=G_cSS9fiq_o
