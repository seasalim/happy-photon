using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _showBurstGroups;

    private IReadOnlyList<BurstGroup>? _burstGroups;
    private Dictionary<string, (string BurstId, int Ordinal, int Index, int Size)>? _burstMembership;
    private CancellationTokenSource? _burstAnalysisCts;
    private Task _burstAnalysisTask = Task.CompletedTask;
    // Burst lifecycle methods and this restart handshake are UI-thread-affine.
    private bool _burstAnalysisRestartRequested;
    private int _burstAnalysisActive;
    private int _burstAnalysisProcessed;
    private int _burstAnalysisTotal;

    internal bool IsBurstAnalysisActive =>
        Volatile.Read(ref _burstAnalysisActive) != 0;
    internal int BurstAnalysisProcessed =>
        Volatile.Read(ref _burstAnalysisProcessed);
    internal int BurstAnalysisTotal =>
        Volatile.Read(ref _burstAnalysisTotal);

    internal bool BurstsComputed => _burstGroups != null;

    internal (string BurstId, int Index, int Size)? GetBurstMembership(string filePath) =>
        _burstMembership != null && _burstMembership.TryGetValue(filePath, out var membership)
            ? (membership.BurstId, membership.Index, membership.Size)
            : null;

    partial void OnShowBurstGroupsChanged(bool value)
    {
        if (value)
        {
            if (_burstGroups != null)
            {
                ApplyBurstIndicators();
            }
            else
            {
                StartBurstAnalysisIfRequested();
            }
        }
        else
        {
            _burstAnalysisRestartRequested = false;
            CancelBurstAnalysis();
            // Remove the published capture-time activity immediately; the sweep
            // task may stay blocked in a non-cancellable metadata load.
            Volatile.Write(ref _burstAnalysisActive, 0);
            ClearBurstIndicators();
        }
    }

    private void ResetBurstState()
    {
        _burstAnalysisRestartRequested = false;
        CancelBurstAnalysis();
        _burstGroups = null;
        _burstMembership = null;
    }

    private bool StartBurstAnalysisIfRequested()
    {
        if (!ShowBurstGroups || _burstGroups != null ||
            Browse.AllImages.Count == 0)
        {
            return false;
        }

        var current = _burstAnalysisCts;
        if (current != null)
        {
            _burstAnalysisRestartRequested = true;
            return true;
        }

        var folderCancellation = _thumbnailLoadingCts;
        if (folderCancellation == null || folderCancellation.IsCancellationRequested)
        {
            return false;
        }

        CancellationTokenSource analysisCts;
        try
        {
            analysisCts = CancellationTokenSource.CreateLinkedTokenSource(
                folderCancellation.Token);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        _burstAnalysisRestartRequested = false;
        Interlocked.Exchange(ref _burstAnalysisCts, analysisCts);
        Volatile.Write(ref _burstAnalysisProcessed, 0);
        Volatile.Write(ref _burstAnalysisTotal, Browse.AllImages
            .Select(image => image.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Volatile.Write(ref _burstAnalysisActive, 1);
        SignalBackgroundActivityStarted();
        _burstAnalysisTask = RunBurstAnalysisAsync(
            Browse.AllImages.GroupBy(image => image.FilePath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList(),
            Volatile.Read(ref _browseGeneration),
            analysisCts);
        return true;
    }

    private async Task RunBurstAnalysisAsync(
        List<ImageFile> images,
        int generation,
        CancellationTokenSource analysisCts)
    {
        try
        {
            await SweepMetadataAndComputeBurstsAsync(
                images,
                generation,
                analysisCts.Token);
        }
        finally
        {
            var shouldRestart = _burstAnalysisRestartRequested &&
                ShowBurstGroups &&
                _burstGroups == null;
            // Defensive ownership check if overlapping analyses are ever allowed.
            var cleared = ReferenceEquals(Interlocked.CompareExchange(
                ref _burstAnalysisCts,
                null,
                analysisCts), analysisCts);
            analysisCts.Dispose();
            var restarted = false;
            if (cleared && shouldRestart)
            {
                _burstAnalysisRestartRequested = false;
                restarted = StartBurstAnalysisIfRequested();
            }
            if (cleared && !restarted)
                Volatile.Write(ref _burstAnalysisActive, 0);
        }
    }

    private void CancelBurstAnalysis() => _burstAnalysisCts?.Cancel();

    internal async Task WaitForBurstAnalysisAsync()
    {
        while (true)
        {
            var observed = _burstAnalysisTask;
            await observed;
            if (ReferenceEquals(observed, _burstAnalysisTask))
            {
                return;
            }
        }
    }

    private async Task SweepMetadataAndComputeBurstsAsync(
        List<ImageFile> images,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzedCount = 0;
            var skippedCount = 0;
            foreach (var image in images)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    var availability = ImageService.GetSourceAvailability(image);
                    if (availability.IsOnlineOnly())
                    {
                        SetSiblingHydrationState(image, true, generation);
                        skippedCount++;
                        continue;
                    }
                    if (availability == SourceAvailability.Unavailable)
                    {
                        continue;
                    }

                    await _loadMetadataAsync(image);
                    if (image.MetadataLoaded)
                    {
                        foreach (var sibling in Browse.AllImages.Where(candidate =>
                            string.Equals(candidate.FilePath, image.FilePath,
                                StringComparison.OrdinalIgnoreCase)))
                            sibling.CopyMetadataFrom(image);
                        SetSiblingHydrationState(image, false, generation);
                        analyzedCount++;
                    }
                    else if (ImageService.GetSourceAvailability(image)
                                 .IsOnlineOnly())
                    {
                        SetSiblingHydrationState(image, true, generation);
                        skippedCount++;
                    }
                }
                finally
                {
                    Interlocked.Increment(ref _burstAnalysisProcessed);
                }
            }

            if (cancellationToken.IsCancellationRequested ||
                generation != Volatile.Read(ref _browseGeneration))
            {
                return;
            }

            var (groups, _) = BurstGroupingService.ComputeGroups(
                images.Select(image => (image.FilePath, image.DateTaken)));
            var membership = new Dictionary<string, (string, int, int, int)>(
                StringComparer.Ordinal);

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                for (var captureIndex = 0;
                     captureIndex < group.Captures.Count;
                     captureIndex++)
                {
                    foreach (var imageId in group.Captures[captureIndex].ImageIds)
                    {
                        membership[imageId] = (
                            group.Id,
                            groupIndex + 1,
                            captureIndex + 1,
                            group.Captures.Count);
                    }
                }
            }

            _burstGroups = groups;
            _burstMembership = membership;
            _burstAnalysisRestartRequested = false;
            var analyzedNoun = analyzedCount == 1 ? "photo" : "photos";
            var skippedNoun = skippedCount == 1 ? "photo" : "photos";
            ShowTransientStatus(
                $"Burst analysis complete — {analyzedCount:N0} local {analyzedNoun} analyzed; " +
                $"{skippedCount:N0} online-only {skippedNoun} skipped.");

            if (ShowBurstGroups)
            {
                ApplyBurstIndicators();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Burst sweep failed: {ex.Message}");
        }
    }

    private void SetSiblingHydrationState(
        ImageFile image,
        bool requiresHydration,
        int generation)
    {
        if (generation != Volatile.Read(ref _browseGeneration)) return;
        foreach (var sibling in Browse.AllImages.Where(candidate =>
                     string.Equals(candidate.FilePath, image.FilePath,
                         StringComparison.OrdinalIgnoreCase)))
            SetSourceRequiresHydration(sibling, requiresHydration);
    }

    private void ApplyBurstIndicators()
    {
        if (_burstMembership == null)
        {
            return;
        }

        foreach (var image in Browse.AllImages)
        {
            ApplyBurstIndicator(image);
        }
    }

    private void ApplyBurstIndicator(ImageFile image)
    {
        if (ShowBurstGroups && _burstMembership != null &&
            _burstMembership.TryGetValue(image.FilePath, out var membership))
        {
            image.BurstGroupOrdinal = membership.Ordinal;
            image.BurstIndex = membership.Index;
            image.BurstSize = membership.Size;
        }
        else
        {
            image.BurstGroupOrdinal = 0;
            image.BurstIndex = 0;
            image.BurstSize = 0;
        }

        image.IsBurstHighlighted = false;
    }

    private void ClearBurstIndicators()
    {
        foreach (var image in Browse.AllImages)
        {
            image.BurstGroupOrdinal = 0;
            image.BurstIndex = 0;
            image.BurstSize = 0;
            image.IsBurstHighlighted = false;
        }
    }
}
