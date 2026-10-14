using System.Linq;
using System.Text;
using RadarNet.Core;
using RadarNet.EditorTools;

// Visibility/Pd of each radar for a point (lat, lon, height above surface, rcs) read from AgentTools/_probe.txt
public static class Probe
{
    public static string Run()
    {
        var f = System.IO.Path.GetFullPath("AgentTools/_probe.txt");
        var a = (System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f).Trim() : "33.9,127.4,600,0.05").Split(',');
        var sim = SimCommands.Headless();
        Geo.Forward(double.Parse(a[0]), double.Parse(a[1]), out var x, out var y);
        var p = new Vec3d(x, y, sim.hm.Surface(x, y) + double.Parse(a[2]));
        double rcs = double.Parse(a[3]);
        var sb = new StringBuilder($"point {p} surface={sim.hm.Surface(x, y):0}\n");
        foreach (var r in sim.radars.OrderBy(r => (r.Pos - p).Length2D).Take(8))
        {
            double d = (r.Pos - p).Length2D;
            double az = Geo.Azimuth(p.x - r.x, p.y - r.y);
            bool vis = r.Visible(p, out var g);
            double el = Geo.Elevation(r.hr, p.z, d);
            sb.AppendLine($"{r.name} d={d / 1000:0.0}km hr={r.hr:0} el={el / Geo.Deg:0.00}° mask={r.MaskAngle(az, d) / Geo.Deg:0.00}° vis={vis} pd={r.PdAt(p, rcs, sim.hm):0.000} snr={(vis ? r.Snr(rcs, g.r, false) : 0):0.0}");
        }
        return sb.ToString();
    }
}
