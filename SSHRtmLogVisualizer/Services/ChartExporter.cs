using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ScottPlot;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using PixelSize = Avalonia.PixelSize;

namespace SSHRtmLogVisualizer.Services;

/// <summary>Writes the current chart out to an image file or onto the clipboard.</summary>
public static class ChartExporter
{
    /// <summary>Floor for exported images so a small window still yields a usable picture.</summary>
    private const int MinimumWidth = 1000;
    private const int MinimumHeight = 560;

    /// <summary>
    /// Prompts for a path and writes the chart there. Returns a status message; returns null
    /// when the user cancelled the dialog.
    /// </summary>
    public static async Task<string?> SaveAsync(Plot plot, TopLevel topLevel, PixelSize size, string suggestedName)
    {
        IStorageFile? target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save chart image",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new FilePickerFileType("Bitmap image") { Patterns = new[] { "*.bmp" } },
            },
        });

        if (target is null)
        {
            return null;
        }

        string? path = target.TryGetLocalPath();
        if (path is null)
        {
            return "That location is not a local file, so the chart could not be written there.";
        }

        (int width, int height) = Clamp(size);
        ImageFormat format = FormatFor(path);

        // The picker's type filter does not always append an extension, so make sure the file
        // name matches the format we are about to write.
        path = EnsureExtension(path, format);

        plot.Save(path, width, height, format);
        return $"Saved {width} × {height} {format.ToString().ToUpperInvariant()} to {path}";
    }

    /// <summary>Puts the chart on the clipboard as a PNG. Returns a status message.</summary>
    public static async Task<string> CopyAsync(Plot plot, TopLevel topLevel, PixelSize size)
    {
        IClipboard? clipboard = topLevel.Clipboard;
        if (clipboard is null)
        {
            return "This platform does not expose a clipboard.";
        }

        (int width, int height) = Clamp(size);
        byte[] png = plot.GetImageBytes(width, height, ImageFormat.Png);

        using var stream = new MemoryStream(png);
        var bitmap = new AvaloniaBitmap(stream);
        await clipboard.SetBitmapAsync(bitmap);

        return $"Copied the chart to the clipboard as a {width} × {height} PNG.";
    }

    /// <summary>Suggests a file name built from the dump's own name.</summary>
    public static string SuggestName(string? dumpPath)
    {
        string stem = string.IsNullOrWhiteSpace(dumpPath)
            ? "sshrtm"
            : Path.GetFileNameWithoutExtension(dumpPath);

        return $"{stem}-chart.png";
    }

    private static (int Width, int Height) Clamp(PixelSize size) =>
        (Math.Max(MinimumWidth, size.Width), Math.Max(MinimumHeight, size.Height));

    private static ImageFormat FormatFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            ".webp" => ImageFormat.Webp,
            _ => ImageFormat.Png,
        };

    private static string EnsureExtension(string path, ImageFormat format)
    {
        string wanted = format switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Bmp => ".bmp",
            ImageFormat.Webp => ".webp",
            _ => ".png",
        };

        string current = Path.GetExtension(path);
        if (current.Equals(wanted, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        // ".jpeg" is as valid as ".jpg" — don't append a second extension to it.
        if (format == ImageFormat.Jpeg && current.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path + wanted;
    }
}
