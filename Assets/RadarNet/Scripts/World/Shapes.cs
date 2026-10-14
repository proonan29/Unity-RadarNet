using System.Collections.Generic;
using RadarNet.Core;
using UnityEngine;

namespace RadarNet.World
{
    /// <summary>Small procedural low-poly models (unit length, nose along +Z) for flying objects and radar sites.</summary>
    public static class Shapes
    {
        class B
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Box(Vector3 c, Vector3 s, Quaternion r)
            {
                Vector3 P(float x, float y, float z) => c + r * Vector3.Scale(new Vector3(x, y, z), s * 0.5f);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
                Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
                Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
                Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
                Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
            }
            public void Box(Vector3 c, Vector3 s) => Box(c, s, Quaternion.identity);
            public void Cyl(Vector3 a, Vector3 b, float r, int seg = 8, bool cone = false)
            {
                var axis = (b - a).normalized;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(axis, u);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                    Vector3 o0 = (Mathf.Cos(a0) * u + Mathf.Sin(a0) * w) * r, o1 = (Mathf.Cos(a1) * u + Mathf.Sin(a1) * w) * r;
                    if (cone) Tri(a + o0, b, a + o1);
                    else { Quad(a + o0, b + o0, b + o1, a + o1); Tri(b + o0, b, b + o1); }
                    Tri(a + o1, a, a + o0);
                }
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
        }

        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Mesh For(PlatformKind k)
        {
            string key = k.ToString();
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var b = new B();
            switch (k)
            {
                case PlatformKind.Airliner:
                    b.Cyl(new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.45f), 0.06f, 8);
                    b.Cyl(new Vector3(0, 0, 0.45f), new Vector3(0, 0, 0.55f), 0.06f, 8, true);
                    b.Box(new Vector3(0, -0.01f, 0.02f), new Vector3(1.0f, 0.02f, 0.16f), Quaternion.Euler(0, 0, 0));
                    b.Box(new Vector3(0, 0.0f, -0.45f), new Vector3(0.36f, 0.015f, 0.08f));
                    b.Box(new Vector3(0, 0.1f, -0.45f), new Vector3(0.015f, 0.18f, 0.1f));
                    break;
                case PlatformKind.Fighter:
                    b.Box(new Vector3(0, 0, 0), new Vector3(0.1f, 0.08f, 0.9f));
                    b.Cyl(new Vector3(0, 0, 0.45f), new Vector3(0, 0, 0.6f), 0.05f, 6, true);
                    b.Tri(new Vector3(0, 0, 0.25f), new Vector3(0.42f, 0, -0.3f), new Vector3(-0.42f, 0, -0.3f));
                    b.Tri(new Vector3(0, 0, 0.25f), new Vector3(-0.42f, 0, -0.3f), new Vector3(0.42f, 0, -0.3f));
                    b.Box(new Vector3(0, 0.1f, -0.36f), new Vector3(0.015f, 0.18f, 0.14f));
                    break;
                case PlatformKind.Helicopter:
                    b.Box(new Vector3(0, 0, 0.12f), new Vector3(0.22f, 0.22f, 0.4f));
                    b.Box(new Vector3(0, 0.03f, -0.28f), new Vector3(0.05f, 0.05f, 0.5f));
                    b.Box(new Vector3(0, 0.1f, -0.52f), new Vector3(0.015f, 0.16f, 0.08f));
                    b.Box(new Vector3(0, 0.15f, 0.12f), new Vector3(0.9f, 0.01f, 0.05f), Quaternion.Euler(0, 30, 0));
                    b.Box(new Vector3(0, 0.15f, 0.12f), new Vector3(0.9f, 0.01f, 0.05f), Quaternion.Euler(0, -60, 0));
                    break;
                case PlatformKind.Drone:
                    b.Box(Vector3.zero, new Vector3(0.18f, 0.08f, 0.18f));
                    b.Box(Vector3.zero, new Vector3(0.9f, 0.03f, 0.05f), Quaternion.Euler(0, 45, 0));
                    b.Box(Vector3.zero, new Vector3(0.9f, 0.03f, 0.05f), Quaternion.Euler(0, -45, 0));
                    foreach (var o in new[] { new Vector3(0.32f, 0.04f, 0.32f), new Vector3(-0.32f, 0.04f, 0.32f), new Vector3(0.32f, 0.04f, -0.32f), new Vector3(-0.32f, 0.04f, -0.32f) })
                        b.Cyl(o, o + Vector3.up * 0.01f, 0.13f, 10);
                    break;
                case PlatformKind.CruiseMissile:
                    b.Cyl(new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.4f), 0.045f, 8);
                    b.Cyl(new Vector3(0, 0, 0.4f), new Vector3(0, 0, 0.5f), 0.045f, 8, true);
                    b.Box(new Vector3(0, 0, 0.05f), new Vector3(0.55f, 0.01f, 0.07f));
                    b.Box(new Vector3(0, 0, -0.45f), new Vector3(0.2f, 0.01f, 0.06f));
                    b.Box(new Vector3(0, 0.06f, -0.45f), new Vector3(0.01f, 0.12f, 0.06f));
                    break;
                default:
                    b.Cyl(new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.3f), 0.07f, 10);
                    b.Cyl(new Vector3(0, 0, 0.3f), new Vector3(0, 0, 0.55f), 0.07f, 10, true);
                    for (int i = 0; i < 4; i++) b.Box(new Vector3(0, 0, -0.42f), new Vector3(0.28f, 0.01f, 0.12f), Quaternion.Euler(0, 0, 45 + 90 * i));
                    break;
            }
            m = b.Build("shape_" + key);
            cache[key] = m;
            return m;
        }

        public static Mesh RadarBase(bool ship)
        {
            string key = ship ? "ship" : "tower";
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var b = new B();
            if (ship)
            {
                b.Box(new Vector3(0, 0.12f, 0), new Vector3(0.35f, 0.24f, 1.4f));
                b.Cyl(new Vector3(0, 0.24f, 0.7f), new Vector3(0, 0.24f, 0.95f), 0.17f, 4, true);
                b.Box(new Vector3(0, 0.45f, -0.1f), new Vector3(0.25f, 0.4f, 0.4f));
                b.Cyl(new Vector3(0, 0.6f, -0.1f), new Vector3(0, 1.0f, -0.1f), 0.03f, 6);
            }
            else
            {
                b.Box(new Vector3(0, 0.04f, 0), new Vector3(0.7f, 0.08f, 0.7f));
                b.Cyl(new Vector3(0, 0.08f, 0), new Vector3(0, 0.75f, 0), 0.1f, 8);
                b.Box(new Vector3(0, 0.8f, 0), new Vector3(0.36f, 0.1f, 0.36f));
            }
            m = b.Build("radar_" + key);
            cache[key] = m;
            return m;
        }

        public static Mesh Antenna()
        {
            if (cache.TryGetValue("antenna", out var m) && m != null) return m;
            var b = new B();
            b.Box(new Vector3(0, 0.15f, 0.05f), new Vector3(0.9f, 0.3f, 0.04f), Quaternion.Euler(-12, 0, 0));
            b.Box(new Vector3(0, 0.0f, 0), new Vector3(0.06f, 0.12f, 0.06f));
            m = b.Build("antenna");
            cache["antenna"] = m;
            return m;
        }

        public static Mesh PhasedArray()
        {
            if (cache.TryGetValue("array", out var m) && m != null) return m;
            var b = new B();
            b.Box(new Vector3(0, 0.45f, 0), new Vector3(1.0f, 0.9f, 0.6f));
            b.Box(new Vector3(0, 0.75f, 0.3f), new Vector3(0.8f, 0.8f, 0.05f), Quaternion.Euler(-20, 0, 0));
            m = b.Build("array");
            cache["array"] = m;
            return m;
        }
    }
}
