using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MtGBattlegroundsDlcConverter;

internal static class EmbeddedResources
{
    private const string Prefix = "MtGBattlegroundsDlcConverter.Manifests.";

    public static T ReadJson<T>(string fileName) where T : class
    {
        string json = ReadText(Prefix + fileName);
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidDataException($"Could not read embedded resource {fileName}.");
    }

    private static string ReadText(string resourceName)
    {
        Assembly assembly = typeof(EmbeddedResources).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            string available = string.Join(", ", assembly.GetManifestResourceNames().OrderBy(x => x));
            throw new InvalidDataException(
                $"Embedded resource {resourceName} was not found. Available resources: {available}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
