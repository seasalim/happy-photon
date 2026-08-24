using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryTests
{
    // Most test states differ by Exposure (compared by EqualsIgnoringRotation).
    private static EditSettings S(double exposure) => new() { Exposure = exposure };

    [Fact]
    public void FreshHistory_NothingToUndoOrRedo()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        // Argument is required but unused on the empty-stack path.
        Assert.Null(history.Undo(S(9)));
        Assert.Null(history.Redo(S(9)));
    }

    [Fact]
    public void UndoThenRedo_RoundTripsSingleEdit()
    {
        var history = new EditHistory();
        var a = S(1);
        var b = S(2);

        history.PushEdit(a);
        Assert.True(history.CanUndo);

        var undone = history.Undo(b);
        Assert.Same(a, undone);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        var redone = history.Redo(a);
        Assert.Same(b, redone);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void MultiStepSequence_UndoesAndRedoesInOrder()
    {
        var history = new EditHistory();
        var a = S(1);
        var b = S(2);
        var c = S(3);

        history.PushEdit(a);
        history.PushEdit(b);

        Assert.Same(b, history.Undo(c));
        Assert.Same(a, history.Undo(b));
        Assert.False(history.CanUndo);

        Assert.Same(b, history.Redo(a));
        Assert.Same(c, history.Redo(b));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void PushEditAfterUndo_ClearsRedoBranch()
    {
        var history = new EditHistory();
        history.PushEdit(S(1));
        history.Undo(S(2));
        Assert.True(history.CanRedo);

        history.PushEdit(S(3));

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(S(4)));

        // Cleared branch stays gone through a further undo/redo cycle.
        history.Undo(S(5));
        history.Redo(S(6));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void PushEdit_DedupsEqualState_AndStillClearsRedo()
    {
        var history = new EditHistory();
        var a = S(1);
        history.PushEdit(a);
        history.PushEdit(S(2));
        history.Undo(S(3));           // undo = [a], redo = [S(3)]
        Assert.True(history.CanRedo);

        history.PushEdit(S(1));       // equal to a via EqualsIgnoringRotation

        Assert.False(history.CanRedo);          // redo cleared despite dedup
        Assert.Same(a, history.Undo(S(1)));     // only one entry
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void PushEdit_NoDedup_PushesEqualState()
    {
        var history = new EditHistory();
        var a = S(1);
        var a2 = S(1);                // equal to a via EqualsIgnoringRotation
        history.PushEdit(a);
        history.PushEdit(S(2));
        history.Undo(S(3));           // undo = [a], redo = [S(3)]

        history.PushEdit(a2, dedup: false);

        Assert.False(history.CanRedo);          // redo still cleared
        Assert.Same(a2, history.Undo(S(1)));    // both entries present
        Assert.Same(a, history.Undo(a2));
        Assert.False(history.CanUndo);
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
    public void Clear_EmptiesBothStacks()
    {
        var history = new EditHistory();
        history.PushEdit(S(1));
        history.Undo(S(2));

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}
