using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Declutterer.Abstractions;

namespace Declutterer.Services;

/// <summary>
/// Service for displaying confirmation dialogs to the user.
/// </summary>
public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    private Window? _ownerWindow;

    public void SetOwnerWindow(Window owner)
    {
        _ownerWindow = owner ?? throw new ArgumentNullException(nameof(owner), "Owner window cannot be null");
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        if (_ownerWindow is null)
        {
            throw new InvalidOperationException(
                "Owner window has not been set. Call SetOwnerWindow() before showing confirmation dialogs.");
        }

        var result = await ShowConfirmationDialogAsync(title, message, _ownerWindow);
        return result;
    }

    //TODO the action buttons may not be visible if the content is too long
    private static Task<bool> ShowConfirmationDialogAsync(string title, string message, Window owner)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = title,
            Icon = owner.Icon,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 500,
            Height = 200,
            CanResize = false,
            Topmost = true
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Title area
        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.DarkBlue),
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(titleText);

        // Message area with better formatting
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Button panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // Cancel button
        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(30, 8),
            IsCancel = true
        };
        cancelButton.Click += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(false);
            }
            dialog.Close();
        };

        // OK button
        var okButton = new Button
        {
            Content = "Delete",
            Padding = new Thickness(30, 8),
            IsDefault = true,
            Background = new SolidColorBrush(Colors.Red),
            Foreground = new SolidColorBrush(Colors.White)
        };
        okButton.Click += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(true);
            }
            dialog.Close();
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        content.Children.Add(titlePanel);
        content.Children.Add(messageText);
        content.Children.Add(buttonPanel);

        dialog.Content = content;
        
        // Handle dialog closing without button click (e.g., clicking X button)
        dialog.Closing += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(false);
            }
        };
        
        _ = dialog.ShowDialog(owner);

        return tcs.Task;
    }
}
