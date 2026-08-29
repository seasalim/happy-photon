using Avalonia;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WindowPlacementTests
{
    private static readonly (double Width, double Height) Minimum = (800, 500);
    private static readonly WindowPlacementScreen Primary = Screen(0, 0, 1920, 1040);

    public static TheoryData<string, WindowPlacement, WindowPlacementScreen[]> ResetCases => new()
    {
        { "wrong schema", Placement(version: 2), [Primary] },
        { "non-finite position", Placement(x: double.NaN), [Primary] },
        { "non-finite size", Placement(width: double.PositiveInfinity), [Primary] },
        { "non-finite scaling", Placement(scaling: double.NaN), [Primary] },
        { "below minimum width", Placement(width: 799), [Primary] },
        { "below minimum height", Placement(height: 499), [Primary] },
        { "larger than every screen", Placement(width: 2000), [Primary] },
        { "less than half visible", Placement(x: 1471), [Primary] },
        { "top edge outside working area", Placement(y: -1), [Primary] },
        { "removed secondary display", Placement(x: 2100), [Primary] },
        {
            "fits display union but no individual display",
            Placement(x: 400, width: 3000),
            [Primary, Screen(1920, 0, 1920, 1040)]
        },
        {
            "current DPI makes physical size too large",
            Placement(width: 1500),
            [Screen(0, 0, 1920, 1040, scaling: 1.5)]
        },
        {
            "saved DPI applies when top-left is outside every screen",
            Placement(x: -100, width: 1500, height: 500, scaling: 1.5),
            [Screen(0, 0, 1920, 1040, scaling: 1.5)]
        },
        {
            "taskbar moved above saved title bar",
            Placement(y: 0),
            [Screen(0, 0, 1920, 1080, workingY: 40, workingHeight: 1040)]
        }
    };

    [Theory]
    [MemberData(nameof(ResetCases))]
    public void Resolve_InvalidPlacement_UsesDefault(
        string reason,
        WindowPlacement saved,
        WindowPlacementScreen[] screens)
    {
        Assert.Null(WindowPlacement.Resolve(saved, screens, Minimum));
        Assert.NotNull(saved);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Resolve_MissingPlacement_UsesDefault() =>
        Assert.Null(WindowPlacement.Resolve(null, [Primary], Minimum));

    [Fact]
    public void Resolve_ValidSingleScreenPlacement_IsUnchanged()
    {
        var saved = Placement(x: 100, y: 80);

        Assert.Same(saved, WindowPlacement.Resolve(saved, [Primary], Minimum));
    }

    [Fact]
    public void Resolve_ValidMixedDpiSecondaryPlacement_UsesCurrentScreenScaling()
    {
        var saved = Placement(x: 2000, y: 100, width: 1000, scaling: 1);
        var secondary = Screen(1920, 0, 2560, 1440, scaling: 1.5);

        Assert.Same(saved, WindowPlacement.Resolve(
            saved, [Primary, secondary], Minimum));
    }

    [Fact]
    public void Resolve_ExactlyHalfVisible_IsValid()
    {
        var saved = Placement(x: 1420, width: 1000);

        Assert.Same(saved, WindowPlacement.Resolve(saved, [Primary], Minimum));
    }

    private static WindowPlacement Placement(
        int version = WindowPlacement.CurrentVersion,
        double x = 100,
        double y = 100,
        double width = 900,
        double height = 600,
        double scaling = 1,
        bool maximized = false) =>
        new(version, x, y, width, height, scaling, maximized);

    private static WindowPlacementScreen Screen(
        int x,
        int y,
        int width,
        int height,
        double scaling = 1,
        int? workingY = null,
        int? workingHeight = null) => new(
        new PixelRect(x, y, width, height),
        new PixelRect(
            x, workingY ?? y, width, workingHeight ?? height),
        scaling);
}

public sealed class WindowPlacementStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsWithoutCatalog()
    {
        using var directory = new TemporaryDirectory();
        var pointerRoot = Path.Combine(directory.Path, "pointer");
        var store = new WindowPlacementStore(pointerRoot);
        var placement = new WindowPlacement(1, 120, 80, 1100, 650, 1.25, true);

        store.Save(placement);

        Assert.Equal(placement, store.Load());
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "catalog")));
    }

    [Fact]
    public void Load_CorruptFileAndDirectoryAtPath_ReturnNull()
    {
        using var directory = new TemporaryDirectory();
        var missing = new WindowPlacementStore(Path.Combine(directory.Path, "missing"));
        var corrupt = new WindowPlacementStore(Path.Combine(directory.Path, "corrupt"));
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt.PlacementPath)!);
        File.WriteAllText(corrupt.PlacementPath, "{broken");
        var directoryPath = new WindowPlacementStore(Path.Combine(directory.Path, "directory"));
        Directory.CreateDirectory(directoryPath.PlacementPath);

        Assert.Null(missing.Load());
        Assert.Null(corrupt.Load());
        Assert.Null(directoryPath.Load());
    }

    [Fact]
    public void Save_UnwritablePointerPath_CompletesSilently()
    {
        using var directory = new TemporaryDirectory();
        var blocker = Path.Combine(directory.Path, "file");
        File.WriteAllText(blocker, "not a directory");
        var store = new WindowPlacementStore(Path.Combine(blocker, "pointer"));

        var exception = Record.Exception(() => store.Save(
            new WindowPlacement(1, 100, 100, 900, 600, 1, false)));

        Assert.Null(exception);
        Assert.False(File.Exists(store.PlacementPath));
    }
}
