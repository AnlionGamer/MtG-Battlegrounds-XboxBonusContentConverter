namespace MtGBattlegroundsDlcConverter;

internal static class GameInstaller
{
    public static async Task InstallAsync(
        string stagingRoot,
        string gameRoot,
        ConverterManifest manifest,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int total = manifest.ReferenceOutputs.Count;
        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.ReferenceOutputs[i];
            string source = Path.Combine(stagingRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(gameRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // Deliberately old-style patcher behavior: overwrite/install directly, no backup or rollback layer.
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);

            progress?.Report(new InstallProgress(92 + (int)Math.Round((i + 1) * 7.0 / total), $"Installing {file.Path}..."));
        }
    }
}
