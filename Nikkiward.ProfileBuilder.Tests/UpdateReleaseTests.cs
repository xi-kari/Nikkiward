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

        var avatar = contentItems.SingleOrDefault(item =>
            string.Equals((string?)item.Attribute("Update"), "Assets\\XikariAvatar.jpg", StringComparison.Ordinal));
        Assert(avatar is not null, "author avatar publish item");
        AssertEqual("PreserveNewest", avatar!.Element("CopyToOutputDirectory")?.Value, "avatar output copy policy");
        AssertEqual("PreserveNewest", avatar.Element("CopyToPublishDirectory")?.Value, "avatar publish copy policy");

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
        var publishChecks = ExtractSection(workflow, "$required = @(", "foreach ($path in $required)");
        var zipChecks = ExtractSection(workflow, "$requiredInZip = @(", "foreach ($path in $requiredInZip)");
        AssertContains(publishChecks, "Assets\\XikariAvatar.jpg", "publish avatar verification");
        AssertContains(zipChecks, "Assets\\XikariAvatar.jpg", "ZIP avatar verification");
        AssertContains(workflow, "Set up Inno Setup", "installer compiler setup");
        AssertContains(workflow, "IncludeSourceRevisionInInformationalVersion=false", "single release commit suffix");
        AssertContains(workflow, "-p:Configuration=Release -p:PublishReadyToRun=true", "release restore matches publish mode");
        AssertContains(workflow, "Package-Installer.ps1", "installer package invocation");
        AssertContains(workflow, "Nikkiward-Setup-win-x64.exe", "installer release asset");
        AssertContains(workflow, "Test Windows installer", "installer workflow acceptance step");
        AssertContains(workflow, "Test-Installer.ps1", "installer workflow acceptance invocation");

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
        AssertContains(installerScript, "runtimes\\win-x64\\native\\nuan5_decryption.dll", "installer native dependency gate");
        AssertContains(installerTest, "INSTALL_VERIFY=PASS", "installer file verification");
        AssertContains(installerTest, "LAUNCH_VERIFY=PASS", "installed app launch verification");
        AssertContains(installerTest, "REPAIR_VERIFY=PASS", "installer repair verification");
        AssertContains(installerTest, "UNINSTALL_VERIFY=PASS", "installer uninstall verification");
        AssertContains(installerTest, "user_data_preserved=True", "installer user-data preservation verification");
        AssertContains(installerTest, "UseDefaultInstallPath", "default installer path verification");

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        AssertContains(readme, "GitHub Releases 提供 Windows x64 安装包和便携 ZIP", "published distribution status");
        AssertContains(readme, "docs/PACKAGING_ACCEPTANCE.md", "packaging acceptance link");

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

    private static string ExtractSection(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert(startIndex >= 0, $"section start is missing: {start}");
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert(endIndex > startIndex, $"section end is missing: {end}");
        return text[startIndex..endIndex];
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
