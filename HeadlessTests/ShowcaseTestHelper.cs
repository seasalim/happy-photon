using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ShowcaseTestHelper
{
    public static void Capture(
        string scene, Window window, PixelSize pixelSize, ThemeVariant theme,
        Action<Window>? stage = null)
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

        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        try
        {
            application.RequestedThemeVariant = theme;
            window.Width = pixelSize.Width;
            window.Height = pixelSize.Height;
            window.Show();
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
        finally
        {
            try
            {
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = previousTheme;
            }
        }
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
