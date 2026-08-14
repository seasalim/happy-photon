using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CloudSourceViewTests
{
    [AvaloniaFact]
    public async Task Workspace_ShowsOnlyTheRightPaneForTheCurrentMode()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-review-pane-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var image = new ImageFile(Path.Combine(root, "photo.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        image.ApplyMetadata(new ImageMetadata
        {
            FileSize = 2_048,
            PixelWidth = 24,
            PixelHeight = 16,
            CameraMake = "Photon",
            CameraModel = "One"
        });
        viewModel.Library.SetImages([image]);
        viewModel.SelectedImage = image;
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var review = window.FindControl<LibraryReviewPane>(
                "LibraryReviewPane")!;
            var develop = window.FindControl<DevelopEditPanel>(
                "DevelopEditPanel")!;
            var navigator = window.FindControl<Border>("NavigatorPanel")!;
            var metadata = review.FindControl<StackPanel>(
                "ReviewMetadataPanel")!;
            var beforeAfter = develop.FindControl<Button>(
                "BeforeAfterButton")!;

            Assert.True(review.IsVisible);
            Assert.False(develop.IsVisible);
            Assert.False(viewModel.ToggleBeforeAfterCommand.CanExecute(null));
            Assert.Contains(develop, beforeAfter.GetLogicalAncestors());
            Assert.False(beforeAfter.GetLogicalAncestors()
                .OfType<Control>()
                .All(control => control.IsVisible));
            Assert.True(double.IsNaN(navigator.Height));
            Assert.DoesNotContain(
                metadata,
                navigator.GetLogicalDescendants());
            Assert.Equal("photo.jpg", review.FindControl<TextBlock>(
                "ReviewFileNameText")!.Text);

            viewModel.IsDevelopMode = true;

            Assert.False(review.IsVisible);
            Assert.True(develop.IsVisible);
            Assert.True(viewModel.ToggleBeforeAfterCommand.CanExecute(null));
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await viewModel.DisposeAsync();
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ReviewEmptyAndOnlineOnlyStates_DoNotLoadSourceContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-review-states-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var metadataLoads = 0;
        var baseLoader = new CountingBaseLoader();
        var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader,
            loadMetadataAsync: _ =>
            {
                Interlocked.Increment(ref metadataLoads);
                return Task.CompletedTask;
            },
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            Dispatcher.UIThread.RunJobs();
            var review = window.FindControl<LibraryReviewPane>(
                "LibraryReviewPane")!;
            Assert.True(review.FindControl<TextBlock>(
                "ReviewEmptyHint")!.IsVisible);
            Assert.Equal(0, metadataLoads);
            Assert.Equal(0, baseLoader.LoadCount);

            var cloud = new ImageFile(
                Path.Combine(root, "cloud.jpg"),
                SourceAvailability.RequiresHydration);
            viewModel.Library.SetImages([cloud]);
            viewModel.SelectedImage = cloud;
            Dispatcher.UIThread.RunJobs();

            Assert.True(review.FindControl<TextBlock>(
                "ReviewOnlineOnlyHint")!.IsVisible);
            Assert.Equal("cloud.jpg", review.FindControl<TextBlock>(
                "ReviewFileNameText")!.Text);
            Assert.Equal(0, metadataLoads);
            Assert.Equal(0, baseLoader.LoadCount);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await viewModel.DisposeAsync();
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ReviewMetadataFailure_ShowsUnavailableInsteadOfLoading()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-review-unavailable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var image = new ImageFile(Path.Combine(root, "unknown.jpg"));
        viewModel.Library.SetImages([image]);
        viewModel.SelectedImage = image;
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            Dispatcher.UIThread.RunJobs();
            var review = window.FindControl<LibraryReviewPane>(
                "LibraryReviewPane")!;
            Assert.False(review.FindControl<TextBlock>(
                "ReviewMetadataLoadingHint")!.IsVisible);
            Assert.True(review.FindControl<TextBlock>(
                "ReviewMetadataUnavailableHint")!.IsVisible);
            Assert.Equal("unknown.jpg", review.FindControl<TextBlock>(
                "ReviewFileNameText")!.Text);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await viewModel.DisposeAsync();
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task SelectedCloudSource_ShowsScopedDownloadActionAndFolderMessage()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-cloud-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
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
            await viewModel.DisposeAsync();
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

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

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return null;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return null;
        }
    }
}
