using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Declutterer.Abstractions;

namespace Declutterer.UI.Services.Icons;

public sealed class MacOSIconLoader : IIconLoader
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
        
        bitmap = await LoadMacOSIconAsync(fullPath, isDirectory);
        
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
}