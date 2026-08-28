using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _restoringShowCapturePairs;

    private static readonly StringComparer CapturePathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    [ObservableProperty]
    private bool _showCapturePairs;

    private readonly HashSet<string> _pairedRawPaths = new(CapturePathComparer);
    private readonly HashSet<string> _pairedJpegPaths = new(CapturePathComparer);
    private readonly Dictionary<string, string> _pairedJpegByRawPath =
        new(CapturePathComparer);
    private readonly Dictionary<string, string> _pairedRawByJpegPath =
        new(CapturePathComparer);
    private CaptureMemberViewportHandoff? _captureMemberViewportHandoff;

    public Func<NormalizedViewport?>? CaptureDevelopViewport { get; set; }
    public Action<ImageFile, NormalizedViewport>? RestoreDevelopViewport { get; set; }

    public bool IsViewingPairedRaw =>
        SelectedImage?.IsRaw == true && GetPairedMember(SelectedImage) != null;

    public bool CanSwitchCaptureMember => CanSwitchCaptureMemberNow();

    private void ConfigureCapturePairs()
    {
        Browse.CaptureIsVisible = IsCaptureVisible;
        Browse.CaptureMatchesFileType = CaptureMatchesFileType;
    }

    public void RestoreShowCapturePairs(bool value)
    {
        _restoringShowCapturePairs = true;
        try { ShowCapturePairs = value; }
        finally { _restoringShowCapturePairs = false; }
    }

    partial void OnShowCapturePairsChanged(bool value)
    {
        var selected = Browse.AllImages.Where(image => image.IsSelected).ToList();
        var active = SelectedImage;
        RecomputeCapturePairs(Browse.AllImages);

        if (value)
        {
            var mappedSelection = selected.Select(MapRawToJpeg)
                .Where(image => image != null)
                .Cast<ImageFile>()
                .Distinct<ImageFile>(ReferenceEqualityComparer.Instance);
            foreach (var image in mappedSelection) image.IsSelected = true;
            SelectedImage = MapRawToJpeg(active);
        }

        Browse.RefreshFilters();
        RefreshVisibleThumbnailQueue();
        NotifyCaptureMemberStateChanged();
        if (!_restoringShowCapturePairs)
        {
            _ = PersistBrowsePreferenceAsync("Capture-pair");
        }
    }

    private bool IsCaptureVisible(ImageFile image) =>
        !ShowCapturePairs || !_pairedRawPaths.Contains(image.FilePath);

    private bool CaptureMatchesFileType(
        ImageFileTypeFilter filter,
        ImageFile image) =>
        ShowCapturePairs && filter == ImageFileTypeFilter.Raw
            ? image.IsRaw || _pairedJpegPaths.Contains(image.FilePath)
            : filter.Matches(image);

    private void RecomputeCapturePairs(IEnumerable<ImageFile> images)
    {
        var imageList = images.ToList();
        _pairedRawPaths.Clear();
        _pairedJpegPaths.Clear();
        _pairedJpegByRawPath.Clear();
        _pairedRawByJpegPath.Clear();

        foreach (var capture in CapturePairingService.GroupCaptures(
                     imageList.Select(image => image.FilePath)))
        {
            if (capture.ImageIds.Count != 2) continue;
            var rawPath = capture.ImageIds.Single(path =>
                ImageFile.RawExtensions.Contains(Path.GetExtension(path)));
            var jpegPath = capture.ImageIds.Single(path =>
                !CapturePathComparer.Equals(path, rawPath));
            _pairedRawPaths.Add(rawPath);
            _pairedJpegPaths.Add(jpegPath);
            _pairedJpegByRawPath[rawPath] = jpegPath;
            _pairedRawByJpegPath[jpegPath] = rawPath;
        }

        foreach (var image in imageList)
            ApplyCapturePairIndicator(image);
        NotifyCaptureMemberStateChanged();
    }

    private void RefreshCapturePairsAfterRemoval()
    {
        RecomputeCapturePairs(Browse.AllImages);
        Browse.RefreshFilters();
        RefreshVisibleThumbnailQueue();
    }

    private void ApplyCapturePairIndicator(ImageFile image) =>
        image.IsRawJpegPair = ShowCapturePairs &&
            _pairedJpegPaths.Contains(image.FilePath);

    private ImageFile? MapRawToJpeg(ImageFile? image)
    {
        if (image == null || !_pairedJpegByRawPath.TryGetValue(
                image.FilePath, out var jpegPath))
            return image;

        return Browse.AllImages.FirstOrDefault(candidate =>
            candidate.Version == 1 &&
            CapturePathComparer.Equals(candidate.FilePath, jpegPath));
    }

    private ImageFile? GetPairedMember(ImageFile? image)
    {
        if (!ShowCapturePairs || image == null) return null;
        var pairPath = image.IsRaw
            ? _pairedJpegByRawPath.GetValueOrDefault(image.FilePath)
            : _pairedRawByJpegPath.GetValueOrDefault(image.FilePath);
        if (pairPath == null) return null;
        return Browse.AllImages.FirstOrDefault(candidate =>
            candidate.Version == 1 &&
            CapturePathComparer.Equals(candidate.FilePath, pairPath));
    }

    private ImageFile? VisibleRepresentative(ImageFile? image) =>
        ShowCapturePairs ? MapRawToJpeg(image) : image;

    private IReadOnlyList<ImageFile> ResolveAssessmentCompanions(
        IReadOnlyList<ImageFile> targets)
    {
        if (!ShowCapturePairs || targets.Count == 0) return [];
        var targetSet = targets.ToHashSet(ReferenceEqualityComparer.Instance);
        return targets.Where(target => target.Version == 1)
            .Select(GetPairedMember)
            .Where(image => image != null && !targetSet.Contains(image))
            .Cast<ImageFile>()
            .Distinct<ImageFile>(ReferenceEqualityComparer.Instance)
            .ToArray();
    }

    [RelayCommand(CanExecute = nameof(CanSwitchCaptureMemberNow))]
    private void SwitchCaptureMember()
    {
        if (!CanSwitchCaptureMemberNow() || GetPairedMember(SelectedImage) is not { } pair)
            return;

        var viewport = _captureMemberViewportHandoff is { } handoff &&
            ReferenceEquals(handoff.Target, SelectedImage)
                ? handoff.Viewport
                : CaptureDevelopViewport?.Invoke();
        if (viewport is { } capturedViewport)
            _captureMemberViewportHandoff = new(pair, capturedViewport);
        SelectedImage = pair;
    }

    private bool CanSwitchCaptureMemberNow() =>
        IsDevelopMode && !IsFullScreenMode && SelectedImage?.Version == 1 &&
        GetPairedMember(SelectedImage) != null;

    private bool PrepareCaptureMemberViewport(ImageFile image)
    {
        if (_captureMemberViewportHandoff is not { } handoff ||
            !ReferenceEquals(handoff.Target, image))
            return false;
        IsZoomFitMode = handoff.Viewport.ZoomRelativeToFit == 1;
        return true;
    }

    private void RestoreCaptureMemberViewportAfterPaint(ImageFile image)
    {
        if (_captureMemberViewportHandoff is not { } handoff ||
            !ReferenceEquals(handoff.Target, image))
            return;
        _captureMemberViewportHandoff = null;
        RestoreDevelopViewport?.Invoke(handoff.Target, handoff.Viewport);
    }

    private void KeepCaptureMemberViewportOnlyFor(ImageFile? image)
    {
        if (_captureMemberViewportHandoff is { } handoff &&
            !ReferenceEquals(handoff.Target, image))
            _captureMemberViewportHandoff = null;
    }

    private void NotifyCaptureMemberStateChanged()
    {
        OnPropertyChanged(nameof(IsViewingPairedRaw));
        OnPropertyChanged(nameof(CanSwitchCaptureMember));
        SwitchCaptureMemberCommand.NotifyCanExecuteChanged();
    }

    private sealed record CaptureMemberViewportHandoff(
        ImageFile Target,
        NormalizedViewport Viewport);
}
