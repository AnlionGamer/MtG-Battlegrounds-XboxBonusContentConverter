using System.Buffers.Binary;
using System.Text;

namespace MtGBattlegroundsDlcConverter;

/// <summary>
/// Rebuilds the two Xbox DLC icon packages for the Windows renderer.
/// No converted texture bytes are embedded: the artwork is decoded from the verified Xbox UTX source,
/// resized, recompressed to BC3/DXT5, and written back into a UE2 package structure.
/// </summary>
internal static class TexturePackagePatcher
{
    private const uint UnrealSignature = 0x9E2A83C1u;
    private const int SummarySize = 0x40;

    private static readonly HashSet<string> SpellTextures = new(StringComparer.Ordinal)
    {
        "SummonLivingHive", "PlagueWind", "SummonReiverDemon", "Insurrection", "SummonSerraAvatar",
        "BlessedWind", "SummonTephraderm", "Biorhythm", "SummonTidalKraken", "TimeStretch",
        "SummonLivingHiveOffspring",
    };

    public static byte[] BuildSpellIcons(string xboxPackagePath)
    {
        var package = ParsedTexturePackage.Parse(File.ReadAllBytes(xboxPackagePath));
        int transformed = 0;
        byte[] result = package.Rebuild((name, serial, oldAbsoluteOffset, newAbsoluteOffset) =>
        {
            if (!SpellTextures.Contains(name)) return serial;
            transformed++;
            var texture = ParsedTexture.Parse(serial, oldAbsoluteOffset);
            if (texture.Width != 128 || texture.Height != 128 || texture.Mips.Count != 8)
                throw new InvalidDataException($"Unexpected Xbox spell-icon geometry for {name}: {texture.Width}x{texture.Height}, {texture.Mips.Count} mips.");

            var top = Bc3Codec.Decode(texture.Mips[0].Data, texture.Mips[0].Width, texture.Mips[0].Height)
                .ResizeBicubic(256, 256);
            var mips = new List<TextureMip>(9)
            {
                TextureMip.FromPixels(top)
            };
            // Every original Xbox lower mip becomes the PC lower chain unchanged.
            mips.AddRange(texture.Mips.Select(x => x.Copy()));
            texture = texture.WithTargetPrefix(package.Names, 256, 256);
            return texture.Rebuild(newAbsoluteOffset, 256, 256, mips);
        });

        if (transformed != 11)
            throw new InvalidDataException($"Expected 11 spell icon textures; transformed {transformed}.");
        ValidateSpellResult(result);
        return result;
    }

    public static byte[] BuildLandIcons(string xboxPackagePath)
    {
        var package = ParsedTexturePackage.Parse(File.ReadAllBytes(xboxPackagePath));
        bool smallDone = false, bigDone = false;
        byte[] result = package.Rebuild((name, serial, oldAbsoluteOffset, newAbsoluteOffset) =>
        {
            if (name == "ExpansionArena1Bigicon")
            {
                bigDone = true;
                var texture = ParsedTexture.Parse(serial, oldAbsoluteOffset);
                if (texture.Width != 512 || texture.Height != 256 || texture.Mips.Count != 10)
                    throw new InvalidDataException("Unexpected Xbox Glacial Vale large-icon geometry.");
                var top = Bc3Codec.Decode(texture.Mips[0].Data, 512, 256).ResizeBicubic(1024, 512);
                var mips = new List<TextureMip>(11) { TextureMip.FromPixels(top) };
                mips.AddRange(texture.Mips.Select(x => x.Copy()));
                texture = texture.WithTargetPrefix(package.Names, 1024, 512);
                return texture.Rebuild(newAbsoluteOffset, 1024, 512, mips);
            }

            if (name == "ExpansionArena1smallicon")
            {
                smallDone = true;
                var texture = ParsedTexture.Parse(serial, oldAbsoluteOffset);
                if (texture.Width != 64 || texture.Height != 128 || texture.Mips.Count != 8)
                    throw new InvalidDataException("Unexpected Xbox Glacial Vale small-icon geometry.");

                // The Windows arena carousel enlarges the active map icon horizontally. The known-good
                // PC layout therefore keeps transparent padding around the Glacial Vale artwork instead
                // of filling the full 128x128 surface. Reconstruct that layout entirely from the Xbox
                // texture: crop only its visible artwork, scale it to the PC selector footprint, place
                // it at the top-left of a transparent 128x128 canvas, then generate the full mip chain
                // from that composed PC image.
                var source = Bc3Codec.Decode(texture.Mips[0].Data, 64, 128);
                var bounds = source.FindAlphaBounds();
                if (bounds != (0, 0, 50, 80))
                    throw new InvalidDataException($"Unexpected Xbox Glacial Vale selector artwork bounds: {bounds}.");

                var artwork = source.Crop(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                    .ResizeBicubic(76, 121);
                var pcTop = RgbaImage.Transparent(128, 128);
                pcTop.Blit(artwork, 0, 0);

                var mips = new List<TextureMip>(8);
                for (int i = 0; i < 8; i++)
                {
                    int target = Math.Max(1, 128 >> i);
                    var image = target == 128 ? pcTop : pcTop.ResizeBicubic(target, target);
                    mips.Add(TextureMip.FromPixels(image));
                }
                texture = texture.WithTargetPrefix(package.Names, 128, 128);
                return texture.Rebuild(newAbsoluteOffset, 128, 128, mips);
            }

            return serial;
        });

        if (!smallDone || !bigDone)
            throw new InvalidDataException("Did not find both Glacial Vale icon textures in the Xbox package.");
        ValidateLandResult(result);
        return result;
    }

    public static void ValidateSpellResult(byte[] packageBytes)
    {
        var p = ParsedTexturePackage.Parse(packageBytes);
        int found = 0;
        foreach (var e in p.Exports)
        {
            string name = p.Names[e.ObjectName].Text;
            if (!SpellTextures.Contains(name)) continue;
            var t = ParsedTexture.Parse(packageBytes.AsSpan(e.SerialOffset, e.SerialSize).ToArray(), e.SerialOffset);
            if (t.Width != 256 || t.Height != 256 || t.Mips.Count != 9)
                throw new InvalidDataException($"Generated spell icon {name} has invalid geometry.");
            AssertMipChain(t, 256, 256, 9);
            found++;
        }
        if (found != 11) throw new InvalidDataException($"Generated spell package contains {found}/11 expected icons.");
    }

    public static void ValidateLandResult(byte[] packageBytes)
    {
        var p = ParsedTexturePackage.Parse(packageBytes);
        bool small = false, big = false;
        foreach (var e in p.Exports)
        {
            string name = p.Names[e.ObjectName].Text;
            if (name is not ("ExpansionArena1smallicon" or "ExpansionArena1Bigicon")) continue;
            var t = ParsedTexture.Parse(packageBytes.AsSpan(e.SerialOffset, e.SerialSize).ToArray(), e.SerialOffset);
            if (name == "ExpansionArena1smallicon")
            {
                AssertMipChain(t, 128, 128, 8); small = true;
            }
            else
            {
                AssertMipChain(t, 1024, 512, 11); big = true;
            }
        }
        if (!small || !big) throw new InvalidDataException("Generated land icon package is missing an expected texture.");
    }

    private static void AssertMipChain(ParsedTexture t, int width, int height, int count)
    {
        if (t.Width != width || t.Height != height || t.Mips.Count != count)
            throw new InvalidDataException("Generated texture header/mip count does not match target geometry.");
        for (int i = 0; i < count; i++)
        {
            int w = Math.Max(1, width >> i), h = Math.Max(1, height >> i);
            var m = t.Mips[i];
            if (m.Width != w || m.Height != h || m.Data.Length != Bc3Codec.DataSize(w, h))
                throw new InvalidDataException($"Generated mip {i} is invalid: {m.Width}x{m.Height}, {m.Data.Length} bytes; expected {w}x{h}.");
        }
    }

    private sealed class ParsedTexture
    {
        public byte[] Prefix { get; }
        public int Width => Mips[0].Width;
        public int Height => Mips[0].Height;
        public List<TextureMip> Mips { get; }
        private ParsedTexture(byte[] prefix, List<TextureMip> mips) { Prefix = prefix; Mips = mips; }

        public static ParsedTexture Parse(byte[] serial, int absoluteSerialOffset)
        {
            for (int start = 1; start < Math.Min(128, serial.Length); start++)
            {
                if (serial[start - 1] != 0) continue; // NAME_None terminator immediately precedes Mips.
                try
                {
                    int cursor = start;
                    int count = CompactIndex.Read(serial, ref cursor);
                    if (count is < 1 or > 16) continue;
                    var mips = new List<TextureMip>(count);
                    int previousW = int.MaxValue, previousH = int.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        if (cursor + 4 > serial.Length) throw new EndOfStreamException();
                        uint endPointer = BinaryPrimitives.ReadUInt32LittleEndian(serial.AsSpan(cursor, 4));
                        cursor += 4;
                        int dataLength = CompactIndex.Read(serial, ref cursor);
                        if (dataLength <= 0 || cursor + dataLength + 10 > serial.Length) throw new InvalidDataException();
                        byte[] data = serial.AsSpan(cursor, dataLength).ToArray();
                        cursor += dataLength;
                        if (endPointer != checked((uint)(absoluteSerialOffset + cursor))) throw new InvalidDataException();
                        int width = BinaryPrimitives.ReadInt32LittleEndian(serial.AsSpan(cursor, 4)); cursor += 4;
                        int height = BinaryPrimitives.ReadInt32LittleEndian(serial.AsSpan(cursor, 4)); cursor += 4;
                        byte uBits = serial[cursor++], vBits = serial[cursor++];
                        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new InvalidDataException();
                        if ((1 << uBits) != width || (1 << vBits) != height) throw new InvalidDataException();
                        if (dataLength != Bc3Codec.DataSize(width, height)) throw new InvalidDataException();
                        if (width > previousW || height > previousH) throw new InvalidDataException();
                        previousW = width; previousH = height;
                        mips.Add(new TextureMip(width, height, data));
                    }
                    if (cursor != serial.Length) continue;
                    return new ParsedTexture(serial[..start], mips);
                }
                catch { /* scan the next candidate */ }
            }
            throw new InvalidDataException("Could not locate a valid UE2 BC3 mip chain in texture export.");
        }

        public byte[] Rebuild(int newAbsoluteSerialOffset, int targetWidth, int targetHeight, IReadOnlyList<TextureMip> mips)
        {
            byte[] prefix = (byte[])Prefix.Clone();
            // Prefix already carries the patched target U/V bits, dimensions, and clamps.

            using var ms = new MemoryStream(prefix.Length + mips.Sum(x => x.Data.Length) + 256);
            ms.Write(prefix);
            CompactIndex.Write(ms, mips.Count);
            Span<byte> ptr = stackalloc byte[4];
            Span<byte> dims = stackalloc byte[10];
            foreach (var mip in mips)
            {
                long pointerPos = ms.Position;
                ms.Write(new byte[4]);
                CompactIndex.Write(ms, mip.Data.Length);
                ms.Write(mip.Data);
                uint absoluteEnd = checked((uint)(newAbsoluteSerialOffset + ms.Position));
                long resume = ms.Position;
                ms.Position = pointerPos;
                BinaryPrimitives.WriteUInt32LittleEndian(ptr, absoluteEnd);
                ms.Write(ptr);
                ms.Position = resume;

                BinaryPrimitives.WriteInt32LittleEndian(dims[..4], mip.Width);
                BinaryPrimitives.WriteInt32LittleEndian(dims.Slice(4, 4), mip.Height);
                dims[8] = checked((byte)Log2(mip.Width));
                dims[9] = checked((byte)Log2(mip.Height));
                ms.Write(dims);
            }
            return ms.ToArray();
        }

        public ParsedTexture WithTargetPrefix(IReadOnlyList<NameEntry> names, int targetWidth, int targetHeight)
        {
            byte[] p = (byte[])Prefix.Clone();
            PatchByteProperty(p, NameIndex(names, "UBits"), checked((byte)Log2(Width)), checked((byte)Log2(targetWidth)));
            PatchByteProperty(p, NameIndex(names, "VBits"), checked((byte)Log2(Height)), checked((byte)Log2(targetHeight)));
            PatchIntProperty(p, NameIndex(names, "USize"), Width, targetWidth);
            PatchIntProperty(p, NameIndex(names, "VSize"), Height, targetHeight);
            PatchIntProperty(p, NameIndex(names, "UClamp"), Width, targetWidth);
            PatchIntProperty(p, NameIndex(names, "VClamp"), Height, targetHeight);
            return new ParsedTexture(p, Mips);
        }
    }

    internal sealed record TextureMip(int Width, int Height, byte[] Data)
    {
        public TextureMip Copy() => new(Width, Height, (byte[])Data.Clone());
        public static TextureMip FromPixels(RgbaImage image) => new(image.Width, image.Height, Bc3Codec.Encode(image));
    }

    private sealed class ParsedTexturePackage
    {
        private readonly byte[] _source;
        private readonly int _nameEnd;
        private readonly byte[] _importTable;
        public List<NameEntry> Names { get; }
        public List<ExportEntry> Exports { get; }
        private readonly int _importOffset;
        private readonly int _exportOffset;

        private ParsedTexturePackage(byte[] source, int nameEnd, byte[] importTable, List<NameEntry> names,
            List<ExportEntry> exports, int importOffset, int exportOffset)
        {
            _source = source; _nameEnd = nameEnd; _importTable = importTable; Names = names; Exports = exports;
            _importOffset = importOffset; _exportOffset = exportOffset;
        }

        public static ParsedTexturePackage Parse(byte[] source)
        {
            if (source.Length < SummarySize || BinaryPrimitives.ReadUInt32LittleEndian(source) != UnrealSignature)
                throw new InvalidDataException("Not a supported Unreal package.");
            int nameCount = ReadI32(source, 12), nameOffset = ReadI32(source, 16);
            int exportCount = ReadI32(source, 20), exportOffset = ReadI32(source, 24);
            int importCount = ReadI32(source, 28), importOffset = ReadI32(source, 32);
            if (nameOffset != SummarySize || nameCount <= 0 || exportCount <= 0 || importCount <= 0)
                throw new InvalidDataException("Unexpected icon package summary.");

            int cursor = nameOffset;
            var names = new List<NameEntry>(nameCount);
            for (int i = 0; i < nameCount; i++)
            {
                int start = cursor;
                int length = CompactIndex.Read(source, ref cursor);
                string text;
                if (length > 0)
                {
                    Ensure(source, cursor, length + 4);
                    if (source[cursor + length - 1] != 0) throw new InvalidDataException("Unterminated ANSI name.");
                    text = Encoding.Latin1.GetString(source, cursor, length - 1); cursor += length;
                }
                else
                {
                    int bytes = checked(-length * 2); Ensure(source, cursor, bytes + 4);
                    text = Encoding.Unicode.GetString(source, cursor, bytes - 2); cursor += bytes;
                }
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(cursor, 4)); cursor += 4;
                names.Add(new NameEntry(text, flags, source[start..cursor]));
            }
            int nameEnd = cursor;
            if (nameEnd > importOffset || importOffset > exportOffset) throw new InvalidDataException("Invalid package table ordering.");
            byte[] importTable = source[importOffset..exportOffset];

            cursor = exportOffset;
            var exports = new List<ExportEntry>(exportCount);
            for (int i = 0; i < exportCount; i++)
            {
                int classIndex = CompactIndex.Read(source, ref cursor);
                int superIndex = CompactIndex.Read(source, ref cursor);
                Ensure(source, cursor, 4); int outer = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor, 4)); cursor += 4;
                int objectName = CompactIndex.Read(source, ref cursor);
                Ensure(source, cursor, 4); uint flags = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(cursor, 4)); cursor += 4;
                int size = CompactIndex.Read(source, ref cursor);
                int offset = size == 0 ? 0 : CompactIndex.Read(source, ref cursor);
                if ((uint)objectName >= (uint)names.Count || size <= 0) throw new InvalidDataException("Invalid icon export.");
                Ensure(source, offset, size);
                exports.Add(new ExportEntry(classIndex, superIndex, outer, objectName, flags, size, offset));
            }
            if (cursor != source.Length) throw new InvalidDataException("Unexpected trailing data after icon export table.");

            int expected = nameEnd;
            foreach (var e in exports)
            {
                if (e.SerialOffset != expected) throw new InvalidDataException("Icon package export bodies are not contiguous in expected order.");
                expected += e.SerialSize;
            }
            if (expected != importOffset) throw new InvalidDataException("Icon package export bodies do not end at import table.");
            return new ParsedTexturePackage(source, nameEnd, importTable, names, exports, importOffset, exportOffset);
        }

        public byte[] Rebuild(Func<string, byte[], int, int, byte[]> transform)
        {
            using var output = new MemoryStream(_source.Length * 4);
            output.Write(_source, 0, _nameEnd);
            var rebuilt = new List<ExportEntry>(Exports.Count);
            foreach (var e in Exports)
            {
                int newOffset = checked((int)output.Position);
                string name = Names[e.ObjectName].Text;
                byte[] serial = _source.AsSpan(e.SerialOffset, e.SerialSize).ToArray();
                serial = transform(name, serial, e.SerialOffset, newOffset);
                output.Write(serial);
                rebuilt.Add(e with { SerialOffset = newOffset, SerialSize = serial.Length });
            }
            int newImportOffset = checked((int)output.Position);
            output.Write(_importTable);
            int newExportOffset = checked((int)output.Position);
            foreach (var e in rebuilt) WriteExport(output, e);
            byte[] result = output.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24, 4), newExportOffset);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(32, 4), newImportOffset);
            return result;
        }
    }

    private static int NameIndex(IReadOnlyList<NameEntry> names, string text)
    {
        int found = -1;
        for (int i = 0; i < names.Count; i++)
            if (names[i].Text.Equals(text, StringComparison.Ordinal))
            {
                if (found >= 0) throw new InvalidDataException($"Duplicate name {text}.");
                found = i;
            }
        return found >= 0 ? found : throw new InvalidDataException($"Missing name {text}.");
    }

    private static void PatchByteProperty(byte[] prefix, int nameIndex, byte oldValue, byte newValue)
    {
        byte[] pattern = Combine(CompactIndex.Write(nameIndex), new byte[] { 0x01, oldValue });
        byte[] replacement = Combine(CompactIndex.Write(nameIndex), new byte[] { 0x01, newValue });
        ReplaceExactOnce(prefix, pattern, replacement);
    }

    private static void PatchIntProperty(byte[] prefix, int nameIndex, int oldValue, int newValue)
    {
        byte[] oldBytes = new byte[4], newBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(oldBytes, oldValue); BinaryPrimitives.WriteInt32LittleEndian(newBytes, newValue);
        byte[] pattern = Combine(CompactIndex.Write(nameIndex), new byte[] { 0x22 }, oldBytes);
        byte[] replacement = Combine(CompactIndex.Write(nameIndex), new byte[] { 0x22 }, newBytes);
        ReplaceExactOnce(prefix, pattern, replacement);
    }

    private static void ReplaceExactOnce(byte[] data, byte[] pattern, byte[] replacement)
    {
        if (pattern.Length != replacement.Length) throw new InvalidOperationException();
        int found = -1;
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (!data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) continue;
            if (found >= 0) throw new InvalidDataException("Texture property pattern occurs more than once.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Expected texture property pattern was not found.");
        replacement.CopyTo(data, found);
    }

    private static int Log2(int value)
    {
        if (value <= 0 || (value & (value - 1)) != 0) throw new InvalidDataException($"Texture dimension {value} is not a power of two.");
        int bits = 0; while ((1 << bits) != value) bits++; return bits;
    }

    private static void WriteExport(Stream stream, ExportEntry e)
    {
        CompactIndex.Write(stream, e.ClassIndex); CompactIndex.Write(stream, e.SuperIndex);
        Span<byte> fixedPart = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(fixedPart[..4], e.OuterIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.Slice(4, 4), e.ObjectFlags);
        stream.Write(fixedPart[..4]); CompactIndex.Write(stream, e.ObjectName); stream.Write(fixedPart.Slice(4, 4));
        CompactIndex.Write(stream, e.SerialSize); if (e.SerialSize != 0) CompactIndex.Write(stream, e.SerialOffset);
    }

    private static int ReadI32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static void Ensure(byte[] b, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > b.Length - length) throw new EndOfStreamException();
    }
    private static byte[] Combine(params byte[][] arrays)
    {
        byte[] result = new byte[arrays.Sum(a => a.Length)]; int p = 0;
        foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, p, a.Length); p += a.Length; }
        return result;
    }

    internal sealed record NameEntry(string Text, uint Flags, byte[] RawBytes);
    internal sealed record ExportEntry(int ClassIndex, int SuperIndex, int OuterIndex, int ObjectName, uint ObjectFlags, int SerialSize, int SerialOffset);
}
