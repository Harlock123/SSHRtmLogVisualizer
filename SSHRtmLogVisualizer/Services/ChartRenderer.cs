using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using SSHRtmLogVisualizer.Models;

namespace SSHRtmLogVisualizer.Services;

/// <summary>
/// Draws the selected measures onto a ScottPlot <see cref="Plot"/> as a multi-series line chart.
/// X runs from the first data row on the left to the last on the right; Y auto-scales to the data.
/// </summary>
public static class ChartRenderer
{
    public static void Render(
        Plot plot,
        RtmLogFile? file,
        LabelColumn? xAxis,
        IReadOnlyList<MetricColumn> metrics,
        bool normalize,
        ChartPalette palette)
    {
        plot.Clear();
        RemoveLegendPanels(plot);
        ApplyTheme(plot, palette);

        if (file is null || file.RowCount == 0 || metrics.Count == 0)
        {
            RenderPlaceholder(plot, file);
            return;
        }

        // One X position per data row: row 0 on the left through row N-1 on the right.
        double[] xs = new double[file.RowCount];
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] = i;
        }

        IPalette seriesPalette = new ScottPlot.Palettes.Category20();

        for (int i = 0; i < metrics.Count; i++)
        {
            MetricColumn metric = metrics[i];
            double[] ys = normalize ? Normalize(metric) : metric.Values;

            var series = plot.Add.ScatterLine(xs, ys, seriesPalette.GetColor(i));
            series.LineWidth = 2;

            // When normalized the plotted numbers no longer carry units, so the legend
            // reports the real range each series was squashed from.
            series.LegendText = normalize
                ? $"{metric.Name}  {metric.CompactRange}"
                : metric.Name;
        }

        plot.Legend.IsVisible = true;

        // Normalized labels carry a value range, so give the panel room before it clips them.
        ScottPlot.Panels.LegendPanel legend = plot.ShowLegend(Edge.Right);
        legend.MaximumSize = normalize ? 560 : 320;

        LabelColumn labels = xAxis ?? LabelColumn.RowNumbers(file.RowCount);
        bool multiDay = labels.SpansMultipleDays;
        bool timeOfDay = labels.HasTimeOfDay;
        plot.Axes.Bottom.Label.Text = labels.IsRowNumbers
            ? $"Row (1 → {file.RowCount:N0})"
            : $"{labels.Name}  (row 1 → row {file.RowCount:N0})";
        plot.Axes.Bottom.TickGenerator = new NumericAutomatic
        {
            IntegerTicksOnly = true,
            LabelFormatter = value => FormatRowTick(labels, value, multiDay, timeOfDay),
        };

        plot.Axes.Left.Label.Text = normalize
            ? "Normalized (0–100 % of each measure's own range)"
            : "Value";

        plot.Title(BuildTitle(file, labels, metrics.Count));

        plot.Axes.AutoScale();
        plot.Axes.SetLimitsX(0, Math.Max(1, file.RowCount - 1));
    }

    private static void RenderPlaceholder(Plot plot, RtmLogFile? file)
    {
        plot.Title(file is null
            ? "Click \"Open CSV…\" to load an SSHRTM dump or any other CSV"
            : "Add measures to the visualize list, then click DO THE VISUALIZE");
        plot.Axes.Bottom.Label.Text = string.Empty;
        plot.Axes.Left.Label.Text = string.Empty;
        plot.Axes.Bottom.TickGenerator = new NumericAutomatic();
        plot.HideLegend();
        plot.Axes.AutoScale();
    }

    /// <summary>
    /// Drops any legend panel left over from a previous render. <see cref="Plot.Clear"/> only
    /// removes plottables, and every <c>ShowLegend(Edge)</c> call adds a fresh panel, so without
    /// this the legends stack up one per visualize click.
    /// </summary>
    private static void RemoveLegendPanels(Plot plot)
    {
        foreach (IPanel panel in plot.Axes.GetPanels())
        {
            if (panel is ScottPlot.Panels.LegendPanel)
            {
                plot.Remove(panel);
            }
        }
    }

    /// <summary>Paints the plot to match the theme selected in the window's theme dropdown.</summary>
    private static void ApplyTheme(Plot plot, ChartPalette palette)
    {
        plot.FigureBackground.Color = Color.FromHex(palette.FigureBackground);
        plot.DataBackground.Color = Color.FromHex(palette.DataBackground);
        plot.Axes.Color(Color.FromHex(palette.Axis));
        plot.Grid.MajorLineColor = Color.FromHex(palette.Grid);
        plot.Legend.BackgroundColor = Color.FromHex(palette.LegendBackground);
        plot.Legend.FontColor = Color.FromHex(palette.LegendText);
        plot.Legend.OutlineColor = Color.FromHex(palette.LegendOutline);
        plot.Axes.Title.Label.ForeColor = Color.FromHex(palette.Axis);
    }

    /// <summary>Rescales a measure onto 0–100 so unlike units share one Y axis legibly.</summary>
    private static double[] Normalize(MetricColumn metric)
    {
        var scaled = new double[metric.Values.Length];

        if (metric.IsFlat)
        {
            // A constant (or all-missing) measure has no range to stretch; park it mid-axis.
            for (int i = 0; i < scaled.Length; i++)
            {
                scaled[i] = double.IsNaN(metric.Values[i]) ? double.NaN : 50d;
            }

            return scaled;
        }

        double span = metric.Maximum - metric.Minimum;
        for (int i = 0; i < scaled.Length; i++)
        {
            double v = metric.Values[i];
            scaled[i] = double.IsNaN(v) ? double.NaN : (v - metric.Minimum) / span * 100d;
        }

        return scaled;
    }

    /// <summary>
    /// Labels an X tick with the row's value in the label column. Times are shortened to what
    /// tells the rows apart: the time of day within one day, the date and time across days, and
    /// the date alone for daily data.
    /// </summary>
    internal static string FormatRowTick(LabelColumn labels, double value, bool multiDay, bool timeOfDay)
    {
        int index = (int)Math.Round(value);
        if (index < 0 || index >= labels.Labels.Count)
        {
            return string.Empty;
        }

        DateTime? stamp = labels.Timestamps[index];
        if (stamp is null)
        {
            // Text columns, row numbers, and times in a form we don't parse show as written.
            return labels.Labels[index];
        }

        return (timeOfDay, multiDay) switch
        {
            (false, _) => stamp.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (true, true) => stamp.Value.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            (true, false) => stamp.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The host (for an SSHRTM dump) or file name, the measure count, and the time span when there is one.</summary>
    internal static string BuildTitle(RtmLogFile file, LabelColumn labels, int seriesCount)
    {
        string source = !string.IsNullOrWhiteSpace(file.HostName)
            ? file.HostName!
            : file.FilePath.Length > 0 ? System.IO.Path.GetFileName(file.FilePath) : "CSV";
        string span = DescribeSpan(labels);
        string measures = seriesCount == 1 ? "1 measure" : $"{seriesCount} measures";
        return span.Length == 0 ? $"{source} — {measures}" : $"{source} — {measures} — {span}";
    }

    private static string DescribeSpan(LabelColumn labels)
    {
        DateTime? first = labels.Timestamps.FirstOrDefault(t => t is not null);
        DateTime? last = labels.Timestamps.LastOrDefault(t => t is not null);
        if (first is null || last is null)
        {
            return string.Empty;
        }

        string format = labels.HasTimeOfDay ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd";
        return $"{first.Value.ToString(format, CultureInfo.InvariantCulture)} → {last.Value.ToString(format, CultureInfo.InvariantCulture)}";
    }
}
