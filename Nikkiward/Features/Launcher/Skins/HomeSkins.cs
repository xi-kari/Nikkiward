using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Launcher.Skins;

public sealed record HomeSkin(string Id, string Name, Func<UserControl> Create);

public static partial class HomeSkins
{
    public static IReadOnlyList<HomeSkin> Available { get; } = Build();

    public static HomeSkin Resolve(string? id) =>
        Find(
#if DEBUG
            Nikkiward.ViewModels.HomePreview.Skin ??
#endif
            id);

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
            skins.Add(new(id, name, () => new WebHomeView(folder)));
        }
    }
}
