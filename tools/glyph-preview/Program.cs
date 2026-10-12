using System;
using System.IO;
using System.IO.Compression;
using HighCommand.UI;

// Contact sheet of all glyphs: each one big (128 px) and at its real size (26 px), light on a dark ground.
internal static class Program
{
    private static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "glyphs.png";
        int count = (int)Glyph.Count;
        const int Cell = 150, Cols = 8;
        int rows = (count + Cols - 1) / Cols;
        int w = Cols * Cell, h = rows * Cell;
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++) { rgb[i * 3] = 20; rgb[i * 3 + 1] = 26; rgb[i * 3 + 2] = 36; }

        for (int g = 0; g < count; g++)
        {
            int ox = (g % Cols) * Cell + 11, oy = (g / Cols) * Cell + 5;
            Blit(rgb, w, GlyphArt.Render((Glyph)g, 128), 128, ox, oy, 230, 236, 245);
            Blit(rgb, w, GlyphArt.Render((Glyph)g, 26), 26, ox + 50, oy + 130 - 4, 255, 164, 46);
        }
        WritePng(path, rgb, w, h);
        Console.WriteLine("Wrote " + Path.GetFullPath(path));
        return 0;
    }

    // alpha rows are bottom-up
    private static void Blit(byte[] rgb, int stride, float[] a, int size, int ox, int oy, byte r, byte g, byte b)
    {
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float al = a[(size - 1 - y) * size + x];
                int px = ox + x, py = oy + y;
                if (px < 0 || py < 0 || px >= stride || py * stride + px >= rgb.Length / 3) continue;
                int i = (py * stride + px) * 3;
                rgb[i] = (byte)(rgb[i] + (r - rgb[i]) * al);
                rgb[i + 1] = (byte)(rgb[i + 1] + (g - rgb[i + 1]) * al);
                rgb[i + 2] = (byte)(rgb[i + 2] + (b - rgb[i + 2]) * al);
            }
    }

    private static void WritePng(string path, byte[] rgb, int w, int h)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * w * 3, w * 3);
        }
        byte[] data = raw.ToArray();

        var z = new MemoryStream();
        z.WriteByte(0x78); z.WriteByte(0x9C);
        using (var d = new DeflateStream(z, CompressionLevel.Optimal, true)) d.Write(data, 0, data.Length);
        uint a = 1, b = 0;
        foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        z.WriteByte((byte)(adler >> 24)); z.WriteByte((byte)(adler >> 16)); z.WriteByte((byte)(adler >> 8)); z.WriteByte((byte)adler);

        using var f = File.Create(path);
        f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        var ihdr = new MemoryStream();
        WriteBE(ihdr, (uint)w); WriteBE(ihdr, (uint)h);
        ihdr.WriteByte(8); ihdr.WriteByte(2); ihdr.WriteByte(0); ihdr.WriteByte(0); ihdr.WriteByte(0);
        Chunk(f, "IHDR", ihdr.ToArray());
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", new byte[0]);
    }

    private static void WriteBE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        WriteBE(s, (uint)data.Length);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFF;
        foreach (byte v in t) crc = Crc(crc, v);
        foreach (byte v in data) crc = Crc(crc, v);
        WriteBE(s, ~crc);
    }

    private static uint Crc(uint c, byte v)
    {
        c ^= v;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }
}
