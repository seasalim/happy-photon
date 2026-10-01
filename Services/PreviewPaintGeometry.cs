using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record PreviewPaintGeometry(
    int Rotation, double Horizon, CropRegion? Crop, GeometrySettings? Geometry)
{
    public bool CanRotate => Horizon == 0 && Crop is not { IsFullImage: false } &&
        Geometry is not { IsIdentity: false };

    public static PreviewPaintGeometry From(EditSettings settings) => new(
        settings.Rotation, settings.HorizonRotation, settings.Crop?.Clone(), settings.Geometry?.Clone());
}
