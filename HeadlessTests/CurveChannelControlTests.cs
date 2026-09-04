using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

using APath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class CurveChannelControlTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task SelectorUsesHeaderWithoutMaterializingAndPaintsChannelState()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        await using var viewModel = CreateViewModel(catalog);
        var panel = new DevelopEditPanel { DataContext = viewModel };
        var window = ShowPanel(panel);
        var image = new ImageFile(Path.Combine(_root.Path, "channels.jpg"));

        try
        {
            viewModel.SelectedImage = image;
            Dispatcher.UIThread.RunJobs();
            var curve = panel.FindControl<CurveView>("ToneCurveView")!;
            var red = curve.FindControl<Button>("RedChannelButton")!;

            Assert.Equal(180, curve.Height);
            Assert.Equal(ToneCurveChannel.Composite, curve.ActiveChannel);
            Assert.Equal("RGB", curve.FindControl<Button>(
                "CompositeChannelButton")!.Content);

            red.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ToneCurveChannel.Red, viewModel.ActiveCurveChannel);
            Assert.Null(image.EditSettings.CurveRed);
            Assert.True(viewModel.CurrentCurve!.IsIdentity());
            Assert.DoesNotContain("touched", red.Classes);

            viewModel.OnCurveEditStarted();
            viewModel.CurrentCurve.AddPointAndReturnIndex(0.5, 0.72);
            await viewModel.OnCurveChangedAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(image.EditSettings.CurveRed);
            Assert.True(viewModel.HasRedCurve);
            Assert.Contains("touched", red.Classes);
            var paths = curve.FindControl<Canvas>("CurveCanvas")!.Children
                .OfType<APath>()
                .ToArray();
            Assert.True(paths.Length >= 2);
            Assert.Equal(0.35, paths[^2].Opacity);
            Assert.Same(HappyPhotonColors.ColorLabelRed, paths[^1].Stroke);
        }
        finally
        {
            window.Close();
            panel.DataContext = null;
        }
    }

    [AvaloniaFact]
    public async Task ChannelCurveHistoryRestoresDragsRemovalAndEmbeddedReset()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "history"));
        await catalog.InitializeAsync();
        await using var viewModel = CreateViewModel(catalog);
        var panel = new DevelopEditPanel { DataContext = viewModel };
        var window = ShowPanel(panel);
        var image = new ImageFile(Path.Combine(_root.Path, "history.jpg"));

        try
        {
            viewModel.SelectedImage = image;
            viewModel.ActiveCurveChannel = ToneCurveChannel.Red;

            await CommitAsync(viewModel, curve =>
                curve.AddPointAndReturnIndex(0.5, 0.7));
            Assert.Equal(0.7, image.EditSettings.CurveRed!.Points[1].Y);
            await CommitAsync(viewModel, curve =>
                curve.MovePoint(1, 0.5, 0.8));
            Assert.Equal(0.8, image.EditSettings.CurveRed!.Points[1].Y);
            await CommitAsync(viewModel, curve => curve.RemovePoint(1));
            Assert.Null(image.EditSettings.CurveRed);

            await viewModel.UndoCommand.ExecuteAsync(null);
            Assert.Equal(0.8, image.EditSettings.CurveRed!.Points[1].Y);
            await viewModel.UndoCommand.ExecuteAsync(null);
            Assert.Equal(0.7, image.EditSettings.CurveRed!.Points[1].Y);
            await viewModel.RedoCommand.ExecuteAsync(null);
            Assert.Equal(0.8, image.EditSettings.CurveRed!.Points[1].Y);

            Dispatcher.UIThread.RunJobs();
            panel.FindControl<CurveView>("ToneCurveView")!.ResetCurve();
            await TestWaits.UntilAsync(() => image.EditSettings.CurveRed == null);
            Assert.False(viewModel.HasRedCurve);

            await viewModel.UndoCommand.ExecuteAsync(null);
            Assert.Equal(0.8, image.EditSettings.CurveRed!.Points[1].Y);
            Assert.True(viewModel.HasRedCurve);
        }
        finally
        {
            window.Close();
            panel.DataContext = null;
        }
    }

    [AvaloniaFact]
    public async Task PanelResetClearsCompositeAndAllChannelCurves()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "reset"));
        await catalog.InitializeAsync();
        await using var viewModel = CreateViewModel(catalog);
        var settings = new EditSettings
        {
            CurveRed = CreateCurve(0.4, 0.7),
            CurveGreen = CreateCurve(0.5, 0.3),
            CurveBlue = CreateCurve(0.6, 0.8)
        };
        settings.Curve.AddPointAndReturnIndex(0.5, 0.65);
        var image = new ImageFile(Path.Combine(_root.Path, "reset.jpg"))
        {
            EditSettings = settings
        };

        viewModel.SelectedImage = image;
        await viewModel.ResetEditsCommand.ExecuteAsync(null);

        Assert.True(image.EditSettings.Curve.IsIdentity());
        Assert.Null(image.EditSettings.CurveRed);
        Assert.Null(image.EditSettings.CurveGreen);
        Assert.Null(image.EditSettings.CurveBlue);
        Assert.False(viewModel.CanReset);
    }

    [AvaloniaFact]
    public async Task UntouchedSelectionSurvivesSaveAndCopyWithoutChannelState()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "lazy"));
        await catalog.InitializeAsync();
        await using var viewModel = CreateViewModel(catalog);
        var source = new ImageFile(Path.Combine(_root.Path, "source.jpg"));
        var target = new ImageFile(Path.Combine(_root.Path, "target.jpg"));

        viewModel.SelectedImage = source;
        viewModel.ActiveCurveChannel = ToneCurveChannel.Green;
        viewModel.Exposure = 0.25;
        await TestWaits.UntilAsync(() => source.EditSettings.Exposure == 0.25);
        Assert.Null(source.EditSettings.CurveGreen);

        viewModel.CopyEditSettingsCommand.Execute(null);
        viewModel.SelectedImage = target;
        await viewModel.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0.25, target.EditSettings.Exposure);
        Assert.Null(target.EditSettings.CurveRed);
        Assert.Null(target.EditSettings.CurveGreen);
        Assert.Null(target.EditSettings.CurveBlue);
    }

    [AvaloniaFact]
    public async Task InterleavedCurveAndSliderHistoryPreservesPostCurveState()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "interleaved"));
        await catalog.InitializeAsync();
        await using var viewModel = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root.Path, "interleaved.jpg"));
        viewModel.SelectedImage = image;
        viewModel.ActiveCurveChannel = ToneCurveChannel.Blue;
        await CommitAsync(viewModel, curve =>
            curve.AddPointAndReturnIndex(0.5, 0.72));

        viewModel.Exposure = 1;
        await TestWaits.UntilAsync(() => image.EditSettings.Exposure == 1);

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, image.EditSettings.Exposure);
        Assert.Equal(0.72, image.EditSettings.CurveBlue!.Points[1].Y);
        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.CurveBlue);

        await viewModel.RedoCommand.ExecuteAsync(null);
        Assert.Equal(0.72, image.EditSettings.CurveBlue!.Points[1].Y);
        await viewModel.RedoCommand.ExecuteAsync(null);
        Assert.Equal(1, image.EditSettings.Exposure);
        Assert.Equal(0.72, image.EditSettings.CurveBlue!.Points[1].Y);
    }

    private static async Task CommitAsync(
        MainWindowViewModel viewModel,
        Action<CurveData> edit)
    {
        viewModel.OnCurveEditStarted();
        edit(viewModel.CurrentCurve!);
        await viewModel.OnCurveChangedAsync();
    }

    private static CurveData CreateCurve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            new TinyBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask)
        {
            IsDevelopMode = true
        };

    private static Window ShowPanel(DevelopEditPanel panel)
    {
        var window = new Window
        {
            Width = 250,
            Height = 660,
            Content = panel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    public void Dispose()
    {
        _root.Dispose();
    }

    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 16, 12)
                {
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    16,
                    12));
    }
}
