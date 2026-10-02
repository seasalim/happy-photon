using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseReviewTypographyTests
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task SelectionCardIsAbsent(ThemeVariant theme)
    {
        using var fixture = new CatalogVmFixture("wp8-review-type");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        var images = Enumerable.Range(1, 3).Select(index =>
            new ImageFile(fixture.Path($"cloud-{index}.jpg"), SourceAvailability.RequiresHydration)).ToArray();
        vm.Browse.SetImages(images);
        var pane = new BrowseReviewPane { DataContext = vm };
        using var scope = new TestUiScope(new Window { Content = pane, Width = 260, Height = 700 }, theme);
        vm.ToggleImageSelection(images[0]);

        for (var count = 1; count <= 3; count++)
        {
            if (count > 1)
            {
                vm.ToggleImageSelection(images[count - 1]);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.Null(pane.FindControl<Border>("SelectionSummaryPanel"));
            Assert.Null(pane.FindControl<TextBlock>("ReviewSelectionCountText"));
            Assert.Null(pane.FindControl<TextBlock>("ReviewSelectionOnlineOnlyText"));
        }
    }
}
