using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private ImageFile? _copiedSource;

    private PhotoFrameFactsReader? _pasteFrameReader;

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
        settings.Crop is { IsFullImage: false } && groups.Any(group => group.Name == "Crop & Straighten");

    private Task<PasteProposal> PreparePasteAsync(ImageFile target, EditSettings previous,
        IReadOnlyCollection<EditSettingsGroup> groups, (ImageFile Source, EditSettings Settings) snapshot)
    {
        return PasteNeedsFrameFacts(snapshot.Settings, groups)
            ? Task.Run(() => PreparePaste(target, previous, groups, snapshot))
            : Task.FromResult(PreparePaste(target, previous, groups, snapshot));
    }

    private PasteProposal PreparePaste(ImageFile target, EditSettings previous,
        IReadOnlyCollection<EditSettingsGroup> groups, (ImageFile Source, EditSettings Settings) snapshot,
        bool cachedOnly = false)
    {
        var proposal = PhotoSettingsTransfer.Apply(snapshot.Source, snapshot.Settings, target, previous, groups,
            PasteFrameReader, out var reframed, out var unavailable, cachedOnly);

        return new PasteProposal(proposal, reframed, unavailable);
    }

    private EditSettings CapturePasteState(ImageFile image, bool changesFrame)
    {
        var settings = CaptureLiveEditState();

        if (changesFrame)
        {
            settings.Rotation = image.EditSettings.Rotation;
            settings.HorizonRotation = image.EditSettings.HorizonRotation;
            settings.Crop = image.EditSettings.Crop?.Clone();
        }

        return settings;
    }

    private void ReportPaste(string status, int reframed, int unavailable, int invalid = 0)
    {
        if (reframed > 0) status += $" · Crop reframed to fit on {reframed}";
        if (unavailable > 0) status += $" · Crop & Straighten kept on {unavailable} (facts unavailable)";
        if (invalid > 0) status += $" · {invalid} unchanged (invalid settings)";

        ShowTransientStatus(status);

        if (unavailable > 0 || invalid > 0)
        {
            _transientStatusCts?.Cancel();
        }
    }

    private sealed record PasteProposal(EditSettings Settings, bool Reframed, bool FactsUnavailable);
}
