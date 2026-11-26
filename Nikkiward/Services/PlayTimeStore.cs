using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Services;

internal sealed record PlayTimeDocument(int SchemaVersion, IReadOnlyList<PlaySession> Sessions);

internal sealed class PlayTimeStore(string root)
{
    private static readonly JsonTypeInfo<PlayTimeDocument> DocumentType =
        new NikkiwardJsonContext(new JsonSerializerOptions { RespectRequiredConstructorParameters = true }).PlayTimeDocument;
    internal const string RelativePath = "PlayTime/sessions.json";
    private readonly string _path = Path.Combine(root, RelativePath);
    private bool _protected;

    public IReadOnlyList<PlaySession> Load()
    {
        _protected = true;
        try
        {
            using var stream = File.OpenRead(_path);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("SchemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number &&
                schema.TryGetInt32(out var version) && version > 1)
                throw new InvalidDataException("游玩记录由更高版本写入，请更新 Nikkiward。");
            var document = json.Deserialize(DocumentType);
            if (document is not { SchemaVersion: 1, Sessions: not null } ||
                document.Sessions.Any(s => s is null || string.IsNullOrWhiteSpace(s.Channel) ||
                    s.StartUtc.Offset != TimeSpan.Zero || s.EndUtc.Offset != TimeSpan.Zero ||
                    s.StartUtc > s.EndUtc || s.Duration != s.EndUtc - s.StartUtc))
                throw new InvalidDataException("游玩记录格式损坏，已保留原文件。");
            // The journal displays the sum as a TimeSpan.
            _ = document.Sessions.Sum(s => s.Duration.Ticks);
            _protected = false;
            return document.Sessions;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            _protected = false;
            return [];
        }
        catch (Exception ex) when (ex is JsonException or OverflowException)
        {
            throw new InvalidDataException("游玩记录格式损坏，已保留原文件。", ex);
        }
    }

    public void Save(IReadOnlyList<PlaySession> sessions)
    {
        EnsureWritable();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var file = new AtomicFile(_path);
        JsonSerializer.Serialize(file.Stream, new PlayTimeDocument(1, sessions), DocumentType);
        file.Commit(overwrite: true);
    }

    public void Clear()
    {
        EnsureWritable();
        try { File.Delete(_path); }
        catch (DirectoryNotFoundException) { }
    }

    private void EnsureWritable()
    {
        if (_protected) throw new InvalidOperationException("游玩记录处于只读保护状态。");
    }
}
