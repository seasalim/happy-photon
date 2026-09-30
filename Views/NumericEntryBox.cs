using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.Views;

// Avalonia checks bindings from the focused element outward before routed KeyDown.
// Shadow ancestor gestures only in this entry and send them through native text editing.
public sealed class NumericEntryBox : TextBox
{
    internal Action<Key>? Finish { get; set; }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        KeyBindings.Clear();
        var gestures = this.GetVisualAncestors().OfType<InputElement>()
            .SelectMany(element => element.KeyBindings).Select(binding => binding.Gesture)
            .OfType<KeyGesture>().Concat([new(Key.Enter), new(Key.Escape)]).Distinct();

        foreach (var gesture in gestures)
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = gesture,
                Command = new RelayCommand(() => OnKeyDown(new KeyEventArgs
                {
                    RoutedEvent = KeyDownEvent, Source = this,
                    Key = gesture.Key, KeyModifiers = gesture.KeyModifiers
                }))
            });
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) Finish?.Invoke(e.Key);
        else base.OnKeyDown(e);

        if (e.Key != Key.Tab) e.Handled = true;
    }
}
