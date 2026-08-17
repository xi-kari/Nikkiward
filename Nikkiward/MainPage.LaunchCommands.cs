using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Diagnostics;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Profile;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Wish;
using Nikkiward.Models;
using Nikkiward.Pages;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async void OnOfficialFlowClicked(object sender, RoutedEventArgs e)
    {
        if (!await _manualLaunchGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
            var activation = await ViewModel.ActivateSelectedChannelAsync(cancellationToken);
            if (activation is not { Succeeded: true })
            {
                return;
            }

            if (!ViewModel.SelectedChannelUsesOfficialAssisted)
            {
                await ViewModel.StartSelectedExternalChannelAsync(cancellationToken);
                return;
            }

            var preparation = await ViewModel.PrepareOfficialAssistedLaunchAsync(
                cancellationToken);
            if (!preparation.Succeeded || preparation.Plan is null)
            {
                return;
            }

            await ViewModel.StartPreparedOfficialAssistedLaunchAsync(preparation);
        }
        catch (OperationCanceledException)
        {
            ViewModel.ReportOfficialAssistedLaunchNotStarted(
                "Runtime.Cancelled",
                "页面生命周期取消了本次操作。",
                isError: false);
        }
        catch (Exception ex)
        {
            ViewModel.ReportOfficialAssistedLaunchNotStarted(
                "Runtime.UnexpectedUiError",
                $"{ex.GetType().Name}: {ex.Message}",
                isError: true);
        }
        finally
        {
            _manualLaunchGate.Release();
        }
    }

    private async void OnCloseGameClicked(object sender, RoutedEventArgs e)
    {
        if (!await _manualLaunchGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await ViewModel.CloseOfficialAssistedGameAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"关闭游戏失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _manualLaunchGate.Release();
        }
    }

    private async void OnLaunchStateTimerTick(object? sender, object e)
    {
        if (Interlocked.Exchange(ref _launchStateRefreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            await ViewModel.RefreshOfficialAssistedRuntimeAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"游戏状态刷新失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _launchStateRefreshInProgress, 0);
        }
    }

    private async void OnDownloadStatusClicked(object sender, RoutedEventArgs e)
    {
        await TryShowDialogAsync(
            "下载状态 · 只读",
            $"{ViewModel.DownloadStatusText}\n{ViewModel.DownloadStatusDetailText}\n\nNikkiward 不修改 Steam appmanifest/ACF；三渠道单本体只通过 Profile 页的冻结 manifest 和 NTFS 硬链接物化。");
    }

    private async void OnPlayTimeClicked(object sender, RoutedEventArgs e)
    {
        await TryShowDialogAsync(
            "奇想手账 · 游戏时长",
            $"{_journalDurationText}\n{_journalDurationDetailText}\n\n时长来自官方奇想手账页面的只读快照；WebView2 负责登录会话，Nikkiward 不读取或导出 cookie、密码、token、localStorage，也不会为了统计时长启动目标进程。");
    }

    private async void OnServiceShortcutClicked(object sender, RoutedEventArgs e)
    {
        await OpenJournalAsync();
    }

    private async void OnResonanceOpenClicked(object sender, RoutedEventArgs e)
    {
        await OpenResonanceJournalAsync();
    }

    private async Task OpenResonanceJournalAsync()
    {
        try
        {
            SetShellNavigationSelection(LibraryNavigationItem);
            ShowLibrary();
            var journalPage = ContentFrame.Content as JournalPage
                ?? throw new InvalidOperationException("Journal page navigation did not complete.");
            _journalRouteIntent = JournalRouteIntent.ResonanceHistory;
            var alreadyOpen = IsResonanceHistoryUri(journalPage.CurrentUri);
            await journalPage.ShowBrowserAsync(
                alreadyOpen ? null : JournalPage.ResonanceUri);
            if (alreadyOpen)
            {
                journalPage.SetBrowserStatus("共鸣衣橱已打开；正在重新整理历史。");
                await SyncResonanceHistoryAsync(isAutomatic: false);
            }
            else
            {
                journalPage.SetBrowserStatus("正在打开共鸣衣橱。");
            }
        }
        catch (Exception ex)
        {
            _hostedWishPage?.SetStatus("打开失败");
            ViewModel.ReportUiError(
                $"共鸣衣橱打开失败：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
