using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _alignmentGridCts;

    [ObservableProperty]
    private bool _isAlignmentGridVisible;

    [ObservableProperty]
    private int _geometryVertical;

    [ObservableProperty]
    private int _geometryHorizontal;

    [ObservableProperty]
    private int _geometryAspect;

    [ObservableProperty]
    private int _geometryDistortion;

    partial void OnGeometryVerticalChanged(int value) => OnGeometryValueChanged();
    partial void OnGeometryHorizontalChanged(int value) => OnGeometryValueChanged();
    partial void OnGeometryAspectChanged(int value) => OnGeometryValueChanged();
    partial void OnGeometryDistortionChanged(int value) => OnGeometryValueChanged();

    private void OnGeometryValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage) return;
        ShowAlignmentGridTemporarily();
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void ShowAlignmentGridTemporarily()
    {
        if (!IsDevelopMode || IsCropMode || SelectedImage is not { } image)
        {
            return;
        }

        IsAlignmentGridVisible = true;
        var debounce = ReplaceDebounce(ref _alignmentGridCts);
        _ = DebouncedAction.RunAsync(
            "alignment grid",
            TimeSpan.FromSeconds(1.5),
            debounce.Token,
            () =>
            {
                if (ReferenceEquals(SelectedImage, image) &&
                    IsDevelopMode &&
                    !IsCropMode)
                {
                    IsAlignmentGridVisible = false;
                }
                return Task.CompletedTask;
            },
            timeProvider: _timeProvider);
    }

    private void ClearAlignmentGrid()
    {
        CancelAndDispose(ref _alignmentGridCts);
        IsAlignmentGridVisible = false;
    }

    private void SaveGeometryTo(EditSettings target)
    {
        var geometry = new GeometrySettings
        {
            Vertical = GeometryVertical,
            Horizontal = GeometryHorizontal,
            Aspect = GeometryAspect,
            Distortion = GeometryDistortion
        };
        target.Geometry = geometry.IsIdentity ? null : geometry;
    }

    private void LoadGeometryFrom(EditSettings source)
    {
        GeometryVertical = source.Geometry?.Vertical ?? 0;
        GeometryHorizontal = source.Geometry?.Horizontal ?? 0;
        GeometryAspect = source.Geometry?.Aspect ?? 0;
        GeometryDistortion = source.Geometry?.Distortion ?? 0;
    }

    private void ResetGeometryUi()
    {
        GeometryVertical = 0;
        GeometryHorizontal = 0;
        GeometryAspect = 0;
        GeometryDistortion = 0;
    }
}
