using System.Collections.Generic;
using UnityEngine;

namespace RadarNet.World
{
    /// <summary>
    /// Immediate-mode builder for screen-width lines and billboard symbols, baked into one mesh per frame
    /// (camera-facing ribbons whose width is given in pixels, so the map reads the same at every zoom).
    /// </summary>
    public class LineBatch
    {
        readonly List<Vector3> v = new List<Vector3>(16384);
        readonly List<Color> c = new List<Color>(16384);
        readonly List<int> idx = new List<int>(32768);
        Camera cam;
        Vector3 camPos, camFwd, camRight, camUp;
        bool ortho;
        float tanHalf, orthoPx;
        bool linear;

        public int Segments => idx.Count / 6;

        public void Begin(Camera camera)
        {
            v.Clear(); c.Clear(); idx.Clear();
            cam = camera;
            var t = cam.transform;
            camPos = t.position; camFwd = t.forward; camRight = t.right; camUp = t.up;
            ortho = cam.orthographic;
            float h = Mathf.Max(1, cam.pixelHeight);
            tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f / h;
            orthoPx = cam.orthographicSize * 2f / h;
            linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        }

        public float WorldPerPixel(Vector3 p) => ortho ? orthoPx : Mathf.Max(0.01f, Vector3.Dot(p - camPos, camFwd)) * tanHalf;

        public bool Visible(Vector3 p)
        {
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0 && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
        }

        Color Lin(Color col) => linear ? new Color(Mathf.GammaToLinearSpace(col.r), Mathf.GammaToLinearSpace(col.g), Mathf.GammaToLinearSpace(col.b), col.a) : col;

        public void Line(Vector3 a, Vector3 b, Color col, float px) => Line(a, b, col, col, px);

        /// <summary>Optional clip rectangle in the XZ plane (map bounds); segments are cut at its edges.</summary>
        public Rect? clip;

        bool Clip(ref Vector3 a, ref Vector3 b, ref Color ca, ref Color cb)
        {
            if (!clip.HasValue) return true;
            var r = clip.Value;
            float t0 = 0, t1 = 1, dx = b.x - a.x, dz = b.z - a.z;
            bool Edge(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-9f) return q >= 0;
                float u = q / p;
                if (p < 0) { if (u > t1) return false; if (u > t0) t0 = u; }
                else { if (u < t0) return false; if (u < t1) t1 = u; }
                return true;
            }
            if (!Edge(-dx, a.x - r.xMin) || !Edge(dx, r.xMax - a.x) || !Edge(-dz, a.z - r.yMin) || !Edge(dz, r.yMax - a.z)) return false;
            if (t0 > 0 || t1 < 1)
            {
                Vector3 na = Vector3.Lerp(a, b, t0), nb = Vector3.Lerp(a, b, t1);
                Color nca = Color.Lerp(ca, cb, t0), ncb = Color.Lerp(ca, cb, t1);
                a = na; b = nb; ca = nca; cb = ncb;
            }
            return true;
        }

        public void Line(Vector3 a, Vector3 b, Color ca, Color cb, float px)
        {
            if (!Clip(ref a, ref b, ref ca, ref cb)) return;
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-10f) return;
            Vector3 view = ortho ? camFwd : ((a + b) * 0.5f - camPos);
            Vector3 side = Vector3.Cross(d, view);
            if (side.sqrMagnitude < 1e-12f) side = camRight;
            side.Normalize();
            float wa = WorldPerPixel(a) * px * 0.5f, wb = WorldPerPixel(b) * px * 0.5f;
            int i = v.Count;
            v.Add(a - side * wa); v.Add(a + side * wa); v.Add(b + side * wb); v.Add(b - side * wb);
            var la = Lin(ca); var lb = Lin(cb);
            c.Add(la); c.Add(la); c.Add(lb); c.Add(lb);
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
        }

        public void Polyline(IList<Vector3> pts, Color col, float px, bool closed = false)
        {
            for (int i = 0; i + 1 < pts.Count; i++) Line(pts[i], pts[i + 1], col, px);
            if (closed && pts.Count > 2) Line(pts[pts.Count - 1], pts[0], col, px);
        }

        public void Dashed(Vector3 a, Vector3 b, Color col, float px, float dashPx = 8)
        {
            float len = Vector3.Distance(a, b);
            float wpp = WorldPerPixel((a + b) * 0.5f);
            int n = Mathf.Clamp(Mathf.FloorToInt(len / (wpp * dashPx * 2)), 1, 200);
            for (int i = 0; i < n; i++) Line(Vector3.Lerp(a, b, (2f * i) / (2 * n)), Vector3.Lerp(a, b, (2f * i + 1) / (2 * n)), col, px);
        }

        /// <summary>Horizontal circle (world radius) — range rings, uncertainty.</summary>
        public void Circle(Vector3 center, float radius, Color col, float px, int segs = 96, bool dashed = false)
        {
            Vector3 prev = center + new Vector3(radius, 0, 0);
            for (int i = 1; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2 / segs;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                if (!dashed || (i & 1) == 0) Line(prev, p, col, px);
                prev = p;
            }
        }

        /// <summary>Horizontal arc between two azimuths (deg, clockwise from north).</summary>
        public void Arc(Vector3 center, float radius, float az0, float az1, Color col, float px, int segs = 48)
        {
            Vector3 P(float az) { float r = az * Mathf.Deg2Rad; return center + new Vector3(Mathf.Sin(r) * radius, 0, Mathf.Cos(r) * radius); }
            Vector3 prev = P(az0);
            for (int i = 1; i <= segs; i++) { var p = P(Mathf.Lerp(az0, az1, i / (float)segs)); Line(prev, p, col, px); prev = p; }
        }

        public enum Shape { Circle, Square, Diamond, Triangle, Hexagon, Cross, Plus }

        /// <summary>Screen-sized symbol facing the camera.</summary>
        public void Marker(Vector3 p, Shape shape, float sizePx, Color col, float px = 2)
        {
            float s = WorldPerPixel(p) * sizePx * 0.5f;
            Vector3 r = camRight * s, u = camUp * s;
            switch (shape)
            {
                case Shape.Square: Loop(p, col, px, -r - u, r - u, r + u, -r + u); break;
                case Shape.Diamond: Loop(p, col, px, -u, r, u, -r); break;
                case Shape.Triangle: Loop(p, col, px, u, r * 0.9f - u * 0.6f, -r * 0.9f - u * 0.6f); break;
                case Shape.Cross: Line(p - r - u, p + r + u, col, px); Line(p - r + u, p + r - u, col, px); break;
                case Shape.Plus: Line(p - r, p + r, col, px); Line(p - u, p + u, col, px); break;
                case Shape.Hexagon:
                    {
                        var pts = new Vector3[6];
                        for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; pts[i] = Mathf.Cos(a) * r + Mathf.Sin(a) * u; }
                        Loop(p, col, px, pts); break;
                    }
                default:
                    {
                        var pts = new Vector3[20];
                        for (int i = 0; i < 20; i++) { float a = i * Mathf.PI / 10; pts[i] = Mathf.Cos(a) * r + Mathf.Sin(a) * u; }
                        Loop(p, col, px, pts); break;
                    }
            }
        }

        /// <summary>Filled screen-sized dot.</summary>
        public void Dot(Vector3 p, float sizePx, Color col)
        {
            float s = WorldPerPixel(p) * sizePx * 0.5f;
            Vector3 r = camRight * s, u = camUp * s;
            int i = v.Count;
            v.Add(p); var lc = Lin(col); c.Add(lc);
            for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4; v.Add(p + Mathf.Cos(a) * r + Mathf.Sin(a) * u); c.Add(lc); }
            for (int k = 0; k < 8; k++) { idx.Add(i); idx.Add(i + 1 + k); idx.Add(i + 1 + (k + 1) % 8); }
        }

        void Loop(Vector3 p, Color col, float px, params Vector3[] offs)
        {
            for (int i = 0; i < offs.Length; i++) Line(p + offs[i], p + offs[(i + 1) % offs.Length], col, px);
        }

        public void End(Mesh m)
        {
            m.Clear();
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(idx, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000);
        }
    }
}
