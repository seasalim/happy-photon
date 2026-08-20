using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public Func<Uri, Task<bool>>? LaunchUriAsync { get; set; }

    [RelayCommand]
    private async Task CopyMetadataDetailsAsync()
    {
        if (CopyToClipboardAsync == null ||
            BuildMetadataDetails(SelectedImage) is not { } details)
        {
            return;
        }

        try
        {
            await CopyToClipboardAsync(details);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Metadata clipboard copy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenSelectedImageMapAsync()
    {
        if (LaunchUriAsync == null ||
            SelectedImage is not { HasGpsCoordinates: true } image)
        {
            return;
        }

        var latitude = image.GpsLatitude!.Value.ToString(
            "0.######",
            CultureInfo.InvariantCulture);
        var longitude = image.GpsLongitude!.Value.ToString(
            "0.######",
            CultureInfo.InvariantCulture);
        var uri = new Uri(
            $"https://www.openstreetmap.org/?mlat={latitude}&mlon={longitude}" +
            $"#map=15/{latitude}/{longitude}");

        try
        {
            await LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Metadata map launch failed: {ex.Message}");
        }
    }

    internal static string? BuildMetadataDetails(ImageFile? image)
    {
        if (image == null) return null;

        var details = new StringBuilder()
            .AppendLine("FILE")
            .AppendLine(image.FileName);
        if (image.MetadataLoaded)
        {
            AppendLine(details, image.FileDetailsDisplay);
            if (image.DisplayDate is { } displayDate)
            {
                details.Append(displayDate.ToString(
                    "MMM d, yyyy  h:mm tt",
                    CultureInfo.CurrentCulture));
                if (image.IsFileModifiedDateFallback)
                {
                    details.Append(" (file modified)");
                }
                details.AppendLine();
            }

            AppendSection(
                details,
                "CAMERA",
                image.CameraDisplay,
                image.LensModel,
                image.ExposureDisplay,
                image.CaptureConditionsDisplay);
            AppendSection(
                details,
                "LOCATION",
                image.GpsDisplay,
                image.GpsAltitudeDisplay);
        }

        return details.ToString().TrimEnd();
    }

    private static void AppendSection(
        StringBuilder details,
        string heading,
        params string?[] rows)
    {
        var visibleRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .ToArray();
        if (visibleRows.Length == 0) return;

        details.AppendLine(heading);
        foreach (var row in visibleRows)
        {
            details.AppendLine(row);
        }
    }

    private static void AppendLine(StringBuilder details, string? row)
    {
        if (!string.IsNullOrWhiteSpace(row))
        {
            details.AppendLine(row);
        }
    }
}
