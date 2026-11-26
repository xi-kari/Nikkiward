using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Launcher.Skins;

public sealed record HomeSkin(string Id, string Name, Func<UserControl> Create)
{
    public string Preview => $"ms-appx:///Assets/HomeSkins/{Id}.jpg";
    public Uri Tokens => new($"ms-appx:///Features/Launcher/Skins/{Id}/Tokens.xaml");
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
        Func<UserControl>? factory = null;
        AddCinema(ref factory);
        Register("Cinema", "熄灯放映");
        AddStitch(ref factory);
        Register("Stitch", "穿针引线");
        AddAtelier(ref factory);
        Register("Atelier", "高定画册");
        AddJournal(ref factory);
        Register("Journal", "搭配师的手账");
        AddWishStar(ref factory);
        Register("WishStar", "心愿星");
        return skins;

        void Register(string id, string name)
        {
            if (factory != null) skins.Add(new(id, name, factory));
            factory = null;
        }
    }

    static partial void AddCinema(ref Func<UserControl>? factory);
    static partial void AddStitch(ref Func<UserControl>? factory);
    static partial void AddAtelier(ref Func<UserControl>? factory);
    static partial void AddJournal(ref Func<UserControl>? factory);
    static partial void AddWishStar(ref Func<UserControl>? factory);
}
