using System;

namespace RadarNet.Core
{
    /// <summary>
    /// Constant-velocity Kalman filter on a 6-state [px py pz vx vy vz] with continuous white-noise acceleration.
    /// Optional gravity term for ballistic tracks. Covariance stored row-major in double[36].
    /// </summary>
    public static class KF
    {
        public static void Predict(double[] x, double[] P, double dt, double q, bool gravity = false)
        {
            if (dt <= 0) return;
            for (int i = 0; i < 3; i++) x[i] += x[i + 3] * dt;
            if (gravity) { x[2] -= 0.5 * Geo.G * dt * dt; x[5] -= Geo.G * dt; }
            // P = F P Fᵀ + Q with F = [I dt·I; 0 I]
            var n = new double[36];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    double pp = P[r * 6 + c], pv = P[r * 6 + c + 3], vp = P[(r + 3) * 6 + c], vv = P[(r + 3) * 6 + c + 3];
                    n[r * 6 + c] = pp + dt * (pv + vp) + dt * dt * vv;
                    n[r * 6 + c + 3] = pv + dt * vv;
                    n[(r + 3) * 6 + c] = vp + dt * vv;
                    n[(r + 3) * 6 + c + 3] = vv;
                }
            double q11 = q * dt * dt * dt / 3, q12 = q * dt * dt / 2, q22 = q * dt;
            for (int i = 0; i < 3; i++)
            {
                n[i * 6 + i] += q11; n[i * 6 + i + 3] += q12; n[(i + 3) * 6 + i] += q12; n[(i + 3) * 6 + i + 3] += q22;
            }
            Array.Copy(n, P, 36);
        }

        /// <summary>Innovation and its covariance S = Ppp + R for a position measurement.</summary>
        public static void Innovation(double[] x, double[] P, Vec3d z, double[] R, double[] nu, double[] S)
        {
            nu[0] = z.x - x[0]; nu[1] = z.y - x[1]; nu[2] = z.z - x[2];
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) S[r * 3 + c] = P[r * 6 + c] + R[r * 3 + c];
        }

        /// <summary>Mahalanobis distance² of a position measurement (gating).</summary>
        public static double Distance2(double[] x, double[] P, Vec3d z, double[] R)
        {
            var nu = new double[3]; var S = new double[9]; var Si = new double[9];
            Innovation(x, P, z, R, nu, S);
            if (!Inv3(S, Si, out _)) return double.MaxValue;
            return Quad3(nu, Si);
        }

        /// <summary>Kalman update with a position measurement. Returns NIS.</summary>
        public static double Update(double[] x, double[] P, Vec3d z, double[] R)
        {
            var nu = new double[3]; var S = new double[9]; var Si = new double[9];
            Innovation(x, P, z, R, nu, S);
            if (!Inv3(S, Si, out _)) return double.MaxValue;
            double nis = Quad3(nu, Si);
            // K = P Hᵀ S⁻¹ (6x3), PHt = P[:,0:3]
            var K = new double[18];
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 3; c++)
                {
                    double s = 0;
                    for (int k = 0; k < 3; k++) s += P[r * 6 + k] * Si[k * 3 + c];
                    K[r * 3 + c] = s;
                }
            for (int r = 0; r < 6; r++) x[r] += K[r * 3] * nu[0] + K[r * 3 + 1] * nu[1] + K[r * 3 + 2] * nu[2];
            // P = P - K H P  (H P = P[0:3,:])
            var n = new double[36];
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 6; c++)
                    n[r * 6 + c] = P[r * 6 + c] - (K[r * 3] * P[c] + K[r * 3 + 1] * P[6 + c] + K[r * 3 + 2] * P[12 + c]);
            for (int r = 0; r < 6; r++)
                for (int c = r; c < 6; c++) { double v = 0.5 * (n[r * 6 + c] + n[c * 6 + r]); P[r * 6 + c] = v; P[c * 6 + r] = v; }
            return nis;
        }

        public static double Quad3(double[] v, double[] M)
        {
            double s = 0;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) s += v[r] * M[r * 3 + c] * v[c];
            return s;
        }

        public static bool Inv3(double[] m, double[] inv, out double det)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-30) return false;
            double id = 1.0 / det;
            inv[0] = A * id; inv[1] = -(b * i - c * h) * id; inv[2] = (b * f - c * e) * id;
            inv[3] = B * id; inv[4] = (a * i - c * g) * id; inv[5] = -(a * f - c * d) * id;
            inv[6] = C * id; inv[7] = -(a * h - b * g) * id; inv[8] = (a * e - b * d) * id;
            return true;
        }

        /// <summary>General n×n inverse (Gauss-Jordan with partial pivoting).</summary>
        public static bool Inv(double[] m, double[] inv, int n)
        {
            var a = (double[])m.Clone();
            for (int i = 0; i < n * n; i++) inv[i] = 0;
            for (int i = 0; i < n; i++) inv[i * n + i] = 1;
            for (int col = 0; col < n; col++)
            {
                int piv = col; double best = Math.Abs(a[col * n + col]);
                for (int r = col + 1; r < n; r++) if (Math.Abs(a[r * n + col]) > best) { best = Math.Abs(a[r * n + col]); piv = r; }
                if (best < 1e-30) return false;
                if (piv != col)
                    for (int c = 0; c < n; c++)
                    {
                        (a[col * n + c], a[piv * n + c]) = (a[piv * n + c], a[col * n + c]);
                        (inv[col * n + c], inv[piv * n + c]) = (inv[piv * n + c], inv[col * n + c]);
                    }
                double d = 1.0 / a[col * n + col];
                for (int c = 0; c < n; c++) { a[col * n + c] *= d; inv[col * n + c] *= d; }
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r * n + col];
                    if (f == 0) continue;
                    for (int c = 0; c < n; c++) { a[r * n + c] -= f * a[col * n + c]; inv[r * n + c] -= f * inv[col * n + c]; }
                }
            }
            return true;
        }

        /// <summary>
        /// Covariance intersection of two estimates with unknown cross-correlation:
        /// P⁻¹ = ω P1⁻¹ + (1-ω) P2⁻¹, x = P (ω P1⁻¹ x1 + (1-ω) P2⁻¹ x2), ω chosen to minimise trace(P).
        /// </summary>
        public static void CovIntersect(double[] x1, double[] P1, double[] x2, double[] P2, double[] xo, double[] Po)
        {
            var I1 = new double[36]; var I2 = new double[36];
            if (!Inv(P1, I1, 6)) { Array.Copy(x2, xo, 6); Array.Copy(P2, Po, 36); return; }
            if (!Inv(P2, I2, 6)) { Array.Copy(x1, xo, 6); Array.Copy(P1, Po, 36); return; }
            double bestTr = double.MaxValue, bestW = 0.5;
            var M = new double[36]; var Mi = new double[36];
            for (int k = 1; k <= 19; k++)
            {
                double w = k / 20.0;
                for (int i = 0; i < 36; i++) M[i] = w * I1[i] + (1 - w) * I2[i];
                if (!Inv(M, Mi, 6)) continue;
                double tr = Mi[0] + Mi[7] + Mi[14];   // position trace
                if (tr < bestTr) { bestTr = tr; bestW = w; }
            }
            for (int i = 0; i < 36; i++) M[i] = bestW * I1[i] + (1 - bestW) * I2[i];
            Inv(M, Po, 6);
            var v = new double[6];
            for (int r = 0; r < 6; r++)
            {
                double s = 0;
                for (int c = 0; c < 6; c++) s += bestW * I1[r * 6 + c] * x1[c] + (1 - bestW) * I2[r * 6 + c] * x2[c];
                v[r] = s;
            }
            for (int r = 0; r < 6; r++) { double s = 0; for (int c = 0; c < 6; c++) s += Po[r * 6 + c] * v[c]; xo[r] = s; }
        }

        /// <summary>Position standard deviation (m, RMS of the three axes).</summary>
        public static double PosSigma(double[] P) => Math.Sqrt(Math.Max(0, (P[0] + P[7] + P[14]) / 3));

        /// <summary>Link-16 style track quality 0..15 from position uncertainty.</summary>
        public static int Quality(double[] P)
        {
            double s = PosSigma(P);
            double tq = 15 - 2 * Math.Log(Math.Max(s, 1) / 50.0, 2);
            return (int)Math.Max(0, Math.Min(15, Math.Round(tq)));
        }

        public static Vec3d Pos(double[] x) => new Vec3d(x[0], x[1], x[2]);
        public static Vec3d Vel(double[] x) => new Vec3d(x[3], x[4], x[5]);

        /// <summary>Position-only correlation distance² between two estimates at the same time (combined covariance).</summary>
        public static double Correlate(double[] x1, double[] P1, double[] x2, double[] P2, double extraVar = 0)
        {
            var S = new double[9]; var Si = new double[9]; var d = new double[3];
            for (int r = 0; r < 3; r++)
            {
                d[r] = x1[r] - x2[r];
                for (int c = 0; c < 3; c++) S[r * 3 + c] = P1[r * 6 + c] + P2[r * 6 + c] + (r == c ? extraVar : 0);
            }
            if (!Inv3(S, Si, out _)) return double.MaxValue;
            return Quad3(d, Si);
        }
    }
}
