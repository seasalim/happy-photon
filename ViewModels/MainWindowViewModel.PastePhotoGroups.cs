using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private ImageFile? _copiedSource;

    private PhotoFrameFactsReader? _pasteFrameReader;

    private PhotoProfileSnapshot? _copiedProfileSource;

    private PhotoCameraFacts? RememberLoadedCamera(ImageFile image)
    {
        var cached = PasteFrameReader.ReadCamera(image, cachedOnly: true);
        if (cached != null) return cached;

        using var basis = ImageService.Previews.AcquireLocalRangeBase(image, image.EditSettings, BaseImage.InteractivePreviewMaxDimension);
        if (basis == null || !image.IsRaw) return null;

        var facts = new PhotoCameraFacts(basis.Base.Info.CameraIdentity, basis.Base.Info.IsMonochrome);
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

    private static bool PasteNeedsFrameFacts(EditSettings settings, IReadOnlyCollection<EditSettingsGroup> groups) =>
        settings.Crop is { IsFullImage: false } && groups.Any(group => group.Name == "Crop & Straighten") ||
        settings.RawProfile != null && groups.Any(group => group.Name == "Camera Profile") ||
        settings.Lens.ProfileOverride != null && groups.Any(group => group.Name == "Lens Profile");

    private Task<PasteProposal> PreparePasteAsync(ImageFile target, EditSettings previous,
        IReadOnlyCollection<EditSettingsGroup> groups, PasteSnapshot snapshot)
    {
        RememberLoadedCamera(target);

        return PasteNeedsFrameFacts(snapshot.Settings, groups)
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
        var proposal = previous.Clone();
        var reframed = false;
        var unavailable = false;

        if (compatible.Length > 0)
        {
            proposal = PhotoSettingsTransfer.Apply(snapshot.Source, snapshot.Settings, target, previous, compatible,
                PasteFrameReader, out reframed, out unavailable, cachedOnly);
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

    private sealed record PasteSnapshot(ImageFile Source, EditSettings Settings, PhotoProfileSnapshot Profiles);

    private sealed record PasteProposal(EditSettings Settings, bool Reframed,
        Dictionary<string, string> Skips, bool LensApplied);
}
