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
    private async Task SetColorLabelAsync(ColorLabel colorLabel)
    {
        if (IsFullScreenMode || !Enum.IsDefined(colorLabel)) return;

        var targets = ResolveAssessmentTargets().Targets;
        if (targets.Count == 0) return;
        var actedOnImage = targets.Count == 1 ? targets[0] : null;
        var previousColorLabel =
            actedOnImage?.ColorLabel ?? ColorLabel.None;
        var next = colorLabel != ColorLabel.None &&
                   targets.All(image => image.ColorLabel == colorLabel)
            ? ColorLabel.None
            : colorLabel;
        if (targets.All(image => image.ColorLabel == next))
        {
            if (actedOnImage != null)
            {
                ShowAssessmentFeedback(
                    actedOnImage,
                    DescribeColorFeedback(next, previousColorLabel));
            }
            return;
        }
        var selectedImage = SelectedImage;
        var replacement = selectedImage != null &&
                          targets.Contains(selectedImage) &&
                          !Browse.MatchesCurrentFilters(selectedImage, next)
            ? Browse.ReplacementAfterRemoval(selectedImage)
            : null;

        try
        {
            foreach (var target in targets)
            {
                await target.EnsureCatalogIdAsync(_catalogService);
            }

            await CommitAssessmentAsync(targets.Select(target =>
                new AssessmentMutation(
                    target.CatalogId, AssessmentAxes.Label,
                    ColorLabel: next)).ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Color label update failed: {ex.Message}");
            ShowTransientStatus("Unable to update color labels");
            return;
        }

        foreach (var target in targets)
        {
            target.ColorLabel = next;
        }

        Browse.RefreshFilters();
        if (!IsCompareMode && replacement != null && Browse.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }
        UpdateSelectedCount();
        if (targets.Count > 1)
        {
            ShowTransientStatus($"Labeled {targets.Count} photos");
        }
        else if (actedOnImage != null &&
                 ReferenceEquals(SelectedImage, actedOnImage))
        {
            ShowAssessmentFeedback(
                actedOnImage,
                DescribeColorFeedback(next, previousColorLabel));
        }
    }

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
