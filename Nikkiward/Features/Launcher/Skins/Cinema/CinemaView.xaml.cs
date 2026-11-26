using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher.Skins;

public sealed partial class CinemaView : UserControl, IDisposable
{
    private readonly DispatcherTimer _idle = new() { Interval = IdleDelay };
    public MainPageViewModel Model => (MainPageViewModel)DataContext;
    private LauncherPage? _host;
    private MainPage? _shell;
    private long _lastInput;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(8);
    private bool _dimmed;
    private LaunchButtonState? _lastState;

    public CinemaView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? p = this; p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is LauncherPage host) _host = host;
            if (p is MainPage shell) { _shell = shell; break; }
        }
        Photos.ItemsSource = _host?.RecentPhotos;
        if (_host != null) _host.RecentPhotos.CollectionChanged += OnPhotosChanged;
        if (_shell == null) return;
        _shell.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointer), true);
        _shell.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointer), true);
        _shell.PreviewKeyDown += OnKey;
        _shell.HomeInteractionChanged += OnInteraction;
        Model.PropertyChanged += OnModelChanged;
        GlassCapabilities.Current.TierChanged += OnInteraction;
        _idle.Tick += OnIdle;
        Refresh();
        Wake();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Model.LaunchButtonState) or nameof(Model.AppearanceSettings) or nameof(Model.HasHomeTask))
        {
            Refresh();
            if (Model.HomeState != _lastState || e.PropertyName == nameof(Model.AppearanceSettings))
            {
                _lastState = Model.HomeState;
                Wake();
            }
        }
    }

    private void OnPhotosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (DataContext is not MainPageViewModel vm) return;
        bool blocked = vm.HomeState is not (LaunchButtonState.Ready or LaunchButtonState.Launching or LaunchButtonState.Running);
        Time.Visibility = Visible(ActualWidth >= 1000 && !blocked);
        TaskColumn.Visibility = Visible(ActualWidth >= 1100 && vm.HasHomeTask && !blocked);
        CheckTime.Visibility = Visible(ActualWidth >= 920);
        Recent.Visibility = Visible(ActualWidth >= 840 && _host?.RecentPhotos.Count > 0);
        ArtLabel.Visibility = Visible(ActualWidth >= 760);
        bool closeVisible = vm.HomeCloseVisibility == Visibility.Visible;
        bool wide = ActualWidth >= (closeVisible ? 1510 : 1430);
        Facts.Spacing = wide ? 36 : 12;
        Art.Width = wide ? 244 : ActualWidth >= 760 ? 155 : 138;
        ArtTitle.MaxWidth = Art.Width - 64;
        ProfileColumn.Width = wide ? 196 : 134;
        CheckColumn.Width = wide ? 150 : 138;
        Time.Width = wide ? 142 : 128;
        TaskColumn.Width = wide ? 150 : 105;
        Launch.MinWidth = wide ? 248 : 190;
        Play.Visibility = Visible(vm.HomeState == LaunchButtonState.Ready);
        LaunchLabel.FontSize = blocked && !vm.HomeBusy ? 17 : 20;
        LaunchLabel.CharacterSpacing = blocked && !vm.HomeBusy ? 60 : 120;
        ReadyDot.Visibility = Visible(vm.HomeState == LaunchButtonState.Ready);
        WarnDot.Visibility = Visible(blocked && !vm.HomeBusy);
        NeutralDot.Visibility = Visible(ReadyDot.Visibility == Visibility.Collapsed && WarnDot.Visibility == Visibility.Collapsed);
        bool stacked = ActualWidth < (blocked ? 1020 : closeVisible ? 920 : 780);
        Grid.SetColumnSpan(Facts, stacked ? 3 : 1);
        Grid.SetRow(Reason, stacked ? 1 : 0);
        Grid.SetColumn(Reason, stacked ? 0 : 1);
        Grid.SetColumnSpan(Reason, stacked ? 3 : 1);
        Grid.SetRow(Actions, stacked ? 2 : 0);
        Grid.SetColumn(Actions, stacked ? 0 : 2);
        Grid.SetColumnSpan(Actions, stacked ? 3 : 1);
        Reason.Visibility = Visible(blocked);
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void OnPointer(object sender, PointerRoutedEventArgs e)
    {
        if (_dimmed && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) e.Handled = true;
        Wake();
    }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (_dimmed) e.Handled = true;
        Wake();
    }
    private void OnInteraction(object? sender, EventArgs e) => Wake();

    private bool CanDim()
    {
        if (_shell?.HomeInteractive != true || !Model.HomeState.AllowsHomeIdle()) return false;
        var signals = GlassCapabilities.Current.ReadSignals();
        return signals.Motion == AppearanceMotionMode.Full && signals.AnimationsEnabled && signals.AdvancedEffectsEnabled &&
            !signals.HighContrast && !signals.LowFrameRateMeasured && !signals.EnergySaverOn && !signals.RemoteSession && !signals.WindowOccluded;
    }

    private void Wake()
    {
#if DEBUG
        if (HomePreview.Active)
        {
            bool dim = HomePreview.Idle && Model.HomeState.AllowsHomeIdle() && _shell?.HomeInteractive == true;
            if (_dimmed != dim)
            {
                SetDimmed(dim, immediate: true);
            }
            return;
        }
#endif
        _lastInput = Environment.TickCount64;
        if (_dimmed) SetDimmed(false);
        if (!_idle.IsEnabled && _shell?.HomeInteractive == true && Model.HomeState.AllowsHomeIdle())
        {
            _idle.Interval = IdleDelay;
            _idle.Start();
        }
    }

    private void OnIdle(object? sender, object e)
    {
        _idle.Stop();
        if (!CanDim()) return;
        var remaining = IdleDelay - TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastInput);
        if (remaining <= TimeSpan.Zero) SetDimmed(true);
        else
        {
            _idle.Interval = remaining;
            _idle.Start();
        }
    }

    private void SetDimmed(bool value, bool immediate = false)
    {
        _dimmed = value;
        Exhibit.IsHitTestVisible = !value;
        var transition = !immediate && CanDim() ? TimeSpan.FromMilliseconds(800) : TimeSpan.Zero;
        if (immediate)
        {
            Exhibit.Visibility = Visible(!value);
        }
        else
        {
            Exhibit.TranslationTransition = new Vector3Transition { Duration = transition };
            Exhibit.Translation = value ? new Vector3(0, (float)Exhibit.ActualHeight + 20, 0) : Vector3.Zero;
        }
        TopScrim.OpacityTransition = new ScalarTransition { Duration = transition };
        TopScrim.Opacity = value ? 0 : 1;
        IdlePlate.Visibility = Visible(value);
        _shell?.SetHomeDimmed(value);
    }

    public void Dispose()
    {
        _idle.Stop();
        if (_shell == null) return;
        _idle.Tick -= OnIdle;
        _shell.RemoveHandler(PointerMovedEvent, new PointerEventHandler(OnPointer));
        _shell.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnPointer));
        _shell.PreviewKeyDown -= OnKey;
        _shell.HomeInteractionChanged -= OnInteraction;
        Model.PropertyChanged -= OnModelChanged;
        GlassCapabilities.Current.TierChanged -= OnInteraction;
        if (_dimmed) SetDimmed(false);
        if (_host != null) _host.RecentPhotos.CollectionChanged -= OnPhotosChanged;
        Photos.ItemsSource = null;
        _host = null;
        _shell = null;
    }
}
