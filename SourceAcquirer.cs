using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MtGBattlegroundsDlcConverter;

public sealed class SourceAcquirer(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<string> DownloadDigiexAsync(
        string url,
        string tempRoot,
        IProgress<(long received, long? total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(tempRoot);
        var destination = Path.Combine(tempRoot, "MtG Battlegrounds DLC Installer.rar");

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[1024 * 1024];
            long received = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report((received, total));
            }
            await output.FlushAsync(cancellationToken);

            // Do not accept a clean HTTP EOF when the server advertised more bytes.
            // A truncated RAR can still expose early headers and fail later with a
            // misleading per-entry unpacked-size error.
            if (total.HasValue && received != total.Value)
            {
                throw new DigiexDownloadException(
                    $"The Digiex download ended early (received {received:N0} of {total.Value:N0} bytes).",
                    new EndOfStreamException("Downloaded archive length did not match Content-Length."));
            }

            return destination;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new DigiexDownloadException(
                "The Xbox DLC source could not be downloaded from Digiex.", ex);
        }
    }

    public static async Task ExtractRequiredXboxFilesAsync(
        string archivePath,
        string extractionRoot,
        IReadOnlyCollection<FileRecord> required,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(extractionRoot);
        var wanted = required.ToDictionary(
            x => NormalizeArchivePath(x.Path),
            x => x,
            StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var archive = ArchiveFactory.OpenArchive(archivePath);

        // Solid RAR archives must be decoded sequentially. Opening arbitrary
        // entry streams can produce "unpacked file size does not match header"
        // even when the archive itself is valid. SharpCompress explicitly
        // exposes ExtractAllEntries() for this case.
        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;

                var key = NormalizeArchivePath(reader.Entry.Key ?? string.Empty);
                if (!TryResolveWanted(key, wanted, out var expected))
                    continue;

                var destination = SafeManifestPath(extractionRoot, expected.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                reader.WriteEntryToFile(destination, new ExtractionOptions
                {
                    ExtractFullPath = false,
                    Overwrite = true
                });

                await Hashing.VerifyFileAsync(destination, expected, cancellationToken);
                found.Add(NormalizeArchivePath(expected.Path));
            }
        }
        else
        {
            foreach (var entry in archive.Entries.Where(x => !x.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = NormalizeArchivePath(entry.Key ?? string.Empty);
                if (!TryResolveWanted(key, wanted, out var expected))
                    continue;

                var destination = SafeManifestPath(extractionRoot, expected.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var source = entry.OpenEntryStream())
                await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
                }
                await Hashing.VerifyFileAsync(destination, expected, cancellationToken);
                found.Add(NormalizeArchivePath(expected.Path));
            }
        }

        var missing = wanted.Keys.Where(x => !found.Contains(x)).OrderBy(x => x).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("The Xbox archive is missing required files:\n" + string.Join("\n", missing));
    }

    private static bool TryResolveWanted(
        string key,
        IReadOnlyDictionary<string, FileRecord> wanted,
        out FileRecord expected)
    {
        if (wanted.TryGetValue(key, out expected!))
            return true;

        // Tolerate a repack that wraps the original installer tree in one
        // extra folder. The destination is still derived only from our
        // trusted manifest path.
        var matches = wanted
            .Where(x => key.EndsWith("/" + x.Key, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToArray();

        if (matches.Length == 0)
        {
            expected = null!;
            return false;
        }

        if (matches.Length != 1)
            throw new InvalidDataException($"Archive entry matches more than one required path: {key}");

        expected = matches[0];
        return true;
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string SafeManifestPath(string root, string relative)
    {
        var normalized = NormalizeArchivePath(relative);
        if (normalized.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Unsafe manifest path: {relative}");
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes extraction root: {relative}");
        return full;
    }
}
