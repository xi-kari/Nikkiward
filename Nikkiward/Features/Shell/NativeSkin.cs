using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Nikkiward;

internal sealed class NativeSkin
{
    private static readonly Dictionary<string, Dictionary<string, string>> Rules = ReadRules();
    private static NativeSkin? _current;
    private static event Action? Changed;
    private readonly string _skin, _tone;
    private readonly SolidColorBrush _paper, _ink, _accent, _accentInk;
    private readonly FontFamily _bodyFont, _titleFont;
    private readonly CornerRadius _corner;
    private readonly ElementTheme _theme;

    private NativeSkin(string skin, string tone)
    {
        _skin = skin;
        _tone = tone;
        var shared = Rules[skin];
        var colors = Rules[skin + "/" + tone];
        string Token(string name) => colors.GetValueOrDefault(name) ?? shared[name];
        _paper = Brush(Token("panel-bg"));
        _ink = Brush(Token("panel-ink"));
        _accent = Brush(Token("panel-accent"));
        _accentInk = Brush(Token("panel-accent-ink"));
        _theme = Brightness(_paper.Color) > Brightness(_ink.Color) ? ElementTheme.Light : ElementTheme.Dark;
        _bodyFont = new FontFamily(Token("font-ui").Replace("'", ""));
        _titleFont = new FontFamily(Token("font-display").Replace("'", ""));
        var radii = Token("panel-radius").Split(' ').Select(v => double.Parse(v.Replace("px", ""), CultureInfo.InvariantCulture)).ToArray();
        _corner = radii.Length == 1 ? new CornerRadius(radii[0]) : new CornerRadius(radii[0], radii[1], radii[2], radii[3]);
    }

    private static int Brightness(Color color) => 299 * color.R + 587 * color.G + 114 * color.B;

    // Only the five packaged skin rules are inputs. Native surfaces use opaque backgrounds.
    private static Dictionary<string, Dictionary<string, string>> ReadRules()
    {
        using var reader = new StreamReader(typeof(NativeSkin).Assembly.GetManifestResourceStream("Nikkiward.NativeTheme.css")!);
        var rules = new Dictionary<string, Dictionary<string, string>>();
        foreach (Match match in Regex.Matches(reader.ReadToEnd(), @"\[data-skin=(\w+)\](?:\[data-tone=(\w+)\])?\s*\{\s*(--nk-[^}]+)\}"))
        {
            var key = match.Groups[1].Value + (match.Groups[2].Success ? "/" + match.Groups[2].Value : "");
            rules[key] = Regex.Matches(match.Groups[3].Value, @"--nk-([\w-]+):([^;]+)")
                .ToDictionary(t => t.Groups[1].Value, t => t.Groups[2].Value.Trim());
        }
        return rules;
    }

    private static SolidColorBrush Brush(string value)
    {
        if (value.StartsWith("rgba("))
        {
            var rgb = value[5..^1].Split(',');
            return new(Color.FromArgb(255, byte.Parse(rgb[0]), byte.Parse(rgb[1]), byte.Parse(rgb[2])));
        }
        var hex = value[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        var n = Convert.ToInt32(hex, 16);
        return new(Color.FromArgb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n));
    }

    internal static void Select(string skin, string tone)
    {
        if (_current?._skin == skin && _current._tone == tone) return;
        _current = new(skin, tone);
        Changed?.Invoke();
    }

    internal static void Refresh() => Changed?.Invoke();

    internal static void Bind(Control control, TextBlock? title = null) =>
        Observe(control, () => _current?.Apply(control, title));

    internal static void Observe(FrameworkElement element, Action update)
    {
        element.Loaded += (_, _) =>
        {
            Changed += update;
            update();
        };
        element.Unloaded += (_, _) => Changed -= update;
    }

    private void Apply(Control control, TextBlock? title)
    {
        if (new AccessibilitySettings().HighContrast)
        {
            var system = new UISettings();
            control.Background = new SolidColorBrush(system.GetColorValue(UIColorType.Background));
            control.Foreground = new SolidColorBrush(system.GetColorValue(UIColorType.Foreground));
            control.ClearValue(FrameworkElement.RequestedThemeProperty);
            foreach (var key in new[] { "ContentDialogBackground", "ContentDialogForeground",
                "AccentButtonBackground", "AccentButtonForeground", "AccentButtonBackgroundPointerOver",
                "AccentButtonForegroundPointerOver", "AccentButtonBackgroundPressed", "AccentButtonForegroundPressed" })
                if (control.Resources.ContainsKey(key)) control.Resources.Remove(key);
            return;
        }
        control.Background = _paper;
        control.Foreground = _ink;
        control.FontFamily = _bodyFont;
        if (title != null) title.FontFamily = _titleFont;
        var r = control.Resources;
        r["ControlCornerRadius"] = _corner;
        r["OverlayCornerRadius"] = _corner;
        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
        {
            r["AccentButtonBackground" + suffix] = _accent;
            r["AccentButtonForeground" + suffix] = _accentInk;
        }
        if (control is ContentDialog dialog)
        {
            r["ContentDialogBackground"] = _paper;
            r["ContentDialogForeground"] = _ink;
            dialog.CornerRadius = _corner;
        }
        control.RequestedTheme = _theme;
    }

    internal static Windows.Foundation.IAsyncOperation<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        Bind(dialog);
        _current?.Apply(dialog, null);
        return dialog.ShowAsync();
    }

    internal static Brush? Paper => _current?._paper;
    internal static bool Light => _current?._theme == ElementTheme.Light;
}
