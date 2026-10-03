using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class TipsCard : UserControl
{
    public static readonly StyledProperty<TipsContent?> TipsProperty =
        AvaloniaProperty.Register<TipsCard, TipsContent?>(nameof(Tips));

    public TipsContent? Tips
    {
        get => GetValue(TipsProperty);
        set => SetValue(TipsProperty, value);
    }

    public TipsCard() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TipsProperty || CopyLines == null) return;

        CopyLines.Children.Clear();
        if (Tips == null) return;

        foreach (var line in Tips.Lines)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FontSizeBody"));
            text.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontBody"));
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextSecondary"));
            var segments = line.Split('`');

            for (var index = 0; index < segments.Length; index++)
            {
                var run = new Run(segments[index]);

                if (index % 2 == 1)
                {
                    run.Bind(TextElement.FontFamilyProperty, this.GetResourceObservable("FontLabel"));
                }

                text.Inlines!.Add(run);
            }

            CopyLines.Children.Add(text);
        }
    }
}
