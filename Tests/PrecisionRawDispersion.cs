using System.Globalization;
using System.Text;

namespace HappyPhoton.Tests;

internal sealed class PrecisionRawDispersion
{
    private readonly List<Row> _rows = [];

    public void Add(string settings, long negativeClips, long channelSamples) =>
        _rows.Add(new Row(settings, negativeClips, channelSamples));

    public void Append(StringBuilder payload, string asset, string population)
    {
        var minimum = _rows.MinBy(row => row.Rate)!;
        var maximum = _rows.MaxBy(row => row.Rate)!;
        var asShot = _rows.Single(row => row.Settings == "as-shot");
        payload.Append("CENSUS_RAW_DISPERSION case=case-5-real-raw")
            .Append(" population=").Append(population)
            .Append(" asset=").Append(asset)
            .Append(" minimumSettings=").Append(minimum.Settings)
            .Append(" minimumNegativeChannelRate=").Append(Format(minimum.Rate))
            .Append(" maximumSettings=").Append(maximum.Settings)
            .Append(" maximumNegativeChannelRate=").Append(Format(maximum.Rate))
            .Append(" asShotNegativeChannelRate=").Append(Format(asShot.Rate))
            .Append(" basis=exact-full-population")
            .AppendLine();
    }

    private static string Format(double value) =>
        value.ToString("F12", CultureInfo.InvariantCulture);

    private sealed record Row(
        string Settings,
        long NegativeClips,
        long ChannelSamples)
    {
        public double Rate => NegativeClips / (double)ChannelSamples;
    }
}
