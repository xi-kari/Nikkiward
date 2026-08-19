using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Nikkiward.Features.Background;

public enum WallpaperImportMode
{
    HolographicCard,
    MotionBackdrop,
}

public enum WallpaperResolvedKind
{
    Still,
    Motion,
    WallpaperEnginePackage,
}

public enum WallpaperPackageType
{
    Unknown,
    Scene,
    Video,
    Web,
    Application,
}

public sealed record WallpaperImportResolution(
    bool IsUsable,
    WallpaperResolvedKind Kind,
    string? SourcePath,
    string? DisplayName,
    string? RejectReason,
    WallpaperPackageType PackageType = WallpaperPackageType.Unknown,
    string? PreviewPath = null)
{
    public static WallpaperImportResolution Reject(string reason) =>
        new(false, WallpaperResolvedKind.Still, null, null, reason);
}

public sealed record WallpaperPackageEntry(
    string Name,
    uint Offset,
    uint Length);

public sealed record WallpaperPackageDescriptor(
    string PackagePath,
    string RootPath,
    string Version,
    IReadOnlyList<WallpaperPackageEntry> Entries,
    WallpaperPackageType ProjectType,
    string? PreviewPath,
    string? MediaPath,
    string? RejectReason)
{
    public bool IsUsable => RejectReason is null;
}

public static class WallpaperSourceRules
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<string> StillExtensions { get; } =
        Array.AsReadOnly(
        [
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff",
            ".heic",
            ".heif",
        ]);

    public static IReadOnlyList<string> PackageExtensions { get; } =
        Array.AsReadOnly([".pkg"]);

    public static bool IsStillExtension(string? extension) =>
        StillExtensions.Contains(
            extension ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

    public static bool IsPackageExtension(string? extension) =>
        PackageExtensions.Contains(
            extension ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

    public static bool IsProjectMetadata(string path) =>
        string.Equals(
            Path.GetFileName(path),
            "project.json",
            StringComparison.OrdinalIgnoreCase);

    public static WallpaperImportMode InferMode(string path) =>
        MotionSourceRules.IsSupportedExtension(Path.GetExtension(path))
            ? WallpaperImportMode.MotionBackdrop
            : WallpaperImportMode.HolographicCard;

    public static WallpaperImportResolution Resolve(
        string sourcePath,
        WallpaperImportMode mode)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return WallpaperImportResolution.Reject("没有选择壁纸文件。");
        }

        try
        {
            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
            {
                return WallpaperImportResolution.Reject("壁纸文件不存在或无法读取。");
            }

            var extension = Path.GetExtension(fullPath);
            if (string.Equals(extension, ".mpkg", StringComparison.OrdinalIgnoreCase))
            {
                return WallpaperImportResolution.Reject(
                    "检测到移动端 .mpkg 包；请选择桌面 Wallpaper Engine 的 .pkg 或普通媒体文件。");
            }

            if (IsPackageExtension(extension))
            {
                return ResolvePackage(fullPath, mode);
            }

            if (IsProjectMetadata(fullPath))
            {
                return ResolveProjectMetadata(fullPath, mode);
            }

            if (IsStillExtension(extension))
            {
                return mode == WallpaperImportMode.MotionBackdrop
                    ? WallpaperImportResolution.Reject(
                        "静态图片不能作为动态背景播放，请切换到光栅卡片模式。")
                    : new(
                        true,
                        WallpaperResolvedKind.Still,
                        fullPath,
                        Path.GetFileName(fullPath),
                        null);
            }

            if (MotionSourceRules.IsSupportedExtension(extension))
            {
                return new(
                    true,
                    WallpaperResolvedKind.Motion,
                    fullPath,
                    Path.GetFileName(fullPath),
                    null);
            }

            if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase))
            {
                var projectPath = Path.Combine(
                    Path.GetDirectoryName(fullPath) ?? string.Empty,
                    "project.json");
                if (File.Exists(projectPath))
                {
                    return ResolveProjectMetadata(projectPath, mode);
                }
            }

            return WallpaperImportResolution.Reject(
                "文件类型不受支持；请选择图片、视频或包含 project.json 的 Wallpaper Engine 项目。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return WallpaperImportResolution.Reject("壁纸文件不存在或无法读取。");
        }
    }

    private static WallpaperImportResolution ResolvePackage(
        string packagePath,
        WallpaperImportMode mode)
    {
        var package = WallpaperPackageReader.Inspect(packagePath);
        if (!package.IsUsable)
        {
            return WallpaperImportResolution.Reject(
                package.RejectReason ?? "Wallpaper .pkg 包格式无效。");
        }

        return package.ProjectType switch
        {
            WallpaperPackageType.Scene => new(
                true,
                WallpaperResolvedKind.WallpaperEnginePackage,
                package.PackagePath,
                Path.GetFileName(package.PackagePath),
                null,
                package.ProjectType,
                package.PreviewPath),
            WallpaperPackageType.Video when mode == WallpaperImportMode.MotionBackdrop =>
                package.MediaPath is not null
                    ? new(
                        true,
                        WallpaperResolvedKind.Motion,
                        package.MediaPath,
                        Path.GetFileName(package.MediaPath),
                        null,
                        package.ProjectType)
                    : WallpaperImportResolution.Reject(
                        "视频项目缺少可用于动态播放的媒体文件。"),
            WallpaperPackageType.Video when package.PreviewPath is not null => new(
                true,
                WallpaperResolvedKind.Still,
                package.PreviewPath,
                Path.GetFileName(package.PreviewPath),
                null,
                package.ProjectType),
            WallpaperPackageType.Video => package.MediaPath is not null
                ? new(
                    true,
                    WallpaperResolvedKind.Motion,
                    package.MediaPath,
                    Path.GetFileName(package.MediaPath),
                    null,
                    package.ProjectType)
                : WallpaperImportResolution.Reject(
                    "视频项目没有可用的媒体或预览资源。"),
            WallpaperPackageType.Web or WallpaperPackageType.Application =>
                package.PreviewPath is not null &&
                mode == WallpaperImportMode.HolographicCard
                    ? new(
                        true,
                        WallpaperResolvedKind.Still,
                        package.PreviewPath,
                        Path.GetFileName(package.PreviewPath),
                        null,
                        package.ProjectType)
                    : WallpaperImportResolution.Reject(
                        "Web 或应用壁纸需要专用运行时，不能直接进入 Nikkiward 动态背景。"),
            WallpaperPackageType.Unknown => new(
                true,
                WallpaperResolvedKind.WallpaperEnginePackage,
                package.PackagePath,
                Path.GetFileName(package.PackagePath),
                null,
                package.ProjectType,
                package.PreviewPath),
            _ => WallpaperImportResolution.Reject(
                "Wallpaper .pkg 缺少可识别的 project.json 或项目预览资源。"),
        };
    }

    private static WallpaperImportResolution ResolveProjectMetadata(
        string projectPath,
        WallpaperImportMode mode)
    {
        var metadata = WallpaperPackageReader.ReadProjectMetadata(projectPath);
        if (!metadata.IsUsable)
        {
            return WallpaperImportResolution.Reject(
                metadata.RejectReason ?? "project.json 无法读取。");
        }

        return metadata.ProjectType switch
        {
            WallpaperPackageType.Video when mode == WallpaperImportMode.MotionBackdrop =>
                metadata.MediaPath is not null
                    ? new(
                        true,
                        WallpaperResolvedKind.Motion,
                        metadata.MediaPath,
                        Path.GetFileName(metadata.MediaPath),
                        null,
                        metadata.ProjectType)
                    : WallpaperImportResolution.Reject(
                        "视频项目缺少可用于动态播放的媒体文件。"),
            WallpaperPackageType.Video when metadata.PreviewPath is not null => new(
                true,
                WallpaperResolvedKind.Still,
                metadata.PreviewPath,
                Path.GetFileName(metadata.PreviewPath),
                null,
                metadata.ProjectType),
            WallpaperPackageType.Scene or WallpaperPackageType.Web or
                WallpaperPackageType.Application when mode == WallpaperImportMode.HolographicCard &&
                metadata.PreviewPath is not null => new(
                    true,
                    WallpaperResolvedKind.Still,
                    metadata.PreviewPath,
                    Path.GetFileName(metadata.PreviewPath),
                    null,
                    metadata.ProjectType),
            WallpaperPackageType.Scene => WallpaperImportResolution.Reject(
                "场景壁纸需要 Wallpaper Engine 运行时才能播放；请选择光栅卡片模式。"),
            _ => WallpaperImportResolution.Reject(
                "此 Wallpaper 项目没有可由当前背景管线使用的资源。"),
        };
    }

    internal static bool TryResolveSafeSibling(
        string rootPath,
        string relativePath,
        out string? resolvedPath)
    {
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\0') ||
            relativePath.Contains(':'))
        {
            return false;
        }

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment is ".." or "." or ""))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidate) ||
            !IsReparseSafe(root, candidate))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    internal static bool IsReparseSafe(string rootPath, string candidatePath)
    {
        var root = new DirectoryInfo(rootPath);
        var directory = new DirectoryInfo(Path.GetDirectoryName(candidatePath)!);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        while (directory is not null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            if (string.Equals(directory.FullName, root.FullName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = directory.Parent;
        }

        var file = new FileInfo(candidatePath);
        return (file.Attributes & FileAttributes.ReparsePoint) == 0;
    }

    internal static bool IsValidPreview(string path) =>
        IsStillExtension(Path.GetExtension(path));

    internal static bool IsValidMedia(string path) =>
        MotionSourceRules.IsSupportedExtension(Path.GetExtension(path));

    internal static WallpaperPackageType ParseProjectType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "scene" => WallpaperPackageType.Scene,
            "video" => WallpaperPackageType.Video,
            "web" => WallpaperPackageType.Web,
            "application" => WallpaperPackageType.Application,
            _ => WallpaperPackageType.Unknown,
        };

    internal static string? ReadStringProperty(
        JsonElement root,
        string name)
    {
        return root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static bool IsStrictUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

public sealed class WallpaperPackageCache
{
    private const long MaximumPackageBytes = 512L * 1024 * 1024;
    private const string RuntimePackageFileName = "scene.pkg";
    private readonly string _rootPath;

    public WallpaperPackageCache(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        string fullSourcePath;
        try
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
            var sourceInfo = new FileInfo(fullSourcePath);
            if (!sourceInfo.Exists ||
                sourceInfo.Length <= 0 ||
                sourceInfo.Length > MaximumPackageBytes ||
                !string.Equals(
                    Path.GetExtension(fullSourcePath),
                    ".pkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return (new(false, "Wallpaper .pkg 文件不存在或超出支持范围。"), null);
            }

            Directory.CreateDirectory(_rootPath);
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return (new(false, "Wallpaper .pkg 文件不存在或无法读取。"), null);
        }

        var packageHash = await ComputeHashAsync(fullSourcePath, cancellationToken);
        var packageDirectory = Path.Combine(_rootPath, packageHash);
        var importedPath = Path.Combine(packageDirectory, RuntimePackageFileName);
        if (File.Exists(importedPath))
        {
            try
            {
                await CopyCompanionFilesAsync(
                    fullSourcePath,
                    packageDirectory,
                    cancellationToken);
                return (BackgroundSourceValidation.Accepted, importedPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                return (new(false, "Wallpaper .pkg 伴随文件正被其他程序占用，暂时无法读取。"), null);
            }
            catch (UnauthorizedAccessException)
            {
                return (new(false, "没有权限读取 Wallpaper .pkg 伴随文件。"), null);
            }
        }

        var temporaryDirectory = Path.Combine(
            _rootPath,
            $".{packageHash}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            await MotionImportFileCopier.CopyWithRetryAsync(
                fullSourcePath,
                Path.Combine(temporaryDirectory, RuntimePackageFileName),
                cancellationToken);
            await CopyCompanionFilesAsync(
                fullSourcePath,
                temporaryDirectory,
                cancellationToken);

            try
            {
                Directory.Move(temporaryDirectory, packageDirectory);
            }
            catch (IOException) when (File.Exists(importedPath))
            {
                return (BackgroundSourceValidation.Accepted, importedPath);
            }

            return (BackgroundSourceValidation.Accepted, importedPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return (new(false, "Wallpaper .pkg 文件正被其他程序占用，暂时无法读取。"), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (new(false, "没有权限读取所选 Wallpaper .pkg 文件。"), null);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(
            stream,
            cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task CopyCompanionFilesAsync(
        string sourcePackagePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.GetDirectoryName(sourcePackagePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return;
        }

        var sourceProject = Path.Combine(sourceDirectory, "project.json");
        if (File.Exists(sourceProject))
        {
            await CopyIfMissingAsync(
                sourceProject,
                Path.Combine(destinationDirectory, "project.json"),
                cancellationToken);
        }

        var descriptor = WallpaperPackageReader.Inspect(sourcePackagePath);
        if (descriptor.PreviewPath is null ||
            !File.Exists(descriptor.PreviewPath))
        {
            return;
        }

        var previewName = Path.GetFileName(descriptor.PreviewPath);
        if (string.IsNullOrWhiteSpace(previewName))
        {
            return;
        }

        await CopyIfMissingAsync(
            descriptor.PreviewPath,
            Path.Combine(destinationDirectory, previewName),
            cancellationToken);
    }

    private static async Task CopyIfMissingAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException("The companion destination directory cannot be resolved.");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}-{Guid.NewGuid():N}.tmp");
        try
        {
            await MotionImportFileCopier.CopyWithRetryAsync(
                sourcePath,
                temporaryPath,
                cancellationToken);
            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public static class WallpaperPackageReader
{
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    public const uint MaximumEntryCount = 4096;
    public const uint MaximumEntryNameBytes = 4096;

    public static WallpaperPackageDescriptor Inspect(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        var rootPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        if (!File.Exists(fullPath))
        {
            return Invalid(fullPath, rootPath, "Wallpaper .pkg 文件不存在。");
        }

        try
        {
            var length = new FileInfo(fullPath).Length;
            if (length <= 0 || length > MaximumPackageBytes)
            {
                return Invalid(fullPath, rootPath, "Wallpaper .pkg 文件大小超出支持范围。");
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            Span<byte> fixedHeader = stackalloc byte[16];
            if (!ReadExactly(stream, fixedHeader) ||
                BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader[..4]) != 8)
            {
                return Invalid(fullPath, rootPath, "Wallpaper .pkg 头部无效。");
            }

            var version = Encoding.ASCII.GetString(fixedHeader.Slice(4, 8));
            if (!TryParseVersion(version))
            {
                return Invalid(fullPath, rootPath, "Wallpaper .pkg 版本不受支持。");
            }

            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(12, 4));
            if (entryCount == 0 || entryCount > MaximumEntryCount)
            {
                return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目数量无效。");
            }

            var entries = new List<WallpaperPackageEntry>((int)entryCount);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < entryCount; index++)
            {
                if (!TryReadUInt32(stream, out var nameLength) ||
                    nameLength == 0 ||
                    nameLength > MaximumEntryNameBytes)
                {
                    return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目名称长度无效。");
                }

                var nameBytes = new byte[(int)nameLength];
                if (!ReadExactly(stream, nameBytes) ||
                    !WallpaperSourceRules.IsStrictUtf8(nameBytes))
                {
                    return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目名称不是有效 UTF-8。");
                }

                var name = Encoding.UTF8.GetString(nameBytes);
                if (!IsSafeEntryName(name) || !names.Add(name))
                {
                    return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目路径无效或重复。");
                }

                if (!TryReadUInt32(stream, out var offset) ||
                    !TryReadUInt32(stream, out var entryLength))
                {
                    return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目索引不完整。");
                }

                entries.Add(new(name, offset, entryLength));
            }

            var dataStart = stream.Position;
            foreach (var entry in entries)
            {
                var end = (ulong)dataStart + entry.Offset + entry.Length;
                if (end > (ulong)length)
                {
                    return Invalid(fullPath, rootPath, "Wallpaper .pkg 条目边界超出文件范围。");
                }
            }

            var project = ReadProjectMetadataFromRoot(rootPath);
            return new(
                fullPath,
                rootPath,
                version,
                entries,
                project.ProjectType,
                project.PreviewPath,
                project.MediaPath,
                null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Invalid(fullPath, rootPath, "Wallpaper .pkg 文件无法读取。");
        }
    }

    internal static ProjectMetadata ReadProjectMetadata(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var rootPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        return ReadProjectMetadataFromRoot(rootPath);
    }

    private static ProjectMetadata ReadProjectMetadataFromRoot(string rootPath)
    {
        var projectPath = Path.Combine(rootPath, "project.json");
        if (!File.Exists(projectPath) || !WallpaperSourceRules.IsReparseSafe(rootPath, projectPath))
        {
            return ProjectMetadata.Invalid("项目目录缺少 project.json。");
        }

        try
        {
            if (new FileInfo(projectPath).Length > 2 * 1024 * 1024)
            {
                return ProjectMetadata.Invalid("project.json 超出支持大小。");
            }

            using var stream = new FileStream(
                projectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                32 * 1024,
                FileOptions.SequentialScan);
            using var document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            var root = document.RootElement;
            var type = WallpaperSourceRules.ParseProjectType(
                WallpaperSourceRules.ReadStringProperty(root, "type"));
            if (type == WallpaperPackageType.Unknown)
            {
                return ProjectMetadata.Invalid("project.json 的壁纸类型无法识别。");
            }

            var preview = ResolveCompanion(
                rootPath,
                WallpaperSourceRules.ReadStringProperty(root, "preview"),
                WallpaperSourceRules.IsValidPreview);
            var media = ResolveCompanion(
                rootPath,
                WallpaperSourceRules.ReadStringProperty(root, "file"),
                WallpaperSourceRules.IsValidMedia);
            return new(type, preview, media, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ProjectMetadata.Invalid("project.json 格式无效或无法读取。");
        }
    }

    private static string? ResolveCompanion(
        string rootPath,
        string? relativePath,
        Func<string, bool> extensionRule)
    {
        if (relativePath is null ||
            !WallpaperSourceRules.TryResolveSafeSibling(
                rootPath,
                relativePath,
                out var candidate) ||
            candidate is null ||
            !extensionRule(candidate))
        {
            return null;
        }

        return candidate;
    }

    private static WallpaperPackageDescriptor Invalid(
        string packagePath,
        string rootPath,
        string reason) =>
        new(
            packagePath,
            rootPath,
            string.Empty,
            Array.Empty<WallpaperPackageEntry>(),
            WallpaperPackageType.Unknown,
            null,
            null,
            reason);

    private static bool TryParseVersion(string version)
    {
        if (!version.StartsWith("PKGV", StringComparison.Ordinal) ||
            version.Length != 8 ||
            !int.TryParse(version.AsSpan(4), out var number))
        {
            return false;
        }

        return number is >= 1 and <= 24;
    }

    private static bool IsSafeEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('\0') ||
            name.Contains(':') ||
            name.StartsWith('/') ||
            name.StartsWith('\\'))
        {
            return false;
        }

        var segments = name.Replace('\\', '/').Split('/');
        return segments.All(segment => segment is not "" and not "." and not "..");
    }

    private static bool TryReadUInt32(Stream stream, out uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!ReadExactly(stream, buffer))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return true;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var count = stream.Read(buffer);
            if (count <= 0)
            {
                return false;
            }

            buffer = buffer[count..];
        }

        return true;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer) =>
        ReadExactly(stream, buffer.AsSpan());

    internal sealed record ProjectMetadata(
        WallpaperPackageType ProjectType,
        string? PreviewPath,
        string? MediaPath,
        string? RejectReason)
    {
        public bool IsUsable => RejectReason is null;

        public static ProjectMetadata Invalid(string reason) =>
            new(WallpaperPackageType.Unknown, null, null, reason);
    }
}
