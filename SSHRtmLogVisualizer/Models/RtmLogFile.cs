using System;
using System.Collections.Generic;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// A parsed SSHRTM CSV dump: the sample timestamps plus every chartable (numeric) measure.
/// </summary>
public sealed class RtmLogFile
{
    public RtmLogFile(
        string filePath,
        string? hostName,
        IReadOnlyList<string> timestampLabels,
        IReadOnlyList<DateTime?> timestamps,
        IReadOnlyList<MetricColumn> metrics,
        int skippedColumnCount)
    {
        FilePath = filePath;
        HostName = hostName;
        TimestampLabels = timestampLabels;
        Timestamps = timestamps;
        Metrics = metrics;
        SkippedColumnCount = skippedColumnCount;
    }

    public string FilePath { get; }

    /// <summary>Value of the <c>hostname</c> column on the first row, when the dump has one.</summary>
    public string? HostName { get; }

    /// <summary>Raw text of the trailing timestamp column, one entry per row.</summary>
    public IReadOnlyList<string> TimestampLabels { get; }

    /// <summary>Parsed timestamps; null where the raw text did not match a known format.</summary>
    public IReadOnlyList<DateTime?> Timestamps { get; }

    /// <summary>Numeric measures available for charting. The timestamp column is not included.</summary>
    public IReadOnlyList<MetricColumn> Metrics { get; }

    /// <summary>Count of non-timestamp columns dropped because they hold text, not numbers.</summary>
    public int SkippedColumnCount { get; }

    public int RowCount => TimestampLabels.Count;

    /// <summary>True when the dump's samples do not all fall on the same calendar day.</summary>
    public bool SpansMultipleDays
    {
        get
        {
            DateTime? first = null;
            foreach (DateTime? t in Timestamps)
            {
                if (t is null)
                {
                    continue;
                }

                if (first is null)
                {
                    first = t.Value.Date;
                }
                else if (t.Value.Date != first.Value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
