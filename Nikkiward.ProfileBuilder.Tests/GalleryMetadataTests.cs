using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Nikkiward.Features.Gallery;

internal static class GalleryMetadataTests
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("gallery native ABI layout and UID extraction are exact", TestLayoutAndUserId),
        ("gallery metadata ABI mismatch fails closed", TestAbiMismatch),
        ("gallery metadata enforces the inclusive 10 MB boundary", TestFileSizeBoundary),
        ("gallery native calls are process-wide serial", TestProcessWideSerialization),
        ("gallery metadata parses audited fields and releases native resources", TestSuccessfulParseAndRelease),
        ("gallery metadata frees returned bytes on native failure", TestFailureRelease),
        ("gallery metadata frees returned camera bytes on native failure", TestCameraFailureRelease),
        ("gallery metadata avoids double-free for aliased keys", TestAliasedKeyRelease),
        ("gallery metadata missing inputs stay calm", TestCalmMissingInputs),
        ("gallery metadata handles missing native DLL calmly", TestMissingLibrary),
    ];

    private static Task TestLayoutAndUserId()
    {
        AssertEqual(24, Marshal.SizeOf<Nuan5CBytes>(), "CBytes x64 size");
        AssertEqual(0, Marshal.OffsetOf<Nuan5CBytes>(nameof(Nuan5CBytes.Data)).ToInt32(), "CBytes data offset");
        AssertEqual(8, Marshal.OffsetOf<Nuan5CBytes>(nameof(Nuan5CBytes.Length)).ToInt32(), "CBytes length offset");
        AssertEqual(16, Marshal.OffsetOf<Nuan5CBytes>(nameof(Nuan5CBytes.Capacity)).ToInt32(), "CBytes capacity offset");
        AssertEqual(32, Marshal.SizeOf<Nuan5MediaResult>(), "result x64 size");
        AssertEqual(0, Marshal.OffsetOf<Nuan5MediaResult>(nameof(Nuan5MediaResult.Status)).ToInt32(), "result status offset");
        AssertEqual(8, Marshal.OffsetOf<Nuan5MediaResult>(nameof(Nuan5MediaResult.Bytes)).ToInt32(), "result bytes offset");

        AssertEqual(
            "001234567890",
            Nuan5GalleryMetadataService.ExtractUserId(@"D:\Photos\001234567890\photo.jpg"),
            "12-digit UID segment");
        AssertEqual(
            "123456",
            Nuan5GalleryMetadataService.ExtractUserId("D:/Photos/123456/photo.jpg"),
            "6-digit UID segment");
        Assert(
            Nuan5GalleryMetadataService.ExtractUserId(@"D:\Photos\x123456y\photo.jpg") is null,
            "UID must occupy a complete path segment");
        Assert(
            Nuan5GalleryMetadataService.ExtractUserId(@"D:\Photos\12345\photo.jpg") is null,
            "short numeric segment must not be accepted");
        return Task.CompletedTask;
    }

    private static async Task TestAbiMismatch()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(32);
        var native = FakeNativeAdapter.ForSuccess();
        native.AbiVersion = 2;

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        AssertEqual(GalleryPhotoMetadataAvailability.NoParameters, result.Availability, "availability");
        AssertEqual(GalleryPhotoMetadataStatus.AbiVersionMismatch, result.Status, "status");
        AssertEqual(GalleryPhotoMetadata.NoParametersDisplayText, result.DisplayStatus, "calm UI text");
        AssertEqual(0, native.CreatedKeyCount, "mismatched ABI must not create keys");
        AssertEqual(0, native.DecodeCallCount, "mismatched ABI must not decode");
    }

    private static async Task TestFileSizeBoundary()
    {
        using var accepted = GalleryMetadataFixture.CreateFile(
            checked((int)Nuan5GalleryMetadataService.MaximumFileSize));
        var acceptedNative = FakeNativeAdapter.ForSuccess();
        var acceptedResult = await new Nuan5GalleryMetadataService(acceptedNative)
            .ReadAsync(accepted.FilePath);

        Assert(acceptedResult.HasParameters, "a file exactly at 10 MB must be accepted");
        AssertEqual(1, acceptedNative.DecodeCallCount, "accepted boundary decode count");

        using var rejected = GalleryMetadataFixture.CreateFile(
            checked((int)Nuan5GalleryMetadataService.MaximumFileSize + 1));
        var rejectedNative = FakeNativeAdapter.ForSuccess();
        var rejectedResult = await new Nuan5GalleryMetadataService(rejectedNative)
            .ReadAsync(rejected.FilePath);

        AssertEqual(GalleryPhotoMetadataStatus.FileTooLarge, rejectedResult.Status, "over-limit status");
        AssertEqual(0, rejectedNative.AbiCallCount, "over-limit file must be rejected before native loading");
    }

    private static async Task TestProcessWideSerialization()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(64);
        var native = FakeNativeAdapter.ForSuccess();
        native.DecodeDelay = TimeSpan.FromMilliseconds(35);
        var firstService = new Nuan5GalleryMetadataService(native);
        var secondService = new Nuan5GalleryMetadataService(native);

        var results = await Task.WhenAll(
            firstService.ReadAsync(fixture.FilePath),
            secondService.ReadAsync(fixture.FilePath),
            firstService.ReadAsync(fixture.FilePath),
            secondService.ReadAsync(fixture.FilePath));

        Assert(results.All(result => result.HasParameters), "all serialized reads should succeed");
        AssertEqual(4, native.DecodeCallCount, "decode count");
        AssertEqual(1, native.MaximumConcurrentNativeCalls, "maximum native concurrency");
    }

    private static async Task TestSuccessfulParseAndRelease()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(128);
        var native = FakeNativeAdapter.ForSuccess();

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        Assert(result.HasParameters, "metadata should be available");
        AssertEqual("123456789", result.UserId, "UID");
        AssertEqual(7001L, result.PoseId, "pose id");
        AssertEqual(19L, result.FramedMoment, "framed moment");
        Assert(result.ClothingIds.SequenceEqual([100020003L, 200030004L]), "clothing IDs");
        AssertEqual(1.25, result.Location?.X, "location X");
        AssertEqual(-2.5, result.Location?.Y, "location Y");
        AssertEqual(9.75, result.Location?.Z, "location Z");
        Assert(result.Tasks.Contains(new GalleryPhotoTask("puzzle", 88)), "puzzle task");
        Assert(result.Tasks.Contains(new GalleryPhotoTask("story", 93)), "structured task");

        var camera = result.Camera ?? throw new InvalidOperationException("camera missing");
        AssertEqual(50.5, camera.FocalLength, "focal length index 14");
        AssertEqual(2.8, camera.Aperture, "aperture index 15");
        Assert(camera.PortraitMode, "portrait index 1");
        AssertEqual("light-4", camera.Light?.Id, "light id index 17");
        AssertEqual(0.4, camera.Light?.Strength, "light strength index 18");
        AssertEqual(0.19, camera.Vignette, "vignette index 19");
        AssertEqual(0.2, camera.Bloom, "bloom index 20");
        AssertEqual(0.21, camera.BloomThreshold, "bloom threshold index 21");
        AssertEqual(0.22, camera.Brightness, "brightness index 22");
        AssertEqual(0.23, camera.Exposure, "exposure index 23");
        AssertEqual(0.24, camera.Contrast, "contrast index 24");
        AssertEqual(0.25, camera.Saturation, "saturation index 25");
        AssertEqual(0.26, camera.Vibrance, "vibrance index 26");
        AssertEqual(0.27, camera.Highlights, "highlights index 27");
        AssertEqual(0.28, camera.Shadows, "shadows index 28");
        AssertEqual("filter-7", camera.Filter?.Id, "filter id index 29");
        AssertEqual(0.3, camera.Filter?.Strength, "filter strength index 30");

        AssertEqual(2, native.FreedBytes.Count, "both successful CBytes results must be freed");
        Assert(native.FreedBytes.All(pair => pair.Value == 1), "each CBytes result must be freed once");
        AssertEqual(2, native.FreedKeys.Count, "both keys must be freed");
        Assert(native.FreedKeys.All(pair => pair.Value == 1), "each key must be freed once");
        Assert(!result.GetType().GetProperties().Any(property =>
            property.Name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)),
            "UI model must not expose raw JSON or keys");
    }

    private static async Task TestFailureRelease()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(32);
        var native = FakeNativeAdapter.ForSuccess();
        native.DecodeStatus = 5;

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        AssertEqual(GalleryPhotoMetadataStatus.DecryptionFailed, result.Status, "failure status");
        AssertEqual(1, native.FreedBytes.Count, "failed result CBytes must be freed");
        AssertEqual(1, native.FreedBytes.Single().Value, "failed result free count");
        AssertEqual(2, native.FreedKeys.Count, "failure must free both keys");
        AssertEqual(0, native.DecryptCallCount, "step three must not run after step one failure");
    }

    private static async Task TestAliasedKeyRelease()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(32);
        var native = FakeNativeAdapter.ForSuccess();
        native.AliasKeys = true;

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        Assert(result.HasParameters, "aliased-key result");
        AssertEqual(1, native.FreedKeys.Count, "aliased key identity count");
        AssertEqual(1, native.FreedKeys.Single().Value, "aliased key must be freed once");
    }

    private static async Task TestCameraFailureRelease()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(32);
        var native = FakeNativeAdapter.ForSuccess();
        native.DecryptStatus = 3;

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        AssertEqual(GalleryPhotoMetadataStatus.DecryptionFailed, result.Status, "camera failure status");
        AssertEqual(2, native.FreedBytes.Count, "both native results must be released");
        Assert(native.FreedBytes.All(pair => pair.Value == 1), "each native result must be released once");
        AssertEqual(2, native.FreedKeys.Count, "camera failure must free both keys");
        AssertEqual(1, native.DecryptCallCount, "camera decrypt call count");
    }

    private static async Task TestCalmMissingInputs()
    {
        using var missingUserId = GalleryMetadataFixture.CreateFile(16, "gallery");
        var missingUserIdNative = FakeNativeAdapter.ForSuccess();
        var missingUserIdResult = await new Nuan5GalleryMetadataService(missingUserIdNative)
            .ReadAsync(missingUserId.FilePath);
        AssertEqual(GalleryPhotoMetadataStatus.UserIdUnavailable, missingUserIdResult.Status, "missing UID status");
        AssertEqual(0, missingUserIdNative.AbiCallCount, "missing UID must not load native code");

        using var missingCamera = GalleryMetadataFixture.CreateFile(16);
        var missingCameraNative = FakeNativeAdapter.ForSuccess();
        missingCameraNative.FirstPayload = Encoding.UTF8.GetBytes(
            """{"social_photo":{"photo_info":{}}}""");
        var missingCameraResult = await new Nuan5GalleryMetadataService(missingCameraNative)
            .ReadAsync(missingCamera.FilePath);
        AssertEqual(
            GalleryPhotoMetadataStatus.CameraParametersUnavailable,
            missingCameraResult.Status,
            "missing CameraParams status");
        AssertEqual(GalleryPhotoMetadata.NoParametersDisplayText, missingCameraResult.DisplayStatus, "missing CameraParams UI text");
        AssertEqual(1, missingCameraNative.FreedBytes.Count, "step-one bytes release without CameraParams");

        using var invalidPayload = GalleryMetadataFixture.CreateFile(16);
        var invalidPayloadNative = FakeNativeAdapter.ForSuccess();
        invalidPayloadNative.FirstPayload = Encoding.UTF8.GetBytes("not-json");
        var invalidPayloadResult = await new Nuan5GalleryMetadataService(invalidPayloadNative)
            .ReadAsync(invalidPayload.FilePath);
        AssertEqual(GalleryPhotoMetadataStatus.InvalidPayload, invalidPayloadResult.Status, "invalid JSON status");
        AssertEqual(GalleryPhotoMetadata.NoParametersDisplayText, invalidPayloadResult.DisplayStatus, "invalid JSON UI text");
        AssertEqual(1, invalidPayloadNative.FreedBytes.Count, "invalid JSON bytes release");
    }

    private static async Task TestMissingLibrary()
    {
        using var fixture = GalleryMetadataFixture.CreateFile(32);
        var native = FakeNativeAdapter.ForSuccess();
        native.AbiException = new DllNotFoundException("fixture");

        var result = await new Nuan5GalleryMetadataService(native).ReadAsync(fixture.FilePath);

        AssertEqual(GalleryPhotoMetadataStatus.NativeLibraryUnavailable, result.Status, "missing library status");
        AssertEqual(GalleryPhotoMetadata.NoParametersDisplayText, result.DisplayStatus, "missing library UI text");
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
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private sealed class GalleryMetadataFixture : IDisposable
    {
        private GalleryMetadataFixture(string root, string filePath)
        {
            Root = root;
            FilePath = filePath;
        }

        public string Root { get; }

        public string FilePath { get; }

        public static GalleryMetadataFixture CreateFile(int length, string userFolderName = "123456789")
        {
            var root = Path.Combine(Path.GetTempPath(), $"nikkiward-gallery-{Guid.NewGuid():N}");
            var userFolder = Path.Combine(root, userFolderName);
            Directory.CreateDirectory(userFolder);
            var filePath = Path.Combine(userFolder, "photo.jpeg");
            using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(length);
            }

            return new GalleryMetadataFixture(root, filePath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FakeNativeAdapter : INuan5GalleryNativeAdapter
    {
        private readonly ConcurrentDictionary<IntPtr, byte[]> _payloads = new();
        private long _nextHandle = 1000;
        private int _activeNativeCalls;
        private int _maximumConcurrentNativeCalls;

        public uint AbiVersion { get; set; } = Nuan5GalleryMetadataService.ExpectedAbiVersion;

        public Exception? AbiException { get; set; }

        public uint DecodeStatus { get; set; }

        public uint DecryptStatus { get; set; }

        public bool AliasKeys { get; set; }

        public TimeSpan DecodeDelay { get; set; }

        public int AbiCallCount { get; private set; }

        public int CreatedKeyCount { get; private set; }

        public int DecodeCallCount { get; private set; }

        public int DecryptCallCount { get; private set; }

        public int MaximumConcurrentNativeCalls => Volatile.Read(ref _maximumConcurrentNativeCalls);

        public ConcurrentDictionary<IntPtr, int> FreedBytes { get; } = new();

        public ConcurrentDictionary<IntPtr, int> FreedKeys { get; } = new();

        public byte[] FirstPayload { get; set; } = Encoding.UTF8.GetBytes(
            """
            {
              "SocialPhoto": {
                "CameraParams": "camera-ciphertext",
                "photo_info": {
                  "pose_id": 7001,
                  "framed_moment": 19,
                  "nikki_clothes": [100020003, { "id": 200030004 }],
                  "nikki_loc_x": 1.25,
                  "nikki_loc_y": -2.5,
                  "nikki_loc_z": 9.75,
                  "task": { "type": "story", "id": 93 }
                }
              },
              "puzzle_game_plugin": { "tag": 88 }
            }
            """);

        public byte[] CameraPayload { get; set; } = Encoding.UTF8.GetBytes(
            """
            [0,1,2,3,4,5,6,7,8,9,10,11,12,13,50.5,2.8,16,"light-4",0.4,0.19,0.2,0.21,0.22,0.23,0.24,0.25,0.26,0.27,0.28,"filter-7",0.3]
            """);

        public static FakeNativeAdapter ForSuccess() => new();

        public uint GetAbiVersion()
        {
            AbiCallCount++;
            if (AbiException is not null)
            {
                throw AbiException;
            }

            return AbiVersion;
        }

        public IntPtr CreateMediaKey(string value)
        {
            CreatedKeyCount++;
            return new IntPtr(41);
        }

        public IntPtr CreateCameraParameterKey()
        {
            CreatedKeyCount++;
            return AliasKeys ? new IntPtr(41) : new IntPtr(42);
        }

        public Nuan5MediaResult DecodeFileBytesUnchecked(byte[] flag, byte[] fileBytes, IntPtr key)
        {
            EnterNativeCall();
            try
            {
                DecodeCallCount++;
                Assert(flag.SequenceEqual(new byte[] { 0xff, 0xd9 }), "JPEG end flag");
                if (DecodeDelay > TimeSpan.Zero)
                {
                    Thread.Sleep(DecodeDelay);
                }

                return CreateResult(DecodeStatus, FirstPayload);
            }
            finally
            {
                ExitNativeCall();
            }
        }

        public Nuan5MediaResult Decrypt(byte[] data, IntPtr key)
        {
            EnterNativeCall();
            try
            {
                DecryptCallCount++;
                AssertEqual("camera-ciphertext", Encoding.UTF8.GetString(data), "camera ciphertext input");
                return CreateResult(DecryptStatus, CameraPayload);
            }
            finally
            {
                ExitNativeCall();
            }
        }

        public byte[] ReadBytes(Nuan5CBytes bytes) =>
            _payloads.TryGetValue(bytes.Data, out var payload)
                ? payload
                : throw new InvalidOperationException("unknown native bytes");

        public void FreeMediaKey(IntPtr key) =>
            FreedKeys.AddOrUpdate(key, 1, static (_, count) => count + 1);

        public void FreeBytes(Nuan5CBytes bytes) =>
            FreedBytes.AddOrUpdate(bytes.Data, 1, static (_, count) => count + 1);

        private Nuan5MediaResult CreateResult(uint status, byte[] payload)
        {
            var handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
            _payloads[handle] = payload;
            return new Nuan5MediaResult(
                status,
                new Nuan5CBytes(handle, checked((nuint)payload.Length), checked((nuint)payload.Length)));
        }

        private void EnterNativeCall()
        {
            var active = Interlocked.Increment(ref _activeNativeCalls);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentNativeCalls);
                if (active <= maximum ||
                    Interlocked.CompareExchange(ref _maximumConcurrentNativeCalls, active, maximum) == maximum)
                {
                    break;
                }
            }
        }

        private void ExitNativeCall() => Interlocked.Decrement(ref _activeNativeCalls);
    }
}
