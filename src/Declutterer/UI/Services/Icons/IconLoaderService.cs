using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Declutterer.Abstractions;

namespace Declutterer.UI.Services.Icons;

public class IconLoaderService : IIconLoader
{
    private static readonly ConcurrentDictionary<string, Bitmap> _iconCache = new();

    /// <summary>
    /// Clears the icon bitmap cache. Call this before a new scan to ensure icons are reloaded.
    /// </summary>
    public void ClearCache()
    {
        _iconCache.Clear();
    }
    public async Task<Bitmap?> LoadIconAsync(string fullPath, bool isDirectory = false)
    {
        if (_iconCache.TryGetValue(fullPath, out var cached))
            return cached;

        Bitmap? bitmap = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bitmap = await LoadWindowsIconAsync(fullPath, isDirectory);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            bitmap = await LoadMacOSIconAsync(fullPath, isDirectory);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bitmap = await LoadLinuxIconAsync(fullPath, isDirectory);
        }

        if (bitmap != null)
        {
            _iconCache[fullPath] = bitmap;
        }

        return bitmap;
    }
    
    private static Task<Bitmap?> LoadMacOSIconAsync(string fullPath, bool isDirectory)
    {
        return Task.Run(() =>
        {
            try
            {
                // Use the 'sips' command-line tool on macOS to convert icon to PNG
                // This is reliable and doesn't require external dependencies
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/sips",
                    Arguments = $"-z 32 32 '{fullPath}' --out /tmp/icon_temp.png",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                        return null;

                    process.WaitForExit(5000); // 5 second timeout

                    if (process.ExitCode == 0 && File.Exists("/tmp/icon_temp.png"))
                    {
                        try
                        {
                            var bitmap = new Bitmap("/tmp/icon_temp.png");
                            File.Delete("/tmp/icon_temp.png"); // Clean up temp file
                            return bitmap;
                        }
                        catch
                        {
                            File.Delete("/tmp/icon_temp.png");
                            return null;
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    private static Task<Bitmap?> LoadLinuxIconAsync(string fullPath, bool isDirectory)
    {
        return Task.Run(() =>
        {
            try
            {
                // Try using 'gio' (GIO - GLib I/O) to get the file's icon names
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/gio",
                    Arguments = $"info -a standard::icon \"{fullPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(5000);

                        // gio output format: "  standard::icon: icon-name-1 icon-name-2 ..."
                        const string marker = "standard::icon:";
                        var markerIndex = output.IndexOf(marker, StringComparison.Ordinal);
                        if (markerIndex >= 0)
                        {
                            var iconLine = output[(markerIndex + marker.Length)..]
                                .Split('\n')[0]
                                .Trim();

                            // gio may return multiple space-separated icon names in priority order
                            var iconNames = iconLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var name in iconNames)
                            {
                                var bitmap = LoadLinuxIconFromTheme(name);
                                if (bitmap != null)
                                    return bitmap;
                            }
                        }
                    }
                }

                // Fallback: derive icon name from file extension or directory flag
                return LoadLinuxIconFromExtension(fullPath, isDirectory);
            }
            catch
            {
                return null;
            }
        });
    }

    private static Bitmap? LoadLinuxIconFromTheme(string iconName)
    {
        try
        {
            // Search order: user theme overrides first, then system themes
            var home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;

            var themeDirs = new[]
            {
                Path.Combine(home, ".local/share/icons/hicolor"),
                "/usr/share/icons/hicolor",
                "/usr/share/icons/Adwaita",
                "/usr/share/icons/gnome",
                "/usr/share/icons/oxygen",
            };

            var sizes = new[] { "32x32", "24x24", "48x48", "16x16", "scalable" };
            var categories = new[] { "mimetypes", "places", "apps", "actions", "categories", "status" };
            var formats = new[] { ".png", ".jpg" }; // skip SVG — Avalonia requires extra work to rasterise

            foreach (var themeDir in themeDirs)
            {
                if (!Directory.Exists(themeDir))
                    continue;

                foreach (var size in sizes)
                {
                    foreach (var category in categories)
                    {
                        var dir = Path.Combine(themeDir, size, category);
                        if (!Directory.Exists(dir))
                            continue;

                        foreach (var format in formats)
                        {
                            var iconPath = Path.Combine(dir, iconName + format);
                            if (!File.Exists(iconPath))
                                continue;
                            try
                            {
                                return new Bitmap(iconPath);
                            }
                            catch
                            {
                                // file exists but is unreadable — try next
                            }
                        }
                    }
                }
            }

            // Also check /usr/share/pixmaps (flat directory, no size/category subdirs)
            foreach (var format in new[] { ".png", ".jpg" })
            {
                var pixmapPath = Path.Combine("/usr/share/pixmaps", iconName + format);
                if (!File.Exists(pixmapPath))
                    continue;
                try
                {
                    return new Bitmap(pixmapPath);
                }
                catch
                {
                    // continue
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadLinuxIconFromExtension(string fullPath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                return LoadLinuxIconFromTheme("folder");
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var iconName = extension switch
            {
                ".txt" or ".log" or ".md" => "text-x-generic",
                ".pdf"                   => "application-pdf",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "image-x-generic",
                ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" => "audio-x-generic",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".webm" => "video-x-generic",
                ".zip" or ".tar" or ".gz" or ".bz2" or ".xz" or ".rar" or ".7z" => "package-x-generic",
                _                        => "text-x-generic"
            };

            return LoadLinuxIconFromTheme(iconName);
        }
        catch
        {
            return null;
        }
    }
    
    private static Task<Bitmap?> LoadWindowsIconAsync(string fullPath, bool isDirectory)
    {
        return Task.Run(() =>
        {
            const uint SHGFI_ICON = 0x100;
            const uint SHGFI_SMALLICON = 0x1;

            uint flags = SHGFI_ICON | SHGFI_SMALLICON; // Use LARGEICON (0x0) if you want 32x32 instead
            uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            var shinfo = new SHFILEINFO();
            IntPtr hImg = SHGetFileInfo(fullPath, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (hImg == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            using var icon = System.Drawing.Icon.FromHandle(shinfo.hIcon);
            using var sysBitmap = icon.ToBitmap();
            using var memory = new MemoryStream();
            sysBitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            memory.Position = 0;

            DestroyIcon(shinfo.hIcon);

            return new Bitmap(memory);
        });
    }

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

}