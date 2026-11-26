using Microsoft.Win32;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Models;

internal static class GameBarGuideTests
{
    private static readonly GamepadSettings Mapped = new() { Enabled = true, GuideAction = GamepadButtonAction.MapKeys };
    private static readonly GameBarRegistryValue One = new((uint)RegistryValueKind.DWord, [1, 0, 0, 0]);

    public static (string Name, Func<Task> Run)[] All =>
    [
        ("Game Bar stays untouched without an effective Guide mapping", WithoutMapping),
        ("Game Bar restores an original DWORD zero", () => RoundTrip(GameBarRegistryValue.Disabled)),
        ("Game Bar restores an original DWORD one", () => RoundTrip(One)),
        ("Game Bar deletes a previously absent value", () => RoundTrip(null)),
        ("Game Bar restores the original registry type and bytes", () => RoundTrip(
            new GameBarRegistryValue((uint)RegistryValueKind.ExpandString, System.Text.Encoding.Unicode.GetBytes("%TEMP%\0")))),
        ("Game Bar preserves external edits and deletion during takeover", ExternalEdits),
        ("Game Bar recovers a crashed takeover before applying settings", CrashRecovery),
        ("Game Bar leaves another live instance's takeover alone", ConcurrentInstance),
        ("Game Bar leaves the registry alone when recovery cannot be recorded", CannotRecord),
    ];

    private static Task WithoutMapping()
    {
        using var fixture = new Fixture(null);
        fixture.Guide.Apply(new GamepadSettings(), initialized: false, hasGuideKeys: false);
        fixture.Guide.Apply(Mapped with { Enabled = false }, true, true);
        fixture.Guide.Apply(Mapped with { GuideAction = GamepadButtonAction.None }, true, true);
        fixture.Guide.Apply(Mapped, initialized: false, hasGuideKeys: true);
        fixture.Guide.Apply(Mapped, initialized: true, hasGuideKeys: false);
        fixture.Guide.Release();
        fixture.Guide.Release();
        Assert(fixture.Registry.Writes.Count == 0 && !File.Exists(fixture.StatePath), "idle must perform zero writes");
        return Task.CompletedTask;
    }

    private static Task RoundTrip(GameBarRegistryValue? original)
    {
        using var fixture = new Fixture(original);
        fixture.Guide.Apply(Mapped, true, true);
        Assert(fixture.Registry.Value?.IsDisabled == true, "Guide must be taken over");
        Assert(File.Exists(fixture.StatePath), "the original value must be on disk");
        var activeWrites = fixture.Registry.Writes.Count;
        fixture.Guide.Apply(Mapped, true, true);
        Assert(fixture.Registry.Writes.Count == activeWrites, "reapplying settings must preserve the original value");
        fixture.Guide.Apply(Mapped with { GuideAction = GamepadButtonAction.None }, true, true);
        Assert(Same(original, fixture.Registry.Value), "removing the mapping must restore the exact original");
        Assert(!File.Exists(fixture.StatePath), "released record must be retired");
        fixture.Guide.Apply(Mapped, true, true);
        fixture.Guide.Apply(Mapped with { Enabled = false }, true, true);
        Assert(Same(original, fixture.Registry.Value), "disabling the feature must restore the exact original");
        fixture.Guide.Apply(Mapped, true, true);
        fixture.Guide.Release();
        Assert(Same(original, fixture.Registry.Value), "exit must restore the exact original");
        var releasedWrites = fixture.Registry.Writes.Count;
        fixture.Guide.Release();
        Assert(fixture.Registry.Writes.Count == releasedWrites, "repeated exit must do nothing");
        return Task.CompletedTask;
    }

    private static Task ExternalEdits()
    {
        foreach (var userValue in new GameBarRegistryValue?[]
                 { One, null, new((uint)RegistryValueKind.Binary, [0, 0, 0, 0]) })
        {
            using var fixture = new Fixture(null);
            fixture.Guide.Apply(Mapped, true, true);
            var activeWrites = fixture.Registry.Writes.Count;
            fixture.Registry.Value = userValue;
            fixture.Guide.Apply(Mapped, true, true);
            fixture.Guide.Release();
            Assert(fixture.Registry.Writes.Count == activeWrites && Same(userValue, fixture.Registry.Value),
                "an external edit must survive both reapplying and releasing");
            Assert(!File.Exists(fixture.StatePath), "externally changed record must be retired");
        }
        return Task.CompletedTask;
    }

    private static Task CrashRecovery()
    {
        foreach (var enabledAfterRestart in new[] { false, true })
        {
            using var fixture = new Fixture(One);
            fixture.Registry.AfterWrite = () =>
            {
                Assert(File.Exists(fixture.StatePath), "recovery must be durable before the first registry write");
                throw new IOException("simulated interruption after registry write");
            };
            fixture.Guide.Apply(Mapped, true, true);
            Assert(fixture.Registry.Value?.IsDisabled == true && File.Exists(fixture.StatePath), "takeover was interrupted");
            fixture.Registry.AfterWrite = null;
            fixture.Registry.Writes.Clear();

            var restarted = new GameBarGuideOverride(fixture.StatePath, fixture.Registry);
            try
            {
                restarted.Apply(Mapped with { Enabled = enabledAfterRestart }, true, true);
                Assert(Same(One, fixture.Registry.Writes.FirstOrDefault()), "startup must restore before reapplying");
                Assert(fixture.Registry.Writes.Count == (enabledAfterRestart ? 2 : 1), "startup write count");
            }
            finally
            {
                restarted.Release();
            }
            Assert(Same(One, fixture.Registry.Value) && !File.Exists(fixture.StatePath), "recovered final state");
        }
        return Task.CompletedTask;
    }

    private static Task ConcurrentInstance()
    {
        using var fixture = new Fixture(One);
        fixture.Guide.Apply(Mapped, true, true);
        var other = new GameBarGuideOverride(fixture.StatePath, fixture.Registry);
        other.Apply(new GamepadSettings(), false, false);
        other.Apply(Mapped, true, true);
        other.Release();
        Assert(fixture.Registry.Value?.IsDisabled == true && fixture.Registry.Writes.Count == 1, "live ownership was stolen");
        return Task.CompletedTask;
    }

    private static Task CannotRecord()
    {
        using var fixture = new Fixture(One);
        Directory.CreateDirectory(fixture.StatePath);
        fixture.Guide.Apply(Mapped, true, true);
        Assert(fixture.Registry.Writes.Count == 0 && Same(One, fixture.Registry.Value), "failed journal must prevent takeover");
        return Task.CompletedTask;
    }

    private static bool Same(GameBarRegistryValue? expected, GameBarRegistryValue? actual) =>
        expected is null ? actual is null : actual?.Type == expected.Type && expected.Data.SequenceEqual(actual.Data);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeRegistry(GameBarRegistryValue? original) : IGameBarGuideRegistry
    {
        public GameBarRegistryValue? Value { get; set; } = original;
        public List<GameBarRegistryValue?> Writes { get; } = [];
        public Action? AfterWrite { get; set; }
        public GameBarRegistryValue? Read() => Value;
        public void Write(GameBarRegistryValue? value)
        {
            Writes.Add(value);
            Value = value;
            AfterWrite?.Invoke();
        }
    }

    private sealed class Fixture(GameBarRegistryValue? original) : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("Nikkiward-GameBar-");
        public FakeRegistry Registry { get; } = new(original);
        public string StatePath => Path.Combine(_root.FullName, "guide.json");
        public GameBarGuideOverride Guide => field ??= new GameBarGuideOverride(StatePath, Registry);
        public void Dispose()
        {
            Registry.AfterWrite = null;
            Guide.Release();
            _root.Delete(recursive: true);
        }
    }
}
