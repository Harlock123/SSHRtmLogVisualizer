using System;
using System.Collections.Generic;
using System.Linq;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// A column that can label the X axis: a time column, any other text column, or the row
/// numbers themselves. Its labels sit under the ticks; when it holds times, the chart title
/// shows the span and the ticks are formatted to suit it.
/// </summary>
public sealed class LabelColumn
{
    public LabelColumn(string name, IReadOnlyList<string> labels, IReadOnlyList<DateTime?> timestamps, bool isTime)
    {
        Name = name;
        Labels = labels;
        Timestamps = timestamps;
        IsTime = isTime;
    }

    /// <summary>The "Row number" choice: labels 1, 2, 3 … and no times.</summary>
    public static LabelColumn RowNumbers(int rowCount) => new(
        RowNumberName,
        Enumerable.Range(1, rowCount).Select(i => i.ToString("N0")).ToArray(),
        new DateTime?[rowCount],
        isTime: false);

    public const string RowNumberName = "Row number";

    public string Name { get; }

    /// <summary>The column's text, one entry per row.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>The parsed times; null where a row's text is not a time (or for a text column).</summary>
    public IReadOnlyList<DateTime?> Timestamps { get; }

    /// <summary>True when most of the column's values read as dates or times.</summary>
    public bool IsTime { get; }

    public bool IsRowNumbers => Name == RowNumberName && !IsTime;

    /// <summary>What the X axis picker shows.</summary>
    public string Caption => IsRowNumbers ? Name : IsTime ? $"{Name}  (time)" : Name;

    /// <summary>True when the times do not all fall on one calendar day.</summary>
    public bool SpansMultipleDays
    {
        get
        {
            DateTime? first = null;
            foreach (DateTime? t in Timestamps)
            {
                if (t is null) continue;
                first ??= t.Value.Date;
                if (t.Value.Date != first.Value) return true;
            }

            return false;
        }
    }

    /// <summary>True when any time carries a time of day; false for dates alone (daily data).</summary>
    public bool HasTimeOfDay => Timestamps.Any(t => t is { } v && v.TimeOfDay != TimeSpan.Zero);

    public override string ToString() => Caption;
}
