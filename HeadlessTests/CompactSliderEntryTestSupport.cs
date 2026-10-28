using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using static HappyPhoton.Tests.CompactSliderEntryTestSupport;

namespace HappyPhoton.Tests;

internal static class CompactSliderEntryTestSupport
{
    internal static Rect VisibleBounds(Visual visual)
    {
        var visible = new Rect(visual.Bounds.Size);

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            var transform = ancestor.TransformToVisual(visual)!.Value;

            if (ancestor.ClipToBounds)
            {
                visible = visible.Intersect(new Rect(ancestor.Bounds.Size).TransformToAABB(transform));
            }

            if (ancestor.Clip is { } clip)
            {
                visible = visible.Intersect(clip.Bounds.TransformToAABB(transform));
            }
        }

        return visible;
    }

    internal sealed class Session : IAsyncDisposable
    {
        private readonly CatalogVmFixture _fixture = new("slider-entry");

        private CatalogService _catalog = null!;

        private TestUiScope _scope = null!;

        public MainWindowViewModel Vm { get; private set; } = null!;

        public MainWindow Window { get; private set; } = null!;

        public TestTimeProvider Clock { get; } = new();

        public CatalogService Catalog => _catalog;

        public TestUiScope Scope => _scope;

        public int Steps => Vm.HistoryEntries.Count(entry => entry.Label != "Original");

        public static async Task<Session> Create(EditSettings? settings = null, bool largeFrame = false)
        {
            var s = new Session();

            try
            {
                s._catalog = await s._fixture.CreateCatalogAsync();
                s.Vm = s._fixture.CreateViewModel(s._catalog, new LocalTestLoader(width: largeFrame ? 640 : 64, height: largeFrame ? 480 : 48), _ => Task.CompletedTask,
                    new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: s.Clock);
                var images = new[] { new ImageFile(s._fixture.Path("first.jpg")), new ImageFile(s._fixture.Path("next.jpg")) };

                foreach (var image in images)
                {
                    image.CatalogId = await s._catalog.GetOrCreateImageAsync(image.FilePath);
                }

                if (settings != null)
                {
                    images[0].EditSettings = settings;
                    await s._catalog.SaveEditSettingsAsync(images[0].CatalogId, settings);
                }

                s.Vm.IsDevelopMode = true;
                s.Vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
                s.Vm.Browse.SetImages(images);
                s.Vm.SelectedImage = images[0];
                await TestWaits.UntilAsync(() => s.Vm.IsHistoryLoaded && s.Vm.PreviewImage != null);
                // test-teardown-policy: allow - the returned session owns ForMainWindow and disposes it before its VM.
                s.Window = new MainWindow { Width = 1200, Height = 700 };
                s._scope = TestUiScope.ForMainWindow(s.Window, s.Vm);

                return s;
            }
            catch
            {
                await s.DisposeAsync();
                throw;
            }
        }

        public CompactSlider Slider(string label, Type? section = null)
        {
            Dispatcher.UIThread.RunJobs();

            return Window.GetVisualDescendants().OfType<CompactSlider>().Single(slider => slider.Label == label &&
                slider.IsEffectivelyVisible && (section == null
                    ? !slider.GetVisualAncestors().Any(a => a is LocalsEditSection or SpotsEditSection)
                    : slider.GetVisualAncestors().Any(a => a.GetType() == section)));
        }

        public Point Center(Control control) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;

        public Point Reveal(CompactSlider slider)
        {
            Dispatcher.UIThread.RunJobs();
            slider.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            ShowcaseTestHelper.Settle(() =>
            {
                var hit = Window.InputHitTest(Center(slider.FindControl<Border>("ValueCell")!)) as Visual;

                return hit == slider || hit?.GetVisualAncestors().Contains(slider) == true;
            }, "slider value hit testing");

            return Center(slider.FindControl<Border>("ValueCell")!);
        }

        public NumericEntryBox Open(CompactSlider slider)
        {
            Click(Reveal(slider));
            Dispatcher.UIThread.RunJobs();
            Assert.True(slider.IsEditingValue);
            var entry = slider.FindControl<NumericEntryBox>("ValueEntry")!;
            Assert.True(entry.IsFocused);

            return entry;
        }

        public void Click(Point point)
        {
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);

        public async Task Drain()
        {
            Clock.Advance(TimeSpan.FromMilliseconds(300));
            Dispatcher.UIThread.RunJobs();
            if (Vm.PendingPreviewDebounceTask is { } preview) await preview.WaitAsync(TestWaits.Condition);
            if (Vm.PendingHistoryCommitTask is { } commit) await commit.WaitAsync(TestWaits.Condition);
        }

        public async ValueTask DisposeAsync()
        {
            _scope?.Dispose();
            if (Vm != null) await Vm.DisposeAsync();
            _catalog?.Dispose();
            _fixture.Dispose();
        }
    }
}
