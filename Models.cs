using System.Text.Json.Serialization;

namespace MtGBattlegroundsDlcConverter;

public sealed class ConverterManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("digiex_url")] public string DigiexUrl { get; set; } = "";
    [JsonPropertyName("xbox_inputs")] public List<FileRecord> XboxInputs { get; set; } = [];
    [JsonPropertyName("pc_v1_4_inputs")] public List<FileRecord> PcInputs { get; set; } = [];
    [JsonPropertyName("reference_outputs")] public List<FileRecord> ReferenceOutputs { get; set; } = [];
    [JsonPropertyName("transforms")] public List<TransformRecord> Transforms { get; set; } = [];
}

public sealed class FileRecord
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("role")] public string? Role { get; set; }
}

public sealed class TransformRecord
{
    [JsonPropertyName("output")] public string Output { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}
