namespace Declutterer.Abstractions;

/// <summary>
/// Gives a file or folder a stable identity that survives a rename or move (Windows: volume serial plus file index).
/// Where there is no such identity the provider is path-only: a moved item then reads as Missing, never as quiet.
/// </summary>
public interface IFileIdentityProvider
{
    bool IsSupported { get; }

    /// <summary>The identity of the item at <paramref name="path"/>, or null if unsupported or the item can not be opened.</summary>
    string? TryGetFileId(string path);
}
