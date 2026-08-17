using System.Text.Json.Serialization;

namespace Nikkiward.Features.Updates;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GitHubRelease[]))]
[JsonSerializable(typeof(UpdateManifest))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
