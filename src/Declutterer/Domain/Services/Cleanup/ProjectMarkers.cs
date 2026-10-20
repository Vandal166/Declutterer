using System;
using System.Collections.Concurrent;
using System.IO;

namespace Declutterer.Domain.Services.Cleanup;

[Flags]
public enum ProjectMarkers
{
    None = 0,
    Git = 1 << 0,
    NodePackage = 1 << 1,
    DotNetProject = 1 << 2,
    Solution = 1 << 3,
    Cargo = 1 << 4,
    Maven = 1 << 5,
    Gradle = 1 << 6,
    CMake = 1 << 7,
    PythonProject = 1 << 8,
    PythonVenv = 1 << 9,
    Podfile = 1 << 10,
    GoModule = 1 << 11,
}

public static class ProjectMarkerProbe
{
    /// <summary>Markers that make a directory "a project" (everything except plain Git / venv).</summary>
    public const ProjectMarkers ProjectLike =
        ProjectMarkers.NodePackage | ProjectMarkers.DotNetProject | ProjectMarkers.Solution | ProjectMarkers.Cargo |
        ProjectMarkers.Maven | ProjectMarkers.Gradle | ProjectMarkers.CMake | ProjectMarkers.PythonProject |
        ProjectMarkers.Podfile | ProjectMarkers.GoModule;

    public static ProjectMarkers FromEntryName(string name, bool isDirectory)
    {
        if (isDirectory)
            return string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ? ProjectMarkers.Git : ProjectMarkers.None;

        // ".git" can also be a file (worktrees / submodules)
        if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
            return ProjectMarkers.Git;

        switch (name.ToLowerInvariant())
        {
            case "package.json": return ProjectMarkers.NodePackage;
            case "cargo.toml": return ProjectMarkers.Cargo;
            case "pom.xml": return ProjectMarkers.Maven;
            case "build.gradle":
            case "build.gradle.kts":
            case "settings.gradle":
            case "settings.gradle.kts": return ProjectMarkers.Gradle;
            case "cmakelists.txt": return ProjectMarkers.CMake;
            case "pyproject.toml":
            case "setup.py":
            case "requirements.txt":
            case "pipfile": return ProjectMarkers.PythonProject;
            case "pyvenv.cfg": return ProjectMarkers.PythonVenv;
            case "podfile": return ProjectMarkers.Podfile;
            case "go.mod": return ProjectMarkers.GoModule;
        }

        var ext = Path.GetExtension(name);
        if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase))
            return ProjectMarkers.DotNetProject;
        if (ext.Equals(".sln", StringComparison.OrdinalIgnoreCase) || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            return ProjectMarkers.Solution;

        return ProjectMarkers.None;
    }
}

/// <summary>Thread-safe cache of "which markers does this directory directly contain" (one listing per directory).</summary>
public sealed class MarkerCache
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    private readonly ConcurrentDictionary<string, ProjectMarkers> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ProjectMarkers GetOwn(string directoryPath) =>
        _cache.GetOrAdd(PathKey.Normalize(directoryPath), static p => Probe(p));

    private static ProjectMarkers Probe(string path)
    {
        var markers = ProjectMarkers.None;
        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
                markers |= ProjectMarkerProbe.FromEntryName(entry.Name, (entry.Attributes & FileAttributes.Directory) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // unreadable -> no markers; the caller treats missing evidence as "unknown", never as "safe"
        }
        return markers;
    }
}
