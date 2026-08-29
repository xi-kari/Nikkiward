using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nikkiward.Models;
using Nikkiward.Services;

/// <summary>
/// Signature audit for the installation on this machine. Since the adaptive
/// contract (ContractVersion 2) hashes and file versions are diagnostics, not
/// gates, so a game update needs no refresh at all. This tool now reports each
/// component's observed publisher identity, evaluates it against the
/// contract's trusted signer list, and prints a paste-ready
/// TrustedSignerRequirement only when an unknown signer shows up (i.e. the
/// publisher rotated to a certificate with a new subject). Read-only: it never
/// edits source. See LAUNCH_CONTRACT.md §5.
/// </summary>
internal static class ContractRefreshTool
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("== 启动契约签名审计 ==");
        Console.WriteLine("只读扫描本机安装，核对组件签名者是否在契约可信列表内。不会修改任何源文件。");
        Console.WriteLine();

        var builder = new WindowsInstallationProfileBuilder();
        var result = await builder.DiscoverAsync().ConfigureAwait(false);
        var candidate = result.Candidates.FirstOrDefault(c =>
            c.Identity.DistributionChannel is DistributionChannel.Official &&
            !string.IsNullOrWhiteSpace(c.LauncherRootPath) &&
            !string.IsNullOrWhiteSpace(c.GameRootPath));

        if (candidate is null)
        {
            Console.WriteLine("未找到官方渠道安装。请确认游戏已安装，或先在启动器里手动定位游戏目录。");
            foreach (var other in result.Candidates)
            {
                Console.WriteLine(
                    $"  候选: {other.DisplayName} state={other.State} " +
                    $"channel={other.Identity.DistributionChannel} failure={other.FailureCode}");
            }

            return 1;
        }

        var launcherRoot = candidate.LauncherRootPath!;
        var gameRoot = candidate.GameRootPath!;
        Console.WriteLine($"launcher 根: {launcherRoot}");
        Console.WriteLine($"游戏根:      {gameRoot}");

        var versionDirectory = DetectVersionDirectory(launcherRoot);
        Console.WriteLine($"版本目录:    {versionDirectory ?? "(未找到)"}（契约按发现值自动适配，无需改常量）");
        Console.WriteLine();

        var effectiveVersion = versionDirectory ?? LaunchProviderCatalog.CnLauncherVersion;
        if (!LaunchProviderCatalog.TryGet(
                DistributionChannel.Official,
                effectiveVersion,
                out var contract))
        {
            Console.WriteLine($"⚠ 版本目录 {effectiveVersion} 不是三段数字版本，无法实例化契约。");
            return 1;
        }

        var inspector = new WindowsInstallationInspector();
        var missing = new List<string>();
        var untrusted = new List<(string ComponentId, SignerIdentity Signer)>();

        foreach (var requirement in contract.RequiredComponents)
        {
            var root = string.Equals(requirement.RootRole, "GameRoot", StringComparison.Ordinal)
                ? gameRoot
                : launcherRoot;
            var fullPath = Path.Combine(root, requirement.RelativePath);
            if (!File.Exists(fullPath))
            {
                missing.Add($"{requirement.ComponentId} -> {requirement.RelativePath}");
                continue;
            }

            var observed = await inspector
                .InspectComponentAsync(requirement.ComponentId, requirement.ComponentId, fullPath)
                .ConfigureAwait(false);
            var identity = SignerIdentityReader.TryRead(fullPath);

            var trusted = identity is { } signer &&
                WindowsLaunchPreflightVerifier.IsTrustedSigner(requirement.TrustedSigners, signer);
            Console.WriteLine($"[{requirement.ComponentId}]");
            Console.WriteLine($"  路径:        {fullPath}");
            Console.WriteLine($"  签名状态:    {observed.SignatureStatus}（{observed.SignatureStatusCode}）");
            Console.WriteLine($"  签名者指纹:  {identity?.Thumbprint ?? "(读取失败)"}");
            Console.WriteLine($"  签名者 CN:   {identity?.SubjectCommonName ?? "(读取失败)"}");
            Console.WriteLine($"  subject 序列: {identity?.SubjectSerialNumber ?? "(无)"}");
            Console.WriteLine($"  可信判定:    {(trusted && observed.SignatureStatus is AuthenticodeSignatureStatus.Valid ? "通过" : "不通过")}");
            Console.WriteLine($"  SHA-256（仅诊断）:   {observed.Sha256 ?? "(未计算)"}");
            Console.WriteLine($"  文件版本（仅诊断）:  {observed.FileVersion ?? "(无)"} / {observed.ProductVersion ?? "(无)"}");
            Console.WriteLine();

            if (observed.SignatureStatus is AuthenticodeSignatureStatus.Valid &&
                identity is { } validSigner &&
                !trusted)
            {
                untrusted.Add((requirement.ComponentId, validSigner));
            }
        }

        if (missing.Count > 0)
        {
            Console.WriteLine("以下组件在磁盘上不存在：");
            missing.ForEach(m => Console.WriteLine($"  - {m}"));
            Console.WriteLine();
        }

        if (untrusted.Count > 0)
        {
            Console.WriteLine("⚠ 检测到签名有效但不在可信列表的签名者（官方可能更换了证书主体）。");
            Console.WriteLine("  人工确认证书归属后，把以下片段合入 LaunchProviderCatalog 对应的 TrustedSignerRequirement：");
            Console.WriteLine();
            foreach (var group in untrusted.GroupBy(item => item.Signer.Thumbprint))
            {
                var signer = group.First().Signer;
                Console.WriteLine($"    // 影响组件: {string.Join(", ", group.Select(item => item.ComponentId))}");
                Console.WriteLine("    new TrustedSignerRequirement");
                Console.WriteLine("    {");
                Console.WriteLine($"        DisplayName = \"{signer.SubjectCommonName}\",");
                Console.WriteLine($"        Thumbprints = [\"{signer.Thumbprint}\"],");
                Console.WriteLine($"        SubjectCommonName = \"{signer.SubjectCommonName}\",");
                if (!string.IsNullOrWhiteSpace(signer.SubjectSerialNumber))
                {
                    Console.WriteLine($"        SubjectSerialNumber = \"{signer.SubjectSerialNumber}\",");
                }

                Console.WriteLine("    },");
                Console.WriteLine();
            }

            Console.WriteLine("合入后运行：dotnet run --project Nikkiward.ProfileBuilder.Tests -- --current-machine");
        }
        else if (missing.Count == 0)
        {
            Console.WriteLine("所有组件签名者均在契约可信列表内 —— 无需任何维护动作。");
        }

        return missing.Count == 0 && untrusted.Count == 0 ? 0 : 1;
    }

    private static string? DetectVersionDirectory(string launcherRoot)
    {
        try
        {
            return Directory.EnumerateDirectories(launcherRoot)
                .Select(Path.GetFileName)
                .Where(name => name is not null &&
                    System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+\.\d+\.\d+$"))
                .OrderByDescending(name => Version.TryParse(name, out var version)
                    ? version
                    : new Version(0, 0, 0))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
