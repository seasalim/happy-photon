using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncSpotBrowseTests
{
    [AvaloniaFact]
    public async Task BrowseCopyReadsMissingFactsOnlyOnFirstSpotPaste()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Repairs = RepairTestWorkload.S64() });
        var target = await fixture.ImageAsync("target", new());
        WriteJpeg(source);
        File.Copy(source.FilePath, target.FilePath);
        var reader = fixture.Vm.PasteFrameReader;
        var opens = new List<(string Path, string Kind)>();
        reader.Opening = (path, kind) => opens.Add((path, kind));
        await fixture.SelectAsync(source, develop: false);
        Assert.Null(fixture.Vm.PreviewImage);
        Assert.Null(reader.ReadSensorFrame(source, cachedOnly: true));
        Assert.Null(reader.ReadCamera(source, cachedOnly: true));
        Assert.True(fixture.Vm.CopyEditSettingsCommand.CanExecute(null));
        fixture.Vm.CopyEditSettingsCommand.Execute(null);
        Assert.True(fixture.Vm.HasCopiedSettings);
        Assert.Empty(opens);
        await fixture.SelectAsync(target, develop: false);
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Empty(opens);
        Assert.Null(target.EditSettings.Repairs);
        fixture.Vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(
            group => group.Name, group => group.Name == "Spot Removal"));
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(source.EditSettings.Repairs, target.EditSettings.Repairs);
        Assert.Equal(4, opens.Count);

        foreach (var file in new[] { source, target })
        {
            Assert.Single(opens, read => read.Path == file.FilePath && read.Kind == "frame");
            Assert.Single(opens, read => read.Path == file.FilePath && read.Kind == "body serial");
        }

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(4, opens.Count);
        Assert.Equal(1, (await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Position);
    }

    [AvaloniaFact]
    public async Task JpegCameraFactsKeepRawPickerHiddenAndProfilePasteSkipped()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(loader: new StandardBaseLoader());
        var target = await fixture.ImageAsync("target", new());
        WriteJpeg(target);
        var reader = fixture.Vm.PasteFrameReader;
        var settings = new EditSettings
        {
            RawProfile = new() { Source = RawProfileSource.UserFile, Location = "profile.dcp", ContentHash = new string('a', 64) },
            Lens = new() { ProfileOverride = "Canon EF 50mm f/1.8 MkII" }
        };
        var source = new PhotoProfileSnapshot("source.cr2", true, new(new("Canon", "EOS 6D"), false));
        var groups = EditSettingsTransfer.Groups.Where(group => group.Name is "Camera Profile" or "Lens Profile").ToArray();
        var before = new Dictionary<string, string>();
        Assert.Empty(ProfileSettingsTransfer.CompatibleGroups(source, settings, target, groups, reader, before, true));
        Assert.Equal("not RAW", before["Camera Profile"]);
        Assert.Equal("not RAW", before["Lens Profile"]);

        await fixture.SelectAsync(target);
        using var basis = fixture.Vm.ImageService.Previews.AcquireLocalRangeBase(
            target, target.EditSettings, BaseImage.InteractivePreviewMaxDimension);
        Assert.NotNull(basis);
        Assert.Equal(new CameraIdentity("Canon", "EOS 6D"), basis.Base.Info.CameraIdentity);
        Assert.Equal(new PhotoSensorFrame(16, 12, 1), basis.Base.Info.SensorFrame);
        Assert.False(fixture.Vm.RawProfilePickerState.IsVisible);
        Assert.False(fixture.Vm.RawProfilePickerState.IsLoading);
        Assert.Empty(fixture.Vm.RawProfilePickerState.StatusMessage);
        Assert.True(Assert.Single(fixture.Vm.RawProfilePickerState.Options, option => option.IsProfile).IsBuiltIn);
        var picker = fixture.Vm.RawProfilePickerState;
        await fixture.Vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Same(picker, fixture.Vm.RawProfilePickerState);
        Assert.Equal(basis.Base.Info.SensorFrame, reader.ReadSensorFrame(target));
        Assert.Equal(basis.Base.Info.CameraIdentity, reader.ReadCamera(target, cachedOnly: true)!.Identity);
        var after = new Dictionary<string, string>();
        reader.Opening = (_, _) => Assert.Fail("Profile paste on JPEG must not open a header");
        Assert.Empty(ProfileSettingsTransfer.CompatibleGroups(source, settings, target, groups, reader, after, false));
        Assert.Equal(before, after);
        Assert.Same(picker, fixture.Vm.RawProfilePickerState);
    }

    private static void WriteJpeg(ImageFile file)
    {
        using var image = new MagickImage(MagickColors.Gray, 16, 12);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Canon");
        exif.SetValue(ExifTag.Model, "EOS 6D");
        exif.SetValue(ExifTag.SerialNumber, "233054000882");

        image.SetProfile(exif);
        image.Write(file.FilePath);
    }
}
