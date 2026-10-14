using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RadarNet.Core;
using UnityEngine;

namespace RadarNet.EditorTools
{
    /// <summary>
    /// Builds the map heightmap from public elevation tiles (AWS Open Data "Terrain Tiles", Terrarium PNG encoding:
    /// height = R·256 + G + B/256 − 32768 m; sources SRTM, GMTED2010, ETOPO1 and others — includes bathymetry).
    /// Tiles are cached in &lt;project&gt;/TerrainCache and resampled into an azimuthal equidistant grid.
    /// </summary>
    public static class TerrainFetcher
    {
        const string UrlFmt = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{0}/{1}/{2}.png";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        static string CacheDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TerrainCache"));

        static double TileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);
        static double TileY(double lat, int z)
        {
            double r = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << z);
        }

        public static Heightmap Build(double lat0, double lon0, double extentM, int n, int zoom, out string log)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Geo.lat0 = lat0; Geo.lon0 = lon0;
            double half = extentM / 2;
            // bounding box of the projected square (sample the border)
            double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
            for (int k = 0; k <= 64; k++)
            {
                double t = -half + extentM * k / 64.0;
                foreach (var (x, y) in new[] { (t, -half), (t, half), (-half, t), (half, t) })
                {
                    Geo.Inverse(x, y, out var la, out var lo);
                    minLat = Math.Min(minLat, la); maxLat = Math.Max(maxLat, la);
                    minLon = Math.Min(minLon, lo); maxLon = Math.Max(maxLon, lo);
                }
            }
            int tx0 = (int)Math.Floor(TileX(minLon, zoom)), tx1 = (int)Math.Floor(TileX(maxLon, zoom));
            int ty0 = (int)Math.Floor(TileY(maxLat, zoom)), ty1 = (int)Math.Floor(TileY(minLat, zoom));
            var keys = new List<(int x, int y)>();
            for (int ty = ty0; ty <= ty1; ty++) for (int tx = tx0; tx <= tx1; tx++) keys.Add((tx, ty));

            // download missing tiles in parallel
            var dir = Path.Combine(CacheDir, zoom.ToString());
            Directory.CreateDirectory(dir);
            int downloaded = 0, failed = 0;
            var tasks = keys.Select(async k =>
            {
                var path = Path.Combine(dir, $"{k.x}_{k.y}.png");
                if (File.Exists(path) && new FileInfo(path).Length > 100) return;
                try
                {
                    var bytes = await http.GetByteArrayAsync(string.Format(UrlFmt, zoom, k.x, k.y)).ConfigureAwait(false);
                    File.WriteAllBytes(path, bytes);
                    System.Threading.Interlocked.Increment(ref downloaded);
                }
                catch (Exception) { System.Threading.Interlocked.Increment(ref failed); }
            }).ToArray();
            Task.Run(() => Task.WhenAll(tasks)).GetAwaiter().GetResult();

            // decode
            var tiles = new Dictionary<long, float[]>();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            foreach (var k in keys)
            {
                var path = Path.Combine(dir, $"{k.x}_{k.y}.png");
                if (!File.Exists(path)) continue;
                if (!tex.LoadImage(File.ReadAllBytes(path))) continue;
                var px = tex.GetPixels32();
                int w = tex.width;   // 256
                var hf = new float[w * w];
                for (int r = 0; r < w; r++)
                    for (int c = 0; c < w; c++)
                    {
                        var p = px[(w - 1 - r) * w + c];   // GetPixels32 is bottom-up; tile rows are top-down
                        hf[r * w + c] = p.r * 256f + p.g + p.b / 256f - 32768f;
                    }
                tiles[((long)k.x << 32) | (uint)k.y] = hf;
            }
            UnityEngine.Object.DestroyImmediate(tex);

            float Sample(double gx, double gy)   // global pixel coordinates at this zoom
            {
                int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
                int tx = ix >> 8, ty = iy >> 8;
                if (!tiles.TryGetValue(((long)tx << 32) | (uint)ty, out var t)) return 0f;
                return t[(iy & 255) * 256 + (ix & 255)];
            }

            var m = new Heightmap { n = n, extent = extentM, half = half, cell = extentM / (n - 1), lat0 = lat0, lon0 = lon0, h = new short[n * n] };
            double scale = 256.0;
            Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    Geo.Inverse(m.X(i), m.Y(j), out var la, out var lo);
                    double gx = TileX(lo, zoom) * scale - 0.5, gy = TileY(la, zoom) * scale - 0.5;
                    int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
                    double fx = gx - x0, fy = gy - y0;
                    double a = Sample(x0, y0), b = Sample(x0 + 1, y0), c = Sample(x0, y0 + 1), d = Sample(x0 + 1, y0 + 1);
                    double v = (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
                    m.h[j * n + i] = (short)Math.Max(-11000, Math.Min(9000, Math.Round(v)));
                }
            });
            // remove spikes/pits (tile artefacts, up to ~2 samples wide): far from the 5x5 median → median (2 passes)
            int fixedSpikes = 0;
            var nb = new short[25];
            for (int pass = 0; pass < 2; pass++)
            {
                var copy = (short[])m.h.Clone();
                for (int j = 2; j < n - 2; j++)
                    for (int i = 2; i < n - 2; i++)
                    {
                        int k = j * n + i, c = 0;
                        for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++) nb[c++] = copy[k + dj * n + di];
                        Array.Sort(nb);
                        short med = nb[12];
                        if (Math.Abs(copy[k] - med) > 700) { m.h[k] = med; fixedSpikes++; }
                    }
            }
            log = $"despiked {fixedSpikes} samples; tiles z{zoom} x{tx0}-{tx1} y{ty0}-{ty1} ({keys.Count}, downloaded {downloaded}, failed {failed}, decoded {tiles.Count}) bbox lat {minLat:0.00}..{maxLat:0.00} lon {minLon:0.00}..{maxLon:0.00} in {sw.ElapsedMilliseconds} ms";
            return m;
        }
    }
}
