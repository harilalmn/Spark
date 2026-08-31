using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Spark.Viewport.Software;

/// <summary>
/// A minimal PNG reader and writer for 8-bit RGBA images, with no dependency on a UI toolkit or
/// an imaging package.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists rather than a package.</b> <c>Spark.Viewport</c> takes no Avalonia
/// dependency by architecture rule, which is what lets it render headlessly at all — and a
/// headless render nobody can look at is not much use. Both callers need exactly one format:
/// <c>spark render</c> writing a picture from the command line, and the visual regression check
/// reading a committed golden image back. Eight-bit RGBA, no interlacing, one image: a hundred
/// lines rather than a dependency and a licence.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> Palettes, greyscale, 16-bit channels, interlacing,
/// gamma and colour-profile chunks, and animation. <see cref="Decode"/> refuses anything it does
/// not understand with a message naming what it found, because a decoder that silently returns
/// approximately the right thing is worse here than one that stops — the whole point of the
/// golden image is that the bytes are the bytes.
/// </para>
/// <para>
/// Rows are top-first in both directions, matching <see cref="SoftwareFramebuffer"/> and PNG
/// itself.
/// </para>
/// </remarks>
public static class PngImage
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes 8-bit RGBA pixels as a PNG.</summary>
    /// <param name="pixels">
    /// <c>width * height * 4</c> bytes, top row first, four channels per pixel.
    /// </param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>The complete PNG file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pixels"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException">The pixel count does not match the dimensions.</exception>
    public static byte[] Encode(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int expected = width * height * 4;
        if (pixels.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for {width}x{height} RGBA, got {pixels.Length}.", nameof(pixels));
        }

        // Filter type 0 (None) on every scanline. The alternative filters exist to help the
        // compressor and would make the output depend on a heuristic; this file is compared byte
        // for byte in CI, so a smaller file is worth less than a predictable one.
        byte[] raw = new byte[height * ((width * 4) + 1)];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            int destination = row * (stride + 1);
            raw[destination] = 0;
            Array.Copy(pixels, row * stride, raw, destination + 1, stride);
        }

        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        using MemoryStream file = new();
        file.Write(Signature, 0, Signature.Length);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;      // Bit depth.
        header[9] = 6;      // Colour type 6: truecolour with alpha.
        header[10] = 0;     // Compression method: deflate.
        header[11] = 0;     // Filter method: adaptive.
        header[12] = 0;     // Interlace: none.

        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>Decodes an 8-bit RGBA PNG.</summary>
    /// <param name="file">The complete PNG file.</param>
    /// <param name="width">The decoded width.</param>
    /// <param name="height">The decoded height.</param>
    /// <returns><c>width * height * 4</c> bytes, top row first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not a PNG, or is one this reader deliberately does not support. The message
    /// names what was found.
    /// </exception>
    public static byte[] Decode(byte[] file, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length < Signature.Length || !file.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG: the eight-byte signature does not match.");
        }

        int offset = Signature.Length;
        width = 0;
        height = 0;
        bool sawHeader = false;
        using MemoryStream data = new();

        while (offset + 8 <= file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            int payload = offset + 8;

            if (length < 0 || payload + length + 4 > file.Length)
            {
                throw new InvalidDataException($"Chunk '{type}' claims {length} bytes, which runs past the end.");
            }

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(payload, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(payload + 4, 4));
                    byte depth = file[payload + 8];
                    byte colour = file[payload + 9];
                    byte interlace = file[payload + 12];

                    if (depth != 8 || colour != 6)
                    {
                        throw new InvalidDataException(
                            $"Only 8-bit RGBA is supported; this file is bit depth {depth}, colour type {colour}.");
                    }

                    if (interlace != 0)
                    {
                        throw new InvalidDataException("Interlaced PNGs are not supported.");
                    }

                    sawHeader = true;
                    break;

                case "IDAT":
                    data.Write(file, payload, length);
                    break;

                case "IEND":
                    offset = file.Length;
                    continue;

                default:
                    break;
            }

            offset = payload + length + 4;
        }

        if (!sawHeader || width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The PNG has no usable IHDR chunk.");
        }

        data.Position = 0;
        using MemoryStream raw = new();
        using (ZLibStream inflate = new(data, CompressionMode.Decompress))
        {
            inflate.CopyTo(raw);
        }

        return Unfilter(raw.ToArray(), width, height);
    }

    /// <summary>
    /// Reverses PNG's per-scanline filters. All five are implemented even though
    /// <see cref="Encode"/> only ever writes type 0, because a golden image that has been through
    /// an image editor comes back with whichever filters that editor preferred, and failing to
    /// read our own committed file back would be an absurd way to lose a morning.
    /// </summary>
    private static byte[] Unfilter(byte[] raw, int width, int height)
    {
        int stride = width * 4;
        int expected = height * (stride + 1);
        if (raw.Length < expected)
        {
            throw new InvalidDataException(
                $"Decompressed to {raw.Length} bytes; {width}x{height} RGBA needs {expected}.");
        }

        byte[] pixels = new byte[height * stride];

        for (int row = 0; row < height; row++)
        {
            int source = row * (stride + 1);
            byte filter = raw[source];
            int destination = row * stride;
            int previous = destination - stride;

            for (int i = 0; i < stride; i++)
            {
                int left = i >= 4 ? pixels[destination + i - 4] : 0;
                int up = row > 0 ? pixels[previous + i] : 0;
                int upLeft = row > 0 && i >= 4 ? pixels[previous + i - 4] : 0;
                int value = raw[source + 1 + i];

                pixels[destination + i] = filter switch
                {
                    0 => (byte)value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException($"Unknown scanline filter {filter} on row {row}."),
                };
            }
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        stream.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(payload, 0, payload.Length);

        uint crc = Crc(typeBytes, payload);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc(byte[] type, byte[] payload)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in payload)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
