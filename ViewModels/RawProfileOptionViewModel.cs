using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed class RawProfileOptionViewModel
{
    internal const string BuiltInLabel = "Happy Photon Matrix";

    public string Label { get; }
    public bool CanSelect { get; }
    public string? Status { get; }
    public bool IsProfile { get; }
    public bool IsGroupHeader { get; }
    public bool IsDivider { get; }
    public bool IsChooseFile { get; }
    public bool CanActivate => IsChooseFile || IsProfile && CanSelect;
    internal RawProfileSelection? Selection { get; }
    internal bool IsBuiltIn { get; }
    internal string? Fingerprint { get; }

    internal RawProfileOptionViewModel(DcpProfileOption option)
    {
        Label = option.IsBuiltIn ? BuiltInLabel : option.DisplayName;
        CanSelect = option.CanSelect;
        Status = option.Message;
        Selection = option.Selection?.Clone();
        IsBuiltIn = option.IsBuiltIn;
        Fingerprint = option.Fingerprint;
        IsProfile = true;
    }

    private RawProfileOptionViewModel(
        string label,
        bool canSelect = false,
        string? status = null,
        RawProfileSelection? selection = null,
        bool isBuiltIn = false,
        bool isProfile = false,
        bool isGroupHeader = false,
        bool isDivider = false,
        bool isChooseFile = false,
        string? fingerprint = null)
    {
        Label = label;
        CanSelect = canSelect;
        Status = status;
        Selection = selection?.Clone();
        IsBuiltIn = isBuiltIn;
        IsProfile = isProfile;
        IsGroupHeader = isGroupHeader;
        IsDivider = isDivider;
        IsChooseFile = isChooseFile;
        Fingerprint = fingerprint;
    }

    internal static RawProfileOptionViewModel BuiltIn() => new(
        BuiltInLabel,
        canSelect: true,
        isBuiltIn: true,
        isProfile: true);

    internal static RawProfileOptionViewModel Anchor(
        RawProfileSelection selection) => new(
            ProfileLabel(selection),
            canSelect: true,
            selection: selection,
            isProfile: true);

    internal static RawProfileOptionViewModel GroupHeader(string label) =>
        new(label, isGroupHeader: true);

    internal static RawProfileOptionViewModel Divider() =>
        new(string.Empty, isDivider: true);

    internal static RawProfileOptionViewModel ChooseFile() =>
        new("Choose .dcp file…", isChooseFile: true);

    internal RawProfileOptionViewModel WithLabel(string label) => new(
        label,
        CanSelect,
        Status,
        Selection,
        IsBuiltIn,
        isProfile: true,
        fingerprint: Fingerprint);

    private static string ProfileLabel(RawProfileSelection selection) =>
        selection.Source == RawProfileSource.Embedded
            ? "Embedded camera profile"
            : Path.GetFileNameWithoutExtension(selection.Location) is { Length: > 0 } name
                ? name
                : "Selected camera profile";
}
