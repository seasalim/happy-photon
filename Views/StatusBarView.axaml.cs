using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class StatusBarView : UserControl
{
    public StatusBarView()
    {
        InitializeComponent();
        LayoutUpdated += async (_, _) =>
        {
            if (StatusText.IsEffectivelyVisible && StatusText.Bounds.Width > 0 &&
                DataContext is MainWindowViewModel { IsNoticePending: true } vm)
            {
                var text = new FormattedText(StatusText.Text ?? "", CultureInfo.CurrentCulture,
                    StatusText.FlowDirection, new Typeface(StatusText.FontFamily, StatusText.FontStyle,
                        StatusText.FontWeight, StatusText.FontStretch), StatusText.FontSize, StatusText.Foreground);

                if (text.WidthIncludingTrailingWhitespace <= StatusText.Bounds.Width)
                {
                    await vm.AcknowledgeNoticeAsync(StatusText.Text);
                }
            }
        };
    }
}
