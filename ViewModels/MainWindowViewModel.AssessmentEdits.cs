using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly AssessmentAxis<ImageFlag> FlagAxis = new(
        image => image.Flag,
        ImageFlag.Unflagged,
        (browse, image, flag) => browse.MatchesCurrentFilters(image, flag),
        (id, flag) => new AssessmentMutation(id, AssessmentAxes.Flag, Flag: flag),
        (_, next, previous) => DescribeFlagFeedback(next, previous),
        (flag, count) => $"{FlagAction(flag)} {count} photos",
        "Flag",
        "Unable to update flags",
        RecordsCullPerf: true);

    private static readonly AssessmentAxis<int> RatingAxis = new(
        image => image.Rating,
        0,
        (browse, image, rating) => browse.MatchesCurrentFilters(image, rating),
        (id, rating) => new AssessmentMutation(id, AssessmentAxes.Rating, Rating: rating),
        (_, next, previous) => DescribeRatingFeedback(next, previous),
        (_, count) => $"Rated {count} photos",
        "Rating",
        "Unable to update ratings",
        RecordsCullPerf: true);

    private static readonly AssessmentAxis<ColorLabel> ColorLabelAxis = new(
        image => image.ColorLabel,
        ColorLabel.None,
        (browse, image, label) => browse.MatchesCurrentFilters(image, label),
        (id, label) => new AssessmentMutation(id, AssessmentAxes.Label, ColorLabel: label),
        (vm, next, previous) => vm.DescribeColorFeedback(next, previous),
        (_, count) => $"Labeled {count} photos",
        "Color label",
        "Unable to update color labels",
        RecordsCullPerf: false);

    // Sets one assessment axis on the resolved targets. With toggles, a request
    // every target already holds clears the axis instead.
    private async Task SetAssessmentAsync<T>(
        AssessmentAxis<T> axis,
        T requested,
        bool toggles)
    {
        if (IsFullScreenMode) return;

        if (axis.RecordsCullPerf)
        {
            CullPerf?.Record("PickReceipt", SelectedImage?.CatalogId ?? 0, operation: -1);
        }

        var resolution = ResolveAssessmentTargets();
        var targets = resolution.Targets;
        var participants = targets.Concat(resolution.Companions).ToArray();
        if (targets.Count == 0) return;

        var comparer = EqualityComparer<T>.Default;
        var actedOnImage = targets.Count == 1 ? targets[0] : null;
        var previous = actedOnImage != null ? axis.Read(actedOnImage) : axis.Cleared;
        var next = toggles && !comparer.Equals(requested, axis.Cleared) &&
                   targets.All(image => comparer.Equals(axis.Read(image), requested))
            ? axis.Cleared
            : requested;

        if (targets.All(image => comparer.Equals(axis.Read(image), next)))
        {
            if (actedOnImage != null)
            {
                ShowAssessmentFeedback(actedOnImage, axis.Describe(this, next, previous));
            }

            return;
        }

        var selectedImage = SelectedImage;
        var representative = VisibleRepresentative(selectedImage);
        var replacement = selectedImage != null &&
                          targets.Contains(selectedImage) &&
                          representative != null &&
                          !axis.MatchesFilters(Browse, representative, next)
            ? Browse.ReplacementAfterRemoval(representative)
            : null;

        try
        {
            foreach (var target in participants)
            {
                await target.EnsureCatalogIdAsync(_catalogService);
            }

            await CommitAssessmentAsync(participants
                .Select(target => axis.Mutation(target.CatalogId, next))
                .ToArray());

            if (axis.RecordsCullPerf)
            {
                CullPerf?.Record("CatalogCommit", actedOnImage?.CatalogId ?? 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"{axis.FailureSubject} update failed: {ex.Message}");
            ShowTransientStatus(axis.FailureStatus);
            return;
        }

        Browse.RefreshFilters();
        if (!IsCompareMode && replacement != null && Browse.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }

        UpdateSelectedCount();
        if (targets.Count > 1)
        {
            ShowTransientStatus(axis.BatchStatus(next, targets.Count));
        }
        else if (actedOnImage != null &&
                 ReferenceEquals(SelectedImage, actedOnImage))
        {
            ShowAssessmentFeedback(actedOnImage, axis.Describe(this, next, previous));
        }
    }

    private static string FlagAction(ImageFlag flag) => flag switch
    {
        ImageFlag.Picked => "Picked",
        ImageFlag.Rejected => "Rejected",
        _ => "Unflagged"
    };

    // One axis of the flag, rating and color-label commands: its value, the
    // value that clears it, its filter check, catalog mutation and messages.
    // Only flags and ratings record CullPerf receipts.
    private sealed record AssessmentAxis<T>(
        Func<ImageFile, T> Read,
        T Cleared,
        Func<BrowseImageState, ImageFile, T, bool> MatchesFilters,
        Func<long, T, AssessmentMutation> Mutation,
        Func<MainWindowViewModel, T, T, string> Describe,
        Func<T, int, string> BatchStatus,
        string FailureSubject,
        string FailureStatus,
        bool RecordsCullPerf);
}
