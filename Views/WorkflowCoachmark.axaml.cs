using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

/// <summary>Edge of the coachmark that the photon trail extends from.</summary>
public enum CoachmarkPointer
{
    None,
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Where the trail sits along the edge it leaves from when the target is not
/// opposite the middle of the bubble.
/// </summary>
public enum CoachmarkPointerAlignment
{
    Center,
    Start,
    End
}

public partial class WorkflowCoachmark : UserControl
{
    public static readonly StyledProperty<CoachmarkPointer> PointerProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, CoachmarkPointer>(
            nameof(Pointer));

    public static readonly StyledProperty<CoachmarkPointerAlignment>
        PointerAlignmentProperty =
            AvaloniaProperty.Register<WorkflowCoachmark, CoachmarkPointerAlignment>(
                nameof(PointerAlignment));

    public static readonly StyledProperty<string> StepTextProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, string>(nameof(StepText));

    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, string>(nameof(Heading));

    public static readonly StyledProperty<string> BodyProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, string>(nameof(Body));

    public static readonly StyledProperty<string> PrimaryTextProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, string>(nameof(PrimaryText));

    public static readonly StyledProperty<ICommand?> PrimaryCommandProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, ICommand?>(nameof(PrimaryCommand));

    public static readonly StyledProperty<string> SecondaryTextProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, string>(nameof(SecondaryText));

    public static readonly StyledProperty<ICommand?> SecondaryCommandProperty =
        AvaloniaProperty.Register<WorkflowCoachmark, ICommand?>(nameof(SecondaryCommand));

    /// <summary>
    /// Which edge the trail leaves from, and therefore which neighbouring region
    /// the coachmark is pointing at. Purely presentational: the trail is anchored
    /// to the bubble rather than to the target's coordinates, so it cannot drift
    /// when the window resizes or a splitter moves.
    /// </summary>
    public CoachmarkPointer Pointer
    {
        get => GetValue(PointerProperty);
        set => SetValue(PointerProperty, value);
    }

    /// <summary>Position of the trail along its edge. Defaults to centred.</summary>
    public CoachmarkPointerAlignment PointerAlignment
    {
        get => GetValue(PointerAlignmentProperty);
        set => SetValue(PointerAlignmentProperty, value);
    }

    public string StepText
    {
        get => GetValue(StepTextProperty);
        set => SetValue(StepTextProperty, value);
    }

    public string Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public string PrimaryText
    {
        get => GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public ICommand? PrimaryCommand
    {
        get => GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    public string SecondaryText
    {
        get => GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public ICommand? SecondaryCommand
    {
        get => GetValue(SecondaryCommandProperty);
        set => SetValue(SecondaryCommandProperty, value);
    }

    public WorkflowCoachmark()
    {
        InitializeComponent();
        UpdatePointerPseudoClasses();
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointerProperty ||
            change.Property == PointerAlignmentProperty)
        {
            UpdatePointerPseudoClasses();
        }
    }

    private void UpdatePointerPseudoClasses()
    {
        PseudoClasses.Set(":pointer-up", Pointer == CoachmarkPointer.Up);
        PseudoClasses.Set(":pointer-down", Pointer == CoachmarkPointer.Down);
        PseudoClasses.Set(":pointer-left", Pointer == CoachmarkPointer.Left);
        PseudoClasses.Set(":pointer-right", Pointer == CoachmarkPointer.Right);
        PseudoClasses.Set(
            ":pointer-start",
            PointerAlignment == CoachmarkPointerAlignment.Start);
        PseudoClasses.Set(
            ":pointer-end",
            PointerAlignment == CoachmarkPointerAlignment.End);
    }
}
