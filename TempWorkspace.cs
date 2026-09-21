namespace MtGBattlegroundsDlcConverter;

public sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public string XboxExtracted => Path.Combine(Root, "xbox");
    public string Staging => Path.Combine(Root, "staging");

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "MtGBattlegroundsDlcConverter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(XboxExtracted);
        Directory.CreateDirectory(Staging);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* A later UI build will log cleanup failures rather than failing a completed conversion. */ }
    }
}
