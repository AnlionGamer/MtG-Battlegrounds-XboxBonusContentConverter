using System.Security.Cryptography;

namespace MtGBattlegroundsDlcConverter;

public static class Hashing
{
    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task VerifyFileAsync(string path, FileRecord expected, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Required file is missing: {expected.Path}", path);
        if (info.Length != expected.Size)
            throw new InvalidDataException($"Size mismatch for {expected.Path}. Expected {expected.Size}, got {info.Length}.");
        var hash = await Sha256FileAsync(path, cancellationToken);
        if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 mismatch for {expected.Path}. Expected {expected.Sha256}, got {hash}.");
    }
}
