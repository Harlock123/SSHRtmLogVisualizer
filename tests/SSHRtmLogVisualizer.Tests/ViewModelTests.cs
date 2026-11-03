using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;
using SSHRtmLogVisualizer.ViewModels;

namespace SSHRtmLogVisualizer.Tests;

/// <summary>
/// Loading files into the window's view model (the X axis choices, presets and messages) and
/// how the chart labels its ticks and names itself.
/// </summary>
public sealed class ViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("csvviz-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Write(string name, string text)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_csv_with_a_time_column_labels_the_x_axis_with_it()
    {
        var vm = new MainWindowViewModel();
        vm.LoadFile(Write("prices.csv", "Symbol,Date,Close\nACME,2026-10-30,101.5\nACME,2026-10-31,102.25\n"));

        Assert.Equal(["Row number", "Date  (time)", "Symbol"], vm.XAxisOptions.Select(o => o.Caption));
        Assert.Equal("Date", vm.SelectedXAxis!.Name);
        Assert.Equal("prices.csv  —  comma-separated  —  2 rows", vm.LoadedFileDescription);
        Assert.Equal("Loaded 1 chartable measure (1 text column skipped). X axis: Date.", vm.StatusText);
        Assert.False(vm.HasPresets);
    }

    [Fact]
    public void Without_a_time_column_the_x_axis_shows_row_numbers()
    {
        var vm = new MainWindowViewModel();
        vm.LoadFile(Write("plain.csv", "a;b\n1,5;2\n3;4\n"));

        Assert.True(vm.SelectedXAxis!.IsRowNumbers);
        Assert.Equal(["1", "2"], vm.SelectedXAxis.Labels);
        Assert.Contains("No time column found", vm.StatusText);
        Assert.Equal("plain.csv  —  semicolon-separated, decimal comma  —  2 rows", vm.LoadedFileDescription);
    }

    [Fact]
    public void An_sshrtm_dump_still_names_its_host_and_offers_presets()
    {
        var vm = new MainWindowViewModel();
        vm.LoadFile(Write("web01.csv", "hostname,cpu_overall_pct,cpu0_pct,load_1m,timestamp\nweb01,10,11,0.5,20260827-150110\n"));

        Assert.Equal("web01.csv  —  web01  —  1 rows", vm.LoadedFileDescription);
        Assert.Equal("timestamp", vm.SelectedXAxis!.Name);
        Assert.Equal(["All CPU %  (2)", "Load avg  (1)"], vm.Presets.Select(p => p.Caption));
    }

    [Fact]
    public void A_file_of_only_text_says_there_is_nothing_to_chart()
    {
        var vm = new MainWindowViewModel();
        vm.LoadFile(Write("hosts.csv", "host,state\nweb01,up\n"));

        Assert.Equal("Loaded 1 rows, but no column holds numbers, so there is nothing to chart.", vm.StatusText);
    }

    [Fact]
    public void Choosing_another_x_axis_redraws_the_chart()
    {
        var vm = new MainWindowViewModel();
        vm.LoadFile(Write("prices.csv", "Symbol,Date,Close\nACME,2026-10-30,101.5\n"));
        int redraws = 0;
        vm.ChartInvalidated += (_, _) => redraws++;

        vm.SelectedXAxis = vm.XAxisOptions.Single(o => o.Name == "Symbol");

        Assert.Equal(1, redraws);
    }

    [Fact]
    public void Ticks_show_what_tells_the_rows_apart()
    {
        LabelColumn Times(params string[] cells) =>
            RtmCsvReader.Parse("t,v\n" + string.Join("\n", cells.Select(c => c + ",1"))).TimeColumn!;
        string Tick(LabelColumn c, int row) => ChartRenderer.FormatRowTick(c, row, c.SpansMultipleDays, c.HasTimeOfDay);

        LabelColumn oneDay = Times("2026-11-03 10:00:00", "2026-11-03 10:00:30");
        Assert.Equal("10:00:30", Tick(oneDay, 1));

        LabelColumn days = Times("2026-11-03 23:59:00", "2026-11-04 00:01:00");
        Assert.Equal("11-04 00:01:00", Tick(days, 1));

        LabelColumn daily = Times("2026-11-03", "2026-11-04");
        Assert.Equal("2026-11-04", Tick(daily, 1));

        LabelColumn text = RtmCsvReader.Parse("name,v\nalpha,1\nbeta,2\n").LabelColumns[0];
        Assert.Equal("beta", Tick(text, 1));
        Assert.Equal("", Tick(text, 5));
    }

    [Fact]
    public void The_title_names_the_host_or_the_file_and_the_span()
    {
        var dump = RtmCsvReader.Parse("hostname,v,timestamp\nweb01,1,20260827-150110\nweb01,2,20260827-150120\n", "/x/dump.csv");
        Assert.Equal("web01 — 1 measure — 2026-08-27 15:01:10 → 2026-08-27 15:01:20",
            ChartRenderer.BuildTitle(dump, dump.TimeColumn!, 1));

        var daily = RtmCsvReader.Parse("Date,Close\n2026-10-30,1\n2026-10-31,2\n", "/x/prices.csv");
        Assert.Equal("prices.csv — 2 measures — 2026-10-30 → 2026-10-31", ChartRenderer.BuildTitle(daily, daily.TimeColumn!, 2));
        Assert.Equal("prices.csv — 1 measure", ChartRenderer.BuildTitle(daily, LabelColumn.RowNumbers(2), 1));
    }
}
