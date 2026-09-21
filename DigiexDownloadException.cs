namespace MtGBattlegroundsDlcConverter;

public sealed class DigiexDownloadException : Exception
{
    public DigiexDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
