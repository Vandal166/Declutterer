using System.Threading.Tasks;

namespace Declutterer.Abstractions;

public interface IClipboardService
{
    /// <summary>
    /// Copies the given text to the clipboard.
    /// </summary>
    Task CopyTextAsync(string text);
}
