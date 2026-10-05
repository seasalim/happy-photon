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
    // Keys 6–9 match the KeyBindings in MainWindow.axaml; Purple has no shortcut.
    public string ToolTip => Value is >= ColorLabel.Red and <= ColorLabel.Blue
        ? $"{Name} label ({(int)Value + 5})"
        : $"{Name} label";
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
