using System;

namespace RadarNet.Core
{
    /// <summary>Double precision 3D vector in the simulation frame: x = east, y = north, z = up (metres, z = altitude MSL).</summary>
    [Serializable]
    public struct Vec3d
    {
        public double x, y, z;
        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static readonly Vec3d zero = new Vec3d(0, 0, 0);
        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.x / s, a.y / s, a.z / s);
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public double Length2D => Math.Sqrt(x * x + y * y);
        public double Dot(Vec3d b) => x * b.x + y * b.y + z * b.z;
        public Vec3d Normalized { get { double l = Length; return l > 1e-12 ? this / l : zero; } }
        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + (b - a) * t;
        public override string ToString() => $"({x / 1000:0.0}, {y / 1000:0.0} km, {z:0} m)";
    }

    /// <summary>Earth constants and the azimuthal equidistant projection used for the 1000 x 1000 km map.</summary>
    public static class Geo
    {
        public const double R = 6371008.8;              // mean earth radius
        public const double Ke = R * 4.0 / 3.0;          // effective radius for radar propagation (4/3 earth)
        public const double G = 9.80665;
        public const double Deg = Math.PI / 180.0;

        // Projection centre (set from the heightmap header)
        public static double lat0 = 36.5, lon0 = 127.7;

        /// <summary>Lat/lon (deg) → map x/y (m) with the azimuthal equidistant projection.</summary>
        public static void Forward(double lat, double lon, out double x, out double y)
        {
            double p = lat * Deg, l = lon * Deg, p0 = lat0 * Deg, l0 = lon0 * Deg;
            double cosc = Math.Sin(p0) * Math.Sin(p) + Math.Cos(p0) * Math.Cos(p) * Math.Cos(l - l0);
            cosc = Math.Max(-1, Math.Min(1, cosc));
            double c = Math.Acos(cosc);
            double k = c < 1e-12 ? 1.0 : c / Math.Sin(c);
            x = R * k * Math.Cos(p) * Math.Sin(l - l0);
            y = R * k * (Math.Cos(p0) * Math.Sin(p) - Math.Sin(p0) * Math.Cos(p) * Math.Cos(l - l0));
        }

        /// <summary>Map x/y (m) → lat/lon (deg).</summary>
        public static void Inverse(double x, double y, out double lat, out double lon)
        {
            double p0 = lat0 * Deg, l0 = lon0 * Deg;
            double rho = Math.Sqrt(x * x + y * y);
            if (rho < 1e-6) { lat = lat0; lon = lon0; return; }
            double c = rho / R;
            double sc = Math.Sin(c), cc = Math.Cos(c);
            lat = Math.Asin(cc * Math.Sin(p0) + y * sc * Math.Cos(p0) / rho) / Deg;
            lon = (l0 + Math.Atan2(x * sc, rho * Math.Cos(p0) * cc - y * Math.Sin(p0) * sc)) / Deg;
        }

        /// <summary>Height drop of the (4/3) earth surface at ground distance d.</summary>
        public static double Drop(double d) => d * d / (2.0 * Ke);

        /// <summary>Azimuth (rad, clockwise from north, 0..2π) of the vector (dx, dy).</summary>
        public static double Azimuth(double dx, double dy)
        {
            double a = Math.Atan2(dx, dy);
            return a < 0 ? a + 2 * Math.PI : a;
        }

        /// <summary>Elevation angle (rad) of a point at altitude h, ground distance d, seen from altitude hr (4/3 earth).</summary>
        public static double Elevation(double hr, double h, double d) => Math.Atan2(h - Drop(d) - hr, Math.Max(d, 1.0));

        public static double Wrap2Pi(double a) { a %= 2 * Math.PI; return a < 0 ? a + 2 * Math.PI : a; }
        public static double WrapPi(double a) { a = Wrap2Pi(a); return a > Math.PI ? a - 2 * Math.PI : a; }

        public static string LatLon(double x, double y)
        {
            Inverse(x, y, out var la, out var lo);
            return $"{Math.Abs(la):0.00}°{(la >= 0 ? "N" : "S")} {Math.Abs(lo):0.00}°{(lo >= 0 ? "E" : "W")}";
        }
    }
}
