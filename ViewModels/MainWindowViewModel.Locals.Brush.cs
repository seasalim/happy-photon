using Avalonia;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly AppSettings _brushPreferences = new();
    private CancellationTokenSource? _brushPreviewDelay;
    private CancellationTokenSource? _brushPreviewRender;
    private CancellationTokenSource? _brushSaveDebounce;
    private bool _brushPreviewDirty;
    private long? _brushPreviewTimestamp;
    private int _brushRemainingPoints;
    public bool IsBrushCreationArmed => IsLocalCreationArmed && _localCreationType == "brush";
    public bool IsBrushSectionVisible => IsBrushCreationArmed || !IsLocalCreationArmed && SelectedLocal?.IsBrush == true;
    public bool IsGradientSectionVisible => !IsBrushSectionVisible;
    public bool IsGradientGeometryExpanded => IsGradientSectionVisible && IsLocalGeometryExpanded;
    public bool IsBrushStrokeActive => IsLocalsGestureActive && _localsGestureLocal?.IsBrush == true;
    public LocalBrushStroke? LiveBrushStroke => IsBrushStrokeActive && SelectedLocal?.Strokes is { Count: > 0 } strokes
        ? strokes[^1] : null;
    public bool IsNeutralBrush => SelectedLocal is { IsBrush: true, Exposure: 0, Temperature: 0, Tint: 0, Saturation: 0 };
    public double BrushRadius => .001 * Math.Pow(250, (BrushSize - 1) / 99);
    public bool IsBrushPaint => BrushMode == "paint";
    public bool IsBrushErase => BrushMode == "erase";
    public string BrushMode
    {
        get => _brushPreferences.BrushMode;
        set { if (value is "paint" or "erase" && value != BrushMode) { _brushPreferences.BrushMode = value; BrushPreferenceChanged(); } }
    }
    public double BrushSize { get => _brushPreferences.BrushSize; set => SetBrushPreference(value, 1, 100, 0); }
    public double BrushFeather { get => _brushPreferences.BrushFeather; set => SetBrushPreference(value, 0, 100, 1); }
    public double BrushFlow { get => _brushPreferences.BrushFlow; set => SetBrushPreference(value, 5, 100, 2); }
    private string BrushLimitInstruction => Locals.Sum(l => l.Strokes?.Sum(s => s.Points.Count) ?? 0) >= LocalBrushStroke.MaximumPoints
        ? "4,000 brush points — clear strokes to paint more"
        : Locals.Sum(l => l.Strokes?.Count ?? 0) >= LocalBrushStroke.MaximumStrokes
            ? "96 brush strokes — clear strokes to paint more" : "";

    public void RestoreBrushPreferences(AppSettings settings)
    {
        _brushPreferences.BrushMode = settings.BrushMode == "erase" ? "erase" : "paint";
        _brushPreferences.BrushSize = FinitePreference(settings.BrushSize, 1, 100, 65);
        _brushPreferences.BrushFeather = FinitePreference(settings.BrushFeather, 0, 100, 50);
        _brushPreferences.BrushFlow = FinitePreference(settings.BrushFlow, 5, 100, 100);
        NotifyBrushState();
    }
    public void CaptureBrushPreferences(AppSettings settings)
    {
        settings.BrushMode = BrushMode; settings.BrushSize = BrushSize;
        settings.BrushFeather = BrushFeather; settings.BrushFlow = BrushFlow;
    }
    private static double FinitePreference(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private void SetBrushPreference(double value, double min, double max, int field)
    {
        if (!double.IsFinite(value)) return;
        value = Math.Clamp(value, min, max);
        if (field == 0) _brushPreferences.BrushSize = value;
        else if (field == 1) _brushPreferences.BrushFeather = value;
        else _brushPreferences.BrushFlow = value;
        BrushPreferenceChanged();
    }
    private void BrushPreferenceChanged()
    {
        NotifyBrushState();
        var token = ReplaceDebounce(ref _brushSaveDebounce).Token;
        _ = DebouncedAction.RunAsync("brush preferences", TimeSpan.FromMilliseconds(250), token,
            () =>
            {
                token.ThrowIfCancellationRequested();
                return PersistAppSettingsAsync?.Invoke() ?? Task.CompletedTask;
            }, onError: (_, _) => ShowTransientStatus("Unable to save brush preferences"), timeProvider: _timeProvider);
    }
    private void NotifyBrushState()
    {
        foreach (var name in new[] { nameof(IsBrushCreationArmed), nameof(IsBrushSectionVisible),
            nameof(IsGradientSectionVisible), nameof(IsGradientGeometryExpanded), nameof(IsBrushStrokeActive),
            nameof(BrushMode), nameof(IsBrushPaint), nameof(IsBrushErase), nameof(BrushSize),
            nameof(BrushRadius), nameof(BrushFeather), nameof(BrushFlow), nameof(LiveBrushStroke) }) OnPropertyChanged(name);
        AddBrushCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsLocalGeometryExpandedChanged(bool value) => OnPropertyChanged(nameof(IsGradientGeometryExpanded));

    [RelayCommand(CanExecute = nameof(CanAddLocal))]
    private void AddBrush()
    {
        DiscardLocalsGesture();
        _localCreationType = "brush";
        IsLocalCreationArmed = true;
    }
    [RelayCommand]
    private void SetBrushMode(string mode)
    {
        BrushMode = mode;
        OnPropertyChanged(nameof(IsBrushPaint));
        OnPropertyChanged(nameof(IsBrushErase));
    }
    [RelayCommand]
    private Task ClearBrushStrokesAsync() => ChangeLocalAsync("Clear strokes", () =>
    {
        if (SelectedLocal is { IsBrush: true } local) local.Strokes = [];
    });

    public bool BeginBrushStroke(Point point, bool straight = false)
    {
        if (!CanEditLocals || !IsBrushSectionVisible || IsLocalsGestureActive ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y) || LocalsFrame is not { } frame ||
            IsBrushCreationArmed && !CanAddLocal || BrushLimitInstruction.Length > 0) return false;
        CancelLocalHuePick();
        _previewDebounce?.Cancel();
        _localsGestureBefore = CaptureLiveEditState();
        _localsGestureImage = SelectedImage;
        _localsGestureFrame = frame;
        _localsHandle = IsBrushCreationArmed ? LocalHandle.Create : LocalHandle.Center;
        var local = IsBrushCreationArmed ? NewLocal() : SelectedLocal!;
        _localsGestureLocal = local with { };
        _brushRemainingPoints = LocalBrushStroke.MaximumPoints - Locals.Sum(l => l.Strokes?.Sum(s => s.Points.Count) ?? 0);
        var start = straight && local.Strokes is { Count: > 0 } strokes ? strokes[^1].Points[^1] : QuantizeBrushPoint(point);
        var stroke = new LocalBrushStroke { Mode = BrushMode, Radius = BrushRadius,
            Feather = BrushFeather / 100, Flow = BrushFlow / 100, Points = [start] };
        local.Strokes = [.. local.Strokes ?? [], stroke];
        _brushRemainingPoints--;
        if (IsBrushCreationArmed) (SelectedImage!.EditSettings.Locals ??= []).Add(local);
        _selectedLocalId = local.Id;
        NotifyLocalsState();
        if (!straight || !ExtendBrushStroke(point, 1, final: true)) ScheduleBrushPreview();
        return true;
    }
    private static LocalBrushPoint QuantizeBrushPoint(Point point) => new(
        (int)Math.Round(Math.Clamp(point.X, -1, 2) * LocalBrushPoint.Scale),
        (int)Math.Round(Math.Clamp(point.Y, -1, 2) * LocalBrushPoint.Scale));

    public bool ExtendBrushStroke(Point point, double screenLongEdge, bool final = false)
    {
        if (!IsBrushStrokeActive || !ReferenceEquals(SelectedImage, _localsGestureImage) ||
            LiveBrushStroke is not { } stroke || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            !double.IsFinite(screenLongEdge) || screenLongEdge <= 0 || _brushRemainingPoints <= 0) return false;
        var next = QuantizeBrushPoint(point);
        var last = stroke.Points[^1];
        var dx = (next.U - last.U) / (double)LocalBrushPoint.Scale * _localsGestureFrame.Width / _localsGestureFrame.LongEdge;
        var dy = (next.V - last.V) / (double)LocalBrushPoint.Scale * _localsGestureFrame.Height / _localsGestureFrame.LongEdge;
        if (next == last || !final && Math.Sqrt(dx * dx + dy * dy) < Math.Max(.15 * stroke.Radius, 1 / screenLongEdge)) return false;
        SelectedLocal!.Strokes = [.. SelectedLocal.Strokes!.Take(SelectedLocal.Strokes!.Count - 1),
            stroke with { Points = [.. stroke.Points, next] }];
        _brushRemainingPoints--;
        OnPropertyChanged(nameof(LiveBrushStroke));
        if (_brushRemainingPoints == 0) OnPropertyChanged(nameof(LocalsInstruction));
        ScheduleBrushPreview();
        return true;
    }
    private void ScheduleBrushPreview()
    {
        _brushPreviewDirty = true;
        if (_brushPreviewDelay != null || _brushPreviewRender != null) return;
        var remaining = _brushPreviewTimestamp is { } last
            ? TimeSpan.FromMilliseconds(60) - _timeProvider.GetElapsedTime(last) : TimeSpan.Zero;
        if (remaining <= TimeSpan.Zero) { DispatchBrushPreview(); return; }
        var delay = _brushPreviewDelay = new CancellationTokenSource();
        TrackPreviewDebounce(DebouncedAction.RunAsync("brush preview", remaining, delay.Token, () =>
        {
            if (!ReferenceEquals(_brushPreviewDelay, delay)) return Task.CompletedTask;
            _brushPreviewDelay = null;
            delay.Dispose();
            if (IsBrushStrokeActive) DispatchBrushPreview();
            return Task.CompletedTask;
        }, timeProvider: _timeProvider));
    }
    private void DispatchBrushPreview()
    {
        _brushPreviewTimestamp = _timeProvider.GetTimestamp();
        CancelAdjacentPreviewWarm(invalidateWorker: true, dropRetained: true, imageFile: SelectedImage);
        var generation = ReserveRenderOutcome(PreviewSurfaceIntent.Edited, promotionEligible: false);
        _brushPreviewDirty = false;
        var render = _brushPreviewRender = ReplaceDebounce(ref _previewDebounce);
        TrackPreviewDebounce(RenderBrushPreviewAsync(render, generation));
    }
    private async Task RenderBrushPreviewAsync(CancellationTokenSource render, long generation)
    {
        var token = render.Token;
        var image = _localsGestureImage;
        var document = image?.EditSettings;
        try { await UpdatePreviewWithCurrentSliders(token, generation, promotable: false); }
        finally
        {
            // Let the captured state paint before reserving another outcome. Points arriving
            // during this render only mark it dirty; completion dispatches the newest state.
            if (ReferenceEquals(_brushPreviewRender, render))
            {
                _brushPreviewRender = null;
                // Rollback replaces the live document; its last stroke is committed paint.
                if (IsBrushStrokeActive && !ReferenceEquals(image?.EditSettings, document))
                {
                    DiscardLocalsGesture();
                }
                else if (!token.IsCancellationRequested && IsBrushStrokeActive && _brushPreviewDirty)
                    ScheduleBrushPreview();
            }
        }
    }
    private void StopBrushPreview()
    {
        CancelAndDispose(ref _brushPreviewDelay);
        // _previewDebounce owns disposal; detach before cancellation can resume the render.
        var render = _brushPreviewRender;
        _brushPreviewRender = null;
        _brushPreviewDirty = false;
        if (ReferenceEquals(_previewDebounce, render)) render?.Cancel();
        _brushPreviewTimestamp = null;
    }
}
