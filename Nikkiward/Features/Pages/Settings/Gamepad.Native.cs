using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private bool _publishingGamepadConnection;
    private readonly SemaphoreSlim _webGamepadGate = new(1, 1);

    private PageAdapter GamepadPageSection() => new("Gamepad", GamepadSection.Actions, () => {
        var runtime = CreateGamepadRuntimeState();
        return GamepadSection.State(ViewModel.GamepadSettings, runtime.StatusText, runtime.CanEdit, runtime.RuntimeMissing);
    }, async (op, m) => {
        if (op == "runtime" && GamepadController.RuntimeMissing)
        {
            await OpenSettingsLinkAsync(new Uri(GamepadController.RuntimeDownloadUrl));
            return;
        }
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        await _webGamepadGate.WaitAsync(token);
        try
        {
            if (op != "change" || !CreateGamepadRuntimeState().CanEdit) throw SettingsValues.Invalid();
            var field = SettingsValues.Field(m, "field");
            var next = GamepadSection.Change(ViewModel.GamepadSettings, field, SettingsValues.Field(m, "value"), keys =>
                GamepadKeyNames.TryParse(keys, out var result, out _, out _) ? result :
                    throw new InvalidDataException($"无法识别按键“{result}”。多个按键请用空格分隔。"));
            await ViewModel.SaveAndApplyGamepadAsync(next, settings => {
                if (settings.Enabled && field == "enabled") StartGamepadController(settings);
                else GamepadController.Apply(settings);
            }, token);
        }
        finally { _webGamepadGate.Release(); }
    });
}
