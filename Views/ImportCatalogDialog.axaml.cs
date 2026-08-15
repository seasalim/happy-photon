using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class ImportCatalogDialog : Window
{
    private CatalogImportFlowViewModel? _flow;
    private readonly Dictionary<string, TextBox> _rootEditors =
        new(StringComparer.Ordinal);
    private bool _returnAppliedOnClose;
    private bool _closingWithResult;
    private bool _policyEventsAttached;

    public ImportCatalogDialog()
    {
        InitializeComponent();
    }

    public ImportCatalogDialog(
        MainWindowViewModel viewModel,
        string catalogPath,
        bool returnAppliedOnClose = false) : this(
            new CatalogImportFlowViewModel(viewModel, catalogPath),
            catalogPath,
            returnAppliedOnClose)
    {
    }

    internal ImportCatalogDialog(
        CatalogImportFlowViewModel flow,
        string catalogPath,
        bool returnAppliedOnClose = false) : this()
    {
        _flow = flow;
        _returnAppliedOnClose = returnAppliedOnClose;
        CatalogPathText.Text = catalogPath;
        StatusText.Text = flow.StatusText;
        flow.InputsReady += OnInputsReady;
        flow.PropertyChanged += OnFlowPropertyChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        UpdateUi();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_flow != null) await _flow.InitializeAsync();
    }

    private void OnInputsReady(object? sender, EventArgs e)
    {
        BuildRootEditors();
        if (_flow == null) return;
        PolicyPicker.SelectedIndex = _flow.Policy ==
                                     CatalogImportPolicy.FillEmptyOnly ? 1 : 0;
        if (!_policyEventsAttached)
        {
            PolicyPicker.SelectionChanged += OnPolicyChanged;
            _policyEventsAttached = true;
        }
        MappingSection.IsVisible = true;
        PolicySection.IsVisible = true;
        UpdateUi();
    }

    private void OnFlowPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        UpdateUi();

    private void UpdateUi()
    {
        if (_flow == null) return;
        BusyProgress.IsVisible = _flow.IsBusy;
        BusyProgress.IsIndeterminate = _flow.IsBusy;
        StatusText.Text = _flow.StatusText;
        MappingSection.IsEnabled = _flow.InputsEnabled;
        PolicySection.IsEnabled = _flow.InputsEnabled;
        ApplyButton.IsVisible = _flow.CanApply;
        ApplyButton.IsEnabled = _flow.CanApply;
        CancelButton.IsEnabled = true;
        CancelButton.Content = _flow.IsApplying
            ? "Cancel import"
            : _flow.IsPreviewRunning
                ? "Cancel check"
                : _flow.IsInitializing
                    ? "Cancel catalog read"
                    : "Close";

        if (_flow.Report != null)
        {
            RenderReport(_flow.Report, _flow.IsApplied);
        }
        else if (_flow.FailureText != null)
        {
            ShowFailure(_flow.FailureText);
        }
        else
        {
            ReportSection.IsVisible = false;
        }

        if (_flow.IsApplied)
        {
            MappingSection.IsVisible = false;
            PolicySection.IsVisible = false;
            ApplyButton.IsVisible = false;
        }
    }

    private void BuildRootEditors()
    {
        if (_flow?.Source == null) return;
        RootsPanel.Children.Clear();
        _rootEditors.Clear();
        var importableRoots = _flow.Source.Roots
            .Where(root => root.PhotoCount > 0)
            .ToArray();
        var automatic = CatalogImportService.ResolveMappings(
            importableRoots, new Dictionary<string, string>());
        foreach (var root in importableRoots)
        {
            var mappedPath = _flow.RootMappings.GetValueOrDefault(
                root.SourcePath, string.Empty);
            RootsPanel.Children.Add(string.IsNullOrEmpty(mappedPath)
                ? BuildRootEditor(root)
                : BuildResolvedRootRow(root, mappedPath));
        }

        var unresolved = importableRoots.Count(root =>
            string.IsNullOrEmpty(_flow.RootMappings.GetValueOrDefault(root.SourcePath)));
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
                pathComparer.Equals(automaticPath,
                    _flow.RootMappings[root.SourcePath]));
            MappingHelpText.Text = allMatchedAutomatically
                ? "All locations matched automatically. Review the paths below or override a match."
                : "All locations are matched. Saved mappings are shown below and can be overridden.";
        }
        else
        {
            MappingHelpText.Text =
                "Choose a local folder for any location you want to import. Leave a location blank to skip its photos.";
        }

        AddZeroCountNotice(importableRoots.Length);
    }

    private void AddZeroCountNotice(int importableRootCount)
    {
        if (_flow?.Source == null) return;
        var zeroCountRoots = _flow.Source.Roots.Count(root => root.PhotoCount == 0);
        if (zeroCountRoots == 0 || importableRootCount == 0) return;
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

    private Grid BuildResolvedRootRow(CatalogSourceRoot root, string mappedPath)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        var labels = new StackPanel { Spacing = 3 };
        var location = new TextBlock
        {
            Text = IsSameLocation(root.SourcePath, mappedPath)
                ? mappedPath
                : $"{root.SourcePath}  →  {mappedPath}",
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
        overrideButton.Click += async (_, _) =>
        {
            var index = RootsPanel.Children.IndexOf(row);
            if (index >= 0)
            {
                RootsPanel.Children.RemoveAt(index);
                RootsPanel.Children.Insert(index, BuildRootEditor(root, mappedPath));
            }
            MappingHelpText.Text =
                "Choose a different local folder, or leave this location blank to skip its photos.";
            if (_flow != null) await _flow.OverrideRootAsync(root.SourcePath);
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
        editor.TextChanged += (_, _) =>
            _flow?.UpdateRootText(root.SourcePath, editor.Text);
        editor.GotFocus += (_, _) => _flow?.BeginRootEdit(root.SourcePath);
        editor.LostFocus += async (_, _) =>
        {
            if (_flow != null)
                await _flow.CommitRootEditAsync(root.SourcePath, editor.Text);
        };

        var choose = new Button { Content = "Choose…" };
        choose.Click += async (_, _) => await ChooseRootAsync(root.SourcePath, editor);
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

    private static bool IsSameLocation(string sourcePath, string mappedPath)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(mappedPath)),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task ChooseRootAsync(string sourceRoot, TextBox editor)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Map Lightroom Source Root",
                AllowMultiple = false
            });
        if (folders.Count == 0 || _flow == null) return;
        var path = folders[0].Path.LocalPath;
        editor.Text = path;
        await _flow.ChooseRootAsync(sourceRoot, path);
    }

    private async void OnPolicyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_flow == null) return;
        await _flow.SetPolicyAsync(PolicyPicker.SelectedIndex == 1
            ? CatalogImportPolicy.FillEmptyOnly
            : CatalogImportPolicy.LightroomWins);
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_flow != null) await _flow.ApplyAsync();
    }

    private void RenderReport(CatalogImportReport report, bool applied)
    {
        ReportSection.IsVisible = true;
        ReportSectionLabel.Text = applied ? "WHAT CHANGED" : "WHAT WILL CHANGE";
        HeadlineText.Text = report.NothingToImport
            ? "Nothing to import"
            : report.NothingMatched
                ? "Nothing matched"
                : applied
                    ? report.UpdatedPhotos == 0
                        ? "Everything is already up to date"
                        : _flow?.IsReimport == true
                            ? $"Updated {report.UpdatedPhotos} photos, {Math.Max(0, report.MatchedPhotos - report.UpdatedPhotos)} unchanged"
                            : $"Imported metadata for {report.UpdatedPhotos} photos"
                    : $"{report.UpdatedPhotos} photos will be updated";
        var rerunNote = _flow?.IsReimport == true &&
                        report.Rating.PreservedByPolicy +
                        report.Flag.PreservedByPolicy +
                        report.ColorLabel.PreservedByPolicy > 0
            ? "\nKept values may include ones you changed in Happy Photon since the last import."
            : string.Empty;
        var updated = applied ? "updated" : "to update";
        var unavailable = report.UnavailableFilePhotos == 0
            ? string.Empty
            : $"\nMapped files not found: {report.UnavailableFilePhotos}";
        OutcomeText.Text =
            $"Matched paths: {report.MatchedPhotos}  ·  Existing: {report.ExistingCatalogRows}  ·  New paths: {report.NewlyStoredPaths}" +
            unavailable + "\n" +
            $"Ratings — {report.Rating.Written} {updated} · {report.Rating.Unchanged} already match · {report.Rating.PreservedByPolicy} kept your value\n" +
            $"Flags — {report.Flag.Written} {updated} · {report.Flag.Unchanged} already match · {report.Flag.PreservedByPolicy} kept your value\n" +
            $"Color labels — {report.ColorLabel.Written} {updated} · {report.ColorLabel.Unchanged} already match · {report.ColorLabel.PreservedByPolicy} kept your value · {report.ColorLabel.Unsupported} unrecognized left as-is" +
            rerunNote;
        ActionableHeading.IsVisible = report.ActionableOutcomes.Count > 0;
        ActionableText.Text = string.Join("\n",
            report.ActionableOutcomes.Select(text => "• " + text));
        InformationHeading.IsVisible = report.InformationalOutcomes.Count > 0;
        InformationText.Text = string.Join("\n",
            report.InformationalOutcomes.Select(text => "• " + text));
    }

    private void ShowFailure(string message)
    {
        ReportSection.IsVisible = true;
        ReportSectionLabel.Text = "WHAT WILL CHANGE";
        HeadlineText.Text = "Import could not continue";
        OutcomeText.Text = message;
        ActionableHeading.IsVisible = false;
        ActionableText.Text = string.Empty;
        InformationHeading.IsVisible = false;
        InformationText.Text = string.Empty;
        ApplyButton.IsVisible = false;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (_flow?.HasInFlightOperation == true)
        {
            _flow.CancelCurrentOperation();
            return;
        }

        _closingWithResult = true;
        Close(_returnAppliedOnClose && _flow?.ApplySucceeded == true);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_flow?.HasInFlightOperation == true)
        {
            _flow.CancelCurrentOperation();
            args.Cancel = true;
            return;
        }

        if (!_returnAppliedOnClose || _flow?.ApplySucceeded != true ||
            _closingWithResult)
        {
            return;
        }

        args.Cancel = true;
        _closingWithResult = true;
        Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_flow == null) return;
        _flow.InputsReady -= OnInputsReady;
        _flow.PropertyChanged -= OnFlowPropertyChanged;
        _flow.Dispose();
    }
}
