using Avalonia.Controls;
using Avalonia.Data.Converters;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class DetailEditGroup : UserControl
{
    public static FuncValueConverter<FbddMode, string>
        NoiseReductionLabelConverter { get; } =
        new(value => value.ToString().ToUpperInvariant());

    public DetailEditGroup()
    {
        InitializeComponent();
    }
}
