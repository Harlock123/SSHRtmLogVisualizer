using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SSHRtmLogVisualizer.ViewModels;

namespace SSHRtmLogVisualizer.Tests;

/// <summary>
/// The window on Avalonia's headless platform with a CSV that isn't an SSHRTM dump. Set CSVVIZ_SHOTS to a folder to
/// save pictures of it.
/// </summary>
public class WindowTests
{
    private static string? Shots => Environment.GetEnvironmentVariable("CSVVIZ_SHOTS") is { Length: > 0 } d ? d : null;

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Shot(Window w, string name)
    {
        Pump();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Shots is not { } dir) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void Charts_any_csv_against_its_date_column()
    {
        string sample = Path.Combine(AppContext.BaseDirectory, "Samples", "weather.csv");
        Assert.True(File.Exists(sample), $"{sample} is missing: is docs/samples/weather.csv committed?");

        var w = new MainWindow { Width = 1280, Height = 800 };
        w.Show();
        w.TryLoadFile(sample);
        Pump();

        var vm = (MainWindowViewModel)w.DataContext!;
        Assert.NotNull(vm.LogFile);
        Assert.Equal("Date", vm.SelectedXAxis!.Name);
        Assert.Equal(["High (°C)", "Low (°C)", "Rain (mm)", "Humidity"], vm.AvailableMetrics.Select(m => m.Name));
        Assert.Equal("Date  (time)", w.FindControl<ComboBox>("XAxisChoice")!.SelectedItem!.ToString());

        foreach (var m in vm.AvailableMetrics.Take(2)) vm.SelectedMetrics.Add(m);
        w.FindControl<Button>("VisualizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.StartsWith("Charted 2 measure(s) across 304 rows.", vm.StatusText);
        Shot(w, "08-any-csv");
        w.Close();
    }
}
