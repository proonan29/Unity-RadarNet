using System;
using System.Collections.Generic;
using System.Linq;
using RadarNet.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RadarNet.World
{
    public struct MapLabel
    {
        public Vector3 world;
        public string text;
        public Color color;
        public int kind;        // 0 radar, 1 track, 2 C2, 3 truth
        public int id;
        public bool selected;
    }

    /// <summary>
    /// Draws the simulation every frame: radar sites (rotating antennas, range rings, beams), the data link mesh with
    /// travelling message pulses, ground-truth objects (3D models + trails) and the control centre's system tracks
    /// (symbols, velocity leaders, holder lines, uncertainty). Works in the 3D view and on the 2D board.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(0)]
    public class SimView : MonoBehaviour
    {
        public Material litMaterial;
        public Material overlayMaterial;

        [NonSerialized] public Sim sim;
        [NonSerialized] public MapView map;
        [NonSerialized] public Camera cam;
        public bool board2D;
        public bool showRings = true, showLinks = true, showTruth = true, showBeams = true, showLabels = true, showTrails = true, showHolders = true;
        [NonSerialized] public int selectedStn, selectedRadar = -1;
        [NonSerialized] public Vector3? spawnStart, hover;
        [NonSerialized] public PlatformKind spawnKind;
        [NonSerialized] public readonly List<MapLabel> labels = new List<MapLabel>();
        [NonSerialized] public float speed = 1;

        readonly LineBatch lb = new LineBatch();
        Mesh overlayMesh;
        GameObject root, overlayGo;
        Material ovMat, lit;
        readonly Dictionary<int, MeshRenderer> targetGos = new Dictionary<int, MeshRenderer>();
        readonly List<(Transform baseT, Transform antenna)> radarGos = new List<(Transform, Transform)>();
        MaterialPropertyBlock mpb;

        struct Anim { public int a, b; public float start, dur; }
        readonly List<Anim> anims = new List<Anim>();
        long lastPulse;

        public static readonly Color LinkColor = Disp.Hex("#39c6ff"), PulseColor = Disp.Hex("#b8f6ff");

        public void Init(Sim s, MapView m)
        {
            sim = s; map = m;
            Clear();
            root = new GameObject("SimView (generated)") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            overlayMesh = new Mesh { name = "overlay", hideFlags = HideFlags.DontSave };
            overlayMesh.MarkDynamic();
            overlayGo = new GameObject("Overlay") { hideFlags = HideFlags.DontSave };
            overlayGo.transform.SetParent(root.transform, false);
            overlayGo.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var mr = overlayGo.AddComponent<MeshRenderer>();
            ovMat = new Material(overlayMaterial != null ? overlayMaterial : new Material(Shader.Find("RadarNet/Overlay"))) { hideFlags = HideFlags.DontSave };
            mr.sharedMaterial = ovMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            lit = new Material(litMaterial != null ? litMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"))) { hideFlags = HideFlags.DontSave };
            lit.SetFloat("_Smoothness", 0.35f);
            mpb = new MaterialPropertyBlock();
            foreach (var r in sim.radars)
            {
                var b = new GameObject(r.name) { hideFlags = HideFlags.DontSave };
                b.transform.SetParent(root.transform, false);
                var bm = b.AddComponent<MeshFilter>(); bm.sharedMesh = r.spec.kind == RadarKind.PhasedArray ? Shapes.PhasedArray() : Shapes.RadarBase(r.atSea);
                var br = b.AddComponent<MeshRenderer>(); br.sharedMaterial = lit; Tint(br, Color.Lerp(Disp.RadarColor(r.spec.kind), Color.white, 0.35f));
                Transform ant = null;
                if (r.Rotating)
                {
                    var a = new GameObject("antenna") { hideFlags = HideFlags.DontSave };
                    a.transform.SetParent(b.transform, false);
                    a.transform.localPosition = new Vector3(0, r.atSea ? 1.0f : 0.85f, 0);
                    a.AddComponent<MeshFilter>().sharedMesh = Shapes.Antenna();
                    var ar = a.AddComponent<MeshRenderer>(); ar.sharedMaterial = lit; Tint(ar, Color.white * 0.9f);
                    ant = a.transform;
                }
                radarGos.Add((b.transform, ant));
            }
            lastPulse = sim.net.pulseSeq;
        }

        public bool IsBuilt => root != null && sim != null;

        void Tint(Renderer r, Color c) { mpb.SetColor("_BaseColor", c); r.SetPropertyBlock(mpb); }

        public void Clear()
        {
            if (root != null) DestroyImmediate(root);
            if (overlayMesh != null) DestroyImmediate(overlayMesh);
            if (ovMat != null) DestroyImmediate(ovMat);
            if (lit != null) DestroyImmediate(lit);
            root = null; targetGos.Clear(); radarGos.Clear(); anims.Clear();
        }

        void OnDisable() => Clear();

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (sim != null && cam != null && root != null) Rebuild(cam);
        }

        // ------------------------------------------------------------------ positions

        public Vector3 RadarTop(RadarSite r) => board2D ? Disp.Flat(r.Pos, 1.0f) : new Vector3((float)(r.x / 1000), (float)(r.ground / 1000 * Disp.exag) + 1.2f, (float)(r.y / 1000));
        public Vector3 NodePos(int node) => node < sim.radars.Count ? RadarTop(sim.radars[node]) : C2Pos;
        public Vector3 C2Pos => board2D ? Disp.Flat(sim.c2Pos, 1.0f) : new Vector3((float)(sim.c2Pos.x / 1000), (float)(sim.c2Pos.z / 1000 * Disp.exag) + 1.5f, (float)(sim.c2Pos.y / 1000));
        public Vector3 TrackPos(Vec3d p) => board2D ? Disp.Flat(p, 1.2f) : Disp.Pos(p);

        // ------------------------------------------------------------------ draw

        public void Rebuild(Camera camera)
        {
            if (sim == null || root == null) return;
            cam = camera;
            ovMat.SetFloat("_ZTest", board2D ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            lb.Begin(cam);
            float half = (float)(sim.hm.half / 1000);
            lb.clip = new Rect(-half, -half, 2 * half, 2 * half);
            labels.Clear();
            double t = sim.t;
            float ringY = board2D ? 0.8f : 0.4f;

            // radars
            for (int i = 0; i < sim.radars.Count; i++)
            {
                var r = sim.radars[i];
                var col = Disp.RadarColor(r.spec.kind);
                bool sel = i == selectedRadar;
                var top = RadarTop(r);
                var ground = board2D ? Disp.Flat(r.Pos, ringY) : new Vector3(top.x, (float)(r.ground / 1000 * Disp.exag), top.z);
                var (bt, ant) = radarGos[i];
                bt.gameObject.SetActive(!board2D);
                float wpp = lb.WorldPerPixel(ground);
                if (!board2D)
                {
                    float s = Mathf.Clamp(wpp * (r.Rotating ? 26 : 16), 0.6f, 25f);
                    bt.localPosition = ground; bt.localScale = Vector3.one * s;
                    bt.localRotation = r.Rotating ? Quaternion.identity : Quaternion.Euler(0, (float)(r.boresight / Geo.Deg), 0);
                    if (ant != null) ant.localRotation = Quaternion.Euler(0, (float)(r.BeamAz(t) / Geo.Deg), 0);
                    top = ground + Vector3.up * s * 1.0f;
                }
                else lb.Marker(ground, r.atSea ? LineBatch.Shape.Diamond : r.Rotating ? LineBatch.Shape.Square : LineBatch.Shape.Triangle, sel ? 16 : 11, col, 2);
                var rc = new Vector3(ground.x, ringY, ground.z);
                float rmax = (float)(r.spec.rMax / 1000);
                if (showRings || sel)
                {
                    var ringCol = col; ringCol.a = sel ? 0.85f : 0.11f;
                    if (r.Rotating) lb.Circle(rc, rmax, ringCol, sel ? 2.2f : 1.2f, 128);
                    else
                    {
                        float bs = (float)(r.boresight / Geo.Deg), hs = (float)(r.spec.sector / 2);
                        lb.Arc(rc, rmax, bs - hs, bs + hs, ringCol, sel ? 2.2f : 1.2f, 64);
                        lb.Line(rc, rc + Dir(bs - hs) * rmax, ringCol, 1f); lb.Line(rc, rc + Dir(bs + hs) * rmax, ringCol, 1f);
                    }
                }
                if (showBeams && r.Rotating)
                {
                    float az = (float)(r.BeamAz(t) / Geo.Deg);
                    float trail = Mathf.Clamp(speed * 360f / (float)r.spec.period * 0.02f, 3f, 24f);   // fade length in degrees
                    for (int k = 0; k < 6; k++)
                    {
                        var bc = col; bc.a = (sel ? 0.75f : 0.3f) * (1 - k / 6f);
                        var cEnd = bc; cEnd.a = 0;
                        lb.Line(rc, rc + Dir(az - trail * k / 6f) * rmax, bc, cEnd, k == 0 ? 2f : 1.2f);
                    }
                }
                if (showLabels) labels.Add(new MapLabel { world = top + Vector3.up * 0.3f, text = r.name, color = col, kind = 0, id = i, selected = sel });
            }
            // control centre
            lb.Marker(C2Pos, LineBatch.Shape.Hexagon, 16, Color.white, 2.2f);
            lb.Marker(C2Pos, LineBatch.Shape.Hexagon, 8, Color.white, 2f);
            if (showLabels) labels.Add(new MapLabel { world = C2Pos + Vector3.up * 0.5f, text = "C2", color = Color.white, kind = 2, id = -1 });

            // data link mesh + message pulses
            if (showLinks)
            {
                var lc = LinkColor; lc.a = 0.22f;
                foreach (var (a, b) in sim.net.edges) lb.Line(NodePos(a), NodePos(b), lc, 1.3f);
                CollectPulses();
                float now = Time.realtimeSinceStartup;
                anims.RemoveAll(an => now > an.start + an.dur);
                foreach (var an in anims)
                {
                    float f = (now - an.start) / an.dur;
                    if (f < 0) continue;
                    var pa = NodePos(an.a); var pb = NodePos(an.b);
                    var p = Vector3.Lerp(pa, pb, f);
                    var q = Vector3.Lerp(pa, pb, Mathf.Max(0, f - 0.12f));
                    var pc = PulseColor; var pc0 = PulseColor; pc0.a = 0;
                    lb.Line(q, p, pc0, pc, 3f);
                    lb.Dot(p, 5f, pc);
                }
            }

            // ground truth
            var alive = new HashSet<int>();
            foreach (var tg in sim.targets)
            {
                if (!tg.started || !tg.alive) continue;
                alive.Add(tg.id);
                var col = Disp.KindColor(tg.kind);
                var p = TrackPos(tg.pos);
                if (!targetGos.TryGetValue(tg.id, out var mr))
                {
                    var go = new GameObject(tg.Name) { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = Shapes.For(tg.kind);
                    mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = lit; mr.shadowCastingMode = ShadowCastingMode.Off;
                    Tint(mr, Color.Lerp(col, Color.white, 0.25f));
                    targetGos[tg.id] = mr;
                }
                bool show3d = showTruth && !board2D;
                mr.gameObject.SetActive(show3d);
                if (show3d)
                {
                    var v = new Vector3((float)tg.vel.x, (float)(tg.vel.z * Disp.exag), (float)tg.vel.y);
                    mr.transform.localPosition = p;
                    if (v.sqrMagnitude > 1e-4f) mr.transform.localRotation = Quaternion.LookRotation(v.normalized);
                    mr.transform.localScale = Vector3.one * Mathf.Clamp(lb.WorldPerPixel(p) * 30, 0.4f, 40f);
                    var gnd = new Vector3(p.x, map.SurfaceY(tg.pos.x, tg.pos.y), p.z);
                    lb.Line(gnd, p, new Color(1, 1, 1, 0.18f), 1f);
                }
                if (showTruth)
                {
                    if (board2D) lb.Marker(p, LineBatch.Shape.Plus, 9, new Color(1, 1, 1, 0.8f), 1.5f);
                    if (showTrails && tg.trail.Count > 1)
                    {
                        var pts = tg.trail.Select(TrackPos).ToList(); pts.Add(p);
                        lb.Polyline(pts, new Color(1, 1, 1, 0.22f), 1.2f);
                    }
                }
            }
            foreach (var id in targetGos.Keys.Where(k => !alive.Contains(k)).ToList()) { DestroyImmediate(targetGos[id].gameObject); targetGos.Remove(id); }

            // system tracks (control centre picture)
            foreach (var s in sim.c2.tracks.Values)
            {
                var col = Disp.ClassColor(s.cls);
                bool coast = s.state == SysState.Coasting;
                bool sel = s.stn == selectedStn;
                if (coast) col.a = 0.55f;
                var pos = s.PosAt(t);
                var p = TrackPos(pos);
                var shape = s.cls == TrackClass.CruiseMissile || s.cls == TrackClass.Ballistic ? LineBatch.Shape.Diamond : LineBatch.Shape.Circle;
                lb.Marker(p, shape, sel ? 18 : 14, col, sel ? 3f : 2.2f);
                if (sel) lb.Marker(p, LineBatch.Shape.Square, 30, new Color(1, 1, 1, 0.9f), 1.5f);
                var lead = s.Vel * 60.0;
                var p2 = TrackPos(pos + lead);
                if (board2D) p2.y = p.y;
                lb.Line(p, p2, col, 2f);
                if (coast || sel)
                {
                    float sig = (float)(s.Sigma(t) * 2 / 1000);
                    if (sig > 0.3f) { var uc = col; uc.a = 0.5f; lb.Circle(p, Mathf.Min(sig, 150f), uc, 1.3f, 48, coast); }
                }
                if (showTrails && s.trail.Count > 1)
                {
                    var tc = col; tc.a = 0.35f;
                    var pts = s.trail.Select(TrackPos).ToList(); pts.Add(p);
                    lb.Polyline(pts, tc, sel ? 2f : 1.4f);
                }
                if ((showHolders || sel) && !coast)
                    foreach (int h in sim.c2.Holders(s, t))
                    {
                        if (h >= sim.radars.Count) continue;
                        var hc = Disp.RadarColor(sim.radars[h].spec.kind);
                        bool r2 = h == s.r2;
                        hc.a = r2 ? 0.85f : 0.35f;
                        if (r2) lb.Line(RadarTop(sim.radars[h]), p, hc, sel ? 2.6f : 1.8f);
                        else lb.Dashed(RadarTop(sim.radars[h]), p, hc, 1.2f, 6);
                    }
                if (showLabels || sel)
                {
                    double spd = s.Vel.Length * 3.6;
                    labels.Add(new MapLabel
                    {
                        world = p, kind = 1, id = s.stn, selected = sel, color = col,
                        text = $"{s.stn:00000}{(coast ? " ~" : "")}\n{ClassShort(s.cls)} {pos.z / 1000:0.0}km {spd:0}km/h",
                    });
                }
            }

            // spawn preview
            if (spawnStart.HasValue)
            {
                var a = spawnStart.Value;
                lb.Marker(a, LineBatch.Shape.Cross, 14, Color.white, 2);
                if (hover.HasValue) lb.Dashed(a, hover.Value, new Color(1, 1, 1, 0.9f), 2f, 8);
            }

            lb.End(overlayMesh);
        }

        static Vector3 Dir(float azDeg) { float r = azDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(r), 0, Mathf.Cos(r)); }

        public static string ClassShort(TrackClass c) => c switch
        {
            TrackClass.Drone => "UAV", TrackClass.Helicopter => "HELO", TrackClass.Jet => "JET", TrackClass.Airliner => "AIRLINER",
            TrackClass.CruiseMissile => "CM", TrackClass.Ballistic => "BM", _ => "UNK",
        };

        void CollectPulses()
        {
            float now = Time.realtimeSinceStartup;
            const float hopDur = 0.22f;
            if (!Application.isPlaying)
            {
                // edit mode / screenshots: show the messages in flight around the current sim time as static pulses
                anims.Clear();
                foreach (var p in sim.net.pulses)
                    if (sim.t - p.t1 < 1.2 && p.t0 <= sim.t + 0.5)
                        anims.Add(new Anim { a = p.a, b = p.b, start = now - hopDur * 0.6f, dur = hopDur });
                lastPulse = sim.net.pulseSeq;
                return;
            }
            if (sim.net.pulseSeq < lastPulse) lastPulse = 0;
            int added = 0;
            foreach (var p in sim.net.pulses)
            {
                if (p.seq <= lastPulse) continue;
                if (added++ > 400) break;
                anims.Add(new Anim { a = p.a, b = p.b, start = now + (p.hop - 1) * hopDur, dur = hopDur });
            }
            lastPulse = sim.net.pulseSeq;
            if (anims.Count > 900) anims.RemoveRange(0, anims.Count - 900);
        }

        /// <summary>Screen-space pick of a track or radar near a screen point (pixels).</summary>
        public (int stn, int radar) Pick(Vector2 screen, float maxPx = 18)
        {
            int bestStn = 0, bestRadar = -1; float bd = maxPx;
            foreach (var s in sim.c2.tracks.Values)
            {
                var sp = cam.WorldToScreenPoint(TrackPos(s.PosAt(sim.t)));
                if (sp.z <= 0) continue;
                float d = Vector2.Distance(screen, sp);
                if (d < bd) { bd = d; bestStn = s.stn; }
            }
            if (bestStn != 0) return (bestStn, -1);
            bd = maxPx;
            for (int i = 0; i < sim.radars.Count; i++)
            {
                var sp = cam.WorldToScreenPoint(RadarTop(sim.radars[i]));
                if (sp.z <= 0) continue;
                float d = Vector2.Distance(screen, sp);
                if (d < bd) { bd = d; bestRadar = i; }
            }
            return (0, bestRadar);
        }
    }
}
