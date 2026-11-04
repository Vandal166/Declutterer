using System;
using Avalonia;

namespace Declutterer.Application;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // "Declutterer --observe": one background observation check-in for the operating system's scheduler. No window is created and
        // nothing of Avalonia is touched.
        if (ObservationHeadless.IsRequested(args))
            return ObservationHeadless.Run(args);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}