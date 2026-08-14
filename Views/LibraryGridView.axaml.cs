using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class LibraryGridView : UserControl
{
    public static readonly StyledProperty<ObservableCollection<ImageFile>?> ImagesProperty =
        AvaloniaProperty.Register<LibraryGridView, ObservableCollection<ImageFile>?>(nameof(Images));

    public static readonly StyledProperty<ImageFile?> SelectedImageProperty =
        AvaloniaProperty.Register<LibraryGridView, ImageFile?>(nameof(SelectedImage), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> TotalImageCountProperty =
        AvaloniaProperty.Register<LibraryGridView, int>(nameof(TotalImageCount));

    public static readonly StyledProperty<string> EmptyMessageTextProperty =
        AvaloniaProperty.Register<LibraryGridView, string>(nameof(EmptyMessageText), "Select a folder to view images");

    public static readonly StyledProperty<string> EmptyHeadingTextProperty =
        AvaloniaProperty.Register<LibraryGridView, string>(
            nameof(EmptyHeadingText),
            "Select a folder to view photographs");

    public static readonly StyledProperty<bool> SuppressEmptyStateProperty =
        AvaloniaProperty.Register<LibraryGridView, bool>(nameof(SuppressEmptyState));

    public static readonly StyledProperty<ImageFileTypeFilter> FileTypeFilterProperty =
        AvaloniaProperty.Register<LibraryGridView, ImageFileTypeFilter>(
            nameof(FileTypeFilter),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<FlagFilter> FlagFilterProperty =
        AvaloniaProperty.Register<LibraryGridView, FlagFilter>(
            nameof(FlagFilter),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumRatingProperty =
        AvaloniaProperty.Register<LibraryGridView, int>(
            nameof(MinimumRating),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ColorLabelFilter> ColorLabelFilterProperty =
        AvaloniaProperty.Register<LibraryGridView, ColorLabelFilter>(
            nameof(ColorLabelFilter),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowBurstsProperty =
        AvaloniaProperty.Register<LibraryGridView, bool>(
            nameof(ShowBursts),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<LibraryThumbnailSize> ThumbnailSizeProperty =
        AvaloniaProperty.Register<LibraryGridView, LibraryThumbnailSize>(
            nameof(ThumbnailSize),
            LibraryThumbnailSize.Medium,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<LibraryGridView, double> ImageViewportWidthProperty =
        AvaloniaProperty.RegisterDirect<LibraryGridView, double>(
            nameof(ImageViewportWidth),
            view => view.ImageViewportWidth);

    public static readonly DirectProperty<LibraryGridView, double> ImageViewportHeightProperty =
        AvaloniaProperty.RegisterDirect<LibraryGridView, double>(
            nameof(ImageViewportHeight),
            view => view.ImageViewportHeight);

    public static readonly DirectProperty<LibraryGridView, double> ThumbnailItemWidthProperty =
        AvaloniaProperty.RegisterDirect<LibraryGridView, double>(
            nameof(ThumbnailItemWidth),
            view => view.ThumbnailItemWidth);

    public static readonly DirectProperty<LibraryGridView, double> ThumbnailItemHeightProperty =
        AvaloniaProperty.RegisterDirect<LibraryGridView, double>(
            nameof(ThumbnailItemHeight),
            view => view.ThumbnailItemHeight);

    public ObservableCollection<ImageFile>? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public ImageFile? SelectedImage
    {
        get => GetValue(SelectedImageProperty);
        set => SetValue(SelectedImageProperty, value);
    }

    public int TotalImageCount
    {
        get => GetValue(TotalImageCountProperty);
        set => SetValue(TotalImageCountProperty, value);
    }

    public string EmptyMessageText
    {
        get => GetValue(EmptyMessageTextProperty);
        set => SetValue(EmptyMessageTextProperty, value);
    }

    public string EmptyHeadingText
    {
        get => GetValue(EmptyHeadingTextProperty);
        set => SetValue(EmptyHeadingTextProperty, value);
    }

    public bool SuppressEmptyState
    {
        get => GetValue(SuppressEmptyStateProperty);
        set => SetValue(SuppressEmptyStateProperty, value);
    }

    public ImageFileTypeFilter FileTypeFilter
    {
        get => GetValue(FileTypeFilterProperty);
        set => SetValue(FileTypeFilterProperty, value);
    }

    public FlagFilter FlagFilter
    {
        get => GetValue(FlagFilterProperty);
        set => SetValue(FlagFilterProperty, value);
    }

    public int MinimumRating
    {
        get => GetValue(MinimumRatingProperty);
        set => SetValue(MinimumRatingProperty, value);
    }

    public ColorLabelFilter ColorLabelFilter
    {
        get => GetValue(ColorLabelFilterProperty);
        set => SetValue(ColorLabelFilterProperty, value);
    }

    public bool ShowBursts
    {
        get => GetValue(ShowBurstsProperty);
        set => SetValue(ShowBurstsProperty, value);
    }

    public LibraryThumbnailSize ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public event EventHandler? DevelopModeRequested;
    public event EventHandler? SelectAllRequested;
    public event EventHandler? DeselectAllRequested;
    public event EventHandler? BatchExportRequested;
    public event EventHandler? DeleteRejectedRequested;
    public event EventHandler? SelectionChanged;
    public event EventHandler<ImageFile>? ImageSelectionToggled;
    public event EventHandler<(ImageFile from, ImageFile to)>? RangeSelectionRequested;
    public event EventHandler<(int StartIndex, int Count)>? ViewportRangeChanged;

    private ImageFile? _lastClickedImage;
    private ObservableCollection<ImageFile>? _subscribedImages;
    private int _lastViewportStart = -1;
    private int _lastViewportCount = -1;

    public LibraryGridView()
    {
        InitializeComponent();
        UpdateFilterBar();
        UpdateBurstsButton();
        UpdateThumbnailSizeButtons();
        FilterScrollViewer.ScrollChanged += OnFilterScrollChanged;
        FilterScrollViewer.SizeChanged += OnFilterScrollViewerSizeChanged;
        ThumbnailScrollViewer.ScrollChanged += OnThumbnailScrollChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ImagesProperty)
        {
            if (_subscribedImages != null)
            {
                _subscribedImages.CollectionChanged -= OnImagesCollectionChanged;
            }

            ThumbnailGrid.ItemsSource = Images;
            _subscribedImages = Images;
            UpdateEmptyState();
            _lastViewportStart = -1;
            _lastViewportCount = -1;
            QueueViewportReport();

            if (Images != null)
            {
                Images.CollectionChanged += OnImagesCollectionChanged;
            }
        }
        else if (change.Property == TotalImageCountProperty ||
                 change.Property == EmptyMessageTextProperty ||
                 change.Property == EmptyHeadingTextProperty ||
                 change.Property == SuppressEmptyStateProperty)
        {
            UpdateEmptyState();
        }
        else if (change.Property == FileTypeFilterProperty)
        {
            UpdateFilterButtons();
        }
        else if (change.Property == FlagFilterProperty)
        {
            UpdateFlagFilterButtons();
        }
        else if (change.Property == ShowBurstsProperty)
        {
            UpdateBurstsButton();
        }
        else if (change.Property == ThumbnailSizeProperty)
        {
            ApplyThumbnailGeometry();
        }
        // SelectedImage changes don't affect selection state anymore
        // Selection is now independent from the active/focused image
    }

    private void OnImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        QueueViewportReport();
    }

    private void OnThumbnailScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        ReportViewportRange();

    private void OnLayoutUpdated(object? sender, EventArgs e) => ReportViewportRange();

    private void QueueViewportReport() => Dispatcher.UIThread.Post(
        ReportViewportRange,
        DispatcherPriority.Background);

    private void ReportViewportRange()
    {
        if (Images == null || Images.Count == 0 ||
            ThumbnailScrollViewer.Viewport.Height <= 0)
        {
            return;
        }

        var rowHeight = Geometry.RowHeight;
        var itemsPerRow = GetItemsPerRow();
        var startRow = Math.Max(0, (int)Math.Floor(
            ThumbnailScrollViewer.Offset.Y / rowHeight));
        var rowCount = Math.Max(1, (int)Math.Ceiling(
            ThumbnailScrollViewer.Viewport.Height / rowHeight) + 1);
        var startIndex = Math.Min(Images.Count - 1, startRow * itemsPerRow);
        var count = Math.Min(Images.Count - startIndex, rowCount * itemsPerRow);
        if (startIndex == _lastViewportStart && count == _lastViewportCount) return;

        _lastViewportStart = startIndex;
        _lastViewportCount = count;
        ViewportRangeChanged?.Invoke(this, (startIndex, count));
    }

    private void UpdateEmptyState()
    {
        var isEmpty = Images == null || Images.Count == 0;
        var isFilteredEmpty = isEmpty && TotalImageCount > 0;
        EmptyHeading.Text = EmptyHeadingText;
        EmptyMessage.Text = EmptyMessageText;
        EmptyState.IsVisible = isEmpty && !isFilteredEmpty && !SuppressEmptyState;
        FilteredEmptyState.IsVisible = isFilteredEmpty;
        ThumbnailGrid.IsVisible = !isEmpty;
    }

    private void UpdateThumbnailSizeButtons()
    {
        SmallThumbnailButton.IsChecked = ThumbnailSize == LibraryThumbnailSize.Small;
        MediumThumbnailButton.IsChecked = ThumbnailSize == LibraryThumbnailSize.Medium;
        LargeThumbnailButton.IsChecked = ThumbnailSize == LibraryThumbnailSize.Large;
    }

    private void OnSmallThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = LibraryThumbnailSize.Small;

    private void OnMediumThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = LibraryThumbnailSize.Medium;

    private void OnLargeThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = LibraryThumbnailSize.Large;

    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not ImageFile image)
            return;

        var point = e.GetCurrentPoint(border);

        if (point.Properties.IsLeftButtonPressed)
        {
            var modifiers = e.KeyModifiers;

            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Click: Toggle selection - notify ViewModel
                ImageSelectionToggled?.Invoke(this, image);
                // Also set as active image
                SelectedImage = image;
            }
            else if (modifiers.HasFlag(KeyModifiers.Shift) && _lastClickedImage != null && Images != null)
            {
                // Shift+Click: Range selection - notify ViewModel
                RangeSelectionRequested?.Invoke(this, (_lastClickedImage, image));
                // Also set as active image
                SelectedImage = image;
            }
            else
            {
                // Normal click: Clear selection, select only this image, set as active
                if (Images != null)
                {
                    foreach (var img in Images)
                    {
                        img.IsSelected = false;
                    }
                }
                image.IsSelected = true;
                SelectedImage = image;

                // Notify selection changed
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            _lastClickedImage = image;
        }
    }

    // View-local visual state lets groupmates react without a view-model round-trip.
    private void OnThumbnailPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not ImageFile image)
            return;
        if (image.BurstGroupOrdinal <= 0 || Images == null)
            return;

        foreach (var candidate in Images)
        {
            candidate.IsBurstHighlighted = candidate.BurstGroupOrdinal == image.BurstGroupOrdinal;
        }
    }

    private void OnThumbnailPointerExited(object? sender, PointerEventArgs e)
    {
        if (Images == null)
            return;

        foreach (var candidate in Images)
        {
            candidate.IsBurstHighlighted = false;
        }
    }

    private void OnThumbnailDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ImageFile image)
        {
            SelectedImage = image;
            DevelopModeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        SelectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeselectAllClick(object? sender, RoutedEventArgs e)
    {
        DeselectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBatchExportClick(object? sender, RoutedEventArgs e)
    {
        BatchExportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeleteRejectedClick(object? sender, RoutedEventArgs e)
    {
        DeleteRejectedRequested?.Invoke(this, EventArgs.Empty);
    }

}
