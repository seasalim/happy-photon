using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Xunit;

namespace HappyPhoton.Tests;

internal static class NumericEntryKeyInput
{
    public static void Type(Window window, Key key, string symbol)
    {
        KeyEventArgs? down = null;
        using var subscription = InputElement.KeyDownEvent.RouteFinished.Subscribe(new AnonymousObserver<RoutedEventArgs>(input =>
        {
            if (input is KeyEventArgs { Route: RoutingStrategies.Bubble } args) down = args;
        }));
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, symbol);
        Assert.NotNull(down);

        // Headless does not generate text from KeyPress. Model Win32's WM_CHAR gate:
        // the completed key route must be unhandled to produce text, before key-up.
        if (!down.Handled) window.KeyTextInput(symbol);

        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, symbol);
        Assert.False(down.Handled, $"{key} suppressed platform text input");
    }

    public static Key KeyFor(char symbol) => symbol switch
    {
        >= '0' and <= '9' => Key.D0 + symbol - '0',
        '.' => Key.OemPeriod,
        '-' => Key.OemMinus,
        _ => Key.A + symbol - 'a'
    };
}
