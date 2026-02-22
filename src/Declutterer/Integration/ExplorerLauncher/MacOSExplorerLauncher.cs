using System;
using System.IO;
using Declutterer.Abstractions;
using Serilog;

namespace Declutterer.Integration.ExplorerLauncher;

public sealed class MacOSExplorerLauncher : IExplorerLauncher
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
            // 'open' is the standard macOS command-line tool for opening files and
            // directories in Finder.  The '-R' flag reveals (selects) the item inside
            // its parent Finder window, mirroring the Windows "explorer /select," behaviour.
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = fileExists ? $"-R \"{path}\"" : $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            System.Diagnostics.Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open path in Finder: {Path}", path);
            throw;
        }
    }
}
