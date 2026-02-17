using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Declutterer.Abstractions;
using Serilog;

namespace Declutterer.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private IClipboard? _clipboard;

    public void SetClipboard(IClipboard? clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task CopyTextAsync(string text)
    {
        try
        {
            if (_clipboard is null)
            {
                Log.Warning("Clipboard service not initialized");
                return;
            }

            await _clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy text to clipboard");
        }
    }
}
