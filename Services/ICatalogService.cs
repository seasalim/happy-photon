using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Interface for centralized catalog storage operations.
/// Manages edit settings, thumbnails, previews, and app settings in a single catalog.
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Gets the catalog root directory path.
    /// </summary>
    string CatalogPath { get; }

    /// <summary>
    /// Initializes the catalog database and directory structure.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Gets or creates an image record in the catalog by file path.
    /// </summary>
    /// <param name="filePath">The full path to the image file.</param>
    /// <returns>The catalog ID for the image.</returns>
    Task<long> GetOrCreateImageAsync(string filePath);

    /// <summary>
    /// Gets the catalog ID for an image if it exists, or null if not found.
    /// </summary>
    Task<long?> GetImageIdAsync(string filePath);

    /// <summary>
    /// Loads catalog state for many image paths in a small number of database queries.
    /// </summary>
    Task<IReadOnlyDictionary<string, CatalogImageState>> LoadImageStatesAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates missing image records in batches and loads state for every requested path.
    /// </summary>
    Task<IReadOnlyDictionary<string, CatalogImageState>> LoadOrCreateImageStatesAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves edit settings for an image to the catalog.
    /// </summary>
    Task SaveEditSettingsAsync(long catalogId, EditSettings settings);

    /// <summary>
    /// Saves edit settings atomically for a group of catalog images.
    /// </summary>
    Task SaveEditSettingsBatchAsync(IReadOnlyList<CatalogEditSettingsUpdate> updates);

    /// <summary>
    /// Saves the culling flag state for an image to the catalog.
    /// </summary>
    Task SaveFlagStateAsync(long catalogId, ImageFlag flag);

    /// <summary>
    /// Saves the star rating (clamped to 0-5) for an image to the catalog.
    /// </summary>
    Task SaveRatingAsync(long catalogId, int rating);

    /// <summary>
    /// Deletes an image and all its associated data from the catalog.
    /// </summary>
    Task DeleteImageAsync(long catalogId);

    /// <summary>
    /// Gets the file path for storing a thumbnail.
    /// </summary>
    string GetThumbnailPath(long catalogId);

    /// <summary>
    /// Gets the file path for storing a preview.
    /// </summary>
    string GetPreviewPath(long catalogId);

    /// <summary>
    /// Gets an application setting value.
    /// </summary>
    Task<string?> GetAppSettingAsync(string key);

    /// <summary>
    /// Sets an application setting value.
    /// </summary>
    Task SetAppSettingAsync(string key, string? value);
}
