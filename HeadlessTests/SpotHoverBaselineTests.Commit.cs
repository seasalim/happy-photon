using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotHoverBaselineTestsCommit(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(SpotHoverBaselineTestSupport.GestureCases), false, MemberType = typeof(SpotHoverBaselineTestSupport))]
    public Task GestureFeedbackPersistsAndEnds(string zone, bool outside, bool cancel) =>
        SpotHoverBaselineTestSupport.GestureFeedbackPersistsAndEnds(output, zone, outside, cancel);
}
