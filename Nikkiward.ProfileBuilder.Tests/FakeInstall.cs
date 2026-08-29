using Nikkiward.Models;

/// <summary>
/// A throwaway on-disk layout that satisfies the coordinator's path checks.
/// The coordinator calls File.Exists / Directory.Exists on every path it is
/// handed, so the receipts have to point at real files. The layout is
/// parameterized by launcher version directory so tests can model a machine
/// where the game or launcher already updated past the fixture default.
/// </summary>
internal sealed class FakeInstall : IDisposable
{
    private readonly (string ComponentId, string Root, string Relative)[] _layout;

    private FakeInstall(string baseDirectory, string version)
    {
        BaseDirectory = baseDirectory;
        Version = version;
        LauncherRoot = Path.Combine(baseDirectory, "launcher");
        GameRoot = Path.Combine(baseDirectory, "game");
        _layout =
        [
            ("official-launcher", "launcher", "launcher.exe"),
            ("official-backend", "launcher", version + "/xstarter.exe"),
            ("game-bootstrap", "game", "InfinityNikki.exe"),
            ("game-client", "game", "X6Game/Binaries/Win64/X6Game-Win64-Shipping.exe"),
            ("anti-cheat-artifact", "game", "X6Game/Binaries/Win64/AntiCheatExpert/ACE-Service64.exe"),
        ];
    }

    public string BaseDirectory { get; }

    public string Version { get; }

    public string LauncherRoot { get; }

    public string GameRoot { get; }

    /// <summary>The adaptive contract materialized for this layout's version.</summary>
    public LaunchProviderContract Contract => LaunchProviderCatalog.CreateCnWindows(Version);

    public static FakeInstall Create(string version = LaunchProviderCatalog.CnLauncherVersion)
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "nikkiward-launch-contract",
            Guid.NewGuid().ToString("n"));
        var install = new FakeInstall(baseDirectory, version);
        foreach (var (_, root, relative) in install._layout)
        {
            var full = Path.Combine(baseDirectory, root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "stub");
        }

        File.WriteAllText(
            Path.Combine(install.GameRoot, "product.db"),
            "{\"name\":\"InfinityNikki Launcher\",\"version\":2828}");

        return install;
    }

    /// <summary>Receipts shaped the way a passing static preflight would return them.</summary>
    public PreflightComponentResult[] ComponentReceipts() => _layout
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
        var contract = Contract;
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
