using System.Collections.Generic;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// A parsed CSV file: every chartable (numeric) column, the columns that can label the X axis,
/// and how the file was laid out. An SSHRTM dump is one of these with a timestamp in the last
/// column and a hostname column; any other CSV reads the same way.
/// </summary>
public sealed class RtmLogFile
{
    public RtmLogFile(
        string filePath,
        string? hostName,
        int rowCount,
        IReadOnlyList<MetricColumn> metrics,
        IReadOnlyList<LabelColumn> labelColumns,
        LabelColumn? timeColumn,
        int skippedColumnCount,
        CsvFormat format,
        IReadOnlyList<string>? servers = null)
    {
        Servers = servers ?? [];
        FilePath = filePath;
        HostName = hostName;
        RowCount = rowCount;
        Metrics = metrics;
        LabelColumns = labelColumns;
        TimeColumn = timeColumn;
        SkippedColumnCount = skippedColumnCount;
        Format = format;
    }

    public string FilePath { get; }

    /// <summary>
    /// The servers of an SSHRTM combined log, in column order (its columns are named <c>server::measure</c>);
    /// empty for any other file.
    /// </summary>
    public IReadOnlyList<string> Servers { get; }

    /// <summary>True for an SSHRTM combined log: several servers' measures side by side.</summary>
    public bool IsCombined => Servers.Count > 0;

    /// <summary>Value of the <c>hostname</c> column on the first row, when the file has one (SSHRTM dumps do).</summary>
    public string? HostName { get; }

    public int RowCount { get; }

    /// <summary>Numeric columns available for charting. Time columns are not included.</summary>
    public IReadOnlyList<MetricColumn> Metrics { get; }

    /// <summary>The columns that can label the X axis: time columns first, then the text columns, in file order.</summary>
    public IReadOnlyList<LabelColumn> LabelColumns { get; }

    /// <summary>The column picked to label the X axis when the file loads, or null to use row numbers.</summary>
    public LabelColumn? TimeColumn { get; }

    /// <summary>Count of columns that hold text rather than numbers, and so can't be charted.</summary>
    public int SkippedColumnCount { get; }

    public CsvFormat Format { get; }
}

/// <summary>How a CSV file was written: its field separator, whether row 1 is a header, and its decimal mark.</summary>
public sealed record CsvFormat(char Delimiter, bool HasHeader, bool DecimalComma)
{
    public static readonly CsvFormat Standard = new(',', true, false);

    /// <summary>"semicolon-separated, decimal comma" and so on, for the status bar.</summary>
    public string Description
    {
        get
        {
            string separator = Delimiter switch
            {
                ',' => "comma-separated",
                ';' => "semicolon-separated",
                '\t' => "tab-separated",
                '|' => "pipe-separated",
                _ => $"'{Delimiter}'-separated",
            };
            var parts = new List<string> { separator };
            if (DecimalComma) parts.Add("decimal comma");
            if (!HasHeader) parts.Add("no header row");
            return string.Join(", ", parts);
        }
    }
}
