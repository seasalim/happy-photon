namespace HappyPhoton.Models;

internal static class RepairOrientation
{
    internal static Repair Map(Repair repair, int from, int to)
    {
        var inverse = from == 6 ? 8 : from == 8 ? 6 : from;
        var destination = Apply(inverse, repair.U, repair.V);
        var source = Apply(inverse, repair.Su, repair.Sv);
        destination = Apply(to, destination.U, destination.V);
        source = Apply(to, source.U, source.V);

        return repair with { U = destination.U, V = destination.V, Su = source.U, Sv = source.V };
    }

    internal static int Compose(int first, int second)
    {
        var origin = Apply(first, 0, 0);
        var corner = Apply(first, 1, 0);
        origin = Apply(second, origin.U, origin.V);
        corner = Apply(second, corner.U, corner.V);

        return Enumerable.Range(1, 8).Single(orientation =>
            Apply(orientation, 0, 0) == origin && Apply(orientation, 1, 0) == corner);
    }

    // Matches ImageServiceHelpers.ApplyExifOrientation's rotate/flip operations.
    private static (double U, double V) Apply(int orientation, double u, double v) => orientation switch
    {
        2 => (1 - u, v),
        3 => (1 - u, 1 - v),
        4 => (u, 1 - v),
        5 => (v, u),
        6 => (1 - v, u),
        7 => (1 - v, 1 - u),
        8 => (v, 1 - u),
        _ => (u, v)
    };
}
