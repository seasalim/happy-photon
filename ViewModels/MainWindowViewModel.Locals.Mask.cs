using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record LocalMaskIdentity(ImageFile Image, string? Base, LocalsFrame Frame, string Settings,
        string LocalId, bool Monochrome);

    private LocalMaskIdentity? _localMaskIdentity;

    private LocalMaskIdentity? _shownLocalMaskIdentity;

    private LocalMaskIdentity? _failedLocalMaskIdentity;

    private WriteableBitmap? _localRangeMask;

    private CancellationTokenSource? _localMaskCancellation;

    private Task _localMaskTask = Task.CompletedTask;

    private readonly List<LocalBrushStroke> _releasedBrushStrokes = [];

    private bool _ownsLocalMask;

    internal Task PendingLocalMaskTask => _localMaskTask;

    internal Func<Task>? LocalMaskRenderGateAsync { get; set; }

    public bool IsLocalRangeMaskUpdating => IsLocalMaskVisible && NeedsRequestedLocalMask &&
        _localMaskIdentity is { } identity && identity != _shownLocalMaskIdentity && identity != _failedLocalMaskIdentity;

    private bool NeedsRequestedLocalMask => SelectedLocal?.IsBrush == true || IsSelectedLocalRangeRestricted;

    private EditSettings LocalMaskSettings => IsBrushStrokeActive ? _localsGestureBefore! : CaptureLiveEditState();

    private LocalAdjustment? MaskLocal(EditSettings settings) => settings.Locals?.FirstOrDefault(l => l.Id == _selectedLocalId)
        ?? (IsBrushStrokeActive ? _localsGestureLocal : null);

    private bool OwnsLocalMask => IsLocalMaskVisible && (_localMaskIdentity ?? _shownLocalMaskIdentity) is { } identity &&
        ReferenceEquals(identity.Image, SelectedImage) && identity.LocalId == _selectedLocalId;

    public Bitmap? LocalRangeMask => _ownsLocalMask ? _localRangeMask : null;

    public IEnumerable<LocalBrushStroke> BrushOverlayStrokes { get; private set; } = [];

    // Only inputs to the weight field belong here; local adjustments and global tone do not.
    private static string LocalMaskSettingsKey(EditSettings settings, LocalAdjustment local) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.Rotation, settings.HorizonRotation, settings.Crop, settings.Geometry,
            Repairs = local.Luminance?.Enabled == true || local.Hue?.Enabled == true ? settings.Repairs : null,
            Wb = new { settings.Wb.Mode, settings.Wb.Kelvin, settings.Wb.Tint, settings.Wb.Gains },
            Local = new { local.Type, local.Cu, local.Cv, local.Angle, local.Feather,
                local.Rx, local.Ry, local.Outside, local.Luminance, local.Hue, local.Strokes }
        });

    // Compare loaded provenance and resolved decode facts, never a size-specific base reference.
    private static string? LocalMaskBaseKey(BaseImage? basis) => basis == null ? null :
        System.Text.Json.JsonSerializer.Serialize(new
        {
            basis.SourceWriteTime, basis.Info.Kind, basis.Info.IsRawSource, basis.Info.IsMonochrome,
            Decode = basis.Info.Decode.CacheKey, basis.Info.ProfileToken, basis.Info.ProfileStatus,
            basis.Info.CamMul, Matrix = basis.Info.CamToSrgb?.Cast<double>().ToArray(),
            basis.Info.AsShotKelvin, basis.Info.AsShotTint, basis.Info.HadIccProfile, basis.Info.IccDescription,
            basis.Info.ExifOrientationApplied, basis.Info.FullWidth, basis.Info.FullHeight,
            basis.Info.SourceExposureBiasEv, basis.Info.LensPrescriptionSummary
        });

    private bool IsCurrentLocalMask(LocalMaskIdentity identity)
    {
        if (!OwnsLocalMask || _renderOutcomeChannelClosed) return false;

        var settings = LocalMaskSettings;
        var local = MaskLocal(settings);
        if (local == null || LocalMaskSettingsKey(settings, local) != identity.Settings) return false;
        if (IsBrushStrokeActive) return true; // The pinned request owns its pre-stroke base and surface.
        if (LocalsFrame != identity.Frame) return false;
        if (identity.Base == null) return true;
        if (PreviewImage is not { } preview) return false;

        using var current = ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(preview.PixelSize.Width, preview.PixelSize.Height), preview);

        return LocalMaskBaseKey(current?.Base) == identity.Base;
    }

    private void InvalidateLocalMask()
    {
        _localMaskCancellation?.Cancel();
        _localMaskIdentity = null;
        _shownLocalMaskIdentity = null;
        _failedLocalMaskIdentity = null;
        _releasedBrushStrokes.Clear();
        if (_localRangeMask is { } previous) _bitmapRetirement.Retire(previous, () => false);

        _localRangeMask = null;
        NotifyLocalMask();
    }

    private void RefreshLocalRangeMask()
    {
        if (!OwnsLocalMask) InvalidateLocalMask();

        var settings = SelectedImage == null ? new EditSettings() : LocalMaskSettings;
        var local = MaskLocal(settings);

        if (IsBrushStrokeActive && _localMaskIdentity is { } pinned &&
            pinned.Settings == LocalMaskSettingsKey(settings, local!))
        {
            NotifyLocalMask();
            return;
        }

        var frame = IsBrushStrokeActive ? _localsGestureFrame : LocalsFrame;
        var visible = IsLocalMaskVisible && NeedsRequestedLocalMask && local != null && frame != null && PreviewImage != null;
        using var lease = visible && IsSelectedLocalRangeRestricted
            ? ImageService.Previews.AcquireLocalRangeBase(SelectedImage!, settings,
                Math.Max(PreviewImage!.PixelSize.Width, PreviewImage.PixelSize.Height), PreviewImage) : null;

        if (visible && IsSelectedLocalRangeRestricted && lease == null)
        {
            _localMaskCancellation?.Cancel();
            _localMaskIdentity = null;
            NotifyLocalMask();
            return;
        }

        var identity = !visible ? null :
            new LocalMaskIdentity(SelectedImage!, LocalMaskBaseKey(lease?.Base), frame!.Value,
                LocalMaskSettingsKey(settings, local!), local!.Id, IsMonochromeSource);

        if (identity == _localMaskIdentity)
        {
            NotifyLocalMask();
            return;
        }

        if (identity == null)
        {
            InvalidateLocalMask();
            return;
        }

        _localMaskCancellation?.Cancel();
        _localMaskIdentity = identity;
        _failedLocalMaskIdentity = null;
        NotifyLocalMask();
        var size = PreviewImage!.PixelSize;
        var held = lease == null ? null : ImageService.Previews.AcquireLocalRangeBase(identity.Image, settings,
            Math.Max(size.Width, size.Height), PreviewImage);

        if (lease != null && !ReferenceEquals(held?.Base, lease.Base))
        {
            held?.Dispose();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _localMaskCancellation = cancellation;
        _localMaskTask = RenderLocalRangeMaskAsync(_localMaskTask, identity, size, held, settings,
            local! with { }, cancellation);
    }

    private async Task RenderLocalRangeMaskAsync(Task previous, LocalMaskIdentity identity, PixelSize size,
        PreviewBaseSnapshot? lease, EditSettings settings, LocalAdjustment local, CancellationTokenSource cancellation)
    {
        using (lease)
        using (cancellation)
        {
            WriteableBitmap? result = null;

            try
            {
                await previous;
                cancellation.Token.ThrowIfCancellationRequested();
                if (LocalMaskRenderGateAsync is { } gate) await gate();

                cancellation.Token.ThrowIfCancellationRequested();
                var color = HappyPhotonColors.LocalMaskColor;
                var tint = (uint)(color.R << 16 | color.G << 8 | color.B);
                result = await Task.Run(() => LocalRangeMaskRenderer.Render(lease?.Base, identity.Frame, settings, local,
                    size, tint, cancellation.Token, identity.Monochrome), cancellation.Token);

                if (!cancellation.IsCancellationRequested && identity == _localMaskIdentity && IsCurrentLocalMask(identity))
                {
                    if (_localRangeMask is { } retired) _bitmapRetirement.Retire(retired, () => false);

                    _localRangeMask = result;
                    result = null;
                    _shownLocalMaskIdentity = identity;
                    _releasedBrushStrokes.Clear();
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
        RefreshLocalMaskPresentation();
        OnPropertyChanged(nameof(LocalRangeMask));
        OnPropertyChanged(nameof(IsLocalRangeMaskUpdating));
    }

    private void RefreshLocalMaskPresentation()
    {
        _ownsLocalMask = OwnsLocalMask;
        BrushOverlayStrokes = !IsLocalMaskVisible ? [] :
            LiveBrushStroke is { } live ? (_ownsLocalMask ? _releasedBrushStrokes : []).Append(live) :
            _ownsLocalMask ? _releasedBrushStrokes : [];
    }
}
