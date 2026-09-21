using System.Windows.Forms;

namespace MtGBattlegroundsDlcConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var manifest = InstallPipeline.LoadManifest();
            Application.Run(new MainForm(manifest));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MTG Battlegrounds – OG Xbox Bonus Content Converter for Windows",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
