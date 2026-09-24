using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public sealed record WatermarkFont(string Name, bool Installed)
{
    public string Label => Installed ? Name : $"{Name} (not installed)";
    public FontFamily Face => Installed ? new FontFamily(Name) : FontManager.Current.DefaultFontFamily;
}

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _watermarkProofDebounce;
    private CancellationTokenSource? _watermarkSaveDebounce;
    private string[]? _watermarkSystemFonts;

    private void RequestWatermarkProofRefresh()
    {
        var token = ReplaceDebounce(ref _watermarkProofDebounce).Token;
        _ = DebouncedAction.RunAsync("watermark proof", TimeSpan.FromMilliseconds(250), token,
            () =>
            {
                token.ThrowIfCancellationRequested();
                RequestExportProofRefresh();
                return Task.CompletedTask;
            }, timeProvider: _timeProvider);
    }

    internal void RequestWatermarkSettingsSave()
    {
        var token = ReplaceDebounce(ref _watermarkSaveDebounce).Token;
        _ = DebouncedAction.RunAsync("watermark settings", TimeSpan.FromMilliseconds(250), token,
            () =>
            {
                token.ThrowIfCancellationRequested();
                return PersistAppSettingsAsync?.Invoke() ?? Task.CompletedTask;
            }, timeProvider: _timeProvider);
    }

    internal void CancelWatermarkSettingsSave() => CancelAndDispose(ref _watermarkSaveDebounce);
    private bool _isWatermarkExpanded;
    private bool _refreshingWatermarkFonts;
    public IReadOnlyList<WatermarkFont> WatermarkFonts { get; private set; } = [];
    public bool IsWatermarkExpanded
    {
        get => _isWatermarkExpanded;
        set
        {
            if (!SetProperty(ref _isWatermarkExpanded, value) || !value) return;
            _watermarkSystemFonts ??= FontManager.Current.SystemFonts.Select(font => font.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
            RefreshWatermarkFonts();
        }
    }
    public WatermarkFont? SelectedWatermarkFont
    {
        get => WatermarkFonts.FirstOrDefault(font => StringComparer.CurrentCultureIgnoreCase.Equals(font.Name, ExportSettings.Watermark.FontFamily));
        set { if (value != null && !_refreshingWatermarkFonts) ExportSettings.Watermark.FontFamily = value.Name; }
    }
    public bool IsWatermarkSide => ExportSettings.Watermark.Edge is WatermarkEdge.Left or WatermarkEdge.Right;
    public bool IsWatermarkAlignmentVisible => ExportSettings.Watermark.Edge != WatermarkEdge.Center;
    public string[] WatermarkAlignmentLabels => IsWatermarkSide ? ["Top", "Middle", "Bottom"] : ["Left", "Center", "Right"];
    public string WatermarkSummary
    {
        get
        {
            var mark = ExportSettings.Watermark;
            var along = WatermarkAlignmentLabels[(int)mark.Alignment].ToLowerInvariant();
            var position = mark.Edge == WatermarkEdge.Center ? "Center" : IsWatermarkSide
                ? $"{mark.Edge} edge · {WatermarkAlignmentLabels[(int)mark.Alignment]}" +
                    (mark.RotateAlongEdge ? " · Rotated" : "")
                : $"{mark.Edge} {along}";
            return mark.Enabled ? $"\"{mark.Text}\" · {position}" : "Off";
        }
    }

    private void InitializeExportWatermark()
    {
        var mark = ExportSettings.Watermark;
        if (string.IsNullOrWhiteSpace(mark.FontFamily))
            mark.FontFamily = FontManager.Current.DefaultFontFamily.Name;
        mark.ResolveFontFamily = family =>
            (_watermarkSystemFonts ?? FontManager.Current.SystemFonts.Select(font => font.Name))
                .Contains(family, StringComparer.CurrentCultureIgnoreCase)
                    ? family : FontManager.Current.DefaultFontFamily.Name;
        mark.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportWatermark.FontFamily)) RefreshWatermarkFonts();
            OnPropertyChanged(nameof(WatermarkSummary));
            OnPropertyChanged(nameof(IsWatermarkSide));
            OnPropertyChanged(nameof(IsWatermarkAlignmentVisible));
            OnPropertyChanged(nameof(WatermarkAlignmentLabels));
        };
    }

    private void RefreshWatermarkFonts()
    {
        if (_watermarkSystemFonts == null) return;
        var saved = ExportSettings.Watermark.FontFamily;
        WatermarkFonts = _watermarkSystemFonts.Append(saved).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new WatermarkFont(name, _watermarkSystemFonts.Contains(name,
                StringComparer.CurrentCultureIgnoreCase))).ToArray();
        _refreshingWatermarkFonts = true;
        try
        {
            OnPropertyChanged(nameof(WatermarkFonts));
            OnPropertyChanged(nameof(SelectedWatermarkFont));
        }
        finally { _refreshingWatermarkFonts = false; }
    }
}
