using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class BatchExportPanel : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? CancelRequested;

    public ExportSettings Settings { get; } = new();

    public BatchExportPanel()
    {
        InitializeComponent();
        DataContext = Settings;
        UpdatePreview();
    }

    public void SetImageCount(int count)
    {
        HeaderText.Text = $"Export {count} Image{(count != 1 ? "s" : "")}";
    }

    public void SetDefaultOutputFolder(string folder)
    {
        if (string.IsNullOrEmpty(Settings.OutputFolder))
        {
            Settings.OutputFolder = Path.Combine(folder, "exports");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            Settings.OutputFolder = folders[0].Path.LocalPath;
        }
    }

    private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FormatBox == null) return;
        Settings.Format = FormatBox.SelectedIndex switch
        {
            1 => ExportFormat.Png,
            2 => ExportFormat.Webp,
            _ => ExportFormat.Jpeg
        };
        UpdatePreview();
    }

    private void OnSizesChanged(object? sender, RoutedEventArgs e) => UpdatePreview();

    private void OnNamingPatternChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NamingPatternBox == null || CustomPatternBox == null) return;

        if (NamingPatternBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString() ?? "{name}";

            if (tag == "custom")
            {
                CustomPatternBox.IsVisible = true;
            }
            else
            {
                CustomPatternBox.IsVisible = false;
                Settings.NamingPattern = tag;
            }

            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (PreviewText == null) return;
        var fileName = Settings.GetOutputFileName($"example_photo{Settings.FileExtension}");
        var variants = Settings.GetActiveVariants();
        var folder = variants.Count > 1
            ? variants.First(variant => variant.MaxDimension.HasValue).Name + "/"
            : string.Empty;
        PreviewText.Text = folder + fileName;
    }

    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        ExportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelExportClick(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowProgress(bool show)
    {
        ExportProgress.IsVisible = show;
        CancelButton.IsVisible = show;
        ExportButton.IsVisible = !show;
    }

    public void UpdateProgress(int current, int total, string currentFile)
    {
        ExportProgress.Maximum = total;
        ExportProgress.Value = current;
        ProgressText.Text = $"{current}/{total} - {currentFile}";
    }

    public void ClearProgress()
    {
        ExportProgress.Value = 0;
        ProgressText.Text = "";
        ShowProgress(false);
    }
}
