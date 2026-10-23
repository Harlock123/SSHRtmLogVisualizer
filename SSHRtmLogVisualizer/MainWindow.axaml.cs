using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;
using SSHRtmLogVisualizer.ViewModels;

namespace SSHRtmLogVisualizer;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        ThemeManager.Apply(_viewModel.SelectedTheme);

        // The System theme has no colours of its own, so it has to re-resolve whenever the
        // OS flips between light and dark.
        ActualThemeVariantChanged += (_, _) =>
        {
            if (_viewModel.SelectedTheme.FollowsSystem)
            {
                ThemeManager.Apply(_viewModel.SelectedTheme);
            }

            RenderChart();
        };

        _viewModel.ChartInvalidated += (_, _) => RenderChart();
        RenderChart();
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select an SSHRTM CSV dump",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SSHRTM CSV dump") { Patterns = new[] { "*.csv" } },
                    FilePickerFileTypes.All,
                },
            });

            if (picked.Count == 0)
            {
                return;
            }

            string? path = picked[0].TryGetLocalPath();
            if (path is null)
            {
                _viewModel.StatusText = "That file is not available on the local filesystem.";
                return;
            }

            TryLoadFile(path);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Could not open the file dialog: {ex.Message}";
        }
    }

    private void OnVisualizeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            RenderChart();
            _viewModel.StatusText =
                $"Charted {_viewModel.SelectedMetrics.Count:N0} measure(s) across {_viewModel.LogFile?.RowCount ?? 0:N0} samples. " +
                "Scroll to zoom, drag to pan.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Could not draw the chart: {ex.Message}";
        }
    }

    private async void OnSaveImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? result = await ChartExporter.SaveAsync(
                Chart.Plot,
                this,
                ChartPixelSize(),
                ChartExporter.SuggestName(_viewModel.LogFile?.FilePath));

            if (result is not null)
            {
                _viewModel.StatusText = result;
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Could not save the image: {ex.Message}";
        }
    }

    private async void OnCopyImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.StatusText = await ChartExporter.CopyAsync(Chart.Plot, this, ChartPixelSize());
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Could not copy the image: {ex.Message}";
        }
    }

    /// <summary>Loads a dump by path, reporting any failure in the status bar rather than crashing.</summary>
    public void TryLoadFile(string path)
    {
        try
        {
            _viewModel.LoadFile(path);
            RenderChart();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Could not load the dump: {ex.Message}";
            _viewModel.LoadedFileDescription = "No dump loaded — click \"Open CSV Dump…\" to pick one.";
        }
    }

    /// <summary>Exports at what is on screen, so the saved image matches the chart as displayed.</summary>
    private PixelSize ChartPixelSize()
    {
        double scale = RenderScaling;
        return new PixelSize(
            Math.Max(1, (int)(Chart.Bounds.Width * scale)),
            Math.Max(1, (int)(Chart.Bounds.Height * scale)));
    }

    private void RenderChart()
    {
        var metrics = new List<MetricColumn>(_viewModel.SelectedMetrics);
        ChartPalette palette = ThemeManager.ResolveChart(_viewModel.SelectedTheme, ActualThemeVariant);

        ChartRenderer.Render(Chart.Plot, _viewModel.LogFile, metrics, _viewModel.NormalizeSeries, palette);
        Chart.Refresh();
    }
}
