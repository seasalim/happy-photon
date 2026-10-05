using Avalonia.Controls;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ControlBarTooltipInventory
{
    private static readonly string[] AssessmentNames =
    [
        "PickImageButton", "RejectImageButton", "UnflagImageButton",
        "Rating1Button", "Rating2Button", "Rating3Button", "Rating4Button", "Rating5Button", "ClearRatingButton"
    ];

    internal static Button[] Full(Control bar, bool develop)
    {
        string[] modeNames = develop
            ? ["PreviousImageButton", "NextImageButton", "RotateLeftButton", "ZoomFitButton", "ActualSizeButton",
                "ColorAssessmentButton", "FullScreenButton", "BeforeAfterSplitButton", "RawJpegSwitchButton"]
            : ["PreviousImageButton", "NextImageButton", "LoupeViewButton", "CompareViewButton", "BurstsButton",
                "PairsButton", "SmallThumbnailButton", "MediumThumbnailButton", "LargeThumbnailButton"];
        var buttons = modeNames.Concat(AssessmentNames)
            .Select(name => ControlBarGateScene.Named<Button>(bar, name)).ToList();

        foreach (var label in Enum.GetValues<ColorLabel>().Where(label => label != ColorLabel.None))
        {
            buttons.Add(bar.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Name == "ColorLabelButton" && Equals(button.CommandParameter, label)));
        }

        if (develop)
        {
            buttons.Add(bar.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(ToolTip.GetTip(button), "Rotate Right (90° clockwise)")));
        }

        Assert.Equal(develop ? 24 : 23, buttons.Count);
        Assert.All(buttons, button => Assert.NotNull(ToolTip.GetTip(button)));
        Assert.All(buttons, button => Assert.True(button.IsEffectivelyVisible));
        var actual = bar.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && ToolTip.GetTip(button) is not null).ToArray();
        Assert.Equal(buttons.Count, actual.Length);
        Assert.All(actual, button => Assert.Contains(button, buttons));

        return buttons.ToArray();
    }
}
