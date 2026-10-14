using System;
using System.Collections.Generic;
using System.Linq;

namespace RadarNet.Core
{
    public enum SysState { Active, Coasting }

    /// <summary>A network-wide track at the control centre: all reports for one STN fused by covariance intersection.</summary>
    public class SystemTrack
    {
        public int stn;
        public readonly Dictionary<int, Message> sources = new Dictionary<int, Message>();
        public double[] x = new double[6], P = new double[36];
        public double tState, tCreate, tLast;
        public SysState state;
        public TrackClass cls;
        public int truthId = -1;
        public int r2 = -1;                       // node with reporting responsibility (best track quality)
        public double tR2;
        public int tq;
        public readonly List<(double t, int node, bool cued)> chain = new List<(double, int, bool)>();
        public readonly HashSet<int> everSources = new HashSet<int>();
        public readonly List<Vec3d> trail = new List<Vec3d>();
        public double nextTrail;

        public void Estimate(double t, double[] xo, double[] Po)
        {
            Array.Copy(x, xo, 6); Array.Copy(P, Po, 36);
            bool ball = cls == TrackClass.Ballistic;
            KF.Predict(xo, Po, t - tState, RadarNode.QFor(cls), ball);
        }

        public Vec3d PosAt(double t)
        {
            double dt = t - tState;
            var p = new Vec3d(x[0] + x[3] * dt, x[1] + x[4] * dt, x[2] + x[5] * dt);
            if (cls == TrackClass.Ballistic) p.z -= 0.5 * Geo.G * dt * dt;
            return p;
        }
        public Vec3d Vel => new Vec3d(x[3], x[4], x[5]);
        public double Sigma(double t) { var xo = new double[6]; var Po = new double[36]; Estimate(t, xo, Po); return KF.PosSigma(Po); }
    }

    /// <summary>Control centre (C2) common picture built from data link reports.</summary>
    public class C2Picture
    {
        public readonly Dictionary<int, SystemTrack> tracks = new Dictionary<int, SystemTrack>();
        readonly Sim sim;
        public double freshAge = 9.0;       // report counts as current for this long (2 report intervals + latency)
        public double coastMax = 90, coastMaxBallistic = 25;

        public C2Picture(Sim sim) { this.sim = sim; }

        public void Receive(Message m, double t)
        {
            if (m.kind == MsgKind.DropTrack)
            {
                if (tracks.TryGetValue(m.stn, out var st))
                {
                    st.sources.Remove(m.source);
                    if (m.mergedInto != 0 && st.sources.Count == 0 && tracks.ContainsKey(m.mergedInto))
                    {
                        tracks.Remove(m.stn);
                        sim.Log(EventKind.Lost, t, m.stn, -1, -1, m.mergedInto);
                    }
                }
                return;
            }
            if (!tracks.TryGetValue(m.stn, out var s))
            {
                s = new SystemTrack { stn = m.stn, tCreate = t, state = SysState.Active };
                Array.Copy(m.x, s.x, 6); Array.Copy(m.P, s.P, 36); s.tState = m.t; s.cls = m.cls;
                tracks[m.stn] = s;
            }
            s.sources[m.source] = m;
            s.everSources.Add(m.source);
        }

        public void Update(double t)
        {
            var xo = new double[6]; var Po = new double[36]; var xa = new double[6]; var Pa = new double[36];
            var dead = new List<int>();
            foreach (var s in tracks.Values)
            {
                var fresh = s.sources.Values.Where(m => t - m.t <= freshAge).OrderByDescending(m => m.tq).ToList();
                if (fresh.Count > 0)
                {
                    // fuse: start from the best report, intersect the others
                    RadarNode.Extrapolate(fresh[0], t, xa, Pa);
                    for (int i = 1; i < fresh.Count; i++)
                    {
                        RadarNode.Extrapolate(fresh[i], t, xo, Po);
                        if (KF.Correlate(xa, Pa, xo, Po, 500.0 * 500.0) > 30) continue;   // inconsistent → skip
                        var xf = new double[6]; var Pf = new double[36];
                        KF.CovIntersect(xa, Pa, xo, Po, xf, Pf);
                        xa = xf; Pa = Pf;
                    }
                    Array.Copy(xa, s.x, 6); Array.Copy(Pa, s.P, 36); s.tState = t;
                    s.tLast = fresh.Max(m => m.t);
                    s.cls = Classifier.Classify(s.x, sim.hm);
                    s.tq = KF.Quality(s.P);
                    // truth label: majority of current sources (evaluation only)
                    s.truthId = fresh.GroupBy(m => m.truthId).OrderByDescending(g => g.Count()).First().Key;
                    // reporting responsibility: highest quality, keep current holder unless clearly worse
                    var cur = fresh.FirstOrDefault(m => m.source == s.r2);
                    var best = fresh[0];
                    int newR2 = cur == null || (best.tq >= cur.tq + 3 && t - s.tR2 > 20) ? best.source : s.r2;
                    if (newR2 != s.r2)
                    {
                        bool cued = sim.nodes[newR2].Holding(s.stn)?.cued ?? false;
                        if (s.r2 >= 0) sim.Log(EventKind.Handover, t, s.stn, newR2, s.r2);
                        s.chain.Add((t, newR2, cued));
                        s.r2 = newR2; s.tR2 = t;
                    }
                    if (s.state == SysState.Coasting) sim.Log(EventKind.Resumed, t, s.stn, s.r2, -1);
                    s.state = SysState.Active;
                }
                else
                {
                    if (s.state == SysState.Active) { s.state = SysState.Coasting; sim.Log(EventKind.Coasting, t, s.stn, s.r2, -1); }
                    double lim = s.cls == TrackClass.Ballistic ? coastMaxBallistic : coastMax;
                    if (t - s.tLast > lim) dead.Add(s.stn);
                }
                s.nextTrail -= 0.5;
                if (s.nextTrail <= 0) { s.trail.Add(s.PosAt(t)); s.nextTrail = s.cls == TrackClass.Ballistic ? 2 : 5; if (s.trail.Count > 300) s.trail.RemoveAt(0); }
            }
            foreach (var k in dead) { tracks.Remove(k); sim.Log(EventKind.Lost, t, k, -1, -1); }
        }

        /// <summary>Nodes holding the track right now (fresh reports).</summary>
        public IEnumerable<int> Holders(SystemTrack s, double t) => s.sources.Values.Where(m => t - m.t <= freshAge).Select(m => m.source);

        public void Clear() => tracks.Clear();
    }
}
