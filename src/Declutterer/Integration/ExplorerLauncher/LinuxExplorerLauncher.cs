using System;
using System.IO;
using Declutterer.Abstractions;
using Serilog;

namespace Declutterer.Integration.ExplorerLauncher;

public sealed class LinuxExplorerLauncher : IExplorerLauncher
{
    public void OpenInExplorer(string path)
    {
        var fileExists = File.Exists(path);
        if (string.IsNullOrWhiteSpace(path) || !fileExists && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"The specified path does not exist: {path}");
        }

        try
        {
            // 'xdg-open' is the standard cross-desktop utility for opening files and
            // directories in the user's preferred application on Linux.
            // For a directory it opens the file manager; for a file it opens the parent
            // folder in most file managers (behaviour is application-dependent).
            var target = fileExists ? System.IO.Path.GetDirectoryName(path) ?? path : path;
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            System.Diagnostics.Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open path in file manager: {Path}", path);
            throw;
        }
    }
}
