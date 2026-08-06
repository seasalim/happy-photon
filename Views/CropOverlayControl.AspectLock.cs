namespace HappyPhoton.Views;

public partial class CropOverlayControl
{
    private void ApplyLockedAspectDrag(DragHandle handle, double deltaX, double deltaY)
    {
        if (Crop == null || _dragStartCrop == null) return;

        var startWidth = _dragStartCrop.Right - _dragStartCrop.Left;
        var startHeight = _dragStartCrop.Bottom - _dragStartCrop.Top;
        if (startWidth <= 0 || startHeight <= 0) return;

        var ratio = startWidth / startHeight;

        switch (handle)
        {
            case DragHandle.TopLeft:
                ResizeLockedCorner(_dragStartCrop.Right, _dragStartCrop.Bottom, -1, -1,
                    startWidth - deltaX, startHeight - deltaY, ratio, deltaX, deltaY);
                break;
            case DragHandle.TopRight:
                ResizeLockedCorner(_dragStartCrop.Left, _dragStartCrop.Bottom, 1, -1,
                    startWidth + deltaX, startHeight - deltaY, ratio, deltaX, deltaY);
                break;
            case DragHandle.BottomLeft:
                ResizeLockedCorner(_dragStartCrop.Right, _dragStartCrop.Top, -1, 1,
                    startWidth - deltaX, startHeight + deltaY, ratio, deltaX, deltaY);
                break;
            case DragHandle.BottomRight:
                ResizeLockedCorner(_dragStartCrop.Left, _dragStartCrop.Top, 1, 1,
                    startWidth + deltaX, startHeight + deltaY, ratio, deltaX, deltaY);
                break;
            case DragHandle.TopCenter:
                ResizeLockedVerticalEdge(_dragStartCrop.Bottom, -1, startHeight - deltaY, ratio);
                break;
            case DragHandle.BottomCenter:
                ResizeLockedVerticalEdge(_dragStartCrop.Top, 1, startHeight + deltaY, ratio);
                break;
            case DragHandle.MiddleLeft:
                ResizeLockedHorizontalEdge(_dragStartCrop.Right, -1, startWidth - deltaX, ratio);
                break;
            case DragHandle.MiddleRight:
                ResizeLockedHorizontalEdge(_dragStartCrop.Left, 1, startWidth + deltaX, ratio);
                break;
        }
    }

    private void ResizeLockedCorner(
        double anchorX,
        double anchorY,
        int directionX,
        int directionY,
        double pointerWidth,
        double pointerHeight,
        double ratio,
        double deltaX,
        double deltaY)
    {
        var maxWidth = directionX > 0 ? 1 - anchorX : anchorX;
        var maxHeight = directionY > 0 ? 1 - anchorY : anchorY;
        var horizontalDrag = Math.Abs(deltaX) >= Math.Abs(deltaY * ratio);

        var (width, height) = horizontalDrag
            ? SizeFromWidth(pointerWidth, ratio, maxWidth, maxHeight)
            : SizeFromHeight(pointerHeight, ratio, maxWidth, maxHeight);

        SetCropFromAnchor(anchorX, anchorY, directionX, directionY, width, height);
    }

    private void ResizeLockedVerticalEdge(double anchorY, int directionY, double pointerHeight, double ratio)
    {
        if (_dragStartCrop == null) return;

        var centerX = (_dragStartCrop.Left + _dragStartCrop.Right) / 2;
        var maxWidth = 2 * Math.Min(centerX, 1 - centerX);
        var maxHeight = directionY > 0 ? 1 - anchorY : anchorY;
        var (width, height) = SizeFromHeight(pointerHeight, ratio, maxWidth, maxHeight);

        Crop!.Left = centerX - width / 2;
        Crop.Right = centerX + width / 2;
        if (directionY > 0)
        {
            Crop.Top = anchorY;
            Crop.Bottom = anchorY + height;
        }
        else
        {
            Crop.Top = anchorY - height;
            Crop.Bottom = anchorY;
        }
    }

    private void ResizeLockedHorizontalEdge(double anchorX, int directionX, double pointerWidth, double ratio)
    {
        if (_dragStartCrop == null) return;

        var centerY = (_dragStartCrop.Top + _dragStartCrop.Bottom) / 2;
        var maxWidth = directionX > 0 ? 1 - anchorX : anchorX;
        var maxHeight = 2 * Math.Min(centerY, 1 - centerY);
        var (width, height) = SizeFromWidth(pointerWidth, ratio, maxWidth, maxHeight);

        if (directionX > 0)
        {
            Crop!.Left = anchorX;
            Crop.Right = anchorX + width;
        }
        else
        {
            Crop!.Left = anchorX - width;
            Crop.Right = anchorX;
        }
        Crop.Top = centerY - height / 2;
        Crop.Bottom = centerY + height / 2;
    }

    private static (double Width, double Height) SizeFromWidth(
        double width,
        double ratio,
        double maxWidth,
        double maxHeight)
    {
        width = Clamp(width, Math.Max(MinCropSize, MinCropSize * ratio), Math.Min(maxWidth, maxHeight * ratio));
        return (width, width / ratio);
    }

    private static (double Width, double Height) SizeFromHeight(
        double height,
        double ratio,
        double maxWidth,
        double maxHeight)
    {
        height = Clamp(height, Math.Max(MinCropSize, MinCropSize / ratio), Math.Min(maxHeight, maxWidth / ratio));
        return (height * ratio, height);
    }

    private void SetCropFromAnchor(
        double anchorX,
        double anchorY,
        int directionX,
        int directionY,
        double width,
        double height)
    {
        if (directionX > 0)
        {
            Crop!.Left = anchorX;
            Crop.Right = anchorX + width;
        }
        else
        {
            Crop!.Left = anchorX - width;
            Crop.Right = anchorX;
        }

        if (directionY > 0)
        {
            Crop.Top = anchorY;
            Crop.Bottom = anchorY + height;
        }
        else
        {
            Crop.Top = anchorY - height;
            Crop.Bottom = anchorY;
        }
    }
}
