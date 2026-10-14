using System;
using System.IO;
using System.Linq;
using System.Text;
using RadarNet.Core;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace RadarNet.EditorTools
{
    /// <summary>Headless simulation commands (no scene needed): radar list, coverage, scenario runs.</summary>
    public static class SimCommands
    {
        static Sim cached;

        public static Sim Headless(bool rebuild = false)
        {
            if (cached != null && !rebuild) return cached;
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(TerrainCommands.DemPath);
            if (ta == null) throw new Exception("heightmap missing: run rn_fetch_terrain");
            cached = new Sim(Heightmap.Load(ta.bytes));
            return cached;
        }

        static ScenarioKind ParseScenario(string s) => Enum.TryParse<ScenarioKind>(s, true, out var k) ? k : ScenarioKind.Mixed;

        [CliCommand("rn_radars", "List the (fictional, automatically sited) radars, the control centre and the data link mesh.")]
        public static string Radars([CliArg("rebuild", "Rebuild the network")] bool rebuild = false)
        {
            var sim = Headless(rebuild);
            var sb = new StringBuilder();
            sb.AppendLine($"radars={sim.radars.Count} build_ms={sim.buildMs:0} C2 at {Geo.LatLon(sim.c2Pos.x, sim.c2Pos.y)} link edges={sim.net.edges.Count}");
            foreach (var r in sim.radars)
                sb.AppendLine($"  {r.name} {r.spec.kind,-11} {Geo.LatLon(r.x, r.y)} x={r.x / 1000,6:0} y={r.y / 1000,6:0} km ground={r.ground,5:0} m{(r.atSea ? " (ship)" : "")}{(r.Rotating ? "" : $" face={r.boresight / Geo.Deg:0}°")} hops→C2={sim.net.Hops(r.index, sim.C2Node)} latency={sim.net.latency[r.index, sim.C2Node]:0.00}s");
            sb.AppendLine("links: " + string.Join(" ", sim.net.edges.Select(e => $"{sim.NodeName(e.a)}-{sim.NodeName(e.b)}")));
            return sb.ToString();
        }

        [CliCommand("rn_sim_run", "Headless run of a scenario for N simulated seconds; prints per-target detection/track continuity/handover statistics.")]
        public static string SimRun([CliArg("scenario", "Mixed, CruiseMissiles, DroneSwarm, Ballistic, Helicopters, Airliners, AllTypes")] string scenario = "Mixed",
                                    [CliArg("duration", "Simulated seconds")] double duration = 900,
                                    [CliArg("seed", "Random seed")] int seed = 1,
                                    [CliArg("events", "Print the last N events")] int events = 0)
        {
            var sim = Headless();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sim.Reset(ParseScenario(scenario), seed);
            sim.Advance(duration);
            var sb = new StringBuilder();
            sb.AppendLine($"wall={sw.ElapsedMilliseconds} ms (x{duration * 1000 / Math.Max(1, sw.ElapsedMilliseconds):0} realtime)");
            sb.Append(sim.Report());
            if (events > 0)
                foreach (var e in sim.events.Skip(Math.Max(0, sim.events.Count - events)))
                    sb.AppendLine($"  {e.t,7:0.0} {e.kind,-9} stn={e.stn} {sim.NodeName(e.radar)} {sim.NodeName(e.other)} aux={e.aux}");
            return sb.ToString();
        }

        [CliCommand("rn_coverage", "Coverage of the network for a target of given RCS at a height above the surface: % of land/sea area with Pd ≥ 0.5 by ≥1 / ≥2 radars. Writes Screenshots/coverage_<alt>.png")]
        public static string Coverage([CliArg("alt", "Height above surface (m)")] double alt = 1000,
                                      [CliArg("rcs", "RCS (m²)")] double rcs = 1,
                                      [CliArg("size", "Grid size")] int size = 400)
        {
            var sim = Headless();
            var hm = sim.hm;
            var counts = new byte[size * size];
            System.Threading.Tasks.Parallel.For(0, size, j =>
            {
                for (int i = 0; i < size; i++)
                {
                    double x = -hm.half + hm.extent * (i + 0.5) / size, y = -hm.half + hm.extent * (j + 0.5) / size;
                    var p = new Vec3d(x, y, hm.Surface(x, y) + alt);
                    int c = 0;
                    foreach (var r in sim.radars) if (r.PdAt(p, rcs, hm) >= 0.5) c++;
                    counts[j * size + i] = (byte)Math.Min(255, c);
                }
            });
            int land = 0, sea = 0, land1 = 0, land2 = 0, sea1 = 0, sea2 = 0;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    double x = -hm.half + hm.extent * (i + 0.5) / size, y = -hm.half + hm.extent * (j + 0.5) / size;
                    bool isSea = hm.IsSea(x, y);
                    int c = counts[j * size + i];
                    if (isSea) { sea++; if (c >= 1) sea1++; if (c >= 2) sea2++; } else { land++; if (c >= 1) land1++; if (c >= 2) land2++; }
                    Color baseC = isSea ? new Color(0.15f, 0.25f, 0.4f) : new Color(0.35f, 0.35f, 0.3f);
                    Color cc = c == 0 ? new Color(0.8f, 0.2f, 0.2f) : c == 1 ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.2f, 0.8f, 0.3f);
                    px[j * size + i] = Color.Lerp(baseC, cc, 0.55f);
                }
            foreach (var r in sim.radars)
            {
                int i = (int)((r.x + hm.half) / hm.extent * size), j = (int)((r.y + hm.half) / hm.extent * size);
                for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii >= 0 && jj >= 0 && ii < size && jj < size) px[jj * size + ii] = Color.white;
                    }
            }
            tex.SetPixels32(px); tex.Apply();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"coverage_{alt:0}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return $"alt={alt}m rcs={rcs}m² land ≥1: {100.0 * land1 / land:0.0}% ≥2: {100.0 * land2 / land:0.0}% | sea ≥1: {100.0 * sea1 / sea:0.0}% ≥2: {100.0 * sea2 / sea:0.0}% → {path}";
        }
    }
}
