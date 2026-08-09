using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _restoringLibraryThumbnailSize;

    [ObservableProperty]
    private LibraryThumbnailSize _libraryThumbnailSize = LibraryThumbnailSize.Medium;

    public ThumbnailSizeRequest LibraryThumbnailRequest =>
        ThumbnailSizeRequest.For(LibraryThumbnailSize);

    public void RestoreLibraryThumbnailSize(LibraryThumbnailSize value)
    {
        _restoringLibraryThumbnailSize = true;
        try
        {
            LibraryThumbnailSize = value;
        }
        finally
        {
            _restoringLibraryThumbnailSize = false;
        }
    }

    partial void OnLibraryThumbnailSizeChanged(LibraryThumbnailSize value)
    {
        OnPropertyChanged(nameof(LibraryThumbnailRequest));
        OnLibraryThumbnailSizeRequestChanged();
        if (!_restoringLibraryThumbnailSize)
        {
            _ = PersistLibraryThumbnailSizeAsync();
        }
    }

    private async Task PersistLibraryThumbnailSizeAsync()
    {
        try
        {
            if (PersistAppSettingsAsync != null)
            {
                await PersistAppSettingsAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Thumbnail-size persistence failed: {exception.Message}");
        }
    }
}
