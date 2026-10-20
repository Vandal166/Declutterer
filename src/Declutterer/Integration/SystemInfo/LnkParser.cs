using System;
using System.Text;

namespace Declutterer.Integration.SystemInfo;

/// <summary>
/// Minimal reader for Windows shell link (.lnk) files: extracts the local target path from the LinkInfo block
/// ([MS-SHLLINK] 2.3). Used to learn which files / folders the user opened recently (Recent items) and what the
/// Startup folder launches, without COM interop. Returns null for anything unexpected - never throws.
/// </summary>
public static class LnkParser
{
    private const uint HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;

    public static string? TryGetTargetPath(ReadOnlySpan<byte> data)
    {
        try
        {
            return Parse(data);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static string? Parse(ReadOnlySpan<byte> d)
    {
        if (d.Length < HeaderSize || U32(d, 0) != HeaderSize)
            return null;

        uint flags = U32(d, 0x14);
        int pos = (int)HeaderSize;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (pos + 2 > d.Length) return null;
            pos += 2 + U16(d, pos);
        }

        if ((flags & HasLinkInfo) == 0 || pos + 0x1C > d.Length)
            return null;

        int start = pos;
        uint size = U32(d, start);
        uint headerSize = U32(d, start + 4);
        uint linkInfoFlags = U32(d, start + 8);

        if ((linkInfoFlags & 1) == 0 || size < 0x1C || (long)start + size > d.Length)
            return null; // no local path (network target)

        uint baseOffset = U32(d, start + 16);
        uint suffixOffset = U32(d, start + 24);

        string? basePath = null, suffix = null;
        if (headerSize >= 0x24 && (long)start + 0x24 <= d.Length)
        {
            uint baseUnicode = U32(d, start + 28);
            uint suffixUnicode = U32(d, start + 32);
            if (baseUnicode != 0) basePath = Utf16(d, start + (int)baseUnicode);
            if (suffixUnicode != 0) suffix = Utf16(d, start + (int)suffixUnicode);
        }

        if (string.IsNullOrEmpty(basePath) && baseOffset != 0) basePath = Ansi(d, start + (int)baseOffset);
        if (string.IsNullOrEmpty(suffix) && suffixOffset != 0) suffix = Ansi(d, start + (int)suffixOffset);

        if (string.IsNullOrEmpty(basePath))
            return null;

        return string.IsNullOrEmpty(suffix) ? basePath : basePath.TrimEnd('\\') + "\\" + suffix;
    }

    private static uint U32(ReadOnlySpan<byte> d, int offset) =>
        offset < 0 || offset + 4 > d.Length ? throw new ArgumentOutOfRangeException(nameof(offset)) : BitConverter.ToUInt32(d.Slice(offset, 4));

    private static ushort U16(ReadOnlySpan<byte> d, int offset) =>
        offset < 0 || offset + 2 > d.Length ? throw new ArgumentOutOfRangeException(nameof(offset)) : BitConverter.ToUInt16(d.Slice(offset, 2));

    private static string Ansi(ReadOnlySpan<byte> d, int offset)
    {
        if (offset < 0 || offset >= d.Length) return string.Empty;
        int end = offset;
        while (end < d.Length && d[end] != 0) end++;
        return Encoding.Latin1.GetString(d.Slice(offset, end - offset));
    }

    private static string Utf16(ReadOnlySpan<byte> d, int offset)
    {
        if (offset < 0 || offset >= d.Length) return string.Empty;
        int end = offset;
        while (end + 1 < d.Length && !(d[end] == 0 && d[end + 1] == 0)) end += 2;
        return Encoding.Unicode.GetString(d.Slice(offset, end - offset));
    }
}
