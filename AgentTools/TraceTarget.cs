using System.Linq;
using System.Text;
using RadarNet.Core;
using RadarNet.EditorTools;

// Timeline of one ground-truth target: local tracks per radar, C2 tracks, events touching its STNs.
public static class TraceTarget
{
    public static string Run()
    {
        var f = System.IO.Path.GetFullPath("AgentTools/_trace.txt"); var args = System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f).Trim() : "Mixed,7,2400,10";
        var a = args.Split(',');
        var sim = SimCommands.Headless();
        sim.Reset((ScenarioKind)System.Enum.Parse(typeof(ScenarioKind), a[0]), 1);
        int id = int.Parse(a[1]); double dur = double.Parse(a[2]), every = double.Parse(a[3]);
        var sb = new StringBuilder();
        var stns = new System.Collections.Generic.HashSet<int>();
        sim.onEvent = e => { if (stns.Contains(e.stn) || e.aux == id && (e.kind == EventKind.Spawned || e.kind == EventKind.Ended)) sb.AppendLine($"   ev {e.t,7:0.0} {e.kind} stn={e.stn} {sim.NodeName(e.radar)}<-{sim.NodeName(e.other)} aux={e.aux}"); };
        for (double t = 0; t < dur; t += every)
        {
            sim.Advance(every);
            var tg = sim.targets.FirstOrDefault(x => x.id == id);
            if (tg == null || !tg.started) continue;
            if (!tg.alive && sim.t - tg.tEnd > every) break;
            var loc = new StringBuilder();
            foreach (var n in sim.nodes)
                foreach (var tr in n.tracks)
                    if (tr.Truth == id) { loc.Append($" {n.site.name}:{(tr.confirmed ? tr.stn.ToString() : "tent")}/{sim.t - tr.tUpdate:0}s{(tr.cued ? "c" : "")}"); if (tr.stn > 0) stns.Add(tr.stn); }
            var sys = string.Join(" ", sim.c2.tracks.Values.Where(s => s.truthId == id).Select(s => $"{s.stn}{(s.state == SysState.Coasting ? "~" : "")}r2={sim.NodeName(s.r2)}"));
            var vis = string.Join(",", sim.radars.Where(r => r.Visible(tg.pos, out _)).Select(r => $"{r.name}({r.PdAt(tg.pos, tg.spec.rcs, sim.hm):0.00})"));
            sb.AppendLine($"t={sim.t,6:0} pos={tg.pos} agl={tg.Agl(sim.hm):0} | local:{loc} | c2: {sys} | vis: {vis}");
        }
        sim.onEvent = null;
        return sb.ToString();
    }
}
