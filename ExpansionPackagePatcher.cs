using System.Buffers.Binary;
using System.Text;

namespace MtGBattlegroundsDlcConverter;

/// <summary>
/// Adapts the verified Xbox MagicExpansionPack1.u package to the known-good Windows layout.
///
/// The transform is source-driven and intentionally small:
/// - rename the imported MagicSoundsX package to MagicSounds;
/// - clear DownloadableContentExtension="X" on the 11 DLC spell/helper classes that carry it;
/// - add the normal PC FooName string default to the 10 player-facing DLC spells;
/// - preserve the historical PC conversion ScriptText metadata for TimeWarp;
/// - rebuild package offsets/export metadata after those size changes.
///
/// Compiled TimeWarp bytecode is not rewritten. It remains the original Xbox code and continues
/// to reference Engine.Emitter.bFrozen, which is supplied by the separate Engine.u/Engine.dll patch.
/// </summary>
public static class ExpansionPackagePatcher
{
    private const uint UnrealSignature = 0x9E2A83C1u;
    private const int SummarySize = 0x40;

    private static readonly HashSet<string> ClearDlcExtensionClasses = new(StringComparer.Ordinal)
    {
        "SummonLivingHiveToken",
        "TimeWarp",
        "PlagueWind",
        "SummonTidalKraken",
        "SummonTephraderm",
        "SummonSerraAvatar",
        "SummonReiverDemon",
        "SummonLivingHive",
        "Biorhythm",
        "Insurrection",
        "BlessedWind",
    };

    private static readonly IReadOnlyDictionary<string, string> FooNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TimeWarp"] = "TimeStretch",
            ["PlagueWind"] = "PlagueWind",
            ["SummonTidalKraken"] = "TidalKraken",
            ["SummonTephraderm"] = "Tephraderm",
            ["SummonSerraAvatar"] = "SerraAvatar",
            ["SummonReiverDemon"] = "ReiverDemon",
            ["SummonLivingHive"] = "LivingHive",
            ["Biorhythm"] = "Biorhythm",
            ["Insurrection"] = "Insurrection",
            ["BlessedWind"] = "BlessedWind",
        };

    public static byte[] Build(string xboxPackagePath)
    {
        byte[] source = File.ReadAllBytes(xboxPackagePath);
        ParsedPackage package = ParsedPackage.Parse(source);

        int magicSoundsXName = FindSingleName(package, "MagicSoundsX");
        int dlcExtensionName = FindSingleName(package, "DownloadableContentExtension");
        int bFrozenName = FindSingleName(package, "bFrozen");

        if (package.Names.Any(x => x.Text.Equals("MagicSounds", StringComparison.Ordinal)))
            throw new InvalidDataException("Xbox expansion source unexpectedly already contains the MagicSounds name.");
        if (package.Names.Any(x => x.Text.Equals("FooName", StringComparison.Ordinal)))
            throw new InvalidDataException("Xbox expansion source unexpectedly already contains FooName.");
        if (package.Names.Any(x => x.Text.Equals("DisableFogging", StringComparison.Ordinal)))
            throw new InvalidDataException("Xbox expansion source unexpectedly already contains DisableFogging.");

        // Preserve every original name record byte-for-byte except the package-name adaptation.
        using var nameStream = new MemoryStream();
        for (int i = 0; i < package.Names.Count; i++)
        {
            NameEntry name = package.Names[i];
            if (i == magicSoundsXName)
                WriteAnsiName(nameStream, "MagicSounds", name.Flags);
            else
                nameStream.Write(name.RawBytes);
        }

        uint ordinaryNameFlags = package.Names[bFrozenName].Flags;
        int disableFoggingName = package.Names.Count;
        WriteAnsiName(nameStream, "DisableFogging", ordinaryNameFlags);
        int fooNameName = package.Names.Count + 1;
        WriteAnsiName(nameStream, "FooName", ordinaryNameFlags);
        byte[] rebuiltNameTable = nameStream.ToArray();

        byte[] oldDlcExtension = Concat(
            CompactIndex.Write(dlcExtensionName),
            new byte[] { 0x5D, 0x03, 0x02, (byte)'X', 0x00 });
        byte[] emptyDlcExtension = Concat(
            CompactIndex.Write(dlcExtensionName),
            new byte[] { 0x5D, 0x01, 0x00 });

        int timeWarpExportIndex = FindSingleTopLevelExport(package, "TimeWarp");
        int timeWarpScriptTextExportIndex = FindSingleOwnedExport(package, "ScriptText", timeWarpExportIndex);

        var rebuiltSerials = new List<byte[]>(package.Exports.Count);
        int clearedCount = 0;
        int fooCount = 0;
        int scriptTextCount = 0;

        for (int i = 0; i < package.Exports.Count; i++)
        {
            ExportEntry export = package.Exports[i];
            string objectName = package.Names[export.ObjectName].Text;
            byte[] serial = SliceSerial(source, export);

            if (ClearDlcExtensionClasses.Contains(objectName))
            {
                serial = ReplaceExactOnce(serial, oldDlcExtension, emptyDlcExtension,
                    $"{objectName}.DownloadableContentExtension");
                clearedCount++;

                if (FooNames.TryGetValue(objectName, out string? fooName))
                {
                    serial = AppendFooNameDefault(serial, fooNameName, fooName);
                    fooCount++;
                }
            }

            if (i + 1 == timeWarpScriptTextExportIndex)
            {
                serial = PatchTimeWarpScriptText(serial);
                scriptTextCount++;
            }

            rebuiltSerials.Add(serial);
        }

        if (clearedCount != 11)
            throw new InvalidDataException($"Expected to clear 11 DLC extension defaults; patched {clearedCount}.");
        if (fooCount != 10)
            throw new InvalidDataException($"Expected to add 10 FooName defaults; patched {fooCount}.");
        if (scriptTextCount != 1)
            throw new InvalidDataException($"Expected one TimeWarp ScriptText patch; patched {scriptTextCount}.");

        // Rebuild the package in its verified physical layout:
        // summary -> names -> export serial bodies -> imports -> export table.
        using var output = new MemoryStream(source.Length + 1024);
        output.Write(source, 0, package.NameOffset);
        output.Write(rebuiltNameTable);

        var rebuiltExports = new List<ExportEntry>(package.Exports.Count);
        for (int i = 0; i < package.Exports.Count; i++)
        {
            ExportEntry old = package.Exports[i];
            byte[] serial = rebuiltSerials[i];
            int serialOffset = checked((int)output.Position);
            output.Write(serial);
            rebuiltExports.Add(old with { SerialSize = serial.Length, SerialOffset = serialOffset });
        }

        int newImportOffset = checked((int)output.Position);
        output.Write(package.ImportTable);
        int newExportOffset = checked((int)output.Position);
        foreach (ExportEntry export in rebuiltExports)
            WriteExport(output, export);

        byte[] result = output.ToArray();
        PatchSummary(result, package, package.Names.Count + 2, newImportOffset, newExportOffset);

        // These are invariants of the verified source/reference pair and catch accidental format drift.
        if (disableFoggingName != package.Names.Count || fooNameName != package.Names.Count + 1)
            throw new InvalidDataException("Unexpected appended-name indexing.");

        return result;
    }

    private static byte[] AppendFooNameDefault(byte[] serial, int fooNameIndex, string value)
    {
        if (serial.Length == 0 || serial[^1] != 0)
            throw new InvalidDataException($"{value} class defaults do not end in NAME_None.");

        byte[] text = Encoding.Latin1.GetBytes(value);
        byte[] unrealString = Concat(CompactIndex.Write(text.Length + 1), text, new byte[] { 0 });
        if (unrealString.Length > byte.MaxValue)
            throw new InvalidDataException("FooName string is too large for the expected compact property tag.");

        // 0x5D = StrProperty with a one-byte explicit serialized-size field in this UE2 package format.
        byte[] tag = Concat(
            CompactIndex.Write(fooNameIndex),
            new byte[] { 0x5D, checked((byte)unrealString.Length) },
            unrealString);

        byte[] result = new byte[serial.Length - 1 + tag.Length + 1];
        Buffer.BlockCopy(serial, 0, result, 0, serial.Length - 1);
        Buffer.BlockCopy(tag, 0, result, serial.Length - 1, tag.Length);
        result[^1] = 0; // NAME_None terminator.
        return result;
    }

    private static byte[] PatchTimeWarpScriptText(byte[] serial)
    {
        // UTextBuffer in this verified package is nine zero bytes followed by one serialized ANSI FString.
        if (serial.Length < 16 || !serial.AsSpan(0, 9).SequenceEqual(new byte[9]))
            throw new InvalidDataException("Unexpected TimeWarp ScriptText header.");

        int cursor = 9;
        int textLength = CompactIndex.Read(serial, ref cursor);
        if (textLength <= 0 || cursor + textLength != serial.Length || serial[^1] != 0)
            throw new InvalidDataException("Unexpected TimeWarp ScriptText string layout.");

        string text = Encoding.Latin1.GetString(serial, cursor, textLength - 1);
        text = ReplaceTextOnce(text,
            "\t\t// Freeze it.\r\n\t\tbs.Foo[i].bFrozen = true;",
            "\t\t// PC conversion: Xbox bFrozen is unavailable; use a harmless Emitter bool placeholder.\r\n\t\tbs.Foo[i].DisableFogging = true;");
        text = ReplaceTextOnce(text,
            "// Unfreeze emitters.",
            "// Restore the placeholder Emitter flag.");
        text = ReplaceTextOnce(text,
            "\t\tFrozenEmitters[i].bFrozen = false;",
            "\t\tFrozenEmitters[i].DisableFogging = false;");

        if (!text.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            throw new InvalidDataException("TimeWarp ScriptText does not have the expected final blank line.");
        text = text[..^2]; // Preserve the known-good package's single final CRLF.

        byte[] textBytes = Encoding.Latin1.GetBytes(text);
        byte[] serializedString = Concat(CompactIndex.Write(textBytes.Length + 1), textBytes, new byte[] { 0 });
        return Concat(serial[..9], serializedString);
    }

    private static string ReplaceTextOnce(string source, string oldValue, string newValue)
    {
        int first = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (first < 0)
            throw new InvalidDataException($"Expected ScriptText fragment was not found: {oldValue}");
        if (source.IndexOf(oldValue, first + oldValue.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException($"ScriptText fragment occurs more than once: {oldValue}");
        return source[..first] + newValue + source[(first + oldValue.Length)..];
    }

    private static byte[] ReplaceExactOnce(byte[] source, byte[] oldValue, byte[] newValue, string description)
    {
        int found = -1;
        for (int i = 0; i <= source.Length - oldValue.Length; i++)
        {
            if (!source.AsSpan(i, oldValue.Length).SequenceEqual(oldValue))
                continue;
            if (found >= 0)
                throw new InvalidDataException($"{description} pattern occurs more than once.");
            found = i;
        }
        if (found < 0)
            throw new InvalidDataException($"{description} pattern was not found.");

        byte[] result = new byte[source.Length - oldValue.Length + newValue.Length];
        Buffer.BlockCopy(source, 0, result, 0, found);
        Buffer.BlockCopy(newValue, 0, result, found, newValue.Length);
        Buffer.BlockCopy(source, found + oldValue.Length, result, found + newValue.Length,
            source.Length - found - oldValue.Length);
        return result;
    }

    private static int FindSingleName(ParsedPackage package, string text)
    {
        var matches = package.Names
            .Select((name, index) => (name, index))
            .Where(x => x.name.Text.Equals(text, StringComparison.Ordinal))
            .Select(x => x.index)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected exactly one name-table entry '{text}'; found {matches.Length}.");
        return matches[0];
    }

    private static int FindSingleTopLevelExport(ParsedPackage package, string objectName)
    {
        int[] matches = package.Exports
            .Select((export, index) => (export, index))
            .Where(x => x.export.OuterIndex == 0 && package.Names[x.export.ObjectName].Text.Equals(objectName, StringComparison.Ordinal))
            .Select(x => x.index + 1)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one top-level export named {objectName}; found {matches.Length}.");
        return matches[0];
    }

    private static int FindSingleOwnedExport(ParsedPackage package, string objectName, int outerIndex)
    {
        int[] matches = package.Exports
            .Select((export, index) => (export, index))
            .Where(x => x.export.OuterIndex == outerIndex && package.Names[x.export.ObjectName].Text.Equals(objectName, StringComparison.Ordinal))
            .Select(x => x.index + 1)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one {objectName} export under export {outerIndex}; found {matches.Length}.");
        return matches[0];
    }

    private static byte[] SliceSerial(byte[] source, ExportEntry export)
    {
        if (export.SerialSize < 0 || export.SerialOffset < 0 || export.SerialOffset > source.Length - export.SerialSize)
            throw new InvalidDataException("Expansion export serial range is invalid.");
        return source.AsSpan(export.SerialOffset, export.SerialSize).ToArray();
    }

    private static void PatchSummary(byte[] result, ParsedPackage source, int newNameCount, int newImportOffset, int newExportOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x0C, 4), newNameCount);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x18, 4), newExportOffset);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x20, 4), newImportOffset);

        int generationCount = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(0x34, 4));
        if (generationCount <= 0)
            throw new InvalidDataException("Expansion package has no generation records.");
        int lastGeneration = checked(0x38 + ((generationCount - 1) * 8));
        if (lastGeneration > source.NameOffset - 8)
            throw new InvalidDataException("Expansion package generation table is truncated.");

        int generationExports = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(lastGeneration, 4));
        if (generationExports != source.Exports.Count)
            throw new InvalidDataException($"Latest generation records {generationExports} exports, expected {source.Exports.Count}.");
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(lastGeneration + 4, 4), newNameCount);
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

    private static void WriteAnsiName(Stream stream, string text, uint flags)
    {
        byte[] textBytes = Encoding.Latin1.GetBytes(text);
        CompactIndex.Write(stream, textBytes.Length + 1);
        stream.Write(textBytes);
        stream.WriteByte(0);
        Span<byte> rawFlags = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(rawFlags, flags);
        stream.Write(rawFlags);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int length = parts.Sum(x => x.Length);
        byte[] result = new byte[length];
        int cursor = 0;
        foreach (byte[] part in parts)
        {
            Buffer.BlockCopy(part, 0, result, cursor, part.Length);
            cursor += part.Length;
        }
        return result;
    }

    private sealed class ParsedPackage
    {
        public required int NameOffset { get; init; }
        public required List<NameEntry> Names { get; init; }
        public required List<ExportEntry> Exports { get; init; }
        public required byte[] ImportTable { get; init; }

        public static ParsedPackage Parse(byte[] source)
        {
            if (source.Length < SummarySize)
                throw new InvalidDataException("Expansion package is too small.");
            ReadOnlySpan<byte> span = source;
            if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != UnrealSignature)
                throw new InvalidDataException("Invalid Unreal package signature.");

            int nameCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x0C, 4));
            int nameOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x10, 4));
            int exportCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x14, 4));
            int exportOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x18, 4));
            int importCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x1C, 4));
            int importOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x20, 4));

            if (nameCount <= 0 || exportCount <= 0 || importCount <= 0)
                throw new InvalidDataException("Expansion package has invalid table counts.");
            ValidateRange(source.Length, nameOffset, 0, "name table");
            ValidateRange(source.Length, importOffset, exportOffset - importOffset, "import table");
            ValidateRange(source.Length, exportOffset, source.Length - exportOffset, "export table");

            int cursor = nameOffset;
            var names = new List<NameEntry>(nameCount);
            for (int i = 0; i < nameCount; i++)
            {
                int start = cursor;
                int length = CompactIndex.Read(span, ref cursor);
                string text;
                if (length < 0)
                {
                    int byteCount = checked(-length * 2);
                    ValidateRange(source.Length, cursor, byteCount, "Unicode name");
                    if (byteCount < 2 || source[cursor + byteCount - 1] != 0 || source[cursor + byteCount - 2] != 0)
                        throw new InvalidDataException("Unicode Unreal name is not NUL terminated.");
                    text = Encoding.Unicode.GetString(source, cursor, byteCount - 2);
                    cursor += byteCount;
                }
                else
                {
                    ValidateRange(source.Length, cursor, length, "ANSI name");
                    if (length <= 0 || source[cursor + length - 1] != 0)
                        throw new InvalidDataException("ANSI Unreal name is not NUL terminated.");
                    text = Encoding.Latin1.GetString(source, cursor, length - 1);
                    cursor += length;
                }

                ValidateRange(source.Length, cursor, 4, "name flags");
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(cursor, 4));
                cursor += 4;
                names.Add(new NameEntry(text, flags, source[start..cursor]));
            }
            int namesEnd = cursor;

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
                    throw new InvalidDataException($"Export {i + 1} has invalid name index {objectName}.");
                if (serialSize < 0)
                    throw new InvalidDataException($"Export {i + 1} has negative serial size.");
                if (serialSize > 0)
                    ValidateRange(source.Length, serialOffset, serialSize, $"export {i + 1} data");
                exports.Add(new ExportEntry(classIndex, superIndex, outerIndex, objectName, objectFlags, serialSize, serialOffset));
            }
            if (cursor != source.Length)
                throw new InvalidDataException("Unexpected trailing bytes after the expansion export table.");

            // Verified Battlegrounds package layout stores every export body contiguously in export-index order.
            int expectedSerialOffset = namesEnd;
            foreach (ExportEntry export in exports)
            {
                if (export.SerialSize == 0)
                    continue;
                if (export.SerialOffset != expectedSerialOffset)
                    throw new InvalidDataException("Expansion serial bodies are not in the expected contiguous layout.");
                expectedSerialOffset = checked(expectedSerialOffset + export.SerialSize);
            }
            if (expectedSerialOffset != importOffset)
                throw new InvalidDataException("Expansion serial data does not end at the import table.");

            return new ParsedPackage
            {
                NameOffset = nameOffset,
                Names = names,
                Exports = exports,
                ImportTable = source[importOffset..exportOffset],
            };
        }
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

    private static void ValidateRange(int sourceLength, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > sourceLength - length)
            throw new InvalidDataException($"Invalid {description} range: offset={offset}, length={length}, file={sourceLength}.");
    }
}
