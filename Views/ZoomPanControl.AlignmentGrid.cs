using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<bool> ShowAlignmentGridProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(
            nameof(ShowAlignmentGrid));

    private AlignmentGridOverlayControl? _alignmentGridOverlay;

    public bool ShowAlignmentGrid
    {
        get => GetValue(ShowAlignmentGridProperty);
        set => SetValue(ShowAlignmentGridProperty, value);
    }

    private void InitializeAlignmentGrid() =>
        _alignmentGridOverlay =
            this.FindControl<AlignmentGridOverlayControl>("AlignmentGridOverlay");

    private void UpdateAlignmentGridVisibility()
    {
        if (_alignmentGridOverlay == null) return;
        _alignmentGridOverlay.IsVisible = Source != null;
        _alignmentGridOverlay.SetGridVisible(
            Source != null && ShowAlignmentGrid && !IsCropMode);
    }

    private void UpdateAlignmentGridSize()
    {
        if (_alignmentGridOverlay == null || _imageControl == null ||
            Source == null) return;

        _alignmentGridOverlay.Width = _imageControl.Width;
        _alignmentGridOverlay.Height = _imageControl.Height;
    }
}
