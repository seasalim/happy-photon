using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

// Observe the real RenderAsync argument while its existing source-work gate is
// suspended. No copied production builder and no image content reads are involved.
internal static class ConstructionVmProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static void Set(object target, string field, object? value) =>
        target.GetType().GetField(field, Flags)!.SetValue(target, value);
    internal static object? Call(object target, string method, params object?[] supplied)
    {
        var info = target.GetType().GetMethods(Flags).Single(m => m.Name == method);
        var args = info.GetParameters().Select((p, i) => i < supplied.Length ? supplied[i] : p.DefaultValue).ToArray();
        return info.Invoke(target, args);
    }

    internal static async Task<EditSettings> Capture(MainWindowViewModel vm, Func<Task> action)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.SourceWorkGateAsync = () => release.Task;
        Task? running = null;
        try
        {
            running = action();
            EditSettings? captured = null;
            await TestWaits.UntilAsync(() =>
                (captured = FindSnapshot(release.Task, new HashSet<object>(ReferenceEqualityComparer.Instance), 0)) != null || running.IsCompleted);
            Assert.NotNull(captured);
            return captured.Clone();
        }
        finally
        {
            vm.ImageService.Previews.SourceWorkGateAsync = null;
            release.TrySetResult();
            if (running != null) await running.WaitAsync(TestWaits.Condition);
        }
    }

    private static EditSettings? FindSnapshot(object? value, HashSet<object> seen, int depth)
    {
        if (value == null || depth > 12 || !seen.Add(value)) return null;
        var type = value.GetType();
        if (value is Delegate action) return FindSnapshot(action.Target, seen, depth + 1);
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum ||
            value is MainWindowViewModel or EditSettings or PreviewService) return null;
        foreach (var field in type.GetFields(Flags))
        {
            var child = field.GetValue(value);
            if (field.Name.Contains("settingsSnapshot", StringComparison.Ordinal) && child is EditSettings settings)
                return settings;
        }
        foreach (var field in type.GetFields(Flags))
        {
            if (field.Name is "m_continuationObject" or "StateMachine" ||
                field.Name.Contains("<>8__", StringComparison.Ordinal) ||
                field.Name.Contains("stateMachine", StringComparison.OrdinalIgnoreCase) ||
                field.Name is "m_action")
            {
                var found = FindSnapshot(field.GetValue(value), seen, depth + 1);
                if (found != null) return found;
            }
        }
        return null;
    }

    internal sealed class NoSourceLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken token) =>
            BaseImageLoadOutcome.FromImage(null, BaseImageLoadFailure.DecodeFailed);
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
