using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

/// <summary>
/// Test-side convenience wrappers over MutateAssessmentsAsync; production code
/// mutates assessments through MutateAssessmentsAsync directly.
/// </summary>
internal static class CatalogAssessmentExtensions
{
    public static Task SaveFlagStateAsync(
        this CatalogService catalog, long catalogId, ImageFlag flag) =>
        catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(catalogId, AssessmentAxes.Flag, Flag: flag)],
            AssessmentAxes.None);

    public static Task SaveFlagStateAsync(
        this CatalogService catalog,
        IReadOnlyCollection<long> catalogIds,
        ImageFlag flag) =>
        catalog.MutateAssessmentsAsync(
            catalogIds.Distinct().Select(id => new AssessmentMutation(
                id, AssessmentAxes.Flag, Flag: flag)).ToArray(),
            AssessmentAxes.None);

    public static Task SaveRatingAsync(
        this CatalogService catalog, long catalogId, int rating) =>
        catalog.MutateAssessmentsAsync(
            [new AssessmentMutation(catalogId, AssessmentAxes.Rating,
                Rating: Math.Clamp(rating, 0, 5))],
            AssessmentAxes.None);

    public static Task SaveRatingAsync(
        this CatalogService catalog,
        IReadOnlyCollection<long> catalogIds,
        int rating) =>
        catalog.MutateAssessmentsAsync(
            catalogIds.Distinct().Select(id => new AssessmentMutation(
                id, AssessmentAxes.Rating,
                Rating: Math.Clamp(rating, 0, 5))).ToArray(),
            AssessmentAxes.None);

    public static Task SaveColorLabelAsync(
        this CatalogService catalog,
        IReadOnlyCollection<long> catalogIds,
        ColorLabel colorLabel) =>
        catalog.MutateAssessmentsAsync(
            catalogIds.Distinct().Select(id => new AssessmentMutation(
                id, AssessmentAxes.Label, ColorLabel: colorLabel)).ToArray(),
            AssessmentAxes.None);
}
