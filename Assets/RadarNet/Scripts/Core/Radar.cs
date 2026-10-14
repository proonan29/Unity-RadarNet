using System;
using System.Collections.Generic;

namespace RadarNet.Core
{
    public enum RadarKind { LongRange, MediumRange, GapFiller, Coastal, Naval, PhasedArray }

    /// <summary>Generic radar parameters (illustrative, not any specific system).</summary>
    public class RadarSpec
    {
        public RadarKind kind;
        public string code;
        public double r0;          // range (m) where a 1 m² target gives SNR 13 dB (Pd ≈ 0.5 at Pfa 1e-6)
        public double rMax, rMin;  // instrumented range (m)
        public double period;      // rotation period / revisit time (s)
        public double elMin, elMax;// beam elevation coverage (deg)
        public double altMax;      // altitude coverage (m)
        public double sigR, sigAz, sigEl;  // measurement noise (m, deg, deg)
        public double mast;        // antenna height above ground/sea (m)
        public double sector = 360;// azimuth coverage (deg); < 360 = fixed-face phased array
        public double faPerScan;   // mean false alarms per scan
        public double lowLossDb;   // extra loss against targets < 200 m above the surface (clutter, multipath)

        public static readonly Dictionary<RadarKind, RadarSpec> All = new Dictionary<RadarKind, RadarSpec>
        {
            { RadarKind.LongRange,   new RadarSpec { kind = RadarKind.LongRange,   code = "L", r0 = 320e3, rMax = 450e3, rMin = 2e3, period = 10, elMin = -2, elMax = 25, altMax = 30000, sigR = 100, sigAz = 0.18, sigEl = 0.35, mast = 15, faPerScan = 0.6, lowLossDb = 6 } },
            { RadarKind.MediumRange, new RadarSpec { kind = RadarKind.MediumRange, code = "M", r0 = 160e3, rMax = 230e3, rMin = 1e3, period = 5, elMin = -2, elMax = 35, altMax = 20000, sigR = 60, sigAz = 0.15, sigEl = 0.45, mast = 12, faPerScan = 0.4, lowLossDb = 4 } },
            { RadarKind.GapFiller,   new RadarSpec { kind = RadarKind.GapFiller,   code = "G", r0 = 55e3,  rMax = 90e3,  rMin = 0.3e3, period = 2, elMin = -3, elMax = 60, altMax = 8000, sigR = 20, sigAz = 0.25, sigEl = 0.4, mast = 20, faPerScan = 0.15, lowLossDb = 1 } },
            { RadarKind.Coastal,     new RadarSpec { kind = RadarKind.Coastal,     code = "C", r0 = 110e3, rMax = 160e3, rMin = 0.5e3, period = 3, elMin = -3, elMax = 20, altMax = 10000, sigR = 30, sigAz = 0.2, sigEl = 0.6, mast = 30, faPerScan = 0.25, lowLossDb = 1 } },
            { RadarKind.Naval,       new RadarSpec { kind = RadarKind.Naval,       code = "S", r0 = 200e3, rMax = 300e3, rMin = 0.5e3, period = 4, elMin = -3, elMax = 70, altMax = 30000, sigR = 40, sigAz = 0.15, sigEl = 0.3, mast = 30, faPerScan = 0.3, lowLossDb = 2 } },
            { RadarKind.PhasedArray, new RadarSpec { kind = RadarKind.PhasedArray, code = "E", r0 = 650e3, rMax = 1000e3, rMin = 5e3, period = 2, elMin = 0, elMax = 75, altMax = 1000e3, sigR = 50, sigAz = 0.1, sigEl = 0.1, mast = 10, sector = 120, faPerScan = 0.4, lowLossDb = 6 } },
        };
    }

    /// <summary>A detection report from one radar scan.</summary>
    public class Plot
    {
        public int radar;
        public double t;
        public Vec3d pos;              // converted measurement in the map frame
        public double[] R = new double[9];
        public double r, az, el, snr;
        public int truthId = -1;       // -1 = false alarm (evaluation only, trackers never use it)
        public bool cued;
    }

    public struct Geometry { public double r, az, el, d; }

    /// <summary>One radar site: geometry, terrain screening table, scan timing and the detection model.</summary>
    public class RadarSite
    {
        public int index;
        public string name;
        public RadarSpec spec;
        public double x, y, ground, hr;      // hr = antenna altitude MSL
        public double boresight;             // phased array face direction (rad)
        public double phase;                 // scan phase (0..1)
        public bool atSea;

        // terrain screening: max terrain elevation angle seen so far along each azimuth ray
        public int azBins, rBins;
        public double rStep;
        float[] maxEl;

        public const double Pfa = 1e-6, PfaCued = 1e-4;

        public RadarSite(int index, RadarKind kind, double x, double y, Heightmap hm, double phase)
        {
            this.index = index; spec = RadarSpec.All[kind]; this.x = x; this.y = y; this.phase = phase;
            double h = hm.Height(x, y);
            atSea = h <= 0.5;
            ground = Math.Max(0, h);
            hr = ground + spec.mast;
            name = spec.code + (index + 1).ToString("00");
        }

        public Vec3d Pos => new Vec3d(x, y, hr);
        public bool Rotating => spec.sector >= 359.9;

        public void BuildScreening(Heightmap hm)
        {
            rStep = 500;
            rBins = (int)Math.Ceiling(spec.rMax / rStep) + 1;
            azBins = spec.rMax > 500e3 ? 2048 : 1024;
            maxEl = new float[azBins * rBins];
            for (int a = 0; a < azBins; a++)
            {
                double az = (a + 0.5) * 2 * Math.PI / azBins;
                if (!Rotating && Math.Abs(Geo.WrapPi(az - boresight)) > (spec.sector / 2 + 2) * Geo.Deg) { for (int k = 0; k < rBins; k++) maxEl[a * rBins + k] = 1.5f; continue; }
                double sx = Math.Sin(az), sy = Math.Cos(az);
                double best = -Math.PI / 2;
                maxEl[a * rBins] = (float)best;
                for (int k = 1; k < rBins; k++)
                {
                    double d = k * rStep;
                    double px = x + sx * d, py = y + sy * d;
                    double s = hm.InBounds(px, py) ? hm.Surface(px, py) : 0;
                    double e = Math.Atan2(s - Geo.Drop(d) - hr, d);
                    if (e > best) best = e;
                    maxEl[a * rBins + k] = (float)best;   // max over terrain at ranges ≤ d
                }
            }
        }

        /// <summary>Terrain screening angle (rad) for a point at ground distance d along azimuth az (terrain strictly in front).</summary>
        public double MaskAngle(double az, double d)
        {
            int a = (int)(az / (2 * Math.PI) * azBins); if (a >= azBins) a = azBins - 1;
            int k = (int)(d / rStep) - 1; if (k < 0) return -Math.PI / 2; if (k >= rBins) k = rBins - 1;
            return maxEl[a * rBins + k];
        }

        /// <summary>Geometric visibility: range, sector, beam elevation, altitude coverage, earth curvature and terrain masking.</summary>
        public bool Visible(Vec3d p, out Geometry g)
        {
            g = default;
            double dx = p.x - x, dy = p.y - y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > spec.rMax || p.z > spec.altMax) return false;
            double hApp = p.z - Geo.Drop(d) - hr;
            double r = Math.Sqrt(d * d + hApp * hApp);
            if (r > spec.rMax || r < spec.rMin) return false;
            double el = Math.Atan2(hApp, Math.Max(d, 1));
            if (el < spec.elMin * Geo.Deg || el > spec.elMax * Geo.Deg) return false;
            double az = Geo.Azimuth(dx, dy);
            if (!Rotating && Math.Abs(Geo.WrapPi(az - boresight)) > spec.sector / 2 * Geo.Deg) return false;
            if (el <= MaskAngle(az, d)) return false;
            g.r = r; g.az = az; g.el = el; g.d = d;
            return true;
        }

        /// <summary>Signal to noise ratio (linear) from the radar equation normalised to r0 (SNR 20 = 13 dB for 1 m² at r0).</summary>
        public double Snr(double rcs, double r, bool lowTarget)
        {
            double snr = 20.0 * rcs * Math.Pow(spec.r0 / Math.Max(r, 1), 4);
            if (lowTarget) snr *= Math.Pow(10, -spec.lowLossDb / 10);
            return snr;
        }

        /// <summary>Swerling 1 detection probability for a single look.</summary>
        public static double Pd(double snr, double pfa) => Math.Pow(pfa, 1.0 / (1.0 + snr));

        public double BeamAz(double t) => Rotating ? Geo.Wrap2Pi(2 * Math.PI * (phase + t / spec.period)) : boresight;

        /// <summary>Does the beam illuminate azimuth az during [t, t+dt)? Returns the illumination time.</summary>
        public bool Illuminates(double az, double t, double dt, out double tHit)
        {
            if (Rotating)
            {
                double w = 2 * Math.PI / spec.period;
                double delta = Geo.Wrap2Pi(az - BeamAz(t));
                tHit = t + delta / w;
                return delta < w * dt;
            }
            // phased array: whole sector revisited every period
            double a = t / spec.period + phase, b = (t + dt) / spec.period + phase;
            tHit = t;
            return Math.Floor(b) > Math.Floor(a);
        }

        /// <summary>Noisy measurement of p and its covariance in the map frame.</summary>
        public Plot Measure(Vec3d p, Geometry g, double tHit, double snr, Random rng)
        {
            double r = g.r + Gauss(rng) * spec.sigR;
            double az = g.az + Gauss(rng) * spec.sigAz * Geo.Deg;
            double el = g.el + Gauss(rng) * spec.sigEl * Geo.Deg;
            return MakePlot(r, az, el, tHit, snr);
        }

        public Plot MakePlot(double r, double az, double el, double t, double snr)
        {
            double d = r * Math.Cos(el);
            double hApp = hr + r * Math.Sin(el);
            double h = hApp + Geo.Drop(d);
            var pl = new Plot { radar = index, t = t, r = r, az = az, el = el, snr = snr };
            pl.pos = new Vec3d(x + d * Math.Sin(az), y + d * Math.Cos(az), h);
            // R = σr² u_r u_rᵀ + (r cos el σaz)² u_az u_azᵀ + (r σel)² u_el u_elᵀ
            double sa = Math.Sin(az), ca = Math.Cos(az), se = Math.Sin(el), ce = Math.Cos(el);
            var ur = new[] { sa * ce, ca * ce, se };
            var ua = new[] { ca, -sa, 0.0 };
            var ue = new[] { -sa * se, -ca * se, ce };
            double vr = spec.sigR * spec.sigR;
            double va = Math.Pow(r * ce * spec.sigAz * Geo.Deg, 2);
            double ve = Math.Pow(r * spec.sigEl * Geo.Deg, 2);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    pl.R[i * 3 + j] = vr * ur[i] * ur[j] + va * ua[i] * ua[j] + ve * ue[i] * ue[j];
            return pl;
        }

        /// <summary>Random false alarm inside the swept sector of this step.</summary>
        public Plot FalseAlarm(double t, double dt, Random rng)
        {
            double az;
            if (Rotating) az = Geo.Wrap2Pi(BeamAz(t) + rng.NextDouble() * 2 * Math.PI * dt / spec.period);
            else az = Geo.Wrap2Pi(boresight + (rng.NextDouble() - 0.5) * spec.sector * Geo.Deg);
            double r = spec.rMin + Math.Sqrt(rng.NextDouble()) * (spec.rMax * 0.85 - spec.rMin);
            double el = (0.2 + rng.NextDouble() * 2.5) * Geo.Deg;
            return MakePlot(r, az, el, t, 25);
        }

        public static double Gauss(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>Probability of detection of a target with the given rcs at point p (single look, not cued). 0 if not visible.</summary>
        public double PdAt(Vec3d p, double rcs, Heightmap hm)
        {
            if (!Visible(p, out var g)) return 0;
            bool low = p.z - hm.Surface(p.x, p.y) < 200;
            return Pd(Snr(rcs, g.r, low), Pfa);
        }
    }
}
