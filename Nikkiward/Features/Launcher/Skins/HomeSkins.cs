using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Launcher.Skins;

public sealed record HomeSkin(string Id, string Name, Func<UserControl> Create)
{
    public string Preview => $"ms-appx:///Web/art/HomeSkins/{Id}.jpg";
    public Uri Tokens => new($"ms-appx:///Features/Launcher/Skins/{(Id == "Atelier" ? Id : "Cinema")}/Tokens.xaml");
    public bool LeftEdge => Id == "Journal";
}

public static partial class HomeSkins
{
    public static IReadOnlyList<HomeSkin> Available { get; } = Build();

    public static HomeSkin Resolve(string? id) =>
        Find(
#if DEBUG
            Nikkiward.ViewModels.HomePreview.Skin ??
#endif
            id);

    private static HomeSkin? _active;
    private static ResourceDictionary? _tokens;

    internal static ResourceDictionary TokensFor(HomeSkin skin)
    {
        if (_active == skin) return _tokens!;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = skin.Tokens };
        if (_tokens != null) dictionaries.Remove(_tokens);
        dictionaries.Add(next);
        _active = skin;
        return _tokens = next;
    }

    private static HomeSkin Find(string? id) =>
        Available.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Available[0];

    private static List<HomeSkin> Build()
    {
        var skins = new List<HomeSkin>();
        Register("Cinema", "熄灯放映");
        Register("Stitch", "穿针引线");
        Register("Atelier", "高定画册");
        Register("Journal", "搭配师的手账");
        Register("WishStar", "心愿星");
        return skins;

        void Register(string id, string name)
        {
            var folder = id.ToLowerInvariant();
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Web", "skins", folder, "index.html")))
                skins.Add(new(id, name, () => new WebHomeView(folder)));
            else if (id == "Cinema")
                skins.Add(new(id, name, () => new CinemaView()));
        }
    }
}
