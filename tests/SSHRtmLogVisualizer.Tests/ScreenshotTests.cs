using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SSHRtmLogVisualizer.Models;
using SSHRtmLogVisualizer.Services;
using SSHRtmLogVisualizer.ViewModels;

namespace SSHRtmLogVisualizer.Tests;

/// <summary>
/// The README's SSHRTM pictures (01 to 07), taken from the made-up dump in docs/samples on Avalonia's headless
/// platform. Each checks what it shows; set CSVVIZ_SHOTS to a folder to save the pictures too.
/// </summary>
public class ScreenshotTests
{
    private const string Dump = "web01-20260827-150110.csv";

    private static string? Shots => Environment.GetEnvironmentVariable("CSVVIZ_SHOTS") is { Length: > 0 } d ? d : null;

    private static bool _overlayPopups;

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Opens the window on the dump in a theme. Popups (the measure picker, the theme list) are drawn in the
    /// window's overlay layer, so they are in the captured frame.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string theme = "Dark")
    {
        if (!_overlayPopups)
        {
            Application.Current!.Styles.Add(new Style(x => x.OfType<Popup>())
            {
                Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) },
            });
            _overlayPopups = true;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Samples", Dump);
        Assert.True(File.Exists(path), $"{path} is missing: is docs/samples/{Dump} committed?");

        var w = new MainWindow { Width = 1400, Height = 793 };
        var vm = (MainWindowViewModel)w.DataContext!;
        vm.SelectedTheme = ThemeManager.Themes.Single(t => t.Name == theme);
        w.Show();
        w.TryLoadFile(path);
        Pump();
        Assert.Equal("web01-20260827-150110.csv  —  web01.example.com  —  1,409 rows", vm.LoadedFileDescription);
        return (w, vm);
    }

    private static void Add(MainWindowViewModel vm, string preset) => vm.Presets.Single(p => p.Name == preset).AddCommand.Execute(null);

    private static void Visualize(Window w)
    {
        w.FindControl<Button>("VisualizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    private static void Shot(Window w, string name)
    {
        Pump();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Shots is not { } dir) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void Overview_all_cpu()
    {
        var (w, vm) = Open();
        Assert.Equal(
            ["All CPU %  (17)", "Load avg  (3)", "Memory %  (2)", "Memory bytes  (5)", "Network  (4)", "Disk busy %  (17)",
             "Filesystem %  (15)", "Top proc CPU %  (20)"],
            vm.Presets.Select(p => p.Caption));
        Assert.Equal("timestamp", vm.SelectedXAxis!.Name);

        Add(vm, "All CPU %");
        Visualize(w);
        Assert.Equal(17, vm.SelectedMetrics.Count);
        Shot(w, "01-overview");
        w.Close();
    }

    [AvaloniaFact]
    public void Measure_picker()
    {
        var (w, vm) = Open();
        Add(vm, "All CPU %");
        Visualize(w);

        var picker = w.GetVisualDescendants().OfType<DropDownButton>().Single();
        picker.Flyout!.ShowAt(picker);
        Pump();
        foreach (string name in new[] { "load_1m", "cpu_overall_pct", "cpu0_pct" })
        {
            vm.MetricSelection.Select(vm.AvailableMetrics.IndexOf(vm.AvailableMetrics.Single(m => m.Name == name)));
        }

        Assert.Equal("3 measures selected", vm.PendingSelectionSummary);
        Shot(w, "02-measure-picker");
        picker.Flyout.Hide();
        w.Close();
    }

    [AvaloniaFact]
    public void Mixed_units_absolute_then_normalized()
    {
        var (w, vm) = Open();
        Add(vm, "Memory bytes");
        Add(vm, "Load avg");
        Visualize(w);
        Assert.Equal(8, vm.SelectedMetrics.Count);
        Shot(w, "03-mixed-units-absolute");

        vm.NormalizeSeries = true;
        Visualize(w);
        Shot(w, "04-mixed-units-normalized");
        w.Close();
    }

    [AvaloniaFact]
    public void Theme_menu()
    {
        var (w, vm) = Open();
        Add(vm, "All CPU %");
        Visualize(w);

        var themes = w.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.Themes));
        themes.IsDropDownOpen = true;
        Pump();
        Assert.Equal(9, vm.Themes.Count);
        Shot(w, "05-theme-menu");
        themes.IsDropDownOpen = false;
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData("Solarized Light", "06-theme-solarized-light")]
    [InlineData("Nord", "07-theme-nord")]
    public void Themes(string theme, string name)
    {
        var (w, vm) = Open();
        Add(vm, "All CPU %");
        Visualize(w);
        vm.SelectedTheme = ThemeManager.Themes.Single(t => t.Name == theme);
        Pump();
        Assert.Equal($"Theme set to {theme}.", vm.StatusText);
        Shot(w, name);
        w.Close();
    }
}
