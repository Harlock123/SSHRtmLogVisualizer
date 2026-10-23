using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SSHRtmLogVisualizer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            // Allow a dump to be opened straight from the command line (or by dropping a
            // CSV onto the executable) instead of going through the file dialog.
            string[] args = desktop.Args ?? [];
            if (args.Length > 0)
            {
                window.TryLoadFile(args[0]);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}