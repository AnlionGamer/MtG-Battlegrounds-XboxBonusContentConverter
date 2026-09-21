namespace MtGBattlegroundsDlcConverter;

public static class TextureConverter
{
    public static async Task<int> BuildAllAsync(
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        CancellationToken cancellationToken = default)
    {
        int count = 0;
        count += await BuildOneAsync(
            xboxRoot, stagingRoot, manifest,
            "Textures/ExpansionSpellIcons.utx",
            TexturePackagePatcher.BuildSpellIcons,
            TexturePackagePatcher.ValidateSpellResult,
            cancellationToken);
        count += await BuildOneAsync(
            xboxRoot, stagingRoot, manifest,
            "Textures/ExpansionLandIcons.utx",
            TexturePackagePatcher.BuildLandIcons,
            TexturePackagePatcher.ValidateLandResult,
            cancellationToken);
        return count;
    }

    private static async Task<int> BuildOneAsync(
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        string outputPath,
        Func<string, byte[]> builder,
        Action<byte[]> validator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transform = manifest.Transforms.Single(x => x.Output.Equals(outputPath, StringComparison.OrdinalIgnoreCase));
        if (transform.Extra is null || !transform.Extra.TryGetValue("xbox_source", out var sourceElement))
            throw new InvalidDataException($"Texture transform {outputPath} has no xbox_source.");
        string xboxSource = sourceElement.GetString() ?? throw new InvalidDataException("Invalid texture xbox_source.");
        string input = Path.Combine(xboxRoot, xboxSource.Replace('/', Path.DirectorySeparatorChar));
        byte[] result = builder(input);
        validator(result);

        var expected = manifest.ReferenceOutputs.Single(x => x.Path.Equals(outputPath, StringComparison.OrdinalIgnoreCase));
        // BC3 encoders can produce different-but-equivalent block bitstreams. Geometry and package structure are
        // validated above; fixed output size proves the exact target mip layout was produced.
        if (result.LongLength != expected.Size)
            throw new InvalidDataException($"Generated {outputPath} is {result.LongLength} bytes; expected structural size {expected.Size}.");

        string destination = Path.Combine(stagingRoot, outputPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, result, cancellationToken);
        return 1;
    }
}
