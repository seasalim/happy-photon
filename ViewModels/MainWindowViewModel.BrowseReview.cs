using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _browseSelectionSummaryCts;
    private Task _browseSelectionSummaryTask = Task.CompletedTask;
    private int _browseSelectionSummaryGeneration;
    private bool _isSelectedMetadataLoadComplete = true;
    private int _browseSelectionCount;
    private int _browseSelectionOnlineOnlyCount;
    private long _browseSelectionCombinedFileSize;
    private DateTime? _browseSelectionEarliestDate;
    private DateTime? _browseSelectionLatestDate;
    private bool _isBrowseSelectionSummaryLoading;

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

    public int BrowseSelectionCount
    {
        get => _browseSelectionCount;
        private set
        {
            if (SetProperty(ref _browseSelectionCount, value))
            {
                OnPropertyChanged(nameof(HasBrowseSelectionSummary));
                OnPropertyChanged(nameof(BrowseSelectionCountDisplay));
            }
        }
    }

    public bool HasBrowseSelectionSummary => BrowseSelectionCount >= 2;

    public string BrowseSelectionCountDisplay =>
        $"{BrowseSelectionCount:N0} photos selected";

    public int BrowseSelectionOnlineOnlyCount
    {
        get => _browseSelectionOnlineOnlyCount;
        private set
        {
            if (SetProperty(ref _browseSelectionOnlineOnlyCount, value))
            {
                OnPropertyChanged(nameof(HasBrowseSelectionOnlineOnlyImages));
                OnPropertyChanged(nameof(BrowseSelectionOnlineOnlyNote));
            }
        }
    }

    public bool HasBrowseSelectionOnlineOnlyImages =>
        BrowseSelectionOnlineOnlyCount > 0;

    public string BrowseSelectionOnlineOnlyNote
    {
        get
        {
            var noun = BrowseSelectionOnlineOnlyCount == 1
                ? "photo"
                : "photos";
            return $"{BrowseSelectionOnlineOnlyCount:N0} online-only {noun} excluded";
        }
    }

    public long BrowseSelectionCombinedFileSize
    {
        get => _browseSelectionCombinedFileSize;
        private set
        {
            if (SetProperty(ref _browseSelectionCombinedFileSize, value))
            {
                OnPropertyChanged(nameof(BrowseSelectionCombinedFileSizeDisplay));
            }
        }
    }

    public string BrowseSelectionCombinedFileSizeDisplay =>
        ImageFile.FormatFileSize(BrowseSelectionCombinedFileSize);

    public DateTime? BrowseSelectionEarliestDate
    {
        get => _browseSelectionEarliestDate;
        private set
        {
            if (SetProperty(ref _browseSelectionEarliestDate, value))
            {
                OnPropertyChanged(nameof(BrowseSelectionDateRangeDisplay));
            }
        }
    }

    public DateTime? BrowseSelectionLatestDate
    {
        get => _browseSelectionLatestDate;
        private set
        {
            if (SetProperty(ref _browseSelectionLatestDate, value))
            {
                OnPropertyChanged(nameof(BrowseSelectionDateRangeDisplay));
            }
        }
    }

    public string BrowseSelectionDateRangeDisplay
    {
        get
        {
            if (BrowseSelectionEarliestDate is not { } earliest)
            {
                return "Dates unavailable";
            }

            var latest = BrowseSelectionLatestDate ?? earliest;
            if (earliest.Date == latest.Date)
            {
                return earliest.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
            }

            return $"{earliest:MMM d, yyyy} – {latest:MMM d, yyyy}";
        }
    }

    public bool IsBrowseSelectionSummaryLoading
    {
        get => _isBrowseSelectionSummaryLoading;
        private set => SetProperty(ref _isBrowseSelectionSummaryLoading, value);
    }

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

    private void RestartBrowseSelectionSummary()
    {
        var generation = Interlocked.Increment(
            ref _browseSelectionSummaryGeneration);
        var images = Browse.GetSelectedImages().ToList();
        PublishBrowseSelectionSummary(
            BuildInitialSelectionSummary(images),
            images.Count >= 2);

        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(
            ref _browseSelectionSummaryCts,
            nextCts);
        previousCts?.Cancel();

        var previousTask = _browseSelectionSummaryTask;
        _browseSelectionSummaryTask = RunBrowseSelectionSummaryAfterAsync(
            previousTask,
            images,
            generation,
            nextCts);
    }

    private async Task RunBrowseSelectionSummaryAfterAsync(
        Task previousTask,
        IReadOnlyList<ImageFile> images,
        int generation,
        CancellationTokenSource summaryCts)
    {
        try
        {
            await previousTask;
            summaryCts.Token.ThrowIfCancellationRequested();
            if (images.Count < 2)
            {
                PublishBrowseSelectionSummary(
                    BuildInitialSelectionSummary(images),
                    isLoading: false,
                    generation);
                return;
            }

            await AggregateBrowseSelectionSummaryAsync(
                images,
                generation,
                summaryCts.Token);
        }
        catch (OperationCanceledException) when (summaryCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Selection summary failed: {ex.Message}");
            PublishBrowseSelectionSummary(
                BuildInitialSelectionSummary(images),
                isLoading: false,
                generation);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _browseSelectionSummaryCts,
                null,
                summaryCts);
            summaryCts.Dispose();
        }
    }

    private async Task AggregateBrowseSelectionSummaryAsync(
        IReadOnlyList<ImageFile> images,
        int generation,
        CancellationToken cancellationToken)
    {
        var members = new Dictionary<ImageFile, SummaryMember>(
            ReferenceEqualityComparer.Instance);
        foreach (var image in images)
        {
            members.Add(image, CreateInitialSummaryMember(image));
        }

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentSelectionSummary(generation);

            var availability = ImageService.GetSourceAvailability(image);
            if (availability.IsOnlineOnly())
            {
                SetSourceRequiresHydration(image, true);
                members[image] = SummaryMember.OnlineOnly;
            }
            else if (availability == SourceAvailability.Unavailable)
            {
                members[image] = SummaryMember.Unavailable;
            }
            else
            {
                if (!image.MetadataLoaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _loadMetadataAsync(image);
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureCurrentSelectionSummary(generation);
                }

                if (image.MetadataLoaded)
                {
                    SetSourceRequiresHydration(image, false);
                    members[image] = SummaryMember.From(image);
                }
                else if (ImageService.GetSourceAvailability(image)
                             .IsOnlineOnly())
                {
                    SetSourceRequiresHydration(image, true);
                    members[image] = SummaryMember.OnlineOnly;
                }
                else
                {
                    members[image] = SummaryMember.Unavailable;
                }
            }

            PublishBrowseSelectionSummary(
                BuildSelectionSummary(members.Values),
                isLoading: true,
                generation);
        }

        PublishBrowseSelectionSummary(
            BuildSelectionSummary(members.Values),
            isLoading: false,
            generation);
    }

    private void EnsureCurrentSelectionSummary(int generation)
    {
        if (generation != Volatile.Read(
                ref _browseSelectionSummaryGeneration))
        {
            throw new OperationCanceledException();
        }
    }

    private async Task CancelBrowseSelectionSummaryAsync()
    {
        Interlocked.Increment(ref _browseSelectionSummaryGeneration);
        Interlocked.Exchange(ref _browseSelectionSummaryCts, null)?.Cancel();
        PublishBrowseSelectionSummary(
            SelectionSummary.Empty,
            isLoading: false);
        while (true)
        {
            var observed = _browseSelectionSummaryTask;
            await observed;
            if (ReferenceEquals(observed, _browseSelectionSummaryTask))
            {
                return;
            }
        }
    }

    internal Task WaitForBrowseSelectionSummaryAsync() =>
        _browseSelectionSummaryTask;

    private void PublishBrowseSelectionSummary(
        SelectionSummary summary,
        bool isLoading,
        int? generation = null)
    {
        if (generation.HasValue &&
            generation.Value != Volatile.Read(
                ref _browseSelectionSummaryGeneration))
        {
            return;
        }

        BrowseSelectionCount = summary.Count;
        BrowseSelectionOnlineOnlyCount = summary.OnlineOnlyCount;
        BrowseSelectionCombinedFileSize = summary.CombinedFileSize;
        BrowseSelectionEarliestDate = summary.EarliestDate;
        BrowseSelectionLatestDate = summary.LatestDate;
        IsBrowseSelectionSummaryLoading = isLoading && summary.Count >= 2;
    }

    private static SelectionSummary BuildInitialSelectionSummary(
        IReadOnlyList<ImageFile> images) =>
        BuildSelectionSummary(images.Select(CreateInitialSummaryMember));

    private static SummaryMember CreateInitialSummaryMember(ImageFile image)
    {
        if (image.SourceRequiresHydration)
        {
            return SummaryMember.OnlineOnly;
        }

        return image.MetadataLoaded
            ? SummaryMember.From(image)
            : SummaryMember.Unavailable;
    }

    private static SelectionSummary BuildSelectionSummary(
        IEnumerable<SummaryMember> members)
    {
        var count = 0;
        var onlineOnlyCount = 0;
        var combinedSize = 0L;
        DateTime? earliest = null;
        DateTime? latest = null;

        foreach (var member in members)
        {
            count++;
            if (member.IsOnlineOnly)
            {
                onlineOnlyCount++;
                continue;
            }

            if (!member.HasMetadata)
            {
                continue;
            }

            combinedSize += member.FileSize;
            if (member.DateTaken is not { } dateTaken)
            {
                continue;
            }

            earliest = !earliest.HasValue || dateTaken < earliest
                ? dateTaken
                : earliest;
            latest = !latest.HasValue || dateTaken > latest
                ? dateTaken
                : latest;
        }

        return new SelectionSummary(
            count,
            onlineOnlyCount,
            combinedSize,
            earliest,
            latest);
    }

    private readonly record struct SummaryMember(
        bool HasMetadata,
        bool IsOnlineOnly,
        long FileSize,
        DateTime? DateTaken)
    {
        public static SummaryMember OnlineOnly { get; } =
            new(false, true, 0, null);

        public static SummaryMember Unavailable { get; } =
            new(false, false, 0, null);

        public static SummaryMember From(ImageFile image) =>
            new(true, false, image.FileSize, image.DateTaken);
    }

    private readonly record struct SelectionSummary(
        int Count,
        int OnlineOnlyCount,
        long CombinedFileSize,
        DateTime? EarliestDate,
        DateTime? LatestDate)
    {
        public static SelectionSummary Empty { get; } =
            new(0, 0, 0, null, null);
    }
}
