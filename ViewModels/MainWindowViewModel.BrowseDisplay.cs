using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _restoringBrowseThumbnailSize;

    [ObservableProperty]
    private BrowseThumbnailSize _browseThumbnailSize = BrowseThumbnailSize.Medium;

    public ThumbnailSizeRequest BrowseThumbnailRequest =>
        ThumbnailSizeRequest.For(BrowseThumbnailSize);

    public void RestoreBrowseThumbnailSize(BrowseThumbnailSize value)
    {
        _restoringBrowseThumbnailSize = true;
        try
        {
            BrowseThumbnailSize = value;
        }
        finally
        {
            _restoringBrowseThumbnailSize = false;
        }
    }

    partial void OnBrowseThumbnailSizeChanged(BrowseThumbnailSize value)
    {
        OnPropertyChanged(nameof(BrowseThumbnailRequest));
        OnBrowseThumbnailSizeRequestChanged();
        if (!_restoringBrowseThumbnailSize)
        {
            _ = PersistBrowseThumbnailSizeAsync();
        }
    }

    private async Task PersistBrowseThumbnailSizeAsync()
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
