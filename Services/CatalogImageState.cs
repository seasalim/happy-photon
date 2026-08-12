using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CatalogImageState(
    long CatalogId,
    EditSettings EditSettings,
    ImageFlag Flag,
    int Rating,
    ColorLabel ColorLabel);
