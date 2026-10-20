using System.Buffers.Binary;
using System.Text;

namespace Srpg95.Data;

/// <summary>Little-endian readers and Shift-JIS (cp932) strings, as used by every SRPG95 file.</summary>
public static class Bin
{
    /// <summary>Microsoft's Shift-JIS superset, which is what the editor actually wrote.</summary>
    public static Encoding Sjis { get; } = InitSjis();

    static Encoding InitSjis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    public static int I32(ReadOnlySpan<byte> b, int off) => BinaryPrimitives.ReadInt32LittleEndian(b[off..]);
    public static short I16(ReadOnlySpan<byte> b, int off) => BinaryPrimitives.ReadInt16LittleEndian(b[off..]);
    public static ushort U16(ReadOnlySpan<byte> b, int off) => BinaryPrimitives.ReadUInt16LittleEndian(b[off..]);

    public static int[] I32s(ReadOnlySpan<byte> b, int off, int count)
    {
        var r = new int[count];
        for (int i = 0; i < count; i++) r[i] = I32(b, off + i * 4);
        return r;
    }

    public static short[] I16s(ReadOnlySpan<byte> b, int off, int count)
    {
        var r = new short[count];
        for (int i = 0; i < count; i++) r[i] = I16(b, off + i * 2);
        return r;
    }

    /// <summary>Fixed-size NUL-terminated string; bytes after the NUL are stale editor leftovers.</summary>
    public static string Str(ReadOnlySpan<byte> b, int off, int size)
    {
        var s = b.Slice(off, Math.Min(size, b.Length - off));
        int nul = s.IndexOf((byte)0);
        if (nul >= 0) s = s[..nul];
        return Sjis.GetString(s);
    }

    /// <summary>Checks a fixed-record file and returns its records.</summary>
    public static ReadOnlyMemory<byte>[] Records(byte[] data, int recordSize, int count, string what)
    {
        if (data.Length != recordSize * count)
            throw new InvalidDataException($"{what}: expected {recordSize * count} bytes, got {data.Length}");
        var r = new ReadOnlyMemory<byte>[count];
        for (int i = 0; i < count; i++) r[i] = data.AsMemory(i * recordSize, recordSize);
        return r;
    }
}
