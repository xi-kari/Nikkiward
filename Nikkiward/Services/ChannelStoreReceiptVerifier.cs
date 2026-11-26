using System.Text.Json;
using Nikkiward.Models;

namespace Nikkiward.Services;

public static class ChannelStoreReceiptVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static ChannelStoreBuildReceipt? ReadReceipt(ChannelStoreSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.StoreRootPath) ||
            string.IsNullOrWhiteSpace(settings.LastReceiptId) ||
            Path.GetFileName(settings.LastReceiptId) != settings.LastReceiptId ||
            !VariantHash.IsSha256(settings.LastPlanSha256))
        {
            return null;
        }

        try
        {
            var root = NormalizeDirectory(settings.StoreRootPath);
            var path = Path.Combine(root, "receipts", settings.LastReceiptId + ".json");
            var receipt = JsonSerializer.Deserialize<ChannelStoreBuildReceipt>(File.ReadAllText(path), JsonOptions);
            return receipt is { Succeeded: true } && receipt.ReceiptId == settings.LastReceiptId &&
                string.Equals(receipt.PlanSha256, settings.LastPlanSha256, StringComparison.OrdinalIgnoreCase) &&
                PathEquals(receipt.StoreRootPath, root) ? receipt : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool Verify(ChannelStoreSettings settings, ChannelStoreProfileSettings profile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.LauncherRootPath) || string.IsNullOrWhiteSpace(profile.XStarterPath) ||
            ReadReceipt(settings) is not { } receipt)
        {
            return false;
        }

        try
        {
            var variantId = profile.DistributionChannel switch
            {
                DistributionChannel.Official => GameVariantId.MainlandOfficial,
                DistributionChannel.Bilibili => GameVariantId.MainlandBilibili,
                DistributionChannel.Steam => GameVariantId.GlobalSteam,
                _ => GameVariantId.Unknown,
            };
            var matches = receipt.Variants
                .Where(variant => variant.VariantId == variantId)
                .ToArray();
            return variantId is not GameVariantId.Unknown &&
                   matches.Length == 1 &&
                   matches[0].Materialization.Succeeded &&
                   PathEquals(matches[0].TargetGameRootPath, profile.GameRootPath) &&
                   PathEquals(matches[0].TargetLauncherRootPath, profile.LauncherRootPath) &&
                   PathEquals(matches[0].TargetXStarterPath, profile.XStarterPath);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            IOException or
            JsonException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
