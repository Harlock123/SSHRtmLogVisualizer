using Avalonia.Styling;

namespace SSHRtmLogVisualizer.Models;

/// <summary>Colours the window chrome borrows from the selected theme.</summary>
/// <param name="Window">Background behind the whole window.</param>
/// <param name="Panel">Background of the bordered panels.</param>
/// <param name="Border">Panel outlines and separators.</param>
/// <param name="Foreground">Default text colour.</param>
/// <param name="Accent">Selection highlight and default-button fill.</param>
public sealed record AppPalette(
    string Window,
    string Panel,
    string Border,
    string Foreground,
    string Accent);

/// <summary>Colours the chart borrows from the selected theme.</summary>
public sealed record ChartPalette(
    string FigureBackground,
    string DataBackground,
    string Axis,
    string Grid,
    string LegendBackground,
    string LegendText,
    string LegendOutline);

/// <summary>
/// One entry in the theme dropdown. <see cref="Variant"/> is
/// <see cref="ThemeVariant.Default"/> for the "System" theme, which follows the OS setting;
/// that theme leaves <see cref="Palette"/> and <see cref="Chart"/> null so the colours are
/// resolved from whichever variant the OS is currently using.
/// </summary>
public sealed class AppTheme
{
    public required string Name { get; init; }

    /// <summary>Light/Dark base the Fluent controls render against, or Default to follow the OS.</summary>
    public required ThemeVariant Variant { get; init; }

    /// <summary>Null for the System theme, which resolves to the stock light or dark palette.</summary>
    public AppPalette? Palette { get; init; }

    /// <summary>Null for the System theme, which resolves to the stock light or dark chart palette.</summary>
    public ChartPalette? Chart { get; init; }

    public bool FollowsSystem => Palette is null;

    public override string ToString() => Name;
}
