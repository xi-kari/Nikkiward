using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Nikkiward.Features.Gallery;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Nuan5CBytes
{
    public Nuan5CBytes(IntPtr data, nuint length, nuint capacity)
    {
        Data = data;
        Length = length;
        Capacity = capacity;
    }

    public readonly IntPtr Data;

    public readonly nuint Length;

    public readonly nuint Capacity;

    public bool IsDefault => Data == IntPtr.Zero && Length == 0 && Capacity == 0;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal readonly struct Nuan5MediaResult
{
    public Nuan5MediaResult(uint status, Nuan5CBytes bytes)
    {
        Status = status;
        Bytes = bytes;
    }

    [FieldOffset(0)]
    public readonly uint Status;

    [FieldOffset(8)]
    public readonly Nuan5CBytes Bytes;
}

internal interface INuan5GalleryNativeAdapter
{
    uint GetAbiVersion();

    IntPtr CreateMediaKey(string value);

    IntPtr CreateCameraParameterKey();

    Nuan5MediaResult DecodeFileBytesUnchecked(
        byte[] flag,
        byte[] fileBytes,
        IntPtr key);

    Nuan5MediaResult Decrypt(byte[] data, IntPtr key);

    byte[] ReadBytes(Nuan5CBytes bytes);

    void FreeMediaKey(IntPtr key);

    void FreeBytes(Nuan5CBytes bytes);
}

public sealed class Nuan5GalleryMetadataService
{
    public const uint ExpectedAbiVersion = 1;
    public const long MaximumFileSize = 10L * 1024 * 1024;

    private static readonly SemaphoreSlim NativeCallGate = new(1, 1);
    private static readonly byte[] JpegEndFlag = [0xff, 0xd9];
    private readonly INuan5GalleryNativeAdapter _native;

    static Nuan5GalleryMetadataService()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(Nuan5GalleryMetadataService).Assembly,
            ResolveNativeLibrary);
    }

    public Nuan5GalleryMetadataService()
        : this(new PInvokeNuan5GalleryNativeAdapter())
    {
    }

    internal Nuan5GalleryMetadataService(INuan5GalleryNativeAdapter native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public async Task<GalleryPhotoMetadata> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var userId = ExtractUserId(filePath);
        if (userId is null)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.UserIdUnavailable);
        }

        byte[] fileBytes;
        try
        {
            var file = new FileInfo(filePath);
            if (!file.Exists)
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.FileUnavailable,
                    userId);
            }

            if (file.Length > MaximumFileSize)
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.FileTooLarge,
                    userId);
            }

            fileBytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken)
                .ConfigureAwait(false);
            if (fileBytes.LongLength > MaximumFileSize)
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.FileTooLarge,
                    userId);
            }
        }
        catch (OperationCanceledException)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.Cancelled,
                userId);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.FileUnavailable,
                userId);
        }

        try
        {
            await NativeCallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.Cancelled,
                userId);
        }

        try
        {
            return ReadWithNativeAdapter(fileBytes, userId);
        }
        catch (Exception ex) when (IsNativeLibraryUnavailable(ex))
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.NativeLibraryUnavailable,
                userId);
        }
        catch (Exception)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.DecryptionFailed,
                userId);
        }
        finally
        {
            NativeCallGate.Release();
        }
    }

    internal static string? ExtractUserId(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        foreach (var segment in filePath.Split(
                     ['\\', '/'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length is >= 6 and <= 12 && segment.All(char.IsAsciiDigit))
            {
                return segment;
            }
        }

        return null;
    }

    private GalleryPhotoMetadata ReadWithNativeAdapter(byte[] fileBytes, string userId)
    {
        if (_native.GetAbiVersion() != ExpectedAbiVersion)
        {
            return GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.AbiVersionMismatch,
                userId);
        }

        var userKey = IntPtr.Zero;
        var cameraKey = IntPtr.Zero;
        try
        {
            userKey = _native.CreateMediaKey(userId);
            if (userKey == IntPtr.Zero)
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.NativeKeyUnavailable,
                    userId);
            }

            cameraKey = _native.CreateCameraParameterKey();
            if (cameraKey == IntPtr.Zero)
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.NativeKeyUnavailable,
                    userId);
            }

            var firstStep = _native.DecodeFileBytesUnchecked(JpegEndFlag, fileBytes, userKey);
            byte[] firstPayload;
            try
            {
                if (firstStep.Status != 0)
                {
                    return GalleryPhotoMetadata.NoParameters(
                        GalleryPhotoMetadataStatus.DecryptionFailed,
                        userId);
                }

                firstPayload = _native.ReadBytes(firstStep.Bytes);
            }
            finally
            {
                FreeBytes(firstStep.Bytes);
            }

            if (!TryReadSocialPhoto(firstPayload, out var socialPhoto))
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.InvalidPayload,
                    userId);
            }

            if (string.IsNullOrWhiteSpace(socialPhoto.CameraParameters))
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.CameraParametersUnavailable,
                    userId);
            }

            var encryptedCameraParameters = Encoding.UTF8.GetBytes(socialPhoto.CameraParameters);
            var secondStep = _native.Decrypt(encryptedCameraParameters, cameraKey);
            byte[] cameraPayload;
            try
            {
                if (secondStep.Status != 0)
                {
                    return GalleryPhotoMetadata.NoParameters(
                        GalleryPhotoMetadataStatus.DecryptionFailed,
                        userId);
                }

                cameraPayload = _native.ReadBytes(secondStep.Bytes);
            }
            finally
            {
                FreeBytes(secondStep.Bytes);
            }

            if (!TryReadCameraParameters(cameraPayload, out var camera))
            {
                return GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.InvalidPayload,
                    userId);
            }

            return new GalleryPhotoMetadata
            {
                Availability = GalleryPhotoMetadataAvailability.Available,
                Status = GalleryPhotoMetadataStatus.Available,
                UserId = userId,
                Camera = camera,
                PoseId = socialPhoto.PoseId,
                FramedMoment = socialPhoto.FramedMoment,
                ClothingIds = socialPhoto.ClothingIds,
                Location = socialPhoto.Location,
                Tasks = socialPhoto.Tasks,
            };
        }
        finally
        {
            if (userKey != IntPtr.Zero && userKey != cameraKey)
            {
                FreeKey(userKey);
            }

            if (cameraKey != IntPtr.Zero)
            {
                FreeKey(cameraKey);
            }
        }
    }

    private void FreeBytes(Nuan5CBytes bytes)
    {
        if (bytes.IsDefault)
        {
            return;
        }

        try
        {
            _native.FreeBytes(bytes);
        }
        catch (Exception)
        {
        }
    }

    private void FreeKey(IntPtr key)
    {
        try
        {
            _native.FreeMediaKey(key);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryReadSocialPhoto(
        byte[] payload,
        out SocialPhotoSnapshot snapshot)
    {
        snapshot = SocialPhotoSnapshot.Empty;
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            if (!TryGetObject(root, out var socialPhoto, "SocialPhoto", "social_photo", "socialPhoto"))
            {
                socialPhoto = root;
            }

            TryGetString(
                socialPhoto,
                out var cameraParameters,
                "CameraParams",
                "camera_params",
                "cameraParams");
            if (string.IsNullOrWhiteSpace(cameraParameters))
            {
                TryGetString(
                    root,
                    out cameraParameters,
                    "CameraParams",
                    "camera_params",
                    "cameraParams");
            }

            var photoInfo = default(JsonElement);
            var hasPhotoInfo = TryGetObject(
                socialPhoto,
                out photoInfo,
                "photo_info",
                "PhotoInfo",
                "photoInfo");

            long? poseId = null;
            long? framedMoment = null;
            IReadOnlyList<long> clothingIds = [];
            GalleryPhotoLocation? location = null;
            if (hasPhotoInfo)
            {
                poseId = TryGetInt64(photoInfo, "pose_id", "PoseId", "poseId");
                framedMoment = TryGetInt64(
                    photoInfo,
                    "framed_moment",
                    "FramedMoment",
                    "framedMoment");
                clothingIds = ReadClothingIds(photoInfo);
                location = ReadLocation(photoInfo);
            }

            snapshot = new SocialPhotoSnapshot(
                cameraParameters,
                poseId,
                framedMoment,
                clothingIds,
                location,
                ReadTasks(root, socialPhoto, hasPhotoInfo ? photoInfo : null));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadCameraParameters(
        byte[] payload,
        out GalleryPhotoCameraParameters camera)
    {
        camera = new GalleryPhotoCameraParameters();
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            JsonElement values;
            if (root.ValueKind == JsonValueKind.Array)
            {
                values = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     TryGetProperty(root, out values, "camera", "Camera"))
            {
            }
            else
            {
                return false;
            }

            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < 31)
            {
                return false;
            }

            camera = new GalleryPhotoCameraParameters
            {
                FocalLength = ReadDouble(values[14]),
                Aperture = ReadDouble(values[15]),
                PortraitMode = ReadDouble(values[1]) == 1,
                Light = ReadAdjustment(values, 17, 18),
                Vignette = ReadDouble(values[19]),
                Bloom = ReadDouble(values[20]),
                BloomThreshold = ReadDouble(values[21]),
                Brightness = ReadDouble(values[22]),
                Exposure = ReadDouble(values[23]),
                Contrast = ReadDouble(values[24]),
                Saturation = ReadDouble(values[25]),
                Vibrance = ReadDouble(values[26]),
                Highlights = ReadDouble(values[27]),
                Shadows = ReadDouble(values[28]),
                Filter = ReadAdjustment(values, 29, 30),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GalleryPhotoAdjustment? ReadAdjustment(
        JsonElement values,
        int idIndex,
        int strengthIndex)
    {
        var idElement = values[idIndex];
        var id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(id) || string.Equals(id, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : new GalleryPhotoAdjustment(id, ReadDouble(values[strengthIndex]));
    }

    private static IReadOnlyList<long> ReadClothingIds(JsonElement photoInfo)
    {
        if (!TryGetProperty(
                photoInfo,
                out var clothes,
                "nikki_clothes",
                "NikkiClothes",
                "nikkiClothes") ||
            clothes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<long>();
        foreach (var item in clothes.EnumerateArray())
        {
            long? id = item.ValueKind == JsonValueKind.Object
                ? TryGetInt64(item, "id", "Id", "ID")
                : ReadInt64(item);
            if (id is > 0)
            {
                ids.Add(id.Value);
            }
        }

        return ids;
    }

    private static GalleryPhotoLocation? ReadLocation(JsonElement photoInfo)
    {
        var x = TryGetDouble(photoInfo, "nikki_loc_x", "NikkiLocX", "nikkiLocX");
        var y = TryGetDouble(photoInfo, "nikki_loc_y", "NikkiLocY", "nikkiLocY");
        var z = TryGetDouble(photoInfo, "nikki_loc_z", "NikkiLocZ", "nikkiLocZ");
        return x.HasValue && y.HasValue && z.HasValue
            ? new GalleryPhotoLocation(x.Value, y.Value, z.Value)
            : null;
    }

    private static IReadOnlyList<GalleryPhotoTask> ReadTasks(
        JsonElement root,
        JsonElement socialPhoto,
        JsonElement? photoInfo)
    {
        var tasks = new List<GalleryPhotoTask>();
        ReadNamedTask(root, tasks, "puzzle", "puzzle_game_plugin", "PuzzleGamePlugin", "puzzleGamePlugin");
        ReadNamedTask(root, tasks, "risk", "risk_photo", "RiskPhoto", "riskPhoto");
        ReadNamedTask(
            root,
            tasks,
            "interactive",
            "interactive_photo",
            "InteractivePhoto",
            "interactivePhoto");
        ReadTaskValue(root, tasks, "task", "tasks");
        ReadTaskValue(socialPhoto, tasks, "task", "tasks");
        if (photoInfo.HasValue)
        {
            ReadTaskValue(photoInfo.Value, tasks, "task", "tasks", "task_id", "TaskId", "taskId");
        }

        return tasks
            .Distinct()
            .ToArray();
    }

    private static void ReadNamedTask(
        JsonElement owner,
        List<GalleryPhotoTask> tasks,
        string kind,
        params string[] propertyNames)
    {
        if (!TryGetProperty(owner, out var value, propertyNames) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var id = value.ValueKind == JsonValueKind.Object
            ? TryGetInt64(value, "tag", "Tag", "id", "Id", "ID")
            : ReadInt64(value);
        tasks.Add(new GalleryPhotoTask(kind, id));
    }

    private static void ReadTaskValue(
        JsonElement owner,
        List<GalleryPhotoTask> tasks,
        params string[] propertyNames)
    {
        if (!TryGetProperty(owner, out var value, propertyNames))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                AddTaskValue(item, tasks);
            }
        }
        else
        {
            AddTaskValue(value, tasks);
        }
    }

    private static void AddTaskValue(JsonElement value, List<GalleryPhotoTask> tasks)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var kind = TryGetString(value, out var text, "type", "Type", "kind", "Kind")
                ? text
                : "task";
            tasks.Add(new GalleryPhotoTask(
                string.IsNullOrWhiteSpace(kind) ? "task" : kind,
                TryGetInt64(value, "tag", "Tag", "id", "Id", "ID", "task_id", "TaskId")));
            return;
        }

        var id = ReadInt64(value);
        if (id.HasValue)
        {
            tasks.Add(new GalleryPhotoTask("task", id));
        }
    }

    private static bool TryGetObject(
        JsonElement owner,
        out JsonElement value,
        params string[] propertyNames)
    {
        if (TryGetProperty(owner, out value, propertyNames) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetProperty(
        JsonElement owner,
        out JsonElement value,
        params string[] propertyNames)
    {
        if (owner.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (owner.TryGetProperty(propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetString(
        JsonElement owner,
        out string? value,
        params string[] propertyNames)
    {
        if (TryGetProperty(owner, out var property, propertyNames) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static long? TryGetInt64(JsonElement owner, params string[] propertyNames) =>
        TryGetProperty(owner, out var property, propertyNames) ? ReadInt64(property) : null;

    private static long? ReadInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static double? TryGetDouble(JsonElement owner, params string[] propertyNames) =>
        TryGetProperty(owner, out var property, propertyNames) ? ReadDouble(property) : null;

    private static double ReadDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(
                   value.GetString(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out number)
            ? number
            : 0;
    }

    private static bool IsNativeLibraryUnavailable(Exception exception) =>
        exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException ||
        exception.InnerException is not null && IsNativeLibraryUnavailable(exception.InnerException);

    private static IntPtr ResolveNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "nuan5_decryption.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        var architectureDirectory = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => string.Empty,
        };
        var candidates = string.IsNullOrWhiteSpace(architectureDirectory)
            ? new[] { Path.Combine(AppContext.BaseDirectory, libraryName) }
            :
            new[]
            {
                Path.Combine(AppContext.BaseDirectory, "runtimes", architectureDirectory, "native", libraryName),
                Path.Combine(AppContext.BaseDirectory, libraryName),
            };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return NativeLibrary.Load(candidate);
            }
            catch (DllNotFoundException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        return IntPtr.Zero;
    }

    private sealed record SocialPhotoSnapshot(
        string? CameraParameters,
        long? PoseId,
        long? FramedMoment,
        IReadOnlyList<long> ClothingIds,
        GalleryPhotoLocation? Location,
        IReadOnlyList<GalleryPhotoTask> Tasks)
    {
        public static SocialPhotoSnapshot Empty { get; } = new(null, null, null, [], null, []);
    }
}

internal sealed class PInvokeNuan5GalleryNativeAdapter : INuan5GalleryNativeAdapter
{
    public uint GetAbiVersion() => NativeMethods.AbiVersion();

    public IntPtr CreateMediaKey(string value) => NativeMethods.MediaKeyFromString(value);

    public IntPtr CreateCameraParameterKey() => NativeMethods.MediaKeyCameraParameter();

    public Nuan5MediaResult DecodeFileBytesUnchecked(
        byte[] flag,
        byte[] fileBytes,
        IntPtr key) =>
        NativeMethods.MediaDecodeFileBytesUnchecked(
            flag,
            checked((nuint)flag.LongLength),
            fileBytes,
            checked((nuint)fileBytes.LongLength),
            key);

    public Nuan5MediaResult Decrypt(byte[] data, IntPtr key) =>
        NativeMethods.MediaDecrypt(data, checked((nuint)data.LongLength), key);

    public byte[] ReadBytes(Nuan5CBytes bytes)
    {
        if (bytes.Length == 0)
        {
            return [];
        }

        if (bytes.Data == IntPtr.Zero || bytes.Length > Nuan5GalleryMetadataService.MaximumFileSize)
        {
            throw new InvalidDataException("Native metadata payload has an invalid length.");
        }

        var result = new byte[checked((int)bytes.Length)];
        Marshal.Copy(bytes.Data, result, 0, result.Length);
        return result;
    }

    public void FreeMediaKey(IntPtr key) => NativeMethods.FreeMediaKey(key);

    public void FreeBytes(Nuan5CBytes bytes) => NativeMethods.FreeCBytes(bytes);

    private static class NativeMethods
    {
        private const string LibraryName = "nuan5_decryption.dll";

        [DllImport(LibraryName, EntryPoint = "abi_version", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

        [DllImport(LibraryName, EntryPoint = "media_key_from_str", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr MediaKeyFromString(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(LibraryName, EntryPoint = "media_key_camera_param", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr MediaKeyCameraParameter();

        [DllImport(LibraryName, EntryPoint = "media_decode_file_bytes_unchecked", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Nuan5MediaResult MediaDecodeFileBytesUnchecked(
            [In] byte[] flag,
            nuint flagLength,
            [In] byte[] fileBytes,
            nuint fileLength,
            IntPtr key);

        [DllImport(LibraryName, EntryPoint = "media_decrypt", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Nuan5MediaResult MediaDecrypt(
            [In] byte[] data,
            nuint dataLength,
            IntPtr key);

        [DllImport(LibraryName, EntryPoint = "free_media_key", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeMediaKey(IntPtr key);

        [DllImport(LibraryName, EntryPoint = "free_c_bytes", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeCBytes(Nuan5CBytes bytes);
    }
}
