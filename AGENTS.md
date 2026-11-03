# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers using the app, the CSV format it reads,
the presets, themes and publishing; this file covers how the code is put together and the rules that keep it working.

## What this is

A desktop app that charts CSV files: SSHRTM metric dumps (one row per sample, one column per metric, a timestamp in
the last column), and any other CSV. The reader works out the separator, header, decimal mark and time column from
the file. It is C# on .NET 9 with Avalonia 12.1.1, ScottPlot 5 for the chart and CommunityToolkit.Mvvm for the
view model, and runs on Windows, macOS and Linux.

## Commands

```bash
dotnet build SSHRtmLogVisualizer.sln
dotnet run --project SSHRtmLogVisualizer -- path/to/dump.csv      # the path is optional; App.axaml.cs loads args[0]
dotnet publish SSHRtmLogVisualizer -c Release -p:PublishProfile=linux-x64   # or win-x64; output in publish/<rid>/
dotnet test SSHRtmLogVisualizer.sln                                  # reader, view model and headless window tests
CSVVIZ_SHOTS=/tmp/shots dotnet test SSHRtmLogVisualizer.sln          # also saves a picture of the window
```

`publish/` is ignored by git.

## Layout

The app is under `SSHRtmLogVisualizer/`; tests are under `tests/`.

- `Services/RtmCsvReader.cs`: `Parse` reads CSV text into an `RtmLogFile`. It works out:
  - the separator (`DetectDelimiter`, or a `sep=` line found by `SkipPreamble`);
  - the header (`IsHeader` compares rows 1 and 2);
  - the decimal mark (`UsesDecimalComma`);
  - which columns are times (`TryReadTimeColumn`), numbers (`TryReadNumericColumn`) or text;
  - and the time column for the X axis (`PickTimeColumn`).
- `Models/`:
  - `RtmLogFile`: the measures, the label columns, the picked time column, and the `CsvFormat`.
  - `MetricColumn`: one measure's values and range.
  - `LabelColumn`: a column that can label the X axis (times, text, or `RowNumbers`).
  - `MetricPreset`: a quick-add button.
  - `AppTheme`: window and chart palettes.
- `ViewModels/MainWindowViewModel.cs`: the picker, filter, visualize list and its commands, the presets, and the X
  axis choice (`XAxisOptions`, `SelectedXAxis`). `PresetDefinitions` holds each preset as a name and a regex;
  `BuildPresets` keeps only those that match the file.
- `Services/ChartRenderer.cs`: builds the ScottPlot line chart, including the 0-100 % normalize option. It labels
  ticks from the chosen `LabelColumn`; `FormatRowTick` shortens times to suit the data.
- `Services/ChartExporter.cs`: save to PNG, JPEG or BMP, and copy to the clipboard.
- `Services/ThemeManager.cs`: the nine themes and how they are applied to the window and the chart.
- `MainWindow.axaml(.cs)`: layout, file dialogs and chart hosting. `TryLoadFile` is the single entry for loading.
- `Properties/PublishProfiles/`: `win-x64` and `linux-x64` self-contained single-file profiles.
- `docs/`: the README's screenshots, a Word user guide, and `samples/`: made-up files only (`weather.csv`, daily data;
  `web01-20260827-150110.csv`, an SSHRTM dump for a pretend host). `.gitignore` keeps every other `.csv` out.
- `tests/SSHRtmLogVisualizer.Tests/`: xUnit v3.
  - `CsvReaderTests`: the reader on many CSV shapes.
  - `ViewModelTests`: loading into the view model, plus tick and title text.
  - `WindowTests`: the window with the weather sample, on Avalonia.Headless.
  - `ScreenshotTests`: the README's SSHRTM pictures, from the made-up dump, checking what each shows.

## Rules that keep it working

- **Nothing is hardcoded to one host or format.** Measures, presets, the header, the separator and the time column
  all come from the loaded file. A new preset is a name and regex in `PresetDefinitions`; one that matches nothing
  simply doesn't show.
- **Never the machine's culture.** Numbers and times are parsed with `CultureInfo.InvariantCulture`, or with the
  fixed `CommaDecimal` format when the file uses decimal commas. A file reads the same on any machine. Keep it that
  way in any new parsing; `CsvReaderTests` runs one test under de-DE to check.
- **Numbers are never times.** `ParseTimestamp` rejects anything `TryParseNumber` accepts, because .NET would read
  "12.5" as a date. `TryParseNumber` only takes thousands separators in groups of three, so a decimal-comma file's
  `03.11.2026` stays a date.
- **Gaps, not zeros.** Blank or `n/a`-style samples are stored as `double.NaN` and drawn as gaps. Don't turn them
  into 0.
- **Read while it grows.** The reader opens the file with `FileShare.ReadWrite` so a dump still being written can be
  charted.
- **No trimming.** The publish profiles set `PublishTrimmed` to false because Avalonia resolves XAML by
  reflection. The csproj also removes `.pdb` files from the publish output so it stays one file.
- **The list and the chart agree.** When the visualize list empties, the chart is cleared too.

## Making a change

- Run `dotnet test`. A new CSV shape the reader should handle gets a test in `CsvReaderTests`.
- Check changes by running the app on a real dump, and on `docs/samples/weather.csv`.
- If the UI changes, refresh the screenshots in `docs/screenshots/` and update `README.md` to match.
  All of them come from the tests: set `CSVVIZ_SHOTS` to a folder, run `dotnet test`, and copy the PNGs over.
  `ScreenshotTests` takes 01 to 07 from the made-up dump `docs/samples/web01-20260827-150110.csv`, with popups drawn
  in the window's overlay layer so they're in the picture; `WindowTests` takes 08 from `docs/samples/weather.csv`.

## CI and releases

`.github/workflows/build.yml` runs on every push: it builds, runs the tests, and publishes single-file,
self-contained builds for eight platforms (Windows, macOS and Linux, Intel/AMD and ARM). Pushes to `main` refresh
the rolling `latest` pre-release; a `v*` tag makes a proper release. The publish profiles still work for a build
by hand.
