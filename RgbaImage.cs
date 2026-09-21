namespace MtGBattlegroundsDlcConverter;

/// <summary>Small RGBA image container used only by the source-derived DLC icon converter.</summary>
internal sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA buffer size does not match dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }


    public RgbaImage Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(width));
        var dst = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 4, dst, row * width * 4, width * 4);
        return new RgbaImage(width, height, dst);
    }


    public (int X, int Y, int Width, int Height) FindAlphaBounds(byte threshold = 0)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            byte a = Pixels[(y * Width + x) * 4 + 3];
            if (a <= threshold) continue;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        if (maxX < minX || maxY < minY)
            throw new InvalidDataException("Image contains no visible alpha content.");
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static RgbaImage Transparent(int width, int height) =>
        new(width, height, new byte[checked(width * height * 4)]);

    public void Blit(RgbaImage source, int x, int y)
    {
        if (x < 0 || y < 0 || x + source.Width > Width || y + source.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        for (int row = 0; row < source.Height; row++)
            Buffer.BlockCopy(source.Pixels, row * source.Width * 4, Pixels, ((y + row) * Width + x) * 4, source.Width * 4);
    }

    public RgbaImage ResizeBicubic(int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        if (targetWidth == Width && targetHeight == Height)
            return new RgbaImage(Width, Height, (byte[])Pixels.Clone());

        var dst = new byte[checked(targetWidth * targetHeight * 4)];
        double scaleX = (double)Width / targetWidth;
        double scaleY = (double)Height / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            double sy = (y + 0.5) * scaleY - 0.5;
            int iy = (int)Math.Floor(sy);
            for (int x = 0; x < targetWidth; x++)
            {
                double sx = (x + 0.5) * scaleX - 0.5;
                int ix = (int)Math.Floor(sx);

                // Interpolate in premultiplied-alpha space to avoid dark halos around the spell icons.
                double sumW = 0, sumA = 0, sumR = 0, sumG = 0, sumB = 0;
                for (int oy = -1; oy <= 2; oy++)
                {
                    int py = Math.Clamp(iy + oy, 0, Height - 1);
                    double wy = Cubic(sy - (iy + oy));
                    for (int ox = -1; ox <= 2; ox++)
                    {
                        int px = Math.Clamp(ix + ox, 0, Width - 1);
                        double wx = Cubic(sx - (ix + ox));
                        double w = wx * wy;
                        int si = (py * Width + px) * 4;
                        double a = Pixels[si + 3] / 255.0;
                        sumW += w;
                        sumA += w * a;
                        sumR += w * a * Pixels[si + 0];
                        sumG += w * a * Pixels[si + 1];
                        sumB += w * a * Pixels[si + 2];
                    }
                }

                if (Math.Abs(sumW) < 1e-12) sumW = 1;
                double alpha = Math.Clamp(sumA / sumW, 0, 1);
                int di = (y * targetWidth + x) * 4;
                dst[di + 3] = ToByte(alpha * 255.0);
                if (alpha > 1e-9)
                {
                    dst[di + 0] = ToByte(sumR / (sumA == 0 ? 1 : sumA));
                    dst[di + 1] = ToByte(sumG / (sumA == 0 ? 1 : sumA));
                    dst[di + 2] = ToByte(sumB / (sumA == 0 ? 1 : sumA));
                }
                else
                {
                    dst[di + 0] = dst[di + 1] = dst[di + 2] = 0;
                }
            }
        }

        return new RgbaImage(targetWidth, targetHeight, dst);
    }

    // Catmull-Rom bicubic kernel (a = -0.5).
    private static double Cubic(double x)
    {
        x = Math.Abs(x);
        if (x <= 1.0)
            return 1.5 * x * x * x - 2.5 * x * x + 1.0;
        if (x < 2.0)
            return -0.5 * x * x * x + 2.5 * x * x - 4.0 * x + 2.0;
        return 0.0;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
