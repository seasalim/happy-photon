using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _librarySelectionSummaryCts;
    private Task _librarySelectionSummaryTask = Task.CompletedTask;
    private int _librarySelectionSummaryGeneration;
    private bool _isSelectedMetadataLoadComplete = true;
    private int _librarySelectionCount;
    private int _librarySelectionOnlineOnlyCount;
    private long _librarySelectionCombinedFileSize;
    private DateTime? _librarySelectionEarliestDate;
    private DateTime? _librarySelectionLatestDate;
    private bool _isLibrarySelectionSummaryLoading;

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

    public int LibrarySelectionCount
    {
        get => _librarySelectionCount;
        private set
        {
            if (SetProperty(ref _librarySelectionCount, value))
            {
                OnPropertyChanged(nameof(HasLibrarySelectionSummary));
                OnPropertyChanged(nameof(LibrarySelectionCountDisplay));
            }
        }
    }

    public bool HasLibrarySelectionSummary => LibrarySelectionCount >= 2;

    public string LibrarySelectionCountDisplay =>
        $"{LibrarySelectionCount:N0} photos selected";

    public int LibrarySelectionOnlineOnlyCount
    {
        get => _librarySelectionOnlineOnlyCount;
        private set
        {
            if (SetProperty(ref _librarySelectionOnlineOnlyCount, value))
            {
                OnPropertyChanged(nameof(HasLibrarySelectionOnlineOnlyImages));
                OnPropertyChanged(nameof(LibrarySelectionOnlineOnlyNote));
            }
        }
    }

    public bool HasLibrarySelectionOnlineOnlyImages =>
        LibrarySelectionOnlineOnlyCount > 0;

    public string LibrarySelectionOnlineOnlyNote
    {
        get
        {
            var noun = LibrarySelectionOnlineOnlyCount == 1
                ? "photo"
                : "photos";
            return $"{LibrarySelectionOnlineOnlyCount:N0} online-only {noun} excluded";
        }
    }

    public long LibrarySelectionCombinedFileSize
    {
        get => _librarySelectionCombinedFileSize;
        private set
        {
            if (SetProperty(ref _librarySelectionCombinedFileSize, value))
            {
                OnPropertyChanged(nameof(LibrarySelectionCombinedFileSizeDisplay));
            }
        }
    }

    public string LibrarySelectionCombinedFileSizeDisplay =>
        ImageFile.FormatFileSize(LibrarySelectionCombinedFileSize);

    public DateTime? LibrarySelectionEarliestDate
    {
        get => _librarySelectionEarliestDate;
        private set
        {
            if (SetProperty(ref _librarySelectionEarliestDate, value))
            {
                OnPropertyChanged(nameof(LibrarySelectionDateRangeDisplay));
            }
        }
    }

    public DateTime? LibrarySelectionLatestDate
    {
        get => _librarySelectionLatestDate;
        private set
        {
            if (SetProperty(ref _librarySelectionLatestDate, value))
            {
                OnPropertyChanged(nameof(LibrarySelectionDateRangeDisplay));
            }
        }
    }

    public string LibrarySelectionDateRangeDisplay
    {
        get
        {
            if (LibrarySelectionEarliestDate is not { } earliest)
            {
                return "Dates unavailable";
            }

            var latest = LibrarySelectionLatestDate ?? earliest;
            if (earliest.Date == latest.Date)
            {
                return earliest.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
            }

            return $"{earliest:MMM d, yyyy} – {latest:MMM d, yyyy}";
        }
    }

    public bool IsLibrarySelectionSummaryLoading
    {
        get => _isLibrarySelectionSummaryLoading;
        private set => SetProperty(ref _isLibrarySelectionSummaryLoading, value);
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

    private void RestartLibrarySelectionSummary()
    {
        var generation = Interlocked.Increment(
            ref _librarySelectionSummaryGeneration);
        var images = Library.GetSelectedImages().ToList();
        PublishLibrarySelectionSummary(
            BuildInitialSelectionSummary(images),
            images.Count >= 2);

        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(
            ref _librarySelectionSummaryCts,
            nextCts);
        previousCts?.Cancel();

        var previousTask = _librarySelectionSummaryTask;
        _librarySelectionSummaryTask = RunLibrarySelectionSummaryAfterAsync(
            previousTask,
            images,
            generation,
            nextCts);
    }

    private async Task RunLibrarySelectionSummaryAfterAsync(
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
                PublishLibrarySelectionSummary(
                    BuildInitialSelectionSummary(images),
                    isLoading: false,
                    generation);
                return;
            }

            await AggregateLibrarySelectionSummaryAsync(
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
            PublishLibrarySelectionSummary(
                BuildInitialSelectionSummary(images),
                isLoading: false,
                generation);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _librarySelectionSummaryCts,
                null,
                summaryCts);
            summaryCts.Dispose();
        }
    }

    private async Task AggregateLibrarySelectionSummaryAsync(
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

            PublishLibrarySelectionSummary(
                BuildSelectionSummary(members.Values),
                isLoading: true,
                generation);
        }

        PublishLibrarySelectionSummary(
            BuildSelectionSummary(members.Values),
            isLoading: false,
            generation);
    }

    private void EnsureCurrentSelectionSummary(int generation)
    {
        if (generation != Volatile.Read(
                ref _librarySelectionSummaryGeneration))
        {
            throw new OperationCanceledException();
        }
    }

    private async Task CancelLibrarySelectionSummaryAsync()
    {
        Interlocked.Increment(ref _librarySelectionSummaryGeneration);
        Interlocked.Exchange(ref _librarySelectionSummaryCts, null)?.Cancel();
        PublishLibrarySelectionSummary(
            SelectionSummary.Empty,
            isLoading: false);
        while (true)
        {
            var observed = _librarySelectionSummaryTask;
            await observed;
            if (ReferenceEquals(observed, _librarySelectionSummaryTask))
            {
                return;
            }
        }
    }

    internal Task WaitForLibrarySelectionSummaryAsync() =>
        _librarySelectionSummaryTask;

    private void PublishLibrarySelectionSummary(
        SelectionSummary summary,
        bool isLoading,
        int? generation = null)
    {
        if (generation.HasValue &&
            generation.Value != Volatile.Read(
                ref _librarySelectionSummaryGeneration))
        {
            return;
        }

        LibrarySelectionCount = summary.Count;
        LibrarySelectionOnlineOnlyCount = summary.OnlineOnlyCount;
        LibrarySelectionCombinedFileSize = summary.CombinedFileSize;
        LibrarySelectionEarliestDate = summary.EarliestDate;
        LibrarySelectionLatestDate = summary.LatestDate;
        IsLibrarySelectionSummaryLoading = isLoading && summary.Count >= 2;
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
