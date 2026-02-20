namespace Declutterer.Tests.Helpers;

/// <summary>
/// Helper for creating and managing temporary test directories.
/// Implements IDisposable for automatic cleanup.
/// </summary>
public class TempTestDirectory : IDisposable
{
    public string Path { get; }

    public TempTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DecluttererTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(Path);
    }

    public string CreateSubDirectory(string name)
    {
        var subPath = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(subPath);
        return subPath;
    }

    public string CreateFile(string relativePath, long sizeInBytes = 1024)
    {
        var filePath = System.IO.Path.Combine(Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create file with specified size
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        fs.SetLength(sizeInBytes);

        return filePath;
    }

    public void SetLastModified(string relativePath, DateTime dateTime)
    {
        var filePath = System.IO.Path.Combine(Path, relativePath);
        if (File.Exists(filePath))
        {
            File.SetLastWriteTime(filePath, dateTime);
        }
        else if (Directory.Exists(filePath))
        {
            Directory.SetLastWriteTime(filePath, dateTime);
        }
    }

    public void SetLastAccessed(string relativePath, DateTime dateTime)
    {
        var filePath = System.IO.Path.Combine(Path, relativePath);
        if (File.Exists(filePath))
        {
            File.SetLastAccessTime(filePath, dateTime);
        }
        else if (Directory.Exists(filePath))
        {
            Directory.SetLastAccessTime(filePath, dateTime);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
