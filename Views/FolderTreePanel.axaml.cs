using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class FolderTreePanel : UserControl
{
    public static readonly StyledProperty<ObservableCollection<FolderNode>> RootFoldersProperty =
        AvaloniaProperty.Register<FolderTreePanel, ObservableCollection<FolderNode>>(nameof(RootFolders));

    public static readonly StyledProperty<FolderNode?> SelectedFolderProperty =
        AvaloniaProperty.Register<FolderTreePanel, FolderNode?>(nameof(SelectedFolder), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> BrowsingFolderNameProperty =
        AvaloniaProperty.Register<FolderTreePanel, string?>(nameof(BrowsingFolderName));

    public static readonly StyledProperty<string?> ViewingFolderNameProperty =
        AvaloniaProperty.Register<FolderTreePanel, string?>(nameof(ViewingFolderName));

    public static readonly StyledProperty<bool> CanRefreshFolderProperty =
        AvaloniaProperty.Register<FolderTreePanel, bool>(nameof(CanRefreshFolder));

    public ObservableCollection<FolderNode> RootFolders
    {
        get => GetValue(RootFoldersProperty);
        set => SetValue(RootFoldersProperty, value);
    }

    public FolderNode? SelectedFolder
    {
        get => GetValue(SelectedFolderProperty);
        set => SetValue(SelectedFolderProperty, value);
    }

    public string? BrowsingFolderName
    {
        get => GetValue(BrowsingFolderNameProperty);
        set => SetValue(BrowsingFolderNameProperty, value);
    }

    public string? ViewingFolderName
    {
        get => GetValue(ViewingFolderNameProperty);
        set => SetValue(ViewingFolderNameProperty, value);
    }

    public bool CanRefreshFolder
    {
        get => GetValue(CanRefreshFolderProperty);
        private set => SetValue(CanRefreshFolderProperty, value);
    }

    public event EventHandler<FolderNode>? FolderExpanding;
    public event EventHandler? ChangeFolderRequested;
    public event EventHandler? RefreshFolderRequested;
    public event EventHandler? ImportCatalogRequested;
    public event EventHandler? PhotoNavigationRequested;

    private bool _selectionStartedByPointer;

    public FolderTreePanel()
    {
        InitializeComponent();

        FolderTree.SelectionChanged += OnSelectionChanged;
        FolderTree.KeyDown += OnTreeKeyDown;
        FolderTree.AddHandler(
            InputElement.PointerPressedEvent,
            OnTreePointerPressed,
            RoutingStrategies.Tunnel);
    }

    public void FocusTree()
    {
        FolderTree.Focus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RootFoldersProperty)
        {
            FolderTree.ItemsSource = RootFolders;

            // Subscribe to expansion events on existing items
            if (RootFolders != null)
            {
                foreach (var node in RootFolders)
                {
                    SubscribeToExpansion(node);
                }
            }
        }

        else if (change.Property == SelectedFolderProperty)
        {
            CanRefreshFolder = change.NewValue is FolderNode { IsDummy: false };
        }
    }

    private readonly HashSet<FolderNode> _subscribedNodes = new();

    private void SubscribeToExpansion(FolderNode node)
    {
        // Avoid duplicate subscriptions
        if (_subscribedNodes.Contains(node))
            return;
        _subscribedNodes.Add(node);

        node.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FolderNode.IsExpanded) && node.IsExpanded)
            {
                FolderExpanding?.Invoke(this, node);

                // Subscribe to children's expansion events after they're loaded
                SubscribeToChildren(node);
            }
        };

        // Also subscribe to any existing non-dummy children
        SubscribeToChildren(node);
    }

    private void SubscribeToChildren(FolderNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (!child.IsDummy)
            {
                SubscribeToExpansion(child);
            }
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FolderTree.SelectedItem is FolderNode folder && !folder.IsDummy)
        {
            SelectedFolder = folder;
            if (_selectionStartedByPointer)
            {
                PhotoNavigationRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _selectionStartedByPointer = e.GetCurrentPoint(FolderTree)
            .Properties.IsLeftButtonPressed;
        Dispatcher.UIThread.Post(
            () => _selectionStartedByPointer = false,
            DispatcherPriority.Input);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            FolderTree.SelectedItem is not FolderNode { IsDummy: false })
        {
            return;
        }

        PhotoNavigationRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnChangeFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ChangeFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRefreshFolderClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnImportCatalogClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ImportCatalogRequested?.Invoke(this, EventArgs.Empty);
    }
}
