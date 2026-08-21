using Microsoft.Data.Sqlite;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorLabelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("label");

    [Fact]
    public void Slots_ArePinnedAndEnumDrivenChoicesIncludeEverySlot()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5],
            Enum.GetValues<ColorLabel>().Select(value => (int)value));
        using var catalog = _fx.CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);

        Assert.Equal(5, vm.ColorLabelChoices.Count);
        Assert.Equal(Enum.GetValues<ColorLabel>().Skip(1),
            vm.ColorLabelChoices.Select(choice => choice.Value));
    }

    [Fact]
    public async Task Names_DefaultMalformedAndRenameRoundTripWithoutImageRewrite()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(_fx.Path("a.jpg"));
        var names = new ColorLabelNames(catalog);
        Assert.Equal("Red", (await names.LoadAsync())[ColorLabel.Red]);
        await catalog.SetAppSettingAsync(ColorLabelNames.SettingKey, "bad-json");
        Assert.Equal("Red", (await names.LoadAsync())[ColorLabel.Red]);
        var updated = new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Select"
        };
        await names.SaveAsync(updated);

        Assert.Equal("Select", (await names.LoadAsync())[ColorLabel.Red]);
        Assert.Equal(0, await ReadLabelAsync(id));
        var choice = new ColorLabelChoice(ColorLabel.Red, "Select");
        Assert.Contains("select", choice.ToolTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select", choice.AutomationName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveLoadAndUnknownStoredValue_RoundTripWithoutRewrite()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var path = _fx.Path("a.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveColorLabelAsync([id], ColorLabel.Green);
        Assert.Equal(ColorLabel.Green,
            (await catalog.LoadImageStatesAsync([path]))[path].ColorLabel);
        await WriteLabelAsync(id, 99);

        Assert.Equal(ColorLabel.None,
            (await catalog.LoadImageStatesAsync([path]))[path].ColorLabel);
        Assert.Equal(99, await ReadLabelAsync(id));
    }

    [Fact]
    public async Task FolderLoad_CarriesCatalogLabelIntoImageFile()
    {
        var photos = Directory.CreateDirectory(_fx.Path("photos")).FullName;
        var path = Path.Combine(photos, "labeled.jpg");
        TestImages.WriteJpeg(path);

        using var catalog = await _fx.CreateCatalogAsync("catalog");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveColorLabelAsync([id], ColorLabel.Yellow);
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            postSelection: action => action());

        await vm.LoadFolderAsync(photos);

        Assert.Equal(
            ColorLabel.Yellow,
            Assert.Single(vm.Library.AllImages).ColorLabel);
    }

    [Fact]
    public async Task SetBasedWrite_NormalizesDuplicatesAndRollsBackOnMissingRow()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var paths = Enumerable.Range(0, 2000)
            .Select(index => _fx.Path($"{index}.jpg"))
            .ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(paths);
        var ids = paths.Select(path => states[path].CatalogId).ToList();
        await catalog.SaveColorLabelAsync(ids.Concat([ids[0]]).ToArray(), ColorLabel.Blue);
        Assert.Equal(2000, await CountLabelAsync(ColorLabel.Blue));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.SaveColorLabelAsync([ids[0], long.MaxValue], ColorLabel.Red));
        Assert.Equal((int)ColorLabel.Blue, await ReadLabelAsync(ids[0]));
    }

    [Fact]
    public async Task Authoring_SelectionWinsAndTogglesOverWholeMaterializedSet()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(catalog);
        var active = await CreateCatalogImageAsync(catalog, "active.jpg", ColorLabel.Blue);
        var first = await CreateCatalogImageAsync(catalog, "first.jpg", ColorLabel.Red);
        var second = await CreateCatalogImageAsync(catalog, "second.jpg", ColorLabel.Red);
        vm.Library.SetImages([active, first, second]);
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(first);
        vm.Library.ToggleSelection(second);

        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red);
        Assert.Equal(ColorLabel.None, first.ColorLabel);
        Assert.Equal(ColorLabel.None, second.ColorLabel);
        Assert.Equal(ColorLabel.Blue, active.ColorLabel);

        second.ColorLabel = ColorLabel.Green;
        await catalog.SaveColorLabelAsync([second.CatalogId], ColorLabel.Green);
        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red);
        Assert.Equal(ColorLabel.Red, first.ColorLabel);
        Assert.Equal(ColorLabel.Red, second.ColorLabel);
        Assert.Equal(ColorLabel.Blue, active.ColorLabel);
    }

    [Fact]
    public async Task Authoring_ReselectsVisibleImageWhenActiveTargetIsFilteredOut()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(catalog);
        var active = await CreateCatalogImageAsync(catalog, "active.jpg", ColorLabel.Red);
        var target = await CreateCatalogImageAsync(catalog, "target.jpg", ColorLabel.Red);
        var replacement = await CreateCatalogImageAsync(
            catalog,
            "replacement.jpg",
            ColorLabel.Red);
        vm.Library.SetImages([active, replacement, target]);
        vm.Library.ColorLabelFilter = ColorLabelFilter.Red;
        vm.SelectedImage = active;
        vm.Library.ToggleSelection(active);
        vm.Library.ToggleSelection(target);

        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Red);

        Assert.Equal(ColorLabel.None, active.ColorLabel);
        Assert.Equal(ColorLabel.None, target.ColorLabel);
        Assert.Same(replacement, vm.SelectedImage);
        Assert.True(vm.Library.ContainsVisible(vm.SelectedImage));
    }

    [Fact]
    public void Filter_CombinesAxesAndDeselectsHidden()
    {
        var red = new ImageFile(_fx.Path("red.jpg"))
        {
            ColorLabel = ColorLabel.Red,
            Flag = ImageFlag.Picked,
            Rating = 4,
            IsSelected = true
        };
        var blue = new ImageFile(_fx.Path("blue.cr2"))
        {
            ColorLabel = ColorLabel.Blue,
            Flag = ImageFlag.Picked,
            Rating = 5
        };
        var state = new LibraryImageState();
        state.SetImages([red, blue]);
        state.FlagFilter = FlagFilter.Picked;
        state.MinimumRating = 4;
        state.FileTypeFilter = ImageFileTypeFilter.Jpeg;
        state.ColorLabelFilter = ColorLabelFilter.Blue;

        Assert.Empty(state.VisibleImages);
        Assert.False(red.IsSelected);
    }

    [Fact]
    public void ShortcutCatalogAndBindings_AreHandSyncedWithoutNumpadOrPurple()
    {
        var xaml = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Views", "MainWindow.axaml"));
        foreach (var (key, label) in new[]
        {
            ("D6", "Red"), ("D7", "Yellow"),
            ("D8", "Green"), ("D9", "Blue")
        })
        {
            Assert.Contains($"Gesture=\"{key}\"", xaml);
            Assert.Contains($">{label}</models:ColorLabel>", xaml);
        }
        Assert.DoesNotContain("NumPad", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Purple</models:ColorLabel>", xaml);
        Assert.Contains(ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "6–9" &&
                     entry.Action.Contains("color", StringComparison.OrdinalIgnoreCase));
    }

    private async Task WriteLabelAsync(long id, int value)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE images SET color_label = @value WHERE id = @id;";
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<ImageFile> CreateCatalogImageAsync(
        CatalogService catalog,
        string name,
        ColorLabel colorLabel)
    {
        var image = new ImageFile(_fx.Path(name))
        {
            ColorLabel = colorLabel
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveColorLabelAsync([image.CatalogId], colorLabel);
        return image;
    }

    private async Task<int> ReadLabelAsync(long id)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT color_label FROM images WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountLabelAsync(ColorLabel label)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM images WHERE color_label = @label;";
        command.Parameters.AddWithValue("@label", (int)label);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(
            $"Data Source={_fx.Path("catalog.db")};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    public void Dispose() => _fx.Dispose();
}
