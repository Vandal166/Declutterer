using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Declutterer.Abstractions;
using Serilog;

namespace Declutterer.UI.Services.Clipboard;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private IClipboard? _clipboard;
    private readonly IErrorDialogService _errorDialogService;

    public AvaloniaClipboardService(IErrorDialogService errorDialogService)
    {
        _errorDialogService = errorDialogService ?? throw new ArgumentNullException(nameof(errorDialogService));
    }

    public void SetClipboard(IClipboard? clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task CopyTextAsync(string? text)
    {
        try
        {
            if (_clipboard is null)
            {
                Log.Warning("Clipboard service not initialized");
                return;
            }

            if (text is null)
                return;

            await _clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy text to clipboard: {Text}", text);
            await _errorDialogService.ShowErrorAsync(
                "Failed to Copy Path",
                $"Could not copy the path to clipboard:\n{text}",
                ex);
        }
    }
}
