using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

/// <summary>The Fluent "Shield" glyph (Segoe Fluent Icons U+EA18) that marks an action which asks for administrator permission (UAC).</summary>
internal static class ShieldIcon
{
    public const string Glyph = "\uEA18";

    public static Element Element(double size = 16) =>
        TextBlock(Glyph).FontFamily("Segoe Fluent Icons").FontSize(size).VAlign(VerticalAlignment.Center);
}
