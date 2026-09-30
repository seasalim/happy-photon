using Avalonia.Input;

namespace HappyPhoton.Views;

internal static class ResizeCursor
{
    private static readonly Cursor[] Directions =
    [
        new(StandardCursorType.SizeWestEast),
        new(StandardCursorType.TopLeftCorner),
        new(StandardCursorType.SizeNorthSouth),
        new(StandardCursorType.TopRightCorner)
    ];

    public static Cursor ForAngle(double radians) =>
        Directions[((int)Math.Floor(radians / (Math.PI / 4) + .5) % 4 + 4) % 4];
}
