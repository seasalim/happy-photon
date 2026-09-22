using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private HashSet<string> _exportVersionedPaths = new(StringComparer.OrdinalIgnoreCase);

    public int VisiblePickedPhotoCount =>
        Browse.VisibleImages.Count(image => image.Flag == ImageFlag.Picked);
    public bool CanUsePickedPhotos => VisiblePickedPhotoCount > 0;
    public string UsePickedPhotosLabel => $"Use picked photos ({VisiblePickedPhotoCount})";
    public string UsePickedPhotosScope => CanUsePickedPhotos
        ? "Replaces the selection with picked photos in the current Browse view."
        : "No picked photos in the current view";
    public string ExportButtonLabel => IsExportJobRunning ? "Export in progress…" : $"Export {ExportFileCount} {(ExportFileCount == 1 ? "file" : "files")}";
    public string ExportValidationReason => IsExportJobRunning
        ? "An export is running. Changes prepare the next batch."
        : HasNoExportCaptures ? "Choose photos in Browse to begin." : ExportSettings.ValidationReason;
    public bool HasExportValidationReason => ExportValidationReason.Length > 0;
    private int _exportFilenameChoice;
    public int ExportFilenameChoice
    {
        get => _exportFilenameChoice;
        set
        {
            if (!SetProperty(ref _exportFilenameChoice, value)) return;
            if (value == 0) ExportSettings.NamingPattern = "{name}";
            OnPropertyChanged(nameof(IsCustomExportFilename));
            OnPropertyChanged(nameof(ExportOptionsSummary));
        }
    }
    public bool IsCustomExportFilename => ExportFilenameChoice == 1;
    public string ExportOptionsSummary =>
        $"{(ExportSettings.OutputColorSpace == OutputColorSpace.Srgb ? "sRGB" : "Display P3")} · " +
        $"{ExportSettings.OutputSharpening} · {(IsCustomExportFilename ? "Custom filenames" : "Original filenames")}";
    public string ExportSubfolderHelp => ArmedExportSizeCount > 1
        ? "Each size gets its own subfolder." : "Files go directly into the destination.";
    public string ExportPathExample
    {
        get
        {
            if (ActiveExportCapture is not { } capture || ExportSettings.ValidationReason.Length > 0)
                return "Choose photos, a destination and valid sizes.";
            var variants = ExportSettings.GetActiveVariants();
            var output = ExportSettings.SnapshotOutput();
            var suffix = _exportVersionedPaths.Contains(capture.Image.FilePath)
                ? $"-V{capture.Image.Version}" : string.Empty;
            try
            {
                return string.Join(Environment.NewLine, variants.Select(variant =>
                    Path.GetRelativePath(output.OutputFolder, ExportJob.ResolvePath(
                        capture.Image.FileName, variant, output, variants.Count > 1,
                        DateTime.Now.ToString("yyyyMMdd"), suffix))));
            }
            catch (ArgumentException) { return "Choose a valid destination folder."; }
            catch (NotSupportedException) { return "Choose a valid destination folder."; }
        }
    }

    [RelayCommand]
    private void ChooseExportPhotosInBrowse() => WorkspaceMode = WorkspaceMode.Browse;

    [RelayCommand(CanExecute = nameof(CanUsePickedPhotos))]
    private void UsePickedPhotos()
    {
        if (!CanUsePickedPhotos) return;
        Browse.ReplaceSelection(Browse.VisibleImages
            .Where(image => image.Flag == ImageFlag.Picked).ToHashSet());
        UpdateSelectedCount();
        PrepareExportWorkspace();
    }

    private void NotifyExportBatchSettings()
    {
        OnPropertyChanged(nameof(ExportButtonLabel));
        OnPropertyChanged(nameof(ExportPathExample));
        OnPropertyChanged(nameof(ExportSubfolderHelp));
        OnPropertyChanged(nameof(ExportOptionsSummary));
        OnPropertyChanged(nameof(VisiblePickedPhotoCount));
        OnPropertyChanged(nameof(UsePickedPhotosLabel));
        OnPropertyChanged(nameof(UsePickedPhotosScope));
        OnPropertyChanged(nameof(CanUsePickedPhotos));
        UsePickedPhotosCommand.NotifyCanExecuteChanged();
        NotifyExportRunCommandState();
    }
}
