using System.Text.Json.Serialization;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ArtAnalysis))]
[JsonSerializable(typeof(GalleryAnnotationSnapshot))]
[JsonSerializable(typeof(GalleryFavoriteProtectionPreferences))]
[JsonSerializable(typeof(NikkiGalleryToolRegistration))]
[JsonSerializable(typeof(AppearanceSettings))]
[JsonSerializable(typeof(GeneralSettings))]
[JsonSerializable(typeof(DownloadSettings))]
[JsonSerializable(typeof(FileManagementSettings))]
[JsonSerializable(typeof(ScreenshotSettings))]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(JournalSnapshot))]
[JsonSerializable(typeof(ResonanceHistorySnapshot))]
[JsonSerializable(typeof(WishHistoryStoreSnapshot))]
[JsonSerializable(typeof(LocalPluginManifest))]
[JsonSerializable(typeof(DiagnosticReportDocument))]
internal sealed partial class NikkiwardJsonContext : JsonSerializerContext
{
}
