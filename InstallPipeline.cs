namespace MtGBattlegroundsDlcConverter;

public static class InstallPipeline
{
    public static ConverterManifest LoadManifest()
        => EmbeddedResources.ReadJson<ConverterManifest>("converter_manifest.json");

    public static EngineDllPatchManifest LoadEngineDllPatchManifest()
        => EmbeddedResources.ReadJson<EngineDllPatchManifest>("engine_dll_patch.json");

    public static async Task RunAsync(
        string gameRoot,
        ConverterManifest manifest,
        IProgress<InstallProgress>? progress = null,
        string? manualXboxArchive = null,
        CancellationToken cancellationToken = default)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        if (!GameLocator.IsGameRoot(gameRoot))
            throw new InvalidDataException("The selected folder does not appear to be a Magic: The Gathering - Battlegrounds installation.");

        progress?.Report(new InstallProgress(2, "Checking the Windows v1.4 installation..."));
        await PcVerifier.VerifySupportedBaselineAsync(gameRoot, manifest.PcInputs, cancellationToken);

        using var workspace = new TempWorkspace();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MtGBgXboxBonusContentConverter/1.0.0");
        var acquirer = new SourceAcquirer(http);

        string archivePath;
        if (!string.IsNullOrWhiteSpace(manualXboxArchive))
        {
            archivePath = Path.GetFullPath(manualXboxArchive);
            progress?.Report(new InstallProgress(12, "Using the selected Xbox DLC source archive..."));
        }
        else
        {
            progress?.Report(new InstallProgress(8, "Downloading the original Xbox DLC source from Digiex..."));
            var downloadProgress = new Progress<(long received, long? total)>(p =>
            {
                int percent = 12;
                if (p.total is > 0)
                    percent = 8 + (int)Math.Clamp(Math.Round(20.0 * p.received / p.total.Value), 0, 20);
                string amount = p.total is > 0
                    ? $"{p.received / 1048576.0:F1} / {p.total.Value / 1048576.0:F1} MiB"
                    : $"{p.received / 1048576.0:F1} MiB";
                progress?.Report(new InstallProgress(percent, $"Downloading Xbox DLC source... {amount}"));
            });
            archivePath = await acquirer.DownloadDigiexAsync(manifest.DigiexUrl, workspace.Root, downloadProgress, cancellationToken);
        }

        progress?.Report(new InstallProgress(30, "Extracting and verifying Xbox DLC source..."));
        await SourceAcquirer.ExtractRequiredXboxFilesAsync(archivePath, workspace.XboxExtracted, manifest.XboxInputs, cancellationToken);

        progress?.Report(new InstallProgress(42, "Converting spell and creature audio..."));
        int audio = await AudioConverter.BuildAllAsync(gameRoot, workspace.XboxExtracted, workspace.Staging, manifest, cancellationToken);
        if (audio != 19) throw new InvalidDataException($"Audio stage produced {audio}/19 packages.");

        progress?.Report(new InstallProgress(62, "Applying Windows engine compatibility changes..."));
        EngineDllPatchManifest enginePatch = LoadEngineDllPatchManifest();
        int engine = await EngineConverter.BuildAllAsync(gameRoot, workspace.XboxExtracted, workspace.Staging, manifest, enginePatch, cancellationToken);
        if (engine != 2) throw new InvalidDataException($"Engine stage produced {engine}/2 files.");

        progress?.Report(new InstallProgress(67, "Adapting the Xbox expansion package..."));
        int expansion = await ExpansionConverter.BuildAllAsync(workspace.XboxExtracted, workspace.Staging, manifest, cancellationToken);
        if (expansion != 1) throw new InvalidDataException($"Expansion stage produced {expansion}/1 file.");

        progress?.Report(new InstallProgress(73, "Converting expansion icons..."));
        int textures = await TextureConverter.BuildAllAsync(workspace.XboxExtracted, workspace.Staging, manifest, cancellationToken);
        if (textures != 2) throw new InvalidDataException($"Texture stage produced {textures}/2 files.");

        progress?.Report(new InstallProgress(80, "Preserving the PC v1.4 arena unlock order..."));
        int arenaRegistry = await ArenaRegistryConverter.BuildAllAsync(workspace.XboxExtracted, workspace.Staging, manifest, cancellationToken);
        if (arenaRegistry != 10) throw new InvalidDataException($"Arena-registry stage produced {arenaRegistry}/10 files.");

        progress?.Report(new InstallProgress(86, "Staging the remaining Xbox bonus-content files..."));
        int direct = await DirectFileStager.StageAsync(workspace.XboxExtracted, workspace.Staging, manifest, cancellationToken);
        if (direct != 14) throw new InvalidDataException($"Direct-file stage produced {direct}/14 files.");

        progress?.Report(new InstallProgress(90, "Checking the completed 48-file conversion..."));
        await OutputVerifier.VerifyStagingAsync(workspace.Staging, manifest, cancellationToken);

        progress?.Report(new InstallProgress(92, "Installing Xbox bonus content..."));
        await GameInstaller.InstallAsync(workspace.Staging, gameRoot, manifest, progress, cancellationToken);
        progress?.Report(new InstallProgress(100, "Installation complete."));
    }
}
