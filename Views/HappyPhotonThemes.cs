using Avalonia.Styling;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public static class HappyPhotonThemes
{
    public static readonly ThemeVariant MidGrey = new("MidGrey", ThemeVariant.Dark);

    public static ThemeVariant For(AppTheme theme) => theme switch
    {
        AppTheme.MidGrey => MidGrey,
        _ => ThemeVariant.Dark
    };
}
