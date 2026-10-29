using System.Diagnostics;
using System.IO.Compression;
using KotoNeko.Data.Backup;
using MySqlConnector;

namespace KotoNeko.Web.Services;

/// <summary>Where the current or last backup stands, for the Settings page.</summary>
public sealed record BackupStatus(bool Running, string? Error, BackupFile? Last);

/// <summary>
/// Backs the database up with mysqldump into the repository's backups folder, as
/// gzipped SQL. Uses the app's own connection string; the password goes to
/// mysqldump through a temporary option file, never on its command line.
/// For running from the repository; an installed copy would need its own scheme.
/// </summary>
public sealed class BackupService
{
    private readonly string _connectionString;
    private readonly ILogger<BackupService> _logger;
    private readonly object _lock = new();
    private BackupStatus _status = new(false, null, null);

    public BackupService(string connectionString, string? folder, string? mysqldump, ILogger<BackupService> logger)
    {
        _connectionString = connectionString;
        Folder = folder;
        Mysqldump = mysqldump;
        _logger = logger;
    }

    /// <summary>The backups folder, or null when it can't be found.</summary>
    public string? Folder { get; }

    /// <summary>The mysqldump executable, or null when it can't be found.</summary>
    public string? Mysqldump { get; }

    /// <summary>The database name from the connection string, for the restore command.</summary>
    public string Database => new MySqlConnectionStringBuilder(_connectionString).Database;

    /// <summary>Why backups can't run, or null when they can.</summary>
    public string? Problem =>
        Folder is null ? "The backups folder wasn't found: KotoNeko isn't running from the repository."
        : Mysqldump is null ? "mysqldump wasn't found. Set Backup:Mysqldump in appsettings to its path."
        : null;

    public event Action? Changed;

    public BackupStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    public List<BackupFile> List() => Folder is null ? new List<BackupFile>() : BackupFiles.List(Folder);

    /// <summary>Start a backup in the background. False if one is already running or backups can't run.</summary>
    public bool Start()
    {
        lock (_lock) {
            if (_status.Running || Problem is not null) {
                return false;
            }
            _status = _status with { Running = true, Error = null };
        }
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            try {
                BackupFile file = await BackupNowAsync(null);
                Update(s => s with { Running = false, Last = file });
            } catch (Exception ex) {
                Update(s => s with { Running = false, Error = ex.Message });
            }
        });
        return true;
    }

    /// <summary>Back up now and wait for it. Throws if backups can't run or mysqldump fails.</summary>
    public async Task<BackupFile> BackupNowAsync(string? reason, CancellationToken cancellationToken = default)
    {
        if (Problem is string problem) {
            throw new InvalidOperationException(problem);
        }

        MySqlConnectionStringBuilder cs = new(_connectionString);
        Directory.CreateDirectory(Folder!);
        string target = Path.Combine(Folder!, BackupFiles.NewName(DateTime.Now, reason));
        string partial = target + ".partial";
        string options = Path.Combine(Path.GetTempPath(), $"kotoneko-backup-{Guid.NewGuid():N}.cnf");
        Stopwatch timer = Stopwatch.StartNew();

        try {
            await File.WriteAllTextAsync(options, BackupFiles.OptionFile(cs.Server, cs.Port, cs.UserID, cs.Password), cancellationToken);

            ProcessStartInfo start = new(Mysqldump!) {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // --defaults-extra-file must come first. --no-tablespaces avoids needing
            // the PROCESS privilege; --hex-blob keeps audio intact; no CREATE DATABASE,
            // so a dump restores into whichever database it is loaded into.
            foreach (string arg in new[] {
                $"--defaults-extra-file={options}",
                "--single-transaction", "--quick", "--no-tablespaces", "--hex-blob",
                "--default-character-set=utf8mb4",
                cs.Database,
            }) {
                start.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("mysqldump didn't start.");
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await using (FileStream file = File.Create(partial))
            await using (GZipStream gzip = new(file, CompressionLevel.Optimal)) {
                await process.StandardOutput.BaseStream.CopyToAsync(gzip, cancellationToken);
            }
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0) {
                throw new InvalidOperationException($"mysqldump failed (exit {process.ExitCode}): {(await errors).Trim()}");
            }

            File.Move(partial, target);
            FileInfo info = new(target);
            _logger.LogInformation("Backed up the database to {File} ({Size:N0} bytes) in {Seconds:N1}s.",
                target, info.Length, timer.Elapsed.TotalSeconds);
            return new BackupFile(info.FullName, info.Name, info.Length, info.LastWriteTime);
        } catch {
            File.Delete(partial);
            throw;
        } finally {
            File.Delete(options);
        }
    }

    private void Update(Func<BackupStatus, BackupStatus> change)
    {
        lock (_lock) {
            _status = change(_status);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// mysqldump from config (Backup:Mysqldump), else the newest MySQL Server
    /// install under Program Files, else null.
    /// </summary>
    public static string? FindMysqldump(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) {
            return File.Exists(configured) ? configured : null;
        }

        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MySQL");
        if (!Directory.Exists(root)) {
            return null;
        }
        return Directory.EnumerateDirectories(root, "MySQL Server *")
            .Select(d => Path.Combine(d, "bin", "mysqldump.exe"))
            .Where(File.Exists)
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
