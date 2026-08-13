using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AvaloniaTestApplication(
    typeof(HappyPhoton.Tests.AvaloniaTestAppBuilder))]

// PerTest isolation (the default) resets the dispatcher between tests; a
// background continuation touching Dispatcher.UIThread in that window claims
// thread ownership, making the next SetupUnsafe throw VerifyAccess. The
// exception escapes the headless session's dispatch loop, which then dies and
// leaves every queued test — and the test host process — waiting forever.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

// Serializing costs nothing — every test body already runs one-at-a-time on
// the session dispatcher thread — and removes a pool of xunit worker threads
// that otherwise sit blocked on the session queue.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// In Avalonia the first thread to create a Dispatcher becomes the permanent
// UI thread, so platform setup must win that race deterministically: warm up
// the session before discovery, while the process is otherwise quiet, so the
// dispatcher binds to the session's loop thread and SetupUnsafe cannot throw
// VerifyAccess later no matter which thread pool thread touches Avalonia.
[assembly: TestPipelineStartup(typeof(HappyPhoton.Tests.HeadlessSessionWarmup))]

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

public sealed class HeadlessSessionWarmup : ITestPipelineStartup
{
    public async ValueTask StartAsync(IMessageSink diagnosticMessageSink)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(HeadlessSessionWarmup).Assembly);
        await session.Dispatch(() => { }, CancellationToken.None);
    }

    // The test framework executor owns the session and disposes it after the
    // assembly run; there is nothing to tear down here.
    public ValueTask StopAsync() => default;
}
