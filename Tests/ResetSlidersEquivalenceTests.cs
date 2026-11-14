using System.Reflection;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ResetSlidersEquivalenceTests : IDisposable
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly CatalogVmFixture _fx = new("reset-sliders");

    [Fact]
    public async Task ResetSliders_MatchesLoadingDefaultSettings_ExceptPickerAndCanReset()
    {
        using var catalog = _fx.CreateCatalog("catalog");
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(_fx.Path("photo.dng"));

        Dirty(vm);
        Invoke(vm, "ResetSliders");
        var reset = Snapshot(vm);

        Dirty(vm);
        Invoke(vm, "LoadSlidersFrom", new EditSettings());
        var loaded = Snapshot(vm);

        var differing = reset.Keys
            .Where(key => !Equals(reset[key], loaded[key]))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // CanArmBrushFromShortcut is derived from !IsWhiteBalancePicking.
        Assert.Equal(
            "CanArmBrushFromShortcut,IsWhiteBalancePicking",
            string.Join(",", differing.Where(key => key != "CanReset")));
        Assert.Equal(false, reset["IsWhiteBalancePicking"]);
        Assert.Equal(true, loaded["IsWhiteBalancePicking"]);
    }

    public void Dispose() => _fx.Dispose();

    private static void Dirty(MainWindowViewModel vm)
    {
        vm.Exposure = 1.5;
        vm.WhiteBalanceKelvinPosition = 0.8;
        vm.WhiteBalanceTint = 12;
        vm.IsWhiteBalancePicking = true;
        vm.HlReconstruction = HlReconstructionMode.Blend;
        vm.CaptureSharpen = 44;
        vm.LuminanceNr = 30;
        vm.ChromaNr = 20;
        vm.Vignette = -15;
        vm.Midpoint = 30;
        vm.Grain = 10;
        vm.GrainSize = GrainSize.Fine;
        vm.LensDistortion = !LensSettings.DefaultDistortion;
        vm.LensChromaticAberration = !LensSettings.DefaultChromaticAberration;
        vm.LensVignetting = !LensSettings.DefaultVignetting;
        vm.GeometryVertical = 5;
        vm.GeometryHorizontal = -5;
        vm.GeometryAspect = 3;
        vm.GeometryDistortion = 2;
        vm.Brightness = 10;
        vm.Contrast = 20;
        vm.Saturation = 30;
        vm.Vibrance = 40;
        vm.Shadows = 15;
        vm.Highlights = -25;
        vm.Whites = 10;
        vm.Blacks = -10;
        vm.Texture = 5;
        vm.Clarity = 7;
        vm.HorizonRotation = 1.25;
        SetLoading(vm, true);
    }

    private static void SetLoading(MainWindowViewModel vm, bool loading) =>
        typeof(MainWindowViewModel).GetField("_isLoadingImage", PrivateInstance)!.SetValue(vm, loading);

    private static void Invoke(MainWindowViewModel vm, string method, params object[] args)
    {
        typeof(MainWindowViewModel).GetMethod(method, PrivateInstance)!.Invoke(vm, args);
        SetLoading(vm, false);
    }

    private static Dictionary<string, object?> Snapshot(MainWindowViewModel vm)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in typeof(MainWindowViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !IsScalar(property.PropertyType)) continue;

            values[property.Name] = property.GetValue(vm);
        }

        foreach (var field in new[] { "_liveWhiteBalance", "_liveMixer" })
        {
            values[field] = JsonSerializer.Serialize(
                typeof(MainWindowViewModel).GetField(field, PrivateInstance)!.GetValue(vm));
        }

        return values;
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;

        return vm;
    }
}
