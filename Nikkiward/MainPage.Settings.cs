using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private string _settingsInitial = "appearance";

    private void ShowSettingsPage(string section = "appearance")
    {
        _settingsInitial = section;
        ShowWebPage("settings");
    }

    private GamepadRuntimeViewState CreateGamepadRuntimeState()
    {
        string status;
        if (!ViewModel.GamepadSettings.Enabled)
        {
            status = "已禁用；导航键与分享键交回系统处理。";
        }
        else if (!GamepadController.Initialized)
        {
            status = GamepadController.InitializationError is { Length: > 0 } error
                ? $"初始化失败：{error}"
                : "初始化失败；手柄增强未生效。";
        }
        else
        {
            status = GamepadController.GamepadConnected
                ? "已启用；检测到手柄。"
                : "已启用；未检测到手柄，连接后自动生效。";
        }

        var protection = App.Settings.ProtectionMessage;
        return new GamepadRuntimeViewState(protection ?? status, GamepadController.RuntimeMissing, protection is null);
    }
}
