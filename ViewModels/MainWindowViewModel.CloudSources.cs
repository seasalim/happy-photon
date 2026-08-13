using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private const string SourceDownloadStatus = "Downloading original…";
    private CancellationTokenSource? _sourceHydrationCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOnlineOnlyPhotos))]
    [NotifyPropertyChangedFor(nameof(OnlineOnlyMessage))]
    private int _onlineOnlyPhotoCount;

    public bool HasOnlineOnlyPhotos => OnlineOnlyPhotoCount > 0;

    public string OnlineOnlyMessage => OnlineOnlyPhotoCount == 1
        ? "1 photo is online-only. Happy Photon will not download it automatically."
        : $"{OnlineOnlyPhotoCount:N0} photos are online-only. Happy Photon will not download them automatically.";

    internal void InitializeCloudSourceCount(
        IEnumerable<ImageFile> images) =>
        OnlineOnlyPhotoCount = images.Count(
            image => image.SourceRequiresHydration);

    internal void ApplyThumbnailLoadStatus(
        ImageFile image,
        ThumbnailLoadStatus status)
    {
        var deferred = status == ThumbnailLoadStatus.DeferredForHydration;
        image.ThumbnailDeferredForHydration = deferred;
        image.ThumbnailLoadFailed = status == ThumbnailLoadStatus.Failed;
        if (status is ThumbnailLoadStatus.Loaded or
            ThumbnailLoadStatus.DeferredForHydration)
        {
            SetSourceRequiresHydration(image, deferred);
        }
    }

    internal void ApplyThumbnailLoadResult(
        ImageFile image,
        ThumbnailLoadResult result)
    {
        if (result.Status != ThumbnailLoadStatus.Loaded)
        {
            if (image.Thumbnail != null)
            {
                if (result.Status == ThumbnailLoadStatus.DeferredForHydration)
                {
                    image.ThumbnailUpgradeDeferredDimension = Math.Max(
                        image.ThumbnailUpgradeDeferredDimension,
                        result.Request.GenerationDimension);
                }
                else
                {
                    image.ThumbnailUpgradeFailedDimension = Math.Max(
                        image.ThumbnailUpgradeFailedDimension,
                        result.Request.GenerationDimension);
                }
                return;
            }

            ApplyThumbnailLoadStatus(image, result.Status);
            return;
        }

        image.ThumbnailDeferredForHydration = false;
        image.ThumbnailLoadFailed = false;
        if (result.SatisfiesMinimumDimension)
        {
            image.ThumbnailUpgradeDeferredDimension = 0;
            image.ThumbnailUpgradeFailedDimension = 0;
        }
        else if (result.BetterResultDeferredForHydration)
        {
            image.ThumbnailUpgradeDeferredDimension = Math.Max(
                image.ThumbnailUpgradeDeferredDimension,
                result.Request.GenerationDimension);
        }
        else if (result.SourceCannotProvideRequestedQuality)
        {
            image.ThumbnailUpgradeFailedDimension = Math.Max(
                image.ThumbnailUpgradeFailedDimension,
                result.Request.GenerationDimension);
        }
    }

    private void SetSourceRequiresHydration(ImageFile image, bool value)
    {
        if (image.SourceRequiresHydration == value)
        {
            return;
        }

        image.SourceRequiresHydration = value;
        if (Library.Contains(image))
        {
            OnlineOnlyPhotoCount = Math.Max(
                0,
                OnlineOnlyPhotoCount + (value ? 1 : -1));
        }

        if (ReferenceEquals(SelectedImage, image))
        {
            NotifySelectedImageEditStateChanged();
            UpdateCanReset();
            NotifyWhiteBalanceCommandState();
            if (value)
            {
                _previewDebounce?.Cancel();
                _thumbnailDebounce?.Cancel();
                _histogramDebounce?.Cancel();
                IsCropMode = false;
                IsWhiteBalancePicking = false;
                Histogram = null;
            }
        }
    }

    private void RefreshSourceAvailability(ImageFile image) =>
        SetSourceRequiresHydration(
            image,
            ImageService.GetSourceAvailability(image) ==
                SourceAvailability.RequiresHydration);

    [RelayCommand]
    private async Task DownloadAndOpenAsync()
    {
        var image = SelectedImage;
        if (image == null || !image.SourceRequiresHydration)
        {
            return;
        }

        var generation = Volatile.Read(ref _libraryGeneration);
        var request = CreateSourceHydrationCancellation();
        var previous = Interlocked.Exchange(ref _sourceHydrationCts, request);
        previous?.Cancel();
        previous?.Dispose();
        ShowPinnedStatus(SourceDownloadStatus);

        try
        {
            var hydrated = await ImageService.HydrateSourceAsync(
                image,
                request.Token);
            if (!hydrated ||
                generation != Volatile.Read(ref _libraryGeneration) ||
                !ReferenceEquals(SelectedImage, image) ||
                !Library.Contains(image))
            {
                if (!request.IsCancellationRequested && !hydrated)
                {
                    ShowTransientStatus("The original could not be downloaded.");
                }
                return;
            }

            _isLoadingImage = true;
            SetSourceRequiresHydration(image, false);
            PrepareWhiteBalanceUi(image);
            LoadSlidersFrom(image.EditSettings);
            _lastSavedState = image.EditSettings.Clone();
            _isLoadingImage = false;
            UpdateCanReset();
            image.ThumbnailDeferredForHydration = false;
            image.ThumbnailLoadFailed = false;
            image.ThumbnailUpgradeDeferredDimension = 0;
            image.ThumbnailUpgradeFailedDimension = 0;
            await TrackDirectThumbnailOperation(LoadThumbnailAsync(
                image,
                generation,
                request.Token));
            await _loadMetadataAsync(image);
            request.Token.ThrowIfCancellationRequested();

            ShowTransientStatus($"Downloaded {image.FileName}");
            if (!IsDevelopMode)
            {
                IsDevelopMode = true;
            }
            else
            {
                await LoadPreviewAsync(image);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Source download failed for {image.FilePath}: {ex.Message}");
            ShowTransientStatus("The original could not be downloaded.");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(
                ref _sourceHydrationCts,
                null,
                request), request))
            {
                request.Dispose();
                ClearPinnedStatus(SourceDownloadStatus);
            }
        }
    }

    private CancellationTokenSource CreateSourceHydrationCancellation()
    {
        var folderCancellation = _thumbnailLoadingCts;
        try
        {
            return folderCancellation == null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(
                    folderCancellation.Token);
        }
        catch (ObjectDisposedException)
        {
            return new CancellationTokenSource();
        }
    }

    private void CancelSourceHydration()
    {
        var request = Interlocked.Exchange(ref _sourceHydrationCts, null);
        request?.Cancel();
        request?.Dispose();
        ClearPinnedStatus(SourceDownloadStatus);
    }
}
