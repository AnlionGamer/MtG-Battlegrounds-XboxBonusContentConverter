namespace MtGBattlegroundsDlcConverter;

public sealed class UnsupportedPcBaselineException : Exception
{
    public UnsupportedPcBaselineException(string? detail = null, Exception? inner = null)
        : base(BuildMessage(detail), inner) { }

    private static string BuildMessage(string? detail)
    {
        const string guidance =
            "This converter requires the Windows PC v1.4 installation. The files used by the converter must be unmodified.\r\n\r\n" +
            "Apply the official v1.4 patch to a clean Battlegrounds installation, then run this converter again. " +
            "A replacement No-CD MTGBattlegrounds.exe is okay because this converter does not patch that executable.";

        return string.IsNullOrWhiteSpace(detail)
            ? guidance
            : guidance + "\r\n\r\nDetails: " + detail;
    }
}

public static class PcVerifier
{
    public static async Task VerifySupportedBaselineAsync(
        string gameRoot,
        IReadOnlyCollection<FileRecord> required,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gameRoot))
            throw new UnsupportedPcBaselineException("The selected game folder does not exist.");

        foreach (var file in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(gameRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                await Hashing.VerifyFileAsync(path, file, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new UnsupportedPcBaselineException($"{file.Path}: {ex.Message}", ex);
            }
        }
    }
}
