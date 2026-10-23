using System;
using System.Collections.Generic;
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

        bool multiDay = file.SpansMultipleDays;
        plot.Axes.Bottom.Label.Text = $"Sample # (row 1 → row {file.RowCount:N0})";
        plot.Axes.Bottom.TickGenerator = new NumericAutomatic
        {
            IntegerTicksOnly = true,
            LabelFormatter = value => FormatRowTick(file, value, multiDay),
        };

        plot.Axes.Left.Label.Text = normalize
            ? "Normalized (0–100 % of each measure's own range)"
            : "Value";

        plot.Title(BuildTitle(file, metrics.Count));

        plot.Axes.AutoScale();
        plot.Axes.SetLimitsX(0, Math.Max(1, file.RowCount - 1));
    }

    private static void RenderPlaceholder(Plot plot, RtmLogFile? file)
    {
        plot.Title(file is null
            ? "Click \"Open CSV Dump…\" to load an SSHRTM log"
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

    /// <summary>Labels an X tick with the timestamp of the row it sits on.</summary>
    private static string FormatRowTick(RtmLogFile file, double value, bool multiDay)
    {
        int index = (int)Math.Round(value);
        if (index < 0 || index >= file.RowCount)
        {
            return string.Empty;
        }

        DateTime? stamp = file.Timestamps[index];
        if (stamp is null)
        {
            // Fall back to the raw text when the dump used a timestamp format we don't parse.
            return file.TimestampLabels[index];
        }

        return multiDay
            ? stamp.Value.ToString("MM-dd HH:mm:ss")
            : stamp.Value.ToString("HH:mm:ss");
    }

    private static string BuildTitle(RtmLogFile file, int seriesCount)
    {
        string host = string.IsNullOrWhiteSpace(file.HostName) ? "SSHRTM dump" : file.HostName!;
        string span = DescribeSpan(file);
        string measures = seriesCount == 1 ? "1 measure" : $"{seriesCount} measures";
        return span.Length == 0 ? $"{host} — {measures}" : $"{host} — {measures} — {span}";
    }

    private static string DescribeSpan(RtmLogFile file)
    {
        DateTime? first = file.Timestamps.FirstOrDefault(t => t is not null);
        DateTime? last = file.Timestamps.LastOrDefault(t => t is not null);
        if (first is null || last is null)
        {
            return string.Empty;
        }

        return $"{first.Value:yyyy-MM-dd HH:mm:ss} → {last.Value:yyyy-MM-dd HH:mm:ss}";
    }
}
