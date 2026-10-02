using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HappyPhoton.Views;

// Menu opening and arrow navigation can assign focus without a keyboard method.
// Project the same shared adorner after navigation, without changing selection.
public sealed class MenuKeyboardFocus : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MenuKeyboardFocus, Control, bool>("Enabled");

    private static readonly AttachedProperty<Tracker?> TrackerProperty =
        AvaloniaProperty.RegisterAttached<MenuKeyboardFocus, Control, Tracker?>("Tracker");

    static MenuKeyboardFocus()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            control.GetValue(TrackerProperty)?.Dispose();
            control.SetValue(TrackerProperty, change.NewValue is true ? new Tracker(control) : null);
        });
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private sealed class Tracker : IDisposable
    {
        private readonly Control _owner;
        private Control? _outline;
        private AdornerLayer? _layer;
        private bool _keyboard;

        public Tracker(Control owner)
        {
            _owner = owner;
            owner.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            owner.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            owner.AddHandler(InputElement.GettingFocusEvent, OnGettingFocus);
            owner.AddHandler(InputElement.LostFocusEvent, OnLostFocus);
            owner.DetachedFromVisualTree += OnDetached;
        }

        public void Dispose()
        {
            _keyboard = false;
            Clear();
            _owner.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _owner.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            _owner.RemoveHandler(InputElement.GettingFocusEvent, OnGettingFocus);
            _owner.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
            _owner.DetachedFromVisualTree -= OnDetached;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            _keyboard = true;
            QueueOutline();
        }

        private void OnGettingFocus(object? sender, FocusChangingEventArgs e)
        {
            if (e.OldFocusedElement is Control previous && previous.Classes.Contains(":focus-visible"))
            {
                _keyboard = true;
            }

            QueueOutline();
        }

        private void QueueOutline()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_keyboard || TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement() is not MenuItem item)
                    return;

                Clear();
                if (item.Classes.Contains(":focus-visible")) return;

                _layer = AdornerLayer.GetAdornerLayer(item);
                _outline = item.FocusAdorner?.Build();
                if (_layer is null || _outline is null) return;

                AdornerLayer.SetAdornedElement(_outline, item);
                _layer.Children.Add(_outline);
            }, DispatcherPriority.Input);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            _keyboard = false;
            Clear();
        }

        private void OnLostFocus(object? sender, RoutedEventArgs e) => Clear();

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _keyboard = false;
            Clear();
        }

        private void Clear()
        {
            if (_outline is not null) _layer?.Children.Remove(_outline);

            _outline = null;
            _layer = null;
        }
    }
}
