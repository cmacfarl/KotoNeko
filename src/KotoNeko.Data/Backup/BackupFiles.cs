using System.Text;

namespace KotoNeko.Data.Backup;

/// <summary>A backup file on disk.</summary>
public sealed record BackupFile(string Path, string Name, long Size, DateTime CreatedAt);

/// <summary>
/// Where database backups live and how they are named. Backups are gzipped
/// mysqldump output: kotoneko-2026-10-29-161500[-reason].sql.gz.
/// </summary>
public static class BackupFiles
{
    public const string FolderName = "backups";
    private const string Prefix = "kotoneko-";
    private const string Extension = ".sql.gz";

    /// <summary>
    /// The backups folder at the repository root: the nearest folder at or above
    /// <paramref name="start"/> that holds KotoNeko.sln. Null when not running
    /// from the repository.
    /// </summary>
    public static string? FindRepositoryFolder(string start)
    {
        for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent) {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "KotoNeko.sln"))) {
                return System.IO.Path.Combine(dir.FullName, FolderName);
            }
        }
        return null;
    }

    /// <summary>A new backup's file name; the reason (e.g. "before-migration") is optional.</summary>
    public static string NewName(DateTime localTime, string? reason = null)
    {
        string name = Prefix + localTime.ToString("yyyy-MM-dd-HHmmss");
        if (!string.IsNullOrWhiteSpace(reason)) {
            string safe = new(reason.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
            name += "-" + safe.Trim('-');
        }
        return name + Extension;
    }

    /// <summary>Backups in the folder, newest first.</summary>
    public static List<BackupFile> List(string folder)
    {
        if (!Directory.Exists(folder)) {
            return new List<BackupFile>();
        }
        return new DirectoryInfo(folder)
            .EnumerateFiles(Prefix + "*" + Extension)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFile(f.FullName, f.Name, f.Length, f.LastWriteTime))
            .ToList();
    }

    /// <summary>
    /// A MySQL client option file holding the connection details, so the password
    /// is never on a command line (where other processes could read it).
    /// </summary>
    public static string OptionFile(string host, uint port, string user, string password)
    {
        StringBuilder sb = new();
        sb.AppendLine("[client]");
        sb.AppendLine($"host={Quote(host)}");
        sb.AppendLine($"port={port}");
        sb.AppendLine($"user={Quote(user)}");
        sb.AppendLine($"password={Quote(password)}");
        return sb.ToString();
    }

    // Option-file values may be double-quoted, with \\ and \" as escapes.
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
