using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private long _cropSessionIdentity;

    private CancellationTokenSource? _autoStraightenCancellation;

    private Task _autoStraightenTask = Task.CompletedTask;

    internal Func<Task>? AutoStraightenGateAsync { get; set; }

    private bool CanAutoStraighten() =>
        !_renderOutcomeChannelClosed && IsCropMode && CanEditSelectedImage &&
        _autoStraightenCancellation == null;

    [RelayCommand(CanExecute = nameof(CanAutoStraighten))]
    private async Task AutoStraightenAsync()
    {
        if (!CanAutoStraighten()) return;

        using var cancellation = new CancellationTokenSource();
        _autoStraightenCancellation = cancellation;
        AutoStraightenCommand.NotifyCanExecuteChanged();

        try
        {
            _autoStraightenTask = DetectHorizonAsync(cancellation.Token);
            await _autoStraightenTask;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _autoStraightenCancellation = null;
            AutoStraightenCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DetectHorizonAsync(CancellationToken cancellationToken)
    {
        var image = SelectedImage!;
        var session = _cropSessionIdentity;
        var horizon = HorizonRotation;
        var settings = CaptureLiveEditState();
        bool IsCurrent() => !_renderOutcomeChannelClosed && IsCropMode && CanEditSelectedImage &&
            ReferenceEquals(image, SelectedImage) && session == _cropSessionIdentity &&
            horizon == HorizonRotation;

        var sample = await ImageService.Previews.SamplePreviewBaseAsync(
            image, settings, HorizonDetection.Detect, cancellationToken, AutoStraightenGateAsync);
        if (sample == null || !IsCurrent()) return;

        if (!await ImageService.Previews.IsPreviewBaseCurrentAsync(
                image, CaptureLiveEditState(), sample.BaseToken, cancellationToken))
        {
            return;
        }

        if (!IsCurrent()) return;

        var angle = Math.Clamp(Math.Round(sample.Value.HorizonRotation, 2), -5, 5);

        if (angle == HorizonRotation)
        {
            ShowTransientStatus("Already level");
            return;
        }

        HorizonRotation = angle;
    }

    private async Task CancelAndDrainAutoStraightenAsync()
    {
        _autoStraightenCancellation?.Cancel();

        try
        {
            await _autoStraightenTask;
        }
        catch (OperationCanceledException) { }
    }
}
