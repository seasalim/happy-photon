using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ShowcaseTestHelper
{
    public static void Capture(
        string scene, TestUiScope scope, PixelSize pixelSize, ThemeVariant theme,
        Action<Window>? stage = null) =>
        Capture(scene, scope.Window!, pixelSize, theme, stage, scope);

    public static void Capture(
        string scene, Window window, PixelSize pixelSize, ThemeVariant theme,
        Action<Window>? stage = null, TestUiScope? mainWindowScope = null)
    {
        ValidateScene(scene);
        var outputDirectory = Path.GetFullPath(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "artifacts", "shots"));
        var outputPath = Path.GetFullPath(
            Path.Combine(outputDirectory, $"{scene}.png"));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetDirectoryName(outputPath),
                outputDirectory,
                pathComparison))
        {
            throw new ArgumentException(
                "The scene path must be an immediate child of artifacts/shots.",
                nameof(scene));
        }

        window.Width = pixelSize.Width;
        window.Height = pixelSize.Height;
        using var scope = mainWindowScope ?? new TestUiScope(window, theme);
        using var themeScope = new TestUiScope(theme: theme);
        // test-teardown-policy: allow - using scope owns the caller-transferred MainWindow binding.
        if (mainWindowScope is not null) scope.Show();
        Dispatcher.UIThread.RunJobs();
        if (stage is not null)
        {
            stage(window);
            Dispatcher.UIThread.RunJobs();
        }

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(pixelSize, frame.PixelSize);
        Directory.CreateDirectory(outputDirectory);
        frame.Save(outputPath);
    }

    /// <summary>Advances the headless render clock until a transition settles.</summary>
    public static void Settle(Func<bool> settled, string what)
    {
        var deadline = DateTime.UtcNow + TestWaits.Condition;
        while (DateTime.UtcNow < deadline)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (settled()) return;
            Thread.Sleep(10);
        }

        Assert.True(settled(), $"{what} never settled.");
    }

    public static void SettleExpanderChevrons(Control root)
    {
        var chevrons = root.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
            .Where(path => path.Name == "ExpandCollapseChevron" && path.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(chevrons);
        // Fluent animates newly attached headers too. Capturing the first frame can
        // freeze a partly rotated chevron even when the content layout is settled.
        Settle(() => chevrons.All(path =>
        {
            var rotation = Assert.IsType<RotateTransform>(path.RenderTransform);
            var expanded = path.GetVisualAncestors().OfType<Expander>().First().IsExpanded;
            return rotation.Angle == (expanded ? 180 : 0);
        }), "Expander chevrons");
    }

    private static void ValidateScene(string scene)
    {
        if (string.IsNullOrEmpty(scene) || scene.Any(character =>
                character != '-' &&
                (character < '0' || character > '9') &&
                (character < 'a' || character > 'z')))
        {
            throw new ArgumentException(
                "Scene names may contain only lowercase letters, digits, and hyphens.",
                nameof(scene));
        }
    }
}
