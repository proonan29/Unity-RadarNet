using System;
using System.Collections.Generic;
using System.Linq;

namespace RadarNet.Core
{
    /// <summary>
    /// Automatic, fictional radar siting on the real terrain (not based on any actual facility):
    /// long-range radars on high peaks, phased arrays facing the map centre, medium-range and coastal radars spread by
    /// farthest-point sampling, gap fillers in the remaining holes, and picket ships at sea.
    /// </summary>
    public static class Siting
    {
        public class Cand { public double x, y, h; public bool coast; }

        public static int CountLR = 5, CountEW = 2, CountMR = 7, CountCST = 7, CountGap = 8, CountShip = 3;

        public static List<RadarSite> Place(Heightmap hm, out Vec3d c2)
        {
            double margin = 30e3;
            var land = new List<Cand>(); var sea = new List<Cand>();
            int step = 8;
            for (int j = 0; j < hm.n; j += step)
                for (int i = 0; i < hm.n; i += step)
                {
                    double x = hm.X(i), y = hm.Y(j);
                    if (Math.Abs(x) > hm.half - margin || Math.Abs(y) > hm.half - margin) continue;
                    double h = hm[i, j];
                    if (h > 5)
                    {
                        bool coast = false;
                        for (int a = 0; a < 8 && !coast; a++)
                            foreach (double r in new[] { 2000.0, 4000.0 })
                                if (hm.Height(x + Math.Sin(a * Math.PI / 4) * r, y + Math.Cos(a * Math.PI / 4) * r) <= 0.5) { coast = true; break; }
                        land.Add(new Cand { x = x, y = y, h = h, coast = coast });
                    }
                    else if (h < -50) sea.Add(new Cand { x = x, y = y, h = h });
                }

            var picks = new List<(RadarKind k, Cand c)>();
            double D(Cand a, Cand b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
            double MinDist(Cand c, Func<RadarKind, bool> filter)
            {
                double m = double.MaxValue;
                foreach (var p in picks) if (filter(p.k)) m = Math.Min(m, D(c, p.c));
                return m;
            }

            // long range: highest peaks with separation
            foreach (var c in land.OrderByDescending(c => c.h))
            {
                if (picks.Count(p => p.k == RadarKind.LongRange) >= CountLR) break;
                if (MinDist(c, k => k == RadarKind.LongRange) >= 250e3) picks.Add((RadarKind.LongRange, c));
            }
            // phased arrays: high ground far apart and away from the long-range sites
            foreach (var c in land.Where(c => c.h > 300).OrderByDescending(c => c.h - 0.0015 * Math.Sqrt(c.x * c.x + c.y * c.y)))
            {
                if (picks.Count(p => p.k == RadarKind.PhasedArray) >= CountEW) break;
                if (MinDist(c, k => k == RadarKind.PhasedArray) >= 420e3 && MinDist(c, k => k == RadarKind.LongRange) >= 80e3)
                    picks.Add((RadarKind.PhasedArray, c));
            }
            bool In(Cand c, double m) => Math.Abs(c.x) < m && Math.Abs(c.y) < m;
            FarthestPoint(picks, land.Where(c => c.h > 80 && c.h < 1300 && In(c, 440e3)).ToList(), RadarKind.MediumRange, CountMR, 120e3,
                k => k == RadarKind.LongRange || k == RadarKind.MediumRange || k == RadarKind.PhasedArray, c => c.h * 20);
            FarthestPoint(picks, land.Where(c => c.coast && c.h > 15 && c.h < 500 && In(c, 450e3)).ToList(), RadarKind.Coastal, CountCST, 110e3,
                k => k == RadarKind.Coastal || k == RadarKind.MediumRange, c => c.h * 30);
            FarthestPoint(picks, land.Where(c => c.h < 700 && In(c, 440e3)).ToList(), RadarKind.GapFiller, CountGap, 70e3,
                k => k != RadarKind.Naval, c => 0);
            FarthestPoint(picks, sea.Where(c => In(c, 380e3)).ToList(), RadarKind.Naval, CountShip, 150e3, k => true, c => 0);

            var order = new[] { RadarKind.LongRange, RadarKind.PhasedArray, RadarKind.MediumRange, RadarKind.Coastal, RadarKind.GapFiller, RadarKind.Naval };
            var sites = new List<RadarSite>();
            foreach (var kind in order)
                foreach (var p in picks.Where(p => p.k == kind).OrderByDescending(p => p.c.y).ThenBy(p => p.c.x))
                {
                    int idx = sites.Count;
                    double phase = (idx * 0.6180339887) % 1.0;
                    var s = new RadarSite(idx, kind, p.c.x, p.c.y, hm, phase);
                    s.name = s.spec.code + (sites.Count(q => q.spec.kind == kind) + 1).ToString("00");
                    if (kind == RadarKind.PhasedArray) s.boresight = Geo.Azimuth(-p.c.x, -p.c.y);   // face the map centre
                    sites.Add(s);
                }
            // control centre: land nearest the map centre
            var cc = land.Where(c => c.h > 20).OrderBy(c => c.x * c.x + c.y * c.y).First();
            c2 = new Vec3d(cc.x, cc.y, cc.h);
            return sites;
        }

        static void FarthestPoint(List<(RadarKind k, Cand c)> picks, List<Cand> cands, RadarKind kind, int count, double minSep,
                                  Func<RadarKind, bool> against, Func<Cand, double> bonus)
        {
            for (int n = 0; n < count; n++)
            {
                Cand best = null; double bs = double.MinValue;
                foreach (var c in cands)
                {
                    double md = double.MaxValue, mdSame = double.MaxValue;
                    foreach (var p in picks)
                    {
                        double d = Math.Sqrt((c.x - p.c.x) * (c.x - p.c.x) + (c.y - p.c.y) * (c.y - p.c.y));
                        if (against(p.k)) md = Math.Min(md, d);
                        if (p.k == kind) mdSame = Math.Min(mdSame, d);
                    }
                    if (mdSame < minSep) continue;
                    double score = Math.Min(md, 600e3) + bonus(c);
                    if (score > bs) { bs = score; best = c; }
                }
                if (best == null) return;
                picks.Add((kind, best));
            }
        }
    }
}
