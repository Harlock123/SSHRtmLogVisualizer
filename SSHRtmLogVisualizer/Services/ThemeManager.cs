using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SSHRtmLogVisualizer.Models;

namespace SSHRtmLogVisualizer.Services;

/// <summary>
/// Owns the theme catalogue and applies a theme to the running application.
///
/// Applying a theme does three things: it pins the Fluent light/dark base, it publishes the
/// five <c>App*</c> brushes the window binds to with <c>DynamicResource</c>, and it feeds the
/// accent colour into Fluent's palette so buttons and list selection follow the theme too.
/// </summary>
public static class ThemeManager
{
    /// <summary>Resource keys the window binds against; kept here so the two stay in step.</summary>
    private const string WindowBackgroundKey = "AppWindowBackground";
    private const string PanelBackgroundKey = "AppPanelBackground";
    private const string BorderKey = "AppBorderBrush";
    private const string ForegroundKey = "AppForeground";
    private const string AccentKey = "AppAccentBrush";

    private static readonly AppPalette StockLight =
        new("#f3f3f3", "#fbfbfb", "#d0d0d0", "#1a1a1a", "#0078d4");

    private static readonly AppPalette StockDark =
        new("#202020", "#272727", "#3d3d3d", "#f0f0f0", "#4cc2ff");

    private static readonly ChartPalette StockLightChart =
        new("#ffffff", "#ffffff", "#000000", "#d8d8d8", "#ffffff", "#000000", "#b0b0b0");

    private static readonly ChartPalette StockDarkChart =
        new("#181818", "#1f1f1f", "#d0d0d0", "#404040", "#1f1f1f", "#d0d0d0", "#505050");

    public static IReadOnlyList<AppTheme> Themes { get; } = new[]
    {
        new AppTheme
        {
            Name = "System (match OS)",
            Variant = ThemeVariant.Default,
        },
        new AppTheme
        {
            Name = "Light",
            Variant = ThemeVariant.Light,
            Palette = StockLight,
            Chart = StockLightChart,
        },
        new AppTheme
        {
            Name = "Dark",
            Variant = ThemeVariant.Dark,
            Palette = StockDark,
            Chart = StockDarkChart,
        },
        new AppTheme
        {
            Name = "Solarized Light",
            Variant = ThemeVariant.Light,
            Palette = new AppPalette("#eee8d5", "#fdf6e3", "#d3cbb7", "#073642", "#268bd2"),
            Chart = new ChartPalette("#fdf6e3", "#fdf6e3", "#586e75", "#e4dcc6", "#fdf6e3", "#073642", "#d3cbb7"),
        },
        new AppTheme
        {
            Name = "Solarized Dark",
            Variant = ThemeVariant.Dark,
            Palette = new AppPalette("#002b36", "#073642", "#144e5a", "#93a1a1", "#268bd2"),
            Chart = new ChartPalette("#002b36", "#073642", "#93a1a1", "#144e5a", "#073642", "#93a1a1", "#144e5a"),
        },
        new AppTheme
        {
            Name = "Nord",
            Variant = ThemeVariant.Dark,
            Palette = new AppPalette("#2e3440", "#3b4252", "#4c566a", "#d8dee9", "#88c0d0"),
            Chart = new ChartPalette("#2e3440", "#3b4252", "#d8dee9", "#4c566a", "#3b4252", "#eceff4", "#4c566a"),
        },
        new AppTheme
        {
            Name = "Dracula",
            Variant = ThemeVariant.Dark,
            Palette = new AppPalette("#282a36", "#343746", "#44475a", "#f8f8f2", "#bd93f9"),
            Chart = new ChartPalette("#282a36", "#343746", "#f8f8f2", "#44475a", "#343746", "#f8f8f2", "#6272a4"),
        },
        new AppTheme
        {
            Name = "Gruvbox Dark",
            Variant = ThemeVariant.Dark,
            Palette = new AppPalette("#282828", "#32302f", "#504945", "#ebdbb2", "#fabd2f"),
            Chart = new ChartPalette("#282828", "#32302f", "#ebdbb2", "#504945", "#32302f", "#ebdbb2", "#665c54"),
        },
        new AppTheme
        {
            Name = "High Contrast",
            Variant = ThemeVariant.Dark,
            Palette = new AppPalette("#000000", "#000000", "#ffffff", "#ffffff", "#ffff00"),
            Chart = new ChartPalette("#000000", "#000000", "#ffffff", "#585858", "#000000", "#ffffff", "#ffffff"),
        },
    };

    public static AppTheme Default => Themes[0];

    /// <summary>Applies a theme to the running application.</summary>
    public static void Apply(AppTheme theme)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedThemeVariant = theme.Variant;

        AppPalette palette = theme.Palette ?? StockFor(app.ActualThemeVariant);

        app.Resources[WindowBackgroundKey] = Brush(palette.Window);
        app.Resources[PanelBackgroundKey] = Brush(palette.Panel);
        app.Resources[BorderKey] = Brush(palette.Border);
        app.Resources[ForegroundKey] = Brush(palette.Foreground);
        app.Resources[AccentKey] = Brush(palette.Accent);

        ApplyFluentAccent(app, theme, palette);
    }

    /// <summary>Chart colours for a theme, resolving "System" against the variant actually in use.</summary>
    public static ChartPalette ResolveChart(AppTheme theme, ThemeVariant actualVariant) =>
        theme.Chart ?? (actualVariant == ThemeVariant.Dark ? StockDarkChart : StockLightChart);

    /// <summary>
    /// Pushes the theme's accent and surface colours into Fluent's palette so the controls
    /// themselves (button fills, list selection) pick up the theme, not just the chrome.
    /// </summary>
    private static void ApplyFluentAccent(Application app, AppTheme theme, AppPalette palette)
    {
        FluentTheme? fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluent is null)
        {
            return;
        }

        ThemeVariant target = theme.Variant == ThemeVariant.Default ? app.ActualThemeVariant : theme.Variant;

        fluent.Palettes[target] = new ColorPaletteResources
        {
            Accent = Color.Parse(palette.Accent),
            RegionColor = Color.Parse(palette.Window),
        };
    }

    private static AppPalette StockFor(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? StockDark : StockLight;

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
