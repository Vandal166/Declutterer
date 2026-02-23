using Declutterer.Domain.Services.Deletion;
using Declutterer.Tests.Helpers;
using Declutterer.Utilities.Exceptions;

namespace Declutterer.Tests.Services;

public class PathSafetyValidatorTests : IDisposable
{
    private readonly TempTestDirectory _tempDir = new();

    // ── FileAttributes.System checks ────────────────────────────────────────────

    [Fact]
    public void Validate_PathWithSystemAttribute_ThrowsOperationFailedException()
    {
        // Create a file and explicitly set the System attribute on it.
        var filePath = _tempDir.CreateFile("system-flagged.txt", 128);
        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.System);

        Assert.Throws<OperationFailedException>(() =>
            PathSafetyValidator.Validate(filePath));
    }

    [Fact]
    public void Validate_SubfolderOfDirectoryWithSystemAttribute_ThrowsOperationFailedException()
    {
        // Parent directory is flagged as System — deleting the child must be blocked
        // because an ancestor carries the System attribute.
        var parentDir = _tempDir.CreateSubDirectory("system-parent");
        var subDir = Directory.CreateDirectory(Path.Combine(parentDir, "child"));
        File.SetAttributes(parentDir, File.GetAttributes(parentDir) | FileAttributes.System);

        Assert.Throws<OperationFailedException>(() =>
            PathSafetyValidator.Validate(subDir.FullName));
    }

    [Fact]
    public void Validate_NormalPath_DoesNotThrow()
    {
        var filePath = _tempDir.CreateFile("normal.txt", 128);
        // Ensure the file does NOT carry the System attribute.
        File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.System);

        var ex = Record.Exception(() => PathSafetyValidator.Validate(filePath));
        Assert.Null(ex);
    }

    // ── Argument validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrWhitespacePath_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => PathSafetyValidator.Validate(path));
    }

    // ── Environment.SpecialFolder checks ────────────────────────────────────────

    [Fact]
    public void Validate_OsCriticalSpecialFolderItself_ThrowsOperationFailedException()
    {
        // OS-critical special folders must be blocked when targeted directly.
        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        }.Where(p => !string.IsNullOrEmpty(p));

        foreach (var protectedPath in protectedPaths)
        {
            Assert.Throws<OperationFailedException>(() =>
                PathSafetyValidator.Validate(protectedPath));
        }
    }

    [Fact]
    public void Validate_DescendantOfOsCriticalFolder_ThrowsOperationFailedException()
    {
        // Subfolders of OS-critical directories (Windows, System32, Program Files …)
        // must also be blocked — not just the root folder itself.
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windowsDir))
            return; // not on Windows — skip

        var descendant = Path.Combine(windowsDir, "SomeFakeSubfolder");

        Assert.Throws<OperationFailedException>(() =>
            PathSafetyValidator.Validate(descendant));
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    [InlineData(Environment.SpecialFolder.MyVideos)]
    public void Validate_UserSpecialFolderItself_ThrowsOperationFailedException(
        Environment.SpecialFolder folder)
    {
        // Deleting the special folder itself must always be blocked — even for user-owned
        // folders that are NOT in ProtectDescendants (their contents remain accessible).
        var folderPath = Environment.GetFolderPath(folder);
        if (string.IsNullOrEmpty(folderPath))
            return; // folder not configured on this machine — skip

        Assert.Throws<OperationFailedException>(() =>
            PathSafetyValidator.Validate(folderPath));
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    [InlineData(Environment.SpecialFolder.MyVideos)]
    public void Validate_ContentInsideUserSpecialFolder_DoesNotThrow(
        Environment.SpecialFolder folder)
    {
        // Contents of user-owned special folders must NOT be blocked —
        // only the root folder itself is protected by the exact-match rule.
        var folderPath = Environment.GetFolderPath(folder);
        if (string.IsNullOrEmpty(folderPath))
            return; // folder not configured on this machine — skip

        // Validate a hypothetical child path without creating a real file inside
        // the user's actual Documents/Desktop/etc.
        var childPath = Path.Combine(folderPath, "SomeFile_ThatDoesNotExist.txt");

        var ex = Record.Exception(() => PathSafetyValidator.Validate(childPath));
        Assert.Null(ex);
    }

    public void Dispose() => _tempDir.Dispose();
}


