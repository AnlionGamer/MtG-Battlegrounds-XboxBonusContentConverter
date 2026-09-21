using System.Buffers.Binary;

namespace MtGBattlegroundsDlcConverter;

internal static class PcmWave
{
    public static byte[] BuildMono16(byte[] pcm, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * (bitsPerSample / 8);
        int byteRate = checked(sampleRate * blockAlign);

        byte[] wav = new byte[checked(44 + pcm.Length)];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], 36 + pcm.Length);
        "WAVE"u8.CopyTo(span[8..12]);
        "fmt "u8.CopyTo(span[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..24], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..34], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..36], bitsPerSample);
        "data"u8.CopyTo(span[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], pcm.Length);
        pcm.CopyTo(wav, 44);
        return wav;
    }
}
