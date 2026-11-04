using Declutterer.Abstractions;

namespace Declutterer.Integration.FileIdentity;

/// <summary>No stable identity (Linux, macOS): a moved item is not recognised, so it reads as Missing and is never treated as quiet.</summary>
public sealed class PathOnlyFileIdentityProvider : IFileIdentityProvider
{
    public bool IsSupported => false;

    public string? TryGetFileId(string path) => null;
}
