namespace HappyPhoton.Views;

public sealed record ShortcutEntry(string Keys, string Action);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutEntry> Entries);

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("Navigation and views",
        [
            new("G", "Switch to Library"),
            new("D", "Switch to Develop"),
            new("E", "Toggle Library and Develop"),
            new("Enter", "Toggle Library/Develop, apply crop, or confirm export"),
            new("Escape", "Close the active panel, cancel crop, or return to Library"),
            new("F", "Toggle image-only fullscreen"),
            new("←  /  →", "Previous or next image"),
            new("↑  /  ↓", "Move by one grid row in Library"),
            new("Page Up  /  Page Down", "Move by one visible page in Library"),
            new("Home  /  End", "Select the first or last image in Library"),
            new("Folder Enter", "Open the selected folder and focus the Library grid"),
        ]),
        new("Organize",
        [
            new("P", "Flag the current image"),
            new("U", "Remove the current image's flag"),
            new("X", "Reject the current image"),
            new("1–5", "Set the current image's star rating"),
            new("0", "Clear the current image's rating"),
            new("Space", "Toggle the current image's export selection"),
            new("Ctrl+A", "Select all visible images for export"),
            new("Ctrl+Click", "Toggle an image in the export selection"),
            new("Shift+Click", "Select an export range"),
            new("Delete", "Move the current image to Trash after confirmation"),
        ]),
        new("Develop and edit",
        [
            new("B", "Toggle before/after"),
            new("C", "Toggle crop mode"),
            new("W", "Toggle the white balance eyedropper"),
            new("Ctrl+Shift+C", "Copy the current image's edit settings"),
            new("Ctrl+Shift+V", "Paste copied edit settings"),
            new("Ctrl+Z", "Undo the last edit"),
            new("Ctrl+Y  /  Ctrl+Shift+Z", "Redo the last undone edit"),
            new("Mouse wheel", "Zoom in Develop"),
            new("Drag  /  Middle-drag", "Pan a zoomed image"),
            new("Double-click thumbnail", "Open the image in Develop"),
        ]),
        new("Export",
        [
            new("Ctrl+E", "Open the export dialog (ignored in fullscreen)"),
        ]),
    ];
}
