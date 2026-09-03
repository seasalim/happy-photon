using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class ComparePaneViewModel(ImageFile image) : ObservableObject
{
    public ImageFile Image { get; } = image;

    [ObservableProperty]
    private DisplayTransformSnapshot _displayTransform = DisplayTransformSnapshot.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingMessage))]
    private Bitmap? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingMessage))]
    private bool _isLoading = true;

    // The message means "nothing to show yet", not "work in progress": a pane
    // that already paints a cached preview must not wear a loading label while
    // the authoritative render catches up.
    public bool ShowLoadingMessage => IsLoading && Preview == null;

    [ObservableProperty]
    private PixelSize _originalViewPixelSize;

    internal int RenderedLongEdge { get; set; }
    internal Bitmap? PreviewResolutionBitmap { get; set; }
    internal int PreviewResolutionLongEdge { get; set; }
    internal int AchievableLongEdge { get; set; }
    internal int RequiredDeviceLongEdge { get; set; }
    internal bool IsLoupeRefinementRequested { get; set; }
    internal bool IsRefinementQueued { get; set; }
}
