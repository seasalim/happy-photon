using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(
    typeof(HappyPhoton.Tests.AvaloniaTestAppBuilder))]

// PerTest isolation (the default) resets the dispatcher between tests; a
// background continuation touching Dispatcher.UIThread in that window claims
// thread ownership, making the next SetupUnsafe throw VerifyAccess. The
// exception escapes the headless session's dispatch loop, which then dies and
// leaves every queued test — and the test host process — waiting forever.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace HappyPhoton.Tests;

public static class AvaloniaTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HappyPhoton.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .LogToTrace();
}
