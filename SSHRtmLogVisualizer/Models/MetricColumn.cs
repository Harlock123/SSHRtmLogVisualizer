using System;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// One numeric measure from an SSHRTM CSV dump: the column header plus the value
/// recorded on every sample row. Missing or unparseable samples are <see cref="double.NaN"/>.
/// </summary>
public sealed class MetricColumn
{
    public MetricColumn(string name, int sourceColumnIndex, double[] values)
    {
        Name = name;
        SourceColumnIndex = sourceColumnIndex;
        Values = values;

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        int count = 0;
        foreach (double v in values)
        {
            if (double.IsNaN(v))
            {
                continue;
            }

            if (v < min) min = v;
            if (v > max) max = v;
            count++;
        }

        if (count == 0)
        {
            Minimum = double.NaN;
            Maximum = double.NaN;
        }
        else
        {
            Minimum = min;
            Maximum = max;
        }

        SampleCount = count;
    }

    /// <summary>Column header text, e.g. <c>cpu_overall_pct</c>.</summary>
    public string Name { get; }

    /// <summary>Zero-based index of this column in the source CSV.</summary>
    public int SourceColumnIndex { get; }

    /// <summary>One entry per data row, in file order. NaN marks a missing sample.</summary>
    public double[] Values { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>Number of rows that carried an actual value (NaN rows excluded).</summary>
    public int SampleCount { get; }

    /// <summary>True when every sample holds the same value, so normalizing would divide by zero.</summary>
    public bool IsFlat => double.IsNaN(Minimum) || Maximum - Minimum <= 0d;

    /// <summary>
    /// Tighter range for the chart legend, where a normalized series has to report the values
    /// it was squashed from without pushing the legend wide enough to clip.
    /// </summary>
    public string CompactRange =>
        SampleCount == 0 ? "no data" : $"{Format(Minimum)}–{Format(Maximum)}";

    /// <summary>Short "min … max" summary shown under the name in the visualize list.</summary>
    public string RangeSummary =>
        SampleCount == 0
            ? "no numeric samples"
            : $"min {Format(Minimum)}  ·  max {Format(Maximum)}";

    public override string ToString() => Name;

    /// <summary>
    /// Formats a value short enough to sit in a legend entry. Byte counts and bit rates run to
    /// eleven digits in these dumps, so anything past a thousand gets an SI suffix — "66.88G"
    /// rather than "6.688E+10".
    /// </summary>
    private static string Format(double value)
    {
        double magnitude = Math.Abs(value);

        return magnitude switch
        {
            >= 1e12 => (value / 1e12).ToString("0.##") + "T",
            >= 1e9 => (value / 1e9).ToString("0.##") + "G",
            >= 1e6 => (value / 1e6).ToString("0.##") + "M",
            >= 1e3 => (value / 1e3).ToString("0.##") + "K",
            0 => "0",
            < 0.001 => value.ToString("G3"),
            _ => value.ToString("0.###"),
        };
    }
}
