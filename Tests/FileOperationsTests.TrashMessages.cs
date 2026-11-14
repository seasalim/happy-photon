using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FileOperationsTests
{
    [Fact]
    public async Task DeleteBatch_ReportsEachTrashFailureWithItsFileOrSidecarMessage()
    {
        using var catalog = await _fx.CreateCatalogAsync("trash-messages");
        var names = new[] { "online", "unsafe", "stuck", "online-unsafe", "unsafe-stuck", "side-online", "side-unsafe", "side-stuck" };
        var images = new List<ImageFile>();
        foreach (var name in names)
        {
            images.Add(await CreateCatalogImageAsync(catalog, name + ".jpg"));
        }

        string PathOf(string name) => images[Array.IndexOf(names, name)].FilePath;

        string SidecarOf(string name) => PathOf(name) + ".xmp";

        foreach (var name in new[] { "side-online", "side-unsafe", "side-stuck" })
        {
            await File.WriteAllTextAsync(SidecarOf(name), "sidecar");
        }

        var onlineOnly = new[] { PathOf("online"), PathOf("online-unsafe"), SidecarOf("side-online") };
        var unsafePaths = new[] { PathOf("unsafe"), PathOf("online-unsafe"), PathOf("unsafe-stuck"), SidecarOf("side-unsafe") };
        var stuck = new[] { PathOf("stuck"), PathOf("unsafe-stuck"), SidecarOf("side-stuck") };
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally)
        {
            Resolver = path => onlineOnly.Contains(path)
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        var operations = new TestFileOperationService
        {
            Assessment = path => new TrashPathAssessment(!unsafePaths.Contains(path), null),
            MoveResult = path => !stuck.Contains(path)
        };
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability,
            fileOperationService: operations);
        vm.Browse.SetImages(images);
        vm.Browse.SelectAllVisible();
        vm.ConfirmDeleteAsync = _ => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        var expected = new Dictionary<string, string>
        {
            [PathOf("online")] = "The file is online-only and was not downloaded.",
            [PathOf("unsafe")] = "The file cannot be moved to Trash safely.",
            [PathOf("stuck")] = "The file could not be moved to Trash.",
            [PathOf("online-unsafe")] = "The file is online-only and was not downloaded.",
            [PathOf("unsafe-stuck")] = "The file cannot be moved to Trash safely.",
            [SidecarOf("side-online")] = "The sidecar is online-only and was not downloaded.",
            [SidecarOf("side-unsafe")] = "The sidecar cannot be moved to Trash safely.",
            [SidecarOf("side-stuck")] = "The sidecar could not be moved to Trash."
        };
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            summary!.Select(failure => KeyValuePair.Create(failure.Path, failure.Reason))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.DoesNotContain(PathOf("unsafe-stuck"), operations.MovedPaths);
        Assert.DoesNotContain(PathOf("online-unsafe"), operations.MovedPaths);
    }
}
