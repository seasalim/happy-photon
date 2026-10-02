using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DialogChromeShowcaseTests
{
    [AvaloniaTheory]
    [InlineData(0, "17-settings-general-wp9")]
    [InlineData(1, "18-settings-metadata-wp9")]
    [InlineData(2, "19-settings-storage-wp9")]
    public async Task Settings(int tab, string scene)
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var migrator = new CatalogLocationMigrator(service);
        var locations = new AppDataLocations(Path.Combine(directory.Path, "catalog"),
            Path.Combine(directory.Path, "cache"), AppDataLocationOrigin.Environment,
            AppDataLocationOrigin.Environment);
        using var catalog = new CatalogService(locations.CatalogRoot);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        vm.SetResolvedDataLocations(locations, migrator);
        vm.RestoreAppTheme(AppTheme.Dark);
        var dialog = new SettingsDialog(vm);
        dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = tab;

        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize(650, 610), ThemeVariant.Dark);
    }

    [AvaloniaTheory]
    [InlineData(0, "20-help-shortcuts-wp9")]
    [InlineData(1, "21-help-about-wp9")]
    public void Help(int tab, string scene)
    {
        var dialog = new HelpAboutDialog();
        dialog.FindControl<TabControl>("HelpAboutTabs")!.SelectedIndex = tab;

        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize(680, 680), ThemeVariant.Dark,
            shown =>
            {
                var keys = shown.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("key") && border.IsEffectivelyVisible);
                Assert.All(keys, key => Assert.Equal(24, key.Bounds.Height));
            });
    }

    [AvaloniaFact]
    public void Paste()
    {
        var vm = new PasteSettingsViewModel("IMG_0412.CR2", 1, new Dictionary<string, bool>());
        Assert.Equal("From IMG_0412.CR2 to 1 photo", vm.Summary);
        var dialog = new PasteSettingsDialog(vm);

        ShowcaseTestHelper.Capture("28-paste-settings-wp9", dialog, new PixelSize(660, 572), ThemeVariant.Dark);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestructiveConfirmation(bool middleGray)
    {
        var theme = middleGray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        var dialog = new ConfirmationDialog("Move to Trash", "Move \"4.2.03.tiff\" to Trash?",
            ConfirmationDialogButtons.YesNo, true, "Cancel", "Move to Trash")
        {
            SizeToContent = SizeToContent.Manual
        };

        ShowcaseTestHelper.Capture($"29-confirmation-{(middleGray ? "midgray" : "dark")}-wp9",
            dialog, new PixelSize(420, 180), theme, shown =>
            {
                Assert.Equal("Cancel", Assert.IsType<Button>(shown.FocusManager!.GetFocusedElement()).Content);
            });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextInput(bool middleGray)
    {
        var dialog = new TextInputDialog("Save preset", "Preset name", "My preset")
        {
            SizeToContent = SizeToContent.Manual
        };

        ShowcaseTestHelper.Capture($"wp9-text-input{(middleGray ? "-midgray" : "")}", dialog, new PixelSize(420, 210),
            middleGray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }
}
