using System;
using System.IO;
using Declutterer.Abstractions;
using Serilog;

namespace Declutterer.Integration.ExplorerLauncher;

public sealed class WindowsExplorerLauncher : IExplorerLauncher
{
    public void OpenInExplorer(string path)
    {
        var fileExists = File.Exists(path);
        if(string.IsNullOrWhiteSpace(path) || !fileExists && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"The specified path does not exist: {path}");
        }

        try
        {
            // Use "explorer.exe" to open the path in Windows Explorer
            // The /select, option will select the file if it's a file, or just open the folder if it's a directory
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
              FileName = "explorer.exe",
              Arguments = fileExists ? $"/select,\"{path}\"" : $"\"{path}\"",
              UseShellExecute = true
            };

            System.Diagnostics.Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open path in Windows Explorer: {Path}", path);
            throw;
        }
    }
}