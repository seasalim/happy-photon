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

public sealed class RestoreShowcaseTests
{
    [AvaloniaFact]
    public async Task StartupErrorRestore_RendersShowcase()
    {
        using var catalog = new CatalogService();
        await using var vm = new MainWindowViewModel(catalog);
        vm.ShowStartupFailure("The local catalog is damaged or unrecognized. Restore a backup or retry.");
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture("startup-error-restore", scope, new PixelSize(1200, 700), ThemeVariant.Dark,
            shown => Assert.Contains(shown.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && Equals(button.Content, "Restore from backup…")));
    }

    [AvaloniaFact]
    public void RestoreChooser_RendersShowcase()
    {
        using var directory = new TemporaryDirectory();
        var vm = new RestoreBackupViewModel(directory.Path)
        {
            Rows = [
                new("verified.zip", "26 Sep 2026 · manual · 12.4 MiB · 2,000 images · 1.0", "verified when created", true),
                new("copied.zip", "copied-catalog-backup.zip", "not checked", true),
                new("damaged.zip", "24 Sep 2026 · before-restore · 12.1 MiB · 2,000 images · 1.0", "damaged", false),
                new("newer.zip", "23 Sep 2026 · manual · 12.4 MiB · 2,000 images · 2.0", "needs a newer app (schema 99)", false),
                new("cloud.zip", "22 Sep 2026 · scheduled · 12.0 MiB · 1,950 images · 1.0", "cloud-only · downloads when restored", true)
            ]
        };
        vm.Selected = vm.Rows[0];
        var dialog = new RestoreBackupDialog { DataContext = vm };
        ShowcaseTestHelper.Capture("restore-chooser", dialog, new PixelSize(650, 440), ThemeVariant.Dark,
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
