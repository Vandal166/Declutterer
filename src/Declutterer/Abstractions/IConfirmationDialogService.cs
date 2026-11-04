using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Declutterer.Abstractions;

/// <summary>
/// Service for displaying confirmation dialogs to the user.
/// Provides a consistent and reusable way to show confirmation messages across the app.
/// </summary>
public interface IConfirmationDialogService
{
    /// <summary>
    /// Sets the parent window for confirmation dialogs.
    /// Must be called before showing any dialogs.
    /// </summary>
    /// <param name="owner">The parent window that owns the confirmation dialog</param>
    void SetOwnerWindow(Window owner);

    /// <summary>
    /// Shows a confirmation dialog with the specified title and message.
    /// </summary>
    /// <returns>True if user clicked OK/Yes, false if user clicked Cancel/No</returns>
    Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog whose confirming button carries <paramref name="confirmText"/>. Unlike the overload above (which is
    /// the red "Delete" confirmation) this one has a neutral style, for decisions that do not delete anything.
    /// </summary>
    /// <returns>True if the user confirmed, false if they cancelled or closed the dialog</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText);
}