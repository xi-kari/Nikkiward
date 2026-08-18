using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Nikkiward.Features.Updates;
using NuGet.Versioning;

internal static class UpdateReleaseTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("update selector uses semantic version order for both channels", TestSemanticReleaseSelection),
        ("update check validates one GitHub release and manifest", TestValidUpdateCheck),
        ("update check rejects package metadata drift", TestPackageMetadataDrift),
        ("private or unpublished update source stays calm", TestMissingPublicRelease),
        ("release packaging configuration preserves required assets", TestPackagingConfiguration),
        ("release payload gate rejects private and unapproved content", TestReleasePayloadGate),
    ];

    private static Task TestSemanticReleaseSelection()
    {
        var releases = new[]
        {
            CreateRelease("v0.9.0", prerelease: false),
            CreateRelease("v0.10.0", prerelease: false),
            CreateRelease("v0.11.0-preview.2", prerelease: true),
            CreateRelease("v0.11.0-preview.10", prerelease: true),
        };

        var stable = ReleaseSelector.Select(releases, UpdateChannel.Stable);
        var preview = ReleaseSelector.Select(releases, UpdateChannel.Preview);

        AssertEqual("0.10.0", stable?.Version.ToNormalizedString(), "stable semantic maximum");
        AssertEqual("0.11.0-preview.10", preview?.Version.ToNormalizedString(), "preview semantic maximum");
        return Task.CompletedTask;
    }

    private static async Task TestValidUpdateCheck()
    {
        using var feed = CreateFeed();
        var service = new GitHubReleaseUpdateService(feed.Client);

        var result = await service.CheckAsync(
            UpdateChannel.Preview,
            NuGetVersion.Parse("0.1.0-preview.1"));

        AssertEqual(UpdateCheckStatus.UpdateAvailable, result.Status, "update status");
        AssertEqual("0.2.0-preview.1", result.LatestVersion?.ToNormalizedString(), "latest version");
        AssertEqual(
            "https://github.com/xi-kari/Nikkiward/releases/tag/v0.2.0-preview.1",
            result.ReleaseUri?.AbsoluteUri,
            "release URL");
    }

    private static async Task TestPackageMetadataDrift()
    {
        using var feed = CreateFeed(packageAssetSize: 43);
        var service = new GitHubReleaseUpdateService(feed.Client);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(UpdateChannel.Preview, NuGetVersion.Parse("0.1.0-preview.1")),
            "package size drift");
    }

    private static async Task TestMissingPublicRelease()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new GitHubReleaseUpdateService(client);

        var result = await service.CheckAsync(UpdateChannel.Stable, NuGetVersion.Parse("0.1.0"));

        AssertEqual(UpdateCheckStatus.NoPublishedRelease, result.Status, "missing release status");
        Assert(result.ReleaseUri is null, "missing release URL");
    }

    private static Task TestPackagingConfiguration()
    {
        var root = FindWorkspaceRoot();
        var projectPath = Path.Combine(root, "Nikkiward", "Nikkiward.csproj");
        var project = XDocument.Load(projectPath);
        var contentItems = project.Descendants("Content").ToArray();
        var projectVersion = project.Descendants("Version").Single().Value.Trim();
        AssertEqual("0.1.0-preview.2", projectVersion, "preview release version");

        var avatar = contentItems.SingleOrDefault(item =>
            string.Equals((string?)item.Attribute("Update"), "Assets\\XikariAvatar.jpg", StringComparison.Ordinal));
        Assert(avatar is not null, "author avatar publish item");
        AssertEqual("PreserveNewest", avatar!.Element("CopyToOutputDirectory")?.Value, "avatar output copy policy");
        AssertEqual("PreserveNewest", avatar.Element("CopyToPublishDirectory")?.Value, "avatar publish copy policy");

        var defaultFavorites = contentItems.SingleOrDefault(item =>
            string.Equals((string?)item.Attribute("Update"), "Assets\\DefaultFavorites\\*.jpg", StringComparison.Ordinal));
        Assert(defaultFavorites is not null, "default favorites publish item");
        AssertEqual("PreserveNewest", defaultFavorites!.Element("CopyToOutputDirectory")?.Value, "default favorites output copy policy");
        AssertEqual("PreserveNewest", defaultFavorites.Element("CopyToPublishDirectory")?.Value, "default favorites publish copy policy");

        foreach (var requiredAsset in new[]
        {
            "Assets\\NikkiDefaultBackgroundBlur.jpg",
            "Assets\\NikkiGameIcon.png",
        })
        {
            var asset = contentItems.SingleOrDefault(item =>
                string.Equals((string?)item.Attribute("Update"), requiredAsset, StringComparison.Ordinal));
            Assert(asset is not null, $"{requiredAsset} publish item");
            AssertEqual("PreserveNewest", asset!.Element("CopyToOutputDirectory")?.Value, $"{requiredAsset} output copy policy");
            AssertEqual("PreserveNewest", asset.Element("CopyToPublishDirectory")?.Value, $"{requiredAsset} publish copy policy");
        }

        var removedContent = contentItems
            .SelectMany(item => ((string?)item.Attribute("Remove") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(removedContent.Contains("Assets\\NikkiBackground.png"), "local background exclusion");
        Assert(removedContent.Contains("Assets\\NikkiDefaultBackground.png"), "large local background exclusion");

        var trimValues = project.Descendants("PublishTrimmed")
            .Select(element => element.Value.Trim())
            .ToArray();
        Assert(trimValues.Any(value => string.Equals(value, "False", StringComparison.OrdinalIgnoreCase)), "publish trimming disabled");
        Assert(!trimValues.Any(value => string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)), "publish trimming must not be enabled by project defaults");

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        AssertContains(workflow, "Set up Inno Setup", "installer compiler setup");
        AssertContains(workflow, "IncludeSourceRevisionInInformationalVersion=false", "single release commit suffix");
        AssertContains(workflow, "-p:Configuration=Release -p:PublishReadyToRun=true", "release restore matches publish mode");
        AssertContains(workflow, "Package-Installer.ps1", "installer package invocation");
        AssertContains(workflow, "Nikkiward-Setup-win-x64.exe", "installer release asset");
        AssertContains(workflow, "Test Windows installer", "installer workflow acceptance step");
        AssertContains(workflow, "Test-Installer.ps1", "installer workflow acceptance invocation");
        AssertContains(workflow, "Test-ReleasePayload.ps1 -Root $publishDir -Label 'workflow publish'", "publish payload gate");
        AssertContains(workflow, "Test-ReleasePayload.ps1 -Root $verifyDir -Label 'workflow ZIP'", "ZIP payload gate");
        AssertContains(workflow, "does not match project version", "tag and project version gate");
        AssertContains(workflow, "ZIP payload differs from the verified publish payload", "publish and ZIP manifest parity gate");
        Assert(!workflow.Contains("--clobber", StringComparison.Ordinal), "release workflow must not clobber existing assets");
        Assert(!workflow.Contains("--draft=false", StringComparison.Ordinal), "release workflow must not publish automatically");
        AssertContains(workflow, "Release already exists and is immutable", "existing release rejection gate");
        AssertContains(workflow, "RELEASE_DRAFT_CREATED", "draft release result");

        var installerDefinitionPath = Path.Combine(root, "build", "Nikkiward.iss");
        var installerScriptPath = Path.Combine(root, "build", "Package-Installer.ps1");
        var installerTestPath = Path.Combine(root, "build", "Test-Installer.ps1");
        Assert(File.Exists(installerDefinitionPath), "installer definition");
        Assert(File.Exists(installerScriptPath), "installer package script");
        Assert(File.Exists(installerTestPath), "installer acceptance script");
        var installerDefinition = File.ReadAllText(installerDefinitionPath);
        var installerScript = File.ReadAllText(installerScriptPath);
        var installerTest = File.ReadAllText(installerTestPath);
        AssertContains(installerDefinition, "PrivilegesRequired=lowest", "per-user installer privilege policy");
        AssertContains(installerDefinition, "{localappdata}\\Programs\\Nikkiward", "per-user install root");
        AssertContains(installerDefinition, "Nikkiward-Setup-win-x64", "installer output name");
        AssertContains(installerDefinition, "VersionInfoProductVersion={#MyVersionInfoVersion}", "numeric installer product version");
        AssertContains(installerDefinition, "{userdesktop}\\Nikkiward", "per-user desktop shortcut");
        AssertContains(installerDefinition, "Source: \"{#PublishDir}\\*\"", "installer publish payload");
        AssertContains(installerScript, "Test-ReleasePayload.ps1", "installer payload gate");
        AssertContains(installerScript, "installer publish input", "installer payload gate label");
        AssertContains(installerTest, "INSTALL_VERIFY=PASS", "installer file verification");
        AssertContains(installerTest, "LAUNCH_VERIFY=PASS", "installed app launch verification");
        AssertContains(installerTest, "REPAIR_VERIFY=PASS", "installer repair verification");
        AssertContains(installerTest, "UNINSTALL_VERIFY=PASS", "installer uninstall verification");
        AssertContains(installerTest, "Assert-InstalledPayload", "installed tree parity gate");
        AssertContains(installerTest, "missing_resource_restored=True", "repair restores a missing resource");
        AssertContains(installerTest, "changed_dll_restored=True", "repair restores a changed DLL");
        AssertContains(installerTest, "user_data_preserved=True", "installer user-data preservation verification");
        AssertContains(installerTest, "UseDefaultInstallPath", "default installer path verification");

        var payloadValidatorPath = Path.Combine(root, "build", "Test-ReleasePayload.ps1");
        Assert(File.Exists(payloadValidatorPath), "release payload validator");
        var payloadValidator = File.ReadAllText(payloadValidatorPath);
        AssertContains(payloadValidator, "48A54DA85DA2570AAE87F76F0D773A47DD01011ACE7AFE66AABA831FACD2E069", "external gallery hash block");
        AssertContains(payloadValidator, "DefaultFavorites\\01.jpg", "default favorite payload gate");
        AssertContains(payloadValidator, "Assets\\NikkiDefaultBackgroundBlur.jpg", "blur asset gate");
        AssertContains(payloadValidator, "Assets\\NikkiGameIcon.png", "game icon gate");
        AssertContains(payloadValidator, "runtimes\\win-x64\\native\\nuan5_decryption.dll", "native dependency gate");
        AssertContains(payloadValidator, "ProtectedFavorites", "protected favorites rejection gate");
        AssertContains(payloadValidator, "plugin.json", "plugin manifest rejection gate");
        AssertContains(payloadValidator, "AdditionalBlockedHash", "testable blocked hash gate");

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        AssertContains(readme, "GitHub Releases 提供 Windows x64 安装包和便携 ZIP", "published distribution status");
        AssertContains(readme, "docs/PACKAGING_ACCEPTANCE.md", "packaging acceptance link");
        AssertContains(readme, $"当前预览版本：`{projectVersion}`", "README release version");
        AssertContains(installerDefinition, $"#define MyAppVersion \"{projectVersion}\"", "installer release version");
        using (var example = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "docs", "Nikkiward-update.example.json"))))
        {
            AssertEqual(projectVersion, example.RootElement.GetProperty("version").GetString(), "update example version");
            AssertEqual($"v{projectVersion}", example.RootElement.GetProperty("tag").GetString(), "update example tag");
        }

        var acceptancePath = Path.Combine(root, "docs", "PACKAGING_ACCEPTANCE.md");
        Assert(File.Exists(acceptancePath), "packaging acceptance document");
        var acceptance = File.ReadAllText(acceptancePath);
        foreach (var requiredText in new[]
        {
            "unpackaged",
            "self-contained",
            "Windows 10",
            "Windows 11",
            "标准用户",
            "WebView2",
            "中文、空格",
            "非系统盘",
            "Steam",
            "%LOCALAPPDATA%\\Nikkiward",
        })
        {
            AssertContains(acceptance, requiredText, "packaging acceptance contract");
        }

        return Task.CompletedTask;
    }

    private static async Task TestReleasePayloadGate()
    {
        var root = FindWorkspaceRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"Nikkiward-Payload-{Guid.NewGuid():N}");
        var payloadRoot = Path.Combine(fixtureRoot, "publish");

        try
        {
            CreateValidPayloadFixture(root, payloadRoot);
            var valid = await RunPayloadValidatorAsync(root, payloadRoot);
            AssertEqual(0, valid.ExitCode, "valid release payload");
            AssertContains(valid.Output, "PAYLOAD_VERIFY=PASS", "valid release payload result");

            foreach (var (relativePath, content) in new[]
            {
                ("Plugins\\plugin.json", "{}"),
                ("ProtectedFavorites\\object.bin", "object"),
                ("ArtCache\\blur.jpg", "cache"),
                ("settings.json", "{}"),
                ("Logs\\app.log", "log"),
                ("cache\\state.dat", "cache"),
                ("cookie.json", "{}"),
                ("token.dat", "token"),
                ("state.db", "db"),
                ("state.backup", "backup"),
                ("temp\\item.tmp", "temp"),
                ("Nikkiward.pdb", "pdb"),
            })
            {
                var path = Path.Combine(payloadRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
                var result = await RunPayloadValidatorAsync(root, payloadRoot);
                Assert(result.ExitCode != 0, $"payload rejects {relativePath}");
                AssertContains(result.Output, "Release payload", $"payload rejection reason for {relativePath}");
                File.Delete(path);
            }

            var extraMediaPath = Path.Combine(payloadRoot, "private.mp4");
            await File.WriteAllBytesAsync(extraMediaPath, [0x00, 0x01, 0x02]);
            var extraMedia = await RunPayloadValidatorAsync(root, payloadRoot);
            Assert(extraMedia.ExitCode != 0, "payload rejects extra media");
            AssertContains(extraMedia.Output, "media allowlist rejected", "extra media rejection reason");
            File.Delete(extraMediaPath);

            var extraExecutablePath = Path.Combine(payloadRoot, "ExternalGalleryPlugin.exe");
            await File.WriteAllBytesAsync(extraExecutablePath, [0]);
            var extraExecutable = await RunPayloadValidatorAsync(root, payloadRoot);
            Assert(extraExecutable.ExitCode != 0, "payload rejects a fourth executable");
            AssertContains(extraExecutable.Output, "executable allowlist rejected", "extra executable rejection reason");
            File.Delete(extraExecutablePath);

            var nativePath = Path.Combine(payloadRoot, "runtimes", "win-x64", "native", "nuan5_decryption.dll");
            var duplicateNativePath = Path.Combine(payloadRoot, "native-copy.dll");
            File.Copy(nativePath, duplicateNativePath);
            var duplicateNative = await RunPayloadValidatorAsync(root, payloadRoot);
            Assert(duplicateNative.ExitCode != 0, "payload rejects a duplicated native dependency");
            AssertContains(duplicateNative.Output, "native dependency count rejected", "duplicate native rejection reason");
            File.Delete(duplicateNativePath);

            var blockedBytes = Encoding.UTF8.GetBytes("release-gate-blocked-fixture");
            var blockedHash = Convert.ToHexString(SHA256.HashData(blockedBytes));
            var blockedPath = Path.Combine(payloadRoot, "payload.dat");
            await File.WriteAllBytesAsync(blockedPath, blockedBytes);
            var blocked = await RunPayloadValidatorAsync(root, payloadRoot, blockedHash);
            Assert(blocked.ExitCode != 0, "payload rejects an additional blocked hash");
            AssertContains(blocked.Output, "payload.dat", "additional blocked hash path");
            File.Delete(blockedPath);

            var packagePluginPath = Path.Combine(payloadRoot, "Plugins", "plugin.json");
            Directory.CreateDirectory(Path.GetDirectoryName(packagePluginPath)!);
            await File.WriteAllTextAsync(packagePluginPath, "{}");
            var installerGate = await RunPackageInstallerAsync(root, payloadRoot, fixtureRoot);
            Assert(installerGate.ExitCode != 0, "installer delegates to the release payload gate");
            AssertContains(installerGate.Output, "privacy paths rejected", "installer payload rejection reason");
            Assert(!Directory.Exists(Path.Combine(fixtureRoot, "installer-output")), "installer compiler is not reached after payload rejection");
            File.Delete(packagePluginPath);

            var favoritePath = Path.Combine(payloadRoot, "Assets", "DefaultFavorites", "01.jpg");
            var favoriteBytes = await File.ReadAllBytesAsync(favoritePath);
            await File.WriteAllBytesAsync(favoritePath, [0xFF, 0xD8, 0xFF, 0xD9]);
            var tamperedFavorite = await RunPayloadValidatorAsync(root, payloadRoot);
            Assert(tamperedFavorite.ExitCode != 0, "payload rejects a tampered default favorite");
            AssertContains(tamperedFavorite.Output, "hash mismatch", "tampered favorite rejection reason");
            await File.WriteAllBytesAsync(favoritePath, favoriteBytes);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static void CreateValidPayloadFixture(string root, string payloadRoot)
    {
        Directory.CreateDirectory(payloadRoot);
        foreach (var relativePath in new[]
        {
            "Nikkiward.exe",
            "createdump.exe",
            "RestartAgent.exe",
            "Nikkiward.dll",
            "Nikkiward.deps.json",
            "Nikkiward.runtimeconfig.json",
            "Nikkiward.pri",
        })
        {
            var path = Path.Combine(payloadRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0]);
        }

        foreach (var relativePath in new[]
        {
            "Assets\\NikkiwardIcon.ico",
            "Assets\\NikkiDefaultBackground.jpg",
            "Assets\\NikkiDefaultBackgroundBlur.jpg",
            "Assets\\NikkiGameIcon.png",
            "Assets\\XikariAvatar.jpg",
            "Assets\\DefaultFavorites\\01.jpg",
            "Assets\\DefaultFavorites\\02.jpg",
            "Assets\\DefaultFavorites\\03.jpg",
            "Assets\\DefaultFavorites\\04.jpg",
            "Assets\\DefaultFavorites\\05.jpg",
            "Shaders\\LauncherNebula.bin",
            "runtimes\\win-x64\\native\\nuan5_decryption.dll",
            "LICENSE",
            "PRIVACY.md",
            "THIRD-PARTY-NOTICES.md",
        })
        {
            var sourceRoot = relativePath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("Shaders\\", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("runtimes\\", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(root, "Nikkiward")
                : root;
            var sourcePath = Path.Combine(sourceRoot, relativePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Payload fixture source is missing.", sourcePath);
            }
            var destinationPath = Path.Combine(payloadRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunPayloadValidatorAsync(
        string root,
        string payloadRoot,
        string? additionalBlockedHash = null)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(root, "build", "Test-ReleasePayload.ps1"));
        startInfo.ArgumentList.Add("-Root");
        startInfo.ArgumentList.Add(payloadRoot);
        startInfo.ArgumentList.Add("-Label");
        startInfo.ArgumentList.Add("profile-builder-test");
        if (!string.IsNullOrWhiteSpace(additionalBlockedHash))
        {
            startInfo.ArgumentList.Add("-AdditionalBlockedHash");
            startInfo.ArgumentList.Add(additionalBlockedHash);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell payload validator process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static async Task<(int ExitCode, string Output)> RunPackageInstallerAsync(
        string root,
        string payloadRoot,
        string fixtureRoot)
    {
        var fakeIsccPath = Path.Combine(fixtureRoot, "ISCC.exe");
        File.WriteAllBytes(fakeIsccPath, [0]);
        var startInfo = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(root, "build", "Package-Installer.ps1"),
            "-PublishDir",
            payloadRoot,
            "-OutputDir",
            Path.Combine(fixtureRoot, "installer-output"),
            "-Version",
            "0.0.0-test",
            "-IsccPath",
            fakeIsccPath,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell package installer process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Nikkiward workspace root was not found.");
    }

    private static void AssertContains(string text, string expected, string message)
    {
        Assert(text.Contains(expected, StringComparison.Ordinal), $"{message}: {expected}");
    }

    private static TestFeed CreateFeed(long packageAssetSize = 42)
    {
        const string version = "0.2.0-preview.1";
        const string packageHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var manifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Product = "Nikkiward",
            Channel = "preview",
            Version = version,
            Tag = $"v{version}",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            MinimumSupportedVersion = "0.1.0-preview.1",
            PublishedAtUtc = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
            Package = new UpdatePackageManifest
            {
                FileName = "Nikkiward-win-x64.zip",
                Sha256 = packageHash,
                Size = 42,
                RuntimeIdentifier = "win-x64",
                Format = "zip",
            },
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            UpdateJsonContext.Default.UpdateManifest);
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        var release = new GitHubRelease
        {
            TagName = $"v{version}",
            HtmlUrl = $"https://github.com/xi-kari/Nikkiward/releases/tag/v{version}",
            Prerelease = true,
            PublishedAt = manifest.PublishedAtUtc,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "Nikkiward-update.json",
                    Size = manifestBytes.LongLength,
                    Digest = $"sha256:{manifestHash}",
                    BrowserDownloadUrl = $"https://github.com/xi-kari/Nikkiward/releases/download/v{version}/Nikkiward-update.json",
                },
                new GitHubReleaseAsset
                {
                    Name = "Nikkiward-win-x64.zip",
                    Size = packageAssetSize,
                    Digest = $"sha256:{packageHash}",
                    BrowserDownloadUrl = $"https://github.com/xi-kari/Nikkiward/releases/download/v{version}/Nikkiward-win-x64.zip",
                },
            ],
        };
        var releasesBytes = JsonSerializer.SerializeToUtf8Bytes(
            new[] { release },
            UpdateJsonContext.Default.GitHubReleaseArray);

        var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                return JsonResponse(releasesBytes);
            }
            if (path.EndsWith("/Nikkiward-update.json", StringComparison.Ordinal))
            {
                return JsonResponse(manifestBytes);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        return new TestFeed(client);
    }

    private static GitHubRelease CreateRelease(string tag, bool prerelease) => new()
    {
        TagName = tag,
        HtmlUrl = $"https://github.com/xi-kari/Nikkiward/releases/tag/{tag}",
        Prerelease = prerelease,
    };

    private static HttpResponseMessage JsonResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
        {
            Headers =
            {
                ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"),
            },
        },
    };

    private static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class TestFeed(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose() => Client.Dispose();
    }
}
