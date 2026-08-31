using Avalonia.Controls;
using Avalonia.Data.Converters;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class EffectsEditGroup : UserControl
{
    public static FuncValueConverter<GrainSize, string>
        GrainSizeLabelConverter { get; } =
        new(value => value switch
        {
            GrainSize.Medium => "Med",
            _ => value.ToString()
        });

    public EffectsEditGroup()
    {
        InitializeComponent();
    }
}
