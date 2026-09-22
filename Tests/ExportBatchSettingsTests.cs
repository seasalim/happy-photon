using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportBatchSettingsTests
{
    [Fact]
    public async Task ChangePhotos_UsesOnlyVisiblePicksAndPreservesBrowseSelectionOnReturn()
    {
        using var fixture = new CatalogVmFixture("export-batch");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        var photos = Enumerable.Range(0, 3).Select(i => new ImageFile(fixture.Path($"{i}.jpg"))
        {
            Flag = i < 2 ? ImageFlag.Picked : ImageFlag.Unflagged,
            Rating = i == 0 ? 0 : 5
        }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.SwitchToExportCommand.Execute(null);
        vm.ActiveExportCapture = vm.ExportCaptures[2];
        Assert.Same(photos[2], vm.SelectedImage);
        Assert.Equal(photos, vm.Browse.GetSelectedImages());
        vm.ChooseExportPhotosInBrowseCommand.Execute(null);
        Assert.True(vm.IsBrowseMode);
        Assert.Equal(photos, vm.Browse.GetSelectedImages());
        vm.Browse.MinimumRating = 5;
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(photos.Skip(1), vm.ExportCaptures.Select(c => c.Image));
        Assert.Equal(1, vm.VisiblePickedPhotoCount);
        vm.UsePickedPhotosCommand.Execute(null);
        Assert.Equal([photos[1]], vm.Browse.GetSelectedImages());
        Assert.Same(photos[1], Assert.Single(vm.ExportCaptures).Image);
        Assert.Equal(0, vm.ExportActivityScopeStartCount);
        photos[1].Flag = ImageFlag.Unflagged;
        vm.Browse.RefreshFilters();
        Assert.False(vm.UsePickedPhotosCommand.CanExecute(null));
        Assert.Equal("No picked photos in the current view", vm.UsePickedPhotosScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathExample_MatchesResolvedJobForVersionsAndSizes(bool twoSizes)
    {
        using var fixture = new CatalogVmFixture("export-path-example");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        var photos = new[] { new ImageFile(fixture.Path("photo.jpg")),
            new ImageFile(fixture.Path("photo.jpg")) { Version = 2 } };
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.ExportSettings.OutputFolder = fixture.Path("copies");
        vm.ExportSettings.ExportWeb = twoSizes;
        vm.ExportSettings.Format = ExportFormat.Tiff;
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(0, vm.ExportFilenameChoice);
        vm.ExportFilenameChoice = 1;
        Assert.True(vm.IsCustomExportFilename);
        Assert.Equal("{name}", vm.ExportSettings.NamingPattern);
        vm.ExportSettings.NamingPattern = "{date}_{name}";
        var job = vm.ExportSettings.CreateJob(photos);
        foreach (var capture in vm.ExportCaptures)
        {
            vm.ActiveExportCapture = capture;
            Assert.Equal(string.Join(Environment.NewLine, job.Targets
                .Where(t => t.Capture == capture.Image)
                .Select(t => Path.GetRelativePath(job.Output.OutputFolder, t.ResolvedPath))), vm.ExportPathExample);
            Assert.Contains($"-V{capture.Image.Version}.tif", vm.ExportPathExample);
        }
        vm.ExportFilenameChoice = 0;
        Assert.Equal("{name}", vm.ExportSettings.NamingPattern);
        vm.ExportSettings.NamingPattern = "custom";
        Assert.Equal(0, vm.ExportFilenameChoice);
        vm.ChooseExportPhotosInBrowseCommand.Execute(null);
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(1, vm.ExportFilenameChoice);
        vm.ExportSettings.NamingPattern = "{name}";
        Assert.Equal(1, vm.ExportFilenameChoice);
        vm.ChooseExportPhotosInBrowseCommand.Execute(null);
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(0, vm.ExportFilenameChoice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void ExplicitVariantsCannotBypassRangeValidation(int size)
    {
        var settings = new ExportSettings { OutputFolder = Path.GetTempPath() };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.CreateJob([], [new("web", size)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../photo")]
    [InlineData("bad:name")]
    public void InvalidCustomPatternsBlockJobs(string pattern)
    {
        var settings = new ExportSettings { OutputFolder = Path.GetTempPath(), NamingPattern = pattern };
        Assert.Contains("filename pattern", settings.ValidationReason);
        Assert.Throws<InvalidOperationException>(() => settings.CreateJob([]));
    }
}
