using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    public async Task ReviewMetadata_FormatsRowsTooltipsAndContextActions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-review-details-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var modified = new DateTime(2026, 8, 14, 10, 15, 0);
        var image = new ImageFile(Path.Combine(root, "photo.jpg"));
        image.ApplyMetadata(new ImageMetadata
        {
            FileSize = 29_779_558,
            PixelWidth = 6000,
            PixelHeight = 4000,
            FileModifiedDate = modified,
            Iso = 100,
            FocalLength = 70,
            FocalLengthIn35mmFilm = 105,
            GpsLatitude = 47.608333,
            GpsLongitude = -122.320833,
            GpsAltitude = 12
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
            var panel = review.FindControl<StackPanel>(
                "ReviewMetadataPanel")!;
            var fileName = review.FindControl<TextBlock>(
                "ReviewFileNameText")!;
            var exposure = review.FindControl<TextBlock>(
                "ReviewExposureText")!;
            var modifiedDate = review.FindControl<TextBlock>(
                "ReviewModifiedDateText")!;

            Assert.Equal(
                "6000×4000 · 24.0 MP · 28.4 MB",
                review.FindControl<TextBlock>("ReviewFileDetailsText")!.Text);
            Assert.Equal(image.FilePath, ToolTip.GetTip(fileName));
            Assert.Equal("70mm (105mm equiv)", ToolTip.GetTip(exposure));
            Assert.True(review.FindControl<StackPanel>(
                "ReviewCameraSection")!.IsVisible);
            Assert.False(review.FindControl<TextBlock>(
                "ReviewCameraText")!.IsVisible);
            Assert.False(review.FindControl<TextBlock>(
                "ReviewLensText")!.IsVisible);
            Assert.True(exposure.IsVisible);
            Assert.True(modifiedDate.IsVisible);
            Assert.True(window.TryFindResource(
                "TextMuted",
                window.ActualThemeVariant,
                out var mutedResource));
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(mutedResource).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(
                    modifiedDate.Foreground).Color);

            string? copied = null;
            var copiedSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.CopyToClipboardAsync = text =>
            {
                copied = text;
                copiedSignal.TrySetResult();
                return Task.CompletedTask;
            };
            var copy = Assert.IsType<MenuItem>(
                Assert.Single(panel.ContextMenu!.Items));
            Assert.Equal("Copy details", copy.Header);
            panel.ContextMenu.Open(panel);
            Dispatcher.UIThread.RunJobs();
            copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await copiedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("photo.jpg", copied);
            Assert.Contains("(file modified)", copied);

            var launches = new List<Uri>();
            var launchedSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.LaunchUriAsync = uri =>
            {
                launches.Add(uri);
                launchedSignal.TrySetResult();
                return Task.FromResult(true);
            };
            Assert.Empty(launches);
            var mapLink = review.FindControl<Button>("ReviewMapLink")!;
            Assert.Same(viewModel.OpenSelectedImageMapCommand, mapLink.Command);
            mapLink.Command!.Execute(null);
            await launchedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(launches);

            image.DateTaken = new DateTime(2026, 8, 1, 8, 0, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(modifiedDate.IsVisible);
            Assert.True(review.FindControl<TextBlock>(
                "ReviewCaptureDateText")!.IsVisible);

            var altitudeOnly = new ImageFile(Path.Combine(root, "altitude.jpg"));
            altitudeOnly.ApplyMetadata(new ImageMetadata { GpsAltitude = -12 });
            viewModel.Library.SetImages([altitudeOnly]);
            viewModel.SelectedImage = altitudeOnly;
            Dispatcher.UIThread.RunJobs();
            Assert.True(review.FindControl<StackPanel>(
                "ReviewLocationSection")!.IsVisible);
            Assert.False(review.FindControl<Button>("ReviewMapLink")!.IsVisible);
            Assert.Equal(
                "-12 m altitude",
                review.FindControl<TextBlock>("ReviewAltitudeText")!.Text);
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
