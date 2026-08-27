using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportReportCardTests
{
    [AvaloniaFact]
    public void DuplicateCustomLabels_StillRenderDistinctVersionNumbers()
    {
        var second = new ImageFile("photo.jpg")
        {
            Version = 2,
            VersionLabel = "B&W"
        };
        var third = new ImageFile("photo.jpg")
        {
            Version = 3,
            VersionLabel = "B&W"
        };
        var report = new ExportRunReport(
            "Export finished with warnings",
            "0 of 2 files exported.",
            [],
            [
                new ExportWarning(second, "first", "first warning"),
                new ExportWarning(third, "second", "second warning")
            ]);
        var card = new ExportReportCard
        {
            DataContext = new ReportHost(report)
        };
        card.IsVisible = true;
        var warningItems = card.GetLogicalDescendants()
            .OfType<ItemsControl>()
            .ElementAt(1);
        warningItems.ItemsSource = report.Warnings;
        warningItems.IsVisible = true;
        var window = new Window { Width = 600, Height = 400, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var versionLabels = card.GetLogicalDescendants()
                .OfType<TextBlock>()
                .SelectMany(block => block.Inlines?.OfType<Run>() ?? [])
                .Select(run => run.Text)
                .ToArray();
            Assert.Contains("V2 · B&W", versionLabels);
            Assert.Contains("V3 · B&W", versionLabels);
        }
        finally
        {
            window.Close();
        }
    }

    public sealed record ReportHost(ExportRunReport ExportReport)
    {
        public bool HasExportReport => true;
    }
}
