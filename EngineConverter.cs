using System.Text.Json;

namespace MtGBattlegroundsDlcConverter;

public static class EngineConverter
{
    public static async Task<int> BuildAllAsync(
        string gameRoot,
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        EngineDllPatchManifest nativePatchManifest,
        CancellationToken cancellationToken = default)
    {
        var referenceByPath = manifest.ReferenceOutputs.ToDictionary(x => Normalize(x.Path), StringComparer.OrdinalIgnoreCase);
        int built = 0;

        foreach (var transform in manifest.Transforms.Where(x =>
                     x.Mode.Equals("patch_engine_package", StringComparison.OrdinalIgnoreCase) ||
                     x.Mode.Equals("patch_engine_native", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string outputPath = SafeCombine(stagingRoot, transform.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            byte[] output;
            if (transform.Mode.Equals("patch_engine_package", StringComparison.OrdinalIgnoreCase))
            {
                string pcRelative = GetRequiredString(transform, "pc_source");
                string xboxRelative = GetRequiredString(transform, "xbox_reference");
                output = EnginePackagePatcher.Build(
                    SafeCombine(gameRoot, pcRelative),
                    SafeCombine(xboxRoot, xboxRelative));
            }
            else
            {
                string pcRelative = GetRequiredString(transform, "pc_source");
                output = NativeEnginePatcher.Build(SafeCombine(gameRoot, pcRelative), nativePatchManifest);
            }

            await File.WriteAllBytesAsync(outputPath, output, cancellationToken);

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
        if (transform.Extra is null || !transform.Extra.TryGetValue(property, out var value) || value.ValueKind != JsonValueKind.String)
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
