namespace Srpg95.Data;

/// <summary>
/// 8-bit paletted Windows BMP (every SRPG95 graphic: BITMAPINFOHEADER, uncompressed,
/// bottom-up). Pixels are stored top-down here. Palette index 0 is the transparent colour
/// of sprite sheets.
/// </summary>
public sealed class IndexedBitmap
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>256 entries of 0xAARRGGBB (alpha always 0xFF).</summary>
    public uint[] Palette { get; }
    /// <summary>Width*Height palette indices, row 0 = top.</summary>
    public byte[] Pixels { get; }

    IndexedBitmap(int w, int h, uint[] pal, byte[] px) => (Width, Height, Palette, Pixels) = (w, h, pal, px);

    public static IndexedBitmap Load(string path) => Decode(File.ReadAllBytes(path), path);

    public static IndexedBitmap Decode(byte[] b, string what = "bitmap")
    {
        if (b.Length < 54 || b[0] != 'B' || b[1] != 'M') throw new InvalidDataException($"{what}: not a BMP");
        int dataOff = Bin.I32(b, 10), hdr = Bin.I32(b, 14);
        int w = Bin.I32(b, 18), h = Bin.I32(b, 22);
        int bpp = Bin.U16(b, 28), comp = Bin.I32(b, 30), used = Bin.I32(b, 46);
        if (bpp != 8 || comp != 0) throw new InvalidDataException($"{what}: only 8-bit uncompressed BMPs are supported (bpp {bpp}, compression {comp})");
        bool topDown = h < 0;
        h = Math.Abs(h);
        var pal = new uint[256];
        int n = used == 0 ? 256 : Math.Min(used, 256);
        for (int i = 0; i < n; i++)
        {
            int p = 14 + hdr + i * 4;
            pal[i] = 0xFF000000u | (uint)b[p + 2] << 16 | (uint)b[p + 1] << 8 | b[p];
        }
        int stride = (w + 3) & ~3;
        var px = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int src = dataOff + (topDown ? y : h - 1 - y) * stride;
            int len = Math.Max(0, Math.Min(w, b.Length - src));
            if (len > 0) Array.Copy(b, src, px, y * w, len);
        }
        return new IndexedBitmap(w, h, pal, px);
    }

    /// <summary>
    /// RGBA8888 pixels (R,G,B,A byte order), premultiplied: transparent pixels (index 0 when
    /// requested) are all-zero, as alpha-blending renderers expect.
    /// </summary>
    public byte[] ToRgba(bool index0Transparent)
    {
        var o = new byte[Width * Height * 4];
        for (int i = 0; i < Pixels.Length; i++)
        {
            byte idx = Pixels[i];
            if (index0Transparent && idx == 0) continue;   // stays 0,0,0,0
            uint c = Palette[idx];
            o[i * 4] = (byte)(c >> 16);
            o[i * 4 + 1] = (byte)(c >> 8);
            o[i * 4 + 2] = (byte)c;
            o[i * 4 + 3] = index0Transparent && idx == 0 ? (byte)0 : (byte)255;
        }
        return o;
    }
}
