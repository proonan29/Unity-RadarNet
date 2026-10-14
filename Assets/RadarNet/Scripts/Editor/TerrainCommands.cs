using System;
using System.IO;
using RadarNet.Core;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace RadarNet.EditorTools
{
    public static class TerrainCommands
    {
        public const string DemPath = "Assets/RadarNet/Resources/RadarNetDEM.bytes";

        [CliCommand("rn_fetch_terrain", "Download public elevation tiles (AWS Terrain Tiles, Terrarium) and build the 1000 x 1000 km heightmap (azimuthal equidistant around lat/lon). Writes " + DemPath + " and Screenshots/dem_preview.png.")]
        public static string FetchTerrain(
            [CliArg("lat", "Centre latitude")] double lat = 36.5,
            [CliArg("lon", "Centre longitude")] double lon = 127.7,
            [CliArg("extent_km", "Map width (km)")] double extentKm = 1000,
            [CliArg("size", "Samples per side (2^k + 1)")] int size = 2049,
            [CliArg("zoom", "Tile zoom level (8 ≈ 490 m/px at 37°N)")] int zoom = 8)
        {
            var m = TerrainFetcher.Build(lat, lon, extentKm * 1000, size, zoom, out var log);
            var blob = m.Save();
            var full = Path.GetFullPath(DemPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, blob);
            AssetDatabase.ImportAsset(DemPath);
            var prev = Preview(m, 1024);
            return $"{log}\nsaved {DemPath} ({blob.Length / 1024} KB)\n{m.Describe()}\npreview {prev}\n{Probe(m)}";
        }

        [CliCommand("rn_terrain_info", "Heightmap statistics and spot heights of known places (sanity check).")]
        public static string TerrainInfo()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(DemPath);
            if (ta == null) return "error: run rn_fetch_terrain first";
            var m = Heightmap.Load(ta.bytes);
            return m.Describe() + "\n" + Probe(m);
        }

        static string Probe(Heightmap m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (name, la, lo) in new[] {
                ("Hallasan 1947", 33.3617, 126.5292), ("Jirisan 1915", 35.3370, 127.7306), ("Seoraksan 1708", 38.1192, 128.4653),
                ("Seoul", 37.5665, 126.9780), ("Busan coast", 35.1587, 129.1604), ("Yellow Sea", 36.0, 124.5), ("East Sea", 37.5, 131.0),
                ("Ulleungdo 984", 37.4983, 130.8667), ("Fukuoka", 33.5902, 130.4017) })
            {
                Geo.Forward(la, lo, out var x, out var y);
                sb.AppendLine($"  {name,-16} x={x / 1000,7:0.0} y={y / 1000,7:0.0} km h={m.Height(x, y),6:0} m (max 5 km: {MaxNear(m, x, y, 5000),5:0})");
            }
            return sb.ToString();
        }

        static double MaxNear(Heightmap m, double x, double y, double r)
        {
            double best = double.MinValue;
            for (double dx = -r; dx <= r; dx += m.cell) for (double dy = -r; dy <= r; dy += m.cell) best = Math.Max(best, m.Height(x + dx, y + dy));
            return best;
        }

        static string Preview(Heightmap m, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    double x = -m.half + m.extent * (i + 0.5) / size, y = -m.half + m.extent * (j + 0.5) / size;
                    double h = m.Height(x, y);
                    double dzdx = (m.Height(x + m.cell, y) - m.Height(x - m.cell, y)) / (2 * m.cell);
                    double dzdy = (m.Height(x, y + m.cell) - m.Height(x, y - m.cell)) / (2 * m.cell);
                    float shade = (float)Math.Max(0.35, Math.Min(1.3, 1.0 - (dzdx * 3 - dzdy * 3)));
                    Color c = h <= 0.5 ? Color.Lerp(new Color(0.55f, 0.75f, 0.9f), new Color(0.05f, 0.15f, 0.4f), (float)Math.Min(1, -h / 3000))
                                       : Color.Lerp(new Color(0.35f, 0.6f, 0.3f), new Color(0.75f, 0.65f, 0.5f), (float)Math.Min(1, h / 1500)) * shade;
                    px[j * size + i] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "dem_preview.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }
    }
}
