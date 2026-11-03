using System.Globalization;
using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;

namespace SSHRtmLogVisualizer.Tests;

/// <summary>
/// Reading CSV files of every common shape: SSHRTM dumps, other separators, decimal commas, no
/// header, time columns anywhere (or none), stray text in number columns, and the edge cases.
/// </summary>
public class CsvReaderTests
{
    private static MetricColumn Metric(RtmLogFile file, string name) => Assert.Single(file.Metrics, m => m.Name == name);

    [Fact]
    public void Reads_an_sshrtm_dump_as_before()
    {
        var file = RtmCsvReader.Parse("""
            hostname,cpu_overall_pct,mem_used_pct,fs_root_type,timestamp
            web01,12.5,40,ext4,20260827-150110
            web01,n/a,41,ext4,20260827-150120
            web01,14,42,ext4,20260827-150130
            """);

        Assert.Equal("web01", file.HostName);
        Assert.Equal(["cpu_overall_pct", "mem_used_pct"], file.Metrics.Select(m => m.Name));
        Assert.True(double.IsNaN(Metric(file, "cpu_overall_pct").Values[1]));
        Assert.Equal("timestamp", file.TimeColumn!.Name);
        Assert.Equal(new DateTime(2026, 8, 27, 15, 1, 20), file.TimeColumn.Timestamps[1]);
        Assert.Equal(2, file.SkippedColumnCount);
        Assert.Equal(CsvFormat.Standard, file.Format);
        Assert.Equal(["timestamp", "hostname", "fs_root_type"], file.LabelColumns.Select(c => c.Name));
    }

    [Fact]
    public void Reads_semicolons_and_decimal_commas()
    {
        var file = RtmCsvReader.Parse("""
            Datum;Temperatur;Umsatz
            03.11.2026;12,5;1.234,50
            04.11.2026;-3,25;980,00
            """);

        Assert.Equal(new CsvFormat(';', true, true), file.Format);
        Assert.Equal([12.5, -3.25], Metric(file, "Temperatur").Values);
        Assert.Equal([1234.5, 980.0], Metric(file, "Umsatz").Values);
        Assert.Equal("Datum", file.TimeColumn!.Name);
        Assert.Equal(new DateTime(2026, 11, 4), file.TimeColumn.Timestamps[1]);
        Assert.Equal("semicolon-separated, decimal comma", file.Format.Description);
    }

    [Fact]
    public void An_excel_sep_line_names_the_separator()
    {
        var file = RtmCsvReader.Parse("sep=|\r\nA|B\r\n1,5|2\r\n");

        Assert.Equal('|', file.Format.Delimiter);
        Assert.Equal(["A", "B"], file.Metrics.Select(m => m.Name));
    }

    [Theory]
    [InlineData("\t", '\t')]
    [InlineData("|", '|')]
    [InlineData(";", ';')]
    [InlineData(",", ',')]
    public void Finds_the_separator(string separator, char expected)
    {
        var file = RtmCsvReader.Parse(string.Join("\n", "when,a,b", "2026-11-01,1,2", "2026-11-02,3,4").Replace(",", separator));

        Assert.Equal(expected, file.Format.Delimiter);
        Assert.Equal(["a", "b"], file.Metrics.Select(m => m.Name));
        Assert.Equal("when", file.TimeColumn!.Name);
    }

    [Fact]
    public void Quoted_fields_keep_their_separators_and_numbers_their_thousands()
    {
        var file = RtmCsvReader.Parse("""
            name,"sales, total",share
            "Smith, J","1,234.50",45%
            "Doe, A","$2,000",5.5 %
            """);

        Assert.Equal(',', file.Format.Delimiter);
        Assert.Equal([1234.5, 2000.0], Metric(file, "sales, total").Values);
        Assert.Equal([45.0, 5.5], Metric(file, "share").Values);
        Assert.Equal(["Smith, J", "Doe, A"], Assert.Single(file.LabelColumns).Labels);
    }

    [Fact]
    public void A_file_with_no_header_gets_numbered_columns()
    {
        var file = RtmCsvReader.Parse("1,2.5,3\n4,5.5,6\n");

        Assert.False(file.Format.HasHeader);
        Assert.Equal(2, file.RowCount);
        Assert.Equal(["Column 1", "Column 2", "Column 3"], file.Metrics.Select(m => m.Name));
        Assert.Null(file.TimeColumn);
    }

    [Fact]
    public void A_file_with_no_header_can_start_with_a_time()
    {
        var file = RtmCsvReader.Parse("2026-11-03 10:00:00,1\n2026-11-03 10:01:00,2\n");

        Assert.False(file.Format.HasHeader);
        Assert.Equal(2, file.RowCount);
        Assert.Equal("Column 1", file.TimeColumn!.Name);
        Assert.Equal(["Column 2"], file.Metrics.Select(m => m.Name));
    }

    [Fact]
    public void A_header_of_text_over_rows_of_text_is_still_a_header()
    {
        var file = RtmCsvReader.Parse("host,state\nweb01,up\nweb02,down\n");

        Assert.True(file.Format.HasHeader);
        Assert.Equal(["host", "state"], file.LabelColumns.Select(c => c.Name));
        Assert.Empty(file.Metrics);
    }

    [Fact]
    public void A_time_column_named_like_one_wins_over_the_last_column()
    {
        var file = RtmCsvReader.Parse("""
            Date,Close,Updated
            2026-10-30,101.5,2026-11-03 09:00
            2026-10-31,102.25,2026-11-03 09:00
            """);

        Assert.Equal("Date", file.TimeColumn!.Name);
        Assert.False(file.TimeColumn.HasTimeOfDay);
        Assert.Equal(["Date", "Updated"], file.LabelColumns.Where(c => c.IsTime).Select(c => c.Name));
    }

    [Fact]
    public void Otherwise_the_last_column_then_the_first_holds_the_times()
    {
        Assert.Equal("b", RtmCsvReader.Parse("a,v,b\n2026-01-01,1,2026-02-01\n2026-01-02,2,2026-02-02\n").TimeColumn!.Name);
        Assert.Equal("a", RtmCsvReader.Parse("a,v,b\n2026-01-01,1,x\n2026-01-02,2,y\n").TimeColumn!.Name);
    }

    [Fact]
    public void Unix_times_count_when_the_column_is_named_like_a_time()
    {
        var file = RtmCsvReader.Parse("epoch_time,load\n1793700000,1.5\n1793700060,1.7\n");
        Assert.Equal("epoch_time", file.TimeColumn!.Name);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1793700060), file.TimeColumn.Timestamps[1]);
        Assert.Equal(["load"], file.Metrics.Select(m => m.Name));

        var millis = RtmCsvReader.Parse("ts,load\n1793700000000,1.5\n");
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1793700000), millis.TimeColumn!.Timestamps[0]);

        // The same numbers under any other name are just numbers.
        Assert.Null(RtmCsvReader.Parse("id,load\n1793700000,1.5\n").TimeColumn);
    }

    [Fact]
    public void Times_with_an_offset_keep_the_clock_time_as_written()
    {
        var file = RtmCsvReader.Parse("time,v\n2026-11-03T10:15:00Z,1\n2026-11-03T10:16:00+05:00,2\n");

        Assert.Equal(new DateTime(2026, 11, 3, 10, 15, 0), file.TimeColumn!.Timestamps[0]);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 16, 0), file.TimeColumn.Timestamps[1]);
    }

    [Fact]
    public void Plain_numbers_are_never_times()
    {
        // DateTime.TryParse would read "12.5" as 5 December.
        var file = RtmCsvReader.Parse("ratio\n12.5\n3.1\n");

        Assert.Null(file.TimeColumn);
        Assert.Equal([12.5, 3.1], Metric(file, "ratio").Values);
    }

    [Fact]
    public void A_few_stray_words_in_a_number_column_become_gaps()
    {
        string rows = string.Join("\n", Enumerable.Range(1, 19).Select(i => $"{i},web{i}"));
        var file = RtmCsvReader.Parse($"reading,host\n{rows}\nERROR,web20\n");

        MetricColumn reading = Metric(file, "reading");
        Assert.Equal(20, reading.Values.Length);
        Assert.True(double.IsNaN(reading.Values[19]));
        Assert.Equal(19, reading.SampleCount);
        Assert.Equal(["host"], file.LabelColumns.Select(c => c.Name));
    }

    [Fact]
    public void Mostly_words_is_a_text_column()
    {
        var file = RtmCsvReader.Parse("code\n1\nA\nB\nC\n");

        Assert.Empty(file.Metrics);
        Assert.Equal(1, file.SkippedColumnCount);
    }

    [Fact]
    public void Skips_comments_and_blank_lines_and_pads_short_rows()
    {
        var file = RtmCsvReader.Parse("# exported 2026-11-03\n# by a tool\n\na,b,c\n1,2,3\n\n4,5\n7,8,9,10\n");

        Assert.Equal(3, file.RowCount);
        Assert.Equal(["a", "b", "c", "Column 4"], file.Metrics.Select(m => m.Name));
        Assert.True(double.IsNaN(Metric(file, "c").Values[1]));
    }

    [Fact]
    public void Repeated_and_blank_header_names_are_made_unique()
    {
        var file = RtmCsvReader.Parse("v,v,,v\n1,2,3,4\n");
        Assert.Equal(["v", "v (2)", "Column 3", "v (3)"], file.Metrics.Select(m => m.Name));
    }

    [Fact]
    public void A_single_column_reads()
    {
        var file = RtmCsvReader.Parse("level\n3\n4\n5\n");
        Assert.Equal([3.0, 4.0, 5.0], Metric(file, "level").Values);
    }

    [Fact]
    public void Empty_files_and_header_only_files_say_so()
    {
        Assert.Contains("empty", Assert.Throws<InvalidDataException>(() => RtmCsvReader.Parse("\n\n")).Message);
        Assert.Contains("no data rows", Assert.Throws<InvalidDataException>(() => RtmCsvReader.Parse("a,b\n")).Message);
    }

    [Fact]
    public void Reads_the_same_whatever_this_machines_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var file = RtmCsvReader.Parse("t,v\n2026-11-03 10:00:00,1.5\n");
            Assert.Equal([1.5], Metric(file, "v").Values);
            Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0), file.TimeColumn!.Timestamps[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Reads_a_file_from_disk()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("csvviz-").FullName, "data.tsv");
        File.WriteAllText(path, "﻿when\tvalue\n2026-11-03\t1\n");
        try
        {
            var file = RtmCsvReader.Read(path);
            Assert.Equal(path, file.FilePath);
            Assert.Equal('\t', file.Format.Delimiter);
            Assert.Equal("when", file.TimeColumn!.Name);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
