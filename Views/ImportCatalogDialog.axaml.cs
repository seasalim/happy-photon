using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class ImportCatalogDialog : Window
{
    private MainWindowViewModel _viewModel = null!;
    private string _catalogPath = string.Empty;
    private readonly Dictionary<string, TextBox> _rootEditors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resolvedRootMappings =
        new(StringComparer.Ordinal);
    private LightroomCatalogContents? _source;
    private CatalogImportPreview? _preview;
    private CancellationTokenSource? _operationCancellation;
    private bool _isReading = true;
    private bool _isReimport;

    public ImportCatalogDialog()
    {
        InitializeComponent();
    }

    public ImportCatalogDialog(
        MainWindowViewModel viewModel,
        string catalogPath) : this()
    {
        _viewModel = viewModel;
        _catalogPath = catalogPath;
        CatalogPathText.Text = catalogPath;
        StatusText.Text = "Creating a consistent, read-only snapshot…";
        Opened += OnOpened;
        Closing += (_, args) => args.Cancel = _isReading;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        SetBusy(true, "Creating a consistent, read-only snapshot…");
        try
        {
            _source = await _viewModel.ReadLightroomCatalogAsync(_catalogPath);
            var stored = await _viewModel.LoadCatalogImportSettingsAsync(_catalogPath);
            _isReimport = stored != null;
            BuildRootEditors(stored?.RootMappings);
            PolicyPicker.SelectedIndex = stored?.Policies.Values.FirstOrDefault() ==
                                         CatalogImportPolicy.FillEmptyOnly ? 1 : 0;
            MappingSection.IsVisible = true;
            PolicySection.IsVisible = true;
            PreviewButton.IsVisible = true;
            CancelButton.IsEnabled = true;
            StatusText.Text = _source.IsVerifiedVersion
                ? $"Lightroom { _source.MajorVersion } catalog ready."
                : $"Catalog version {_source.DatabaseVersion} is structurally compatible but unverified.";
            await RefreshPreviewAsync();
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
        }
        finally
        {
            _isReading = false;
            SetBusy(false, StatusText.Text ?? string.Empty);
            CancelButton.IsEnabled = true;
        }
    }

    private void BuildRootEditors(IReadOnlyDictionary<string, string>? storedMappings)
    {
        if (_source == null) return;
        RootsPanel.Children.Clear();
        _rootEditors.Clear();
        _resolvedRootMappings.Clear();
        var importableRoots = _source.Roots
            .Where(root => root.PhotoCount > 0)
            .ToArray();
        var initial = CatalogImportService.ResolveMappings(
            importableRoots,
            storedMappings ?? new Dictionary<string, string>());
        var automatic = CatalogImportService.ResolveMappings(
            importableRoots,
            new Dictionary<string, string>());
        foreach (var root in importableRoots)
        {
            if (initial.TryGetValue(root.SourcePath, out var mappedPath))
                RootsPanel.Children.Add(BuildResolvedRootRow(root, mappedPath));
            else
                RootsPanel.Children.Add(BuildRootEditor(root));
        }

        var unresolved = importableRoots.Count(root =>
            !initial.ContainsKey(root.SourcePath));
        if (importableRoots.Length == 0)
        {
            MappingHelpText.Text =
                "No Lightroom locations contain photos with ratings, flags, or color labels.";
        }
        else if (unresolved == 0)
        {
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var allMatchedAutomatically = importableRoots.All(root =>
                automatic.TryGetValue(root.SourcePath, out var automaticPath) &&
                initial.TryGetValue(root.SourcePath, out var selectedPath) &&
                pathComparer.Equals(automaticPath, selectedPath));
            MappingHelpText.Text = allMatchedAutomatically
                ? "All locations matched automatically. Review the paths below or override a match."
                : "All locations are matched. Saved mappings are shown below and can be overridden.";
        }
        else
        {
            MappingHelpText.Text =
                "Choose a local folder for any location you want to import. Leave a location blank to skip its photos.";
        }

        var zeroCountRoots = _source.Roots.Count(root => root.PhotoCount == 0);
        if (zeroCountRoots > 0 && importableRoots.Length > 0)
        {
            var notice = new TextBlock
            {
                Text = zeroCountRoots == 1
                    ? "1 Lightroom location without ratings, flags, or color labels is not shown."
                    : $"{zeroCountRoots} Lightroom locations without ratings, flags, or color labels are not shown.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            notice.Classes.Add("root-count");
            RootsPanel.Children.Add(notice);
        }
    }

    private Grid BuildResolvedRootRow(CatalogSourceRoot root, string mappedPath)
    {
        _resolvedRootMappings[root.SourcePath] = mappedPath;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        var labels = new StackPanel { Spacing = 3 };
        var location = new TextBlock
        {
            Text = $"{root.SourcePath}  →  {mappedPath}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        location.Classes.Add("root-location");
        labels.Children.Add(location);
        var count = new TextBlock
        {
            Text = $"{root.PhotoCount} photos with ratings, flags, or color labels",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        count.Classes.Add("root-count");
        labels.Children.Add(count);
        row.Children.Add(labels);

        var overrideButton = new Button
        {
            Content = "Override…",
            VerticalAlignment = VerticalAlignment.Center
        };
        overrideButton.Classes.Add("root-override");
        overrideButton.Click += (_, _) =>
        {
            _resolvedRootMappings.Remove(root.SourcePath);
            var index = RootsPanel.Children.IndexOf(row);
            if (index >= 0)
            {
                RootsPanel.Children.RemoveAt(index);
                RootsPanel.Children.Insert(index, BuildRootEditor(root, mappedPath));
            }
            MappingHelpText.Text =
                "Choose a different local folder, or leave this location blank to skip its photos.";
        };
        Grid.SetColumn(overrideButton, 1);
        row.Children.Add(overrideButton);
        return row;
    }

    private Grid BuildRootEditor(CatalogSourceRoot root, string? initialPath = null)
    {
        var editor = new TextBox
        {
            Text = initialPath,
            PlaceholderText = "Choose a matching local folder"
        };
        _rootEditors[root.SourcePath] = editor;
        var choose = new Button { Content = "Choose…" };
        choose.Click += async (_, _) => await ChooseRootAsync(editor);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(new TextBlock
        {
            Text = $"{root.SourcePath}  ·  {root.PhotoCount} photos with ratings, flags, or color labels",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        labels.Children.Add(editor);
        row.Children.Add(labels);
        Grid.SetColumn(choose, 1);
        choose.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(choose);
        return row;
    }

    private async Task ChooseRootAsync(TextBox editor)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Map Lightroom Source Root",
                AllowMultiple = false
            });
        if (folders.Count > 0) editor.Text = folders[0].Path.LocalPath;
    }

    private async void OnPreviewClick(object? sender, RoutedEventArgs e) =>
        await RefreshPreviewAsync();

    private async Task RefreshPreviewAsync()
    {
        if (_source == null) return;
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, "Comparing Lightroom metadata with Happy Photon…");
        CancelButton.Content = "Cancel preview";
        try
        {
            var mappings = new Dictionary<string, string>(
                _resolvedRootMappings, StringComparer.Ordinal);
            foreach (var (sourceRoot, editor) in _rootEditors)
            {
                if (!string.IsNullOrWhiteSpace(editor.Text))
                    mappings[sourceRoot] = editor.Text;
            }
            var policy = PolicyPicker.SelectedIndex == 1
                ? CatalogImportPolicy.FillEmptyOnly
                : CatalogImportPolicy.LightroomWins;
            _preview = await _viewModel.PreviewCatalogImportAsync(
                _source, mappings, policy, _operationCancellation.Token);
            RenderReport(_preview.Report, applied: false);
            ApplyButton.IsVisible = true;
            StatusText.Text = "Review the preview, then apply when ready.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Preview canceled. No catalog changes were made.";
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            CancelButton.Content = "Close";
            SetBusy(false, StatusText.Text ?? string.Empty);
        }
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_preview == null) return;
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, "Applying all catalog changes in one transaction…");
        CancelButton.Content = "Cancel import";
        CancelButton.IsEnabled = true;
        try
        {
            var result = await _viewModel.ApplyCatalogImportAsync(
                _preview, _operationCancellation.Token);
            RenderReport(result.Report, applied: true);
            StatusText.Text = result.DatabaseWrites == 0
                ? "Everything is already up to date. No catalog rows were changed."
                : "Import complete. Lightroom and your original photographs were not changed.";
            MappingSection.IsVisible = false;
            PolicySection.IsVisible = false;
            PreviewButton.IsVisible = false;
            ApplyButton.IsVisible = false;
            CancelButton.Content = "Close";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Import canceled. No Happy Photon catalog changes were applied.";
            CancelButton.Content = "Close";
        }
        catch (CatalogImportConflictException exception)
        {
            ShowFailure(exception.Message, canRefreshPreview: true);
            CancelButton.Content = "Close";
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
            CancelButton.Content = "Close";
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false, StatusText.Text ?? string.Empty);
        }
    }

    private void RenderReport(CatalogImportReport report, bool applied)
    {
        ReportSection.IsVisible = true;
        HeadlineText.Text = report.NothingToImport
            ? "Nothing to import"
            : report.NothingMatched
                ? "Nothing matched"
                : applied
                    ? report.UpdatedPhotos == 0
                        ? "Everything is already up to date"
                        : _isReimport
                            ? $"Updated {report.UpdatedPhotos} photos, {Math.Max(0, report.MatchedPhotos - report.UpdatedPhotos)} unchanged"
                            : $"Imported metadata for {report.UpdatedPhotos} photos"
                    : $"{report.UpdatedPhotos} photos will be updated";
        var rerunNote = _isReimport &&
                        report.Rating.PreservedByPolicy +
                        report.Flag.PreservedByPolicy +
                        report.ColorLabel.PreservedByPolicy > 0
            ? "\nKept values may include ones you changed in Happy Photon since the last import."
            : string.Empty;
        var updated = applied ? "updated" : "to update";
        OutcomeText.Text =
            $"Matched paths: {report.MatchedPhotos}  ·  Existing: {report.ExistingCatalogRows}  ·  New paths: {report.NewlyStoredPaths}\n" +
            $"Ratings — {report.Rating.Written} {updated} · {report.Rating.Unchanged} already match · {report.Rating.PreservedByPolicy} kept your value\n" +
            $"Flags — {report.Flag.Written} {updated} · {report.Flag.Unchanged} already match · {report.Flag.PreservedByPolicy} kept your value\n" +
            $"Color labels — {report.ColorLabel.Written} {updated} · {report.ColorLabel.Unchanged} already match · {report.ColorLabel.PreservedByPolicy} kept your value · {report.ColorLabel.Unsupported} unrecognized left as-is" +
            rerunNote;
        ActionableHeading.IsVisible = report.ActionableOutcomes.Count > 0;
        ActionableText.Text = string.Join("\n", report.ActionableOutcomes.Select(text => "• " + text));
        InformationHeading.IsVisible = report.InformationalOutcomes.Count > 0;
        InformationText.Text = string.Join("\n", report.InformationalOutcomes.Select(text => "• " + text));
    }

    private void SetBusy(bool busy, string status)
    {
        BusyProgress.IsVisible = busy;
        BusyProgress.IsIndeterminate = busy;
        PreviewButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy;
        StatusText.Text = status;
    }

    private void ShowFailure(string message, bool canRefreshPreview = false)
    {
        StatusText.Text = message;
        ReportSection.IsVisible = true;
        HeadlineText.Text = "Import could not continue";
        OutcomeText.Text = message;
        PreviewButton.IsVisible = canRefreshPreview;
        PreviewButton.IsEnabled = canRefreshPreview;
        ApplyButton.IsVisible = false;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (_operationCancellation != null)
            _operationCancellation.Cancel();
        else
            Close();
    }
}
