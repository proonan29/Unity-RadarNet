using System;
using System.Collections.Generic;

namespace RadarNet.Core
{
    public enum ScenarioKind { Mixed, CruiseMissiles, DroneSwarm, Ballistic, Helicopters, Airliners, AllTypes, Empty }

    /// <summary>Preset traffic (generic test routes over the real map). Positions get a small seed-dependent jitter.</summary>
    public static class Scenarios
    {
        static Vec3d LL(double lat, double lon) { Geo.Forward(lat, lon, out var x, out var y); return new Vec3d(x, y, 0); }

        class Builder
        {
            public readonly List<Target> list = new List<Target>();
            public Random rng; public int id;
            public double jitter = 8000;
            Vec3d J(Vec3d p) => new Vec3d(p.x + (rng.NextDouble() - 0.5) * 2 * jitter, p.y + (rng.NextDouble() - 0.5) * 2 * jitter, 0);
            public void Route(PlatformKind k, double t, params (double lat, double lon)[] pts)
            {
                var r = new List<Vec3d>();
                foreach (var p in pts) r.Add(J(LL(p.lat, p.lon)));
                list.Add(Target.Waypoints(++id, k, r, t + rng.NextDouble() * 10, 0.95 + rng.NextDouble() * 0.1));
            }
            public void Loop(PlatformKind k, double t, int laps, params (double lat, double lon)[] pts)
            {
                var all = new List<(double, double)>();
                for (int i = 0; i < laps; i++) all.AddRange(pts);
                all.Add(pts[0]);
                Route(k, t, all.ToArray());
            }
            public void Ballistic(double t, (double lat, double lon) a, (double lat, double lon) b) =>
                list.Add(Target.Ballistic(++id, J(LL(a.lat, a.lon)), J(LL(b.lat, b.lon)), t));
        }

        public static List<Target> Build(ScenarioKind kind, int seed, ref int nextId)
        {
            var b = new Builder { rng = new Random(seed * 7919 + (int)kind), id = nextId };
            switch (kind)
            {
                case ScenarioKind.Mixed: Mixed(b); break;
                case ScenarioKind.CruiseMissiles: Cruise(b, 0); break;
                case ScenarioKind.DroneSwarm: Drones(b, 0, 8); break;
                case ScenarioKind.Ballistic:
                    BallisticSet(b, 0);
                    b.Route(PlatformKind.Airliner, 0, (35.0, 122.3), (38.0, 133.2));
                    b.Route(PlatformKind.Airliner, 30, (37.5, 127.0), (33.6, 130.4), (32.5, 132.8));
                    break;
                case ScenarioKind.Helicopters: Helis(b, 0); break;
                case ScenarioKind.Airliners: Airliners(b, 0); break;
                case ScenarioKind.AllTypes:
                    Mixed(b); Cruise(b, 120); Drones(b, 60, 4);
                    b.Ballistic(300, (35.0, 123.5), (38.5, 131.5));
                    break;
            }
            nextId = b.id;
            return b.list;
        }

        static void Airliners(Builder b, double t)
        {
            b.Route(PlatformKind.Airliner, t + 0, (32.4, 124.0), (36.2, 126.0), (37.45, 126.45));
            b.Route(PlatformKind.Airliner, t + 20, (37.5, 127.0), (33.6, 130.4), (32.5, 132.8));
            b.Route(PlatformKind.Airliner, t + 40, (40.8, 123.0), (35.5, 133.0));
            b.Route(PlatformKind.Airliner, t + 60, (35.0, 122.3), (38.0, 133.2));
            b.Route(PlatformKind.Airliner, t + 90, (32.2, 128.0), (35.2, 128.2), (40.8, 128.4));
            b.Route(PlatformKind.Airliner, t + 120, (39.8, 133.3), (36.8, 127.9), (33.0, 122.4));
        }

        static void Mixed(Builder b)
        {
            b.Route(PlatformKind.Airliner, 0, (32.4, 124.0), (36.2, 126.0), (37.45, 126.45));
            b.Route(PlatformKind.Airliner, 20, (37.5, 127.0), (33.6, 130.4), (32.5, 132.8));
            b.Route(PlatformKind.Airliner, 40, (40.8, 123.0), (35.5, 133.0));
            b.Loop(PlatformKind.Fighter, 10, 2, (37.0, 124.6), (35.5, 124.8), (35.6, 125.6), (37.0, 125.5));
            b.Loop(PlatformKind.Fighter, 30, 2, (38.0, 129.8), (36.5, 130.2), (36.6, 131.0), (38.1, 130.6));
            b.Route(PlatformKind.Helicopter, 0, (36.0, 126.0), (36.9, 126.1), (37.3, 126.5));
            b.Route(PlatformKind.Helicopter, 15, (34.5, 126.3), (34.6, 127.5), (34.8, 128.5));
            b.Route(PlatformKind.Drone, 0, (38.5, 128.6), (37.7, 128.9), (37.0, 129.4));
            b.Route(PlatformKind.Drone, 25, (35.5, 126.4), (35.9, 127.2));
            b.Route(PlatformKind.CruiseMissile, 60, (35.6, 124.2), (35.9, 126.5), (36.3, 127.6), (36.9, 128.6));
        }

        static void Cruise(Builder b, double t)
        {
            b.Route(PlatformKind.CruiseMissile, t + 0, (34.0, 124.0), (34.9, 126.4), (35.6, 127.3), (36.2, 128.0));
            b.Route(PlatformKind.CruiseMissile, t + 40, (37.2, 131.5), (37.1, 129.6), (36.6, 128.7), (36.0, 127.9));
            b.Route(PlatformKind.CruiseMissile, t + 80, (32.8, 128.5), (34.8, 128.3), (35.6, 128.6), (36.4, 129.0));
            b.Route(PlatformKind.CruiseMissile, t + 120, (33.0, 131.8), (34.5, 129.5), (35.5, 129.0), (36.2, 128.4));
        }

        static void Drones(Builder b, double t, int n)
        {
            for (int i = 0; i < n; i++)
            {
                double dl = (i - n / 2.0) * 0.12;
                b.Route(PlatformKind.Drone, t + i * 10, (33.9, 127.4 + dl), (34.6, 127.5 + dl * 0.6), (35.1, 127.6 + dl * 0.4));
            }
        }

        static void Helis(Builder b, double t)
        {
            b.Route(PlatformKind.Helicopter, t + 0, (36.0, 126.0), (36.9, 126.1), (37.3, 126.5));
            b.Route(PlatformKind.Helicopter, t + 15, (34.5, 126.3), (34.6, 127.5), (34.8, 128.5));
            b.Route(PlatformKind.Helicopter, t + 30, (35.3, 129.6), (36.3, 129.5), (37.2, 129.2));
            b.Route(PlatformKind.Helicopter, t + 45, (33.3, 126.1), (33.6, 126.8), (34.3, 127.3));
        }

        static void BallisticSet(Builder b, double t)
        {
            b.Ballistic(t + 0, (35.0, 123.5), (38.5, 131.5));
            b.Ballistic(t + 60, (40.3, 132.4), (33.5, 124.5));
            b.Ballistic(t + 120, (32.4, 126.0), (39.5, 131.0));
        }

        /// <summary>One user-placed target (map clicks).</summary>
        public static Target Single(PlatformKind kind, Vec3d from, Vec3d to, double t, int id)
        {
            if (kind == PlatformKind.BallisticMissile) return Target.Ballistic(id, from, to, t);
            return Target.Waypoints(id, kind, new List<Vec3d> { from, to }, t);
        }
    }
}
