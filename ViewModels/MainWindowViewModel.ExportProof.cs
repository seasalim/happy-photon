using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record ExportProofSize(string Name, int? MaxDimension)
{
    public string Label => MaxDimension is { } pixels ? $"{Name} · {pixels} PX" : $"{Name} · No resizing";
}

public partial class MainWindowViewModel
{
    private bool _proofIsDisplayed;
    private bool _proofUpdatePending;
    private OutputColorSpace _displayedProofColorSpace = OutputColorSpace.Srgb;
    private ExportProofSize? _displayedProofSize;
    private ExportProofSize? _selectedExportProofSize;
    private Task? _proofTask;

    public IReadOnlyList<ExportProofSize> ExportProofSizes { get; private set; } = [];
    public ExportProofSize? SelectedExportProofSize
    {
        get => _selectedExportProofSize;
        set
        {
            if (value == null || !ExportProofSizes.Contains(value) ||
                !SetProperty(ref _selectedExportProofSize, value)) return;
            RequestExportProofRefresh();
        }
    }

    private void RefreshExportProofSizes()
    {
        ExportProofSizes = ExportSettings.GetActiveVariants()
            .Where(variant => variant.MaxDimension is null ||
                ExportSettings.IsValidSize(variant.MaxDimension.Value))
            .Select(variant => new ExportProofSize(variant.Name switch
            {
                "hi-res" => "Full size",
                "web" => "Web",
                _ => "Small"
            }, variant.MaxDimension)).ToArray();
        _selectedExportProofSize = ExportProofSizes.FirstOrDefault(size =>
            size.Name == _selectedExportProofSize?.Name) ?? ExportProofSizes.FirstOrDefault();
        OnPropertyChanged(nameof(ExportProofSizes));
        OnPropertyChanged(nameof(SelectedExportProofSize));
    }

    private static bool ChangesProofSizes(string? property) => property is
        nameof(ExportSettings.ExportHiRes) or nameof(ExportSettings.ExportWeb) or
        nameof(ExportSettings.ExportSmall) or nameof(ExportSettings.WebMaxSize) or
        nameof(ExportSettings.SmallMaxSize);

    private static bool ChangesProofPixels(string? property) =>
        ChangesProofSizes(property) || property is nameof(ExportSettings.OutputColorSpace) or
            nameof(ExportSettings.OutputSharpening);

    public bool IsExportProofCaptionVisible =>
        IsExportMode && !HasNoExportCaptures && PreviewImage != null;
    public string ExportProofCaption => FormatExportProofCaption(
        _proofIsDisplayed, _displayedProofSize, _displayedProofColorSpace) +
        (_proofUpdatePending && ExportSettings.ShowProof ? " · UPDATING…" : string.Empty);

    public PixelSize ExportPreviewNativePixelSize
    {
        get
        {
            if (_proofIsDisplayed) return PreviewImage?.PixelSize ?? default;
            if (OriginalViewPixelSize.Width > 0 && OriginalViewPixelSize.Height > 0)
                return OriginalViewPixelSize;
            if (SelectedImage is { } image)
            {
                var size = RenderGeometry.CalculateOriginalViewSize(
                    image.PixelWidth, image.PixelHeight, image.EditSettings);
                if (size.Width > 0 && size.Height > 0) return size;
            }
            return PreviewImage?.PixelSize ?? default;
        }
    }

    partial void OnOriginalViewPixelSizeChanged(PixelSize value) =>
        OnPropertyChanged(nameof(ExportPreviewNativePixelSize));

    private void RequestExportProofRefresh()
    {
        var image = SelectedImage;
        if (!IsExportMode || !ExportSettings.ShowProof || image == null ||
            _renderOutcomeChannelClosed) return;

        var generation = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: false);
        _previewLoadingCts?.Cancel();
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        _ = RefreshExportProofAsync(image, generation, requestCts);
    }

    private void RestoreExportPreview()
    {
        SetProofDisplayed(false);
        var image = SelectedImage;
        if (!IsExportMode || image == null || _renderOutcomeChannelClosed) return;

        var generation = ReserveRenderOutcome();
        ApplySurfaceClearOutcome(image, generation);
        _ = LoadPreviewAsync(image, generation);
    }

    private async Task RefreshExportProofAsync(
        ImageFile image,
        long generation,
        CancellationTokenSource requestCts)
    {
        using var previewActivity = BeginInitialPreviewActivity();
        try
        {
            await LoadExportProofAsync(
                image,
                CaptureRestingSettings(),
                generation,
                requestCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                _previewLoadingCts = null;
            }
            requestCts.Dispose();
        }
    }

    private Task<bool> LoadExportProofAsync(
        ImageFile image,
        EditSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        var task = RenderExportProofAsync(
            image, settings, generation, cancellationToken);
        var pending = _proofTask;
        _proofTask = pending is { IsCompleted: false }
            ? Task.WhenAll(pending, task)
            : task;
        return task;
    }

    private async Task<bool> RenderExportProofAsync(
        ImageFile image,
        EditSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        var size = SelectedExportProofSize;
        if (size == null)
        {
            _proofUpdatePending = false;
            OnPropertyChanged(nameof(ExportProofCaption));
            return false;
        }
        var colorSpace = ExportSettings.OutputColorSpace;
        _proofUpdatePending = true;
        OnPropertyChanged(nameof(ExportProofCaption));
        try
        {
            var bitmap = await ImageService.Previews.RenderProofAsync(
                image,
                settings,
                size.MaxDimension,
                colorSpace,
                ExportSettings.OutputSharpening,
                cancellationToken);
            if (bitmap == null || cancellationToken.IsCancellationRequested ||
                !IsExportMode ||
                !ExportSettings.ShowProof ||
                generation != LatestPreviewOutcomeGeneration ||
                !ReferenceEquals(SelectedImage, image))
            {
                bitmap?.Dispose();
                return false;
            }

            ReplaceExportProof(bitmap, size, colorSpace);
            return true;
        }
        finally
        {
            if (generation == LatestPreviewOutcomeGeneration)
            {
                _proofUpdatePending = false;
                OnPropertyChanged(nameof(ExportProofCaption));
            }
        }
    }

    internal void ReplaceExportProof(Bitmap bitmap, ExportProofSize size, OutputColorSpace colorSpace)
    {
        _displayedProofSize = size;
        _displayedProofColorSpace = colorSpace;
        _proofUpdatePending = false;
        CancelRestingPreview(clearParent: true);
        ReplacePreviewImage(bitmap, PreviewPaintSource.FreshRender, isProof: true);
        _hasPromotableEditedRender = true;
    }

    private void SetProofDisplayed(bool value)
    {
        _proofIsDisplayed = value;
        if (!value)
        {
            _displayedProofColorSpace = OutputColorSpace.Srgb;
            _proofUpdatePending = false;
        }
        OnPropertyChanged(nameof(ExportProofCaption));
        OnPropertyChanged(nameof(PreviewDisplayColorSpace));
        OnPropertyChanged(nameof(ExportPreviewNativePixelSize));
    }

    internal static string FormatExportProofCaption(
        bool proofIsDisplayed,
        ExportProofSize? size,
        OutputColorSpace outputColorSpace) =>
        !proofIsDisplayed ? "PREVIEW · edits applied" :
        $"PROOF · {size?.Label} · " +
        (outputColorSpace == OutputColorSpace.Srgb ? "sRGB" : "Display P3");
}
