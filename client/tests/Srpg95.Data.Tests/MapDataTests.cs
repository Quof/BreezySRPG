namespace Srpg95.Data.Tests;

/// <summary>SMAP layout as the runtime's map loader reads it (FUN_004048ac, FUN_0040289c), on a map built here.</summary>
public class MapDataTests
{
    static void I32(byte[] d, int at, int v) => BitConverter.GetBytes(v).CopyTo(d, at);

    [Fact]
    public void Tiles_by_rows_and_packed_blocks_around_a_free_fat_entry()
    {
        const int w = 3, h = 2, tilesAt = 0x4b0, objectsAt = tilesAt + w * h * 2, fatAt = objectsAt + 100 * 164;
        const int dirAt = fatAt + 5000 * 4, blocksAt = dirAt + 5000 * 16;
        var d = new byte[blocksAt + 3 * 100];
        I32(d, 0x20, w);
        I32(d, 0x24, h);                                  // +0x444 left 0: the tiles are h rows of w chips
        for (int i = 0; i < w * h; i++) BitConverter.GetBytes((ushort)(i + 1)).CopyTo(d, tilesAt + i * 2);
        I32(d, objectsAt, 1);
        I32(d, objectsAt + 4, 2);
        I32(d, objectsAt + 8, 1);

        // OPENING takes blocks 0 and 2, EV000001 block 3; block 1 is free, so the file holds blocks 0, 2, 3
        for (int k = 0; k < 5000; k++) I32(d, fatAt + k * 4, -1);
        I32(d, fatAt, 2);
        I32(d, fatAt + 8, -2);
        I32(d, fatAt + 12, -2);
        byte[] stream = { 0x01, 6, 0, 0, 2, 0, (byte)'a', (byte)'b', (byte)'c', 0, 0, 0, 0, 0, 0 };
        System.Text.Encoding.ASCII.GetBytes("OPENING").CopyTo(d, dirAt);
        I32(d, dirAt + 8, 0);
        I32(d, dirAt + 12, 104 + stream.Length);
        System.Text.Encoding.ASCII.GetBytes("EV000001").CopyTo(d, dirAt + 16);
        I32(d, dirAt + 16 + 8, 3);
        I32(d, dirAt + 16 + 12, 100);
        I32(d, blocksAt + 100, stream.Length);
        stream.CopyTo(d, blocksAt + 104);
        Array.Fill(d, (byte)0xff, blocksAt + 200, 100);

        var m = MapData.Parse(d);
        Assert.Equal(6, m.Tiles[1, 2]);
        Assert.Equal(4, m.Tiles[1, 0]);
        Assert.Equal((2, 1), (Assert.Single(m.Objects).X, m.Objects[0].Y));
        var opening = m.Scripts["OPENING"];
        Assert.True(opening is { Error: null, RoundTrips: true });
        Assert.Equal("abc", Assert.IsType<MessageNode>(Assert.Single(opening.Tree)).Lines[0]);
    }
}
