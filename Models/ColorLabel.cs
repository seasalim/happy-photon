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
    public string ToolTip => $"Set {Name.ToLowerInvariant()} label; click again to clear";
    public string AutomationName => $"Set {Name.ToLowerInvariant()} color label";
}

public sealed record ColorLabelFilterChoice(ColorLabelFilter Value, string Name)
{
    /// <summary>True for the color slots, which show a swatch instead of their name.</summary>
    public bool IsColorSlot =>
        Value is not (ColorLabelFilter.All or ColorLabelFilter.None);
}
