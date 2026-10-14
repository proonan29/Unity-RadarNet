using System;
using System.IO;
using System.IO.Compression;

namespace RadarNet.Core
{
    /// <summary>
    /// Square elevation grid in the projected map frame. Heights in metres (negative = sea depth, bathymetry).
    /// Row j runs south → north, column i west → east; sample (i, j) is at x = -half + i·cell, y = -half + j·cell.
    /// Stored as a deflate-compressed blob: "RNH1", n, extent, lat0, lon0, int16[n·n].
    /// </summary>
    public class Heightmap
    {
        public int n;
        public double extent, half, cell, lat0, lon0;
        public short[] h;

        public static Heightmap Load(byte[] blob)
        {
            using var ms = new MemoryStream(blob);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var br = new BinaryReader(ds);
            var magic = new string(br.ReadChars(4));
            if (magic != "RNH1") throw new InvalidDataException("not a RadarNet heightmap");
            var m = new Heightmap { n = br.ReadInt32(), extent = br.ReadDouble(), lat0 = br.ReadDouble(), lon0 = br.ReadDouble() };
            m.half = m.extent / 2; m.cell = m.extent / (m.n - 1);
            var raw = br.ReadBytes(m.n * m.n * 2);
            m.h = new short[m.n * m.n];
            Buffer.BlockCopy(raw, 0, m.h, 0, raw.Length);
            Geo.lat0 = m.lat0; Geo.lon0 = m.lon0;
            return m;
        }

        public byte[] Save()
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
            using (var bw = new BinaryWriter(ds))
            {
                bw.Write("RNH1".ToCharArray());
                bw.Write(n); bw.Write(extent); bw.Write(lat0); bw.Write(lon0);
                var raw = new byte[h.Length * 2];
                Buffer.BlockCopy(h, 0, raw, 0, raw.Length);
                bw.Write(raw);
            }
            return ms.ToArray();
        }

        public short this[int i, int j] => h[Math.Max(0, Math.Min(n - 1, j)) * n + Math.Max(0, Math.Min(n - 1, i))];

        public bool InBounds(double x, double y) => x >= -half && x <= half && y >= -half && y <= half;

        /// <summary>Bilinear elevation (m), negative over the sea.</summary>
        public double Height(double x, double y)
        {
            double fx = (x + half) / cell, fy = (y + half) / cell;
            if (fx < 0) fx = 0; else if (fx > n - 1.001) fx = n - 1.001;
            if (fy < 0) fy = 0; else if (fy > n - 1.001) fy = n - 1.001;
            int i = (int)fx, j = (int)fy;
            double tx = fx - i, ty = fy - j;
            int k = j * n + i;
            double a = h[k], b = h[k + 1], c = h[k + n], d = h[k + n + 1];
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        /// <summary>Ground or sea surface height (m, ≥ 0).</summary>
        public double Surface(double x, double y) { double v = Height(x, y); return v > 0 ? v : 0; }

        public bool IsSea(double x, double y) => Height(x, y) <= 0.5;

        public double X(int i) => -half + i * cell;
        public double Y(int j) => -half + j * cell;

        public string Describe()
        {
            int land = 0; short max = short.MinValue, min = short.MaxValue; int imax = 0;
            for (int k = 0; k < h.Length; k++)
            {
                if (h[k] > 0) land++;
                if (h[k] > max) { max = h[k]; imax = k; }
                if (h[k] < min) min = h[k];
            }
            double mx = X(imax % n), my = Y(imax / n);
            return $"heightmap n={n} cell={cell:0.0}m extent={extent / 1000:0}km centre={lat0:0.###},{lon0:0.###} land={100.0 * land / h.Length:0.0}% min={min}m max={max}m at {Geo.LatLon(mx, my)}";
        }
    }
}
