using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class LibraryImageState : ObservableObject
{
    private readonly List<ImageFile> _allImages = new();
    private readonly HashSet<ImageFile> _allImageSet = new(ReferenceEqualityComparer.Instance);
    private readonly Action<ImageFile, Bitmap> _retireThumbnail;

    public LibraryImageState()
        : this(static (_, bitmap) => bitmap.Dispose())
    {
    }

    internal LibraryImageState(Action<ImageFile, Bitmap> retireThumbnail) =>
        _retireThumbnail = retireThumbnail;

    [ObservableProperty]
    private ObservableCollection<ImageFile> _visibleImages = new();

    [ObservableProperty]
    private ImageFileTypeFilter _fileTypeFilter;

    [ObservableProperty]
    private FlagFilter _flagFilter;

    [ObservableProperty]
    private int _minimumRating;   // 0 = show all; not persisted, same as FlagFilter

    public event EventHandler? FilterChanged;
    public event EventHandler? StateChanged;

    public IReadOnlyList<ImageFile> AllImages => _allImages;
    public int TotalCount => _allImages.Count;
    public int VisibleCount => VisibleImages.Count;
    public int SelectedCount => VisibleImages.Count(i => i.IsSelected);

    public string PhotoCountText =>
        FileTypeFilter == ImageFileTypeFilter.All && FlagFilter == HappyPhoton.Models.FlagFilter.All &&
        MinimumRating == 0
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
    {
        var index = VisibleImages.IndexOf(image);
        if (index < 0 || VisibleImages.Count <= 1) return null;

        return index < VisibleImages.Count - 1 ? VisibleImages[index + 1] : VisibleImages[index - 1];
    }

    public bool MatchesCurrentFilters(ImageFile image) =>
        FileTypeFilter.Matches(image) && MatchesFlagFilter(image) && image.Rating >= MinimumRating;

    public bool MatchesCurrentFilters(ImageFile image, ImageFlag flag) =>
        FileTypeFilter.Matches(image) && MatchesFlagFilter(flag) && image.Rating >= MinimumRating;

    public bool MatchesCurrentFilters(ImageFile image, int rating) =>
        FileTypeFilter.Matches(image) && MatchesFlagFilter(image) && rating >= MinimumRating;

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
        OnPropertyChanged(nameof(SelectedCount));
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

        OnPropertyChanged(nameof(SelectedCount));
    }

    public void SelectAllVisible()
    {
        foreach (var image in VisibleImages)
        {
            image.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedCount));
    }

    public void DeselectAllVisible()
    {
        foreach (var image in VisibleImages)
        {
            image.IsSelected = false;
        }

        OnPropertyChanged(nameof(SelectedCount));
    }

    public IEnumerable<ImageFile> GetSelectedImages() =>
        VisibleImages.Where(i => i.IsSelected);

    private int IndexOfVisible(ImageFile? image) =>
        image == null ? -1 : VisibleImages.IndexOf(image);

    private void ApplyFilter()
    {
        var visible = _allImages
            .Where(image => FileTypeFilter.Matches(image))
            .Where(MatchesFlagFilter)
            .Where(image => image.Rating >= MinimumRating)
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
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(PhotoCountText));
        OnPropertyChanged(nameof(EmptyMessage));
        StateChanged?.Invoke(this, EventArgs.Empty);
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
}
