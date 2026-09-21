namespace MtGBattlegroundsDlcConverter;

internal static class OutputVerifier
{
    private static bool IsSemanticTexture(string path) =>
        path.Equals("Textures/ExpansionSpellIcons.utx", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("Textures/ExpansionLandIcons.utx", StringComparison.OrdinalIgnoreCase);

    public static async Task VerifyStagingAsync(
        string stagingRoot,
        ConverterManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var expectedPaths = new HashSet<string>(manifest.ReferenceOutputs.Select(x => Normalize(x.Path)), StringComparer.OrdinalIgnoreCase);
        var actualPaths = Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
            .Select(x => Normalize(Path.GetRelativePath(stagingRoot, x)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expectedPaths.Except(actualPaths, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var extra = actualPaths.Except(expectedPaths, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
            throw new InvalidDataException($"Staging file set mismatch. Missing: {string.Join(", ", missing)}; Extra: {string.Join(", ", extra)}");

        foreach (var expected in manifest.ReferenceOutputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(stagingRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar));
            if (IsSemanticTexture(expected.Path))
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != expected.Size)
                    throw new InvalidDataException($"Texture output {expected.Path} failed structural size validation.");
                byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (expected.Path.EndsWith("ExpansionSpellIcons.utx", StringComparison.OrdinalIgnoreCase))
                    TexturePackagePatcher.ValidateSpellResult(bytes);
                else
                    TexturePackagePatcher.ValidateLandResult(bytes);
            }
            else
            {
                await Hashing.VerifyFileAsync(path, expected, cancellationToken);
            }
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
