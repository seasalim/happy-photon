using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private async void OnSavePresetRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.CanSavePreset)
        {
            return;
        }

        var initialName = string.Empty;
        while (true)
        {
            var name = await TextInputDialog.ShowAsync(this, "Save Preset", "Preset name:", initialName);
            if (name == null)
            {
                return;
            }

            var existing = vm.PresetService.FindUserPresetByName(name);
            if (existing == null)
            {
                await RunPresetOperationAsync(() => vm.SaveCurrentAsPresetAsync(name), "save");
                return;
            }

            var overwrite = await ConfirmationDialog.ConfirmAsync(
                this,
                "Overwrite Preset",
                $"A preset named '{name}' already exists. Overwrite it?");
            if (overwrite)
            {
                await RunPresetOperationAsync(
                    () => vm.SaveCurrentAsPresetAsync(name, existing.Id), "save");
                return;
            }

            initialName = name;
        }
    }

    private async void OnRenamePresetRequested(object? sender, string presetId)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.PresetService.GetById(presetId) is not { } preset)
        {
            return;
        }

        var initialName = preset.Name;
        while (true)
        {
            var name = await TextInputDialog.ShowAsync(
                this, "Rename Preset", "Preset name:", initialName);
            if (name == null || name == preset.Name)
            {
                return;
            }

            var collision = vm.PresetService.FindUserPresetByName(name);
            if (collision != null && collision.Id != presetId)
            {
                await ConfirmationDialog.ShowMessageAsync(
                    this, "Rename Preset", "A preset with this name already exists.");
                initialName = name;
                continue;
            }

            await RunPresetOperationAsync(
                () => vm.RenameUserPresetAsync(presetId, name), "rename");
            return;
        }
    }

    private async void OnDeletePresetRequested(object? sender, string presetId)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.PresetService.GetById(presetId) is not { } preset)
        {
            return;
        }

        var confirmed = await ConfirmationDialog.ConfirmAsync(
            this,
            "Delete Preset",
            $"Delete preset '{preset.Name}'? Images already edited with it keep their edits.",
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        await RunPresetOperationAsync(() => vm.DeleteUserPresetAsync(presetId), "delete");
    }

    private async Task RunPresetOperationAsync(Func<Task> operation, string action)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ConfirmationDialog.ShowMessageAsync(
                this, "Preset Error", $"Could not {action} preset: {exception.Message}");
        }
    }
}
