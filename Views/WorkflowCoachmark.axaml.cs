using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

public partial class WorkflowCoachmark : UserControl
{
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
    }
}
