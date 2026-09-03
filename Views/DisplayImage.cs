using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public sealed class DisplayImage : Image
{
    public static readonly StyledProperty<Bitmap?> CanonicalSourceProperty =
        AvaloniaProperty.Register<DisplayImage, Bitmap?>(nameof(CanonicalSource));

    public static readonly StyledProperty<DisplayTransformSnapshot> DisplayTransformProperty =
        AvaloniaProperty.Register<DisplayImage, DisplayTransformSnapshot>(
            nameof(DisplayTransform), DisplayTransformSnapshot.None);

    public static readonly StyledProperty<DisplaySourceColorSpace> DisplaySourceColorSpaceProperty =
        AvaloniaProperty.Register<DisplayImage, DisplaySourceColorSpace>(
            nameof(DisplaySourceColorSpace), DisplaySourceColorSpace.Srgb);

    private Bitmap? _displayCopy;
    private Bitmap? _derivedCanonical;
    private DisplayTransformSnapshot? _derivedTransform;
    private DisplaySourceColorSpace _derivedSourceColorSpace;
    private readonly List<Visual> _visibilityAncestors = [];

    public Bitmap? CanonicalSource
    {
        get => GetValue(CanonicalSourceProperty);
        set => SetValue(CanonicalSourceProperty, value);
    }

    public DisplayTransformSnapshot DisplayTransform
    {
        get => GetValue(DisplayTransformProperty);
        set => SetValue(DisplayTransformProperty, value);
    }

    public DisplaySourceColorSpace DisplaySourceColorSpace
    {
        get => GetValue(DisplaySourceColorSpaceProperty);
        set => SetValue(DisplaySourceColorSpaceProperty, value);
    }

    internal int DerivationCount { get; private set; }
    internal Bitmap? DisplayedBitmap => Source as Bitmap;
    internal Bitmap? DisplayCopy => _displayCopy;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CanonicalSourceProperty ||
            change.Property == DisplayTransformProperty)
        {
            UpdateDisplayedSource();
        }
        // A source-space change describes the canonical bitmap published next.
        // Keep showing the correctly interpreted current bitmap until that swap.
        else if (change.Property == DisplaySourceColorSpaceProperty &&
                 CanonicalSource == null)
        {
            UpdateDisplayedSource();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateVisibility();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestor.PropertyChanged += OnVisibilityAncestorPropertyChanged;
            _visibilityAncestors.Add(ancestor);
        }
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var ancestor in _visibilityAncestors)
            ancestor.PropertyChanged -= OnVisibilityAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        ClearDisplayedSource();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnVisibilityAncestorPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty) UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (this.IsAttachedToVisualTree() &&
            IsEffectivelyVisible &&
            IsVisible &&
            _visibilityAncestors.All(ancestor => ancestor.IsVisible))
        {
            UpdateDisplayedSource();
        }
        else ClearDisplayedSource();
    }

    private void UpdateDisplayedSource()
    {
        if (!this.IsAttachedToVisualTree() ||
            !IsEffectivelyVisible ||
            !IsVisible ||
            _visibilityAncestors.Any(ancestor => !ancestor.IsVisible))
        {
            ClearDisplayedSource();
            return;
        }

        var canonical = CanonicalSource;
        var transform = DisplayTransform;
        var sourceColorSpace = ReferenceEquals(canonical, _derivedCanonical) &&
            DisplaySourceColorSpace != _derivedSourceColorSpace
                ? _derivedSourceColorSpace
                : DisplaySourceColorSpace;
        if (ReferenceEquals(canonical, _derivedCanonical) &&
            ReferenceEquals(transform, _derivedTransform) &&
            sourceColorSpace == _derivedSourceColorSpace &&
            (Source != null || canonical == null))
        {
            return;
        }

        Bitmap? displayed = null;
        Bitmap? owned = null;
        if (canonical != null)
        {
            displayed = transform.Derive(canonical, sourceColorSpace);
            if (!ReferenceEquals(displayed, canonical)) owned = displayed;
            DerivationCount++;
        }

        var previous = _displayCopy;
        _derivedCanonical = canonical;
        _derivedTransform = transform;
        _derivedSourceColorSpace = sourceColorSpace;
        _displayCopy = owned;
        Source = displayed;
        previous?.Dispose();
    }

    private void ClearDisplayedSource()
    {
        var copy = _displayCopy;
        _displayCopy = null;
        Source = null;
        copy?.Dispose();
    }
}
