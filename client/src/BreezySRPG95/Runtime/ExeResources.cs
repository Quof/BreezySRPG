using Srpg95.Data;

namespace BreezySRPG95.Runtime;

/// <summary>
/// Reads resources out of SRPGEXEC.EXE: the start-up logos LOGO1 and LOGO2 (RT_BITMAP, FUN_00425818 / FUN_004258d0) and
/// the 12-px font KANJIFONT (type "KANJI", FUN_0040a284). The file is only read: a PE32 resource directory walk (the type,
/// the named entry, its first language).
/// </summary>
public static class ExeResources
{
    const int RtBitmap = 2;

    /// <summary>A bitmap resource, given the file header a BMP has.</summary>
    public static IndexedBitmap? Bitmap(string exePath, string name)
    {
        if (Raw(exePath, RtBitmap, null, name) is not { } dib) return null;
        try
        {
            int hdr = Bin.I32(dib, 0), bpp = Bin.U16(dib, 14), used = Bin.I32(dib, 32);
            int colours = used != 0 ? used : bpp <= 8 ? 1 << bpp : 0;
            var bmp = new byte[14 + dib.Length];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
            BitConverter.GetBytes(14 + hdr + colours * 4).CopyTo(bmp, 10);
            dib.CopyTo(bmp, 14);
            return IndexedBitmap.Decode(bmp, name);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A resource's bytes: of type <paramref name="typeId"/>, or of the type named <paramref name="typeName"/>.</summary>
    public static byte[]? Raw(string exePath, int typeId, string? typeName, string name)
    {
        try
        {
            var b = File.ReadAllBytes(exePath);
            int pe = Bin.I32(b, 0x3C);
            if (Bin.I32(b, pe) != 0x4550) return null;   // "PE\0\0"
            int sections = Bin.U16(b, pe + 6), optSize = Bin.U16(b, pe + 20), opt = pe + 24;
            if (Bin.U16(b, opt) != 0x10B) return null;    // PE32
            int rsrcRva = Bin.I32(b, opt + 96 + 2 * 8);
            if (rsrcRva == 0) return null;
            int table = opt + optSize;
            int Offset(int rva)
            {
                for (int s = 0; s < sections; s++)
                {
                    int h = table + s * 40, va = Bin.I32(b, h + 12), size = Math.Max(Bin.I32(b, h + 8), Bin.I32(b, h + 16));
                    if (rva >= va && rva < va + size) return rva - va + Bin.I32(b, h + 20);
                }
                return -1;
            }
            int root = Offset(rsrcRva);
            if (root < 0) return null;
            // one directory level: the entry whose id (or name) matches
            int? Find(int dir, int id, string? named)
            {
                int n = Bin.U16(b, dir + 12) + Bin.U16(b, dir + 14);
                for (int k = 0; k < n; k++)
                {
                    int e = dir + 16 + k * 8;
                    uint nameField = (uint)Bin.I32(b, e), target = (uint)Bin.I32(b, e + 4);
                    bool match = named is null
                        ? (nameField & 0x80000000) == 0 && nameField == id
                        : (nameField & 0x80000000) != 0 && ResourceName(b, root + (int)(nameField & 0x7FFFFFFF)).Equals(named, StringComparison.OrdinalIgnoreCase);
                    if (match) return (int)target;
                }
                return null;
            }
            if (Find(root, typeId, typeName) is not int types || (types & int.MinValue) == 0) return null;
            if (Find(root + (types & 0x7FFFFFFF), 0, name) is not int names || (names & int.MinValue) == 0) return null;
            int langs = root + (names & 0x7FFFFFFF);
            if (Bin.U16(b, langs + 12) + Bin.U16(b, langs + 14) == 0) return null;
            int dataEntry = root + Bin.I32(b, langs + 16 + 4);
            int dataAt = Offset(Bin.I32(b, dataEntry)), size = Bin.I32(b, dataEntry + 4);
            if (dataAt < 0 || dataAt + size > b.Length) return null;
            return b.AsSpan(dataAt, size).ToArray();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A resource directory string: u16 length, then UTF-16 characters.</summary>
    static string ResourceName(byte[] b, int at) => System.Text.Encoding.Unicode.GetString(b, at + 2, Bin.U16(b, at) * 2);
}
