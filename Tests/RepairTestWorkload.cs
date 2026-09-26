using HappyPhoton.Models;

namespace HappyPhoton.Tests;

// Shared with headless flow tests. RepairModelTests pins it to WP1's frozen S64.
internal static class RepairTestWorkload
{
    internal static List<Repair> S64() => Enumerable.Range(0, 64).Select(i => new Repair
    {
        Id = (i + 1).ToString("x32"), Type = i % 4 == 3 ? "clone" : "heal",
        U = Q(.14 + (i % 8) * .095 - (i % 8 == 1 ? .065 : 0)),
        V = Q(.18 + (i / 8) * .085), Su = Q(i % 4 == 0 ? .08 : .85 - (i % 7) * .08),
        Sv = Q(.10 + (i % 9) * .085), Radius = .005 + (i % 8) * .005
    }).ToList();

    private static double Q(double value) => Math.Round(value * 16384) / 16384;
}
