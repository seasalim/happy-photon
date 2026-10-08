using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _mixerMaskCancellation;

    private Task _mixerMaskTask = Task.CompletedTask;

    private (ColorMixerBand Band, uint Tint)? _hoveredMixerBand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleClippingOverlaySides))]
    private Bitmap? _mixerBandMask;

    internal Task PendingMixerMaskTask => _mixerMaskTask;

    internal Func<Task>? MixerMaskRenderGateAsync { get; set; }

    private bool CanShowMixerMask => IsDevelopMode && CanEditSelectedImage && IsColorEditingEnabled &&
        !IsCropMode && !IsFullScreenMode && !IsShowingOriginal && !IsBeforeAfterSplit &&
        !_isHoveringPreset && _hoveredHistoryEntry == null && !_renderOutcomeChannelClosed;

    // A refresh keeps the shown mask until its replacement is published, so a refined preview does not blink.
    public void BeginMixerBandHover(ColorMixerBand band, uint tint, bool refresh = false)
    {
        if (refresh) _mixerMaskCancellation?.Cancel();
        else EndMixerBandHover();

        if (!CanShowMixerMask || SelectedImage is not { } image)
        {
            EndMixerBandHover();
            return;
        }

        _hoveredMixerBand = (band, tint);
        if (PreviewImage is not { } preview) return;

        var settings = CaptureLiveEditState();
        var size = preview.PixelSize;
        var lease = ImageService.Previews.AcquireLocalRangeBase(image, settings, Math.Max(size.Width, size.Height), preview);
        if (lease == null) return;

        var cancellation = new CancellationTokenSource();
        _mixerMaskCancellation = cancellation;
        _mixerMaskTask = BuildAsync(_mixerMaskTask);

        async Task BuildAsync(Task previous)
        {
            using (lease)
            using (cancellation)
            {
                Bitmap? result = null;

                try
                {
                    await previous;
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (MixerMaskRenderGateAsync is { } gate) await gate();

                    result = await Task.Run(() => MixerBandMaskRenderer.Render(lease.Base, settings,
                        band, size, tint, cancellation.Token), cancellation.Token);

                    if (!cancellation.IsCancellationRequested && CanShowMixerMask &&
                        ReferenceEquals(image, SelectedImage) && ReferenceEquals(preview, PreviewImage) &&
                        RenderSettingsHash.Compute(settings) == RenderSettingsHash.Compute(CaptureLiveEditState()))
                    {
                        var shown = MixerBandMask;
                        MixerBandMask = result;
                        result = null;
                        if (shown != null) _bitmapRetirement.Retire(shown, () => false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ImageServiceHelpers.LogError($"Mixer mask failed for '{image.FileName}': {ex.Message}");
                }
                finally
                {
                    result?.Dispose();
                    if (ReferenceEquals(_mixerMaskCancellation, cancellation)) _mixerMaskCancellation = null;
                }
            }
        }
    }

    public void EndMixerBandHover()
    {
        _hoveredMixerBand = null;
        _mixerMaskCancellation?.Cancel();
        if (MixerBandMask is not { } previous) return;

        MixerBandMask = null;
        _bitmapRetirement.Retire(previous, () => false);
    }
}
