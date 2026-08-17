using Nikkiward.Models;

/// <summary>
/// A throwaway on-disk layout that satisfies the coordinator's path checks.
/// The coordinator calls File.Exists / Directory.Exists on every path it is
/// handed, so the receipts have to point at real files.
/// </summary>
internal sealed class FakeInstall : IDisposable
{
    private static readonly (string ComponentId, string Root, string Relative)[] Layout =
    [
        ("official-launcher", "launcher", "launcher.exe"),
        ("official-backend", "launcher", LaunchProviderCatalog.CnLauncherVersion + "/xstarter.exe"),
        ("game-bootstrap", "game", "InfinityNikki.exe"),
        ("game-client", "game", "X6Game/Binaries/Win64/X6Game-Win64-Shipping.exe"),
        ("anti-cheat-artifact", "game", "X6Game/Binaries/Win64/AntiCheatExpert/ACE-Service64.exe"),
    ];

    private FakeInstall(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        LauncherRoot = Path.Combine(baseDirectory, "launcher");
        GameRoot = Path.Combine(baseDirectory, "game");
    }

    public string BaseDirectory { get; }

    public string LauncherRoot { get; }

    public string GameRoot { get; }

    public static FakeInstall Create()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "nikkiward-launch-contract",
            Guid.NewGuid().ToString("n"));
        var install = new FakeInstall(baseDirectory);
        foreach (var (_, root, relative) in Layout)
        {
            var full = Path.Combine(baseDirectory, root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "stub");
        }

        return install;
    }

    /// <summary>Receipts shaped the way a passing static preflight would return them.</summary>
    public PreflightComponentResult[] ComponentReceipts() => Layout
        .Select(entry => new PreflightComponentResult
        {
            ComponentId = entry.ComponentId,
            FilePath = Path.Combine(
                BaseDirectory,
                entry.Root,
                entry.Relative.Replace('/', Path.DirectorySeparatorChar)),
            Passed = true,
            SignatureStatus = AuthenticodeSignatureStatus.Valid,
        })
        .ToArray();

    public InstallationProfileCandidate BuildCandidate(
        Func<LaunchProviderBinding, LaunchProviderBinding>? mutateProvider = null)
    {
        var contract = LaunchProviderCatalog.CnWindows131;
        var provider = new LaunchProviderBinding
        {
            ProviderId = contract.ContractId,
            ContractVersion = contract.ContractVersion,
            BackendExecutablePath = Path.Combine(LauncherRoot, contract.BackendRelativeExecutablePath),
            WorkingDirectory = LauncherRoot,
            ArgumentPresetId = contract.ArgumentPresetId,
            ArgumentList = contract.ArgumentList.ToArray(),
            MaximumCapability = LaunchCapability.OfficialAssisted,
            ExecutionEnabled = contract.ExecutionEnabled,
        };

        return new InstallationProfileCandidate
        {
            ProfileId = "fake-cn-official",
            DisplayName = "无限暖暖 · 国服（测试夹具）",
            Identity = new ProfileIdentity
            {
                RegionFamily = RegionFamily.MainlandChina,
                DistributionChannel = DistributionChannel.Official,
                AccountAuthority = AccountAuthority.Papergames,
            },
            State = InstallationCandidateState.ReadyForStaticVerification,
            LauncherRootPath = LauncherRoot,
            GameRootPath = GameRoot,
            Provider = mutateProvider is null ? provider : mutateProvider(provider),
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(BaseDirectory))
            {
                Directory.Delete(BaseDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
