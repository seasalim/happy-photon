using Avalonia.Styling;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public static class HappyPhotonThemes
{
    public static readonly ThemeVariant MidGray = new("MidGray", ThemeVariant.Dark);

    public static ThemeVariant For(AppTheme theme) => theme switch
    {
        AppTheme.MidGray => MidGray,
        _ => ThemeVariant.Dark
    };
}
