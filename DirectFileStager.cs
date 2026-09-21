namespace MtGBattlegroundsDlcConverter;

internal static class DirectFileStager
{
    public static async Task<int> StageAsync(
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        CancellationToken cancellationToken = default)
    {
        int count = 0;
        foreach (var transform in manifest.Transforms.Where(x => x.Mode.Equals("copy_or_rename", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transform.Extra is null || !transform.Extra.TryGetValue("xbox_source", out var sourceElement))
                throw new InvalidDataException($"Direct-copy transform {transform.Output} has no xbox_source.");
            string sourceRelative = sourceElement.GetString() ?? throw new InvalidDataException("Invalid xbox_source.");
            string source = Path.Combine(xboxRoot, sourceRelative.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(stagingRoot, transform.Output.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);

            var expected = manifest.ReferenceOutputs.Single(x => x.Path.Equals(transform.Output, StringComparison.OrdinalIgnoreCase));
            await Hashing.VerifyFileAsync(destination, expected, cancellationToken);
            count++;
        }
        return count;
    }
}
