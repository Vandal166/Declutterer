using Declutterer.Integration.ExplorerLauncher;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Integration;

public class ExplorerLauncherTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;

    public ExplorerLauncherTests()
    {
        _tempDir = new TempTestDirectory();
    }

    // ────────────────────────────────────────────────────────────────
    // LinuxExplorerLauncher
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void LinuxExplorerLauncher_NonExistentPath_ThrowsFileNotFoundException()
    {
        var launcher = new LinuxExplorerLauncher();
        var nonExistentPath = Path.Combine(_tempDir.Path, "does_not_exist");

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer(nonExistentPath));
    }

    [Fact]
    public void LinuxExplorerLauncher_NullOrWhitespacePath_ThrowsFileNotFoundException()
    {
        var launcher = new LinuxExplorerLauncher();

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer("   "));
    }

    // ────────────────────────────────────────────────────────────────
    // MacOSExplorerLauncher
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void MacOSExplorerLauncher_NonExistentPath_ThrowsFileNotFoundException()
    {
        var launcher = new MacOSExplorerLauncher();
        var nonExistentPath = Path.Combine(_tempDir.Path, "does_not_exist");

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer(nonExistentPath));
    }

    [Fact]
    public void MacOSExplorerLauncher_NullOrWhitespacePath_ThrowsFileNotFoundException()
    {
        var launcher = new MacOSExplorerLauncher();

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer("   "));
    }

    // ────────────────────────────────────────────────────────────────
    // WindowsExplorerLauncher (guard tests that run on all platforms)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void WindowsExplorerLauncher_NonExistentPath_ThrowsFileNotFoundException()
    {
        var launcher = new WindowsExplorerLauncher();
        var nonExistentPath = Path.Combine(_tempDir.Path, "does_not_exist");

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer(nonExistentPath));
    }

    [Fact]
    public void WindowsExplorerLauncher_NullOrWhitespacePath_ThrowsFileNotFoundException()
    {
        var launcher = new WindowsExplorerLauncher();

        Assert.Throws<FileNotFoundException>(() => launcher.OpenInExplorer("   "));
    }

    public void Dispose() => _tempDir.Dispose();
}
