# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers using the app, the CSV format it reads,
the presets, themes and publishing; this file covers how the code is put together and the rules that keep it working.

## What this is

A desktop app that charts an SSHRTM CSV metric dump (one row per sample, one column per metric, a timestamp in the
last column). It is C# on .NET 9 with Avalonia 12.1.1, ScottPlot 5 for the chart and CommunityToolkit.Mvvm for the
view model, and runs on Windows, macOS and Linux.

## Commands

```bash
dotnet build SSHRtmLogVisualizer.sln
dotnet run --project SSHRtmLogVisualizer -- path/to/dump.csv      # the path is optional; App.axaml.cs loads args[0]
dotnet publish SSHRtmLogVisualizer -c Release -p:PublishProfile=linux-x64   # or win-x64; output in publish/<rid>/
```

There are no tests yet. `publish/` is ignored by git.

## Layout

All code is under `SSHRtmLogVisualizer/`.

- `Services/RtmCsvReader.cs`: parses the dump (quoted fields, the timestamp column, numeric-column detection) into
  an `RtmLogFile`.
- `Models/`: `RtmLogFile` (timestamps plus chartable measures), `MetricColumn` (one measure's values and range),
  `MetricPreset` (a quick-add button), `AppTheme` (window and chart palettes).
- `ViewModels/MainWindowViewModel.cs`: the picker, filter, visualize list and its commands, and the presets.
  `PresetDefinitions` holds each preset as a name and a regex; `BuildPresets` keeps only those that match the dump.
- `Services/ChartRenderer.cs`: builds the ScottPlot line chart, including the 0-100 % normalize option.
- `Services/ChartExporter.cs`: save to PNG, JPEG or BMP, and copy to the clipboard.
- `Services/ThemeManager.cs`: the nine themes and how they are applied to the window and the chart.
- `MainWindow.axaml(.cs)`: layout, file dialogs and chart hosting. `TryLoadFile` is the single entry for loading.
- `Properties/PublishProfiles/`: `win-x64` and `linux-x64` self-contained single-file profiles.
- `docs/`: the README's screenshots and a Word user guide.

## Rules that keep it working

- **Nothing is hardcoded to one host.** Measures, presets and the header all come from the loaded file. A new
  preset is a name and regex in `PresetDefinitions`; one that matches nothing simply doesn't show.
- **Invariant culture.** Numbers and timestamps are parsed with `CultureInfo.InvariantCulture`, so a dump reads the
  same on any machine. Keep it that way in any new parsing.
- **Gaps, not zeros.** Blank or `n/a`-style samples are stored as `double.NaN` and drawn as gaps. Don't turn them
  into 0.
- **Read while it grows.** The reader opens the file with `FileShare.ReadWrite` so a dump still being written can be
  charted.
- **No trimming.** The publish profiles set `PublishTrimmed` to false because Avalonia resolves XAML by
  reflection. The csproj also removes `.pdb` files from the publish output so it stays one file.
- **The list and the chart agree.** When the visualize list empties, the chart is cleared too.

## Making a change

- Check changes by running the app on a real dump; there are no automated tests.
- If the UI changes, refresh the screenshots in `docs/screenshots/` and update `README.md` to match.

## CI and releases

There is no CI workflow. Builds are published by hand with the publish profiles.
