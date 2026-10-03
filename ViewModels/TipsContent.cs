namespace HappyPhoton.ViewModels;

public sealed record TipsContent(string Title, string[] Lines)
{
    public static TipsContent Browse { get; } = new("Browse tips",
    [
        "Cull from the keyboard: `P` pick, `X` reject, `U` unflag, `1–5` rate, `6–9` color label.",
        "Build a selection with `Ctrl+click` and `Shift+click`. Selected photos are lighter, and the ring marks the active photo.",
        "Flags, ratings and labels act on the whole selection. `Ctrl+Shift+S` syncs the outlined photo's settings to the rest.",
        "`E` opens Loupe, `C` compares 2–4 selected photos, and `D` goes to Develop."
    ]);

    public static TipsContent Develop { get; } = new("Develop tips",
    [
        "Click a slider's value to type an exact number. `Double-click` a slider to reset it.",
        "`Alt+click` a group header to show only that group.",
        "`Ctrl+Shift+C` copies settings and `Ctrl+Shift+V` pastes them. In Browse, paste goes to every selected photo.",
        "`\\` toggles before and after. `R` crops, `Q` removes spots, and `Shift+W` opens Locals."
    ]);

    public static TipsContent Export { get; } = new("Export tips",
    [
        "Export works on your Browse selection. Change photos… can switch it to your picks.",
        "Example for this photo shows the exact path and name each copy gets.",
        "Originals are never modified, and exports never overwrite them.",
        "`Enter` exports, and `Escape` returns to where you were."
    ]);
}
