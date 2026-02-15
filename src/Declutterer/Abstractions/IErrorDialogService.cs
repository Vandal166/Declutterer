using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Declutterer.Abstractions;

/// <summary>
/// Service for displaying error dialogs to the user.
/// Provides a consistent and reusable way to show error messages across the app.
/// </summary>
public interface IErrorDialogService
{
    /// <summary>
    /// Sets the parent window for error dialogs.
    /// Must be called before showing any dialogs.
    /// </summary>
    /// <param name="owner">The parent window that owns the error dialog</param>
    void SetOwnerWindow(Window owner);

    /// <summary>
    /// Shows an error dialog with the specified title and message.
    /// </summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows an error dialog with the specified title and message derived from an exception.
    /// </summary>
    /// <param name="exception">The exception to extract details from</param>
    Task ShowErrorAsync(string title, string message, Exception exception);
}
