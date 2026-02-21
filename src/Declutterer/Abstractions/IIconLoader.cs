using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Declutterer.Abstractions;

/// <summary>
/// Responsible for loading icons for files and directories, with caching support.
/// </summary>
public interface IIconLoader
{
    Task<Bitmap?> LoadIconAsync(string fullPath, bool isDirectory = false);
    void ClearCache();
}