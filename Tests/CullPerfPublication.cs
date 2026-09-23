using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

internal static class CullPerfPublication
{
    internal static async Task<(double Milliseconds, double BracketMilliseconds)> MeasureAsync(MainWindowViewModel vm, ImageFile image,
        CullPerfRecorder? recorder, bool measureAtNotification = false)
    {
        var start = Stopwatch.GetTimestamp();
        long observedPublication = 0;
        vm.PropertyChanged += OnChanged;
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.PreviewImage) && ReferenceEquals(vm.SelectedImage, image) && vm.PreviewImage != null && vm.Histogram != null && observedPublication == 0)
                observedPublication = Stopwatch.GetTimestamp();
        }
        vm.SelectedImage = image;
        var lastFalse = start;
        long firstTrue = 0;
        long publication = 0;
        // The polling bracket is equivalence evidence only; its delay is never latency.
        // The UI thread assigns PreviewImage before its handlers stamp the notification and
        // before it records the paint, so a poll counts as true only once that timestamp
        // exists, and is stamped after reading it.
        await TestWaits.UntilAsync(() =>
        {
            var observed = Stopwatch.GetTimestamp();
            publication = recorder?.Snapshot().Where(item => item.Timestamp >= start &&
                item.ImageId == image.CatalogId && CullPerfLedger.IsPaint(item))
                .Select(item => item.Timestamp).FirstOrDefault() ?? Volatile.Read(ref observedPublication);
            if (vm.PreviewImage == null || vm.Histogram == null || publication == 0)
            {
                lastFalse = observed;
                return false;
            }
            firstTrue = Stopwatch.GetTimestamp();
            return true;
        });
        vm.PropertyChanged -= OnChanged;
        Assert.InRange(publication, lastFalse, firstTrue);
        var width = Stopwatch.GetElapsedTime(lastFalse, firstTrue).TotalMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"Publication polling bracket: {width:F3} ms.");
        return (Stopwatch.GetElapsedTime(start, measureAtNotification ? observedPublication : publication).TotalMilliseconds, width);
    }
}
