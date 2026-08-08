using Avalonia.Controls;
using Avalonia.Layout;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CloudSourceViewTests
{
    private readonly AvaloniaTestFixture _fixture;

    public CloudSourceViewTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public void SelectedCloudSource_ShowsScopedDownloadActionAndFolderMessage()
    {
        _fixture.RequireWindows();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-cloud-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        Complete(catalog.InitializeAsync());
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        var image = new ImageFile(Path.Combine(root, "photo.jpg"))
        {
            SourceRequiresHydration = true
        };
        viewModel.Library.SetImages([image]);
        viewModel.InitializeCloudSourceCount([image]);
        viewModel.SelectedImage = image;
        viewModel.IsDevelopMode = true;
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            var download = window.FindControl<Button>("DownloadAndOpenButton")!;
            var grid = window.FindControl<LibraryGridView>("LibraryGridView")!;
            var message = grid.FindControl<TextBlock>("OnlineOnlyMessage")!;
            var develop = window.FindControl<DevelopEditPanel>(
                "DevelopEditPanel")!;
            var presets = window.FindControl<PresetsPanel>("PresetsPanel")!;

            Assert.True(download.IsVisible);
            Assert.Same(viewModel.DownloadAndOpenCommand, download.Command);
            Assert.Equal(HorizontalAlignment.Left, download.HorizontalAlignment);
            Assert.Equal(10, download.FontSize);
            Assert.False(viewModel.CanEditSelectedImage);
            Assert.False(develop.IsEnabled);
            Assert.False(presets.IsEnabled);
            Assert.True(message.IsVisible);
            Assert.Equal(viewModel.OnlineOnlyMessage, message.Text);
            Assert.True(image.ShowCloudPlaceholder);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Complete(viewModel.DisposeAsync().AsTask());
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
