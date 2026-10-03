namespace HappyPhoton.ViewModels;

public sealed record TipsContent(string Title, string[] Lines)
{
    public static TipsContent Browse { get; } = new("Browse tips",
    [
        "`P` pick, `X` reject, `1–5` rate, `6–9` label.",
        "`Ctrl+click` or `Shift+click` to select several.",
        "`Ctrl+Shift+S` syncs settings across the selection.",
        "`E` Loupe, `C` Compare, `D` Develop."
    ]);

    public static TipsContent Develop { get; } = new("Develop tips",
    [
        "Click a slider's value to type a number.",
        "`Double-click` a slider to reset it.",
        "`Alt+click` a group header to solo it.",
        "`\\` before and after, `R` crop, `Q` spots."
    ]);

    public static TipsContent Export { get; } = new("Export tips",
    [
        "Exports use your Browse selection.",
        "Example for this photo shows each file's name.",
        "Originals are never changed or overwritten.",
        "`Enter` exports, `Escape` goes back."
    ]);
}
