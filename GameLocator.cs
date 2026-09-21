using Microsoft.Win32;

namespace MtGBattlegroundsDlcConverter;

public static class GameLocator
{
    private const string ProductCode = "{0C88C4A1-A9D7-4C28-8F06-4C2048765193}";

    public static string? AutoDetect()
    {
        var candidates = new List<string>();

        // This is the same uninstall product code embedded in Atari's v1.4 patcher.
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}");
                AddCandidate(candidates, key?.GetValue("InstallLocation") as string);
            }
            catch { }
        }

        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\MTGBattlegrounds.exe");
                if (key?.GetValue(null) is string exe && !string.IsNullOrWhiteSpace(exe))
                    AddCandidate(candidates, Path.GetDirectoryName(Path.GetDirectoryName(exe.Trim('"'))));
            }
            catch { }
        }

        string? pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string baseDir in new[] { pf86, pf }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, Path.Combine(baseDir, "Atari", "Magic The Gathering - Battlegrounds"));
            AddCandidate(candidates, Path.Combine(baseDir, "Atari", "Magic: The Gathering - Battlegrounds"));
            AddCandidate(candidates, Path.Combine(baseDir, "Magic The Gathering - Battlegrounds"));
        }
        AddCandidate(candidates, @"C:\Games\Magic The Gathering - Battlegrounds");

        return candidates.FirstOrDefault(IsGameRoot);
    }

    public static bool IsGameRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string root = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(Path.Combine(root, "SYSTEM", "MTGBattlegrounds.EXE")) &&
                   File.Exists(Path.Combine(root, "SYSTEM", "Engine.u")) &&
                   File.Exists(Path.Combine(root, "SYSTEM", "Engine.dll"));
        }
        catch { return false; }
    }

    private static void AddCandidate(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string path = value.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.EndsWith($"{Path.DirectorySeparatorChar}SYSTEM", StringComparison.OrdinalIgnoreCase))
            path = Directory.GetParent(path)?.FullName ?? path;
        if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
    }
}
