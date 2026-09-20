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
        // The polling bracket is equivalence evidence only; its delay is never latency.
        await TestWaits.UntilAsync(() =>
        {
            var observed = Stopwatch.GetTimestamp();
            if (vm.PreviewImage == null || vm.Histogram == null)
            {
                lastFalse = observed;
                return false;
            }
            firstTrue = observed;
            return true;
        });
        vm.PropertyChanged -= OnChanged;
        var publication = recorder?.Snapshot().First(item => item.Timestamp >= start &&
            item.ImageId == image.CatalogId && CullPerfLedger.IsPaint(item)).Timestamp ?? observedPublication;
        Assert.InRange(publication, lastFalse, firstTrue);
        var width = Stopwatch.GetElapsedTime(lastFalse, firstTrue).TotalMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"Publication polling bracket: {width:F3} ms.");
        return (Stopwatch.GetElapsedTime(start, measureAtNotification ? observedPublication : publication).TotalMilliseconds, width);
    }
}
