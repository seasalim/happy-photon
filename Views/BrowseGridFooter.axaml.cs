using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class BrowseGridFooter : UserControl
{
    private static readonly string[] ControlNames =
    [
        "ThumbnailSizePanel",
        "CompareViewButton",
        "BurstsButton",
        "PairsButton",
        "SmallThumbnailButton",
        "MediumThumbnailButton",
        "LargeThumbnailButton",
        "ImageAssessment",
        "OnlineOnlyMessage"
    ];

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
        BurstsButton.Classes.Set("active", ShowBursts);

    private void OnBurstsClick(object? sender, RoutedEventArgs e) =>
        ShowBursts = !ShowBursts;

    private void UpdatePairsButton() =>
        PairsButton.Classes.Set("active", ShowPairs);

    private void OnPairsClick(object? sender, RoutedEventArgs e) =>
        ShowPairs = !ShowPairs;

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

    internal INameScope MergeWith(
        INameScope parentNameScope,
        IEnumerable<string> parentControlNames)
    {
        var merged = new NameScope();
        RegisterNames(merged, parentNameScope, parentControlNames);
        RegisterNames(merged, NameScope.GetNameScope(this)!, ControlNames);
        merged.Complete();
        return merged;
    }

    private static void RegisterNames(
        INameScope target,
        INameScope source,
        IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (source.Find(name) is { } control)
            {
                target.Register(name, control);
            }
        }
    }
}
