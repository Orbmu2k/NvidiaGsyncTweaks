using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using NvidiaGsyncTweaks.Model;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

internal static class TweakPageView
{
    public static Element Build(Ctx c, TweakPage page) =>
        VStack(16,
            Ui.PageHeader(page.Title, page.Subtitle),
            VStack(16, page.Groups.Select(g => TweakRows.Group(c, g).WithKey(g.Title)).ToArray()));
}
