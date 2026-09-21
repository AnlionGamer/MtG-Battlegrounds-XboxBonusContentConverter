using System.Text.Json;

namespace MtGBattlegroundsDlcConverter;

public static class AudioConverter
{
    public static async Task<int> BuildAllAsync(
        string gameRoot,
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var referenceByPath = manifest.ReferenceOutputs.ToDictionary(x => Normalize(x.Path), StringComparer.OrdinalIgnoreCase);
        var xsbCache = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        int built = 0;

        foreach (var transform in manifest.Transforms.Where(x => x.Mode.Equals("merge_audio", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string pcRelative = GetRequiredString(transform, "pc_source");
            string xwbRelative = GetRequiredString(transform, "xbox_wavebank");
            string xsbRelative = GetRequiredString(transform, "xsb");

            string pcPath = SafeCombine(gameRoot, pcRelative);
            string xwbPath = SafeCombine(xboxRoot, xwbRelative);
            string xsbPath = SafeCombine(xboxRoot, xsbRelative);
            string outputPath = SafeCombine(stagingRoot, transform.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (!xsbCache.TryGetValue(xsbPath, out var nameGroups))
            {
                nameGroups = XsbNameMap.Read(xsbPath);
                xsbCache.Add(xsbPath, nameGroups);
            }

            string bankName = Path.GetFileNameWithoutExtension(xwbPath);
            if (!nameGroups.TryGetValue(bankName, out var orderedNames))
                throw new InvalidDataException($"XSB does not contain a name list for wave bank {bankName}.");

            var waves = XactWaveBank.DecodeNamedWaves(xwbPath, orderedNames);
            var package = UnrealSoundPackage.Load(pcPath);
            byte[] rebuilt = package.Rebuild(waves, orderedNames);
            await File.WriteAllBytesAsync(outputPath, rebuilt, cancellationToken);

            string key = Normalize(transform.Output);
            if (!referenceByPath.TryGetValue(key, out var expected))
                throw new InvalidDataException($"No reference output record exists for {transform.Output}.");
            await Hashing.VerifyFileAsync(outputPath, expected, cancellationToken);
            built++;
        }

        return built;
    }

    private static string GetRequiredString(TransformRecord transform, string property)
    {
        if (transform.Extra is null || !transform.Extra.TryGetValue(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Transform {transform.Output} is missing required property {property}.");
        return value.GetString() ?? throw new InvalidDataException($"Transform {transform.Output} has an empty {property} property.");
    }

    private static string SafeCombine(string root, string relative)
    {
        string normalized = Normalize(relative);
        if (normalized.Split('/').Any(part => part is "." or ".."))
            throw new InvalidDataException($"Unsafe relative path: {relative}");
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes root: {relative}");
        return full;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
