using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Updates;
using System.Numerics;
using System.Text.Json;
using Windows.UI;
using Windows.UI.Text;

namespace Nikkiward.Features.Settings;

public sealed partial class AboutSettingsView : UserControl
{
    private const float AuthorCardWidth = 406f;
    private const float AuthorCardHeight = 564f;
    private const float AuthorPatternAngle = -20f;

    private static readonly Color[] AuthorPatternColors =
    [
        Color.FromArgb(25, 122, 225, 255),
        Color.FromArgb(24, 183, 159, 255),
        Color.FromArgb(23, 255, 169, 218),
        Color.FromArgb(22, 255, 218, 151),
    ];

    private readonly GitHubReleaseUpdateService _updateService = new();
    private readonly CanvasTextFormat _authorPatternFormat = new()
    {
        FontFamily = "Bahnschrift",
        FontSize = 28f,
        FontWeight = new FontWeight { Weight = 600 },
        WordWrapping = CanvasWordWrapping.NoWrap,
    };
    private readonly PlaneProjection _authorCardProjection = new()
    {
        CenterOfRotationX = 0.5d,
        CenterOfRotationY = 0.5d,
    };
    private AppVersionInfo? _appVersion;
    private CancellationTokenSource? _updateCancellation;
    private Uri? _releaseUri;
    private Vector2 _authorPointer = new(AuthorCardWidth / 2f, AuthorCardHeight / 2f);
    private Vector2 _authorPointerNormalized;
    private double _authorStrength;

    public AboutSettingsView()
    {
        InitializeComponent();
        UpdateChannelSelector.SelectionChanged += OnUpdateChannelChanged;
        AuthorProfileCard.Projection = _authorCardProjection;
        LoadVersionInformation();
    }

    private void OnAuthorProfilePointerEntered(object sender, PointerRoutedEventArgs e) =>
        UpdateAuthorPointer(e);

    private void OnAuthorProfilePointerMoved(object sender, PointerRoutedEventArgs e) =>
        UpdateAuthorPointer(e);

    private void OnAuthorProfilePointerExited(object sender, PointerRoutedEventArgs e) =>
        ResetAuthorPointer();

    private void OnAuthorProfilePointerCanceled(object sender, PointerRoutedEventArgs e) =>
        ResetAuthorPointer();

    private void OnAuthorProfilePointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        ResetAuthorPointer();

    private void UpdateAuthorPointer(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(AuthorProfileHitSurface).Position;
        var width = Math.Max(1d, AuthorProfileHitSurface.ActualWidth);
        var height = Math.Max(1d, AuthorProfileHitSurface.ActualHeight);
        _authorPointerNormalized = new Vector2(
            Math.Clamp((float)((point.X / width * 2d) - 1d), -1f, 1f),
            Math.Clamp((float)((point.Y / height * 2d) - 1d), -1f, 1f));
        _authorPointer = new Vector2(
            (float)((_authorPointerNormalized.X + 1d) * 0.5d * AuthorCardWidth),
            (float)((_authorPointerNormalized.Y + 1d) * 0.5d * AuthorCardHeight));
        _authorStrength = 1d;
        ApplyAuthorProjection();
        AuthorHologramCanvas.Invalidate();
    }

    private void ResetAuthorPointer()
    {
        _authorPointerNormalized = Vector2.Zero;
        _authorPointer = new Vector2(AuthorCardWidth / 2f, AuthorCardHeight / 2f);
        _authorStrength = 0d;
        ApplyAuthorProjection();
        AuthorHologramCanvas.Invalidate();
    }

    private void ApplyAuthorProjection()
    {
        var state = AuthorProfileDepthProjection.Project(
            AuthorCardWidth,
            AuthorCardHeight,
            (_authorPointerNormalized.X + 1d) * 0.5d * AuthorCardWidth,
            (_authorPointerNormalized.Y + 1d) * 0.5d * AuthorCardHeight,
            _authorStrength);
        _authorCardProjection.RotationX = state.RotationX;
        _authorCardProjection.RotationY = state.RotationY;
        AuthorAvatarImage.Translation = state.AvatarTranslation;
        AuthorHologramCanvas.Translation = state.ShineTranslation;
        AuthorTitleLayer.Translation = state.HeaderTranslation;
        AuthorBottomLayer.Translation = state.FooterTranslation;
    }

    private void OnAuthorHologramDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        args.DrawingSession.Clear(Colors.Transparent);
        var width = (float)sender.Size.Width;
        var height = (float)sender.Size.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var scaleX = width / AuthorCardWidth;
        var scaleY = height / AuthorCardHeight;
        var pointer = new Vector2(_authorPointer.X * scaleX, _authorPointer.Y * scaleY);
        if (_authorStrength > 0.001d)
        {
            DrawAuthorGlare(sender, args.DrawingSession, pointer, width, height);
        }

        DrawAuthorPattern(sender, args.DrawingSession, pointer, width, height);
    }

    private void DrawAuthorGlare(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawingSession drawingSession,
        Vector2 pointer,
        float width,
        float height)
    {
        var radius = Math.Clamp(Math.Min(width, height) * 0.42f, 105f, 170f);
        using var glareBrush = new CanvasRadialGradientBrush(
            resourceCreator,
            Color.FromArgb(34, 222, 241, 255),
            Color.FromArgb(0, 169, 135, 255))
        {
            Center = pointer,
            RadiusX = radius * 1.12f,
            RadiusY = radius * 0.92f,
            Opacity = (float)_authorStrength,
        };
        drawingSession.FillEllipse(
            pointer,
            glareBrush.RadiusX,
            glareBrush.RadiusY,
            glareBrush);
    }

    private void DrawAuthorPattern(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawingSession drawingSession,
        Vector2 pointer,
        float width,
        float height)
    {
        var center = new Vector2(width / 2f, height / 2f);
        var rotation = Matrix3x2.CreateRotation(
            AuthorPatternAngle * (MathF.PI / 180f),
            center);
        if (!Matrix3x2.Invert(rotation, out var inverseRotation))
        {
            return;
        }

        var patternPointer = Vector2.Transform(pointer, inverseRotation);
        var radius = Math.Clamp(Math.Min(width, height) * 0.42f, 105f, 170f);
        var stops = new CanvasGradientStop[]
        {
            new() { Position = 0f, Color = Color.FromArgb(242, 244, 255, 255) },
            new() { Position = 0.20f, Color = Color.FromArgb(224, 126, 222, 255) },
            new() { Position = 0.46f, Color = Color.FromArgb(190, 180, 145, 255) },
            new() { Position = 0.72f, Color = Color.FromArgb(92, 255, 170, 219) },
            new() { Position = 1f, Color = Color.FromArgb(0, 255, 170, 219) },
        };
        using var shineBrush = new CanvasRadialGradientBrush(resourceCreator, stops)
        {
            Center = patternPointer,
            RadiusX = radius,
            RadiusY = radius,
            Opacity = (float)_authorStrength,
        };

        var normalizedPointer = _authorPointerNormalized;
        var patternDrift = new Vector2(
            normalizedPointer.X * 12f * (float)_authorStrength,
            normalizedPointer.Y * 8f * (float)_authorStrength);
        var previousTransform = drawingSession.Transform;
        drawingSession.Transform = rotation;
        try
        {
            var row = 0;
            for (var y = -140f; y <= height + 140f; y += 50f)
            {
                var offset = row % 2 == 0 ? -80f : -17f;
                var column = 0;
                for (var x = offset; x <= width + 160f; x += 126f)
                {
                    var position = new Vector2(x, y) + patternDrift;
                    drawingSession.DrawText(
                        "Xikari",
                        position,
                        AuthorPatternColors[(row + column) % AuthorPatternColors.Length],
                        _authorPatternFormat);
                    if (_authorStrength > 0.001d)
                    {
                        drawingSession.DrawText(
                            "Xikari",
                            position,
                            shineBrush,
                            _authorPatternFormat);
                    }

                    column++;
                }

                row++;
            }
        }
        finally
        {
            drawingSession.Transform = previousTransform;
        }
    }

    private void LoadVersionInformation()
    {
        try
        {
            _appVersion = AppVersionProvider.GetCurrent();
            VersionText.Text = _appVersion.DisplayVersion;
            RuntimeText.Text = $"{_appVersion.RuntimeIdentifier} · {_appVersion.DistributionKind}";
            if (!string.IsNullOrWhiteSpace(_appVersion.CommitSha))
            {
                CommitText.Text = _appVersion.CommitSha[..8];
                CommitRow.Visibility = Visibility.Visible;
            }
        }
        catch (InvalidOperationException ex)
        {
            VersionText.Text = "版本信息无效";
            CheckUpdateButton.IsEnabled = false;
            ShowStatus(InfoBarSeverity.Error, "版本不可用", ex.Message);
        }
    }

    private async void OnCheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_appVersion is null)
        {
            return;
        }

        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetCheckingState(true);
        ReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusBar.IsOpen = false;

        try
        {
            var channel = UpdateChannelSelector.SelectedIndex == 1
                ? UpdateChannel.Preview
                : UpdateChannel.Stable;
            var result = await _updateService.CheckAsync(
                channel,
                _appVersion.Version,
                cancellation.Token);
            ApplyUpdateResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
        {
            ShowStatus(InfoBarSeverity.Error, "检查失败", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            {
                _updateCancellation = null;
                SetCheckingState(false);
            }
            cancellation.Dispose();
        }
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        _releaseUri = result.ReleaseUri;
        ReleaseButton.Visibility = result.ReleaseUri is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                ReleaseButtonText.Text = "查看新版本";
                ShowStatus(
                    InfoBarSeverity.Success,
                    "发现新版本",
                    $"{result.CurrentVersion.ToNormalizedString()} → {result.LatestVersion!.ToNormalizedString()}");
                break;
            case UpdateCheckStatus.UpToDate:
                ReleaseButtonText.Text = "查看当前版本";
                ShowStatus(
                    InfoBarSeverity.Success,
                    "已是最新版本",
                    result.CurrentVersion.ToNormalizedString());
                break;
            default:
                ShowStatus(
                    InfoBarSeverity.Informational,
                    "暂无公开发布",
                    "当前更新源没有可用的公开 Release。");
                break;
        }
    }

    private async void OnReleaseClicked(object sender, RoutedEventArgs e)
    {
        if (_releaseUri is not null)
        {
            await OpenUriAsync(_releaseUri);
        }
    }

    private async void OnProjectLinkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            await OpenUriAsync(uri);
        }
    }

    private async Task OpenUriAsync(Uri uri)
    {
        var opened = await Windows.System.Launcher.LaunchUriAsync(uri);
        if (!opened)
        {
            ShowStatus(InfoBarSeverity.Warning, "未能打开链接", uri.Host);
        }
    }

    private void OnUpdateChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        _updateCancellation?.Cancel();
        _releaseUri = null;
        ReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusBar.IsOpen = false;
    }

    private void SetCheckingState(bool checking)
    {
        CheckUpdateButton.IsEnabled = !checking && _appVersion is not null;
        UpdateChannelSelector.IsEnabled = !checking;
        UpdateProgressRing.IsActive = checking;
        UpdateProgressRing.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        UpdateStatusBar.Severity = severity;
        UpdateStatusBar.Title = title;
        UpdateStatusBar.Message = message;
        UpdateStatusBar.IsOpen = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyAuthorProjection();
        AuthorHologramCanvas.Invalidate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _authorPointerNormalized = Vector2.Zero;
        _authorStrength = 0d;
        _authorPointer = new Vector2(AuthorCardWidth / 2f, AuthorCardHeight / 2f);
        _updateCancellation?.Cancel();
    }
}
