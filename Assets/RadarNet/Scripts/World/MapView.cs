using System;
using System.Threading.Tasks;
using RadarNet.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RadarNet.World
{
    /// <summary>Display conventions: 1 unit = 1 km; heights exaggerated (altitudes above 15 km compressed).</summary>
    public static class Disp
    {
        public static float exag = 8f;
        public static float AltY(double alt)
        {
            double a = alt <= 15000 ? alt : 15000 + (alt - 15000) * 0.15;
            return (float)(a / 1000.0 * exag);
        }
        public static Vector3 Pos(Vec3d p) => new Vector3((float)(p.x / 1000), AltY(p.z), (float)(p.y / 1000));
        public static Vector3 Flat(Vec3d p, float y = 0.6f) => new Vector3((float)(p.x / 1000), y, (float)(p.y / 1000));
        public static Vec3d Map(Vector3 w) => new Vec3d(w.x * 1000.0, w.z * 1000.0, 0);

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static Color RadarColor(RadarKind k) => k switch
        {
            RadarKind.LongRange => Hex("#4da3ff"),
            RadarKind.MediumRange => Hex("#3ddbd9"),
            RadarKind.GapFiller => Hex("#8be05a"),
            RadarKind.Coastal => Hex("#f0c75e"),
            RadarKind.Naval => Hex("#b48cff"),
            _ => Hex("#ff7eb6"),
        };

        public static Color ClassColor(TrackClass c) => c switch
        {
            TrackClass.Drone => Hex("#ffa23a"),
            TrackClass.Helicopter => Hex("#ffe14d"),
            TrackClass.Jet => Hex("#ff5c5c"),
            TrackClass.Airliner => Hex("#a8dcff"),
            TrackClass.CruiseMissile => Hex("#ff5ce1"),
            TrackClass.Ballistic => Hex("#ff3d14"),
            _ => Hex("#d0d0d0"),
        };

        public static Color KindColor(PlatformKind k) => k switch
        {
            PlatformKind.Drone => ClassColor(TrackClass.Drone),
            PlatformKind.Helicopter => ClassColor(TrackClass.Helicopter),
            PlatformKind.Fighter => ClassColor(TrackClass.Jet),
            PlatformKind.Airliner => ClassColor(TrackClass.Airliner),
            PlatformKind.CruiseMissile => ClassColor(TrackClass.CruiseMissile),
            _ => ClassColor(TrackClass.Ballistic),
        };
    }

    /// <summary>Terrain mesh (chunked, exaggerated relief) with relief / tactical board textures and the coverage overlay.</summary>
    [ExecuteAlways]
    public class MapView : MonoBehaviour
    {
        public Material terrainMaterial;
        [NonSerialized] public Heightmap hm;
        [NonSerialized] public Texture2D relief, board, coverage;
        [NonSerialized] public string coverageInfo = "";
        GameObject root;
        Material mat;
        float builtExag = -1;

        public void Build(Heightmap heightmap)
        {
            hm = heightmap;
            if (root != null && Mathf.Approximately(builtExag, Disp.exag) && relief != null) return;
            Clear();
            builtExag = Disp.exag;
            root = new GameObject("Terrain (generated)") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            if (relief == null) BakeTextures(2048);
            mat = terrainMaterial != null ? new Material(terrainMaterial) : new Material(Shader.Find("RadarNet/Terrain"));
            mat.hideFlags = HideFlags.DontSave;
            mat.SetTexture("_ReliefTex", relief);
            mat.SetTexture("_BoardTex", board);
            mat.SetTexture("_CovTex", coverage != null ? coverage : Texture2D.blackTexture);
            mat.SetFloat("_CovAlpha", coverage != null && coverageInfo != "" ? 1 : 0);
            mat.SetFloat("_MapSize", (float)(hm.extent / 1000));

            int step = 2, chunk = 128;
            int cells = (hm.n - 1) / step;               // 1024
            int nChunks = cells / chunk;                  // 8
            for (int cy = 0; cy < nChunks; cy++)
                for (int cx = 0; cx < nChunks; cx++)
                {
                    var mesh = new Mesh { name = $"terrain_{cx}_{cy}", hideFlags = HideFlags.DontSave };
                    int vn = chunk + 1;
                    var verts = new Vector3[vn * vn];
                    for (int j = 0; j < vn; j++)
                        for (int i = 0; i < vn; i++)
                        {
                            int gi = (cx * chunk + i) * step, gj = (cy * chunk + j) * step;
                            double h = Math.Max(0, (double)hm[gi, gj]);
                            verts[j * vn + i] = new Vector3((float)(hm.X(gi) / 1000), (float)(h / 1000 * Disp.exag), (float)(hm.Y(gj) / 1000));
                        }
                    var tris = new int[chunk * chunk * 6];
                    int k = 0;
                    for (int j = 0; j < chunk; j++)
                        for (int i = 0; i < chunk; i++)
                        {
                            int a = j * vn + i;
                            tris[k++] = a; tris[k++] = a + vn; tris[k++] = a + 1;
                            tris[k++] = a + 1; tris[k++] = a + vn; tris[k++] = a + vn + 1;
                        }
                    mesh.vertices = verts; mesh.triangles = tris;
                    mesh.RecalculateBounds();
                    var go = new GameObject(mesh.name) { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                }
        }

        public bool IsBuilt => root != null && hm != null;

        public void SetBoard(float mix) { if (mat != null) mat.SetFloat("_BoardMix", mix); }
        public void SetGrid(float a) { if (mat != null) mat.SetFloat("_GridAlpha", a); }

        public float SurfaceY(double x, double y) => (float)(hm.Surface(x, y) / 1000 * Disp.exag);

        void BakeTextures(int size)
        {
            var rp = new Color32[size * size];
            var bp = new Color32[size * size];
            double ext = hm.extent, half = hm.half, px = ext / size;
            Color[] landStops = { Disp.Hex("#5d8c4c"), Disp.Hex("#8ea860"), Disp.Hex("#b9a678"), Disp.Hex("#9a7e62"), Disp.Hex("#ebe7df") };
            double[] landH = { 0, 300, 800, 1500, 2300 };
            Color seaShallow = Disp.Hex("#86b9da"), seaDeep = Disp.Hex("#123766");
            Color bSea0 = Disp.Hex("#0d2135"), bSea1 = Disp.Hex("#081320"), bLand0 = Disp.Hex("#1d2a27"), bLand1 = Disp.Hex("#3a4038"), bCoast = Disp.Hex("#3fb0d6");
            Parallel.For(0, size, j =>
            {
                for (int i = 0; i < size; i++)
                {
                    double x = -half + px * (i + 0.5), y = -half + px * (j + 0.5);
                    double h = hm.Height(x, y);
                    double hx = hm.Height(x + px, y) - hm.Height(x - px, y);
                    double hy = hm.Height(x, y + px) - hm.Height(x, y - px);
                    bool sea = h <= 0.5;
                    double ex = sea ? 1.0 : 3.0;
                    // light from the north-west, 45° altitude
                    var n = new Vector3((float)(-hx * ex / (2 * px)), 1f, (float)(-hy * ex / (2 * px))).normalized;
                    float shade = Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.5f, 0.707f, 0.5f).normalized));
                    Color c;
                    if (sea)
                    {
                        float d = Mathf.Clamp01((float)(-h / 2500.0));
                        c = Color.Lerp(seaShallow, seaDeep, Mathf.Sqrt(d));
                        c *= 0.85f + 0.3f * shade;
                    }
                    else
                    {
                        int k = 0; while (k < landH.Length - 2 && h > landH[k + 1]) k++;
                        float f = Mathf.Clamp01((float)((h - landH[k]) / (landH[k + 1] - landH[k])));
                        c = Color.Lerp(landStops[k], landStops[k + 1], f);
                        c *= 0.45f + 0.85f * shade;
                    }
                    c.a = 1;
                    rp[j * size + i] = c;

                    // tactical board: dark sea, dark land with faint relief, bright coastline
                    bool coast = false;
                    if (!sea) coast = hm.Height(x + px, y) <= 0.5 || hm.Height(x - px, y) <= 0.5 || hm.Height(x, y + px) <= 0.5 || hm.Height(x, y - px) <= 0.5;
                    Color b = sea ? Color.Lerp(bSea0, bSea1, Mathf.Clamp01((float)(-h / 2000))) : Color.Lerp(bLand0, bLand1, Mathf.Clamp01((float)(h / 1800))) * (0.75f + 0.5f * shade);
                    if (coast) b = bCoast;
                    b.a = 1;
                    bp[j * size + i] = b;
                }
            });
            relief = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Relief", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            relief.SetPixels32(rp); relief.Apply(true, false);
            board = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Board", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            board.SetPixels32(bp); board.Apply(true, false);
        }

        /// <summary>Coverage overlay for a target of given RCS at a height above the surface: red = none, yellow = 1 radar, green = 2+.</summary>
        public void ComputeCoverage(Sim sim, double alt, double rcs, int size = 400)
        {
            if (alt <= 0) { coverageInfo = ""; if (mat != null) mat.SetFloat("_CovAlpha", 0); return; }
            var px = new Color32[size * size];
            int land = 0, sea = 0, l1 = 0, l2 = 0, s1 = 0, s2 = 0;
            var counts = new int[size * size];
            Parallel.For(0, size, j =>
            {
                for (int i = 0; i < size; i++)
                {
                    double x = -hm.half + hm.extent * (i + 0.5) / size, y = -hm.half + hm.extent * (j + 0.5) / size;
                    var p = new Vec3d(x, y, hm.Surface(x, y) + alt);
                    int c = 0;
                    foreach (var r in sim.radars) if (r.PdAt(p, rcs, hm) >= 0.5) c++;
                    counts[j * size + i] = c;
                }
            });
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    double x = -hm.half + hm.extent * (i + 0.5) / size, y = -hm.half + hm.extent * (j + 0.5) / size;
                    int c = counts[j * size + i];
                    if (hm.IsSea(x, y)) { sea++; if (c >= 1) s1++; if (c >= 2) s2++; } else { land++; if (c >= 1) l1++; if (c >= 2) l2++; }
                    px[j * size + i] = c == 0 ? new Color(0.95f, 0.18f, 0.15f, 0.34f) : c == 1 ? new Color(1f, 0.82f, 0.2f, 0.22f) : new Color(0.2f, 0.9f, 0.35f, 0.17f);
                }
            if (coverage == null) coverage = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Coverage", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            coverage.SetPixels32(px); coverage.Apply();
            if (mat != null) { mat.SetTexture("_CovTex", coverage); mat.SetFloat("_CovAlpha", 1); }
            coverageInfo = $"{100.0 * l1 / Math.Max(1, land):0}|{100.0 * l2 / Math.Max(1, land):0}|{100.0 * s1 / Math.Max(1, sea):0}|{100.0 * s2 / Math.Max(1, sea):0}";
        }

        public void Clear()
        {
            if (root != null) { foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) DestroyImmediate(mf.sharedMesh); DestroyImmediate(root); }
            root = null;
            if (mat != null) DestroyImmediate(mat);
            builtExag = -1;
        }

        void OnDisable() => Clear();
    }
}
