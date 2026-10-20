using System;
using System.Collections.Generic;
using System.IO;

namespace Declutterer.Domain.Services.Cleanup;

public enum FileClass
{
    Unknown = 0,
    Document,
    Image,
    Audio,
    Video,
    Source,
    Database,
    Archive,
    DiskImage,
    Installer,
    Executable,
    Library,
    Config,
    Log,
    Temp,
    Backup,
    Dump,
    Credential,
}

/// <summary>Cheap, extension / name based classification of files. No I/O.</summary>
public static class FileClassifier
{
    public static readonly int ClassCount = Enum.GetValues<FileClass>().Length;

    private static readonly Dictionary<string, FileClass> ByExtension = Build();

    private static readonly HashSet<string> TempNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", ".ds_store", "ehthumbs.db", "npm-debug.log", "yarn-error.log",
    };

    private static readonly HashSet<string> CredentialNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "wallet.dat", ".git-credentials", ".netrc", "authorized_keys",
    };

    private static Dictionary<string, FileClass> Build()
    {
        var d = new Dictionary<string, FileClass>(StringComparer.OrdinalIgnoreCase);
        void Add(FileClass c, params string[] exts)
        {
            foreach (var e in exts) d[e] = c;
        }

        Add(FileClass.Document, ".doc", ".docx", ".docm", ".odt", ".rtf", ".pdf", ".xls", ".xlsx", ".xlsm", ".ods", ".csv",
            ".ppt", ".pptx", ".odp", ".txt", ".md", ".epub", ".mobi", ".tex", ".pages", ".numbers", ".key", ".one", ".vsdx", ".psd", ".ai", ".indd");
        Add(FileClass.Image, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw", ".dng", ".svg", ".ico");
        Add(FileClass.Audio, ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma", ".opus", ".mid");
        Add(FileClass.Video, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg");
        Add(FileClass.Source, ".cs", ".vb", ".fs", ".java", ".kt", ".py", ".js", ".ts", ".jsx", ".tsx", ".c", ".h", ".cpp", ".hpp", ".rs", ".go",
            ".rb", ".php", ".swift", ".sh", ".ps1", ".bat", ".cmd", ".sql", ".html", ".css", ".scss", ".xaml", ".axaml", ".sln", ".csproj", ".gd", ".lua", ".ipynb");
        Add(FileClass.Database, ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".mdf", ".ldf", ".sdf", ".pst", ".ost", ".kdbx", ".kdb", ".realm", ".wallet");
        Add(FileClass.Archive, ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".zst");
        Add(FileClass.DiskImage, ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".wim", ".esd");
        Add(FileClass.Installer, ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".msp");
        Add(FileClass.Executable, ".exe", ".com", ".scr", ".jar", ".apk", ".appimage");
        Add(FileClass.Library, ".dll", ".so", ".dylib", ".sys", ".ocx", ".pyd", ".node");
        Add(FileClass.Config, ".json", ".xml", ".ini", ".cfg", ".conf", ".yaml", ".yml", ".toml", ".plist", ".reg", ".settings", ".config", ".lnk", ".url");
        Add(FileClass.Log, ".log", ".etl", ".trace", ".out", ".err");
        Add(FileClass.Temp, ".tmp", ".temp", ".crdownload", ".part", ".partial", ".download", ".chk", ".gid", ".swp", ".swo", ".cache", ".dmp.tmp");
        Add(FileClass.Backup, ".bak", ".old", ".orig", ".bkp", ".backup", ".prev");
        Add(FileClass.Dump, ".dmp", ".mdmp", ".hdmp");
        Add(FileClass.Credential, ".ppk", ".pfx", ".p12", ".pem", ".pub", ".asc", ".gpg", ".jks", ".keystore");
        return d;
    }

    public static FileClass Classify(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return FileClass.Unknown;

        if (TempNames.Contains(fileName))
            return FileClass.Temp;
        if (CredentialNames.Contains(fileName))
            return FileClass.Credential;
        // Office lock files: ~$report.docx
        if (fileName.StartsWith("~$", StringComparison.Ordinal))
            return FileClass.Temp;

        var ext = Path.GetExtension(fileName);
        if (ext.Length == 0)
            return FileClass.Unknown;

        return ByExtension.TryGetValue(ext, out var cls) ? cls : FileClass.Unknown;
    }

    /// <summary>Content that people create or can not trivially recreate.</summary>
    public static bool IsValuable(FileClass c) =>
        c is FileClass.Document or FileClass.Image or FileClass.Audio or FileClass.Video
            or FileClass.Source or FileClass.Database or FileClass.Credential;

    /// <summary>Content that is by nature disposable.</summary>
    public static bool IsDisposable(FileClass c) =>
        c is FileClass.Log or FileClass.Temp or FileClass.Dump or FileClass.Backup;

    /// <summary>Media/source files inside application data are usually bundled assets, not user creations.</summary>
    public static bool IsAssetLike(FileClass c) =>
        c is FileClass.Image or FileClass.Audio or FileClass.Video or FileClass.Source;
}
