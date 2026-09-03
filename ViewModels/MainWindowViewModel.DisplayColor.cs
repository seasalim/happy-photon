using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private DisplayTransformSnapshot _displayTransform = DisplayTransformSnapshot.None;

    public DisplayTransformSnapshot DisplayTransform
    {
        get => _displayTransform;
        private set
        {
            if (!SetProperty(ref _displayTransform, value)) return;
            foreach (var pane in ComparePanes) pane.DisplayTransform = value;
            if (LoupePane != null) LoupePane.DisplayTransform = value;
            OnPropertyChanged(nameof(DisplayProfileStatusText));
            OnPropertyChanged(nameof(RawRuntimeSupportText));
        }
    }

    public string DisplayProfileStatusText => DisplayTransform.DiagnosticText;

    public DisplaySourceColorSpace PreviewDisplayColorSpace =>
        _proofIsDisplayed &&
        _displayedProofColorSpace == Models.OutputColorSpace.DisplayP3
            ? DisplaySourceColorSpace.DisplayP3
            : DisplaySourceColorSpace.Srgb;

    internal bool ResolveDisplayProfile(nint windowHandle)
    {
        var resolved = _displayColorManagementService.Resolve(
            windowHandle,
            DisplayTransform);
        if (string.Equals(
                resolved.Identity,
                DisplayTransform.Identity,
                StringComparison.Ordinal))
        {
            return false;
        }

        DisplayTransform = resolved;
        return true;
    }
}
