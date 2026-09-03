namespace HappyPhoton.Services;

internal sealed partial class LensfunDatabase
{
    private LensfunCamera[] MatchingCameras(string make, string model)
    {
        var matches = _cameras.Where(camera =>
            MakerMatches(camera.MakerKey, make) &&
            ModelMatches(camera.ModelKey, make, model)).ToArray();
        if (matches.Length > 0) return matches;
        return _cameras.Where(camera =>
            MakerMatches(camera.MakerKey, make) && camera.Aliases.Any(alias =>
                ModelMatches(alias, make, model))).ToArray();
    }

    private static LensfunLens[] MatchingLenses(IReadOnlyList<LensfunLens> mounted, string suppliedModel)
    {
        var key = Normalize(suppliedModel);
        var matches = mounted.Where(lens => LensModelMatches(lens, key)).ToArray();
        if (matches.Length > 0) return matches;
        var tokens = Tokenize(suppliedModel);
        matches = mounted.Where(lens => LensTokenModelMatches(lens, tokens)).ToArray();
        if (matches.Length > 0) return matches;
        matches = mounted.Where(lens => lens.Aliases.Any(alias =>
            ModelIdentityMatches(alias, lens.MakerKey, key))).ToArray();
        if (matches.Length > 0) return ExpandAliasMatches(mounted, matches);
        matches = mounted.Where(lens =>
            lens.Aliases.Any(alias => TokenIdentityMatches(
                alias, lens.MakerTokens, tokens))).ToArray();
        return ExpandAliasMatches(mounted, matches);
    }

    private static LensfunLens[] ExpandAliasMatches(
        IReadOnlyList<LensfunLens> mounted,
        IReadOnlyList<LensfunLens> matches)
    {
        var primaryKeys = matches.Select(PrimaryIdentityKey)
            .ToHashSet(StringComparer.Ordinal);
        return mounted.Where(lens => primaryKeys.Contains(PrimaryIdentityKey(lens)))
            .ToArray();
    }

    private static string PrimaryIdentityKey(LensfunLens lens) =>
        lens.VariantModelKey ?? lens.ModelKey;

    private static bool MakerMatches(string databaseMaker, string suppliedMaker) =>
        databaseMaker.StartsWith(suppliedMaker, StringComparison.Ordinal) ||
        suppliedMaker.StartsWith(databaseMaker, StringComparison.Ordinal);

    private static bool ModelMatches(string databaseModel, string maker,
        string suppliedModel) =>
        databaseModel == suppliedModel ||
        databaseModel == maker + suppliedModel ||
        maker + databaseModel == suppliedModel;

    private static bool LensModelMatches(LensfunLens lens, string suppliedModel) =>
        ModelMatches(lens.ModelKey, lens.MakerKey, suppliedModel) ||
        lens.VariantModelKey is { } variant &&
        ModelMatches(variant, lens.MakerKey, suppliedModel);

    private static bool ModelIdentityMatches(LensfunModelIdentity identity, string maker,
        string suppliedModel) =>
        ModelMatches(identity.Key, maker, suppliedModel) ||
        identity.VariantKey is { } variant &&
        ModelMatches(variant, maker, suppliedModel);

    private static bool LensTokenModelMatches(
        LensfunLens lens, IReadOnlySet<string> suppliedModel) =>
        TokenModelMatches(lens.ModelTokens, lens.MakerTokens, suppliedModel) ||
        lens.VariantModelTokens is { } variant &&
        TokenModelMatches(variant, lens.MakerTokens, suppliedModel);

    private static bool TokenIdentityMatches(LensfunModelIdentity identity,
        IReadOnlySet<string> maker, IReadOnlySet<string> suppliedModel) =>
        TokenModelMatches(identity.Tokens, maker, suppliedModel) ||
        identity.VariantTokens is { } variant &&
        TokenModelMatches(variant, maker, suppliedModel);

    private static bool TokenModelMatches(IReadOnlySet<string> databaseModel,
        IReadOnlySet<string> maker, IReadOnlySet<string> suppliedModel) =>
        databaseModel.SetEquals(suppliedModel) ||
        UnionSetEquals(databaseModel, maker, suppliedModel) ||
        UnionSetEquals(suppliedModel, maker, databaseModel);

    private static bool UnionSetEquals(
        IReadOnlySet<string> first, IReadOnlySet<string> second,
        IReadOnlySet<string> expected) =>
        first.All(expected.Contains) && second.All(expected.Contains) &&
        expected.All(value => first.Contains(value) || second.Contains(value));

    private static LensfunLens? SelectLensCalibration(IReadOnlyList<LensfunLens> matches,
        double cameraCrop)
    {
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0 || matches.Select(item =>
            PrimaryIdentityKey(item)).Distinct().Count() != 1)
            return null;
        var ranked = matches.Select(item => new
            {
                Lens = item,
                Distance = Math.Abs(Math.Log((item.CropFactor ?? cameraCrop) / cameraCrop))
            })
            .OrderBy(item => item.Distance)
            .ToArray();
        return ranked.Length > 1 && Math.Abs(ranked[0].Distance - ranked[1].Distance) < 1e-12
            ? null
            : ranked[0].Lens;
    }
}
