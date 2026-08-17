using Microsoft.UI.Xaml.Media;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed record LaunchSettingsNavigationContext(
    MainPageViewModel ViewModel,
    ImageSource? CurrentBackgroundSource);
