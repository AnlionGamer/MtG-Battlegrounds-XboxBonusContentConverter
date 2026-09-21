using System.Buffers.Binary;

namespace MtGBattlegroundsDlcConverter;

internal static class XboxAdpcm
{
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544,
        598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707,
        1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
        5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635,
        13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    ];

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];

    // This title stores mono Xbox ADPCM as 36-byte blocks. Each block yields
    // the 16-bit predictor plus 64 nibble-decoded samples = 65 PCM samples.
    public static byte[] DecodeMono16(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length % 36 != 0)
            throw new InvalidDataException($"Xbox ADPCM payload length {encoded.Length} is not a multiple of 36 bytes.");

        int blockCount = encoded.Length / 36;
        byte[] pcm = new byte[checked(blockCount * 65 * 2)];
        int outOffset = 0;

        for (int blockOffset = 0; blockOffset < encoded.Length; blockOffset += 36)
        {
            var block = encoded.Slice(blockOffset, 36);
            int predictor = BinaryPrimitives.ReadInt16LittleEndian(block[..2]);
            int index = block[2];
            if ((uint)index > 88)
                throw new InvalidDataException($"Invalid Xbox ADPCM step index {index}.");

            WriteSample(pcm, ref outOffset, predictor);

            for (int i = 4; i < 36; i++)
            {
                byte packed = block[i];
                DecodeNibble(packed & 0x0F, ref predictor, ref index, pcm, ref outOffset);
                DecodeNibble(packed >> 4, ref predictor, ref index, pcm, ref outOffset);
            }
        }

        return pcm;
    }

    private static void DecodeNibble(int nibble, ref int predictor, ref int index, byte[] output, ref int outputOffset)
    {
        int step = StepTable[index];
        int diff = step >> 3;
        if ((nibble & 1) != 0) diff += step >> 2;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 4) != 0) diff += step;

        predictor += (nibble & 8) != 0 ? -diff : diff;
        predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);

        index += IndexTable[nibble & 7];
        index = Math.Clamp(index, 0, 88);

        WriteSample(output, ref outputOffset, predictor);
    }

    private static void WriteSample(byte[] output, ref int offset, int sample)
    {
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset, 2), (short)sample);
        offset += 2;
    }
}
