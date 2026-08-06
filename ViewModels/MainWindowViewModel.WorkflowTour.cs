using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public enum WorkflowTourStep
{
    None,
    ChooseWhatMatters,
    ShapePhotograph,
    DeliverCopies
}

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private WorkflowTourStep _workflowTourStep;

    public bool IsChooseWhatMattersTourVisible =>
        WorkflowTourStep == WorkflowTourStep.ChooseWhatMatters &&
        !IsDevelopMode;

    public bool IsShapePhotographTourVisible =>
        WorkflowTourStep == WorkflowTourStep.ShapePhotograph &&
        IsDevelopMode;

    public bool IsDeliverCopiesTourVisible =>
        WorkflowTourStep == WorkflowTourStep.DeliverCopies &&
        !IsDevelopMode;

    public void StartWorkflowTour()
    {
        IsFullScreenMode = false;
        IsDevelopMode = false;
        WorkflowTourStep = WorkflowTourStep.ChooseWhatMatters;
    }

    [RelayCommand]
    private void ShowDevelopTourStep()
    {
        IsFullScreenMode = false;
        IsDevelopMode = true;
        WorkflowTourStep = WorkflowTourStep.ShapePhotograph;
    }

    [RelayCommand]
    private void ShowExportTourStep()
    {
        IsDevelopMode = false;
        WorkflowTourStep = WorkflowTourStep.DeliverCopies;
    }

    [RelayCommand]
    private async Task OpenExportFromTourAsync()
    {
        WorkflowTourStep = WorkflowTourStep.None;
        IsFullScreenMode = false;
        IsDevelopMode = false;
        await ShowExportDialogAsync(ExportDialogMode.TourPreview);
    }

    [RelayCommand]
    private void FinishWorkflowTour() =>
        WorkflowTourStep = WorkflowTourStep.None;

    [RelayCommand]
    private void EndWorkflowTour() =>
        WorkflowTourStep = WorkflowTourStep.None;

    partial void OnWorkflowTourStepChanged(WorkflowTourStep value) =>
        NotifyWorkflowTourVisibilityChanged();

    private void NotifyWorkflowTourVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsChooseWhatMattersTourVisible));
        OnPropertyChanged(nameof(IsShapePhotographTourVisible));
        OnPropertyChanged(nameof(IsDeliverCopiesTourVisible));
    }
}
