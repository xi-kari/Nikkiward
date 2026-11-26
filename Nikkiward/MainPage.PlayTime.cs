using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly PlayTimeStore _playTimeStore = new(ApplicationDataPaths.Root);
    private readonly SemaphoreSlim _playTimeGate = new(1, 1);
    private PlayTimeLedger? _playTime;
    private DispatcherTimer? _playTimeTimer;
    private string? _playTimeError;
    private int _playTimeGeneration;

    private async Task InitializePlayTimeAsync(CancellationToken token)
    {
        if (_playTime is null && _playTimeError is null)
        {
            try
            {
                var history = await Task.Run(_playTimeStore.Load, token);
                token.ThrowIfCancellationRequested();
                _playTime = new(history);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetPlayTimeError(ex);
            }
        }
        UpdatePlayTimeTimer();
    }

    private void UpdatePlayTimeTimer(bool fromSettings = false)
    {
        if (_playTime != null && _playTimeError is null && ViewModel.GeneralSettings.RecordLocalPlayTime && IsLoaded)
        {
            if (_playTimeTimer is null)
            {
                _playTimeTimer = new DispatcherTimer { Interval = PlayTimeLedger.Period };
                _playTimeTimer.Tick += OnPlayTimeTick;
            }
            if (!_playTimeTimer.IsEnabled)
            {
                if (fromSettings) _playTime.Disconnect(DateTimeOffset.UtcNow);
                _playTimeTimer.Start();
                OnPlayTimeTick(this, EventArgs.Empty);
            }
        }
        else
        {
            StopPlayTime();
        }
        RefreshPlayTimeViews();
    }

    private void StopPlayTime()
    {
        if (_playTimeTimer?.IsEnabled != true) return;
        _playTimeTimer.Stop();
        _playTimeGeneration++;
        _playTime?.Disconnect(DateTimeOffset.UtcNow);
    }

    private async void OnPlayTimeTick(object? sender, object e)
    {
        if (_playTime is null || _playTimeTimer?.IsEnabled != true || !_playTimeGate.Wait(0)) return;
        try
        {
            var generation = _playTimeGeneration;
            var installs = ViewModel.GetPlayTimeInstallations();
            var (observedUtc, games) = installs.Count == 0
                ? (DateTimeOffset.UtcNow, new List<ObservedGame>())
                : await Task.Run(ObserveGames);
            if (generation != _playTimeGeneration) return;
            if (_playTime.Observe(observedUtc, games, installs))
            {
                await Task.Run(() => _playTimeStore.Save(_playTime.Sessions));
            }
            RefreshPlayTimeViews();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            SetPlayTimeError(ex);
        }
        finally
        {
            _playTimeGate.Release();
        }
    }

    private static (DateTimeOffset, List<ObservedGame>) ObserveGames()
    {
        var games = new List<ObservedGame>();
        foreach (var process in Process.GetProcessesByName("X6Game-Win64-Shipping"))
        {
            using (process)
            {
                if (OfficialAssistedLaunchCoordinator.TryQueryProcessPath(process.Id, out var path, out var created))
                {
                    games.Add(new(path, process.Id, created));
                }
            }
        }
        return (DateTimeOffset.UtcNow, games);
    }

    private async void OnClearPlayTimeRequested(object? sender, EventArgs e)
    {
        if (!_dialogGate.Wait(0)) return;
        try
        {
            if (XamlRoot is null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "清除本机游玩记录？",
                Content = "将删除所有渠道的本机游玩记录。清除后不可恢复。",
                PrimaryButtonText = "清除记录",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"清除确认框显示失败：{ex.Message}");
            return;
        }
        finally
        {
            _dialogGate.Release();
        }

        await _playTimeGate.WaitAsync();
        try
        {
            if (_playTime is null) return;
            await Task.Run(_playTimeStore.Clear);
            _playTime = new([]);
            _playTime.Disconnect(DateTimeOffset.UtcNow);
            _playTimeError = null;
            UpdatePlayTimeTimer();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetPlayTimeError(ex);
        }
        finally
        {
            _playTimeGate.Release();
        }
    }

    private void SetPlayTimeError(Exception ex)
    {
        _playTimeError = $"本机记录已暂停：{ex.Message}";
        StopPlayTime();
        RefreshPlayTimeViews();
    }

    private void RefreshPlayTimeViews()
    {
        var sessions = _playTime?.Sessions ?? [];
        _hostedSettingsPage?.ApplyPlayTimeState(sessions.Count > 0, _playTimeError);
        _hostedJournalPage?.ApplyPlayTime(ViewModel.GeneralSettings.RecordLocalPlayTime, sessions, _playTimeError);
    }
}
