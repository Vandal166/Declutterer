using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Declutterer.Abstractions;

public interface IIconLoader
{
    Task<Bitmap?> LoadIconAsync(string fullPath, bool isDirectory = false);
    void ClearCache();
}