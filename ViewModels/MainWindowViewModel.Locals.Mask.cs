using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record LocalMaskIdentity(ImageFile Image, BaseImage Base, string Settings,
        string LocalId, PixelSize Size, bool Monochrome);
    private LocalMaskIdentity? _localMaskIdentity;
    private LocalMaskIdentity? _failedLocalMaskIdentity;
    private WriteableBitmap? _localRangeMask;
    private CancellationTokenSource? _localMaskCancellation;
    private Task _localMaskTask = Task.CompletedTask;
    internal Task PendingLocalMaskTask => _localMaskTask;
    internal Func<Task>? LocalMaskRenderGateAsync { get; set; }
    public bool IsLocalRangeMaskUpdating => IsLocalMaskVisible && IsSelectedLocalRangeRestricted &&
        _localRangeMask == null && (_localMaskIdentity == null || _localMaskIdentity != _failedLocalMaskIdentity);
    public Bitmap? LocalRangeMask => _localRangeMask;

    // Only inputs to the weight field belong here; local adjustments and global tone do not.
    private static string LocalMaskSettingsKey(EditSettings settings, LocalAdjustment local) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.Rotation, settings.HorizonRotation, settings.Crop, settings.Geometry,
            Decode = BaseDecodeSettings.From(settings).CacheKey,
            Wb = new { settings.Wb.Mode, settings.Wb.Kelvin, settings.Wb.Tint, settings.Wb.Gains },
            Local = new { local.Type, local.Cu, local.Cv, local.Angle, local.Feather,
                local.Rx, local.Ry, local.Outside, local.Luminance, local.Hue }
        });

    private bool IsCurrentLocalMask(LocalMaskIdentity identity)
    {
        if (!IsLocalMaskVisible || !ReferenceEquals(SelectedImage, identity.Image) || SelectedLocal?.Id != identity.LocalId ||
            PreviewImage?.PixelSize != identity.Size || _renderOutcomeChannelClosed) return false;
        var settings = CaptureLiveEditState();
        using var current = ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(identity.Size.Width, identity.Size.Height), PreviewImage);
        return ReferenceEquals(current?.Base, identity.Base) && LocalMaskSettingsKey(settings, SelectedLocal!) == identity.Settings;
    }

    private void RefreshLocalRangeMask()
    {
        var settings = SelectedImage == null ? new EditSettings() : CaptureLiveEditState();
        using var lease = IsLocalMaskVisible && IsSelectedLocalRangeRestricted &&
            SelectedImage is { } image && PreviewImage is { } preview
            ? ImageService.Previews.AcquireLocalRangeBase(image, settings, Math.Max(preview.PixelSize.Width, preview.PixelSize.Height), preview) : null;
        var identity = lease == null ? null : new LocalMaskIdentity(SelectedImage!, lease.Base,
            LocalMaskSettingsKey(settings, SelectedLocal!), SelectedLocal!.Id, PreviewImage!.PixelSize, lease.Base.Info.IsMonochrome);
        if (identity == _localMaskIdentity) { NotifyLocalMask(); return; }
        _localMaskCancellation?.Cancel();
        _localMaskIdentity = identity;
        _failedLocalMaskIdentity = null;
        if (_localRangeMask is { } previous) _bitmapRetirement.Retire(previous, () => false);
        _localRangeMask = null;
        NotifyLocalMask();
        if (identity == null) return;
        var held = ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(identity.Size.Width, identity.Size.Height), PreviewImage);
        if (held == null || !ReferenceEquals(held.Base, identity.Base)) { held?.Dispose(); return; }
        var cancellation = new CancellationTokenSource();
        _localMaskCancellation = cancellation;
        _localMaskTask = RenderLocalRangeMaskAsync(_localMaskTask, identity, held, settings,
            SelectedLocal! with { }, cancellation);
    }

    private async Task RenderLocalRangeMaskAsync(Task previous, LocalMaskIdentity identity,
        PreviewBaseSnapshot lease, EditSettings settings, LocalAdjustment local, CancellationTokenSource cancellation)
    {
        using (lease)
        using (cancellation)
        {
            WriteableBitmap? result = null;
            try
            {
                await previous;
                if (LocalMaskRenderGateAsync is { } gate) await gate();
                cancellation.Token.ThrowIfCancellationRequested();
                var color = HappyPhotonColors.LocalMaskColor;
                var tint = (uint)(color.R << 16 | color.G << 8 | color.B);
                result = await Task.Run(() => LocalRangeMaskRenderer.Render(lease.Base, settings, local,
                    identity.Size, tint, cancellation.Token), cancellation.Token);
                if (!cancellation.IsCancellationRequested && identity == _localMaskIdentity && IsCurrentLocalMask(identity))
                {
                    _localRangeMask = result; result = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested && identity == _localMaskIdentity && IsCurrentLocalMask(identity))
                {
                    _failedLocalMaskIdentity = identity;
                    ImageServiceHelpers.LogError($"Local mask render failed for '{identity.Image.FileName}': {ex.Message}");
                }
            }
            finally
            {
                result?.Dispose();
                if (ReferenceEquals(_localMaskCancellation, cancellation)) _localMaskCancellation = null;
                NotifyLocalMask();
            }
        }
    }

    private void NotifyLocalMask()
    {
        OnPropertyChanged(nameof(LocalRangeMask));
        OnPropertyChanged(nameof(IsLocalRangeMaskUpdating));
    }
}
