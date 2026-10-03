using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsCopyTests
{
    [AvaloniaFact]
    public async Task BrowseCopyPinsSyncSettingsAndSelectionHints()
    {
        await using var scene = new TipsTestScene();
        var card = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        var lines = card.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Parent is StackPanel { Name: "CopyLines" }).ToArray();
        Assert.Equal(new[]
        {
            "P pick, X reject, 1–5 rate, 6–9 label.",
            "Ctrl+click or Shift+click to select several.",
            "Ctrl+Shift+S syncs settings across the selection.",
            "E Loupe, C Compare, D Develop."
        }, lines.Select(line => string.Concat(line.Inlines!.OfType<Run>().Select(run => run.Text))));
        var keys = lines.SelectMany(line => line.Inlines!.OfType<Run>())
            .Where(run => new[] { "P", "X", "1–5", "6–9", "Ctrl+click", "Shift+click", "Ctrl+Shift+S", "E", "C", "D" }.Contains(run.Text));
        Assert.Equal(10, keys.Count());
        Assert.All(keys, run => Assert.Equal(
            ThemeResourceTests.Resource<FontFamily>("FontLabel", ThemeVariant.Dark), run.FontFamily));
    }

    [AvaloniaFact]
    public async Task TipsDoNotSuppressEmptyStatesOrTakeFocus()
    {
        await using var scene = new TipsTestScene();
        var browseCard = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        Assert.True(TipsLayoutTests.Named(scene.Window, "EmptyState").IsEffectivelyVisible);
        Assert.False(browseCard.Focusable);
        Assert.All(browseCard.GetVisualDescendants().OfType<Button>(), button => Assert.False(button.Focusable));
        var link = TipsTestScene.Action(browseCard, "Keyboard shortcuts");
        var label = Assert.Single(link.GetVisualDescendants().OfType<TextBlock>());
        Assert.Contains(label.TextDecorations!, decoration => decoration.Location == TextDecorationLocation.Underline);
        scene.Vm.IsDevelopMode = true;
        Assert.True(TipsTestScene.Card(scene.Window, WorkspaceMode.Develop).IsEffectivelyVisible);
        Assert.True(TipsLayoutTests.Named(scene.Window, "DevelopEmptyState").IsEffectivelyVisible);
    }
}
