using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _assessmentFeedbackCts;

    [ObservableProperty]
    private string? _assessmentFeedback;

    [ObservableProperty]
    private bool _isAssessmentFeedbackVisible;

    private void ShowAssessmentFeedback(ImageFile image, string text)
    {
        if (!ReferenceEquals(SelectedImage, image)) return;

        AssessmentFeedback = text;
        IsAssessmentFeedbackVisible = true;
        CullPerf?.Record("Feedback", image.CatalogId);
        var debounce = ReplaceDebounce(ref _assessmentFeedbackCts);
        _ = DebouncedAction.RunAsync(
            "assessment feedback",
            TimeSpan.FromSeconds(1.5),
            debounce.Token,
            async () =>
            {
                if (!ReferenceEquals(SelectedImage, image)) return;

                IsAssessmentFeedbackVisible = false;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(160),
                    _timeProvider,
                    debounce.Token);
                if (ReferenceEquals(SelectedImage, image))
                {
                    AssessmentFeedback = null;
                }
            },
            timeProvider: _timeProvider);
    }

    private void ClearAssessmentFeedback()
    {
        CancelAndDispose(ref _assessmentFeedbackCts);
        IsAssessmentFeedbackVisible = false;
        AssessmentFeedback = null;
    }

    partial void OnSelectedImageChanging(ImageFile? oldValue, ImageFile? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
        {
            SliderEditsEnding?.Invoke();
            DiscardGestures();
            ClearAssessmentFeedback();
            ClearAlignmentGrid();
        }
    }

    partial void OnIsFullScreenModeChanging(bool value)
    {
        DiscardGestures();
        if (value)
        {
            ClearAssessmentFeedback();
        }
    }
}
