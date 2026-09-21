using System.Buffers.Binary;
using System.Text;

namespace MtGBattlegroundsDlcConverter;

internal static class XactWaveBank
{
    private const uint Wbnd = 0x444E4257; // "WBND" little-endian

    public static Dictionary<string, byte[]> DecodeNamedWaves(string xwbPath, IReadOnlyList<string> orderedNames)
    {
        byte[] data = File.ReadAllBytes(xwbPath);
        var span = data.AsSpan();
        if (span.Length < 40 || BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != Wbnd)
            throw new InvalidDataException($"Not a supported XWB file: {xwbPath}");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span[4..8]);
        if (version != 3)
            throw new InvalidDataException($"Unsupported XWB version {version}; this converter expects the Battlegrounds version-3 banks.");

        var segments = new (int Offset, int Length)[4];
        int headerOffset = 8;
        for (int i = 0; i < segments.Length; i++)
        {
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(headerOffset, 4)));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(headerOffset + 4, 4)));
            ValidateRange(span.Length, offset, length, $"XWB segment {i}");
            segments[i] = (offset, length);
            headerOffset += 8;
        }

        var bankData = span.Slice(segments[0].Offset, segments[0].Length);
        if (bankData.Length < 36)
            throw new InvalidDataException("XWB bank-data segment is too short.");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bankData[..4]);
        int entryCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bankData[4..8]));
        string bankName = ReadFixedAscii(bankData.Slice(8, 16));
        int metaSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bankData[24..28]));
        int entryNameSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bankData[28..32]));
        int alignment = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bankData[32..36]));

        _ = flags;
        _ = entryNameSize;
        _ = alignment;

        if (entryCount != orderedNames.Count)
            throw new InvalidDataException($"XSB/XWB count mismatch for {bankName}: {orderedNames.Count} names vs {entryCount} wave entries.");
        if (metaSize < 16)
            throw new InvalidDataException($"Unsupported XWB metadata element size {metaSize}.");
        if (checked(entryCount * metaSize) > segments[1].Length)
            throw new InvalidDataException("XWB entry metadata exceeds its segment.");

        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entryCount; i++)
        {
            int metaOffset = checked(segments[1].Offset + i * metaSize);
            var meta = span.Slice(metaOffset, metaSize);
            uint format = BinaryPrimitives.ReadUInt32LittleEndian(meta[4..8]);
            int playOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(meta[8..12]));
            int playLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(meta[12..16]));

            int codec = (int)(format & 0x3);
            int channels = (int)((format >> 2) & 0x7);
            int sampleRate = (int)((format >> 5) & ((1u << 18) - 1));

            // Version-3 XACT banks used by this game encode tag 1 as Xbox ADPCM.
            if (codec != 1 || channels != 1 || sampleRate != 44100)
                throw new InvalidDataException($"Unexpected audio format in {bankName} entry {i}: codec={codec}, channels={channels}, rate={sampleRate}.");

            int absoluteAudioOffset = checked(segments[3].Offset + playOffset);
            ValidateRange(span.Length, absoluteAudioOffset, playLength, $"XWB audio entry {i}");
            byte[] pcm = XboxAdpcm.DecodeMono16(span.Slice(absoluteAudioOffset, playLength));
            byte[] wav = PcmWave.BuildMono16(pcm, sampleRate);
            result.Add(orderedNames[i], wav);
        }

        return result;
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        if (nul >= 0) bytes = bytes[..nul];
        return Encoding.ASCII.GetString(bytes);
    }

    private static void ValidateRange(int totalLength, int offset, int length, string what)
    {
        if (offset < 0 || length < 0 || offset > totalLength || length > totalLength - offset)
            throw new InvalidDataException($"{what} points outside the file.");
    }
}
