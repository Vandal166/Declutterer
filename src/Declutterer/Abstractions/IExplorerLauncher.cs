namespace Declutterer.Abstractions;

public interface IExplorerLauncher
{
    /// <summary>
    /// Opens the specified path in the system's file explorer.
    /// </summary>
    /// <param name="path">The file or directory path to open</param>
    void OpenInExplorer(string path);
}