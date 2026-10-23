using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// A one-click group of related measures, e.g. "All CPU %". The matches are resolved against
/// the loaded dump, so a preset only appears when that dump actually contains those measures.
/// </summary>
public sealed class MetricPreset
{
    public MetricPreset(string name, IReadOnlyList<MetricColumn> matches, Action<MetricPreset> add)
    {
        Name = name;
        Matches = matches;
        AddCommand = new RelayCommand(() => add(this));
    }

    public string Name { get; }

    /// <summary>The measures this preset will append, in CSV column order.</summary>
    public IReadOnlyList<MetricColumn> Matches { get; }

    public ICommand AddCommand { get; }

    public string Tooltip => Matches.Count == 1
        ? $"Add 1 measure: {Matches[0].Name}"
        : $"Add {Matches.Count:N0} measures";

    /// <summary>Button caption: the name plus how many measures it will add.</summary>
    public string Caption => $"{Name}  ({Matches.Count:N0})";
}
