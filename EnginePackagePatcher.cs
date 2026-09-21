using System.Buffers.Binary;

namespace MtGBattlegroundsDlcConverter;

/// <summary>
/// Rebuilds the PC Engine.u Emitter field chain so it exposes the Xbox title-update bFrozen property.
/// No Engine.u payload bytes are bundled: all metadata and serialized property bodies are derived from
/// the verified PC v1.4 package, while the verified Xbox title-update Engine.u is used to confirm that
/// the source update actually contains Emitter.bFrozen.
/// </summary>
public static class EnginePackagePatcher
{
    public static byte[] Build(string pcEnginePath, string xboxEnginePath)
    {
        byte[] pcBytes = File.ReadAllBytes(pcEnginePath);
        byte[] xboxBytes = File.ReadAllBytes(xboxEnginePath);

        ParsedPackage pc = ParsedPackage.Parse(pcBytes);
        ParsedPackage xbox = ParsedPackage.Parse(xboxBytes);

        ValidateXboxSource(xbox);

        int emitterExportIndex = FindSingleExportIndex(pc, "Emitter", outerIndex: null);
        int savedScaleExportIndex = FindSingleExportIndex(pc, "SavedParticleScale", emitterExportIndex);

        // The stock PC package already has a BoolProperty named bFrozen on another class.  Reuse that
        // verified PC property as the metadata/serialization template, but attach the new clone to Emitter.
        int frozenTemplateExportIndex = FindSingleExportIndex(pc, "bFrozen", outerIndex: null);
        var frozenTemplate = pc.Exports[frozenTemplateExportIndex - 1];
        if (frozenTemplate.OuterIndex == emitterExportIndex)
            throw new InvalidDataException("PC Engine.u already contains Emitter.bFrozen; refusing to patch twice.");

        var savedScale = pc.Exports[savedScaleExportIndex - 1];
        if (savedScale.OuterIndex != emitterExportIndex)
            throw new InvalidDataException("SavedParticleScale is not owned by Emitter in the PC package.");
        if (savedScale.SerialSize <= 0 || frozenTemplate.SerialSize <= 0)
            throw new InvalidDataException("Required Engine.u property export has no serialized body.");

        int newFrozenExportIndex = checked(pc.ExportCount + 1); // Unreal object indices are 1-based.

        byte[] savedScaleSerial = SliceSerial(pcBytes, savedScale);
        byte[] frozenTemplateSerial = SliceSerial(pcBytes, frozenTemplate);

        // UField serialization begins after UObject tagged properties (terminated here by NAME_None),
        // then SuperField and Next are compact object indices.  SavedParticleScale is the old tail of
        // Emitter's field chain; link it to the new bFrozen property.  The cloned bFrozen becomes the new tail.
        byte[] newSavedScaleSerial = RewriteUFieldNext(savedScaleSerial, expectedOldNext: 0, newFrozenExportIndex);
        byte[] newFrozenSerial = RewriteUFieldNext(frozenTemplateSerial, expectedOldNext: null, newNext: 0);

        int insertedSerialBytes = checked(newSavedScaleSerial.Length + newFrozenSerial.Length);
        int newImportOffset = checked(pc.ImportOffset + insertedSerialBytes);
        int newExportOffset = checked(pc.ExportOffset + insertedSerialBytes);

        var rewrittenExports = pc.Exports.Select(x => x.Clone()).ToList();
        var rewrittenSavedScale = rewrittenExports[savedScaleExportIndex - 1];
        rewrittenSavedScale.SerialSize = newSavedScaleSerial.Length;
        rewrittenSavedScale.SerialOffset = pc.ImportOffset;

        var newFrozen = frozenTemplate.Clone();
        newFrozen.SuperIndex = 0;
        newFrozen.OuterIndex = emitterExportIndex;
        newFrozen.SerialSize = newFrozenSerial.Length;
        newFrozen.SerialOffset = checked(pc.ImportOffset + newSavedScaleSerial.Length);
        rewrittenExports.Add(newFrozen);

        byte[] prefix = pcBytes[..pc.ImportOffset].ToArray();
        PatchHeader(prefix, pc, newFrozenExportIndex, newImportOffset, newExportOffset);

        using var output = new MemoryStream(pcBytes.Length + insertedSerialBytes + 32);
        output.Write(prefix);
        output.Write(newSavedScaleSerial);
        output.Write(newFrozenSerial);

        // Import table is unchanged; only its absolute file position moves because the two replacement
        // property bodies are appended immediately before it.
        output.Write(pcBytes, pc.ImportOffset, pc.ExportOffset - pc.ImportOffset);

        foreach (var export in rewrittenExports)
            WriteExport(output, export);

        return output.ToArray();
    }

    private static void ValidateXboxSource(ParsedPackage xbox)
    {
        int emitter = FindSingleExportIndex(xbox, "Emitter", outerIndex: null);
        _ = FindSingleExportIndex(xbox, "SavedParticleScale", emitter);
        _ = FindSingleExportIndex(xbox, "bFrozen", emitter);
    }

    private static int FindSingleExportIndex(ParsedPackage package, string name, int? outerIndex)
    {
        var matches = new List<int>();
        for (int i = 0; i < package.Exports.Count; i++)
        {
            var export = package.Exports[i];
            if (!package.Names[export.ObjectName].Equals(name, StringComparison.Ordinal))
                continue;
            if (outerIndex.HasValue && export.OuterIndex != outerIndex.Value)
                continue;
            matches.Add(i + 1);
        }

        if (matches.Count != 1)
        {
            string owner = outerIndex.HasValue ? $" under export {outerIndex.Value}" : "";
            throw new InvalidDataException($"Expected exactly one export named {name}{owner}; found {matches.Count}.");
        }
        return matches[0];
    }

    private static byte[] SliceSerial(byte[] source, ExportEntry export)
    {
        if (export.SerialOffset < 0 || export.SerialSize < 0 || export.SerialOffset > source.Length - export.SerialSize)
            throw new InvalidDataException("Engine.u export serial range is invalid.");
        return source.AsSpan(export.SerialOffset, export.SerialSize).ToArray();
    }

    private static byte[] RewriteUFieldNext(byte[] serial, int? expectedOldNext, int newNext)
    {
        ReadOnlySpan<byte> span = serial;
        int cursor = 0;

        // In these verified Engine.u property exports the first compact index is the NAME_None terminator.
        _ = CompactIndex.Read(span, ref cursor);
        _ = CompactIndex.Read(span, ref cursor); // UField.SuperField
        int nextStart = cursor;
        int oldNext = CompactIndex.Read(span, ref cursor);

        if (expectedOldNext.HasValue && oldNext != expectedOldNext.Value)
            throw new InvalidDataException($"Unexpected UField.Next value. Expected {expectedOldNext.Value}, got {oldNext}.");

        byte[] encodedNext = CompactIndex.Write(newNext);
        byte[] result = new byte[nextStart + encodedNext.Length + (serial.Length - cursor)];
        Buffer.BlockCopy(serial, 0, result, 0, nextStart);
        Buffer.BlockCopy(encodedNext, 0, result, nextStart, encodedNext.Length);
        Buffer.BlockCopy(serial, cursor, result, nextStart + encodedNext.Length, serial.Length - cursor);
        return result;
    }

    private static void PatchHeader(
        byte[] prefix,
        ParsedPackage pc,
        int newExportCount,
        int newImportOffset,
        int newExportOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x14, 4), newExportCount);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x18, 4), newExportOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x20, 4), newImportOffset);

        int generationCount = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(0x34, 4));
        if (generationCount <= 0)
            throw new InvalidDataException("Engine.u has no package generations.");
        int lastGenerationOffset = checked(0x38 + ((generationCount - 1) * 8));
        if (lastGenerationOffset > prefix.Length - 8)
            throw new InvalidDataException("Engine.u generation table is truncated.");

        int recordedExports = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(lastGenerationOffset, 4));
        if (recordedExports != pc.ExportCount)
            throw new InvalidDataException($"Latest Engine.u generation records {recordedExports} exports, expected {pc.ExportCount}.");
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(lastGenerationOffset, 4), newExportCount);
    }

    private static void WriteExport(Stream stream, ExportEntry export)
    {
        CompactIndex.Write(stream, export.ClassIndex);
        CompactIndex.Write(stream, export.SuperIndex);
        Span<byte> int32 = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(int32, export.OuterIndex);
        stream.Write(int32);
        CompactIndex.Write(stream, export.ObjectName);
        BinaryPrimitives.WriteUInt32LittleEndian(int32, export.ObjectFlags);
        stream.Write(int32);
        CompactIndex.Write(stream, export.SerialSize);
        if (export.SerialSize != 0)
            CompactIndex.Write(stream, export.SerialOffset);
    }

    private sealed class ParsedPackage
    {
        public required List<string> Names { get; init; }
        public required List<ExportEntry> Exports { get; init; }
        public required int ExportCount { get; init; }
        public required int ExportOffset { get; init; }
        public required int ImportOffset { get; init; }

        public static ParsedPackage Parse(byte[] source)
        {
            ReadOnlySpan<byte> span = source;
            if (source.Length < 0x40)
                throw new InvalidDataException("Unreal package is too small.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != 0x9E2A83C1u)
                throw new InvalidDataException("Invalid Unreal package signature.");

            int nameCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x0C, 4));
            int nameOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x10, 4));
            int exportCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x14, 4));
            int exportOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x18, 4));
            int importCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x1C, 4));
            int importOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x20, 4));

            if (nameCount <= 0 || exportCount <= 0 || importCount < 0)
                throw new InvalidDataException("Unreal package header contains invalid table counts.");
            ValidateOffset(source.Length, nameOffset, "name table");
            ValidateOffset(source.Length, importOffset, "import table");
            ValidateOffset(source.Length, exportOffset, "export table");
            if (importOffset > exportOffset)
                throw new InvalidDataException("Import table begins after export table.");

            int cursor = nameOffset;
            var names = new List<string>(nameCount);
            for (int i = 0; i < nameCount; i++)
            {
                int length = CompactIndex.Read(span, ref cursor);
                string name;
                if (length > 0)
                {
                    EnsureRange(source.Length, cursor, length, "ANSI name");
                    name = System.Text.Encoding.Latin1.GetString(span.Slice(cursor, length - 1));
                    if (span[cursor + length - 1] != 0)
                        throw new InvalidDataException("ANSI Unreal name is not null-terminated.");
                    cursor += length;
                }
                else if (length < 0)
                {
                    int chars = checked(-length);
                    int bytes = checked(chars * 2);
                    EnsureRange(source.Length, cursor, bytes, "Unicode name");
                    name = System.Text.Encoding.Unicode.GetString(span.Slice(cursor, bytes - 2));
                    cursor += bytes;
                }
                else
                {
                    throw new InvalidDataException("Unreal name has zero length.");
                }

                EnsureRange(source.Length, cursor, 4, "name flags");
                cursor += 4;
                names.Add(name);
            }

            cursor = exportOffset;
            var exports = new List<ExportEntry>(exportCount);
            for (int i = 0; i < exportCount; i++)
            {
                int classIndex = CompactIndex.Read(span, ref cursor);
                int superIndex = CompactIndex.Read(span, ref cursor);
                EnsureRange(source.Length, cursor, 4, "export outer index");
                int outerIndex = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(cursor, 4));
                cursor += 4;
                int objectName = CompactIndex.Read(span, ref cursor);
                if ((uint)objectName >= (uint)names.Count)
                    throw new InvalidDataException($"Export {i + 1} has invalid name index {objectName}.");
                EnsureRange(source.Length, cursor, 4, "export flags");
                uint objectFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(cursor, 4));
                cursor += 4;
                int serialSize = CompactIndex.Read(span, ref cursor);
                int serialOffset = serialSize == 0 ? 0 : CompactIndex.Read(span, ref cursor);
                if (serialSize < 0)
                    throw new InvalidDataException($"Export {i + 1} has a negative serial size.");
                if (serialSize > 0)
                    EnsureRange(source.Length, serialOffset, serialSize, $"export {i + 1} serial data");

                exports.Add(new ExportEntry
                {
                    ClassIndex = classIndex,
                    SuperIndex = superIndex,
                    OuterIndex = outerIndex,
                    ObjectName = objectName,
                    ObjectFlags = objectFlags,
                    SerialSize = serialSize,
                    SerialOffset = serialOffset
                });
            }

            if (cursor != source.Length)
                throw new InvalidDataException("Unexpected trailing bytes after Engine.u export table.");

            return new ParsedPackage
            {
                Names = names,
                Exports = exports,
                ExportCount = exportCount,
                ExportOffset = exportOffset,
                ImportOffset = importOffset
            };
        }

        private static void ValidateOffset(int length, int offset, string label)
        {
            if (offset < 0 || offset > length)
                throw new InvalidDataException($"Invalid {label} offset {offset}.");
        }

        private static void EnsureRange(int length, int offset, int count, string label)
        {
            if (offset < 0 || count < 0 || offset > length - count)
                throw new InvalidDataException($"Invalid {label} range.");
        }
    }

    private sealed class ExportEntry
    {
        public int ClassIndex { get; set; }
        public int SuperIndex { get; set; }
        public int OuterIndex { get; set; }
        public int ObjectName { get; set; }
        public uint ObjectFlags { get; set; }
        public int SerialSize { get; set; }
        public int SerialOffset { get; set; }

        public ExportEntry Clone() => (ExportEntry)MemberwiseClone();
    }
}
