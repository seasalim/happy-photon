using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace HappyPhoton.Views;

public partial class EditHistoryPanel : UserControl
{
    public EditHistoryPanel() => InitializeComponent();
}

public static class EditHistoryLayoutConverters
{
    public static IValueConverter FortyPercent { get; } =
        new FuncValueConverter<double, double>(value => value * 0.4);
}
