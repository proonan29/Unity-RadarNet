using System;
using System.Collections.Generic;
using System.Linq;

namespace RadarNet.Core
{
    public enum TrackClass { Unknown, Drone, Helicopter, Jet, Airliner, CruiseMissile, Ballistic }

    public static class Classifier
    {
        /// <summary>Kinematic classification (speed, altitude, height above surface).</summary>
        public static TrackClass Classify(double[] x, Heightmap hm)
        {
            double sp = Math.Sqrt(x[3] * x[3] + x[4] * x[4] + x[5] * x[5]);
            double agl = x[2] - hm.Surface(x[0], x[1]);
            if (x[2] > 25000 || sp > 900) return TrackClass.Ballistic;
            if (sp < 55) return TrackClass.Drone;
            if (sp < 100) return TrackClass.Helicopter;
            if (agl < 450 && sp > 150) return TrackClass.CruiseMissile;
            if (x[2] > 9000 && sp < 300) return TrackClass.Airliner;
            return TrackClass.Jet;
        }
    }

    /// <summary>A track held by one radar's own tracker.</summary>
    public class LocalTrack
    {
        public int id, stn;
        public double born;                    // creation time of the STN (older numbers win when duplicates merge)
        public bool confirmed, cued;
        public double[] x = new double[6], P = new double[36];
        public double tUpdate, tCreate, tReport = double.NegativeInfinity;
        public int hits, calm;
        public bool maneuver;
        public int mergeStn, mergeCount;
        public readonly Dictionary<int, int> votes = new Dictionary<int, int>();
        public int Truth { get { int best = -1, n = 0; foreach (var kv in votes) if (kv.Value > n) { n = kv.Value; best = kv.Key; } return best; } }
        public bool IsBallistic => x[2] > 25000 || Math.Sqrt(x[3] * x[3] + x[4] * x[4] + x[5] * x[5]) > 900;
        public double Q => IsBallistic ? 40 : maneuver || !confirmed ? RadarNode.QHigh : RadarNode.QLow;

        public void PredictTo(double t, double[] xo, double[] Po)
        {
            Array.Copy(x, xo, 6); Array.Copy(P, Po, 36);
            KF.Predict(xo, Po, t - tUpdate, Q, IsBallistic);
        }
    }

    public class Cue { public int stn, from; public double t, born; public double[] x = new double[6], P = new double[36]; }

    /// <summary>
    /// Radar node: local plot-to-track association (GNN), M-of-N initiation, Kalman tracking, and data link behaviour —
    /// track reports, system track number (STN) correlation, dual-track merging, and cueing from remote tracks (handover).
    /// </summary>
    public class RadarNode
    {
        public const double QLow = 15, QHigh = 900;
        public const double MemorySeconds = 180;    // remote/dropped tracks remembered for correlation and cueing
        public const double ReportInterval = 4;
        public const double GateConfirmed = 16, GateTentative = 22, CorrGate = 11.3;

        /// <summary>Process noise used to extrapolate a reported track of a given class (straight flyers grow slower).</summary>
        public static double QFor(TrackClass c) => c == TrackClass.Airliner ? 5 : c == TrackClass.Drone || c == TrackClass.Helicopter ? 30 : c == TrackClass.Ballistic ? 40 : c == TrackClass.CruiseMissile ? 300 : QHigh;

        public readonly RadarSite site;
        public readonly int node;
        public readonly List<LocalTrack> tracks = new List<LocalTrack>();
        public readonly Dictionary<int, Dictionary<int, Message>> remote = new Dictionary<int, Dictionary<int, Message>>();
        public readonly List<Cue> cues = new List<Cue>();
        int nextId;
        public long plotsIn, plotsAssoc;

        public RadarNode(RadarSite site, int node) { this.site = site; this.node = node; }

        double T => site.spec.period;

        // ------------------------------------------------------------------ plots

        public void ProcessPlots(List<Plot> plots, Sim sim)
        {
            if (plots.Count == 0) return;
            plotsIn += plots.Count;
            var xp = new double[6]; var Pp = new double[36];
            var pairs = new List<(double d2, Plot p, LocalTrack tr)>();
            foreach (var p in plots)
                foreach (var tr in tracks)
                {
                    if (p.t - tr.tUpdate < 0.4 * T) continue;   // one plot per track per scan
                    tr.PredictTo(p.t, xp, Pp);
                    double d2 = KF.Distance2(xp, Pp, p.pos, p.R);
                    double gate = tr.confirmed ? GateConfirmed : GateTentative;
                    if (d2 < gate) pairs.Add((d2, p, tr));
                }
            pairs.Sort((a, b) => a.d2.CompareTo(b.d2));
            var usedP = new HashSet<Plot>(); var usedT = new HashSet<LocalTrack>();
            foreach (var (d2, p, tr) in pairs)
            {
                if (usedP.Contains(p) || usedT.Contains(tr) || !tracks.Contains(tr)) continue;
                usedP.Add(p); usedT.Add(tr);
                Update(tr, p, sim);
            }
            foreach (var p in plots)
            {
                if (usedP.Contains(p)) continue;
                if (TryCue(p, sim)) continue;
                NewTentative(p);
            }
        }

        void Update(LocalTrack tr, Plot p, Sim sim)
        {
            KF.Predict(tr.x, tr.P, p.t - tr.tUpdate, tr.Q, tr.IsBallistic);
            double nis = KF.Update(tr.x, tr.P, p.pos, p.R);
            tr.tUpdate = p.t; tr.hits++; plotsAssoc++;
            if (nis > 9) { tr.maneuver = true; tr.calm = 0; }
            else if (nis < 4 && ++tr.calm >= 3) tr.maneuver = false;
            tr.votes.TryGetValue(p.truthId, out var v); tr.votes[p.truthId] = v + 1;
            if (!tr.confirmed && tr.hits >= 3) Confirm(tr, p.t, sim);
        }

        void NewTentative(Plot p)
        {
            var tr = new LocalTrack { id = ++nextId, tUpdate = p.t, tCreate = p.t, hits = 1 };
            tr.x[0] = p.pos.x; tr.x[1] = p.pos.y; tr.x[2] = p.pos.z;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) tr.P[r * 6 + c] = p.R[r * 3 + c];
            tr.P[21] = tr.P[28] = 600 * 600; tr.P[35] = 300 * 300;
            tr.votes[p.truthId] = 1;
            tracks.Add(tr);
        }

        /// <summary>A plot inside a cue gate starts a confirmed track at once, inheriting the remote STN (handover).</summary>
        bool TryCue(Plot p, Sim sim)
        {
            Cue best = null; double bd = 16;
            var xp = new double[6]; var Pp = new double[36];
            foreach (var c in cues)
            {
                Array.Copy(c.x, xp, 6); Array.Copy(c.P, Pp, 36);
                KF.Predict(xp, Pp, p.t - c.t, QHigh, xp[2] > 25000);
                double d2 = KF.Distance2(xp, Pp, p.pos, p.R);
                if (d2 < bd) { bd = d2; best = c; }
            }
            if (best == null) return false;
            var tr = new LocalTrack { id = ++nextId, tCreate = p.t, hits = 2, confirmed = true, cued = true, stn = best.stn, born = best.born };
            Array.Copy(best.x, tr.x, 6); Array.Copy(best.P, tr.P, 36);
            KF.Predict(tr.x, tr.P, p.t - best.t, QHigh, tr.x[2] > 25000);
            KF.Update(tr.x, tr.P, p.pos, p.R);
            tr.tUpdate = p.t; tr.maneuver = true;
            tr.votes[p.truthId] = 1;
            tracks.Add(tr);
            cues.Remove(best);
            plotsAssoc++;
            sim.Log(EventKind.Cued, p.t, best.stn, site.index, best.from);
            return true;
        }

        /// <summary>
        /// Confirmation (3 hits). In order: (1) duplicate of an own live track → discard; (2) an own track that lost the
        /// target hands over its STN (track recovery); (3) adopt the STN of a correlating remote/remembered track;
        /// (4) allocate a new STN from this node's block.
        /// </summary>
        void Confirm(LocalTrack tr, double t, Sim sim)
        {
            var xr = new double[6]; var Pr = new double[36];
            LocalTrack own = null; double bd = CorrGate;
            foreach (var o in tracks)
            {
                if (o == tr || !o.confirmed) continue;
                o.PredictTo(t, xr, Pr);
                double d2 = KF.Correlate(tr.x, tr.P, xr, Pr, 1000.0 * 1000.0);
                if (d2 < bd && VelocityAgrees(tr.x, tr.P, xr, Pr, t - o.tUpdate)) { bd = d2; own = o; }
            }
            if (own != null)
            {
                if (t - own.tUpdate < 1.2 * T) { tracks.Remove(tr); return; }      // duplicate of a live track
                tr.confirmed = true; tr.stn = own.stn; tr.born = own.born; tr.tReport = own.tReport;
                foreach (var kv in own.votes) { tr.votes.TryGetValue(kv.Key, out var v); tr.votes[kv.Key] = v + kv.Value; }
                tracks.Remove(own);
                return;
            }
            tr.confirmed = true;
            int stn = 0, from = -1; double born = t; bd = CorrGate;
            foreach (var kv in remote)
            {
                if (tracks.Any(o => o != tr && o.confirmed && o.stn == kv.Key)) continue;
                var m = Best(kv.Value, t);
                if (m == null || t - m.t > MemorySeconds) continue;
                Extrapolate(m, t, xr, Pr);
                double d2 = KF.Correlate(tr.x, tr.P, xr, Pr, 1000.0 * 1000.0);
                if (d2 < bd && VelocityAgrees(tr.x, tr.P, xr, Pr, t - m.t)) { bd = d2; stn = kv.Key; from = m.source; born = m.born; }
            }
            if (stn != 0) { tr.stn = stn; tr.born = born; sim.Log(EventKind.Adopted, t, stn, site.index, from); }
            else { tr.stn = sim.AllocStn(node); tr.born = t; sim.Log(EventKind.NewTrack, t, tr.stn, site.index, -1); }
        }

        /// <summary>Velocities consistent? Allows for a manoeuvre of up to ~25 m/s² during the time the estimates were apart.</summary>
        static bool VelocityAgrees(double[] x1, double[] P1, double[] x2, double[] P2, double age)
        {
            double dvx = x1[3] - x2[3], dvy = x1[4] - x2[4], dvz = x1[5] - x2[5];
            double dv = Math.Sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
            double s = Math.Sqrt((P1[21] + P1[28] + P1[35] + P2[21] + P2[28] + P2[35]) / 3);
            return dv < 120 + 3 * s + 25 * Math.Max(0, age);
        }

        // ------------------------------------------------------------------ periodic maintenance

        public void Maintain(double t, Sim sim, bool correlate)
        {
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                var tr = tracks[i];
                double age = t - tr.tUpdate;
                if (tr.confirmed && age > 1.3 * T) tr.maneuver = true;   // missed a scan: open the gate (possible manoeuvre)
                double limit = tr.confirmed ? Math.Max(3.2 * T, 12) : Math.Max(2.2 * T, 4);
                if (age > limit)
                {
                    if (tr.confirmed && tr.stn != 0)
                    {
                        sim.net.Send(new Message { kind = MsgKind.DropTrack, stn = tr.stn, source = node, t = t }, node, t);
                        sim.Log(EventKind.LocalDrop, t, tr.stn, site.index, -1);
                    }
                    tracks.RemoveAt(i);
                }
            }
            foreach (var tr in tracks)
            {
                if (!tr.confirmed) continue;
                if (t - tr.tReport >= ReportInterval) { Report(tr, t, sim); tr.tReport = t; }
            }
            if (correlate) CorrelateRemote(t, sim);
        }

        void Report(LocalTrack tr, double t, Sim sim)
        {
            var m = new Message { kind = MsgKind.TrackReport, stn = tr.stn, born = tr.born, source = node, t = t, truthId = tr.Truth };
            tr.PredictTo(t, m.x, m.P);
            m.tq = KF.Quality(m.P);
            m.cls = Classifier.Classify(m.x, sim.hm);
            sim.net.Send(m, node, t);
            Receive(m, t);   // remember own reports too (re-acquisition after a drop keeps the number)
        }

        /// <summary>
        /// Dual designation: a local track that keeps correlating with another live STN (3 checks, 2 s apart) is renumbered;
        /// the older STN survives (ties → lower number).
        /// </summary>
        void CorrelateRemote(double t, Sim sim)
        {
            var xp = new double[6]; var Pp = new double[36]; var xr = new double[6]; var Pr = new double[36];
            foreach (var tr in tracks)
            {
                if (!tr.confirmed || tr.stn == 0) continue;
                tr.PredictTo(t, xp, Pp);
                int match = 0; double bd = 9, matchBorn = 0;
                foreach (var kv in remote)
                {
                    if (kv.Key == tr.stn) continue;
                    var m = Best(kv.Value, t);
                    if (m == null || t - m.t > 12) continue;
                    Extrapolate(m, t, xr, Pr);
                    double d2 = KF.Correlate(xp, Pp, xr, Pr, 300.0 * 300.0);
                    if (d2 < bd && VelocityAgrees(xp, Pp, xr, Pr, t - m.t)) { bd = d2; match = kv.Key; matchBorn = m.born; }
                }
                if (match == 0) { tr.mergeCount = 0; continue; }
                if (match == tr.mergeStn) tr.mergeCount++; else { tr.mergeStn = match; tr.mergeCount = 1; }
                bool otherWins = matchBorn < tr.born - 0.01 || (Math.Abs(matchBorn - tr.born) <= 0.01 && match < tr.stn);
                if (tr.mergeCount >= 3 && otherWins)
                {
                    int old = tr.stn;
                    var drop = new Message { kind = MsgKind.DropTrack, stn = old, source = node, t = t, mergedInto = match };
                    sim.net.Send(drop, node, t);
                    Receive(drop, t);
                    tr.stn = match; tr.born = matchBorn; tr.mergeCount = 0; tr.tReport = double.NegativeInfinity;
                    sim.Log(EventKind.Merged, t, match, site.index, -1, old);
                }
            }
        }

        /// <summary>Remote/remembered tracks predicted inside this radar's coverage that it does not hold → cues
        /// (detection threshold lowered in the gate, instant initiation with the same STN).</summary>
        public void RefreshCues(double t)
        {
            cues.Clear();
            var held = new HashSet<int>();
            foreach (var tr in tracks) if (tr.confirmed) held.Add(tr.stn);
            foreach (var kv in remote)
            {
                if (held.Contains(kv.Key)) continue;
                var m = Best(kv.Value, t);
                if (m == null || t - m.t > MemorySeconds) continue;
                var c = new Cue { stn = kv.Key, from = m.source, t = t, born = m.born };
                Extrapolate(m, t, c.x, c.P);
                if (KF.PosSigma(c.P) > 10000 || !site.Visible(KF.Pos(c.x), out _)) continue;
                cues.Add(c);
            }
        }

        /// <summary>True if a target at p lies in one of the cue gates (the radar looks there with a lower threshold).</summary>
        public bool InCue(Vec3d p, double t)
        {
            foreach (var c in cues)
            {
                double dt = t - c.t;
                double cx = c.x[0] + c.x[3] * dt, cy = c.x[1] + c.x[4] * dt, cz = c.x[2] + c.x[5] * dt;
                double s = KF.PosSigma(c.P) + 300 * dt + 500;
                double dx = p.x - cx, dy = p.y - cy, dz = p.z - cz;
                if (dx * dx + dy * dy + dz * dz < 9 * s * s) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ data link

        /// <summary>
        /// Data link input (own reports are stored too, so a radar remembers tracks it dropped). A dropped track is kept
        /// until it ages out so that re-acquisition by any radar re-uses its STN; a merged STN is removed at once.
        /// </summary>
        public void Receive(Message m, double t)
        {
            if (m.kind == MsgKind.DropTrack)
            {
                if (m.mergedInto != 0 && remote.TryGetValue(m.stn, out var srcs)) { srcs.Remove(m.source); if (srcs.Count == 0) remote.Remove(m.stn); }
                return;
            }
            if (!remote.TryGetValue(m.stn, out var d)) remote[m.stn] = d = new Dictionary<int, Message>();
            d[m.source] = m;
        }

        public void PruneRemote(double t)
        {
            var dead = new List<int>();
            foreach (var kv in remote)
            {
                var old = kv.Value.Where(s => t - s.Value.t > MemorySeconds).Select(s => s.Key).ToList();
                foreach (var o in old) kv.Value.Remove(o);
                if (kv.Value.Count == 0) dead.Add(kv.Key);
            }
            foreach (var k in dead) remote.Remove(k);
        }

        /// <summary>Best remote report: highest quality after accounting for age.</summary>
        public static Message Best(Dictionary<int, Message> srcs, double t)
        {
            Message best = null; double bs = double.MinValue;
            foreach (var m in srcs.Values)
            {
                double score = m.tq - (t - m.t) * 0.5;
                if (score > bs) { bs = score; best = m; }
            }
            return best;
        }

        public static void Extrapolate(Message m, double t, double[] xo, double[] Po)
        {
            Array.Copy(m.x, xo, 6); Array.Copy(m.P, Po, 36);
            KF.Predict(xo, Po, t - m.t, QFor(m.cls), m.cls == TrackClass.Ballistic);
        }

        public LocalTrack Holding(int stn) { foreach (var tr in tracks) if (tr.confirmed && tr.stn == stn) return tr; return null; }
        public int Confirmed => tracks.Count(tr => tr.confirmed);

        public void Clear() { tracks.Clear(); remote.Clear(); cues.Clear(); nextId = 0; plotsIn = plotsAssoc = 0; }
    }
}
