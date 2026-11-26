using System.ComponentModel;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nikkiward.Models;
using Nikkiward.Services;

namespace Nikkiward.Features.GamepadControl;

internal sealed class GameBarGuideOverride(string statePath, IGameBarGuideRegistry registry)
{
    private readonly object _gate = new();
    private FileStream? _ownershipLock;

    public void Apply(GamepadSettings settings, bool initialized, bool hasGuideKeys) =>
        SetActive(settings.Enabled && initialized && settings.GuideAction is GamepadButtonAction.MapKeys && hasGuideKeys);

    public void Release() => SetActive(false);

    private void SetActive(bool active)
    {
        lock (_gate)
        {
            if (active && _ownershipLock is not null ||
                !active && _ownershipLock is null && !File.Exists(statePath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                // The OS releases this handle on process death. A live second instance must leave the record alone.
                _ownershipLock ??= new FileStream(statePath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                if (File.Exists(statePath))
                {
                    Restore();
                }

                if (active)
                {
                    var original = registry.Read();
                    using (var file = new AtomicFile(statePath))
                    {
                        JsonSerializer.Serialize(file.Stream, new GameBarGuideState(original), GameBarJsonContext.Default.GameBarGuideState);
                        file.Commit(overwrite: false);
                    }

                    if (original?.IsDisabled != true)
                    {
                        registry.Write(GameBarRegistryValue.Disabled);
                    }
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                or Win32Exception or JsonException or InvalidDataException)
            {
                // Retain any pending record for the next attempt when the hive or disk is available.
            }

            _ownershipLock?.Dispose();
            _ownershipLock = null;
        }
    }

    private void Restore()
    {
        GameBarGuideState? state;
        using (var stream = File.OpenRead(statePath))
        {
            state = JsonSerializer.Deserialize(stream, GameBarJsonContext.Default.GameBarGuideState);
        }
        if (state is null || state.Original is { Data: null })
        {
            throw new InvalidDataException("Invalid Game Bar recovery state.");
        }

        if (registry.Read()?.IsDisabled == true && state.Original?.IsDisabled != true)
        {
            registry.Write(state.Original);
        }
        File.Delete(statePath);
    }
}

internal sealed record GameBarGuideState([property: JsonRequired] GameBarRegistryValue? Original);

[JsonSerializable(typeof(GameBarGuideState))]
internal sealed partial class GameBarJsonContext : JsonSerializerContext
{
}
