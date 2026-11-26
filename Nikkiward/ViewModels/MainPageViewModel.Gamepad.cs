using Nikkiward.Models;

namespace Nikkiward.ViewModels;

public sealed partial class MainPageViewModel
{
    internal async Task SaveAndApplyGamepadAsync(GamepadSettings settings, Action<GamepadSettings> apply,
        CancellationToken cancellationToken)
    {
        await SaveGamepadSettingsAsync(settings, cancellationToken);
        apply(GamepadSettings);
    }
}
