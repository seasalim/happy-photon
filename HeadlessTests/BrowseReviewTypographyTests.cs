using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
    public async Task SelectionCountsUseMonoAndWordsUseBody(ThemeVariant theme)
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

            await vm.WaitForBrowseSelectionSummaryAsync();
            Dispatcher.UIThread.RunJobs();
            AssertRuns("ReviewSelectionCountText", count, " photos selected");
            AssertRuns("ReviewSelectionOnlineOnlyText", count,
                count == 1 ? " online-only photo excluded" : " online-only photos excluded");

            if (count == 2)
            {
                ShowcaseTestHelper.Capture("wp8-review-selection-" +
                    (theme == ThemeVariant.Dark ? "dark" : "gray"), scope, new PixelSize(260, 700), theme);
            }
        }

        void AssertRuns(string name, int count, string words)
        {
            var runs = pane.FindControl<TextBlock>(name)!.Inlines!.OfType<Run>().ToArray();
            Assert.Equal(count.ToString("N0") + words, string.Concat(runs.Select(run => run.Text)));
            Assert.Equal(count.ToString("N0"), runs[0].Text);
            Assert.Equal(ThemeResourceTests.Resource<FontFamily>("FontLabel", theme), runs[0].FontFamily);
            Assert.All(runs.Skip(1), run =>
                Assert.Equal(ThemeResourceTests.Resource<FontFamily>("FontBody", theme), run.FontFamily));
        }
    }
}
