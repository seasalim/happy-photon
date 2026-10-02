using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreShowcaseTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task StartupErrorRestore_RendersShowcase() =>
        new DialogChromeStartupBaselineTests(output).CaptureAsync(pointerRecovery: false, run: 1);

    [AvaloniaFact]
    public void RestoreChooser_RendersShowcase()
    {
        using var directory = new TemporaryDirectory();
        var vm = new RestoreBackupViewModel(directory.Path)
        {
            Rows = [
                new("verified.zip", "Sep 26, 2026 2:06 PM · manual · 12.4 MB · 2,000 images · 1.0", "verified when created", true),
                new("copied.zip", "copied-catalog-backup.zip", "not checked", true),
                new("damaged.zip", "Sep 24, 2026 2:06 PM · before-restore · 12.1 MB · 2,000 images · 1.0", "damaged", false),
                new("newer.zip", "Sep 23, 2026 2:06 PM · manual · 12.4 MB · 2,000 images · 2.0", "needs a newer app (schema 99)", false),
                new("cloud.zip", "Sep 22, 2026 2:06 PM · scheduled · 12.0 MB · 1,950 images · 1.0", "cloud-only · downloads when restored", true)
            ]
        };
        vm.Selected = vm.Rows[0];
        var dialog = new RestoreBackupDialog { DataContext = vm };
        ShowcaseTestHelper.Capture("30-restore-backup-wp9", dialog, new PixelSize(650, 440), ThemeVariant.Dark,
            shown =>
            {
                Assert.Contains(shown.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && Equals(button.Content, "Choose a backup file…"));
                var list = Assert.Single(shown.GetVisualDescendants().OfType<ListBox>());
                foreach (var row in vm.Rows)
                {
                    var label = Assert.Single(shown.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.IsEffectivelyVisible && text.Text == row.State);
                    var position = label.TranslatePoint(new Point(0, label.Bounds.Height), list);
                    Assert.NotNull(position);
                    Assert.InRange(position.Value.Y, 0, list.Bounds.Height);
                }
            });
    }
}
