using System.Text;

namespace HappyPhoton.Tests;

internal static class PeBinaryInspection
{
    private sealed record Section(uint VirtualAddress, uint VirtualSize, uint Raw, uint RawSize);

    public static NativeBinaryInfo Inspect(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            stream.Position = 0x3c;
            var peOffset = reader.ReadUInt32();
            RequireRange(stream, peOffset, 24);
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                throw new InvalidDataException("Invalid PE signature.");
            }

            var machine = reader.ReadUInt16();
            var sectionCount = reader.ReadUInt16();
            stream.Position += 12;
            var optionalSize = reader.ReadUInt16();
            stream.Position += 2;
            var optionalOffset = stream.Position;
            RequireRange(stream, optionalOffset, optionalSize);
            var magic = reader.ReadUInt16();
            var directoryOffset = optionalOffset + (magic switch
            {
                0x10b => 96,
                0x20b => 112,
                _ => throw new InvalidDataException("Unsupported PE optional header.")
            });
            stream.Position = optionalOffset + 40;
            var osMajor = reader.ReadUInt16();
            var osMinor = reader.ReadUInt16();
            stream.Position = optionalOffset + 48;
            var subsystemMajor = reader.ReadUInt16();
            var subsystemMinor = reader.ReadUInt16();
            RequireRange(stream, directoryOffset + 8, 8);
            stream.Position = directoryOffset + 8;
            var importRva = reader.ReadUInt32();
            var importSize = reader.ReadUInt32();

            stream.Position = optionalOffset + optionalSize;
            var sections = new List<Section>(sectionCount);
            for (var i = 0; i < sectionCount; i++)
            {
                RequireRange(stream, stream.Position, 40);
                stream.Position += 8;
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var raw = reader.ReadUInt32();
                stream.Position += 16;
                sections.Add(new Section(virtualAddress, virtualSize, raw, rawSize));
            }

            var imports = ReadImports(reader, stream, sections, importRva, importSize);
            return new NativeBinaryInfo(
                "PE", Architecture(machine), null, imports,
                [$"PE OS version {osMajor}.{osMinor}",
                 $"PE subsystem version {subsystemMajor}.{subsystemMinor}"]);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("PE binary is truncated.", exception);
        }
    }

    private static IReadOnlyList<string> ReadImports(
        BinaryReader reader,
        Stream stream,
        IReadOnlyList<Section> sections,
        uint rva,
        uint size)
    {
        if (rva == 0 || size == 0)
        {
            return [];
        }

        var offset = MapRva(rva, sections);
        var imports = new List<string>();
        var maximum = Math.Min(size / 20, 4096);
        for (var i = 0; i < maximum; i++)
        {
            RequireRange(stream, offset + i * 20, 20);
            stream.Position = offset + i * 20;
            var originalThunk = reader.ReadUInt32();
            var timestamp = reader.ReadUInt32();
            var forwarder = reader.ReadUInt32();
            var nameRva = reader.ReadUInt32();
            var thunk = reader.ReadUInt32();
            if ((originalThunk | timestamp | forwarder | nameRva | thunk) == 0)
            {
                break;
            }

            imports.Add(ReadAsciiZ(reader, stream, MapRva(nameRva, sections)));
        }

        return imports.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static long MapRva(uint rva, IReadOnlyList<Section> sections)
    {
        var section = sections.FirstOrDefault(candidate =>
            rva >= candidate.VirtualAddress &&
            rva < candidate.VirtualAddress + Math.Max(candidate.VirtualSize, candidate.RawSize));
        if (section == null)
        {
            throw new InvalidDataException($"PE RVA 0x{rva:X} is outside all sections.");
        }

        return section.Raw + rva - section.VirtualAddress;
    }

    private static string ReadAsciiZ(BinaryReader reader, Stream stream, long offset)
    {
        RequireRange(stream, offset, 1);
        stream.Position = offset;
        var bytes = new List<byte>();
        while (bytes.Count < 4096)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add(value);
        }
        throw new InvalidDataException("PE import name is not terminated.");
    }

    private static string Architecture(ushort machine) => machine switch
    {
        0x8664 => "x86-64",
        0xaa64 => "arm64",
        0x14c => "x86",
        _ => $"machine-0x{machine:X4}"
    };

    private static void RequireRange(Stream stream, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > stream.Length - length)
        {
            throw new InvalidDataException("PE offset is outside the file.");
        }
    }
}
