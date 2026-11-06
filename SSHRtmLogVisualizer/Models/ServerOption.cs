using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SSHRtmLogVisualizer.Models;

/// <summary>
/// One server of an SSHRTM combined log, as a check box: unticked, its measures leave the picker and the presets.
/// </summary>
public sealed partial class ServerOption : ObservableObject
{
    private readonly Action _changed;

    public ServerOption(string name, Action changed)
    {
        Name = name;
        _changed = changed;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => _changed();

    public override string ToString() => Name;
}
