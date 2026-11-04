using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Declutterer.Abstractions;

namespace Declutterer.UI.Services.Dialog;

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

        var result = await ShowConfirmationDialogAsync(title, message, _ownerWindow, "Delete", destructive: true);
        return result;
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText)
    {
        if (_ownerWindow is null)
        {
            throw new InvalidOperationException(
                "Owner window has not been set. Call SetOwnerWindow() before showing confirmation dialogs.");
        }

        return await ShowConfirmationDialogAsync(title, message, _ownerWindow, confirmText, destructive: false);
    }

    private static Task<bool> ShowConfirmationDialogAsync(string title, string message, Window owner, string confirmText, bool destructive)
    {
        var tcs = new TaskCompletionSource<bool>();
        var screen = owner.Screens.ScreenFromWindow(owner);
        var maxDialogHeight = screen is null
            ? 650
            : screen.WorkingArea.Height / screen.Scaling * 0.9;

        var dialog = new Window
        {
            Title = title,
            Icon = owner.Icon,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 500,
            MaxHeight = maxDialogHeight,
            CanResize = false,
            Topmost = true
        };

        var content = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 15
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

        var messageScrollViewer = new ScrollViewer
        {
            Content = messageText,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
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
            Content = confirmText,
            Padding = new Thickness(30, 8),
            IsDefault = true,
            Background = new SolidColorBrush(destructive ? Colors.Red : Colors.CornflowerBlue),
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
        Grid.SetRow(messageScrollViewer, 1);
        content.Children.Add(messageScrollViewer);
        Grid.SetRow(buttonPanel, 2);
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
