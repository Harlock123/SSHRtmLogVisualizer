using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;
using SSHRtmLogVisualizer.ViewModels;

namespace SSHRtmLogVisualizer.Tests;

/// <summary>
/// SSHRTM's combined logs: several servers' columns side by side, named server::measure, with the timestamp first.
/// The made-up sample is docs/samples/combined-20261106-141500.csv (web01, db01 and cache01; db01 drops out for
/// 45 seconds).
/// </summary>
public sealed class CombinedLogTests
{
    private const string Sample = "combined-20261106-141500.csv";

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", Sample);

    private const string Small = """
        timestamp,web01::status,web01::sample_age_s,web01::hostname,web01::cpu_overall_pct,web01::load_1m,db01::status,db01::sample_age_s,db01::hostname,db01::cpu_overall_pct,db01::load_1m,db01::cpu0_pct
        20261106-141500.012,Ok,0.20,web01.example.com,10,0.5,Ok,0.30,db01.example.com,50,2.5,51
        20261106-141501.015,Ok,0.21,web01.example.com,12,0.6,Error,,,,,
        20261106-141502.010,Ok,0.22,web01.example.com,14,0.7,Ok,0.10,db01.example.com,55,2.6,56
        """;

    private static MainWindowViewModel Load(string text)
    {
        string dir = Directory.CreateTempSubdirectory("csvviz-").FullName;
        try
        {
            string path = Path.Combine(dir, "web01+db01-20261106-141500.csv");
            File.WriteAllText(path, text);
            var vm = new MainWindowViewModel();
            vm.LoadFile(path);
            return vm;
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_reader_lists_the_servers_and_splits_each_measure_from_its_server()
    {
        var file = RtmCsvReader.Parse(Small);

        Assert.True(file.IsCombined);
        Assert.Equal(["web01", "db01"], file.Servers);
        Assert.Equal("timestamp", file.TimeColumn!.Name);
        Assert.Equal(new DateTime(2026, 11, 6, 14, 15, 1, 15), file.TimeColumn.Timestamps[1]);

        MetricColumn db = Assert.Single(file.Metrics, m => m.Name == "db01::cpu_overall_pct");
        Assert.Equal("db01", db.Server);
        Assert.Equal("cpu_overall_pct", db.Measure);
        Assert.Equal("db01 · cpu_overall_pct", db.DisplayName);
        Assert.True(double.IsNaN(db.Values[1]), "db01's outage is a gap, not a zero");
        Assert.Contains(file.LabelColumns, c => c.Name == "db01::status");
    }

    [Fact]
    public void A_plain_csv_has_no_servers()
    {
        var file = RtmCsvReader.Parse("hostname,cpu_overall_pct,timestamp\nweb01,1,20260827-150110\n");
        Assert.False(file.IsCombined);
        Assert.Null(file.Metrics[0].Server);
        Assert.Equal("cpu_overall_pct", file.Metrics[0].DisplayName);

        Assert.Equal((null, "::x"), MetricColumn.SplitServer("::x"));
        Assert.Equal((null, "x::"), MetricColumn.SplitServer("x::"));
        Assert.Equal(("rack.1", "a::b"), MetricColumn.SplitServer("rack.1::a::b"));
    }

    [Fact]
    public void Loading_names_the_servers_and_offers_their_check_boxes_and_measures_to_compare()
    {
        var vm = Load(Small);

        Assert.True(vm.IsCombined);
        Assert.Equal("web01+db01-20261106-141500.csv  —  combined log: web01 and db01  —  3 rows", vm.LoadedFileDescription);
        Assert.Equal(["web01", "db01"], vm.ServerOptions.Select(o => o.Name));
        Assert.All(vm.ServerOptions, o => Assert.True(o.IsChecked));
        Assert.Equal(["sample_age_s", "cpu_overall_pct", "load_1m", "cpu0_pct"], vm.CompareMeasures);
        Assert.Equal("cpu_overall_pct", vm.SelectedCompareMeasure);
        Assert.Contains("across 2 servers", vm.StatusText);
    }

    [Fact]
    public void Compare_adds_the_measure_for_every_ticked_server()
    {
        var vm = Load(Small);

        vm.CompareAcrossServersCommand.Execute(null);
        Assert.Equal(["web01::cpu_overall_pct", "db01::cpu_overall_pct"], vm.SelectedMetrics.Select(m => m.Name));
        Assert.Equal("Added \"cpu_overall_pct\" for 2 servers.", vm.StatusText);

        vm.CompareAcrossServersCommand.Execute(null);
        Assert.Equal(2, vm.SelectedMetrics.Count);

        vm.ServerOptions.Single(o => o.Name == "web01").IsChecked = false;
        vm.SelectedCompareMeasure = "load_1m";
        vm.CompareAcrossServersCommand.Execute(null);
        Assert.Equal("db01::load_1m", vm.SelectedMetrics[^1].Name);
        Assert.Equal(3, vm.SelectedMetrics.Count);

        // A measure only some servers have adds just theirs.
        vm.ServerOptions.Single(o => o.Name == "web01").IsChecked = true;
        vm.SelectedCompareMeasure = "cpu0_pct";
        vm.CompareAcrossServersCommand.Execute(null);
        Assert.Equal("Added \"cpu0_pct\" for 1 server.", vm.StatusText);
    }

    [Fact]
    public void Unticking_a_server_takes_its_measures_out_of_the_picker_and_the_presets()
    {
        var vm = Load(Small);
        Assert.Equal(["All CPU %  (3)", "Load avg  (2)"], vm.Presets.Select(p => p.Caption));
        Assert.Equal(7, vm.AvailableMetrics.Count);

        vm.ServerOptions.Single(o => o.Name == "db01").IsChecked = false;

        Assert.All(vm.AvailableMetrics, m => Assert.Equal("web01", m.Server));
        Assert.Equal(["All CPU %  (1)", "Load avg  (1)"], vm.Presets.Select(p => p.Caption));
        Assert.Equal("Showing the measures of 1 of 2 servers.", vm.StatusText);

        vm.ServerOptions.Single(o => o.Name == "web01").IsChecked = false;
        Assert.Empty(vm.AvailableMetrics);
        Assert.False(vm.CompareAcrossServersCommand.CanExecute(null));
    }

    [Fact]
    public void The_filter_box_matches_server_names_too()
    {
        var vm = Load(Small);
        vm.MetricFilter = "db01 · load";
        Assert.Equal(["db01::load_1m"], vm.AvailableMetrics.Select(m => m.Name));
    }

    [Fact]
    public void The_title_and_legend_name_the_servers()
    {
        var file = RtmCsvReader.Parse(Small, "/x/combined.csv");
        Assert.Equal("web01 and db01 — 2 measures — 2026-11-06 14:15:00 → 2026-11-06 14:15:02",
            ChartRenderer.BuildTitle(file, file.TimeColumn!, 2));
        Assert.Equal("web01, db01 and cache01", ChartRenderer.DescribeServers(["web01", "db01", "cache01"]));
        Assert.Equal("5 servers", ChartRenderer.DescribeServers(["a", "b", "c", "d", "e"]));
    }

    [Fact]
    public void The_sample_combined_log_reads_whole()
    {
        Assert.True(File.Exists(SamplePath), $"{SamplePath} is missing: is docs/samples/{Sample} committed?");
        var file = RtmCsvReader.Read(SamplePath);

        Assert.Equal(["web01", "db01", "cache01"], file.Servers);
        Assert.Equal(600, file.RowCount);
        MetricColumn db = Assert.Single(file.Metrics, m => m.Name == "db01::cpu_overall_pct");
        Assert.Equal(555, db.SampleCount); // 45 rows of outage
        Assert.Equal(3, file.Metrics.Count(m => m.Measure == "cpu_overall_pct"));
        Assert.Equal("timestamp", file.TimeColumn!.Name);
    }
}
