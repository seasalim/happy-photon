using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private ImageFile? _copiedSource;

    private PhotoFrameFactsReader? _pasteFrameReader;

    private PhotoProfileSnapshot? _copiedProfileSource;

    private PhotoSpotSnapshot? _copiedSpotSource;

    private PhotoCameraFacts? RememberLoadedCamera(ImageFile image)
    {
        var cached = PasteFrameReader.ReadCamera(image, cachedOnly: true);

        using var basis = ImageService.Previews.AcquireLocalRangeBase(image, image.EditSettings, BaseImage.InteractivePreviewMaxDimension);
        if (basis == null) return cached;

        if (basis.Base.Info.SensorFrame is { } frame) PasteFrameReader.RememberSensorFrame(image, frame);

        var facts = cached ?? basis.Base.Info.CameraFacts ??
            new PhotoCameraFacts(basis.Base.Info.CameraIdentity, basis.Base.Info.IsMonochrome);
        PasteFrameReader.RememberCamera(image, facts);

        return facts;
    }

    private void CaptureProfileSource(ImageFile image, EditSettings settings)
    {
        var facts = RememberLoadedCamera(image);

        if (facts == null && image.IsRaw && (settings.RawProfile != null || settings.Lens.ProfileOverride != null))
        {
            facts = PasteFrameReader.ReadCamera(image);
        }

        _copiedProfileSource = new(image.FilePath, image.IsRaw, facts);
        _copiedSpotSource = new(image, facts, PasteFrameReader.ReadSensorFrame(image, cachedOnly: true));
    }

    internal PhotoFrameFactsReader PasteFrameReader =>
        _pasteFrameReader ??= new PhotoFrameFactsReader(_sourceAvailabilityService);

    private int? CachedReframeCount(IReadOnlyList<ImageFile> targets, bool optics)
    {
        if (_copiedSettings?.Crop is not { IsFullImage: false } || _copiedSource == null) return 0;

        var source = PasteFrameReader.Read(_copiedSource, _copiedSettings, cachedOnly: true);
        if (source == null) return null;

        var count = 0;

        foreach (var target in targets)
        {
            var settings = target.EditSettings.Clone();

            if (optics)
            {
                EditSettingsTransfer.ApplyGroups(_copiedSettings, settings,
                    [EditSettingsTransfer.Groups.Single(group => group.Name == "Optics")]);
            }

            var frame = PasteFrameReader.Read(target, settings, cachedOnly: true);
            if (frame == null) return null;
            if (!CropTransfer.Matches(source.Value, frame.Value)) count++;
        }

        return count;
    }

    private static bool PasteNeedsFrameFacts(EditSettings settings, IReadOnlyCollection<EditSettingsGroup> groups,
        bool targetHasRepairs) =>
        (settings.Repairs is { Count: > 0 } || targetHasRepairs) && groups.Any(group => group.Name == "Spot Removal") ||
        settings.Crop is { IsFullImage: false } && groups.Any(group => group.Name == "Crop & Straighten") ||
        settings.RawProfile != null && groups.Any(group => group.Name == "Camera Profile") ||
        settings.Lens.ProfileOverride != null && groups.Any(group => group.Name == "Lens Profile");

    private Task<PasteProposal> PreparePasteAsync(ImageFile target, EditSettings previous,
        IReadOnlyCollection<EditSettingsGroup> groups, PasteSnapshot snapshot)
    {
        RememberLoadedCamera(target);

        return PasteNeedsFrameFacts(snapshot.Settings, groups, previous.Repairs is { Count: > 0 })
            ? Task.Run(() => PreparePaste(target, previous, groups, snapshot))
            : Task.FromResult(PreparePaste(target, previous, groups, snapshot));
    }

    private PasteProposal PreparePaste(ImageFile target, EditSettings previous,
        IReadOnlyCollection<EditSettingsGroup> groups, PasteSnapshot snapshot,
        bool cachedOnly = false)
    {
        var skips = new Dictionary<string, string>();
        var compatible = ProfileSettingsTransfer.CompatibleGroups(snapshot.Profiles, snapshot.Settings,
            target, groups, PasteFrameReader, skips, cachedOnly);
        var from = 1;
        var to = 1;

        if ((snapshot.Settings.Repairs is { Count: > 0 } || previous.Repairs is { Count: > 0 }) &&
            compatible.Any(group => group.Name == "Spot Removal"))
        {
            var reason = snapshot.Spots.Compatibility(target, PasteFrameReader, cachedOnly, out from, out to);

            if (reason != null)
            {
                skips["Spot Removal"] = reason;
                compatible = compatible.Where(group => group.Name != "Spot Removal").ToArray();
            }
        }

        var proposal = previous.Clone();
        var reframed = false;
        var unavailable = false;

        if (compatible.Length > 0)
        {
            proposal = PhotoSettingsTransfer.Apply(snapshot.Source, snapshot.Settings, target, previous, compatible,
                PasteFrameReader, out reframed, out unavailable, cachedOnly);
        }

        if (from != to && proposal.Repairs != null && !skips.ContainsKey("Spot Removal"))
        {
            proposal.Repairs = proposal.Repairs.Select(repair => RepairOrientation.Map(repair, from, to)).ToList();
            EditSettingsJson.ValidateForSave(proposal);
        }

        if (unavailable) skips["Crop & Straighten"] = "facts unavailable";

        return new PasteProposal(proposal, reframed, skips,
            compatible.Any(group => group.Name == "Lens Profile"));
    }

    private EditSettings CapturePasteState(ImageFile image, bool changesFrame, bool changesLens)
    {
        var settings = CaptureLiveEditState();
        if (changesLens) settings.Lens.ProfileOverride = image.EditSettings.Lens.ProfileOverride;

        if (changesFrame)
        {
            settings.Rotation = image.EditSettings.Rotation;
            settings.HorizonRotation = image.EditSettings.HorizonRotation;
            settings.Crop = image.EditSettings.Crop?.Clone();
        }

        return settings;
    }

    private void ReportPaste(string status, int reframed,
        IEnumerable<KeyValuePair<string, string>> skips, int invalid = 0)
    {
        if (reframed > 0) status += $" · Crop reframed to fit on {reframed}";
        var counts = skips.GroupBy(skip => (skip.Key, skip.Value)).ToArray();

        foreach (var count in counts)
        {
            status += $" · {count.Key.Key} kept on {count.Count()} ({count.Key.Value})";
        }

        if (invalid > 0) status += $" · {invalid} unchanged (invalid settings)";

        ShowTransientStatus(status);

        if (counts.Length > 0 || invalid > 0)
        {
            _transientStatusCts?.Cancel();
        }
    }

    private sealed record PasteSnapshot(ImageFile Source, EditSettings Settings, PhotoProfileSnapshot Profiles, PhotoSpotSnapshot Spots);

    private sealed record PasteProposal(EditSettings Settings, bool Reframed,
        Dictionary<string, string> Skips, bool LensApplied);
}
