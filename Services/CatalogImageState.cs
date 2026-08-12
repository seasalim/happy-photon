using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CatalogImageState(
    long CatalogId,
    EditSettings EditSettings,
    ImageFlag Flag,
    int Rating,
    ColorLabel ColorLabel,
    long AssessmentRevision = 0,
    DateTime? AssessedUtc = null,
    AssessmentAxes PendingAxes = AssessmentAxes.None);
