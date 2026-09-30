using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class PresetsPanel : UserControl
{
    public static readonly StyledProperty<string?> ActivePresetIdProperty =
        AvaloniaProperty.Register<PresetsPanel, string?>(nameof(ActivePresetId));

    public static readonly StyledProperty<bool> CanSavePresetProperty =
        AvaloniaProperty.Register<PresetsPanel, bool>(nameof(CanSavePreset));

    public static readonly StyledProperty<bool> ShowBuiltInsProperty =
        AvaloniaProperty.Register<PresetsPanel, bool>(nameof(ShowBuiltIns));

    public static readonly StyledProperty<IReadOnlyDictionary<string, bool>?> PresetGroupsProperty =
        AvaloniaProperty.Register<PresetsPanel, IReadOnlyDictionary<string, bool>?>(nameof(PresetGroups));

    public bool ShowBuiltIns
    {
        get => GetValue(ShowBuiltInsProperty);
        set => SetValue(ShowBuiltInsProperty, value);
    }

    public IReadOnlyDictionary<string, bool>? PresetGroups
    {
        get => GetValue(PresetGroupsProperty);
        set => SetValue(PresetGroupsProperty, value);
    }

    private readonly Dictionary<string, Button> _presetButtons = new();
    private readonly HashSet<string> _userPresetIds = new();
    private PresetService? _presetSource;
    private StackPanel? _userPresetPanel;
    private Button? _savePresetButton;
    private bool _isSubscribed;

    public PresetsPanel()
    {
        InitializeComponent();
        BuildPresetUI();
        AttachedToVisualTree += (_, _) => SubscribeToPresetSource();
        DetachedFromVisualTree += (_, _) => UnsubscribeFromPresetSource();
    }

    public string? ActivePresetId
    {
        get => GetValue(ActivePresetIdProperty);
        set => SetValue(ActivePresetIdProperty, value);
    }

    public bool CanSavePreset
    {
        get => GetValue(CanSavePresetProperty);
        set => SetValue(CanSavePresetProperty, value);
    }

    public event EventHandler<string>? PresetClicked;
    public event EventHandler<string>? PresetHoverEnter;
    public event EventHandler<string>? PresetHoverLeave;
    public event EventHandler? SavePresetRequested;
    public event EventHandler<string>? RenamePresetRequested;
    public event EventHandler<string>? DeletePresetRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowBuiltInsProperty || change.Property == PresetGroupsProperty)
        {
            BuildPresetUI();
        }
        else if (change.Property == ActivePresetIdProperty)
        {
            UpdateActiveState();
        }
        else if (change.Property == CanSavePresetProperty && _savePresetButton != null)
        {
            _savePresetButton.IsEnabled = CanSavePreset;
        }
    }

    public void SetPresetSource(PresetService? presetSource)
    {
        if (!ReferenceEquals(_presetSource, presetSource))
        {
            UnsubscribeFromPresetSource();
            _presetSource = presetSource;
        }

        SubscribeToPresetSource();
        BuildPresetUI();
    }

    private void BuildPresetUI()
    {
        var container = this.FindControl<StackPanel>("PresetsContainer");
        if (container == null)
        {
            return;
        }

        container.Children.Clear();
        _presetButtons.Clear();
        var userExpander = CreateExpander("My Presets");
        _userPresetPanel = CreateButtonPanel();
        userExpander.Content = _userPresetPanel;
        container.Children.Add(userExpander);

        if (ShowBuiltIns && _presetSource != null)
        {
            foreach (var group in _presetSource.BuiltInPresets.GroupBy(preset => preset.Group!))
            {
                var panel = CreateButtonPanel();

                foreach (var preset in group)
                {
                    var button = CreatePresetButton(preset);
                    ToolTip.SetTip(button, preset.Description);
                    panel.Children.Add(button);
                }

                var expander = CreateExpander(group.Key);
                expander.Content = panel;
                container.Children.Add(expander);
            }
        }

        RebuildUserPresets();
    }

    private void RebuildUserPresets()
    {
        if (_userPresetPanel == null)
        {
            return;
        }

        foreach (var id in _userPresetIds)
        {
            _presetButtons.Remove(id);
        }

        _userPresetIds.Clear();
        _userPresetPanel.Children.Clear();

        _savePresetButton = new Button
        {
            Content = "＋ Save Current…",
            IsEnabled = CanSavePreset,
            Classes = { "preset" }
        };
        _savePresetButton.Click += (_, _) => SavePresetRequested?.Invoke(this, EventArgs.Empty);
        _userPresetPanel.Children.Add(_savePresetButton);

        foreach (var preset in _presetSource?.UserPresets ?? Array.Empty<Preset>())
        {
            var button = CreatePresetButton(preset);
            button.ContextMenu = CreateUserPresetContextMenu(preset.Id);
            _userPresetPanel.Children.Add(button);
            _userPresetIds.Add(preset.Id);
        }

        UpdateActiveState();
    }

    private Button CreatePresetButton(Preset preset)
    {
        var button = new Button
        {
            Content = preset.Name,
            Tag = preset.Id,
            Classes = { "preset" }
        };

        button.Click += OnPresetButtonClick;
        button.PointerEntered += OnPresetButtonPointerEntered;
        button.PointerExited += OnPresetButtonPointerExited;
        _presetButtons[preset.Id] = button;

        return button;
    }

    private ContextMenu CreateUserPresetContextMenu(string presetId)
    {
        var renameItem = new MenuItem { Header = "Rename…" };
        renameItem.Click += (_, _) => RenamePresetRequested?.Invoke(this, presetId);
        var deleteItem = new MenuItem { Header = "Delete…" };
        deleteItem.Click += (_, _) => DeletePresetRequested?.Invoke(this, presetId);

        return new ContextMenu
        {
            ItemsSource = new[] { renameItem, deleteItem }
        };
    }

    private Expander CreateExpander(string name)
    {
        var expander = new Expander
        {
            Classes = { "compact-chevron" },
            Header = new TextBlock
            {
                Text = name,
                Classes = { "preset-header" }
            },
            IsExpanded = PresetGroups?.GetValueOrDefault(name, true) ?? true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        expander.PropertyChanged += async (_, change) =>
        {
            if (change.Property == Expander.IsExpandedProperty && DataContext is MainWindowViewModel vm)
            {
                await vm.SetPresetGroupExpandedAsync(name, expander.IsExpanded);
            }
        };

        return expander;
    }

    private static StackPanel CreateButtonPanel()
    {
        return new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private void SubscribeToPresetSource()
    {
        if (_presetSource == null || _isSubscribed)
        {
            return;
        }

        _presetSource.PresetsChanged += OnPresetsChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeFromPresetSource()
    {
        if (_presetSource == null || !_isSubscribed)
        {
            return;
        }

        _presetSource.PresetsChanged -= OnPresetsChanged;
        _isSubscribed = false;
    }

    private void OnPresetsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildUserPresets();
        }
        else
        {
            Dispatcher.UIThread.Post(RebuildUserPresets);
        }
    }

    private void OnPresetButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string presetId })
        {
            PresetClicked?.Invoke(this, presetId);
        }
    }

    private void OnPresetButtonPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is Button { Tag: string presetId })
        {
            PresetHoverEnter?.Invoke(this, presetId);
        }
    }

    private void OnPresetButtonPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is Button { Tag: string presetId })
        {
            PresetHoverLeave?.Invoke(this, presetId);
        }
    }

    private void UpdateActiveState()
    {
        foreach (var (id, button) in _presetButtons)
        {
            var isActive = id == ActivePresetId;
            if (isActive && !button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }
            else if (!isActive && button.Classes.Contains("active"))
            {
                button.Classes.Remove("active");
            }
        }
    }
}
