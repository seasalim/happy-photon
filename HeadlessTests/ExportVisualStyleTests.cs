using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportVisualStyleTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("export-visual-style");

    [AvaloniaFact]
    public async Task ExportPane_UsesSharedDevelopControlMetrics()
    {
        using var catalog = _fixture.CreateCatalog();
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.ExportSettings.OutputFolder = Path.Combine(
            catalog.CatalogPath,
            "finished copies");
        var pane = new ExportSettingsPane { DataContext = viewModel };
        var window = new Window { Width = 280, Height = 1100, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var surface = Assert.IsType<Border>(pane.Content);
            AssertBrush("SurfaceMid", surface.Background);
            var scroll = Assert.IsType<ScrollViewer>(surface.Child);
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            var stack = Assert.IsType<StackPanel>(scroll.Content);
            Assert.Equal(new Thickness(15, 0, 15, 15), stack.Margin);
            Assert.All(
                stack.Children.OfType<TextBlock>()
                    .Where(text => text.Classes.Contains("section-label")),
                heading => Assert.Equal(new Thickness(0, 20, 0, 8), heading.Margin));

            var format = pane.FindControl<ComboBox>("ExportFormatBox")!;
            Assert.Equal(28, format.Height);
            Assert.Equal(11, format.FontSize);
            Assert.Equal(FontWeight.SemiBold, format.FontWeight);
            AssertBrush("SurfaceHigh", format.Background);
            AssertBrush("Divider", format.BorderBrush);

            foreach (var name in new[]
                     {
                         "WebMaxSizeField",
                         "SmallMaxSizeField",
                         "ExportFolderField",
                         "ExportNamingPatternField"
                     })
            {
                var field = pane.FindControl<TextBox>(name)!;
                Assert.Equal(28, field.Height);
                Assert.Equal(11, field.FontSize);
                Assert.Equal(FontWeight.Normal, field.FontWeight);
                AssertBrush("SurfaceHigh", field.Background);
                AssertBrush("Divider", field.BorderBrush);
            }

            var web = pane.FindControl<TextBox>("WebMaxSizeField")!;
            var small = pane.FindControl<TextBox>("SmallMaxSizeField")!;
            Assert.Equal(
                web.TranslatePoint(default, pane)!.Value.X,
                small.TranslatePoint(default, pane)!.Value.X,
                precision: 3);
            Assert.Equal(web.Bounds.Width, small.Bounds.Width, precision: 3);

            var folder = pane.FindControl<TextBox>("ExportFolderField")!;
            Assert.Equal(viewModel.ExportSettings.OutputFolder, folder.Text);
            Assert.Equal(viewModel.ExportSettings.OutputFolder, ToolTip.GetTip(folder));

            var recipeToggle = pane.GetVisualDescendants().OfType<CheckBox>().First();
            Assert.Equal(28, recipeToggle.Height);
            Assert.Equal(11, recipeToggle.FontSize);
            Assert.Equal(new CornerRadius(3), recipeToggle.CornerRadius);
            var checkBox = Assert.Single(
                recipeToggle.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "NormalRectangle");
            var checkGlyph = Assert.Single(
                recipeToggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                path => path.Name == "CheckGlyph");
            Assert.Equal(0.8, checkBox.RenderTransform!.Value.M11, precision: 3);
            Assert.Equal(0.8, checkBox.RenderTransform.Value.M22, precision: 3);
            AssertBrush("ControlActive", checkBox.Background);
            AssertBrush("OnControlActive", checkGlyph.Fill);
            var browse = pane.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Browse…"));
            Assert.Equal(28, browse.Height);
            Assert.Equal(11, browse.FontSize);
            AssertBrush("SurfaceHigh", browse.Background);
            AssertBrush("Divider", browse.BorderBrush);

            AssertSegmented(pane.FindControl<ListBox>("ExportColorSpaceBox")!);
            AssertSegmented(pane.FindControl<ListBox>("ExportSharpeningBox")!);

            var report = pane.GetVisualDescendants().OfType<ExportReportCard>().Single();
            var reportSurface = Assert.IsType<Border>(report.Content);
            AssertBrush("SurfaceLow", reportSurface.Background);

            var quality = pane.FindControl<CompactSlider>("ExportQualitySlider")!;
            // The control once advertised a dead 22px self-style. Pin both the
            // real 20px contract and its shared Develop/Export metric.
            Assert.Equal(20, quality.Bounds.Height);
            Assert.Equal(DevelopCompactSliderHeight(viewModel), quality.Bounds.Height);
            Assert.Equal(11, quality.FindControl<TextBlock>("LabelText")!.FontSize);
            quality.Value = 92;
            Assert.Equal("92%", quality.FindControl<TextBlock>("ValueText")!.Text);
            Assert.Equal(92, viewModel.ExportSettings.Quality);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExportCaptureList_HidesItsScrollBar()
    {
        using var catalog = _fixture.CreateCatalog();
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        var pane = new ExportCapturePane { DataContext = viewModel };
        var window = new Window { Width = 220, Height = 400, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var list = pane.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Equal(
                ScrollBarVisibility.Hidden,
                ScrollViewer.GetVerticalScrollBarVisibility(list));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DevelopSegmentedRows_KeepSharedMetrics()
    {
        using var catalog = _fixture.CreateCatalog();
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = viewModel };
        var window = new Window { Width = 280, Height = 900, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            AssertSegmented(panel.FindControl<ListBox>("HighlightHandlingControl")!);
            var effects = panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!;
            AssertSegmented(effects.FindControl<ListBox>("GrainSizeControl")!);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1200, 600, 800)]
    [InlineData(600, 1200, 800)]
    [InlineData(188, 752, 340)]
    public void ProofCaption_TracksFittedImageBottomLeft(
        int width,
        int height,
        int hostWidth)
    {
        using var source = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        var pane = new ExportPreviewPane();
        var frame = pane.FindControl<UniformImageOverlayPanel>(
            "ExportPreviewImageFrame")!;
        var image = pane.FindControl<DisplayImage>("ExportPreviewImage")!;
        var placeholder = pane.FindControl<Image>("ExportPlaceholderImage")!;
        var caption = pane.FindControl<TextBlock>("ExportProofCaption")!;
        var emptyState = pane.FindControl<Border>("ExportPreviewEmptyState")!;
        placeholder.IsVisible = false;
        image.CanonicalSource = source;
        caption.Text = "PREVIEW · JPEG · Display P3 · 65536 PX";
        caption.IsVisible = true;
        emptyState.IsVisible = false;
        var window = new Window { Width = hostWidth, Height = 600, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        pane.UpdateLayout();

        try
        {
            var captionOrigin = caption.TranslatePoint(default, frame)!.Value;
            var imageBounds = ViewportRegion.UniformImageBounds(
                frame.Bounds.Size,
                new Size(width, height));
            var captionBottom = captionOrigin.Y + caption.Bounds.Height;

            Assert.True(imageBounds.Width > 0 && imageBounds.Height > 0);
            // imageBounds is predicted with the same helper the panel uses, so pin it
            // from a second direction: the fit must be centered and must touch one axis.
            Assert.Equal(
                frame.Bounds.Width - imageBounds.Right, imageBounds.Left, precision: 3);
            Assert.Equal(
                frame.Bounds.Height - imageBounds.Bottom, imageBounds.Top, precision: 3);
            Assert.True(
                Math.Abs(imageBounds.Width - frame.Bounds.Width) < 0.01 ||
                Math.Abs(imageBounds.Height - frame.Bounds.Height) < 0.01);
            Assert.Equal(width / (double)height,
                imageBounds.Width / imageBounds.Height, precision: 3);
            Assert.InRange(
                Math.Abs(captionOrigin.X -
                    (imageBounds.Left + UniformImageOverlayPanel.OverlayInset)),
                0,
                1);
            Assert.InRange(
                Math.Abs(captionBottom -
                    (imageBounds.Bottom - UniformImageOverlayPanel.OverlayInset)),
                0,
                1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProofCaption_SmallSourceShowsAtNativeSize()
    {
        using var source = new WriteableBitmap(
            new PixelSize(256, 256),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        var pane = new ExportPreviewPane();
        var frame = pane.FindControl<UniformImageOverlayPanel>(
            "ExportPreviewImageFrame")!;
        var image = pane.FindControl<DisplayImage>("ExportPreviewImage")!;
        var caption = pane.FindControl<TextBlock>("ExportProofCaption")!;
        pane.FindControl<Image>("ExportPlaceholderImage")!.IsVisible = false;
        pane.FindControl<Border>("ExportPreviewEmptyState")!.IsVisible = false;
        image.CanonicalSource = source;
        caption.Text = "PREVIEW · JPEG · sRGB · 256 PX";
        caption.IsVisible = true;
        var window = new Window { Width = 800, Height = 600, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        pane.UpdateLayout();

        try
        {
            var imageOrigin = image.TranslatePoint(default, frame)!.Value;
            var captionOrigin = caption.TranslatePoint(default, frame)!.Value;

            Assert.Equal(256, image.Bounds.Width, precision: 3);
            Assert.Equal(256, image.Bounds.Height, precision: 3);
            Assert.Equal((frame.Bounds.Width - 256) / 2, imageOrigin.X, precision: 3);
            Assert.Equal((frame.Bounds.Height - 256) / 2, imageOrigin.Y, precision: 3);
            Assert.InRange(
                Math.Abs(captionOrigin.X -
                    (imageOrigin.X + UniformImageOverlayPanel.OverlayInset)),
                0,
                1);
            Assert.InRange(
                Math.Abs(captionOrigin.Y + caption.Bounds.Height -
                    (imageOrigin.Y + 256 - UniformImageOverlayPanel.OverlayInset)),
                0,
                1);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose() => _fixture.Dispose();

    private static double DevelopCompactSliderHeight(MainWindowViewModel viewModel)
    {
        var panel = new DevelopEditPanel { DataContext = viewModel };
        var window = new Window { Width = 280, Height = 900, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            return panel.GetVisualDescendants().OfType<CompactSlider>()
                .First(slider => slider.IsVisible && slider.Bounds.Height > 0)
                .Bounds.Height;
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertSegmented(ListBox control)
    {
        Assert.Equal(22, control.Height);
        AssertBrush("SurfaceHigh", control.Background);
        var item = control.GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.Equal(18, item.Height);
        Assert.Equal(9, item.FontSize);
        Assert.Equal(1, item.LetterSpacing);
    }

    private static void AssertBrush(string resource, IBrush? actual) =>
        Assert.Equal(
            ThemeResourceTests.Brush(resource, ThemeVariant.Dark).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
}
