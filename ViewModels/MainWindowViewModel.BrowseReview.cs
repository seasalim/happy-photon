using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isSelectedMetadataLoadComplete = true;

    public bool IsSelectedMetadataLoading =>
        HasSelectedImage &&
        SelectedImage?.SourceRequiresHydration != true &&
        SelectedImage?.MetadataLoaded != true &&
        !_isSelectedMetadataLoadComplete;

    public bool IsSelectedMetadataUnavailable =>
        HasSelectedImage &&
        SelectedImage?.SourceRequiresHydration != true &&
        SelectedImage?.MetadataLoaded != true &&
        _isSelectedMetadataLoadComplete;

    private void ResetSelectedMetadataState(ImageFile? image)
    {
        _isSelectedMetadataLoadComplete = image == null ||
            image.MetadataLoaded ||
            image.SourceRequiresHydration;
        NotifySelectedMetadataStateChanged();
    }

    private void CompleteSelectedMetadataLoad(ImageFile image)
    {
        if (!ReferenceEquals(SelectedImage, image))
        {
            return;
        }

        _isSelectedMetadataLoadComplete = true;
        NotifySelectedMetadataStateChanged();
    }

    private void NotifySelectedMetadataStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedMetadataLoading));
        OnPropertyChanged(nameof(IsSelectedMetadataUnavailable));
    }
}
