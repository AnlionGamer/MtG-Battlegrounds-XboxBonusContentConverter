using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace MtGBattlegroundsDlcConverter;

public static class NativeEnginePatcher
{
    public static byte[] Build(string pcEngineDllPath, EngineDllPatchManifest patch)
    {
        byte[] source = File.ReadAllBytes(pcEngineDllPath);
        if (source.LongLength != patch.Size)
            throw new InvalidDataException($"Engine.dll size mismatch. Expected {patch.Size}, got {source.LongLength}.");

        string inputHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        if (!inputHash.Equals(patch.InputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Engine.dll SHA-256 mismatch. Expected {patch.InputSha256}, got {inputHash}.");

        byte[] output = source.ToArray();
        foreach (var operation in patch.Operations)
        {
            byte[] expected = Convert.FromHexString(operation.ExpectHex);
            byte[] replacement = Convert.FromHexString(operation.WriteHex);
            if (expected.Length != replacement.Length)
                throw new InvalidDataException($"Engine.dll patch at 0x{operation.Offset:X} changes file length, which is not supported.");
            if (operation.Offset < 0 || operation.Offset > output.Length - expected.Length)
                throw new InvalidDataException($"Engine.dll patch offset 0x{operation.Offset:X} is outside the file.");
            if (!output.AsSpan(operation.Offset, expected.Length).SequenceEqual(expected))
                throw new InvalidDataException($"Engine.dll bytes at 0x{operation.Offset:X} do not match the verified baseline.");
            replacement.AsSpan().CopyTo(output.AsSpan(operation.Offset, replacement.Length));
        }

        string outputHash = Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
        if (!outputHash.Equals(patch.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Patched Engine.dll SHA-256 mismatch. Expected {patch.OutputSha256}, got {outputHash}.");

        return output;
    }
}

public sealed class EngineDllPatchManifest
{
    [JsonPropertyName("input_sha256")] public string InputSha256 { get; set; } = "";
    [JsonPropertyName("output_sha256")] public string OutputSha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("operations")] public List<EngineDllPatchOperation> Operations { get; set; } = [];
}

public sealed class EngineDllPatchOperation
{
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("expect_hex")] public string ExpectHex { get; set; } = "";
    [JsonPropertyName("write_hex")] public string WriteHex { get; set; } = "";
}
