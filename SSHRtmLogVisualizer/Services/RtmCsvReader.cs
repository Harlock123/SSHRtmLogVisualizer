using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SSHRtmLogVisualizer.Models;

namespace SSHRtmLogVisualizer.Services;

/// <summary>
/// Reads a CSV file into chartable columns. It works out the layout from the file itself:
/// <list type="bullet">
/// <item>the separator (comma, semicolon, tab or pipe, or an Excel <c>sep=</c> line);</item>
/// <item>whether row 1 is a header;</item>
/// <item>whether numbers use a decimal comma;</item>
/// <item>which column holds the times, wherever it is.</item>
/// </list>
/// Columns that are mostly numbers become measures. The rest (hostnames, process names, comments)
/// can label the X axis but can't be charted. An SSHRTM dump, with its timestamp in the last column,
/// is one case of this.
/// </summary>
public static partial class RtmCsvReader
{
    /// <summary>Time layouts tried first, most specific first: SSHRTM's own, then common ones.</summary>
    private static readonly string[] TimestampFormats =
    {
        "yyyyMMdd-HHmmss",
        "yyyyMMdd-HHmmss.fff",
        "yyyyMMdd_HHmmss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy",
    };

    /// <summary>Field texts that mean "no sample" rather than a real number.</summary>
    private static readonly HashSet<string> MissingValueTokens =
        new(StringComparer.OrdinalIgnoreCase) { "", "-", "--", "n/a", "na", "null", "nan", "none" };

    /// <summary>The separators a file may use, in the order they win a tie.</summary>
    private static readonly char[] Delimiters = { ',', ';', '\t', '|' };

    /// <summary>Numbers written with a decimal comma and dot thousands: 1.234,5.</summary>
    private static readonly NumberFormatInfo CommaDecimal = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    /// <summary>A column is a measure when at least this share of its filled-in cells are numbers.</summary>
    public const double NumericShare = 0.9;

    /// <summary>A column holds times when at least this share of its filled-in cells read as dates or times.</summary>
    public const double TimeShare = 0.8;

    public static RtmLogFile Read(string filePath)
    {
        // FileShare.ReadWrite so a file another program is still appending to (or has open) can be charted.
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), filePath);
    }

    /// <summary>Reads CSV text; <paramref name="filePath"/> is only recorded on the result.</summary>
    public static RtmLogFile Parse(string text, string filePath = "")
    {
        int start = SkipPreamble(text, out char? declared);
        char delimiter = declared ?? DetectDelimiter(text, start);

        var records = new List<List<string>>();
        using (var reader = new StringReader(text[start..]))
        {
            List<string>? record;
            while ((record = ReadRecord(reader, delimiter)) is not null)
            {
                // Skip blank lines, wherever they are.
                if (record.All(f => f.Trim().Length == 0)) continue;
                records.Add(record);
            }
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("The file is empty: there is nothing to chart.");
        }

        bool decimalComma = delimiter != ',' && UsesDecimalComma(records);
        NumberFormatInfo numbers = decimalComma ? CommaDecimal : NumberFormatInfo.InvariantInfo;

        bool hasHeader = IsHeader(records, numbers);
        int width = records.Max(r => r.Count);
        string[] names = hasHeader
            ? MakeUniqueNames(records[0], width)
            : Enumerable.Range(1, width).Select(i => $"Column {i}").ToArray();

        List<string[]> rows = records.Skip(hasHeader ? 1 : 0)
            .Select(r => Enumerable.Range(0, width).Select(i => i < r.Count ? r[i].Trim() : string.Empty).ToArray())
            .ToList();

        if (rows.Count == 0)
        {
            throw new InvalidDataException("The file has a header row but no data rows to chart.");
        }

        var metrics = new List<MetricColumn>();
        var timeColumns = new List<(LabelColumn Column, int Index)>();
        var textColumns = new List<LabelColumn>();

        for (int c = 0; c < width; c++)
        {
            string[] cells = rows.Select(r => r[c]).ToArray();

            if (TryReadTimeColumn(names[c], cells, numbers) is { } times)
            {
                timeColumns.Add((new LabelColumn(names[c], cells, times, isTime: true), c));
            }
            else if (TryReadNumericColumn(cells, numbers) is { } values)
            {
                metrics.Add(new MetricColumn(names[c], c, values));
            }
            else
            {
                textColumns.Add(new LabelColumn(names[c], cells, new DateTime?[cells.Length], isTime: false));
            }
        }

        LabelColumn? timeColumn = PickTimeColumn(timeColumns, width);

        string? hostName = null;
        for (int c = 0; c < width; c++)
        {
            if (names[c].Equals("hostname", StringComparison.OrdinalIgnoreCase))
            {
                hostName = rows[0][c];
                break;
            }
        }

        return new RtmLogFile(
            filePath,
            hostName,
            rows.Count,
            metrics,
            timeColumns.Select(t => t.Column).Concat(textColumns).ToList(),
            timeColumn,
            textColumns.Count,
            new CsvFormat(delimiter, hasHeader, decimalComma));
    }

    /// <summary>
    /// True when row 1 names the columns rather than holding data. It does when some column has
    /// text in row 1 but a number or time in row 2; with nothing to compare against, when every
    /// filled-in cell in row 1 is text.
    /// </summary>
    private static bool IsHeader(List<List<string>> records, NumberFormatInfo numbers)
    {
        bool IsValue(string cell) => TryParseNumber(cell, numbers, out _) || ParseTimestamp(cell.Trim(), numbers) is not null;
        bool IsText(string cell) => cell.Trim().Length > 0 && !MissingValueTokens.Contains(cell.Trim()) && !IsValue(cell);

        List<string> first = records[0];
        if (records.Count > 1)
        {
            List<string> second = records[1];
            for (int c = 0; c < Math.Min(first.Count, second.Count); c++)
            {
                if (IsText(first[c]) && IsValue(second[c])) return true;
            }

            // Every column reads the same in both rows: a header only if row 1 has no numbers or times at all.
            if (first.Any(IsValue)) return false;
        }

        return first.Any(IsText) && !first.Any(IsValue);
    }

    /// <summary>
    /// The time column to label the X axis with: one named like a time (time, date, timestamp…),
    /// else the last column when it holds times (SSHRTM's layout), else the first time column.
    /// </summary>
    private static LabelColumn? PickTimeColumn(List<(LabelColumn Column, int Index)> timeColumns, int width)
    {
        if (timeColumns.Count == 0) return null;
        return timeColumns.FirstOrDefault(t => TimeName().IsMatch(t.Column.Name)).Column
               ?? timeColumns.FirstOrDefault(t => t.Index == width - 1).Column
               ?? timeColumns[0].Column;
    }

    [GeneratedRegex(@"time|date|stamp|^ts$|^when$|^day$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeName();

    /// <summary>
    /// The column's times, or null when it isn't a time column. A column is one when most of its
    /// filled-in cells read as dates or times, or when it is named like a time and holds Unix
    /// timestamps (seconds or milliseconds since 1970).
    /// </summary>
    private static DateTime?[]? TryReadTimeColumn(string name, string[] cells, NumberFormatInfo numbers)
    {
        var times = new DateTime?[cells.Length];
        int filled = 0, parsed = 0;
        // Most columns are numbers or text: stop once too many cells have failed to reach TimeShare.
        double allowedMisses = cells.Length * (1 - TimeShare);

        for (int r = 0; r < cells.Length; r++)
        {
            if (MissingValueTokens.Contains(cells[r])) continue;
            filled++;
            if (ParseTimestamp(cells[r], numbers) is { } t)
            {
                times[r] = t;
                parsed++;
            }
            else if (filled - parsed > allowedMisses)
            {
                filled = -1;
                break;
            }
        }

        if (filled > 0 && parsed >= filled * TimeShare) return times;
        if (filled < 0) filled = cells.Count(c => !MissingValueTokens.Contains(c));

        if (filled > 0 && TimeName().IsMatch(name) && cells.All(c => MissingValueTokens.Contains(c) || UnixTime(c) is not null))
        {
            return cells.Select(UnixTime).ToArray();
        }

        return null;
    }

    /// <summary>Seconds (10 digits) or milliseconds (13 digits) since 1970, between 2001 and 2100.</summary>
    private static DateTime? UnixTime(string cell)
    {
        if (!long.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out long n)) return null;
        DateTime? t = cell.Length switch
        {
            10 => DateTime.UnixEpoch.AddSeconds(n),
            13 => DateTime.UnixEpoch.AddMilliseconds(n),
            _ => null,
        };
        return t is { Year: > 2000 and < 2100 } ? t : null;
    }

    /// <summary>
    /// Returns the column's values when at least <see cref="NumericShare"/> of its filled-in cells
    /// are numbers, or null when it is text. Cells that aren't numbers (an "ERROR" in a column
    /// of readings) become gaps.
    /// </summary>
    private static double[]? TryReadNumericColumn(string[] cells, NumberFormatInfo numbers)
    {
        var values = new double[cells.Length];
        int filled = 0, parsed = 0;

        for (int r = 0; r < cells.Length; r++)
        {
            values[r] = double.NaN;
            if (MissingValueTokens.Contains(cells[r])) continue;
            filled++;
            if (TryParseNumber(cells[r], numbers, out double v))
            {
                values[r] = v;
                parsed++;
            }
        }

        return parsed > 0 && parsed >= filled * NumericShare ? values : null;
    }

    /// <summary>
    /// Reads a number the way spreadsheets and logs write them: thousands separators, a trailing
    /// %, or a leading currency sign are allowed ("1,234.5", "45%", "$12.50").
    /// </summary>
    public static bool TryParseNumber(string text, NumberFormatInfo numbers, out double value)
    {
        string s = text.Trim();
        if (s.EndsWith('%')) s = s[..^1].TrimEnd();
        if (s.Length > 0 && "$€£¥".Contains(s[0])) s = s[1..].TrimStart();
        else if (s.Length > 1 && s[0] == '-' && "$€£¥".Contains(s[1])) s = "-" + s[2..];
        // Spaces (plain, no-break and narrow) are thousands separators in some locales: 1 234,5.
        s = s.Replace(" ", "").Replace(" ", "").Replace(" ", "");

        // .NET accepts thousands separators anywhere, which would read the date 03.11.2026 as a
        // number in a decimal-comma file. Only take them in groups of three: 1.234.567,5.
        string group = numbers.NumberGroupSeparator;
        int point = s.IndexOf(numbers.NumberDecimalSeparator, StringComparison.Ordinal);
        string whole = point < 0 ? s : s[..point];
        if (whole.Contains(group, StringComparison.Ordinal))
        {
            string[] groups = whole.TrimStart('-', '+').Split(group);
            if (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(g => g.Length != 3))
            {
                value = 0;
                return false;
            }
        }

        return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, numbers, out value)
               && double.IsFinite(value);
    }

    /// <summary>A date or time, or null. Plain numbers are never times ("12.5" is not 5 December).</summary>
    private static DateTime? ParseTimestamp(string raw, NumberFormatInfo numbers)
    {
        if (raw.Length == 0 || TryParseNumber(raw, numbers, out _))
        {
            return null;
        }

        if (DateTime.TryParseExact(raw, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime exact))
        {
            return exact;
        }

        // ISO 8601 with an offset or Z, and the invariant culture's other forms (MM/dd/yyyy and so on).
        // The clock time is kept as written rather than converted to this machine's zone.
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset loose))
        {
            return loose.DateTime;
        }

        return null;
    }

    /// <summary>
    /// Index where the CSV proper starts, after an Excel "sep=;" line (which names the separator)
    /// and any comment lines starting with '#'.
    /// </summary>
    private static int SkipPreamble(string text, out char? declared)
    {
        declared = null;
        int at = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        while (at < text.Length)
        {
            int end = text.IndexOf('\n', at);
            string line = (end < 0 ? text[at..] : text[at..end]).TrimEnd('\r');
            string trimmed = line.Trim();

            if (declared is null && trimmed.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 5)
            {
                declared = trimmed[4];
            }
            else if (!trimmed.StartsWith('#') && trimmed.Length > 0)
            {
                return at;
            }

            if (end < 0) return text.Length;
            at = end + 1;
        }

        return at;
    }

    /// <summary>
    /// The separator that splits the first lines most consistently: the one with the most fields
    /// on every line. A file with one column has none, and reads as comma-separated.
    /// </summary>
    private static char DetectDelimiter(string text, int start)
    {
        var lines = new List<string>();
        using (var reader = new StringReader(text[start..]))
        {
            string? line;
            while (lines.Count < 20 && (line = reader.ReadLine()) is not null)
            {
                if (line.Trim().Length > 0) lines.Add(line);
            }
        }

        char best = ',';
        int bestCount = 0;
        foreach (char d in Delimiters)
        {
            int[] counts = lines.Select(l => CountOutsideQuotes(l, d)).ToArray();
            int least = counts.Length == 0 ? 0 : counts.Min();
            if (least > bestCount)
            {
                best = d;
                bestCount = least;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        int count = 0;
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == delimiter && !quoted) count++;
        }

        return count;
    }

    /// <summary>
    /// True when numbers in the file are written with a decimal comma (12,5) rather than a point
    /// (12.5). Only asked of files not separated by commas, where a comma can't split fields.
    /// </summary>
    private static bool UsesDecimalComma(List<List<string>> records)
    {
        int comma = 0, point = 0;
        foreach (string cell in records.Take(200).SelectMany(r => r))
        {
            string s = cell.Trim().TrimEnd('%');
            if (DecimalCommaNumber().IsMatch(s)) comma++;
            else if (DecimalPointNumber().IsMatch(s)) point++;
        }

        return comma > point;
    }

    [GeneratedRegex(@"^[-+]?\d{1,3}(\.\d{3})*,\d+$|^[-+]?\d+,\d+$")]
    private static partial Regex DecimalCommaNumber();

    [GeneratedRegex(@"^[-+]?\d+\.\d+$")]
    private static partial Regex DecimalPointNumber();

    /// <summary>
    /// Trims header cells and disambiguates blank or repeated names so every entry in the
    /// dropdown identifies exactly one column. Rows wider than the header get "Column n" names.
    /// </summary>
    private static string[] MakeUniqueNames(List<string> header, int width)
    {
        var names = new string[width];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < width; i++)
        {
            string name = i < header.Count ? header[i].Trim() : string.Empty;
            if (name.Length == 0)
            {
                name = $"Column {i + 1}";
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
    /// Reads one CSV record, honouring quoted fields (including embedded separators, quotes
    /// and newlines). Returns null at end of file.
    /// </summary>
    private static List<string>? ReadRecord(TextReader reader, char delimiter)
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

            if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
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
