namespace MtGBattlegroundsDlcConverter;

internal static class CompactIndex
{
    public static int Read(ReadOnlySpan<byte> data, ref int offset)
    {
        if ((uint)offset >= (uint)data.Length)
            throw new EndOfStreamException("Unexpected end of compact index.");

        byte b0 = data[offset++];
        bool negative = (b0 & 0x80) != 0;
        int value = b0 & 0x3F;

        if ((b0 & 0x40) != 0)
        {
            byte b1 = ReadByte(data, ref offset);
            value |= (b1 & 0x7F) << 6;
            if ((b1 & 0x80) != 0)
            {
                byte b2 = ReadByte(data, ref offset);
                value |= (b2 & 0x7F) << 13;
                if ((b2 & 0x80) != 0)
                {
                    byte b3 = ReadByte(data, ref offset);
                    value |= (b3 & 0x7F) << 20;
                    if ((b3 & 0x80) != 0)
                    {
                        byte b4 = ReadByte(data, ref offset);
                        value |= b4 << 27;
                    }
                }
            }
        }

        return negative ? -value : value;
    }

    public static byte[] Write(int value)
    {
        bool negative = value < 0;
        uint v = (uint)Math.Abs((long)value);
        Span<byte> buffer = stackalloc byte[5];
        int count = 0;

        byte b0 = (byte)(v & 0x3F);
        v >>= 6;
        if (negative) b0 |= 0x80;
        if (v != 0) b0 |= 0x40;
        buffer[count++] = b0;

        while (v != 0)
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            buffer[count++] = b;
        }

        return buffer[..count].ToArray();
    }

    public static void Write(Stream stream, int value)
    {
        var bytes = Write(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        if ((uint)offset >= (uint)data.Length)
            throw new EndOfStreamException("Unexpected end of compact index.");
        return data[offset++];
    }
}
