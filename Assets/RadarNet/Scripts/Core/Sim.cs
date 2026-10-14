using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RadarNet.Core
{
    public enum EventKind { Spawned, Ended, NewTrack, Adopted, Cued, Handover, Merged, LocalDrop, Coasting, Resumed, Lost }

    public struct SimEvent
    {
        public double t;
        public EventKind kind;
        public int stn, radar, other, aux;
    }

    /// <summary>Evaluation statistics per ground-truth object.</summary>
    public class TargetStats
    {
        public int id;
        public PlatformKind kind;
        public double tSpawn, tEnd = double.NaN, tFirstPlot = double.NaN, tFirstTrack = double.NaN;
        public readonly HashSet<int> stns = new HashSet<int>();
        public readonly Dictionary<int, double> stnTime = new Dictionary<int, double>();
        /// <summary>STNs that labelled this object for at least 15 s (short-lived dual designations excluded).</summary>
        public int StableStns { get { int n = 0; foreach (var v in stnTime.Values) if (v >= 15) n++; return Math.Max(n, stns.Count > 0 ? 1 : 0); } }
        public readonly HashSet<int> radars = new HashSet<int>();
        public int handovers, cues;
        public double alive, tracked, sinceFirst, err2;
        public int errN;
        public double Rms => errN > 0 ? Math.Sqrt(err2 / errN) : double.NaN;
        public double Continuity => sinceFirst > 0 ? tracked / sinceFirst : 0;
    }

    /// <summary>Deterministic fixed-step simulation: targets → radar scans → local trackers → data link → control centre.</summary>
    public class Sim
    {
        public const double Dt = 0.1;
        public readonly Heightmap hm;
        public readonly List<RadarSite> radars;
        public readonly List<RadarNode> nodes = new List<RadarNode>();
        public readonly LinkNet net = new LinkNet();
        public readonly C2Picture c2;
        public readonly Vec3d c2Pos;
        public int C2Node => radars.Count;

        public readonly List<Target> targets = new List<Target>();
        public readonly Dictionary<int, TargetStats> stats = new Dictionary<int, TargetStats>();
        public readonly List<SimEvent> events = new List<SimEvent>();
        public long eventCount;
        public double t;
        public long step;
        public int seed;
        public ScenarioKind scenario = ScenarioKind.Empty;
        public Random rng;
        int nextTarget;
        int[] stnSeq;
        public long plots, falseAlarms;
        public double buildMs;
        public Action<SimEvent> onEvent;

        readonly List<Plot> buf = new List<Plot>();

        public Sim(Heightmap hm)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            this.hm = hm;
            radars = Siting.Place(hm, out c2Pos);
            System.Threading.Tasks.Parallel.ForEach(radars, r => r.BuildScreening(hm));
            for (int i = 0; i < radars.Count; i++) nodes.Add(new RadarNode(radars[i], i));
            var pos = radars.Select(r => r.Pos).ToList();
            pos.Add(c2Pos);
            net.Build(pos.ToArray());
            c2 = new C2Picture(this);
            stnSeq = new int[radars.Count + 1];
            buildMs = sw.Elapsed.TotalMilliseconds;
            Reset(ScenarioKind.Empty, 1);
        }

        public void Reset(ScenarioKind sc, int seed)
        {
            this.seed = seed; scenario = sc;
            rng = new Random(seed);
            t = 0; step = 0; plots = falseAlarms = 0;
            targets.Clear(); stats.Clear(); events.Clear(); eventCount = 0;
            foreach (var n in nodes) n.Clear();
            net.Clear(); c2.Clear();
            for (int i = 0; i < stnSeq.Length; i++) stnSeq[i] = 0;
            nextTarget = 0;
            if (sc != ScenarioKind.Empty) targets.AddRange(Scenarios.Build(sc, seed, ref nextTarget));
        }

        public Target AddTarget(PlatformKind kind, Vec3d from, Vec3d to, double delay = 0)
        {
            var tg = Scenarios.Single(kind, from, to, t + delay, ++nextTarget);
            targets.Add(tg);
            return tg;
        }

        /// <summary>STN blocks per node, like Link-16 track number blocks: node k issues (k+1)·1000 + n.</summary>
        public int AllocStn(int node) { stnSeq[node] = stnSeq[node] % 999 + 1; return (node + 1) * 1000 + stnSeq[node]; }

        public void Log(EventKind k, double time, int stn, int radar, int other, int aux = 0)
        {
            var e = new SimEvent { t = time, kind = k, stn = stn, radar = radar, other = other, aux = aux };
            events.Add(e); eventCount++;
            if (events.Count > 500) events.RemoveRange(0, events.Count - 400);
            if (k == EventKind.Handover || k == EventKind.Cued)
            {
                int truth = c2.tracks.TryGetValue(stn, out var st) ? st.truthId : -1;
                if (truth >= 0 && stats.TryGetValue(truth, out var s)) { if (k == EventKind.Handover) s.handovers++; else s.cues++; }
            }
            onEvent?.Invoke(e);
        }

        public void Advance(double seconds)
        {
            long n = (long)Math.Round(seconds / Dt);
            for (long i = 0; i < n; i++) Step();
        }

        public void Step()
        {
            // 1. targets
            foreach (var tg in targets)
            {
                if (!tg.started && tg.alive && t >= tg.tSpawn)
                {
                    tg.Start(hm);
                    stats[tg.id] = new TargetStats { id = tg.id, kind = tg.kind, tSpawn = t };
                    Log(EventKind.Spawned, t, 0, -1, -1, tg.id);
                }
                if (!tg.started || !tg.alive) continue;
                tg.Step(t + Dt, Dt, hm);
                if (!tg.alive)
                {
                    if (double.IsNaN(tg.tEnd)) tg.tEnd = t + Dt;
                    if (stats.TryGetValue(tg.id, out var st)) st.tEnd = tg.tEnd;
                    Log(EventKind.Ended, t, 0, -1, -1, tg.id);
                }
            }

            // 2. radar scans → plots → local trackers
            for (int i = 0; i < radars.Count; i++)
            {
                var r = radars[i]; var node = nodes[i];
                buf.Clear();
                foreach (var tg in targets)
                {
                    if (!tg.started || !tg.alive) continue;
                    double az = Geo.Azimuth(tg.pos.x - r.x, tg.pos.y - r.y);
                    if (!r.Illuminates(az, t, Dt, out double tHit)) continue;
                    var p = tg.pos + tg.vel * (tHit - (t + Dt));   // targets were already moved to t + Dt
                    if (!r.Visible(p, out var g)) continue;
                    bool low = p.z - hm.Surface(p.x, p.y) < 200;
                    double snr = r.Snr(tg.spec.rcs, g.r, low);
                    bool cued = node.cues.Count > 0 && node.InCue(p, tHit);
                    double pd = RadarSite.Pd(snr, cued ? RadarSite.PfaCued : RadarSite.Pfa);
                    if (rng.NextDouble() >= pd) continue;
                    var pl = r.Measure(p, g, tHit, snr, rng);
                    pl.truthId = tg.id; pl.cued = cued;
                    buf.Add(pl); plots++;
                    var st = stats[tg.id];
                    if (double.IsNaN(st.tFirstPlot)) st.tFirstPlot = tHit;
                }
                double fa = r.Rotating ? r.spec.faPerScan * Dt / r.spec.period : (r.Illuminates(r.boresight, t, Dt, out _) ? r.spec.faPerScan : 0);
                if (fa > 0 && rng.NextDouble() < fa) { buf.Add(r.FalseAlarm(t, Dt, rng)); falseAlarms++; }
                if (buf.Count > 0) { buf.Sort((a, b) => a.t.CompareTo(b.t)); node.ProcessPlots(buf, this); }
            }

            t += Dt; step++;

            // 3. data link delivery
            net.Deliver(t, (to, m, tt) => { if (to == C2Node) c2.Receive(m, tt); else nodes[to].Receive(m, tt); });

            // 4. periodic work
            if (step % 5 == 0)
            {
                bool corr = step % 20 == 0;
                foreach (var n in nodes) n.Maintain(t, this, corr);
                c2.Update(t);
            }
            if (step % 10 == 0)
            {
                foreach (var n in nodes) n.RefreshCues(t);
                net.PrunePulses(t);
                UpdateStats(1.0);
            }
            if (step % 100 == 0) foreach (var n in nodes) n.PruneRemote(t);
        }

        void UpdateStats(double dt)
        {
            var bySys = new Dictionary<int, List<SystemTrack>>();
            foreach (var s in c2.tracks.Values)
            {
                if (s.truthId < 0) continue;
                if (!bySys.TryGetValue(s.truthId, out var l)) bySys[s.truthId] = l = new List<SystemTrack>();
                l.Add(s);
            }
            foreach (var tg in targets)
            {
                if (!tg.started || !tg.alive || !stats.TryGetValue(tg.id, out var st)) continue;
                st.alive += dt;
                if (!double.IsNaN(st.tFirstTrack)) st.sinceFirst += dt;
                if (!bySys.TryGetValue(tg.id, out var l)) continue;
                double bestErr = double.MaxValue; bool active = false;
                foreach (var s in l)
                {
                    st.stns.Add(s.stn);
                    st.stnTime.TryGetValue(s.stn, out var tt); st.stnTime[s.stn] = tt + dt;
                    if (s.state != SysState.Active) continue;
                    active = true;
                    bestErr = Math.Min(bestErr, (s.PosAt(t) - tg.pos).Length);
                }
                if (active)
                {
                    if (double.IsNaN(st.tFirstTrack)) { st.tFirstTrack = t; st.sinceFirst = dt; }
                    st.tracked += dt;
                    st.err2 += bestErr * bestErr; st.errN++;
                }
            }
            foreach (var n in nodes)
                foreach (var tr in n.tracks)
                    if (tr.confirmed) { int id = tr.Truth; if (id >= 0 && stats.TryGetValue(id, out var st)) st.radars.Add(n.site.index); }
        }

        // ------------------------------------------------------------------ summaries

        public int Alive => targets.Count(x => x.started && x.alive);
        public int SysActive => c2.tracks.Values.Count(s => s.state == SysState.Active);
        public int SysCoasting => c2.tracks.Values.Count(s => s.state == SysState.Coasting);

        public (int n, int detected, double meanStn, double continuity, double rms, int handovers, double latency) Totals()
        {
            var l = stats.Values.ToList();
            int det = l.Count(s => !double.IsNaN(s.tFirstTrack));
            var withTrack = l.Where(s => !double.IsNaN(s.tFirstTrack)).ToList();
            double meanStn = withTrack.Count > 0 ? withTrack.Average(s => (double)s.StableStns) : 0;
            double cont = withTrack.Count > 0 ? withTrack.Average(s => s.Continuity) : 0;
            double e2 = withTrack.Sum(s => s.err2); int en = withTrack.Sum(s => s.errN);
            double lat = withTrack.Count > 0 ? withTrack.Average(s => s.tFirstTrack - s.tSpawn) : double.NaN;
            return (l.Count, det, meanStn, cont, en > 0 ? Math.Sqrt(e2 / en) : double.NaN, l.Sum(s => s.handovers), lat);
        }

        public string Report()
        {
            var sb = new StringBuilder();
            var tot = Totals();
            sb.AppendLine($"t={t:0.0}s scenario={scenario} seed={seed} targets={tot.n} alive={Alive} detected={tot.detected} sys active={SysActive} coasting={SysCoasting} STN/target={tot.meanStn:0.00} continuity={tot.continuity * 100:0.0}% rms={tot.rms:0}m handovers={tot.handovers} first-track latency={tot.latency:0.0}s");
            sb.AppendLine($"plots={plots} false_alarms={falseAlarms} link sent={net.sent} delivered={net.delivered} events={eventCount}");
            sb.AppendLine("id  kind              spawn   end     1st plot 1st trk  STNs(all) radars HO cue continuity rms(m)");
            foreach (var s in stats.Values.OrderBy(s => s.id))
            {
                string F(double v) => double.IsNaN(v) ? "   -  " : v.ToString("0").PadLeft(6);
                sb.AppendLine($"T{s.id:00} {s.kind,-16} {F(s.tSpawn)} {F(s.tEnd)} {F(s.tFirstPlot)}  {F(s.tFirstTrack)}  {s.StableStns,3}({s.stns.Count,2})  {s.radars.Count,4}  {s.handovers,3} {s.cues,3}  {s.Continuity * 100,6:0.0}%  {(double.IsNaN(s.Rms) ? "-" : s.Rms.ToString("0")),6}  stn=[{string.Join(",", s.stns.OrderBy(x => x))}] radars=[{string.Join(",", s.radars.OrderBy(x => x).Select(x => radars[x].name))}]");
            }
            return sb.ToString();
        }

        public string NodeName(int node) => node >= 0 && node < radars.Count ? radars[node].name : node == C2Node ? "C2" : "-";
    }
}
