using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly object _selectionMetadataLoadsSync = new();
    private readonly HashSet<Task> _selectionMetadataLoads = new();

    private void StartSelectionMetadataLoad(ImageFile image)
    {
        var load = LoadSelectionMetadataSafelyAsync(image);
        lock (_selectionMetadataLoadsSync) _selectionMetadataLoads.Add(load);
        _ = RemoveSelectionMetadataLoadWhenCompleteAsync(load);
    }

    private async Task LoadSelectionMetadataSafelyAsync(ImageFile image)
    {
        try
        {
            await _loadMetadataAsync(image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Metadata load failed for {image.FilePath}: {ex.Message}");
        }
        finally
        {
            CompleteSelectedMetadataLoad(image);
        }
    }

    private async Task RemoveSelectionMetadataLoadWhenCompleteAsync(Task load)
    {
        await load;
        lock (_selectionMetadataLoadsSync) _selectionMetadataLoads.Remove(load);
    }

    private async Task WaitForSelectionMetadataLoadsAsync()
    {
        Task[] loads;
        lock (_selectionMetadataLoadsSync)
        {
            loads = _selectionMetadataLoads.ToArray();
        }

        await Task.WhenAll(loads);
    }
}
