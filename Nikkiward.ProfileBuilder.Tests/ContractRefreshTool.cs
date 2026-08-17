using System.Security.Cryptography;
using Nikkiward.Models;
using Nikkiward.Services;

/// <summary>
/// Reads the installation on this machine and prints a paste-ready
/// RequiredComponents block for LaunchProviderCatalog. Read-only: it never
/// edits source. See LAUNCH_CONTRACT.md §5.
/// </summary>
internal static class ContractRefreshTool
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("== 启动契约刷新工具 ==");
        Console.WriteLine("只读扫描本机安装，输出可粘贴的契约片段。不会修改任何源文件。");
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
        Console.WriteLine($"版本目录:    {versionDirectory ?? "(未找到)"}");
        if (versionDirectory is not null &&
            !string.Equals(versionDirectory, LaunchProviderCatalog.CnLauncherVersion, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ 版本目录已从 {LaunchProviderCatalog.CnLauncherVersion} 变为 {versionDirectory}。");
            Console.WriteLine($"    需同时把 CnLauncherVersion 改为 \"{versionDirectory}\"，");
            Console.WriteLine("    并递增 ContractVersion（契约 id 可保留或按新版本命名）。");
        }

        var effectiveVersion = versionDirectory ?? LaunchProviderCatalog.CnLauncherVersion;
        Console.WriteLine();

        var inspector = new WindowsInstallationInspector();
        var probes = new (string ComponentId, string RootRole, string Root, string Relative)[]
        {
            ("official-launcher", "LauncherRoot", launcherRoot, "launcher.exe"),
            ("official-backend", "LauncherRoot", launcherRoot, Path.Combine(effectiveVersion, "xstarter.exe")),
            ("game-bootstrap", "GameRoot", gameRoot, "InfinityNikki.exe"),
            ("game-client", "GameRoot", gameRoot,
                Path.Combine("X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe")),
            ("anti-cheat-artifact", "GameRoot", gameRoot,
                Path.Combine("X6Game", "Binaries", "Win64", "AntiCheatExpert", "ACE-Service64.exe")),
        };

        var lines = new List<string>();
        var drifted = new List<string>();
        var missing = new List<string>();

        foreach (var probe in probes)
        {
            var fullPath = Path.Combine(probe.Root, probe.Relative);
            if (!File.Exists(fullPath))
            {
                missing.Add($"{probe.ComponentId} -> {probe.Relative}");
                continue;
            }

            var observed = await inspector
                .InspectComponentAsync(probe.ComponentId, probe.ComponentId, fullPath)
                .ConfigureAwait(false);
            var sha = observed.Sha256 ?? await ComputeSha256Async(fullPath).ConfigureAwait(false);
            var thumbprint = ReadSignerThumbprint(fullPath);

            var expected = LaunchProviderCatalog.CnWindows131.RequiredComponents
                .FirstOrDefault(c => c.ComponentId == probe.ComponentId);
            if (expected is not null &&
                !string.Equals(expected.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                drifted.Add(probe.ComponentId);
            }

            if (observed.SignatureStatus is not AuthenticodeSignatureStatus.Valid)
            {
                Console.WriteLine(
                    $"  ⚠ {probe.ComponentId} 签名状态为 {observed.SignatureStatus}" +
                    $"（{observed.SignatureStatusCode}），请人工确认后再采用。");
            }

            lines.Add(Format(probe, sha, observed, thumbprint));
        }

        if (missing.Count > 0)
        {
            Console.WriteLine("以下组件在磁盘上不存在，无法生成契约：");
            missing.ForEach(m => Console.WriteLine($"  - {m}"));
            Console.WriteLine();
        }

        Console.WriteLine(drifted.Count == 0
            ? "所有组件哈希与当前契约一致 —— 无需刷新。"
            : $"以下组件已漂移，需要刷新：{string.Join(", ", drifted)}");

        if (lines.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("---- 以下内容替换 LaunchProviderCatalog.RequiredComponents ----");
            Console.WriteLine("        RequiredComponents = new[]");
            Console.WriteLine("        {");
            Console.WriteLine(string.Join($",{Environment.NewLine}", lines));
            Console.WriteLine("        },");
            Console.WriteLine("---- 结束 ----");
            Console.WriteLine();
            Console.WriteLine("粘贴后运行：dotnet run --project Nikkiward.ProfileBuilder.Tests -- --current-machine");
        }

        return missing.Count == 0 ? 0 : 1;
    }

    private static string? ReadSignerThumbprint(string path)
    {
        try
        {
            // X509CertificateLoader cannot read an Authenticode signature out of a PE
            // file; CreateFromSignedFile remains the only API that does.
#pragma warning disable SYSLIB0057
            using var certificate =
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
            using var signer =
                new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate);
#pragma warning restore SYSLIB0057
            return signer.Thumbprint;
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string Format(
        (string ComponentId, string RootRole, string Root, string Relative) probe,
        string sha,
        ComponentVerification observed,
        string? thumbprint)
    {
        var relative = probe.ComponentId switch
        {
            "official-backend" => $"Path.Combine(CnLauncherVersion, \"xstarter.exe\")",
            "game-client" => "Path.Combine(\n                    \"X6Game\",\n                    \"Binaries\",\n                    \"Win64\",\n                    \"X6Game-Win64-Shipping.exe\")",
            "anti-cheat-artifact" => "Path.Combine(\n                    \"X6Game\",\n                    \"Binaries\",\n                    \"Win64\",\n                    \"AntiCheatExpert\",\n                    \"ACE-Service64.exe\")",
            _ => $"\"{probe.Relative.Replace("\\", "\\\\")}\"",
        };

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("            new BinaryIdentityRequirement");
        builder.AppendLine("            {");
        builder.AppendLine($"                ComponentId = \"{probe.ComponentId}\",");
        if (probe.RootRole != "LauncherRoot")
        {
            builder.AppendLine($"                RootRole = \"{probe.RootRole}\",");
        }

        builder.AppendLine($"                RelativePath = {relative},");
        builder.AppendLine($"                ExpectedSha256 = \"{sha}\",");
        if (!string.IsNullOrWhiteSpace(observed.FileVersion))
        {
            builder.AppendLine($"                ExpectedFileVersion = \"{observed.FileVersion}\",");
        }

        if (!string.IsNullOrWhiteSpace(observed.ProductVersion))
        {
            builder.AppendLine($"                ExpectedProductVersion = \"{observed.ProductVersion}\",");
        }

        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            builder.AppendLine($"                ExpectedSignerThumbprint = \"{thumbprint}\",");
        }

        builder.Append("            }");
        return builder.ToString();
    }

    private static string? DetectVersionDirectory(string launcherRoot)
    {
        try
        {
            return Directory.EnumerateDirectories(launcherRoot)
                .Select(Path.GetFileName)
                .Where(name => name is not null &&
                    System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+\.\d+\.\d+$"))
                .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 20,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
