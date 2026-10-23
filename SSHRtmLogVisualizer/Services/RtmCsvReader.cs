using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SSHRtmLogVisualizer.Models;

namespace SSHRtmLogVisualizer.Services;

/// <summary>
/// Reads an SSHRTM CSV dump. Row 1 holds the measure names, the last column is the
/// sample timestamp, and every other column is a measure. Columns whose samples are
/// text rather than numbers (hostname, filesystem types, process names) cannot be
/// charted and are dropped.
/// </summary>
public static class RtmCsvReader
{
    /// <summary>Timestamp layouts emitted by SSHRTM, most specific first.</summary>
    private static readonly string[] TimestampFormats =
    {
        "yyyyMMdd-HHmmss",
        "yyyyMMdd-HHmmss.fff",
        "yyyyMMdd_HHmmss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
    };

    /// <summary>Field texts that mean "no sample" rather than a real number.</summary>
    private static readonly HashSet<string> MissingValueTokens =
        new(StringComparer.OrdinalIgnoreCase) { "", "-", "--", "n/a", "na", "null", "nan" };

    public static RtmLogFile Read(string filePath)
    {
        // FileShare.ReadWrite so a dump that SSHRTM is still appending to (or that another
        // viewer has open) can still be charted.
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        List<string>? header = ReadRecord(reader);
        if (header is null || header.Count < 2)
        {
            throw new InvalidDataException(
                "The file does not look like an SSHRTM dump: the first row must list at least one measure plus the trailing timestamp column.");
        }

        string[] names = MakeUniqueNames(header);
        int timestampIndex = names.Length - 1;

        var rows = new List<string[]>();
        List<string>? record;
        while ((record = ReadRecord(reader)) is not null)
        {
            // Skip blank trailing lines.
            if (record.Count == 1 && record[0].Length == 0)
            {
                continue;
            }

            var row = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                row[i] = i < record.Count ? record[i] : string.Empty;
            }

            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("The file has a header row but no data rows to chart.");
        }

        var timestampLabels = new string[rows.Count];
        var timestamps = new DateTime?[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            string raw = rows[r][timestampIndex].Trim();
            timestampLabels[r] = raw;
            timestamps[r] = ParseTimestamp(raw);
        }

        var metrics = new List<MetricColumn>();
        int skipped = 0;
        for (int c = 0; c < timestampIndex; c++)
        {
            double[]? values = TryReadNumericColumn(rows, c);
            if (values is null)
            {
                skipped++;
                continue;
            }

            metrics.Add(new MetricColumn(names[c], c, values));
        }

        string? hostName = null;
        for (int c = 0; c < timestampIndex; c++)
        {
            if (names[c].Equals("hostname", StringComparison.OrdinalIgnoreCase))
            {
                hostName = rows[0][c].Trim();
                break;
            }
        }

        return new RtmLogFile(filePath, hostName, timestampLabels, timestamps, metrics, skipped);
    }

    /// <summary>
    /// Returns the column's values, or null when any populated sample is not a number
    /// (which means the column is descriptive text and cannot be plotted).
    /// </summary>
    private static double[]? TryReadNumericColumn(List<string[]> rows, int columnIndex)
    {
        var values = new double[rows.Count];
        bool sawNumber = false;

        for (int r = 0; r < rows.Count; r++)
        {
            string raw = rows[r][columnIndex].Trim();
            if (MissingValueTokens.Contains(raw))
            {
                values[r] = double.NaN;
                continue;
            }

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return null;
            }

            values[r] = parsed;
            sawNumber = true;
        }

        return sawNumber ? values : null;
    }

    private static DateTime? ParseTimestamp(string raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        if (DateTime.TryParseExact(raw, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime exact))
        {
            return exact;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime loose))
        {
            return loose;
        }

        return null;
    }

    /// <summary>
    /// Trims header cells and disambiguates blank or repeated names so every entry in the
    /// dropdown identifies exactly one column.
    /// </summary>
    private static string[] MakeUniqueNames(List<string> header)
    {
        var names = new string[header.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < header.Count; i++)
        {
            string name = header[i].Trim();
            if (name.Length == 0)
            {
                name = $"column_{i + 1}";
            }

            if (seen.TryGetValue(name, out int previous))
            {
                int next = previous + 1;
                seen[name] = next;
                name = $"{name} ({next})";
            }
            else
            {
                seen[name] = 1;
            }

            names[i] = name;
        }

        return names;
    }

    /// <summary>
    /// Reads one CSV record, honouring quoted fields (including embedded commas, quotes
    /// and newlines). Returns null at end of file.
    /// </summary>
    private static List<string>? ReadRecord(TextReader reader)
    {
        if (reader.Peek() < 0)
        {
            return null;
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        while (true)
        {
            int read = reader.Read();
            if (read < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            char c = (char)read;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }

                    fields.Add(field.ToString());
                    return fields;
                case '\n':
                    fields.Add(field.ToString());
                    return fields;
                default:
                    field.Append(c);
                    break;
            }
        }
    }
}
