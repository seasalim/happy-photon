using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HappyPhoton.Views;

public partial class AlignmentGridOverlayControl : UserControl
{
    private const int ShortAxisDivisions = 8;
    private static readonly IBrush GridBrush = HappyPhotonColors.CropGridLine;

    private Canvas? _canvas;

    internal bool IsGridVisible { get; private set; }

    public AlignmentGridOverlayControl()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("GridCanvas");
    }

    internal void SetGridVisible(bool visible)
    {
        if (IsGridVisible == visible) return;

        IsGridVisible = visible;
        _canvas?.Classes.Set("visible", visible);
        if (visible) DrawGrid();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsGridVisible) DrawGrid();
    }

    private void DrawGrid()
    {
        if (_canvas == null) return;

        _canvas.Children.Clear();
        var rect = new Rect(_canvas.Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var cellSize = Math.Min(rect.Width, rect.Height) / ShortAxisDivisions;
        var columns = Math.Max(1, (int)Math.Round(rect.Width / cellSize));
        var rows = Math.Max(1, (int)Math.Round(rect.Height / cellSize));
        OverlayGridLines.Draw(_canvas, rect, GridBrush, columns, rows);
    }
}
