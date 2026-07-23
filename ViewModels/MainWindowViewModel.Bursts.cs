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
                ShowTransientStatus("Analyzing capture times…");
            }
        }
        else
        {
            ClearBurstIndicators();
        }
    }

    private void ResetBurstState()
    {
        _burstGroups = null;
        _burstMembership = null;
    }

    private async Task SweepMetadataAndComputeBurstsAsync(
        List<ImageFile> images,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var image in images)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await ImageService.LoadMetadataAsync(image);
            }

            if (cancellationToken.IsCancellationRequested)
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
                for (var imageIndex = 0; imageIndex < group.ImageIds.Count; imageIndex++)
                {
                    membership[group.ImageIds[imageIndex]] = (
                        group.Id,
                        groupIndex + 1,
                        imageIndex + 1,
                        group.ImageIds.Count);
                }
            }

            _burstGroups = groups;
            _burstMembership = membership;

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

    private void ApplyBurstIndicators()
    {
        if (_burstMembership == null)
        {
            return;
        }

        foreach (var image in Library.AllImages)
        {
            if (_burstMembership.TryGetValue(image.FilePath, out var membership))
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
    }

    private void ClearBurstIndicators()
    {
        foreach (var image in Library.AllImages)
        {
            image.BurstGroupOrdinal = 0;
            image.BurstIndex = 0;
            image.BurstSize = 0;
            image.IsBurstHighlighted = false;
        }
    }
}
