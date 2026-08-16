using System.Buffers.Binary;
using System.Text;

namespace HappyPhoton.Tests;

internal static class MachOBinaryInspection
{
    private const uint LoadDylib = 0xc;
    private const uint IdDylib = 0xd;
    private const uint LoadWeakDylib = 0x80000018;
    private const uint ReexportDylib = 0x8000001f;
    private const uint LoadUpwardDylib = 0x80000023;
    private const uint VersionMinMacOs = 0x24;
    private const uint BuildVersion = 0x32;

    public static NativeBinaryInfo Inspect(Stream stream)
    {
        var data = ReadAll(stream);
        if (data.Length < 32)
        {
            throw new InvalidDataException("Mach-O header is truncated.");
        }
        var little = data.AsSpan(0, 4).SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe });
        var big = data.AsSpan(0, 4).SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xcf });
        if (!little && !big)
        {
            throw new InvalidDataException("Unsupported Mach-O header.");
        }

        var cpu = U32(data, 4, little);
        var commandCount = U32(data, 16, little);
        var commandsSize = U32(data, 20, little);
        if (commandCount > 4096 || 32UL + commandsSize > (ulong)data.Length)
        {
            throw new InvalidDataException("Invalid Mach-O load commands.");
        }

        var imports = new List<string>();
        var requirements = new List<string>();
        string? identity = null;
        var offset = 32;
        for (var i = 0; i < commandCount; i++)
        {
            Require(data, offset, 8);
            var command = U32(data, offset, little);
            var size = U32(data, offset + 4, little);
            if (size < 8 || (ulong)offset + size > (ulong)data.Length)
            {
                throw new InvalidDataException("Invalid Mach-O load command size.");
            }

            if (command is LoadDylib or LoadWeakDylib or ReexportDylib or LoadUpwardDylib or IdDylib)
            {
                Require(data, offset, 12);
                var nameOffset = U32(data, offset + 8, little);
                if (nameOffset >= size)
                {
                    throw new InvalidDataException("Invalid Mach-O dylib name offset.");
                }
                var name = ReadAsciiZ(data, offset + checked((int)nameOffset), offset + checked((int)size));
                if (command == IdDylib)
                {
                    identity = name;
                }
                else
                {
                    imports.Add(name);
                }
            }
            else if (command == VersionMinMacOs)
            {
                Require(data, offset, 16);
                requirements.Add($"Mach-O minimum macOS {Version(U32(data, offset + 8, little))}");
            }
            else if (command == BuildVersion)
            {
                Require(data, offset, 24);
                var platform = U32(data, offset + 8, little);
                var minimum = Version(U32(data, offset + 12, little));
                requirements.Add($"Mach-O platform {Platform(platform)} minimum {minimum}");
            }

            offset += checked((int)size);
        }

        return new NativeBinaryInfo(
            "Mach-O 64", Architecture(cpu), identity,
            imports.Distinct(StringComparer.Ordinal).ToArray(), requirements);
    }

    private static string Version(uint packed) =>
        $"{packed >> 16}.{(packed >> 8) & 0xff}.{packed & 0xff}";

    private static string Platform(uint platform) => platform switch
    {
        1 => "macOS",
        2 => "iOS",
        6 => "Mac Catalyst",
        _ => platform.ToString()
    };

    private static string Architecture(uint cpu) => cpu switch
    {
        0x01000007 => "x86-64",
        0x0100000c => "arm64",
        _ => $"cpu-0x{cpu:X8}"
    };

    private static byte[] ReadAll(Stream stream)
    {
        if (stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("Mach-O binary is too large.");
        }
        var data = new byte[(int)stream.Length];
        stream.Position = 0;
        stream.ReadExactly(data);
        return data;
    }

    private static uint U32(byte[] data, int offset, bool little)
    {
        Require(data, offset, 4);
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    }

    private static string ReadAsciiZ(byte[] data, int offset, int limit)
    {
        var terminator = Array.IndexOf(data, (byte)0, offset, limit - offset);
        if (terminator < 0)
        {
            throw new InvalidDataException("Mach-O string is not terminated.");
        }
        return Encoding.UTF8.GetString(data, offset, terminator - offset);
    }

    private static void Require(byte[] data, int offset, int size)
    {
        if (offset < 0 || size < 0 || offset > data.Length - size)
        {
            throw new InvalidDataException("Mach-O structure is truncated.");
        }
    }
}
