using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

/// <summary>
/// Represents a point on the curve (0-1 range for both axes).
/// </summary>
public struct CurvePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    public CurvePoint(double x, double y)
    {
        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
    }
}

/// <summary>
/// Holds curve data with control points for tonal adjustments.
/// Points are in 0-1 range, interpolated to create a lookup table.
/// </summary>
public class CurveData
{
    [JsonPropertyName("points")]
    public List<CurvePoint> Points { get; set; } = new()
    {
        new CurvePoint(0, 0),
        new CurvePoint(1, 1)
    };

    /// <summary>
    /// Lookup table (256 entries) mapping input to output values.
    /// </summary>
    [JsonIgnore]
    public byte[] LookupTable { get; private set; } = new byte[256];

    public CurveData()
    {
        BuildLookupTable();
    }

    public void Reset()
    {
        Points.Clear();
        Points.Add(new CurvePoint(0, 0));
        Points.Add(new CurvePoint(1, 1));
        BuildLookupTable();
    }

    /// <summary>
    /// Adds a point and returns the index where it was inserted.
    /// </summary>
    public int AddPointAndReturnIndex(double x, double y)
    {
        var point = new CurvePoint(x, y);

        // Find insertion position to keep points sorted by X
        int insertIndex = 0;
        for (int i = 0; i < Points.Count; i++)
        {
            if (Points[i].X > x)
            {
                insertIndex = i;
                break;
            }
            insertIndex = i + 1;
        }

        Points.Insert(insertIndex, point);
        BuildLookupTable();
        return insertIndex;
    }

    /// <summary>
    /// Gets the interpolated curve value at a given X position (0-1).
    /// </summary>
    public double GetValueAt(double x)
    {
        return InterpolateMonotonic(Math.Clamp(x, 0, 1));
    }

    public void MovePoint(int index, double x, double y)
    {
        if (index < 0 || index >= Points.Count) return;

        // Constrain X to stay within neighbors (prevents reordering during drag)
        double minX = index > 0 ? Points[index - 1].X + 0.001 : 0;
        double maxX = index < Points.Count - 1 ? Points[index + 1].X - 0.001 : 1;

        // Endpoints have fixed X
        if (index == 0) x = 0;
        else if (index == Points.Count - 1) x = 1;
        else x = Math.Clamp(x, minX, maxX);

        Points[index] = new CurvePoint(x, y);
        BuildLookupTable();
    }

    public void RemovePoint(int index)
    {
        // Don't remove first or last point
        if (index <= 0 || index >= Points.Count - 1) return;

        Points.RemoveAt(index);
        BuildLookupTable();
    }

    public bool IsIdentity()
    {
        if (Points.Count != 2) return false;
        return Points[0].X == 0 && Points[0].Y == 0 &&
               Points[1].X == 1 && Points[1].Y == 1;
    }

    public void BuildLookupTable()
    {
        if (Points.Count < 2)
        {
            // Linear fallback
            for (int i = 0; i < 256; i++)
                LookupTable[i] = (byte)i;
            return;
        }

        // Use monotonic cubic interpolation for smooth curve
        for (int i = 0; i < 256; i++)
        {
            double x = i / 255.0;
            double y = InterpolateMonotonic(x);
            LookupTable[i] = (byte)Math.Clamp((int)(y * 255), 0, 255);
        }
    }

    private double InterpolateMonotonic(double x)
    {
        // Find the segment containing x
        int segmentIndex = 0;
        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (x >= Points[i].X && x <= Points[i + 1].X)
            {
                segmentIndex = i;
                break;
            }
            if (i == Points.Count - 2)
                segmentIndex = i;
        }

        // Get the four points for Catmull-Rom interpolation
        // P0 = point before segment, P1 = segment start, P2 = segment end, P3 = point after segment
        var p1 = Points[segmentIndex];
        var p2 = Points[segmentIndex + 1];

        if (Math.Abs(p2.X - p1.X) < 0.0001)
            return p1.Y;

        // For endpoints, mirror the adjacent point to maintain smooth tangent
        var p0 = segmentIndex > 0 ? Points[segmentIndex - 1] : new CurvePoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
        var p3 = segmentIndex < Points.Count - 2 ? Points[segmentIndex + 2] : new CurvePoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

        double t = (x - p1.X) / (p2.X - p1.X);

        // Catmull-Rom spline interpolation
        double t2 = t * t;
        double t3 = t2 * t;

        double y = 0.5 * (
            (2 * p1.Y) +
            (-p0.Y + p2.Y) * t +
            (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
            (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3
        );

        // Clamp to valid range
        return Math.Clamp(y, 0, 1);
    }

    public CurveData Clone()
    {
        var clone = new CurveData();
        clone.Points = Points.Select(p => new CurvePoint(p.X, p.Y)).ToList();
        clone.BuildLookupTable();
        return clone;
    }
}
