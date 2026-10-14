using System;
using System.Collections.Generic;

namespace RadarNet.Core
{
    public enum MsgKind { TrackReport, DropTrack }

    /// <summary>Data link message (Link-16 J3.2 "air track" / drop-track flavour).</summary>
    public class Message
    {
        public MsgKind kind;
        public int stn;              // system track number
        public int source;           // reporting node
        public double t;             // time of validity
        public double born;          // time the STN was created
        public double[] x = new double[6], P = new double[36];
        public int tq;               // track quality 0..15
        public TrackClass cls;
        public int truthId = -1;     // evaluation only
        public int mergedInto;       // DropTrack: the STN this one was merged into (dual designation resolved)
        public double sent;
    }

    public struct Pulse { public int a, b, hop, source; public double t0, t1; public int stn; public long seq; }

    /// <summary>
    /// Data link network between radar nodes and the control centre. Messages are relayed hop by hop over a mesh
    /// (k-nearest neighbours + minimum spanning tree); every hop adds a slot latency. Delivery is broadcast to all nodes.
    /// </summary>
    public class LinkNet
    {
        public int count;
        public Vec3d[] pos;
        public readonly List<(int a, int b)> edges = new List<(int, int)>();
        public double hopLatency = 0.35;      // s per relay (TDMA slot + processing)
        public double[,] latency;             // shortest-path latency between nodes
        int[,] parent;                        // shortest-path tree parent of v when broadcasting from s

        readonly List<(double t, int to, Message m)> queue = new List<(double, int, Message)>();
        public readonly List<Pulse> pulses = new List<Pulse>();
        public long sent, delivered, pulseSeq;
        public int maxPulses = 600;

        public void Build(Vec3d[] nodes, double linkRange = 330e3, int k = 3)
        {
            count = nodes.Length; pos = nodes;
            var set = new HashSet<long>();
            void Add(int a, int b) { if (a == b) return; if (a > b) (a, b) = (b, a); if (set.Add(((long)a << 32) | (uint)b)) edges.Add((a, b)); }
            // k nearest within range
            for (int i = 0; i < count; i++)
            {
                var order = new List<int>();
                for (int j = 0; j < count; j++) if (j != i) order.Add(j);
                order.Sort((p, q) => Dist(i, p).CompareTo(Dist(i, q)));
                for (int n = 0; n < Math.Min(k, order.Count); n++) if (Dist(i, order[n]) <= linkRange) Add(i, order[n]);
            }
            // minimum spanning tree guarantees connectivity
            var inTree = new bool[count]; inTree[0] = true;
            for (int added = 1; added < count; added++)
            {
                double best = double.MaxValue; int ba = -1, bb = -1;
                for (int a = 0; a < count; a++) if (inTree[a]) for (int b = 0; b < count; b++) if (!inTree[b] && Dist(a, b) < best) { best = Dist(a, b); ba = a; bb = b; }
                inTree[bb] = true; Add(ba, bb);
            }
            // all-pairs hop latency + broadcast trees (Dijkstra from every source)
            latency = new double[count, count]; parent = new int[count, count];
            var adj = new List<int>[count];
            for (int i = 0; i < count; i++) adj[i] = new List<int>();
            foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }
            for (int s = 0; s < count; s++)
            {
                var dist = new double[count]; var done = new bool[count];
                for (int i = 0; i < count; i++) { dist[i] = double.MaxValue; parent[s, i] = -1; }
                dist[s] = 0;
                for (int it = 0; it < count; it++)
                {
                    int u = -1; double bd = double.MaxValue;
                    for (int i = 0; i < count; i++) if (!done[i] && dist[i] < bd) { bd = dist[i]; u = i; }
                    if (u < 0) break;
                    done[u] = true;
                    foreach (var v in adj[u])
                    {
                        double nd = dist[u] + hopLatency + Dist(u, v) / 3e8;
                        if (nd < dist[v]) { dist[v] = nd; parent[s, v] = u; }
                    }
                }
                for (int i = 0; i < count; i++) latency[s, i] = dist[i];
            }
        }

        public double Dist(int a, int b) => (pos[a] - pos[b]).Length2D;

        public int Hops(int s, int v) { int h = 0; while (v != s && v >= 0 && h < 64) { v = parent[s, v]; h++; } return h; }

        /// <summary>Broadcast a message from node 'from' to all other nodes.</summary>
        public void Send(Message m, int from, double t)
        {
            m.sent = t; sent++;
            for (int v = 0; v < count; v++)
            {
                if (v == from || latency[from, v] == double.MaxValue) continue;
                queue.Add((t + latency[from, v], v, m));
                int p = parent[from, v];
                if (p >= 0) pulses.Add(new Pulse { a = p, b = v, t0 = t + latency[from, p], t1 = t + latency[from, v], stn = m.stn, hop = Hops(from, v), source = from, seq = ++pulseSeq });
            }
            if (pulses.Count > maxPulses) pulses.RemoveRange(0, pulses.Count - maxPulses);
        }

        /// <summary>Deliver everything due by time t.</summary>
        public void Deliver(double t, Action<int, Message, double> receive)
        {
            if (queue.Count == 0) return;
            var due = new List<(double t, int to, Message m)>();
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].t <= t) { due.Add(queue[i]); queue.RemoveAt(i); }
            due.Sort((a, b) => a.t.CompareTo(b.t));
            foreach (var d in due) { delivered++; receive(d.to, d.m, d.t); }
        }

        public void PrunePulses(double t) => pulses.RemoveAll(p => p.t1 < t - 0.5);

        public void Clear() { queue.Clear(); pulses.Clear(); sent = delivered = 0; }   // pulseSeq keeps counting (views track it)
        public int Pending => queue.Count;
    }
}
