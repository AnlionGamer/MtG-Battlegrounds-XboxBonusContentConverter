using System.Buffers.Binary;
using System.Text;

namespace MtGBattlegroundsDlcConverter;

internal sealed class UnrealSoundPackage
{
    private const uint PackageSignature = 0x9E2A83C1;
    private const int SummarySize = 64;

    private readonly byte[] _source;
    private readonly byte[] _summary;
    private readonly List<NameEntry> _names;
    private readonly List<ExportEntry> _exports;
    private readonly byte[] _importTable;
    private readonly byte[] _soundPrefix;

    private UnrealSoundPackage(
        byte[] source,
        byte[] summary,
        List<NameEntry> names,
        List<ExportEntry> exports,
        byte[] importTable,
        byte[] soundPrefix)
    {
        _source = source;
        _summary = summary;
        _names = names;
        _exports = exports;
        _importTable = importTable;
        _soundPrefix = soundPrefix;
    }

    public static UnrealSoundPackage Load(string path)
    {
        byte[] source = File.ReadAllBytes(path);
        if (source.Length < SummarySize)
            throw new InvalidDataException($"Unreal package is too small: {path}");

        var span = source.AsSpan();
        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(span[0..4]);
        if (signature != PackageSignature)
            throw new InvalidDataException($"Invalid Unreal package signature: {path}");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span[4..8]);
        int nameCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..16]));
        int nameOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[16..20]));
        int exportCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[20..24]));
        int exportOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[24..28]));
        int importCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[28..32]));
        int importOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[32..36]));
        int generationCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[52..56]));

        if (version != 118)
            throw new InvalidDataException($"Unsupported Unreal package version {version}; expected the PC v1.4 audio package version 118.");
        if (nameOffset != SummarySize)
            throw new InvalidDataException($"Unexpected name-table offset {nameOffset}.");
        if (generationCount != 1)
            throw new InvalidDataException($"Unexpected generation count {generationCount}.");
        if (importCount != 2)
            throw new InvalidDataException($"Unexpected import count {importCount}.");
        ValidateRange(source.Length, nameOffset, exportOffset - nameOffset, "name/object data area");
        ValidateRange(source.Length, importOffset, exportOffset - importOffset, "import table");

        int cursor = nameOffset;
        var names = new List<NameEntry>(nameCount);
        for (int i = 0; i < nameCount; i++)
        {
            int start = cursor;
            int stringLength = CompactIndex.Read(span, ref cursor);
            string text;
            if (stringLength >= 0)
            {
                ValidateRange(source.Length, cursor, stringLength, "ANSI name");
                if (stringLength == 0)
                {
                    text = string.Empty;
                }
                else
                {
                    if (source[cursor + stringLength - 1] != 0)
                        throw new InvalidDataException("Unreal ANSI name is not NUL terminated.");
                    text = Encoding.Latin1.GetString(source, cursor, stringLength - 1);
                }
                cursor += stringLength;
            }
            else
            {
                int charCount = checked(-stringLength);
                int byteCount = checked(charCount * 2);
                ValidateRange(source.Length, cursor, byteCount, "Unicode name");
                if (byteCount < 2 || source[cursor + byteCount - 1] != 0 || source[cursor + byteCount - 2] != 0)
                    throw new InvalidDataException("Unreal Unicode name is not NUL terminated.");
                text = Encoding.Unicode.GetString(source, cursor, byteCount - 2);
                cursor += byteCount;
            }

            ValidateRange(source.Length, cursor, 4, "name flags");
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(cursor, 4));
            cursor += 4;
            names.Add(new NameEntry(text, flags, source[start..cursor]));
        }

        int namesEnd = cursor;
        if (namesEnd > importOffset)
            throw new InvalidDataException("Name table overlaps later package data.");

        if (importOffset > exportOffset)
            throw new InvalidDataException("Import table begins after export table.");
        byte[] importTable = source[importOffset..exportOffset];

        cursor = exportOffset;
        var exports = new List<ExportEntry>(exportCount);
        for (int i = 0; i < exportCount; i++)
        {
            int classIndex = CompactIndex.Read(span, ref cursor);
            int superIndex = CompactIndex.Read(span, ref cursor);
            ValidateRange(source.Length, cursor, 4, "export outer index");
            int outerIndex = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(cursor, 4));
            cursor += 4;
            int objectName = CompactIndex.Read(span, ref cursor);
            ValidateRange(source.Length, cursor, 4, "export flags");
            uint objectFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(cursor, 4));
            cursor += 4;
            int serialSize = CompactIndex.Read(span, ref cursor);
            int serialOffset = serialSize == 0 ? 0 : CompactIndex.Read(span, ref cursor);

            if ((uint)objectName >= (uint)names.Count)
                throw new InvalidDataException($"Export {i} has invalid object-name index {objectName}.");
            if (serialSize <= 0)
                throw new InvalidDataException($"Audio export {i} has invalid serial size {serialSize}.");
            ValidateRange(source.Length, serialOffset, serialSize, $"export {i} data");

            exports.Add(new ExportEntry(classIndex, superIndex, outerIndex, objectName, objectFlags, serialSize, serialOffset));
        }

        if (cursor != source.Length)
            throw new InvalidDataException("Unexpected trailing bytes after the Unreal export table.");
        if (exports.Count == 0)
            throw new InvalidDataException("Audio package has no exports.");

        byte[]? prefix = null;
        int expectedDataOffset = namesEnd;
        foreach (var export in exports)
        {
            if (export.SerialOffset != expectedDataOffset)
                throw new InvalidDataException("Audio exports are not stored contiguously in the expected PC layout.");
            var sound = ParseSoundObject(source, export);
            prefix ??= sound.Prefix;
            if (!sound.Prefix.AsSpan().SequenceEqual(prefix))
                throw new InvalidDataException("Audio package contains mixed sound-object prefixes.");
            expectedDataOffset += export.SerialSize;
        }

        if (expectedDataOffset != importOffset)
            throw new InvalidDataException("Audio object data does not end at the import table.");

        return new UnrealSoundPackage(source, source[..SummarySize], names, exports, importTable, prefix!);
    }

    public byte[] Rebuild(
        IReadOnlyDictionary<string, byte[]> xboxWaves,
        IReadOnlyList<string> orderedXboxNames)
    {
        var existingNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _names.Count; i++)
            existingNames.TryAdd(_names[i].Text, i);

        var appendedNames = orderedXboxNames
            .Where(name => !existingNames.ContainsKey(name))
            .ToList();

        uint appendedNameFlags = _names[_exports[0].ObjectName].Flags;
        using var nameStream = new MemoryStream();
        foreach (var name in _names)
            nameStream.Write(name.RawBytes);
        foreach (string name in appendedNames)
            WriteAnsiName(nameStream, name, appendedNameFlags);
        byte[] nameTable = nameStream.ToArray();

        var allNameIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _names.Count; i++)
            allNameIndexes[_names[i].Text] = i;
        for (int i = 0; i < appendedNames.Count; i++)
            allNameIndexes[appendedNames[i]] = _names.Count + i;

        int initialCapacity = checked(_source.Length + xboxWaves.Values.Sum(x => x.Length));
        using var output = new MemoryStream(Math.Max(initialCapacity, _source.Length));
        output.SetLength(SummarySize);
        output.Position = SummarySize;
        output.Write(nameTable);

        var rebuiltExports = new List<ExportEntry>(_exports.Count + appendedNames.Count);
        foreach (var export in _exports)
        {
            string objectName = _names[export.ObjectName].Text;
            byte[] wav = xboxWaves.TryGetValue(objectName, out var replacement)
                ? replacement
                : ParseSoundObject(_source, export).Wav;

            int serialOffset = checked((int)output.Position);
            byte[] soundObject = BuildSoundObject(wav, serialOffset, _soundPrefix);
            output.Write(soundObject);
            rebuiltExports.Add(export with { SerialSize = soundObject.Length, SerialOffset = serialOffset });
        }

        var exportTemplate = _exports[0];
        foreach (string objectName in appendedNames)
        {
            if (!xboxWaves.TryGetValue(objectName, out var wav))
                throw new InvalidDataException($"No Xbox WAV was decoded for new sound {objectName}.");

            int serialOffset = checked((int)output.Position);
            byte[] soundObject = BuildSoundObject(wav, serialOffset, _soundPrefix);
            output.Write(soundObject);
            rebuiltExports.Add(new ExportEntry(
                exportTemplate.ClassIndex,
                exportTemplate.SuperIndex,
                exportTemplate.OuterIndex,
                allNameIndexes[objectName],
                exportTemplate.ObjectFlags,
                soundObject.Length,
                serialOffset));
        }

        int importOffset = checked((int)output.Position);
        output.Write(_importTable);
        int exportOffset = checked((int)output.Position);
        foreach (var export in rebuiltExports)
            WriteExport(output, export);

        byte[] result = output.ToArray();
        byte[] summary = (byte[])_summary.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(12, 4), checked((uint)(_names.Count + appendedNames.Count)));
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(20, 4), checked((uint)rebuiltExports.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(24, 4), checked((uint)exportOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(32, 4), checked((uint)importOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(56, 4), checked((uint)rebuiltExports.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(60, 4), checked((uint)(_names.Count + appendedNames.Count)));
        summary.CopyTo(result, 0);
        return result;
    }

    private static SoundObject ParseSoundObject(byte[] source, ExportEntry export)
    {
        var objectSpan = source.AsSpan(export.SerialOffset, export.SerialSize);
        if (objectSpan.Length < 11)
            throw new InvalidDataException("Sound export is too short.");

        byte[] prefix = objectSpan[..2].ToArray();
        uint absoluteEnd = BinaryPrimitives.ReadUInt32LittleEndian(objectSpan[2..6]);
        if (absoluteEnd != checked((uint)(export.SerialOffset + export.SerialSize)))
            throw new InvalidDataException("Sound export end-pointer does not match its serialized extent.");

        int cursor = 6;
        int wavLength = CompactIndex.Read(objectSpan, ref cursor);
        if (wavLength <= 0 || cursor + wavLength != objectSpan.Length)
            throw new InvalidDataException("Unexpected PC sound-object layout.");

        byte[] wav = objectSpan.Slice(cursor, wavLength).ToArray();
        if (wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Sound export does not contain a PCM RIFF/WAVE payload.");
        return new SoundObject(prefix, wav);
    }

    private static byte[] BuildSoundObject(byte[] wav, int serialOffset, byte[] prefix)
    {
        byte[] wavLength = CompactIndex.Write(wav.Length);
        int serialSize = checked(2 + 4 + wavLength.Length + wav.Length);
        uint absoluteEnd = checked((uint)(serialOffset + serialSize));

        byte[] result = new byte[serialSize];
        prefix.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2, 4), absoluteEnd);
        wavLength.CopyTo(result, 6);
        wav.CopyTo(result, 6 + wavLength.Length);
        return result;
    }

    private static void WriteAnsiName(Stream stream, string name, uint flags)
    {
        byte[] text = Encoding.Latin1.GetBytes(name);
        CompactIndex.Write(stream, checked(text.Length + 1));
        stream.Write(text);
        stream.WriteByte(0);
        Span<byte> flagBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(flagBytes, flags);
        stream.Write(flagBytes);
    }

    private static void WriteExport(Stream stream, ExportEntry export)
    {
        CompactIndex.Write(stream, export.ClassIndex);
        CompactIndex.Write(stream, export.SuperIndex);
        Span<byte> fixedBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(fixedBytes[..4], export.OuterIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(fixedBytes[4..8], export.ObjectFlags);
        stream.Write(fixedBytes[..4]);
        CompactIndex.Write(stream, export.ObjectName);
        stream.Write(fixedBytes[4..8]);
        CompactIndex.Write(stream, export.SerialSize);
        if (export.SerialSize != 0)
            CompactIndex.Write(stream, export.SerialOffset);
    }

    private static void ValidateRange(int totalLength, int offset, int length, string what)
    {
        if (offset < 0 || length < 0 || offset > totalLength || length > totalLength - offset)
            throw new InvalidDataException($"{what} points outside the package.");
    }

    private sealed record NameEntry(string Text, uint Flags, byte[] RawBytes);
    private sealed record ExportEntry(
        int ClassIndex,
        int SuperIndex,
        int OuterIndex,
        int ObjectName,
        uint ObjectFlags,
        int SerialSize,
        int SerialOffset);
    private sealed record SoundObject(byte[] Prefix, byte[] Wav);
}
