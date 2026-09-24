using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class ComparePaneViewModel : ObservableObject, IDisposable
{
    private readonly LoadingMessageGrace _loadingMessage;
    public ImageFile Image { get; }

    public ComparePaneViewModel(ImageFile image, TimeProvider? timeProvider = null)
    {
        Image = image;
        _loadingMessage = new(timeProvider ?? TimeProvider.System, value => IsLoadingMessageVisible = value);
        _loadingMessage.Update(ShowLoadingMessage);
    }

    [ObservableProperty]
    private bool _isLoadingMessageVisible;

    partial void OnPreviewChanged(Bitmap? value) => _loadingMessage.Update(ShowLoadingMessage);
    partial void OnIsLoadingChanged(bool value) => _loadingMessage.Update(ShowLoadingMessage);
    public void Dispose() => _loadingMessage.Dispose();

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
