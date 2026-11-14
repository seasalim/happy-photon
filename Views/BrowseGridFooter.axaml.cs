using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class BrowseGridFooter : UserControl
{
    public static readonly StyledProperty<bool> ShowBurstsProperty =
        AvaloniaProperty.Register<BrowseGridFooter, bool>(
            nameof(ShowBursts),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowPairsProperty =
        AvaloniaProperty.Register<BrowseGridFooter, bool>(
            nameof(ShowPairs),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<BrowseThumbnailSize> ThumbnailSizeProperty =
        AvaloniaProperty.Register<BrowseGridFooter, BrowseThumbnailSize>(
            nameof(ThumbnailSize),
            BrowseThumbnailSize.Medium,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool ShowBursts
    {
        get => GetValue(ShowBurstsProperty);
        set => SetValue(ShowBurstsProperty, value);
    }

    public bool ShowPairs
    {
        get => GetValue(ShowPairsProperty);
        set => SetValue(ShowPairsProperty, value);
    }

    public BrowseThumbnailSize ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public BrowseGridFooter()
    {
        InitializeComponent();
        UpdateBurstsButton();
        UpdatePairsButton();
        UpdateThumbnailSizeButtons();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowBurstsProperty)
        {
            UpdateBurstsButton();
        }
        else if (change.Property == ShowPairsProperty)
        {
            UpdatePairsButton();
        }
        else if (change.Property == ThumbnailSizeProperty)
        {
            UpdateThumbnailSizeButtons();
        }
    }

    private void UpdateBurstsButton() =>
        BurstsButton.IsChecked = ShowBursts;

    private void OnBurstsClick(object? sender, RoutedEventArgs e) =>
        ShowBursts = !ShowBursts;

    private void UpdatePairsButton() =>
        PairsButton.IsChecked = ShowPairs;

    private void OnPairsClick(object? sender, RoutedEventArgs e) =>
        ShowPairs = !ShowPairs;

    private void OnNavigationClick(object? sender, RoutedEventArgs e)
    {
        // Button executes its bound command after routing Click.
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
            {
                window.FollowBrowseNavigation();
            }
        });
    }

    private void UpdateThumbnailSizeButtons()
    {
        SmallThumbnailButton.IsChecked = ThumbnailSize == BrowseThumbnailSize.Small;
        MediumThumbnailButton.IsChecked = ThumbnailSize == BrowseThumbnailSize.Medium;
        LargeThumbnailButton.IsChecked = ThumbnailSize == BrowseThumbnailSize.Large;
    }

    private void OnSmallThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = BrowseThumbnailSize.Small;

    private void OnMediumThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = BrowseThumbnailSize.Medium;

    private void OnLargeThumbnailClick(object? sender, RoutedEventArgs e) =>
        ThumbnailSize = BrowseThumbnailSize.Large;
}
