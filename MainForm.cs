using System.Drawing;
using System.Windows.Forms;

namespace MtGBattlegroundsDlcConverter;

public sealed class MainForm : Form
{
    private readonly ConverterManifest _manifest;
    private readonly TextBox _pathBox = new();
    private readonly Button _browseButton = new();
    private readonly RadioButton _downloadSource = new();
    private readonly RadioButton _localSource = new();
    private readonly TextBox _archiveBox = new();
    private readonly Button _archiveBrowseButton = new();
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();

    public MainForm(ConverterManifest manifest)
    {
        _manifest = manifest;
        Text = "MTG Battlegrounds – OG Xbox Bonus Content Converter for Windows";
        ClientSize = new Size(620, 453);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "MTG Battlegrounds\r\nOG Xbox Bonus Content Converter for Windows",
            AutoSize = true,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Location = new Point(22, 18),
        };
        Controls.Add(title);

        var requirement = new Label
        {
            Text = "Requires PC v1.4. Converter input files must match a clean v1.4 installation; a replacement No-CD EXE is okay.",
            AutoSize = false,
            Size = new Size(576, 36),
            ForeColor = Color.DimGray,
            Location = new Point(22, 78),
        };
        Controls.Add(requirement);

        var pathLabel = new Label { Text = "Game installation:", AutoSize = true, Location = new Point(22, 114) };
        Controls.Add(pathLabel);

        _pathBox.Location = new Point(22, 136);
        _pathBox.Size = new Size(484, 25);
        Controls.Add(_pathBox);

        _browseButton.Text = "Browse...";
        _browseButton.Location = new Point(516, 134);
        _browseButton.Size = new Size(82, 29);
        _browseButton.Click += BrowseClicked;
        Controls.Add(_browseButton);

        var sourceLabel = new Label { Text = "Xbox DLC source:", AutoSize = true, Location = new Point(22, 180) };
        Controls.Add(sourceLabel);

        _downloadSource.Text = "Download automatically from Digiex";
        _downloadSource.AutoSize = true;
        _downloadSource.Checked = true;
        _downloadSource.Location = new Point(25, 203);
        _downloadSource.CheckedChanged += (_, _) => UpdateSourceControls();
        Controls.Add(_downloadSource);

        _localSource.Text = "Use my own DLC installer/archive";
        _localSource.AutoSize = true;
        _localSource.Location = new Point(25, 230);
        _localSource.CheckedChanged += (_, _) => UpdateSourceControls();
        Controls.Add(_localSource);

        _archiveBox.Location = new Point(47, 258);
        _archiveBox.Size = new Size(459, 25);
        _archiveBox.Enabled = false;
        Controls.Add(_archiveBox);

        _archiveBrowseButton.Text = "Browse...";
        _archiveBrowseButton.Location = new Point(516, 256);
        _archiveBrowseButton.Size = new Size(82, 29);
        _archiveBrowseButton.Enabled = false;
        _archiveBrowseButton.Click += ArchiveBrowseClicked;
        Controls.Add(_archiveBrowseButton);

        var info = new Label
        {
            Text = "Unofficial fan-made converter. No game or DLC files are bundled.\r\n" +
                   "Not affiliated with Wizards of the Coast, Atari, Microsoft, or Digiex.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(22, 298),
        };
        Controls.Add(info);

        _progress.Location = new Point(22, 349);
        _progress.Size = new Size(576, 18);
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        Controls.Add(_progress);

        _status.Text = "Ready.";
        _status.AutoEllipsis = true;
        _status.Location = new Point(22, 373);
        _status.Size = new Size(450, 22);
        Controls.Add(_status);

        _installButton.Text = "Install";
        _installButton.Location = new Point(488, 407);
        _installButton.Size = new Size(110, 32);
        _installButton.Click += InstallClicked;
        Controls.Add(_installButton);

        Shown += (_, _) => AutoDetect();
    }

    private void AutoDetect()
    {
        string? detected = GameLocator.AutoDetect();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _pathBox.Text = detected;
            _status.Text = "Game installation detected. PC v1.4 will be verified when installation starts.";
        }
        else
        {
            _status.Text = "Game installation was not detected automatically. Select it with Browse.";
        }
    }

    private void UpdateSourceControls()
    {
        bool local = _localSource.Checked;
        _archiveBox.Enabled = local;
        _archiveBrowseButton.Enabled = local;
    }

    private void BrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Magic: The Gathering - Battlegrounds installation folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(_pathBox.Text)) dialog.InitialDirectory = _pathBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dialog.SelectedPath;
    }

    private void ArchiveBrowseClicked(object? sender, EventArgs e)
    {
        string? selected = SelectLocalArchive();
        if (!string.IsNullOrWhiteSpace(selected))
            _archiveBox.Text = selected;
    }

    private string? SelectLocalArchive()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the original Xbox DLC installer/archive",
            Filter = "DLC archives (*.rar;*.zip;*.7z)|*.rar;*.zip;*.7z|RAR archives (*.rar)|*.rar|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (File.Exists(_archiveBox.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(_archiveBox.Text);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        string path;
        try { path = Path.GetFullPath(_pathBox.Text.Trim()); }
        catch
        {
            MessageBox.Show(this, "Select a valid game installation folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!GameLocator.IsGameRoot(path))
        {
            MessageBox.Show(this, "The selected folder does not contain a recognized Battlegrounds installation.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? manualArchive = null;
        if (_localSource.Checked)
        {
            if (string.IsNullOrWhiteSpace(_archiveBox.Text) || !File.Exists(_archiveBox.Text))
            {
                MessageBox.Show(this, "Select a valid Xbox DLC installer/archive.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            manualArchive = Path.GetFullPath(_archiveBox.Text);
        }

        string sourceDescription = manualArchive is null
            ? "The original Xbox DLC source will be downloaded from Digiex after you continue."
            : "The selected local Xbox DLC archive will be used as the conversion source.";

        var answer = MessageBox.Show(this,
            "Install the original Xbox bonus content into this Windows installation?\r\n\r\n" + path +
            "\r\n\r\nPC requirement: v1.4 with the converter source files unmodified.\r\n\r\n" + sourceDescription,
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return;

        SetBusy(true);
        var progress = new Progress<InstallProgress>(p =>
        {
            _progress.Value = Math.Clamp(p.Percent, 0, 100);
            _status.Text = p.Status;
        });

        try
        {
            await RunInstallAsync(path, manualArchive, progress);
            ShowSuccess();
        }
        catch (DigiexDownloadException)
        {
            var fallback = MessageBox.Show(this,
                "The Xbox DLC source could not be downloaded from Digiex.\r\n\r\nWould you like to select your own copy of the DLC installer/archive instead?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (fallback == DialogResult.Yes)
            {
                SetBusy(false);
                string? selected = SelectLocalArchive();
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _localSource.Checked = true;
                    _archiveBox.Text = selected;
                    SetBusy(true);
                    try
                    {
                        _progress.Value = 0;
                        await RunInstallAsync(path, selected, progress);
                        ShowSuccess();
                    }
                    catch (Exception ex)
                    {
                        ShowFailure(ex);
                    }
                }
                else
                {
                    _status.Text = "Installation cancelled.";
                }
            }
            else
            {
                _status.Text = "Installation failed: Digiex download unavailable.";
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunInstallAsync(string gameRoot, string? manualArchive, IProgress<InstallProgress> progress)
    {
        await Task.Run(async () => await InstallPipeline.RunAsync(gameRoot, _manifest, progress, manualArchive));
    }

    private void ShowSuccess()
    {
        _progress.Value = 100;
        _status.Text = "Installation complete.";
        MessageBox.Show(this, "Installation complete.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowFailure(Exception ex)
    {
        _status.Text = "Installation failed.";
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetBusy(bool busy)
    {
        _pathBox.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _downloadSource.Enabled = !busy;
        _localSource.Enabled = !busy;
        _archiveBox.Enabled = !busy && _localSource.Checked;
        _archiveBrowseButton.Enabled = !busy && _localSource.Checked;
        _installButton.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
