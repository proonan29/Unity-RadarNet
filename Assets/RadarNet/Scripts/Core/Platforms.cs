using System;
using System.Collections.Generic;

namespace RadarNet.Core
{
    public enum PlatformKind { Drone, Helicopter, Fighter, Airliner, CruiseMissile, BallisticMissile }

    /// <summary>Generic flight parameters per platform kind (illustrative values, not any specific system).</summary>
    public class PlatformSpec
    {
        public PlatformKind kind;
        public double rcs;          // mean radar cross section (m²), Swerling 1 fluctuation
        public double speed;        // cruise ground speed (m/s)
        public double alt;          // cruise altitude (m): above surface when agl, else MSL
        public bool agl;
        public double turnRate;     // rad/s
        public double climbRate;    // m/s
        public double minClear;     // minimum clearance above surface (m)

        static readonly Dictionary<PlatformKind, PlatformSpec> table = new Dictionary<PlatformKind, PlatformSpec>
        {
            { PlatformKind.Drone,            new PlatformSpec { kind = PlatformKind.Drone, rcs = 0.05, speed = 45, alt = 600, agl = true, turnRate = 6 * Geo.Deg, climbRate = 5, minClear = 80 } },
            { PlatformKind.Helicopter,       new PlatformSpec { kind = PlatformKind.Helicopter, rcs = 5, speed = 65, alt = 120, agl = true, turnRate = 6 * Geo.Deg, climbRate = 10, minClear = 40 } },
            { PlatformKind.Fighter,          new PlatformSpec { kind = PlatformKind.Fighter, rcs = 3, speed = 260, alt = 7500, agl = false, turnRate = 5 * Geo.Deg, climbRate = 80, minClear = 300 } },
            { PlatformKind.Airliner,         new PlatformSpec { kind = PlatformKind.Airliner, rcs = 40, speed = 235, alt = 10700, agl = false, turnRate = 1.5 * Geo.Deg, climbRate = 12, minClear = 600 } },
            { PlatformKind.CruiseMissile,    new PlatformSpec { kind = PlatformKind.CruiseMissile, rcs = 0.1, speed = 240, alt = 60, agl = true, turnRate = 3 * Geo.Deg, climbRate = 40, minClear = 25 } },
            { PlatformKind.BallisticMissile, new PlatformSpec { kind = PlatformKind.BallisticMissile, rcs = 0.3, speed = 1800, alt = 0, agl = false, turnRate = 0, climbRate = 0, minClear = 0 } },
        };

        public static PlatformSpec Get(PlatformKind k) => table[k];
    }

    /// <summary>A flying object (ground truth). Waypoint flight with turn/climb limits and terrain following, or a ballistic arc.</summary>
    public class Target
    {
        public int id;
        public PlatformKind kind;
        public PlatformSpec spec;
        public Vec3d pos, vel;
        public double heading, speed, cruiseAlt;
        public bool alive = true, started;
        public double tSpawn, tEnd = double.NaN;
        public string endReason = "";
        public readonly List<Vec3d> route = new List<Vec3d>();   // x, y waypoints (z unused)
        public int wp;
        public readonly List<Vec3d> trail = new List<Vec3d>();
        double nextTrail;

        // ballistic arc
        public Vec3d launch, impact;
        public double flightTime, apogee, vh, vz0;

        public string Name => $"T{id:00}";

        public static Target Waypoints(int id, PlatformKind kind, IList<Vec3d> pts, double tSpawn, double speedScale = 1, double altOverride = double.NaN)
        {
            var s = PlatformSpec.Get(kind);
            var t = new Target { id = id, kind = kind, spec = s, tSpawn = tSpawn, speed = s.speed * speedScale, cruiseAlt = double.IsNaN(altOverride) ? s.alt : altOverride };
            t.route.AddRange(pts);
            t.pos = new Vec3d(pts[0].x, pts[0].y, 0);
            t.wp = pts.Count > 1 ? 1 : 0;
            if (pts.Count > 1) t.heading = Geo.Azimuth(pts[1].x - pts[0].x, pts[1].y - pts[0].y);
            return t;
        }

        public static Target Ballistic(int id, Vec3d from, Vec3d to, double tSpawn, double apogeeFrac = 0.28)
        {
            var t = new Target { id = id, kind = PlatformKind.BallisticMissile, spec = PlatformSpec.Get(PlatformKind.BallisticMissile), tSpawn = tSpawn };
            t.launch = from; t.impact = to;
            double d = (to - from).Length2D;
            t.apogee = Math.Max(25000, Math.Min(450000, apogeeFrac * d));
            t.flightTime = 2 * Math.Sqrt(2 * t.apogee / Geo.G);
            t.vh = d / t.flightTime;
            t.vz0 = Math.Sqrt(2 * Geo.G * t.apogee);
            t.route.Add(from); t.route.Add(to);
            t.pos = from;
            t.heading = Geo.Azimuth(to.x - from.x, to.y - from.y);
            t.speed = t.vh;
            return t;
        }

        /// <summary>Called once when the target appears (sets initial altitude and velocity).</summary>
        public void Start(Heightmap hm)
        {
            started = true;
            if (kind == PlatformKind.BallisticMissile)
            {
                pos = new Vec3d(launch.x, launch.y, hm.Surface(launch.x, launch.y));
                launch = pos;
                impact = new Vec3d(impact.x, impact.y, hm.Surface(impact.x, impact.y));
            }
            else
            {
                double surf = hm.Surface(pos.x, pos.y);
                pos.z = spec.agl ? surf + cruiseAlt : Math.Max(cruiseAlt, surf + spec.minClear);
            }
            vel = new Vec3d(Math.Sin(heading) * speed, Math.Cos(heading) * speed, 0);
            trail.Clear(); trail.Add(pos); nextTrail = 0;
        }

        public void Step(double t, double dt, Heightmap hm)
        {
            if (!alive) return;
            var old = pos;
            if (kind == PlatformKind.BallisticMissile) StepBallistic(t, hm);
            else StepWaypoints(dt, hm);
            vel = (pos - old) / dt;
            if (alive && !hm.InBounds(pos.x, pos.y)) { alive = false; tEnd = t; endReason = "exit"; }
            nextTrail -= dt;
            if (nextTrail <= 0) { trail.Add(pos); nextTrail = kind == PlatformKind.BallisticMissile ? 2 : 5; if (trail.Count > 400) trail.RemoveAt(0); }
        }

        void StepBallistic(double t, Heightmap hm)
        {
            double tau = t - tSpawn;
            if (tau >= flightTime) { pos = impact; alive = false; tEnd = t; endReason = "impact"; return; }
            double f = tau / flightTime;
            var h = Vec3d.Lerp(launch, impact, f);
            double z = launch.z + (impact.z - launch.z) * f + vz0 * tau - 0.5 * Geo.G * tau * tau;
            pos = new Vec3d(h.x, h.y, z);
        }

        void StepWaypoints(double dt, Heightmap hm)
        {
            if (wp >= route.Count) { alive = false; endReason = "arrived"; return; }
            var target = route[wp];
            double dx = target.x - pos.x, dy = target.y - pos.y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double turnRadius = spec.turnRate > 0 ? speed / spec.turnRate : 0;
            if (dist < Math.Max(speed * dt * 3, wp == route.Count - 1 ? speed * dt * 3 : Math.Min(turnRadius * 0.6, 4000)))
            {
                wp++;
                if (wp >= route.Count) { alive = false; endReason = "arrived"; return; }
                target = route[wp]; dx = target.x - pos.x; dy = target.y - pos.y;
            }
            double want = Geo.Azimuth(dx, dy);
            double turn = Geo.WrapPi(want - heading);
            double maxTurn = spec.turnRate * dt;
            heading = Geo.Wrap2Pi(heading + Math.Max(-maxTurn, Math.Min(maxTurn, turn)));
            double sx = Math.Sin(heading), sy = Math.Cos(heading);
            double nx = pos.x + sx * speed * dt, ny = pos.y + sy * speed * dt;

            // altitude: terrain following with look-ahead (agl) or cruise level
            double surf = hm.Surface(nx, ny);
            double desired;
            if (spec.agl)
            {
                double ahead = surf;
                foreach (double s in new[] { 4.0, 8.0, 15.0, 25.0 })
                    ahead = Math.Max(ahead, hm.Surface(nx + sx * speed * s, ny + sy * speed * s) - spec.climbRate * s * 0.5);
                desired = ahead + cruiseAlt;
                if (kind == PlatformKind.CruiseMissile && surf <= 0.5 && ahead <= 0.5) desired = 20;   // sea skimming
            }
            else desired = Math.Max(cruiseAlt, surf + spec.minClear);
            double dz = desired - pos.z;
            double maxDz = spec.climbRate * dt;
            double nz = pos.z + Math.Max(-maxDz, Math.Min(maxDz, dz));
            nz = Math.Max(nz, surf + spec.minClear);   // never into the ground
            pos = new Vec3d(nx, ny, nz);
        }

        public double Agl(Heightmap hm) => pos.z - hm.Surface(pos.x, pos.y);
    }
}
