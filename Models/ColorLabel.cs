namespace HappyPhoton.Models;

public enum ColorLabel
{
    None = 0,
    Red = 1,
    Yellow = 2,
    Green = 3,
    Blue = 4,
    Purple = 5
}

public enum ColorLabelFilter
{
    All,
    None,
    Red,
    Yellow,
    Green,
    Blue,
    Purple
}

public sealed record ColorLabelChoice(ColorLabel Value, string Name)
{
    public string ToolTip =>
        $"Set {Name.ToLowerInvariant()} label on the Browse selection when non-empty, " +
        "otherwise the active photo; active photo only in Develop; click again to clear";
    public string AutomationName => $"Set {Name.ToLowerInvariant()} color label";
}

public sealed record ColorLabelFilterChoice(ColorLabelFilter Value, string Name)
{
    /// <summary>True for choices that render as a filled color swatch.</summary>
    public bool IsColorSlot =>
        Value is not (ColorLabelFilter.All or ColorLabelFilter.None);
    public bool IsNoneSlot => Value == ColorLabelFilter.None;

    public string ToolTip => Value switch
    {
        ColorLabelFilter.All => "Show all labels",
        ColorLabelFilter.None => "Show photos with no color label",
        _ => $"Show {Name.ToLowerInvariant()} label only"
    };

    public string AutomationName => ToolTip;
}
