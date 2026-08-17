using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Settings;

public sealed record GallerySettingsViewState(
    string ProfileText,
    string GalleryRootText,
    string RootModeText,
    bool CanChooseRoot,
    bool CanResetRoot,
    bool CanOpenGallery,
    bool ProtectionEnabled,
    string ProtectionStatusText,
    string ProtectionPathText,
    bool CanChangeProtection,
    bool CanOpenProtectionRoot,
    bool CanVerifyProtection,
    bool CanCleanProtection,
    string CacheStatusText,
    string CachePathText,
    bool CanRefreshCache,
    bool CanClearCache,
    string NikkiGalleryStatusText,
    string NikkiGalleryPathText,
    bool CanRegisterNikkiGallery,
    bool CanOpenNikkiGallery,
    bool CanDisconnectNikkiGallery,
    bool IsBusy);

public sealed class GalleryProtectionEnabledChangedEventArgs : EventArgs
{
    public GalleryProtectionEnabledChangedEventArgs(bool enabled)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; }
}

public sealed partial class GallerySettingsView : UserControl
{
    private bool _applyingState;

    public event EventHandler? ChooseRootRequested;
    public event EventHandler? ResetRootRequested;
    public event EventHandler? OpenGalleryRequested;
    public event EventHandler<GalleryProtectionEnabledChangedEventArgs>? ProtectionEnabledChanged;
    public event EventHandler? ChooseProtectionRootRequested;
    public event EventHandler? OpenProtectionRootRequested;
    public event EventHandler? VerifyProtectionRequested;
    public event EventHandler? CleanProtectionRequested;
    public event EventHandler? RefreshCacheRequested;
    public event EventHandler? ClearCacheRequested;
    public event EventHandler? RegisterNikkiGalleryRequested;
    public event EventHandler? OpenNikkiGalleryRequested;
    public event EventHandler? DisconnectNikkiGalleryRequested;

    public GallerySettingsView()
    {
        InitializeComponent();
    }

    public void ApplyState(GallerySettingsViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _applyingState = true;
        try
        {
            ProfileText.Text = state.ProfileText;
            GalleryRootTextBox.Text = state.GalleryRootText;
            RootModeText.Text = state.RootModeText;
            ChooseRootButton.IsEnabled = state.CanChooseRoot;
            ResetRootButton.IsEnabled = state.CanResetRoot;
            OpenGalleryButton.IsEnabled = state.CanOpenGallery;
            ProtectionEnabledToggle.IsOn = state.ProtectionEnabled;
            ProtectionEnabledToggle.IsEnabled = state.CanChangeProtection;
            ProtectionStatusText.Text = state.ProtectionStatusText;
            ProtectionPathTextBox.Text = state.ProtectionPathText;
            ChooseProtectionRootButton.IsEnabled = state.CanChangeProtection;
            OpenProtectionRootButton.IsEnabled = state.CanOpenProtectionRoot;
            VerifyProtectionButton.IsEnabled = state.CanVerifyProtection;
            CleanProtectionButton.IsEnabled = state.CanCleanProtection;
            CacheStatusText.Text = state.CacheStatusText;
            CachePathTextBox.Text = state.CachePathText;
            RefreshCacheButton.IsEnabled = state.CanRefreshCache;
            ClearCacheButton.IsEnabled = state.CanClearCache;
            NikkiGalleryStatusText.Text = state.NikkiGalleryStatusText;
            NikkiGalleryPathTextBox.Text = state.NikkiGalleryPathText;
            RegisterNikkiGalleryButton.IsEnabled = state.CanRegisterNikkiGallery;
            OpenNikkiGalleryButton.IsEnabled = state.CanOpenNikkiGallery;
            DisconnectNikkiGalleryButton.IsEnabled = state.CanDisconnectNikkiGallery;
            BusyRing.IsActive = state.IsBusy;
            BusyRing.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _applyingState = false;
        }
    }

    private void OnChooseRootClicked(object sender, RoutedEventArgs e) => ChooseRootRequested?.Invoke(this, EventArgs.Empty);
    private void OnResetRootClicked(object sender, RoutedEventArgs e) => ResetRootRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenGalleryClicked(object sender, RoutedEventArgs e) => OpenGalleryRequested?.Invoke(this, EventArgs.Empty);
    private void OnProtectionEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (!_applyingState)
        {
            ProtectionEnabledChanged?.Invoke(
                this,
                new GalleryProtectionEnabledChangedEventArgs(ProtectionEnabledToggle.IsOn));
        }
    }
    private void OnChooseProtectionRootClicked(object sender, RoutedEventArgs e) => ChooseProtectionRootRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenProtectionRootClicked(object sender, RoutedEventArgs e) => OpenProtectionRootRequested?.Invoke(this, EventArgs.Empty);
    private void OnVerifyProtectionClicked(object sender, RoutedEventArgs e) => VerifyProtectionRequested?.Invoke(this, EventArgs.Empty);
    private void OnCleanProtectionClicked(object sender, RoutedEventArgs e) => CleanProtectionRequested?.Invoke(this, EventArgs.Empty);
    private void OnRefreshCacheClicked(object sender, RoutedEventArgs e) => RefreshCacheRequested?.Invoke(this, EventArgs.Empty);
    private void OnClearCacheClicked(object sender, RoutedEventArgs e) => ClearCacheRequested?.Invoke(this, EventArgs.Empty);
    private void OnRegisterNikkiGalleryClicked(object sender, RoutedEventArgs e) => RegisterNikkiGalleryRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenNikkiGalleryClicked(object sender, RoutedEventArgs e) => OpenNikkiGalleryRequested?.Invoke(this, EventArgs.Empty);
    private void OnDisconnectNikkiGalleryClicked(object sender, RoutedEventArgs e) => DisconnectNikkiGalleryRequested?.Invoke(this, EventArgs.Empty);
}
