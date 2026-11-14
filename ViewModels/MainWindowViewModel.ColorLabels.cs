using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyDictionary<ColorLabel, string> _colorLabelNames =
        Services.ColorLabelNames.Defaults;

    public IReadOnlyList<ColorLabelChoice> ColorLabelChoices =>
        Enum.GetValues<ColorLabel>()
            .Where(label => label != ColorLabel.None)
            .Select(label =>
            {
                var name = _colorLabelNames.GetValueOrDefault(
                    label,
                    label.ToString());
                return new ColorLabelChoice(label, name);
            })
            .ToArray();

    public IReadOnlyList<ColorLabelFilterChoice> ColorLabelFilterChoices =>
        Enum.GetValues<ColorLabelFilter>()
            .Where(filter => filter != ColorLabelFilter.All)
            .Select(filter =>
            {
                if (filter == ColorLabelFilter.None)
                    return new ColorLabelFilterChoice(filter, filter.ToString());
                var label = (ColorLabel)((int)filter - 1);
                return new ColorLabelFilterChoice(
                    filter,
                    _colorLabelNames.GetValueOrDefault(label, label.ToString()));
            })
            .ToArray();

    internal void SetColorLabelNames(
        IReadOnlyDictionary<ColorLabel, string> names)
    {
        _colorLabelNames = new Dictionary<ColorLabel, string>(names);
        OnPropertyChanged(nameof(ColorLabelChoices));
        OnPropertyChanged(nameof(ColorLabelFilterChoices));
    }

    [RelayCommand]
    private Task SetColorLabelAsync(ColorLabel colorLabel) =>
        Enum.IsDefined(colorLabel)
            ? SetAssessmentAsync(ColorLabelAxis, colorLabel, toggles: true)
            : Task.CompletedTask;

    private string DescribeColorFeedback(
        ColorLabel next,
        ColorLabel previous) =>
        next != ColorLabel.None
            ? $"Set color: {ColorLabelName(next)}"
            : previous != ColorLabel.None
                ? $"Unset color: {ColorLabelName(previous)}"
                : "Unset color";

    private string ColorLabelName(ColorLabel label) =>
        _colorLabelNames.GetValueOrDefault(label, label.ToString());
}
