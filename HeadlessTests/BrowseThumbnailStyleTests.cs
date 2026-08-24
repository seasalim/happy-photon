using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseThumbnailStyleTests
{
    // The tile styles live in their own file so BrowseGridView.axaml stays
    // within the repository line limit. A StyleInclude that stops resolving
    // fails silently, leaving unstyled tiles, so pin the setters here.
    [AvaloniaFact]
    public void IncludedThumbnailStyles_ReachTheRealizedTile()
    {
        var image = new ImageFile(Path.Combine(Path.GetTempPath(), "styled.jpg"));
        var control = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile> { image }
        };
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tile = Assert.Single(
            control.GetVisualDescendants().OfType<Border>(),
            border => ReferenceEquals(border.DataContext, image) &&
                      border.Classes.Contains("thumbnail"));
        Assert.Equal(new CornerRadius(14), tile.CornerRadius);
        Assert.Equal(new Thickness(2), tile.BorderThickness);
        Assert.NotNull(tile.Background);
        Assert.NotNull(tile.Transitions);

        var badge = Assert.Single(
            control.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("check-badge"));
        Assert.Equal(0d, badge.Opacity);

        window.Close();
    }
}
