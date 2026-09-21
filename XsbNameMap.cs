using System.Text;

namespace MtGBattlegroundsDlcConverter;

internal static class XsbNameMap
{
    public static Dictionary<string, List<string>> Read(string xsbPath)
    {
        var data = File.ReadAllBytes(xsbPath);
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        int start = 0;
        while (start < data.Length)
        {
            while (start < data.Length && !IsPrintableAscii(data[start])) start++;
            if (start >= data.Length) break;

            int end = start;
            while (end < data.Length && IsPrintableAscii(data[end])) end++;

            // We only treat a run as a string when the run is NUL terminated.
            if (end < data.Length && data[end] == 0 && end - start >= 4)
            {
                string text = Encoding.ASCII.GetString(data, start, end - start);
                int slash = text.IndexOf('\\');
                if (slash > 0 && slash + 1 < text.Length)
                {
                    string bank = text[..slash];
                    string name = text[(slash + 1)..];
                    if (!groups.TryGetValue(bank, out var list))
                    {
                        list = [];
                        groups.Add(bank, list);
                    }
                    list.Add(name);
                }
            }

            start = Math.Max(end + 1, start + 1);
        }

        return groups;
    }

    private static bool IsPrintableAscii(byte b) => b is >= 0x20 and <= 0x7E;
}
