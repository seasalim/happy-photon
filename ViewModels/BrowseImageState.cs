using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class BrowseImageState : ObservableObject
{
    private readonly List<ImageFile> _allImages = new();
    private readonly HashSet<ImageFile> _allImageSet = new(ReferenceEqualityComparer.Instance);
    private readonly Action<ImageFile, Bitmap> _retireThumbnail;

    internal Func<ImageFile, bool> CaptureIsVisible { get; set; } = static _ => true;
    internal Func<ImageFileTypeFilter, ImageFile, bool> CaptureMatchesFileType { get; set; } =
        static (filter, image) => filter.Matches(image);

    public BrowseImageState()
        : this(static (_, bitmap) => bitmap.Dispose())
    {
    }

    internal BrowseImageState(Action<ImageFile, Bitmap> retireThumbnail) =>
        _retireThumbnail = retireThumbnail;

    [ObservableProperty]
    private ObservableCollection<ImageFile> _visibleImages = new();

    [ObservableProperty]
    private ImageFileTypeFilter _fileTypeFilter;

    [ObservableProperty]
    private FlagFilter _flagFilter;

    [ObservableProperty]
    private int _minimumRating;   // 0 = show all; not persisted, same as FlagFilter

    [ObservableProperty]
    private ColorLabelFilter _colorLabelFilter;

    public event EventHandler? FilterChanged;
    public event EventHandler? StateChanged;

    public IReadOnlyList<ImageFile> AllImages => _allImages;
    public int TotalCount => _allImages.Count(CaptureIsVisible);
    public int VisibleCount => VisibleImages.Count;
    public int SelectedCount => VisibleImages.Count(i => i.IsSelected);
    public bool HasVisibleImages => VisibleCount > 0;
    public bool HasSelectedImages => SelectedCount > 0;

    public string PhotoCountText =>
        FileTypeFilter == ImageFileTypeFilter.All && FlagFilter == HappyPhoton.Models.FlagFilter.All &&
        MinimumRating == 0 && ColorLabelFilter == ColorLabelFilter.All
            ? $"{TotalCount} photos"
            : $"{VisibleCount} of {TotalCount} photos";

    public string EmptyMessage =>
        TotalCount == 0
            ? "Select a folder to view images"
            : MinimumRating > 0
                ? $"No images rated {MinimumRating}+ match this filter"
            : FlagFilter == HappyPhoton.Models.FlagFilter.Picked
                ? "No picked images match this filter"
            : FlagFilter == HappyPhoton.Models.FlagFilter.Rejected
                ? "No rejected images match this filter"
            : FileTypeFilter switch
            {
                ImageFileTypeFilter.Raw => "No RAW files in this folder",
                ImageFileTypeFilter.Jpeg => "No JPEG files in this folder",
                _ => "No images match the current filter"
            };

    partial void OnFileTypeFilterChanged(ImageFileTypeFilter value)
    {
        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFlagFilterChanged(FlagFilter value)
    {
        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnMinimumRatingChanged(int value)
    {
        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnColorLabelFilterChanged(ColorLabelFilter value)
    {
        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetImages(IEnumerable<ImageFile> images)
    {
        var replacements = images.ToList();
        var retained = replacements.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var image in _allImages)
        {
            if (!retained.Contains(image)) ReplaceThumbnail(image, null);
        }
        _allImages.Clear();
        _allImages.AddRange(replacements);
        _allImageSet.Clear();
        _allImageSet.UnionWith(replacements);
        ApplyFilter();
    }

    public void RefreshFilters()
    {
        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearFilters()
    {
#pragma warning disable MVVMTK0034 // Atomic multi-property update requires backing fields.
        var fileTypeChanged = _fileTypeFilter != ImageFileTypeFilter.All;
        var flagChanged = _flagFilter != HappyPhoton.Models.FlagFilter.All;
        var ratingChanged = _minimumRating != 0;
        var colorLabelChanged = _colorLabelFilter != ColorLabelFilter.All;
        if (!fileTypeChanged && !flagChanged && !ratingChanged && !colorLabelChanged)
            return;

        _fileTypeFilter = ImageFileTypeFilter.All;
        _flagFilter = HappyPhoton.Models.FlagFilter.All;
        _minimumRating = 0;
        _colorLabelFilter = ColorLabelFilter.All;

        if (fileTypeChanged) OnPropertyChanged(nameof(FileTypeFilter));
        if (flagChanged) OnPropertyChanged(nameof(FlagFilter));
        if (ratingChanged) OnPropertyChanged(nameof(MinimumRating));
        if (colorLabelChanged) OnPropertyChanged(nameof(ColorLabelFilter));

        ApplyFilter();
        FilterChanged?.Invoke(this, EventArgs.Empty);
#pragma warning restore MVVMTK0034
    }

    public bool ContainsVisible(ImageFile? image) =>
        image != null && VisibleImages.Contains(image);

    public bool Contains(ImageFile image) => _allImageSet.Contains(image);

    public void ReplaceThumbnail(ImageFile image, Bitmap? thumbnail)
    {
        var previous = image.SwapThumbnail(thumbnail);
        if (previous != null) _retireThumbnail(image, previous);
    }

    public ImageFile? FirstVisible() =>
        VisibleImages.Count > 0 ? VisibleImages[0] : null;

    public ImageFile? LastVisible() =>
        VisibleImages.Count > 0 ? VisibleImages[^1] : null;

    public ImageFile? PreviousVisible(ImageFile? image)
    {
        var index = IndexOfVisible(image);
        return index > 0 ? VisibleImages[index - 1] : null;
    }

    public ImageFile? NextVisible(ImageFile? image)
    {
        var index = IndexOfVisible(image);
        return index >= 0 && index < VisibleImages.Count - 1 ? VisibleImages[index + 1] : null;
    }

    public ImageFile? MoveVisible(ImageFile? image, int offset)
    {
        var index = IndexOfVisible(image);
        if (index < 0) return null;

        var newIndex = index + offset;
        return newIndex >= 0 && newIndex < VisibleImages.Count ? VisibleImages[newIndex] : null;
    }

    public ImageFile? ReplacementAfterRemoval(ImageFile image)
        => ReplacementAfterRemoval(image, _ => true);

    public ImageFile? ReplacementAfterRemoval(
        ImageFile image,
        Func<ImageFile, bool> canReplace)
    {
        var index = VisibleImages.IndexOf(image);
        if (index < 0 || VisibleImages.Count <= 1) return null;

        for (var distance = 1; distance < VisibleImages.Count; distance++)
        {
            var next = index + distance;
            if (next < VisibleImages.Count && canReplace(VisibleImages[next]))
                return VisibleImages[next];
            var previous = index - distance;
            if (previous >= 0 && canReplace(VisibleImages[previous]))
                return VisibleImages[previous];
        }
        return null;
    }

    public bool MatchesCurrentFilters(ImageFile image) =>
        CaptureIsVisible(image) && CaptureMatchesFileType(FileTypeFilter, image) &&
        MatchesFlagFilter(image) &&
        image.Rating >= MinimumRating && MatchesColorLabelFilter(image.ColorLabel);

    public bool MatchesCurrentFilters(ImageFile image, ImageFlag flag) =>
        CaptureIsVisible(image) && CaptureMatchesFileType(FileTypeFilter, image) &&
        MatchesFlagFilter(flag) &&
        image.Rating >= MinimumRating && MatchesColorLabelFilter(image.ColorLabel);

    public bool MatchesCurrentFilters(ImageFile image, int rating) =>
        CaptureIsVisible(image) && CaptureMatchesFileType(FileTypeFilter, image) &&
        MatchesFlagFilter(image) &&
        rating >= MinimumRating && MatchesColorLabelFilter(image.ColorLabel);

    public bool MatchesCurrentFilters(ImageFile image, ColorLabel colorLabel) =>
        CaptureIsVisible(image) && CaptureMatchesFileType(FileTypeFilter, image) &&
        MatchesFlagFilter(image) &&
        image.Rating >= MinimumRating && MatchesColorLabelFilter(colorLabel);

    public IReadOnlyList<ImageFile> GetRejectedImages() =>
        _allImages.Where(image => image.Flag == ImageFlag.Rejected).ToList();

    public void Remove(ImageFile image)
    {
        _allImages.Remove(image);
        _allImageSet.Remove(image);
        VisibleImages.Remove(image);
        ReplaceThumbnail(image, null);
        NotifyCountsChanged();
    }

    public void InsertVersion(ImageFile image)
    {
        var index = _allImages.FindLastIndex(candidate =>
            string.Equals(candidate.FilePath, image.FilePath,
                StringComparison.OrdinalIgnoreCase));
        _allImages.Insert(index < 0 ? _allImages.Count : index + 1, image);
        _allImageSet.Add(image);
        ApplyFilter();
    }

    public void RemoveRange(IEnumerable<ImageFile> images)
    {
        foreach (var image in images.ToList())
        {
            _allImages.Remove(image);
            _allImageSet.Remove(image);
            VisibleImages.Remove(image);
            ReplaceThumbnail(image, null);
        }

        NotifyCountsChanged();
    }

    public void DisposeThumbnails()
    {
        foreach (var image in _allImages)
        {
            ReplaceThumbnail(image, null);
        }
    }

    public void ToggleSelection(ImageFile image)
    {
        if (!ContainsVisible(image)) return;

        image.IsSelected = !image.IsSelected;
        NotifySelectedCountChanged();
    }

    public void SelectOnly(ImageFile image)
    {
        foreach (var candidate in VisibleImages)
        {
            candidate.IsSelected = ReferenceEquals(candidate, image);
        }

        NotifySelectedCountChanged();
    }

    public void SelectRange(ImageFile fromImage, ImageFile toImage)
    {
        var fromIndex = VisibleImages.IndexOf(fromImage);
        var toIndex = VisibleImages.IndexOf(toImage);

        if (fromIndex < 0 || toIndex < 0) return;

        var start = Math.Min(fromIndex, toIndex);
        var end = Math.Max(fromIndex, toIndex);

        for (var i = start; i <= end; i++)
        {
            VisibleImages[i].IsSelected = true;
        }

        NotifySelectedCountChanged();
    }

    public void SelectAllVisible()
    {
        foreach (var image in VisibleImages)
        {
            image.IsSelected = true;
        }

        NotifySelectedCountChanged();
    }

    public void DeselectAllVisible()
    {
        foreach (var image in VisibleImages)
        {
            image.IsSelected = false;
        }

        NotifySelectedCountChanged();
    }

    public IEnumerable<ImageFile> GetSelectedImages() =>
        VisibleImages.Where(i => i.IsSelected);

    private int IndexOfVisible(ImageFile? image) =>
        image == null ? -1 : VisibleImages.IndexOf(image);

    private void ApplyFilter()
    {
        var visible = _allImages
            .Where(CaptureIsVisible)
            .Where(image => CaptureMatchesFileType(FileTypeFilter, image))
            .Where(MatchesFlagFilter)
            .Where(image => image.Rating >= MinimumRating)
            .Where(image => MatchesColorLabelFilter(image.ColorLabel))
            .ToList();
        var visibleSet = visible.ToHashSet();

        foreach (var image in _allImages)
        {
            if (!visibleSet.Contains(image))
            {
                image.IsSelected = false;
            }
        }

        VisibleImages = new ObservableCollection<ImageFile>(visible);
        NotifyCountsChanged();
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasVisibleImages));
        NotifySelectedCountChanged();
        OnPropertyChanged(nameof(PhotoCountText));
        OnPropertyChanged(nameof(EmptyMessage));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySelectedCountChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedImages));
    }

    private bool MatchesFlagFilter(ImageFile image) =>
        MatchesFlagFilter(image.Flag);

    private bool MatchesFlagFilter(ImageFlag flag) =>
        FlagFilter switch
        {
            HappyPhoton.Models.FlagFilter.Picked => flag == ImageFlag.Picked,
            HappyPhoton.Models.FlagFilter.Rejected => flag == ImageFlag.Rejected,
            _ => true
        };

    private bool MatchesColorLabelFilter(ColorLabel colorLabel) =>
        ColorLabelFilter == ColorLabelFilter.All ||
        (int)ColorLabelFilter - 1 == (int)colorLabel;
}
