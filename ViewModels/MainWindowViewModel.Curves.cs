using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private CurveData? _currentCurve;

    [ObservableProperty]
    private ToneCurveChannel _activeCurveChannel;

    private EditSettings? _curveGestureStartState;
    private ToneCurveChannel _curveGestureChannel;

    public CurveData? CompositeCurve => SelectedImage?.EditSettings.Curve;
    public bool HasRedCurve => SelectedImage?.EditSettings.CurveRed != null;
    public bool HasGreenCurve => SelectedImage?.EditSettings.CurveGreen != null;
    public bool HasBlueCurve => SelectedImage?.EditSettings.CurveBlue != null;

    partial void OnActiveCurveChannelChanged(ToneCurveChannel value)
    {
        if (IsMonochromeSource && value != ToneCurveChannel.Composite)
        {
            ActiveCurveChannel = ToneCurveChannel.Composite;
            return;
        }
        LoadCurrentCurveFrom(SelectedImage?.EditSettings);
    }

    public void OnCurveEditStarted()
    {
        if (!CanEditSelectedImage || SelectedImage == null ||
            IsMonochromeSource && ActiveCurveChannel != ToneCurveChannel.Composite)
        {
            return;
        }

        _curveGestureStartState = CaptureLiveEditState();
        _curveGestureChannel = ActiveCurveChannel;
    }

    public Task OnCurveChangedAsync()
    {
        var commit = CommitCurveChangeAsync();
        TrackHistoryCommit(commit);
        return commit;
    }

    private async Task CommitCurveChangeAsync()
    {
        if (!CanEditSelectedImage ||
            SelectedImage == null ||
            CurrentCurve == null ||
            IsMonochromeSource && ActiveCurveChannel != ToneCurveChannel.Composite)
        {
            _curveGestureStartState = null;
            return;
        }

        if (_curveGestureStartState != null &&
            _curveGestureChannel != ActiveCurveChannel)
        {
            _curveGestureStartState = null;
            LoadCurrentCurveFrom(SelectedImage.EditSettings);
            return;
        }

        CurrentCurve.BuildLookupTable();
        var before = _curveGestureStartState ??
            _lastSavedState?.Clone() ??
            SelectedImage.EditSettings.Clone();
        _curveGestureStartState = null;

        MaterializeActiveCurve(SelectedImage.EditSettings);
        var after = CaptureLiveEditState();
        NotifyCurveStateChanged();
        UpdateCanReset();
        if (before.HasSameEdits(after))
        {
            return;
        }

        var surfaceGeneration = RequestEditedRender();
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;
        if (await UpdatePreviewWithCurrentSliders(generation: surfaceGeneration))
        {
            await AutoSaveAsync("Curve", before);
        }
    }

    private void MaterializeActiveCurve(EditSettings target)
    {
        switch (ActiveCurveChannel)
        {
            case ToneCurveChannel.Composite:
                target.Curve = CurrentCurve?.Clone() ?? new CurveData();
                break;
            case ToneCurveChannel.Red:
                target.CurveRed = MaterializeChannelCurve();
                break;
            case ToneCurveChannel.Green:
                target.CurveGreen = MaterializeChannelCurve();
                break;
            case ToneCurveChannel.Blue:
                target.CurveBlue = MaterializeChannelCurve();
                break;
        }
    }

    private CurveData? MaterializeChannelCurve() =>
        CurrentCurve == null || CurrentCurve.IsIdentity()
            ? null
            : CurrentCurve.Clone();

    private void LoadCurrentCurveFrom(EditSettings? source)
    {
        CurrentCurve = source == null
            ? (SelectedImage == null ? null : new CurveData())
            : GetCurve(source, ActiveCurveChannel)?.Clone() ?? new CurveData();
        NotifyCurveStateChanged();
    }

    private static CurveData? GetCurve(
        EditSettings source,
        ToneCurveChannel channel) => channel switch
    {
        ToneCurveChannel.Composite => source.Curve,
        ToneCurveChannel.Red => source.CurveRed,
        ToneCurveChannel.Green => source.CurveGreen,
        ToneCurveChannel.Blue => source.CurveBlue,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    private void NotifyCurveStateChanged()
    {
        OnPropertyChanged(nameof(CompositeCurve));
        OnPropertyChanged(nameof(HasRedCurve));
        OnPropertyChanged(nameof(HasGreenCurve));
        OnPropertyChanged(nameof(HasBlueCurve));
    }

    private void ClearCurveGesture() => _curveGestureStartState = null;
}
