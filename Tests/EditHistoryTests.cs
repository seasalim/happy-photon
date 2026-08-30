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

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(0, 1, false)]
    public void TruncateAbovePublishesWithoutAnAppend(
        int position,
        int expectedCount,
        bool canUndo)
    {
        var history = Loaded(0, 1, 2, 3);

        history.Publish(new CatalogEditHistoryMutation(position, [], position));

        Assert.Equal(expectedCount, history.Entries.Count);
        Assert.Equal(position, history.Position);
        Assert.Equal(canUndo, history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.Entries[position].IsCurrent);
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
    public void CropDivergenceSeedsTheSavedStateAsOriginal()
    {
        var history = Loaded(0);
        var cropped = new EditSettings
        {
            Crop = new CropRegion { Left = .1, Right = .9 }
        };
        var edited = cropped.Clone();
        edited.Exposure = 1;

        var mutation = history.PrepareAppend(cropped, edited)!;

        Assert.Equal(["Original", "Exposure +1.00 (+1.00)"],
            mutation.Appended.Select(entry => entry.Label));
        Assert.Equal(.1, mutation.Appended[0].Settings.Crop!.Left);
    }

    [Fact]
    public void RotationHorizonCropAndManualGeometryAppend()
    {
        var history = new EditHistory();
        Assert.NotNull(history.PrepareAppend(
            new EditSettings(), new EditSettings { Rotation = 90 }));
        Assert.NotNull(history.PrepareAppend(
            new EditSettings(), new EditSettings { HorizonRotation = 2 }));
        Assert.NotNull(history.PrepareAppend(
            new EditSettings(), new EditSettings
            {
                Crop = new CropRegion { Left = .1, Right = .9 }
            }));
        Assert.NotNull(history.PrepareAppend(
            new EditSettings(), new EditSettings
            {
                Geometry = new GeometrySettings { Vertical = 1 }
            }));
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

        Assert.False(baseline.HasSameEdits(composite));
        Assert.False(baseline.HasSameEdits(red));
        Assert.False(baseline.HasSameEdits(green));
        Assert.False(baseline.HasSameEdits(blue));
        Assert.False(red.HasSameEdits(green));
    }

    [Fact]
    public void EqualityIncludesEveryGeometryField()
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

        Assert.False(baseline.HasSameEdits(manual));
        Assert.False(baseline.HasSameEdits(crop));
        Assert.False(baseline.HasSameEdits(new EditSettings
        {
            Crop = new CropRegion()
        }));
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
