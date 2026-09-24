using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record LocalMaskIdentity(ImageFile Image, BaseImage? Base, LocalsFrame Frame, string Settings,
        string LocalId, PixelSize Size, bool Monochrome);
    private LocalMaskIdentity? _localMaskIdentity;
    private LocalMaskIdentity? _failedLocalMaskIdentity;
    private WriteableBitmap? _localRangeMask;
    private CancellationTokenSource? _localMaskCancellation;
    private Task _localMaskTask = Task.CompletedTask;
    internal Task PendingLocalMaskTask => _localMaskTask;
    internal Func<Task>? LocalMaskRenderGateAsync { get; set; }
    public bool IsLocalRangeMaskUpdating => IsLocalMaskVisible && NeedsRequestedLocalMask &&
        _localRangeMask == null && (_localMaskIdentity == null || _localMaskIdentity != _failedLocalMaskIdentity);
    private bool NeedsRequestedLocalMask => SelectedLocal?.IsBrush == true || IsSelectedLocalRangeRestricted;
    private EditSettings LocalMaskSettings => IsBrushStrokeActive ? _localsGestureBefore! : CaptureLiveEditState();
    private LocalAdjustment? MaskLocal(EditSettings settings) => settings.Locals?.FirstOrDefault(l => l.Id == _selectedLocalId)
        ?? (IsBrushStrokeActive ? _localsGestureLocal : null);
    public Bitmap? LocalRangeMask => _localRangeMask;

    // Only inputs to the weight field belong here; local adjustments and global tone do not.
    private static string LocalMaskSettingsKey(EditSettings settings, LocalAdjustment local) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.Rotation, settings.HorizonRotation, settings.Crop, settings.Geometry,
            Decode = BaseDecodeSettings.From(settings).CacheKey,
            Wb = new { settings.Wb.Mode, settings.Wb.Kelvin, settings.Wb.Tint, settings.Wb.Gains },
            Local = new { local.Type, local.Cu, local.Cv, local.Angle, local.Feather,
                local.Rx, local.Ry, local.Outside, local.Luminance, local.Hue, local.Strokes }
        });

    private bool IsCurrentLocalMask(LocalMaskIdentity identity)
    {
        if (!IsLocalMaskVisible || !ReferenceEquals(SelectedImage, identity.Image) || SelectedLocal?.Id != identity.LocalId ||
            !IsBrushStrokeActive && PreviewImage?.PixelSize != identity.Size || _renderOutcomeChannelClosed) return false;
        var settings = LocalMaskSettings;
        var local = MaskLocal(settings);
        if (local == null || LocalMaskSettingsKey(settings, local) != identity.Settings) return false;
        if (IsBrushStrokeActive) return true; // The pinned request owns its pre-stroke base and surface.
        if (identity.Base == null) return LocalsFrame == identity.Frame;
        using var current = ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(identity.Size.Width, identity.Size.Height), PreviewImage);
        return ReferenceEquals(current?.Base, identity.Base);
    }

    private void RefreshLocalRangeMask()
    {
        var settings = SelectedImage == null ? new EditSettings() : LocalMaskSettings;
        var local = MaskLocal(settings);
        if (IsBrushStrokeActive && _localMaskIdentity is { } pinned &&
            ReferenceEquals(pinned.Image, SelectedImage) && pinned.LocalId == local?.Id &&
            pinned.Settings == LocalMaskSettingsKey(settings, local))
        { NotifyLocalMask(); return; }
        var frame = IsBrushStrokeActive ? _localsGestureFrame : LocalsFrame;
        var visible = IsLocalMaskVisible && NeedsRequestedLocalMask && local != null && frame != null && PreviewImage != null;
        using var lease = visible && IsSelectedLocalRangeRestricted
            ? ImageService.Previews.AcquireLocalRangeBase(SelectedImage!, settings,
                Math.Max(PreviewImage!.PixelSize.Width, PreviewImage.PixelSize.Height), PreviewImage) : null;
        var identity = !visible || IsSelectedLocalRangeRestricted && lease == null ? null :
            new LocalMaskIdentity(SelectedImage!, lease?.Base, frame!.Value, LocalMaskSettingsKey(settings, local!),
                local!.Id, PreviewImage!.PixelSize, IsMonochromeSource);
        if (identity == _localMaskIdentity) { NotifyLocalMask(); return; }
        _localMaskCancellation?.Cancel();
        _localMaskIdentity = identity;
        _failedLocalMaskIdentity = null;
        if (_localRangeMask is { } previous) _bitmapRetirement.Retire(previous, () => false);
        _localRangeMask = null;
        NotifyLocalMask();
        if (identity == null) return;
        var held = identity.Base == null ? null : ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(identity.Size.Width, identity.Size.Height), PreviewImage);
        if (identity.Base != null && !ReferenceEquals(held?.Base, identity.Base)) { held?.Dispose(); return; }
        var cancellation = new CancellationTokenSource();
        _localMaskCancellation = cancellation;
        _localMaskTask = RenderLocalRangeMaskAsync(_localMaskTask, identity, held, settings,
            local! with { }, cancellation);
    }

    private async Task RenderLocalRangeMaskAsync(Task previous, LocalMaskIdentity identity,
        PreviewBaseSnapshot? lease, EditSettings settings, LocalAdjustment local, CancellationTokenSource cancellation)
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
                result = await Task.Run(() => LocalRangeMaskRenderer.Render(lease?.Base, identity.Frame, settings, local,
                    identity.Size, tint, cancellation.Token, identity.Monochrome), cancellation.Token);
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
