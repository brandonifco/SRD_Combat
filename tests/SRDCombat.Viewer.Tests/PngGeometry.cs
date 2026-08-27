namespace SRDCombat.Viewer.Tests;

/// <summary>
/// Reads a PNG's canvas size straight from its header bytes — no <c>Godot.Image</c>
/// involved, deliberately: that type is a native handle (see this project's own file's
/// doc comment) and constructing one outside a running engine takes the whole test host
/// down with it. A PNG's width and height are the first eight bytes of its first chunk
/// (the IHDR, which PNG requires to come first), big-endian, sitting right after the
/// fixed 8-byte signature and the chunk's own 4-byte length and 4-byte type — offsets
/// 16 and 20. Nothing here decodes a pixel; it reads sixteen bytes of one specific file
/// format, which is exactly what a canvas-dimensions gate needs and no more.
/// </summary>
internal static class PngGeometry
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static (int Width, int Height) Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];

        if (stream.Read(header) != header.Length)
        {
            throw new InvalidDataException($"'{path}' is too short to be a PNG.");
        }

        if (!header[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException($"'{path}' does not start with the PNG signature.");
        }

        if (!header.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException($"'{path}'s first chunk is not IHDR — malformed or unsupported PNG.");
        }

        var width = ReadUInt32BigEndian(header.Slice(16, 4));
        var height = ReadUInt32BigEndian(header.Slice(20, 4));
        return (checked((int)width), checked((int)height));
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
}
