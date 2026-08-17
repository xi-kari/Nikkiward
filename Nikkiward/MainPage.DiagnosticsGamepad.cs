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
    private async void OnExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                CommitButtonText = "选择导出目录",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(folder.Path))
            {
                await TryShowDialogAsync(
                    "无法使用所选位置",
                    "所选位置没有可供 System.IO 使用的本地文件系统路径。请选择本地文件夹后重试。");
                return;
            }

            var result = await ViewModel.ExportDiagnosticsAsync(
                folder.Path,
                _backdrop.DiagnosticState,
                _lifetimeCancellation?.Token ?? CancellationToken.None);

            if (result.Succeeded)
            {
                await TryShowDialogAsync(
                    "诊断已导出",
                    $"JSON：{result.JsonFilePath}\n\n文本：{result.TextFilePath}");
            }
            else
            {
                await TryShowDialogAsync("诊断导出失败", result.Error ?? "服务未返回错误详情。");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"导出失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("诊断导出失败", ViewModel.LastErrorText);
        }
    }

    private async void OnProviderValidationDetailsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var receiptText = ViewModel.ProviderValidationReceiptText;
            var receiptBox = new TextBox
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                IsReadOnly = true,
                MinWidth = 760,
                Text = receiptText,
                TextWrapping = TextWrapping.NoWrap,
            };
            var scrollViewer = new ScrollViewer
            {
                Content = receiptBox,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            await ShowDialogAsync(
                "Provider validation receipt · 只读详情",
                scrollViewer);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"验证事务详情显示失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("验证事务详情显示失败", ViewModel.LastErrorText);
        }
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        await ShowDialogAsync(
            title,
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
    }

    private async Task ShowDialogAsync(string title, object content)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = content,
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
            };

            await dialog.ShowAsync();
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private async Task TryShowDialogAsync(string title, string message)
    {
        try
        {
            await ShowDialogAsync(title, message);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"对话框显示失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void InitializeGamepad()
    {
        var settings = ViewModel.GamepadSettings;
        GamepadController.StateChanged += OnGamepadStateChanged;
        if (settings.Enabled)
        {
            StartGamepadController(settings);
        }
        else
        {
            GamepadController.Apply(settings);
        }

        ApplySettingsPageState(_hostedSettingsPage);
    }

    /// <summary>
    /// Initializes GameInput if needed, then pushes the current UI state. The
    /// stored Enabled flag is downgraded when initialization failed, so the
    /// Xbox Game Bar keeps the Guide button rather than losing it to a feature
    /// that cannot answer.
    /// </summary>
    private void StartGamepadController(GamepadSettings settings)
    {
        GamepadController.Initialize(
            DispatcherQueue,
            () => App.MainWindow.ShowByGamepad());
        GamepadController.Apply(settings with
        {
            Enabled = settings.Enabled && GamepadController.Initialized,
        });
    }

    private void OnGamepadStateChanged(object? sender, EventArgs e) =>
        ApplySettingsPageState(_hostedSettingsPage);

    private async void OnSettingsGamepadChanged(
        object? sender,
        GamepadSettingsChangedEventArgs e)
    {
        try
        {
            if (e.ChangeKind is GamepadSettingsChangeKind.Enabled && e.Settings.Enabled)
            {
                StartGamepadController(e.Settings);
            }
            else
            {
                GamepadController.Apply(e.Settings);
            }

            await ViewModel.SaveGamepadSettingsAsync(e.Settings);
            ApplySettingsPageState(_hostedSettingsPage);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"手柄设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnGamepadRedistDownloadClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(GamepadController.RuntimeDownloadUrl));
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"打开下载页面失败：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
