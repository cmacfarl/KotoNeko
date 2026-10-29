using System.Text.Json;
using KotoNeko.Data;
using KotoNeko.Data.Dictionary;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Where the current or last import stands, for the Settings page.</summary>
public sealed record DictionaryImportStatus(
    bool Running,
    ImportStage? Stage,
    long Done,
    long Total,
    string? Source,
    string? Error,
    ImportResult? Result);

/// <summary>
/// Runs dictionary imports in the background, independent of any page, so an
/// import keeps going if the user navigates away. One import at a time. Imports
/// come either from the latest jmdict-simplified release on GitHub or from a file
/// the user uploaded. The only network use is that download: nothing typed in
/// KotoNeko is ever sent anywhere.
/// </summary>
public sealed class DictionaryImportService
{
    public const string HttpClientName = "jmdict-releases";
    private const string LatestReleaseUrl = "https://api.github.com/repos/scriptin/jmdict-simplified/releases/latest";

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly IHttpClientFactory _http;
    private readonly SentenceReadingService _readings;
    private readonly ILogger<DictionaryImportService> _logger;
    private readonly object _lock = new();
    private DictionaryImportStatus _status = new(false, null, 0, 0, null, null, null);

    public DictionaryImportService(
        IDbContextFactory<KotoNekoDbContext> factory, IHttpClientFactory http,
        SentenceReadingService readings, ILogger<DictionaryImportService> logger)
    {
        _factory = factory;
        _http = http;
        _readings = readings;
        _logger = logger;
    }

    /// <summary>Raised (on a background thread) whenever <see cref="Status"/> changes.</summary>
    public event Action? Changed;

    public DictionaryImportStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    /// <summary>Ask GitHub for the newest full English dictionary release.</summary>
    public async Task<DictionaryRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        HttpClient client = _http.CreateClient(HttpClientName);
        await using Stream body = await client.GetStreamAsync(LatestReleaseUrl, cancellationToken);
        using JsonDocument release = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return JmdictFiles.PickRelease(release.RootElement)
            ?? throw new InvalidOperationException("The latest jmdict-simplified release has no full English JSON download.");
    }

    /// <summary>Download the latest release and import it. False if an import is already running.</summary>
    public bool StartDownloadAndImport()
    {
        return TryStart("the latest release on GitHub", async (folder, cancellationToken) =>
        {
            DictionaryRelease release = await GetLatestReleaseAsync(cancellationToken);
            Update(s => s with { Source = $"{release.FileName} from GitHub" });

            string zip = Path.Combine(folder, "jmdict.json.zip");
            HttpClient client = _http.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.GetAsync(
                release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.Size;

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream target = File.Create(zip)) {
                byte[] buffer = new byte[81920];
                long done = 0, reported = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0) {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    if (done > JmdictFiles.MaxFileSize) {
                        throw new InvalidDataException("The download is larger than any dictionary should be.");
                    }
                    // Every half megabyte is plenty for a progress bar.
                    if (done - reported >= 512 * 1024) {
                        reported = done;
                        Report(new ImportProgress(ImportStage.Downloading, done, total));
                    }
                }
                Report(new ImportProgress(ImportStage.Downloading, done, total));
            }

            return JmdictFiles.ExtractJson(zip, folder);
        });
    }

    /// <summary>
    /// Import a file the user uploaded, already copied to <paramref name="uploadedFile"/>
    /// (a .json or a .zip holding one). The service takes ownership of the file.
    /// False if an import is already running.
    /// </summary>
    public bool StartImportFromFile(string uploadedFile, string displayName)
    {
        bool started = TryStart(displayName, (folder, _) =>
        {
            string moved = Path.Combine(folder, Path.GetFileName(uploadedFile));
            File.Move(uploadedFile, moved);
            return Task.FromResult(moved.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? JmdictFiles.ExtractJson(moved, folder)
                : moved);
        });

        if (!started) {
            File.Delete(uploadedFile);
        }
        return started;
    }

    /// <summary>
    /// Start an import in the background: <paramref name="prepare"/> produces the
    /// JSON file inside a temporary folder that is deleted afterwards.
    /// </summary>
    private bool TryStart(string source, Func<string, CancellationToken, Task<string>> prepare)
    {
        lock (_lock) {
            if (_status.Running) {
                return false;
            }
            _status = new DictionaryImportStatus(true, null, 0, 0, source, null, null);
        }
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "kotoneko-jmdict-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                string json = await prepare(folder, CancellationToken.None);
                await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
                ImportResult result = await JmdictImporter.ImportAsync(db, json, new Reporter(this));
                _logger.LogInformation("Imported {Entries} dictionary entries (version {Version}) in {Seconds:N0}s, peak memory {Peak} MB.",
                    result.Entries, result.Version, result.Elapsed.TotalSeconds, result.PeakMemoryMb);
                Update(s => s with { Running = false, Stage = ImportStage.Done, Result = result });
                // The import dropped readings worked out from the old dictionary: rebuild them.
                _readings.RequestBackfill();
            } catch (Exception ex) {
                _logger.LogError(ex, "Dictionary import failed.");
                Update(s => s with { Running = false, Error = ex.Message });
            } finally {
                try {
                    Directory.Delete(folder, recursive: true);
                } catch (IOException) {
                    // A temp folder left behind is harmless.
                }
            }
        });
        return true;
    }

    private void Report(ImportProgress progress) =>
        Update(s => s with { Stage = progress.Stage, Done = progress.Done, Total = progress.Total });

    private void Update(Func<DictionaryImportStatus, DictionaryImportStatus> change)
    {
        lock (_lock) {
            _status = change(_status);
        }
        Changed?.Invoke();
    }

    /// <summary>Progress straight into the status, on the importing thread.</summary>
    private sealed class Reporter(DictionaryImportService service) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => service.Report(value);
    }
}
