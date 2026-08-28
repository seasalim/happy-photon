using CommunityToolkit.Mvvm.ComponentModel;
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
        }

        foreach (var image in imageList)
            ApplyCapturePairIndicator(image);
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
            CapturePathComparer.Equals(candidate.FilePath, jpegPath));
    }
}
