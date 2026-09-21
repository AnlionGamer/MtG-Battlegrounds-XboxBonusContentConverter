using System.Text;
using System.Text.Json;

namespace MtGBattlegroundsDlcConverter;

/// <summary>
/// Keeps the PC v1.4 arena registry ordering intact while registering Glacial Vale late.
/// The stock PC menu treats registry slot (3,1) as SecretLevel, so inserting the DLC arena
/// before the stock arenas causes Mishra's Stronghold to be mistaken for that hidden slot.
/// </summary>
internal static class ArenaRegistryConverter
{
    private static readonly byte[] PublicHeader = Encoding.ASCII.GetBytes("[Public]\r\n");
    private static readonly byte[] ArenaRegistration = Encoding.ASCII.GetBytes(
        "Object=(Name=MajorArenaBonus1,Class=Class,MetaClass=Engine.LevelSummary,Description=\"DLoaded|Glacial Vale|ExpansionLandIcons.ExpansionArena1smallicon|ExpansionLandIcons.ExpansionArena1Bigicon\")");
    private static readonly byte[] CrLf = [13, 10];

    public static async Task<int> BuildAllAsync(
        string xboxRoot,
        string stagingRoot,
        ConverterManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var expectedByPath = manifest.ReferenceOutputs.ToDictionary(
            x => Normalize(x.Path), StringComparer.OrdinalIgnoreCase);

        int built = 0;
        foreach (var transform in manifest.Transforms.Where(x =>
                     x.Mode.Equals("relocate_arena_registration", StringComparison.OrdinalIgnoreCase) ||
                     x.Mode.Equals("late_arena_registration", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string sourceRelative = GetRequiredString(transform, "xbox_source");
            string sourcePath = SafeCombine(xboxRoot, sourceRelative);
            byte[] source = await File.ReadAllBytesAsync(sourcePath, cancellationToken);

            int registrationOffset = source.AsSpan().IndexOf(ArenaRegistration);
            if (registrationOffset < 0)
                throw new InvalidDataException($"Glacial Vale LevelSummary registration was not found in {sourceRelative}.");
            if (source.AsSpan(registrationOffset + ArenaRegistration.Length).IndexOf(ArenaRegistration) >= 0)
                throw new InvalidDataException($"Multiple Glacial Vale LevelSummary registrations were found in {sourceRelative}.");

            byte[] output = transform.Mode.Equals("relocate_arena_registration", StringComparison.OrdinalIgnoreCase)
                ? RemoveRegistrationLine(source, registrationOffset)
                : BuildLateRegistrySidecar();

            string outputPath = SafeCombine(stagingRoot, transform.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, output, cancellationToken);

            string key = Normalize(transform.Output);
            if (!expectedByPath.TryGetValue(key, out var expected))
                throw new InvalidDataException($"No reference output record exists for {transform.Output}.");
            await Hashing.VerifyFileAsync(outputPath, expected, cancellationToken);
            built++;
        }

        return built;
    }

    private static byte[] RemoveRegistrationLine(byte[] source, int offset)
    {
        int end = offset + ArenaRegistration.Length;
        if (end + 1 < source.Length && source[end] == 13 && source[end + 1] == 10)
            end += 2;
        else if (end < source.Length && source[end] == 10)
            end += 1;
        else
            throw new InvalidDataException("The Glacial Vale registry line has an unexpected line ending.");

        byte[] result = new byte[source.Length - (end - offset)];
        Buffer.BlockCopy(source, 0, result, 0, offset);
        Buffer.BlockCopy(source, end, result, offset, source.Length - end);
        return result;
    }

    private static byte[] BuildLateRegistrySidecar()
    {
        byte[] result = new byte[PublicHeader.Length + ArenaRegistration.Length + CrLf.Length];
        int p = 0;
        Buffer.BlockCopy(PublicHeader, 0, result, p, PublicHeader.Length);
        p += PublicHeader.Length;
        Buffer.BlockCopy(ArenaRegistration, 0, result, p, ArenaRegistration.Length);
        p += ArenaRegistration.Length;
        Buffer.BlockCopy(CrLf, 0, result, p, CrLf.Length);
        return result;
    }

    private static string GetRequiredString(TransformRecord transform, string property)
    {
        if (transform.Extra is null ||
            !transform.Extra.TryGetValue(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Transform {transform.Output} is missing required property {property}.");

        return value.GetString() ??
               throw new InvalidDataException($"Transform {transform.Output} has an empty {property} property.");
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
