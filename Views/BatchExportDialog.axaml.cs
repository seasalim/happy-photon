using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class BatchExportDialog : Window
{
    private readonly MainWindowViewModel? _mainViewModel;
    private readonly IReadOnlyList<ImageFile> _images = [];
    private CancellationTokenSource? _exportCts;

    public BatchExportDialog()
    {
        InitializeComponent();
        ViewModel = new ExportDialogViewModel(new ExportSettings(), 0);
        DataContext = ViewModel;
    }

    public BatchExportDialog(
        MainWindowViewModel mainViewModel,
        IReadOnlyList<ImageFile> images,
        ExportDialogMode mode = ExportDialogMode.Standard)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        _images = images;
        ViewModel = new ExportDialogViewModel(
            mainViewModel.ExportSettings,
            images.Count,
            mode);
        ViewModel.UpdateHydrationScope(
            mainViewModel.GetExportHydrationScope(images));
        DataContext = ViewModel;
    }

    public ExportDialogViewModel ViewModel { get; }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsExporting)
        {
            _exportCts?.Cancel();
            return;
        }

        Close(false);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            ViewModel.Settings.OutputFolder = folders[0].Path.LocalPath;
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsTourPreview)
        {
            Close(false);
            return;
        }

        if (_mainViewModel == null || !ViewModel.CanExport || _exportCts != null)
        {
            return;
        }

        var targetPaths = _images
            .Select(image => ViewModel.Settings.GetOutputPath(
                image.FileName,
                ViewModel.SelectedVariant,
                useSubfolders: false))
            .ToList();
        var originalPaths = ExportSafety.BuildOriginalPathSet(
            _mainViewModel.Library.AllImages.Select(image => image.FilePath));
        var blockedCount = targetPaths.Count(path =>
            ExportSafety.IsOriginalPath(path, originalPaths));

        if (blockedCount > 0)
        {
            var noun = blockedCount == 1 ? "file" : "files";
            await ConfirmationDialog.ShowMessageAsync(
                this,
                "Export blocked",
                $"{blockedCount} export {noun} would overwrite original images. " +
                "Choose a different output folder or naming pattern.");
            return;
        }

        var existingFiles = targetPaths.Where(File.Exists).ToList();
        if (existingFiles.Count > 0)
        {
            var message = existingFiles.Count == 1
                ? $"The file \"{Path.GetFileName(existingFiles[0])}\" already exists. Overwrite?"
                : $"{existingFiles.Count} files already exist in the output folder. Overwrite?";
            if (!await ConfirmationDialog.ConfirmAsync(
                    this,
                    "Confirm Overwrite",
                    message))
            {
                return;
            }
        }

        var hydrationScope = _mainViewModel.GetExportHydrationScope(_images);
        ViewModel.UpdateHydrationScope(hydrationScope);
        var hydrationApproved = false;
        if (hydrationScope.IsRequired)
        {
            hydrationApproved = await ConfirmationDialog.ConfirmAsync(
                this,
                "Download originals for export?",
                ViewModel.OnlineOnlyMessage,
                cancelLabel: "Cancel",
                confirmLabel: "Download / Export");
            if (!hydrationApproved)
            {
                return;
            }
        }

        _exportCts = new CancellationTokenSource();
        ViewModel.BeginExport();
        var progress = new Progress<(int current, int total, string fileName)>(value =>
            ViewModel.UpdateProgress(value.current, value.total, value.fileName));

        try
        {
            ExportBatchResult result;
            if (hydrationApproved)
            {
                result = await _mainViewModel.ExportBatchApprovedAsync(
                    _images,
                    progress,
                    _exportCts.Token);
            }
            else
            {
                result = await _mainViewModel.ExportBatchAsync(
                    _images,
                    progress,
                    _exportCts.Token);
            }

            if (result.FailedImages.Count > 0)
            {
                ViewModel.ShowPartialExport(result);
                return;
            }

            if (result.Warnings.Count > 0)
            {
                ViewModel.ShowExportWarnings(result);
                return;
            }

            ViewModel.EndExport();
            Close(true);
        }
        catch (OperationCanceledException)
        {
            ViewModel.EndExport();
        }
        catch (Exception exception)
        {
            ViewModel.ShowError($"Export failed: {exception.Message}");
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
        }
    }

    private void OnCancelExportClick(object? sender, RoutedEventArgs e) =>
        _exportCts?.Cancel();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel.IsExporting)
            {
                _exportCts?.Cancel();
            }
            else
            {
                Close(false);
            }

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ViewModel.IsExporting)
        {
            _exportCts?.Cancel();
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
