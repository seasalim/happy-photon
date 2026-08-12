using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HappyPhoton.Views;

public partial class LibraryRatingFilter : UserControl
{
    public static readonly StyledProperty<int> MinimumRatingProperty =
        AvaloniaProperty.Register<LibraryRatingFilter, int>(
            nameof(MinimumRating),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public int MinimumRating
    {
        get => GetValue(MinimumRatingProperty);
        set => SetValue(MinimumRatingProperty, value);
    }

    public LibraryRatingFilter()
    {
        InitializeComponent();
        UpdateStars();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MinimumRatingProperty)
        {
            UpdateStars();
        }
    }

    private void OnStarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !int.TryParse(button.Tag?.ToString(), out var requested)) return;
        MinimumRating = MinimumRating == requested ? 0 : requested;
    }

    private void UpdateStars()
    {
        RatingFilter1Filled.IsVisible = MinimumRating >= 1;
        RatingFilter2Filled.IsVisible = MinimumRating >= 2;
        RatingFilter3Filled.IsVisible = MinimumRating >= 3;
        RatingFilter4Filled.IsVisible = MinimumRating >= 4;
        RatingFilter5Filled.IsVisible = MinimumRating >= 5;
    }
}
