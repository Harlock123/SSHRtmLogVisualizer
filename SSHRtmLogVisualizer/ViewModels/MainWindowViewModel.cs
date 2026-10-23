using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;

namespace SSHRtmLogVisualizer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Quick-add groups offered for any dump, as (button label, measure-name pattern).
    /// A preset only reaches the UI when the loaded dump has at least one matching measure.
    /// </summary>
    private static readonly (string Name, string Pattern)[] PresetDefinitions =
    {
        ("All CPU %", @"^cpu.*_pct$"),
        ("Load avg", @"^load_\d+m$"),
        ("Memory %", @"^(mem|swap)_used_pct$"),
        ("Memory bytes", @"^mem_.*_bytes$"),
        ("Network", @"^net_.*_(rx|tx)_bps$"),
        ("Disk busy %", @"^disk_.*_busy_pct$"),
        ("Filesystem %", @"^fs_.*_used_pct$"),
        ("Top proc CPU %", @"^proc\d+_cpu_pct$"),
    };

    /// <summary>Every numeric measure in the loaded dump, before the filter box is applied.</summary>
    private readonly List<MetricColumn> _allMetrics = new();

    public MainWindowViewModel()
    {
        SelectedMetrics.CollectionChanged += OnSelectedMetricsChanged;
        MetricSelection.SelectionChanged += OnMetricSelectionChanged;
    }

    /// <summary>Raised when the chart should be redrawn without the user pressing Visualize.</summary>
    public event EventHandler? ChartInvalidated;

    /// <summary>Themes offered by the dropdown in the window's top-right corner.</summary>
    public IReadOnlyList<AppTheme> Themes => ThemeManager.Themes;

    /// <summary>One-click measure groups for the loaded dump; empty until a dump is open.</summary>
    public ObservableCollection<MetricPreset> Presets { get; } = new();

    /// <summary>Measures offered by the dropdown (all of them, or those matching the filter).</summary>
    public ObservableCollection<MetricColumn> AvailableMetrics { get; } = new();

    /// <summary>
    /// What the user has ticked in the dropdown so far. Multi-select, so one "Add" can push
    /// several measures onto the visualize list at once.
    /// </summary>
    public SelectionModel<MetricColumn> MetricSelection { get; } = new() { SingleSelect = false };

    /// <summary>The ordered list of measures the user wants on the chart.</summary>
    public ObservableCollection<MetricColumn> SelectedMetrics { get; } = new();

    [ObservableProperty]
    private RtmLogFile? _logFile;

    [ObservableProperty]
    private string _loadedFileDescription = "No dump loaded — click \"Open CSV Dump…\" to pick one.";

    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _metricFilter = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private int _selectedListIndex = -1;

    /// <summary>
    /// When set, each series is rescaled to 0–100 % of its own range so measures with very
    /// different magnitudes (byte counts next to percentages) stay readable on one Y axis.
    /// </summary>
    [ObservableProperty]
    private bool _normalizeSeries;

    [ObservableProperty]
    private AppTheme _selectedTheme = ThemeManager.Default;

    /// <summary>Drives the enabled state of the "Do The Visualize" button.</summary>
    public bool HasMetricsToVisualize => SelectedMetrics.Count > 0;

    public bool IsFileLoaded => LogFile is not null;

    /// <summary>Hides the quick-add section entirely when the dump matches no preset.</summary>
    public bool HasPresets => Presets.Count > 0;

    /// <summary>Caption on the dropdown button itself.</summary>
    public string MeasurePickerCaption => _allMetrics.Count == 0
        ? "Choose measures…"
        : $"Choose measures…   ({_allMetrics.Count:N0} available)";

    /// <summary>Footer text inside the dropdown, showing how many measures are ticked.</summary>
    public string PendingSelectionSummary => MetricSelection.Count switch
    {
        0 => "Nothing selected — click measures to tick them",
        1 => MetricSelection.SelectedItem?.Name ?? "1 measure selected",
        int n => $"{n} measures selected",
    };

    /// <summary>Loads a dump and resets the picker and visualize list to match it.</summary>
    public void LoadFile(string filePath)
    {
        RtmLogFile file = RtmCsvReader.Read(filePath);

        LogFile = file;
        _allMetrics.Clear();
        _allMetrics.AddRange(file.Metrics);

        SelectedMetrics.Clear();
        MetricFilter = string.Empty;
        ApplyFilter();
        BuildPresets();
        OnPropertyChanged(nameof(MeasurePickerCaption));

        string host = string.IsNullOrWhiteSpace(file.HostName) ? "unknown host" : file.HostName!;
        LoadedFileDescription = $"{Path.GetFileName(filePath)}  —  {host}  —  {file.RowCount:N0} samples";
        StatusText = file.SkippedColumnCount > 0
            ? $"Loaded {file.Metrics.Count:N0} chartable measures ({file.SkippedColumnCount:N0} text columns skipped)."
            : $"Loaded {file.Metrics.Count:N0} chartable measures.";
    }

    partial void OnMetricFilterChanged(string value) => ApplyFilter();

    partial void OnLogFileChanged(RtmLogFile? value) => OnPropertyChanged(nameof(IsFileLoaded));

    private void ApplyFilter()
    {
        // Ticks refer to positions in the old list, so drop them rather than let them
        // silently land on whatever now sits at those positions.
        MetricSelection.Clear();

        AvailableMetrics.Clear();
        foreach (MetricColumn metric in _allMetrics)
        {
            if (MetricFilter.Length == 0 ||
                metric.Name.Contains(MetricFilter, StringComparison.OrdinalIgnoreCase))
            {
                AvailableMetrics.Add(metric);
            }
        }
    }

    /// <summary>Rebuilds the quick-add buttons for the dump just loaded, dropping any with no matches.</summary>
    private void BuildPresets()
    {
        Presets.Clear();

        foreach ((string name, string pattern) in PresetDefinitions)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            List<MetricColumn> matches = _allMetrics.Where(m => regex.IsMatch(m.Name)).ToList();

            if (matches.Count > 0)
            {
                Presets.Add(new MetricPreset(name, matches, ApplyPreset));
            }
        }

        OnPropertyChanged(nameof(HasPresets));
    }

    /// <summary>Appends a preset's measures to the visualize list, skipping any already there.</summary>
    private void ApplyPreset(MetricPreset preset)
    {
        int added = 0;
        int duplicates = 0;

        foreach (MetricColumn metric in preset.Matches)
        {
            if (SelectedMetrics.Contains(metric))
            {
                duplicates++;
                continue;
            }

            SelectedMetrics.Add(metric);
            added++;
        }

        if (added > 0)
        {
            SelectedListIndex = SelectedMetrics.Count - 1;
        }

        StatusText = added == 0
            ? $"\"{preset.Name}\" is already in the visualize list."
            : duplicates == 0
                ? $"\"{preset.Name}\" added {added:N0} measures."
                : $"\"{preset.Name}\" added {added:N0} measures, skipped {duplicates:N0} already in the list.";
    }

    private void OnMetricSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs<MetricColumn> e)
    {
        OnPropertyChanged(nameof(PendingSelectionSummary));
        AddMetricCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedMetricsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasMetricsToVisualize));

        // These commands gate on the list's contents as well as on the selection.
        ClearListCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();

        // An empty list means an empty chart — leaving the last render on screen would
        // show measures the user has just taken out of the list.
        if (SelectedMetrics.Count == 0)
        {
            ChartInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        ThemeManager.Apply(value);
        ChartInvalidated?.Invoke(this, EventArgs.Empty);
        StatusText = $"Theme set to {value.Name}.";
    }

    /// <summary>Appends every measure ticked in the dropdown, in dropdown order, skipping repeats.</summary>
    [RelayCommand(CanExecute = nameof(CanAddMetric))]
    private void AddMetric()
    {
        // Snapshot first: the selection is cleared below, and SelectedItems is a live view.
        List<MetricColumn> picked = MetricSelection.SelectedItems
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

        if (picked.Count == 0)
        {
            return;
        }

        int added = 0;
        int duplicates = 0;
        string? lastAdded = null;

        foreach (MetricColumn metric in picked)
        {
            if (SelectedMetrics.Contains(metric))
            {
                duplicates++;
                continue;
            }

            SelectedMetrics.Add(metric);
            lastAdded = metric.Name;
            added++;
        }

        if (added > 0)
        {
            SelectedListIndex = SelectedMetrics.Count - 1;
        }

        MetricSelection.Clear();
        StatusText = DescribeAdd(added, duplicates, lastAdded);
    }

    private static string DescribeAdd(int added, int duplicates, string? lastAdded)
    {
        if (added == 0)
        {
            return duplicates == 1
                ? "That measure is already in the visualize list."
                : $"All {duplicates:N0} selected measures are already in the visualize list.";
        }

        string addedText = added == 1 ? $"Added \"{lastAdded}\"." : $"Added {added:N0} measures.";
        return duplicates == 0
            ? addedText
            : $"{addedText} Skipped {duplicates:N0} already in the list.";
    }

    private bool CanAddMetric() => MetricSelection.Count > 0;

    [RelayCommand(CanExecute = nameof(CanModifySelectedItem))]
    private void RemoveSelected()
    {
        int index = SelectedListIndex;
        string name = SelectedMetrics[index].Name;
        SelectedMetrics.RemoveAt(index);

        SelectedListIndex = SelectedMetrics.Count == 0
            ? -1
            : Math.Min(index, SelectedMetrics.Count - 1);
        StatusText = $"Removed \"{name}\".";
    }

    [RelayCommand(CanExecute = nameof(CanClearList))]
    private void ClearList()
    {
        SelectedMetrics.Clear();
        SelectedListIndex = -1;
        StatusText = "Visualize list cleared — the chart is empty too.";
    }

    private bool CanClearList() => SelectedMetrics.Count > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        int index = SelectedListIndex;
        SelectedMetrics.Move(index, index - 1);
        SelectedListIndex = index - 1;
    }

    private bool CanMoveUp() => SelectedListIndex > 0;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        int index = SelectedListIndex;
        SelectedMetrics.Move(index, index + 1);
        SelectedListIndex = index + 1;
    }

    private bool CanMoveDown() => SelectedListIndex >= 0 && SelectedListIndex < SelectedMetrics.Count - 1;

    private bool CanModifySelectedItem() =>
        SelectedListIndex >= 0 && SelectedListIndex < SelectedMetrics.Count;
}
