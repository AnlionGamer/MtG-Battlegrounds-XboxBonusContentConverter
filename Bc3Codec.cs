using System.Buffers.Binary;

namespace MtGBattlegroundsDlcConverter;

/// <summary>Minimal deterministic BC3/DXT5 decoder/encoder for the DLC icon packages.</summary>
internal static class Bc3Codec
{
    public static int DataSize(int width, int height) =>
        checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16);

    public static RgbaImage Decode(ReadOnlySpan<byte> data, int width, int height)
    {
        if (data.Length != DataSize(width, height))
            throw new InvalidDataException($"BC3 payload size {data.Length} does not match {width}x{height}.");

        byte[] pixels = new byte[checked(width * height * 4)];
        int cursor = 0;
        Span<byte> alpha = stackalloc byte[8];
        Span<Rgb> colors = stackalloc Rgb[4];
        for (int by = 0; by < height; by += 4)
        for (int bx = 0; bx < width; bx += 4)
        {
            ReadOnlySpan<byte> block = data.Slice(cursor, 16);
            cursor += 16;

            BuildAlphaPalette(block[0], block[1], alpha);
            ulong alphaBits = Read48(block.Slice(2, 6));

            ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(8, 2));
            ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(10, 2));
            BuildColorPalette(c0, c1, colors);
            uint colorBits = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12, 4));

            for (int py = 0; py < 4; py++)
            for (int px = 0; px < 4; px++)
            {
                int x = bx + px, y = by + py;
                if (x >= width || y >= height) continue;
                int p = py * 4 + px;
                int ai = (int)((alphaBits >> (3 * p)) & 7);
                int ci = (int)((colorBits >> (2 * p)) & 3);
                int di = (y * width + x) * 4;
                pixels[di + 0] = colors[ci].R;
                pixels[di + 1] = colors[ci].G;
                pixels[di + 2] = colors[ci].B;
                pixels[di + 3] = alpha[ai];
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    public static byte[] Encode(RgbaImage image)
    {
        byte[] result = new byte[DataSize(image.Width, image.Height)];
        int cursor = 0;
        Span<Rgba> blockPixels = stackalloc Rgba[16];

        for (int by = 0; by < image.Height; by += 4)
        for (int bx = 0; bx < image.Width; bx += 4)
        {
            for (int py = 0; py < 4; py++)
            for (int px = 0; px < 4; px++)
            {
                int x = Math.Min(image.Width - 1, bx + px);
                int y = Math.Min(image.Height - 1, by + py);
                int si = (y * image.Width + x) * 4;
                blockPixels[py * 4 + px] = new Rgba(
                    image.Pixels[si], image.Pixels[si + 1], image.Pixels[si + 2], image.Pixels[si + 3]);
            }

            Span<byte> output = result.AsSpan(cursor, 16);
            EncodeAlpha(blockPixels, output[..8]);
            EncodeColor(blockPixels, output[8..]);
            cursor += 16;
        }
        return result;
    }

    private static void EncodeAlpha(ReadOnlySpan<Rgba> pixels, Span<byte> output)
    {
        byte min = 255, max = 0;
        foreach (var p in pixels) { if (p.A < min) min = p.A; if (p.A > max) max = p.A; }
        byte a0 = max, a1 = min;
        output[0] = a0; output[1] = a1;
        Span<byte> palette = stackalloc byte[8];
        BuildAlphaPalette(a0, a1, palette);

        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int j = 0; j < 8; j++)
            {
                int d = pixels[i].A - palette[j]; d *= d;
                if (d < bestDistance) { bestDistance = d; best = j; }
            }
            bits |= (ulong)best << (3 * i);
        }
        Write48(output.Slice(2, 6), bits);
    }

    private static void EncodeColor(ReadOnlySpan<Rgba> pixels, Span<byte> output)
    {
        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        foreach (var p in pixels)
        {
            if (p.R < minR) minR = p.R; if (p.G < minG) minG = p.G; if (p.B < minB) minB = p.B;
            if (p.R > maxR) maxR = p.R; if (p.G > maxG) maxG = p.G; if (p.B > maxB) maxB = p.B;
        }

        // The historical conversion used the simple min/max RGB565 family. Flooring preserves that behavior closely.
        ushort c0 = Pack565(maxR, maxG, maxB);
        ushort c1 = Pack565(minR, minG, minB);
        if (c0 < c1) (c0, c1) = (c1, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(output[..2], c0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(2, 2), c1);

        Span<Rgb> palette = stackalloc Rgb[4];
        BuildColorPalette(c0, c1, palette);
        uint bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int j = 0; j < 4; j++)
            {
                int dr = pixels[i].R - palette[j].R;
                int dg = pixels[i].G - palette[j].G;
                int db = pixels[i].B - palette[j].B;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDistance) { bestDistance = d; best = j; }
            }
            bits |= (uint)best << (2 * i);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(4, 4), bits);
    }

    private static void BuildAlphaPalette(byte a0, byte a1, Span<byte> p)
    {
        p[0] = a0; p[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++) p[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++) p[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            p[6] = 0; p[7] = 255;
        }
    }

    private static void BuildColorPalette(ushort c0, ushort c1, Span<Rgb> p)
    {
        p[0] = Unpack565(c0); p[1] = Unpack565(c1);
        // BC2/BC3 always use four-color interpolation; unlike DXT1, c0 <= c1 is not a transparency mode.
        p[2] = new Rgb((byte)((2 * p[0].R + p[1].R) / 3), (byte)((2 * p[0].G + p[1].G) / 3), (byte)((2 * p[0].B + p[1].B) / 3));
        p[3] = new Rgb((byte)((p[0].R + 2 * p[1].R) / 3), (byte)((p[0].G + 2 * p[1].G) / 3), (byte)((p[0].B + 2 * p[1].B) / 3));
    }

    private static ushort Pack565(byte r, byte g, byte b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
    private static Rgb Unpack565(ushort c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return new Rgb((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
    }
    private static ulong Read48(ReadOnlySpan<byte> s)
    {
        ulong v = 0; for (int i = 0; i < 6; i++) v |= (ulong)s[i] << (8 * i); return v;
    }
    private static void Write48(Span<byte> s, ulong v) { for (int i = 0; i < 6; i++) s[i] = (byte)(v >> (8 * i)); }

    private readonly record struct Rgb(byte R, byte G, byte B);
    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}
