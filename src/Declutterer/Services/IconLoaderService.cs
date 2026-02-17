using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Declutterer.Abstractions;

namespace Declutterer.Services;

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
            //bitmap = await LoadMacOSIconAsync(fullPath);
        }
        else // Linux or fallback
        {
            // Optional: Load a generic folder/file icon from assets
            // e.g., bitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Declutterer/Assets/folder.png")));
            // You'll need to add actual asset files to your project and adjust the URI accordingly.
        }

        if (bitmap != null)
        {
            _iconCache[fullPath] = bitmap;
        }

        return bitmap;
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