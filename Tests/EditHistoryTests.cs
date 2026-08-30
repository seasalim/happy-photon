using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryTests
{
    private static EditSettings S(double exposure) => new() { Exposure = exposure };

    [Fact]
    public void AppendCreatesOriginalAndCurrentStep()
    {
        var history = new EditHistory();
        var mutation = history.PrepareAppend(S(0), S(1), "Exposure +1.00");

        history.Publish(Assert.IsType<CatalogEditHistoryMutation>(mutation));

        Assert.Equal(2, history.Entries.Count);
        Assert.Equal(["Original", "Exposure +1.00"],
            history.Entries.Select(entry => entry.Label));
        Assert.Equal(1, history.Position);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void PositionMovesPreserveRedoAndNextAppendTruncatesIt()
    {
        var history = Loaded(0, 1, 2);
        history.PublishPosition(1);

        Assert.True(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Publish(history.PrepareAppend(S(1), S(4), "Exposure +4.00")!);

        Assert.Equal([0d, 1d, 4d],
            history.Entries.Select(entry => entry.Settings.Exposure));
        Assert.Equal(2, history.Position);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void EditAfterMiddleJumpDoesNotInsertOriginal()
    {
        var history = Loaded(0, 1, 2);
        history.PublishPosition(1);

        history.Publish(history.PrepareAppend(S(1), S(3))!);

        Assert.Equal(3, history.Entries.Count);
        Assert.Single(history.Entries, entry => entry.Label == "Original");
    }

    [Fact]
    public void DivergedSavedStateIsReconciledBeforeEdit()
    {
        var history = Loaded(0, 1);
        var mutation = history.PrepareAppend(S(9), S(10), "Edit")!;

        Assert.Equal(["Original", "Edit"],
            mutation.Appended.Select(entry => entry.Label));
        Assert.Equal(9, mutation.Appended[0].Settings.Exposure);
    }

    [Fact]
    public void RotationHorizonAndCropDoNotAppendButManualGeometryDoes()
    {
        var history = new EditHistory();
        var geometryOnly = new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 2,
            Crop = new CropRegion { Left = .1, Right = .9 }
        };
        Assert.Null(history.PrepareAppend(new EditSettings(), geometryOnly));

        geometryOnly.Geometry = new GeometrySettings { Vertical = 1 };
        Assert.NotNull(history.PrepareAppend(new EditSettings(), geometryOnly));
    }

    [Fact]
    public void EqualityDistinguishesEveryCurveAndNullFromPresent()
    {
        var baseline = new EditSettings();
        var composite = baseline.Clone();
        composite.Curve.AddPointAndReturnIndex(0.5, 0.7);
        var red = baseline.Clone();
        red.CurveRed = new CurveData();
        var green = baseline.Clone();
        green.CurveGreen = new CurveData();
        var blue = baseline.Clone();
        blue.CurveBlue = new CurveData();

        Assert.False(baseline.EqualsIgnoringRotation(composite));
        Assert.False(baseline.EqualsIgnoringRotation(red));
        Assert.False(baseline.EqualsIgnoringRotation(green));
        Assert.False(baseline.EqualsIgnoringRotation(blue));
        Assert.False(red.EqualsIgnoringRotation(green));
    }

    [Fact]
    public void EqualityIncludesManualGeometryButStillIgnoresCropGeometry()
    {
        var baseline = new EditSettings();
        var manual = new EditSettings
        {
            Geometry = new GeometrySettings { Vertical = 1 }
        };
        var crop = new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 3,
            Crop = new CropRegion { Left = 0.1, Right = 0.9 }
        };

        Assert.False(baseline.EqualsIgnoringRotation(manual));
        Assert.True(baseline.EqualsIgnoringRotation(crop));
    }

    [Fact]
    public void EqualSnapshotIsDeduplicated()
    {
        var history = Loaded(0, 1);
        Assert.Null(history.PrepareAppend(S(1), S(1)));
    }

    [Fact]
    public void ClearResetsListAndPosition()
    {
        var history = Loaded(0, 1);
        history.Clear();
        Assert.Empty(history.Entries);
        Assert.Equal(-1, history.Position);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    private static EditHistory Loaded(params double[] values)
    {
        var history = new EditHistory();
        history.Load(values.Select((value, index) => new CatalogEditHistoryEntry(
            index, index == 0 ? "Original" : $"Edit {index}", S(value))),
            values.Length - 1);
        return history;
    }
}
