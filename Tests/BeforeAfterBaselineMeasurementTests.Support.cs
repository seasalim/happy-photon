using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BeforeAfterBaselineMeasurementTests
{
    private sealed class OperationObserver
    {
        private readonly MainWindowViewModel _vm;
        private readonly CountingLoader _loader;
        private readonly int _startingDecodeCount;
        private int _peakDecodeTasks;
        private int _peakPreviewActivity;

        public OperationObserver(MainWindowViewModel vm, CountingLoader loader)
        {
            _vm = vm;
            _loader = loader;
            _startingDecodeCount = loader.TotalCount;
            Observe();
        }

        public async Task ObserveUntilAsync(Func<bool> complete)
        {
            var deadline = DateTime.UtcNow + TestWaits.Condition;
            while (!complete())
            {
                Observe();
                Assert.True(DateTime.UtcNow < deadline, "Measurement did not settle.");
                await Task.Delay(1);
            }
            Observe();
        }

        public OperationSample Complete(double elapsedMs = 0)
        {
            Observe();
            return new OperationSample(
                _loader.TotalCount - _startingDecodeCount,
                _peakDecodeTasks,
                _peakPreviewActivity,
                _vm.ImageService.Previews.RetainedBasePairCount,
                elapsedMs);
        }

        private void Observe()
        {
            _peakDecodeTasks = Math.Max(_peakDecodeTasks, DecodeTaskCount(_vm));
            _peakPreviewActivity = Math.Max(
                _peakPreviewActivity,
                _vm.ImageService.Previews.PreviewActivityCount);
        }
    }

    private sealed class CountingLoader(IBaseImageLoader inner) : IBaseImageLoader
    {
        private readonly ConcurrentDictionary<string, int> _counts =
            new(StringComparer.OrdinalIgnoreCase);

        public int TotalCount => _counts.Values.Sum();
        public bool CanLoad(ImageFile file) => inner.CanLoad(file);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _counts.AddOrUpdate(file.FilePath, 1, (_, count) => count + 1);
            return inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadFullBase(file, decode, cancellationToken);
    }

    private sealed record FixtureSample(
        OperationSample DefaultToggle,
        OperationSample? HighlightToggle,
        OperationSample ToneEdit,
        OperationSample Selection,
        long? ParentBefore,
        long? ParentAfter,
        OperationSample SplitEntry,
        OperationSample SplitToneEdit,
        OperationSample SplitLoupe,
        OperationSample SplitSelection,
        long? SplitParentBefore,
        long? SplitParentAfter);

    private sealed record OperationSample(
        int DecodeDelta,
        int PeakDecodeTasks,
        int PeakPreviewActivity,
        int RetainedPairs,
        double ElapsedMs);
}
