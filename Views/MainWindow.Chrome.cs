using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private const double ResizeBorderThickness = 12;
    private static readonly Cursor HorizontalResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor VerticalResizeCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestResizeCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastResizeCursor = new(StandardCursorType.TopRightCorner);

    private void InitializeWindowChrome()
    {
        AddHandler(
            PointerPressedEvent,
            OnWindowPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
        PointerExited += (_, _) => Cursor = Cursor.Default;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        Cursor = GetResizeEdge(e.GetPosition(this)) switch
        {
            WindowEdge.West or WindowEdge.East => HorizontalResizeCursor,
            WindowEdge.North or WindowEdge.South => VerticalResizeCursor,
            WindowEdge.NorthWest or WindowEdge.SouthEast => NorthWestResizeCursor,
            WindowEdge.NorthEast or WindowEdge.SouthWest => NorthEastResizeCursor,
            _ => Cursor.Default
        };
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanResize || WindowState != WindowState.Normal ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (GetResizeEdge(e.GetPosition(this)) is { } resizeEdge)
        {
            BeginResizeDrag(resizeEdge, e);
            e.Handled = true;
        }
    }

    private WindowEdge? GetResizeEdge(Avalonia.Point position)
    {
        if (!CanResize || WindowState != WindowState.Normal)
        {
            return null;
        }

        var left = position.X <= ResizeBorderThickness;
        var right = position.X >= Bounds.Width - ResizeBorderThickness;
        var top = position.Y <= ResizeBorderThickness;
        var bottom = position.Y >= Bounds.Height - ResizeBorderThickness;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null
        };
    }
}
