using System;
using System.IO;
using System.IO.Compression;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>最小限の PNG 書き出し（24bit RGB）。外部パッケージを足さないため自前。</summary>
    internal static class Png
    {
        public static void Write(string path, int width, int height, byte[] rgb)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter: none
                raw.Write(rgb, y * width * 3, width * 3);
            }

            byte[] compressed;
            using (var outp = new MemoryStream())
            {
                using (var z = new ZLibStream(outp, CompressionLevel.Optimal, true))
                {
                    byte[] data = raw.ToArray();
                    z.Write(data, 0, data.Length);
                }
                compressed = outp.ToArray();
            }

            using (var fs = File.Create(path))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                var ihdr = new byte[13];
                WriteInt(ihdr, 0, width);
                WriteInt(ihdr, 4, height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 2;  // colour type: truecolour
                Chunk(fs, "IHDR", ihdr);
                Chunk(fs, "IDAT", compressed);
                Chunk(fs, "IEND", new byte[0]);
            }
        }

        private static void WriteInt(byte[] b, int at, int v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteInt(len, 0, data.Length);
            s.Write(len, 0, 4);

            var full = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) full[i] = (byte)type[i];
            Array.Copy(data, 0, full, 4, data.Length);
            s.Write(full, 0, full.Length);

            var crc = new byte[4];
            WriteInt(crc, 0, unchecked((int)Crc32(full)));
            s.Write(crc, 0, 4);
        }

        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc32(byte[] b)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < b.Length; i++) c = Table[(c ^ b[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
