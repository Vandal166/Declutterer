using System;
using Avalonia.Controls;

namespace Declutterer.Utilities.Extensions;

public static class WindowExtensions
{
    /// <summary>
    /// Resizes the window to a percentage of the current screen's working area (DPI-aware).
    /// The window is then re-centred on screen.
    /// </summary>
    /// <param name="window">The window to resize.</param>
    /// <param name="widthFraction">Fraction of the screen working-area width (0–1). The lesser the value the more the window will be resized down.
    /// <param name="heightFraction">Fraction of the screen working-area height (0–1). The lesser the value the more the window will be resized down.</param>
    public static void FitToScreen(this Window window, double widthFraction = 0.85, double heightFraction = 0.85)
    {
        var screen = window.Screens.ScreenFromWindow(window);
        if (screen is null)
            return;

        // WorkingArea is in physical pixels; divide by Scaling to get logical (DIP) pixels
        var scaling = screen.Scaling;
        var workingWidth  = screen.WorkingArea.Width  / scaling;
        var workingHeight = screen.WorkingArea.Height / scaling;

        var desiredWidth  = workingWidth  * widthFraction;
        var desiredHeight = workingHeight * heightFraction;

        // Never go below the declared minimums
        var newWidth  = Math.Max(window.MinWidth,  desiredWidth);
        var newHeight = Math.Max(window.MinHeight, desiredHeight);

        // Never exceed the working area
        newWidth  = Math.Min(newWidth,  workingWidth);
        newHeight = Math.Min(newHeight, workingHeight);

        window.Width  = newWidth;
        window.Height = newHeight;

        // Manually center the window within the screen's working area (in logical pixels)
        var workingAreaLeft = screen.WorkingArea.X / scaling;
        var workingAreaTop  = screen.WorkingArea.Y / scaling;

        window.Position = new Avalonia.PixelPoint(
            (int)(workingAreaLeft + (workingWidth  - newWidth)  / 2 * scaling),
            (int)(workingAreaTop  + (workingHeight - newHeight) / 2 * scaling)
        );
    }
}

