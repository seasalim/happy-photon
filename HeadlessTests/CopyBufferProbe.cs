using System.Reflection;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Tests;

// Reads the view model's private copied-settings buffer (a PasteSnapshot record).
internal static class CopyBufferProbe
{
    public static object? Snapshot(MainWindowViewModel vm) =>
        typeof(MainWindowViewModel)
            .GetField("_copied", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm);

    public static object? Member(MainWindowViewModel vm, string name)
    {
        var snapshot = Snapshot(vm);

        return snapshot?.GetType().GetProperty(name)!.GetValue(snapshot);
    }
}
