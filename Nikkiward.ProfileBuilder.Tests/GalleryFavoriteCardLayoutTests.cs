using Nikkiward.Features.Gallery;

internal static class GalleryFavoriteCardLayoutTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("favorite gallery preserves mixed source aspect ratios", TestMixedAspectLayout),
        ("favorite gallery packs up to four adaptive cards per row", TestRowPacking),
        ("favorite gallery last row stays compact", TestCompactLastRow),
        ("favorite gallery remains responsive at narrow widths", TestNarrowLayout),
        ("favorite gallery layout stays bounded and non-overlapping", TestLayoutBounds),
        ("favorite gallery layout rejects invalid inputs", TestInvalidInputs),
        ("favorite gallery direct border glow stays view gated", TestVisualContract),
    ];

    private static readonly double[] MixedAspectRatios =
    [
        16d / 9d,
        1.6d,
        9d / 16d,
        1.6d,
    ];

    private static Task TestMixedAspectLayout()
    {
        var layout = GalleryFavoriteCardLayoutProjection.Project(1284d, MixedAspectRatios);

        Assert(layout.IsValid, "mixed layout");
        AssertEqual(MixedAspectRatios.Length, layout.Placements.Count, "placement count");
        foreach (var placement in layout.Placements)
        {
            var actualRatio = placement.Width / placement.Height;
            AssertNear(
                MixedAspectRatios[placement.ItemIndex],
                actualRatio,
                0.000001d,
                $"item {placement.ItemIndex} aspect ratio");
        }

        var portrait = layout.Placements.Single(placement => placement.ItemIndex == 2);
        Assert(portrait.Width < portrait.Height, "portrait card shape");
        return Task.CompletedTask;
    }

    private static Task TestRowPacking()
    {
        var layout = GalleryFavoriteCardLayoutProjection.Project(1284d, MixedAspectRatios);
        var rows = layout.Placements.GroupBy(placement => placement.RowIndex).ToArray();

        Assert(rows.Length <= 2, "four-card row count");
        Assert(
            rows.All(row => row.Count() <= GalleryFavoriteCardLayoutProjection.MaximumItemsPerRow),
            "maximum row count");
        Assert(layout.ContentHeight < 760d, "four cards fit a wide launcher viewport");
        return Task.CompletedTask;
    }

    private static Task TestCompactLastRow()
    {
        const double availableWidth = 1284d;
        var layout = GalleryFavoriteCardLayoutProjection.Project(
            availableWidth,
            [16d / 9d]);
        var card = layout.Placements.Single();

        Assert(card.Width < availableWidth * 0.6d, "single last-row card width");
        AssertNear(16d / 9d, card.Width / card.Height, 0.000001d, "single aspect ratio");
        return Task.CompletedTask;
    }

    private static Task TestNarrowLayout()
    {
        const double availableWidth = 390d;
        var layout = GalleryFavoriteCardLayoutProjection.Project(
            availableWidth,
            [16d / 9d, 1.6d, 9d / 16d]);

        Assert(layout.IsValid, "narrow layout");
        Assert(
            layout.Placements.All(item => item.X + item.Width <= availableWidth + 0.000001d),
            "narrow width boundary");
        return Task.CompletedTask;
    }

    private static Task TestLayoutBounds()
    {
        const double availableWidth = 1284d;
        var layout = GalleryFavoriteCardLayoutProjection.Project(
            availableWidth,
            [16d / 9d, 1.6d, 9d / 16d, 1.6d, 2.4d, 0.8d]);

        Assert(
            layout.Placements.All(item =>
                item.X >= 0d &&
                item.Y >= 0d &&
                item.X + item.Width <= availableWidth + 0.000001d &&
                item.Y + item.Height <= layout.ContentHeight + 0.000001d),
            "layout boundaries");
        for (var firstIndex = 0; firstIndex < layout.Placements.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1;
                 secondIndex < layout.Placements.Count;
                 secondIndex++)
            {
                Assert(
                    !Overlaps(layout.Placements[firstIndex], layout.Placements[secondIndex]),
                    $"placements {firstIndex} and {secondIndex} overlap");
            }
        }

        return Task.CompletedTask;
    }

    private static Task TestInvalidInputs()
    {
        var invalidWidth = GalleryFavoriteCardLayoutProjection.Project(0d, [1d]);
        var fallbackRatio = GalleryFavoriteCardLayoutProjection.Project(
            800d,
            [double.NaN]);

        Assert(
            !invalidWidth.IsValid && invalidWidth.Placements.Count == 0,
            "zero width");
        AssertNear(
            GalleryFavoriteCardLayoutProjection.DefaultAspectRatio,
            fallbackRatio.Placements.Single().Width / fallbackRatio.Placements.Single().Height,
            0.000001d,
            "invalid ratio fallback");
        return Task.CompletedTask;
    }

    private static Task TestVisualContract()
    {
        var xaml = ReadSource("Nikkiward", "Pages", "GalleryPage.xaml");
        var interactions = ReadSource(
            "Nikkiward",
            "Pages",
            "GalleryPage.Interactions.cs");
        var pageCode = ReadSource("Nikkiward", "Pages", "GalleryPage.xaml.cs");
        var favoritePanel = ReadSource(
            "Nikkiward",
            "Features",
            "Gallery",
            "GalleryFavoriteJustifiedPanel.cs");
        var borderGlowCode = ReadSource("Nikkiward", "Controls", "CardBorderGlow.cs");
        var galleryViewModel = ReadSource("Nikkiward", "ViewModels", "GalleryViewModel.cs");
        var notices = ReadSource("THIRD-PARTY-NOTICES.md");
        var navigation = ReadSource("Nikkiward", "MainPage.ContentNavigation.cs");
        var appearanceRuntime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");
        var imageIndex = xaml.IndexOf("x:Name=\"GalleryThumbnail\"", StringComparison.Ordinal);
        var materialIndex = xaml.IndexOf(
            "x:Name=\"GalleryBorderGlow\"",
            StringComparison.Ordinal);
        var actionsIndex = xaml.IndexOf(
            "x:Name=\"GalleryStarButton\"",
            StringComparison.Ordinal);

        var materialEndIndex = xaml.IndexOf(
            "</controls:CardBorderGlow>",
            StringComparison.Ordinal);
        Assert(
            materialIndex >= 0 &&
            imageIndex > materialIndex &&
            actionsIndex > imageIndex &&
            materialEndIndex > actionsIndex,
            "border glow must own the image and local actions");
        Assert(xaml.Contains("Stretch=\"Uniform\"", StringComparison.Ordinal),
            "complete image fit");
        Assert(
            xaml.Contains(
                "Source=\"{x:Bind CardSource, Mode=OneWay}\"",
                StringComparison.Ordinal) &&
            galleryViewModel.Contains(
                "public ImageSource CardSource => _isStarred",
                StringComparison.Ordinal) &&
            galleryViewModel.Contains(
                "CreateOptions = BitmapCreateOptions.IgnoreImageCache",
                StringComparison.Ordinal) &&
            galleryViewModel.Contains(
                "_cardSource ??= CreateFullResolutionSource",
                StringComparison.Ordinal) &&
            galleryViewModel.Contains(
                "stem.EndsWith(\"_Low\"",
                StringComparison.Ordinal) &&
            galleryViewModel.Contains("DecodePixelWidth = 360", StringComparison.Ordinal),
            "favorites must bypass the retained ordinary thumbnail decode path");
        Assert(
            xaml.Contains("GalleryStandardItemsPanelTemplate", StringComparison.Ordinal) &&
            xaml.Contains("GalleryFavoriteItemsPanelTemplate", StringComparison.Ordinal) &&
            xaml.Contains("gallery:GalleryFavoriteJustifiedPanel", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"GalleryStandardGridHost\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"GalleryFavoriteGridHost\"", StringComparison.Ordinal) &&
            xaml.Contains(
                "ItemsSource=\"{x:Bind ViewModel.Photos, Mode=OneWay}\"",
                StringComparison.Ordinal) &&
            pageCode.Contains("ActiveGalleryGridView", StringComparison.Ordinal) &&
            !pageCode.Contains(".ItemsPanel =", StringComparison.Ordinal) &&
            favoritePanel.Contains(
                "GalleryFavoriteCardLayoutProjection.Project",
                StringComparison.Ordinal),
            "favorite layout must use a dedicated proportional panel");
        Assert(
            galleryViewModel.Contains("public double CardAspectRatio", StringComparison.Ordinal) &&
            galleryViewModel.Contains("decoder.OrientedPixelWidth", StringComparison.Ordinal) &&
            galleryViewModel.Contains("decoder.OrientedPixelHeight", StringComparison.Ordinal) &&
            favoritePanel.Contains(
                "nameof(GalleryPhotoItemViewModel.CardAspectRatio)",
                StringComparison.Ordinal),
            "favorite layout must follow oriented source dimensions");
        Assert(
            !xaml.Contains("GalleryImageInfo", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"GalleryStarButton\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"GalleryCopyButton\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"GalleryTimePlate\"", StringComparison.Ordinal),
            "hover controls must use local hit targets rather than a full-card plate");
        Assert(
            xaml.Contains("SelectionMode=\"None\"", StringComparison.Ordinal) &&
            xaml.Contains("GridViewItemBackgroundPointerOver", StringComparison.Ordinal) &&
            !xaml.Contains("GalleryHolographicOverlay", StringComparison.Ordinal),
            "favorite cards must suppress system gray states and the full material canvas");
        Assert(
            !xaml.Contains("<Image.Projection>", StringComparison.Ordinal) &&
            !xaml.Contains("<PlaneProjection", StringComparison.Ordinal) &&
            !xaml.Contains("Visual.Orientation", StringComparison.Ordinal),
            "favorite cards must not run a second tilt path");
        Assert(
            xaml.Contains("x:Name=\"GalleryThumbnailScaleHost\"", StringComparison.Ordinal) &&
            interactions.Contains(
                "ApplyScaleTransition(thumbnailScaleHost)",
                StringComparison.Ordinal) &&
            !interactions.Contains(
                "ApplyScaleTransition(thumbnail);",
                StringComparison.Ordinal) &&
            !interactions.Contains("thumbnail.CenterPoint", StringComparison.Ordinal) &&
            !xaml.Contains("OnGalleryThumbnailSizeChanged", StringComparison.Ordinal),
            "ordinary thumbnail scale must remain on its existing host");
        Assert(
            xaml.Contains("IsDirectPointerTrackingEnabled=\"True\"", StringComparison.Ordinal) &&
            xaml.Contains("IsIntroAnimationEnabled=\"False\"", StringComparison.Ordinal) &&
            xaml.Contains("IsLiftEnabled=\"False\"", StringComparison.Ordinal) &&
            !xaml.Contains("PointerMoved=\"OnGalleryItemPointerMoved\"", StringComparison.Ordinal),
            "React Bits pointer tracking must be direct and single-owner");
        Assert(
            pageCode.Contains("_viewMode == GalleryViewMode.Favorites", StringComparison.Ordinal) &&
            pageCode.Contains("HolographicCardEnabled", StringComparison.Ordinal) &&
            pageCode.Contains("_surfaceActive", StringComparison.Ordinal),
            "favorites setting and surface gate");
        Assert(
            interactions.Contains("borderGlow.SetGlowEnabled(effectEnabled)", StringComparison.Ordinal) &&
            interactions.Contains("borderGlow.ApplyMotion(_appearanceSettings.Motion)", StringComparison.Ordinal) &&
            interactions.Contains("FavoriteCardCornerRadius = 16d", StringComparison.Ordinal) &&
            !interactions.Contains("SetGalleryThumbnailTilt", StringComparison.Ordinal),
            "surface gate and large-card radius must not add tilt work");
        Assert(
            interactions.Contains("_favoriteOperationGates.GetOrAdd", StringComparison.Ordinal) &&
            interactions.Contains("previous.Cancel()", StringComparison.Ordinal) &&
            interactions.Contains("EnsureCurrentFavoriteOperation", StringComparison.Ordinal) &&
            interactions.Contains("CleanUnstarredAsync", StringComparison.Ordinal) &&
            interactions.Contains("operationCancellation.Token", StringComparison.Ordinal),
            "favorite protection must serialize per photo and reject stale profile results");
        Assert(
            borderGlowCode.Contains("IsDirectPointerTrackingEnabled", StringComparison.Ordinal) &&
            borderGlowCode.Contains("_renderAngle = _pointerState.AngleDegrees", StringComparison.Ordinal) &&
            borderGlowCode.Contains("_animationTimer?.Stop()", StringComparison.Ordinal) &&
            borderGlowCode.Contains("if (!IsDirectPointerTrackingEnabled || _introActive)", StringComparison.Ordinal),
            "direct pointer mode must update once and keep its idle timer stopped");
        Assert(
            notices.Contains("React Bits Border Glow", StringComparison.Ordinal) &&
            notices.Contains("4e0e030193b563be6be33d928f77d0d01cefe237", StringComparison.Ordinal) &&
            notices.Contains("Commons Clause", StringComparison.Ordinal),
            "ported source and license must remain attributed");
        Assert(
            navigation.Contains("galleryPage.SetSurfaceActive(false)", StringComparison.Ordinal) &&
            navigation.Contains("ViewModel.AppearanceSettings", StringComparison.Ordinal),
            "navigation gating and appearance propagation");
        Assert(
            appearanceRuntime.Contains(
                "ContentFrame.Content is GalleryPage galleryPage",
                StringComparison.Ordinal) &&
            appearanceRuntime.Contains(
                "galleryPage.ApplyAppearanceSettings(settings)",
                StringComparison.Ordinal),
            "live appearance propagation");
        return Task.CompletedTask;
    }

    private static string ReadSource(params string[] segments)
    {
        var root = FindRoot();
        return File.ReadAllText(segments.Aggregate(root, Path.Combine));
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Nikkiward", "Nikkiward.csproj")) &&
                File.Exists(Path.Combine(
                    current.FullName,
                    "Nikkiward.ProfileBuilder.Tests",
                    "Nikkiward.ProfileBuilder.Tests.csproj")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The Nikkiward source root was not found.");
    }

    private static bool Overlaps(
        GalleryFavoriteCardPlacement first,
        GalleryFavoriteCardPlacement second) =>
        first.X < second.X + second.Width &&
        first.X + first.Width > second.X &&
        first.Y < second.Y + second.Height &&
        first.Y + first.Height > second.Y;

    private static void AssertNear(
        double expected,
        double actual,
        double tolerance,
        string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
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
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
    }
}
