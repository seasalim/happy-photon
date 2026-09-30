namespace HappyPhoton.Tests;

public sealed partial class LocalsHoverBaselineTests
{
    // Observed on 4294c63 through real pointer input; independent of production hit testing.
    private static string FrozenPressOutcome(string selected, string zone)
    {
        var target = selected;
        var gesture = "none";
        var changed = "none";

        switch (zone)
        {
            case "center":
            case "radial-pin" when selected == "radial":
            case "linear-pin" when selected == "linear":
                gesture = "Center";
                changed = selected + ".center";
                break;

            case "radial-pin":
                target = "radial";
                break;

            case "linear-pin":
                target = "linear";
                break;

            case "brush-pin":
                target = "brush";
                break;

            case "x+":
            case "handle-pin-overlap":
                gesture = "AxisXPositive";
                changed = "radial.Rx";
                break;

            case "x-":
                gesture = "AxisXNegative";
                changed = "radial.Rx";
                break;

            case "y+":
                gesture = "AxisYPositive";
                changed = "radial.Ry";
                break;

            case "y-":
                gesture = "AxisYNegative";
                changed = "radial.Ry";
                break;

            case "rotation":
                gesture = "Rotation";
                changed = "radial.angle";
                break;

            case "direction":
                gesture = "Direction";
                changed = "linear.angle";
                break;

            case "feather+":
            case "feather-":
                gesture = "Feather";
                changed = "linear.feather";
                break;

            case "ring-45":
            case "ring-135":
            case "ring-225":
            case "ring-315":
                gesture = "FeatherRing";
                changed = "radial.feather";
                break;
        }

        return $"selected={target}; gesture={gesture}; changed={changed}";
    }
}


