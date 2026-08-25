namespace HappyPhoton.Views;

public enum ShortcutWorkspace
{
    Browse,
    Develop,
    FullScreen
}

public enum ShortcutExemption
{
    AcceleratorOfListedControl,
    DialogAffordance
}

public sealed record ShortcutReachabilityClaim(
    string Action,
    string? ControlName = null,
    ShortcutWorkspace? Workspace = null,
    ShortcutExemption? Exemption = null)
{
    public static ShortcutReachabilityClaim Control(
        string action,
        string controlName,
        ShortcutWorkspace workspace) =>
        new(action, controlName, workspace);

    public static ShortcutReachabilityClaim Exempt(
        string action,
        ShortcutExemption exemption) =>
        new(action, Exemption: exemption);
}

public sealed record ShortcutEntry(
    string Keys,
    string Action,
    IReadOnlyList<ShortcutReachabilityClaim> Reachability);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutEntry> Entries);

public static class ShortcutCatalog
{
    private static ShortcutReachabilityClaim Browse(string action, string controlName) =>
        ShortcutReachabilityClaim.Control(action, controlName, ShortcutWorkspace.Browse);

    private static ShortcutReachabilityClaim Develop(string action, string controlName) =>
        ShortcutReachabilityClaim.Control(action, controlName, ShortcutWorkspace.Develop);

    private static ShortcutReachabilityClaim FullScreen(string action, string controlName) =>
        ShortcutReachabilityClaim.Control(action, controlName, ShortcutWorkspace.FullScreen);

    private static ShortcutReachabilityClaim Accelerator(string action) =>
        ShortcutReachabilityClaim.Exempt(action, ShortcutExemption.AcceleratorOfListedControl);

    private static ShortcutReachabilityClaim Dialog(string action) =>
        ShortcutReachabilityClaim.Exempt(action, ShortcutExemption.DialogAffordance);

    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("Navigation and views",
        [
            new("G", "Switch to Browse", [Browse("Switch to Browse", "BrowseTabButton")]),
            new("D", "Switch to Develop", [Develop("Switch to Develop", "DevelopTabButton")]),
            new("E", "Toggle Browse and Develop",
            [
                Browse("Switch to Browse", "BrowseTabButton"),
                Develop("Switch to Develop", "DevelopTabButton")
            ]),
            new("Enter", "Toggle Browse/Develop, apply crop, or confirm export",
            [
                Browse("Switch to Browse", "BrowseTabButton"),
                Develop("Switch to Develop", "DevelopTabButton"),
                Develop("Apply crop", "ApplyCropButton"),
                Dialog("Confirm a dialog")
            ]),
            new("Escape", "Close the active panel, cancel crop, or return to Browse",
            [
                Dialog("Close the active dialog"),
                Develop("Cancel crop", "CancelCropButton"),
                Browse("Return to Browse", "BrowseTabButton")
            ]),
            new("F", "Toggle fullscreen; restrict navigation to 2+ selected photos",
            [
                Develop("Enter fullscreen", "FullScreenButton"),
                FullScreen("Exit fullscreen", "FullScreenExitButton")
            ]),
            new("←  /  →", "Previous or next image",
            [
                Develop("Previous image", "PreviousImageButton"),
                Develop("Next image", "NextImageButton")
            ]),
            new("↑  /  ↓", "Move by one grid row in Browse",
                [Browse("Move by one grid row", "ThumbnailTile")]),
            new("Page Up  /  Page Down", "Move by one visible page in Browse",
                [Browse("Move by one visible page", "ThumbnailTile")]),
            new("Home  /  End", "Select the first or last image in Browse",
                [Browse("Select the first or last image", "ThumbnailTile")]),
            new("Folder Enter", "Open the selected folder and focus the Browse grid",
                [Browse("Open the selected folder", "FolderTree")]),
            new("Ctrl+,", "Open Settings", [Browse("Open Settings", "SettingsButton")]),
        ]),
        new("Organize",
        [
            new("P", "Pick the Browse selection, else active photo; active-only in Develop",
                [Browse("Pick images", "PickImageButton")]),
            new("U", "Unflag the Browse selection, else active photo; active-only in Develop",
                [Browse("Unflag images", "UnflagImageButton")]),
            new("X", "Reject the Browse selection, else active photo; active-only in Develop",
                [Browse("Reject images", "RejectImageButton")]),
            new("1–5", "Rate the Browse selection, else active photo; active-only in Develop",
            [
                Browse("Set a 1-star rating", "Rating1Button"),
                Browse("Set a 2-star rating", "Rating2Button"),
                Browse("Set a 3-star rating", "Rating3Button"),
                Browse("Set a 4-star rating", "Rating4Button"),
                Browse("Set a 5-star rating", "Rating5Button")
            ]),
            new("0", "Clear ratings on the Browse selection, else active photo; active-only in Develop",
                [Browse("Clear ratings", "ClearRatingButton")]),
            new("6–9", "Set color labels on Browse selection, else active photo; active-only in Develop",
                [Browse("Set a color label", "ColorLabelButton")]),
            new("Space", "Toggle the active photo in the selection",
                [Browse("Toggle selection", "SelectionBadgeButton")]),
            new("Ctrl+A", "Select all visible images",
                [Browse("Select all visible images", "SelectAllMenuItem")]),
            new("Ctrl+D", "Deselect all visible images",
                [Browse("Deselect all visible images", "DeselectAllMenuItem")]),
            new("Ctrl+Click", "Toggle an image in the selection",
                [Accelerator("Toggle selection")]),
            new("Shift+Click", "Select a range", [Accelerator("Select a range")]),
            new("Delete", "Move the Browse selection, else active photo, to Trash after confirmation",
                [Browse("Move images to Trash", "DeleteImageMenuItem")]),
        ]),
        new("Develop and edit",
        [
            new("B", "Toggle before/after in Develop or fullscreen",
                [Develop("Toggle before/after", "BeforeAfterButton")]),
            new("Ctrl+B", "Toggle color assessment mode in Develop or fullscreen",
                [Develop("Toggle color assessment", "ColorAssessmentButton")]),
            new("C", "Toggle crop mode", [Develop("Toggle crop mode", "CropModeButton")]),
            new("W", "Toggle the white balance eyedropper",
                [Develop("Toggle the white balance eyedropper", "WhiteBalancePickerButton")]),
            new("J", "Toggle highlight/floor clipping indicators in Develop",
                [Develop("Toggle clipping indicators", "DisplayFloorTriangleTarget")]),
            new("Ctrl+Shift+C", "Copy the current image's edit settings",
                [Develop("Copy edit settings", "CopyEditSettingsButton")]),
            new("Ctrl+Shift+V", "Paste copied edit settings",
                [Develop("Paste edit settings", "PasteEditSettingsButton")]),
            new("Ctrl+Z", "Undo the last edit in Develop",
                [Develop("Undo the last edit", "UndoEditButton")]),
            new("Ctrl+Y  /  Ctrl+Shift+Z", "Redo the last undone edit in Develop",
                [Develop("Redo the last undone edit", "RedoEditButton")]),
            new("Mouse wheel", "Zoom in Develop", [Accelerator("Zoom")]),
            new("Hold left mouse", "Peek at 1:1 below 1:1 in Develop or fullscreen",
                [Accelerator("Peek at 1:1")]),
            new("Drag  /  Middle-drag", "Pan a zoomed image", [Accelerator("Pan")]),
            new("Double-click thumbnail", "Open the image in Develop",
                [Accelerator("Open an image in Develop")]),
        ]),
        new("Export",
        [
            new("Ctrl+E", "Open the export dialog (ignored in fullscreen)",
                [Browse("Open the export dialog", "BrowseExportButton")]),
        ]),
    ];
}
