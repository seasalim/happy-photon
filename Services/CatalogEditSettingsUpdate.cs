using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CatalogEditSettingsUpdate(
    long CatalogId, EditSettings Settings, EditSettings? Previous = null);
