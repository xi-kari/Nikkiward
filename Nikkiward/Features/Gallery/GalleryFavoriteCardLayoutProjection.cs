namespace Nikkiward.Features.Gallery;

public readonly record struct GalleryFavoriteCardPlacement(
    int ItemIndex,
    int RowIndex,
    double X,
    double Y,
    double Width,
    double Height)
{
    public bool IsValid =>
        ItemIndex >= 0 &&
        RowIndex >= 0 &&
        X >= 0d &&
        Y >= 0d &&
        Width > 0d &&
        Height > 0d &&
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);
}

public sealed record GalleryFavoriteCardLayout(
    double AvailableWidth,
    double ContentHeight,
    IReadOnlyList<GalleryFavoriteCardPlacement> Placements)
{
    public bool IsValid =>
        AvailableWidth > 0d &&
        ContentHeight >= 0d &&
        double.IsFinite(AvailableWidth) &&
        double.IsFinite(ContentHeight) &&
        Placements.All(placement => placement.IsValid);
}

public static class GalleryFavoriteCardLayoutProjection
{
    public const double DefaultAspectRatio = 16d / 9d;
    public const double StandardItemWidth = 216d;
    public const double StandardItemHeight = 127d;
    public const double ItemSpacing = 12d;
    public const double MinimumTargetRowHeight = 220d;
    public const double MaximumTargetRowHeight = 340d;
    public const int MaximumItemsPerRow = 4;

    public static GalleryFavoriteCardLayout Project(
        double availableWidth,
        IReadOnlyList<double> aspectRatios)
    {
        ArgumentNullException.ThrowIfNull(aspectRatios);
        if (!double.IsFinite(availableWidth) || availableWidth <= 0d)
        {
            return new GalleryFavoriteCardLayout(
                0d,
                0d,
                Array.Empty<GalleryFavoriteCardPlacement>());
        }

        if (aspectRatios.Count == 0)
        {
            return new GalleryFavoriteCardLayout(
                availableWidth,
                0d,
                Array.Empty<GalleryFavoriteCardPlacement>());
        }

        var normalizedRatios = aspectRatios
            .Select(NormalizeAspectRatio)
            .ToArray();
        var targetRowHeight = Math.Clamp(
            availableWidth / 4d,
            MinimumTargetRowHeight,
            MaximumTargetRowHeight);
        var placements = new List<GalleryFavoriteCardPlacement>(normalizedRatios.Length);
        var rowIndex = 0;
        var itemIndex = 0;
        var y = 0d;

        while (itemIndex < normalizedRatios.Length)
        {
            var rowStart = itemIndex;
            var rowCount = 1;
            var rowRatio = normalizedRatios[itemIndex];

            while (rowStart + rowCount < normalizedRatios.Length &&
                   rowCount < MaximumItemsPerRow)
            {
                var currentHeight = CalculateFilledRowHeight(
                    availableWidth,
                    rowRatio,
                    rowCount);
                var candidateRatio = rowRatio + normalizedRatios[rowStart + rowCount];
                var candidateHeight = CalculateFilledRowHeight(
                    availableWidth,
                    candidateRatio,
                    rowCount + 1);
                if (Math.Abs(currentHeight - targetRowHeight) <=
                    Math.Abs(candidateHeight - targetRowHeight))
                {
                    break;
                }

                rowRatio = candidateRatio;
                rowCount++;
            }

            var filledRowHeight = CalculateFilledRowHeight(
                availableWidth,
                rowRatio,
                rowCount);
            var isLastRow = rowStart + rowCount >= normalizedRatios.Length;
            var rowHeight = isLastRow || filledRowHeight > MaximumTargetRowHeight
                ? Math.Min(targetRowHeight, filledRowHeight)
                : filledRowHeight;
            var usedWidth = rowHeight * rowRatio + ItemSpacing * (rowCount - 1);
            var x = Math.Max(0d, (availableWidth - usedWidth) / 2d);

            for (var rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                var placementIndex = rowStart + rowOffset;
                var cardWidth = rowHeight * normalizedRatios[placementIndex];
                placements.Add(new GalleryFavoriteCardPlacement(
                    placementIndex,
                    rowIndex,
                    x,
                    y,
                    cardWidth,
                    rowHeight));
                x += cardWidth + ItemSpacing;
            }

            itemIndex += rowCount;
            rowIndex++;
            y += rowHeight;
            if (itemIndex < normalizedRatios.Length)
            {
                y += ItemSpacing;
            }
        }

        return new GalleryFavoriteCardLayout(availableWidth, y, placements);
    }

    private static double CalculateFilledRowHeight(
        double availableWidth,
        double totalAspectRatio,
        int itemCount)
    {
        var usableWidth = availableWidth - ItemSpacing * (itemCount - 1);
        return Math.Max(0.000001d, usableWidth / totalAspectRatio);
    }

    private static double NormalizeAspectRatio(double aspectRatio) =>
        double.IsFinite(aspectRatio) && aspectRatio > 0d
            ? aspectRatio
            : DefaultAspectRatio;
}
