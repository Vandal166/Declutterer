namespace Declutterer.Abstractions;

public interface IExplorerLauncher
{
    /// <summary>
    /// Opens the specified path in the system's file explorer.
    /// </summary>
    /// <param name="path">The file or directory path to open</param>
    /// <returns>A task that completes when the explorer has been launched</returns>
    void OpenInExplorer(string path);
}