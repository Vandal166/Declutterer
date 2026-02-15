using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Declutterer.Abstractions;

namespace Declutterer.Services;

public sealed class ErrorDialogService : IErrorDialogService
{
    private Window? _ownerWindow;

    public void SetOwnerWindow(Window owner)
    {
        _ownerWindow = owner ?? throw new ArgumentNullException(nameof(owner), "Owner window cannot be null");
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (_ownerWindow is null)
        {
            throw new InvalidOperationException(
                "Owner window has not been set. Call SetOwnerWindow() before showing error dialogs.");
        }

        var dialog = CreateErrorDialog(title, message);
        await dialog.ShowDialog(_ownerWindow);
    }

    public async Task ShowErrorAsync(string title, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detailedMessage = BuildDetailedMessage(message, exception);
        await ShowErrorAsync(title, detailedMessage);
    }

    private static string BuildDetailedMessage(string message, Exception exception)
    {
        var details = exception.Message;
        
        // Include inner exception if present
        if (exception.InnerException != null)
        {
            details += $"\n\nInner Exception: {exception.InnerException.Message}";
        }
        
        return $"{message}\n\nDetails: {details}";
    }

    /// <summary>
    /// Creates an error dialog window with the specified title and message.
    /// </summary>
    private static Window CreateErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
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

        // Error icon/title area
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
            Foreground = new SolidColorBrush(Colors.DarkRed),
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

        // OK button
        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(30, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.Close();

        content.Children.Add(titlePanel);
        content.Children.Add(messageText);
        content.Children.Add(okButton);

        dialog.Content = content;
        return dialog;
    }
}
