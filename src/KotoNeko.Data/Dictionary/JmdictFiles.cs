using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotoNeko.Data.Dictionary;

/// <summary>A downloadable jmdict-simplified release asset.</summary>
public sealed record DictionaryRelease(string Version, string FileName, string DownloadUrl, long Size);

/// <summary>Finding and unpacking jmdict-simplified release files.</summary>
public static partial class JmdictFiles
{
    /// <summary>Largest file an upload, download or zip entry may be: the full dictionary is about 120 MB.</summary>
    public const long MaxFileSize = 512L * 1024 * 1024;

    /// <summary>
    /// From a GitHub release (the JSON of the releases API), the full English
    /// dictionary as a zip: for example jmdict-eng-3.6.2+20261026175559.json.zip,
    /// not the "common" subset. Null if the release has none.
    /// </summary>
    public static DictionaryRelease? PickRelease(JsonElement release)
    {
        string version = release.TryGetProperty("tag_name", out JsonElement tag) ? tag.GetString() ?? "" : "";
        if (!release.TryGetProperty("assets", out JsonElement assets)) {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray()) {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (FullEnglishZip().IsMatch(name)) {
                return new DictionaryRelease(
                    version,
                    name,
                    asset.GetProperty("browser_download_url").GetString() ?? "",
                    asset.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0);
            }
        }
        return null;
    }

    [GeneratedRegex(@"^jmdict-eng-\d[^/\\]*\.json\.zip$")]
    private static partial Regex FullEnglishZip();

    /// <summary>
    /// Extract the dictionary JSON from a zip into <paramref name="folder"/>, under a
    /// name chosen here (never the entry's own path). Exactly one .json entry is
    /// expected. Returns the extracted file's path.
    /// </summary>
    public static string ExtractJson(string zipPath, string folder)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry[] json = archive.Entries
            .Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (json.Length != 1) {
            throw new InvalidDataException($"Expected one .json file in the zip, found {json.Length}.");
        }
        if (json[0].Length > MaxFileSize) {
            throw new InvalidDataException("The dictionary in the zip is larger than any dictionary should be.");
        }

        string target = Path.Combine(folder, "jmdict.json");
        json[0].ExtractToFile(target, overwrite: true);
        return target;
    }
}
