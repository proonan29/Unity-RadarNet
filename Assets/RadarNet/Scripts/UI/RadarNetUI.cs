using System;
using System.Collections.Generic;
using System.Linq;
using RadarNet.Core;
using RadarNet.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace RadarNet.UI
{
    /// <summary>
    /// UI Toolkit interface built in code: left control panel (scenario, run, spawn, view, coverage, statistics, settings),
    /// right panel (system track list + details of the selected track or radar), event log and map labels.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(UIDocument))]
    public class RadarNetUI : MonoBehaviour
    {
        public AppController app;

        UIDocument doc;
        VisualElement root, left, right, bottom, labelLayer, trackList, detail;
        Label timeLabel, stateLabel, spawnHint, statsLabel, coverageLabel, eventsLabel, hintLabel, perfLabel;
        readonly List<Label> labelPool = new List<Label>();
        float nextRefresh;
        bool built;

        static readonly Color Panel = new Color(0.04f, 0.07f, 0.11f, 0.88f);
        static readonly Color Accent = new Color(0.25f, 0.75f, 1f);
        static readonly Color Dim = new Color(0.65f, 0.72f, 0.8f);

        void OnEnable()
        {
            doc = GetComponent<UIDocument>();
            Build();
            if (app != null && app.rig != null)
            {
                app.rig.isOverUI = IsOverUI;
                app.rig.onClick -= OnMapClick;
                app.rig.onClick += OnMapClick;
            }
        }

        void OnDisable() { if (app != null && app.rig != null) app.rig.onClick -= OnMapClick; }

        // ------------------------------------------------------------------ layout helpers

        static VisualElement Box(Color bg, float pad = 10)
        {
            var v = new VisualElement();
            v.style.backgroundColor = bg;
            v.style.paddingLeft = v.style.paddingRight = pad; v.style.paddingTop = v.style.paddingBottom = pad;
            v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = 8;
            return v;
        }

        static Label Text(string s, int size = 13, Color? c = null, bool bold = false)
        {
            var l = new Label(s);
            l.style.fontSize = size; l.style.color = c ?? new Color(0.9f, 0.94f, 0.98f);
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            return l;
        }

        static VisualElement Row(bool wrap = true)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            if (wrap) r.style.flexWrap = Wrap.Wrap;
            r.style.marginBottom = 4;
            return r;
        }

        static Button Btn(string s, Action a, bool on = false, float minW = 0)
        {
            var b = new Button(a) { text = s };
            b.style.fontSize = 12;
            b.style.marginLeft = 0; b.style.marginRight = 4; b.style.marginTop = 2; b.style.marginBottom = 2;
            b.style.paddingLeft = 7; b.style.paddingRight = 7; b.style.paddingTop = 3; b.style.paddingBottom = 3;
            b.style.backgroundColor = on ? new Color(0.13f, 0.45f, 0.7f) : new Color(0.14f, 0.19f, 0.26f);
            b.style.color = Color.white;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 5;
            if (minW > 0) b.style.minWidth = minW;
            return b;
        }

        static Label Section(VisualElement parent, string title)
        {
            var l = Text(title, 12, Accent, true);
            l.style.marginTop = 10; l.style.marginBottom = 4;
            l.style.borderBottomWidth = 1; l.style.borderBottomColor = new Color(0.25f, 0.75f, 1f, 0.25f);
            parent.Add(l);
            return l;
        }

        void Check(VisualElement parent, string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(Loc.T(label)) { value = get() };
            t.style.fontSize = 12; t.style.color = Color.white; t.style.marginBottom = 0; t.style.marginTop = 0;
            t.labelElement.style.minWidth = 150; t.labelElement.style.color = new Color(0.85f, 0.9f, 0.95f);
            t.RegisterValueChangedCallback(e => set(e.newValue));
            parent.Add(t);
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            if (doc == null) doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            if (root == null || app == null) return;
            root.Clear();
            labelPool.Clear();
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;

            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute; labelLayer.style.left = 0; labelLayer.style.top = 0; labelLayer.style.right = 0; labelLayer.style.bottom = 0;
            root.Add(labelLayer);

            // ---------------- left panel
            left = Box(Panel, 12);
            left.style.position = Position.Absolute; left.style.left = 10; left.style.top = 10; left.style.bottom = 10; left.style.width = 330;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            left.Add(scroll);
            root.Add(left);
            var lc = scroll.contentContainer;

            lc.Add(Text("RadarNet", 22, Color.white, true));
            lc.Add(Text(Loc.T("Radar data link tracking simulation"), 12, Dim));

            Section(lc, Loc.T("Scenario"));
            var sr = Row();
            foreach (ScenarioKind k in Enum.GetValues(typeof(ScenarioKind)))
            {
                var kk = k;
                sr.Add(Btn(Loc.Scenario(k), () => { app.LoadScenario(kk, app.seed); Build(); }, app.scenario == k));
            }
            lc.Add(sr);
            var sr2 = Row();
            sr2.Add(Btn(Loc.T("Restart"), () => { app.Restart(); }));
            sr2.Add(Btn(Loc.T("New seed") + $" ({app.seed})", () => { app.LoadScenario(app.scenario, app.seed + 1); Build(); }));
            lc.Add(sr2);

            Section(lc, Loc.T("Run"));
            var rr = Row();
            rr.Add(Btn(app.running ? "❚❚ " + Loc.T("Pause") : "▶ " + Loc.T("Start"), () => { app.running = !app.running; Build(); }, app.running, 90));
            foreach (var s in new[] { 1, 5, 20, 60, 200 })
            {
                int ss = s;
                rr.Add(Btn($"{s}x", () => { app.speed = ss; Build(); }, Mathf.Approximately(app.speed, s)));
            }
            lc.Add(rr);
            timeLabel = Text("", 15, Color.white, true); lc.Add(timeLabel);
            stateLabel = Text("", 11, Dim); lc.Add(stateLabel);

            Section(lc, Loc.T("Add object"));
            var ar = Row();
            foreach (PlatformKind k in Enum.GetValues(typeof(PlatformKind)))
            {
                var kk = k;
                var b = Btn(Loc.Kind(k), () => { app.spawnKind = kk; app.view.spawnStart = null; Build(); }, app.spawnKind == k);
                b.style.borderLeftWidth = 3; b.style.borderLeftColor = Disp.KindColor(k);
                ar.Add(b);
            }
            lc.Add(ar);
            spawnHint = Text("", 12, new Color(1f, 0.85f, 0.4f)); lc.Add(spawnHint);
            if (app.spawnKind.HasValue) lc.Add(Btn(Loc.T("Cancel"), () => { app.spawnKind = null; app.view.spawnStart = null; Build(); }));

            Section(lc, Loc.T("View"));
            var vr = Row();
            vr.Add(Btn(Loc.T("3D terrain"), () => { app.SetView2D(false); Build(); }, !app.view.board2D));
            vr.Add(Btn(Loc.T("2D board"), () => { app.SetView2D(true); Build(); }, app.view.board2D));
            lc.Add(vr);
            lc.Add(Text(Loc.T("Coverage (1 m², Pd ≥ 0.5)"), 12, Dim));
            var cr = Row();
            foreach (var (alt, name) in new[] { (0, Loc.T("Off")), (100, "100 m"), (1000, "1 km"), (5000, "5 km"), (10000, "10 km") })
            {
                int a = alt;
                cr.Add(Btn(name, () => { app.SetCoverage(a); Build(); }, app.coverageAlt == alt));
            }
            lc.Add(cr);
            coverageLabel = Text("", 11, Dim); lc.Add(coverageLabel);
            var v = app.view;
            Check(lc, "Radar rings", () => v.showRings, x => v.showRings = x);
            Check(lc, "Beams", () => v.showBeams, x => v.showBeams = x);
            Check(lc, "Data link", () => v.showLinks, x => v.showLinks = x);
            Check(lc, "Holder lines", () => v.showHolders, x => v.showHolders = x);
            Check(lc, "Ground truth", () => v.showTruth, x => v.showTruth = x);
            Check(lc, "Trails", () => v.showTrails, x => v.showTrails = x);
            Check(lc, "Labels", () => v.showLabels, x => v.showLabels = x);
            Check(lc, "Follow selected", () => app.followSelected, x => app.followSelected = x);

            Section(lc, Loc.T("Statistics"));
            statsLabel = Text("", 12); lc.Add(statsLabel);

            Section(lc, Loc.T("Legend"));
            var lg = Row();
            foreach (RadarKind k in Enum.GetValues(typeof(RadarKind))) lg.Add(Chip(RadarSpec.All[k].code + " " + Loc.Radar(k), Disp.RadarColor(k)));
            lc.Add(lg);
            var lg2 = Row();
            foreach (TrackClass k in new[] { TrackClass.Drone, TrackClass.Helicopter, TrackClass.Jet, TrackClass.Airliner, TrackClass.CruiseMissile, TrackClass.Ballistic })
                lg2.Add(Chip(Loc.Class(k), Disp.ClassColor(k)));
            lc.Add(lg2);
            var lg3 = Row(); lg3.Add(Chip(Loc.T("Data link"), SimView.LinkColor)); lg3.Add(Chip("C2", Color.white)); lc.Add(lg3);

            Section(lc, Loc.T("Settings"));
            var lr = Row();
            lr.Add(Btn("한국어", () => { Loc.Korean = true; Build(); }, Loc.Korean));
            lr.Add(Btn("English", () => { Loc.Korean = false; Build(); }, !Loc.Korean));
            lc.Add(lr);
            lc.Add(Text(Loc.T("Height exaggeration"), 12, Dim));
            var er = Row();
            foreach (var e in new[] { 1f, 4f, 8f, 16f })
            {
                float ee = e;
                er.Add(Btn($"×{e:0}", () => { app.SetExaggeration(ee); Build(); }, Mathf.Approximately(app.exaggeration, e)));
            }
            lc.Add(er);
            lc.Add(Text(Loc.T("Terrain: AWS Terrain Tiles (SRTM, GMTED, ETOPO1). Radar sites are fictional."), 10, Dim));
            perfLabel = Text("", 10, Dim); lc.Add(perfLabel);

            // ---------------- right panel
            right = Box(Panel, 10);
            right.style.position = Position.Absolute; right.style.right = 10; right.style.top = 10; right.style.bottom = 10; right.style.width = 350;
            root.Add(right);
            right.Add(Text(Loc.T("System tracks (C2)"), 14, Accent, true));
            var listScroll = new ScrollView(ScrollViewMode.Vertical);
            listScroll.style.height = new Length(42, LengthUnit.Percent);
            listScroll.style.flexShrink = 0;
            trackList = listScroll.contentContainer;
            right.Add(listScroll);
            var dScroll = new ScrollView(ScrollViewMode.Vertical);
            dScroll.style.flexGrow = 1; dScroll.style.marginTop = 8;
            dScroll.style.borderTopWidth = 1; dScroll.style.borderTopColor = new Color(0.25f, 0.75f, 1f, 0.25f);
            detail = dScroll.contentContainer;
            right.Add(dScroll);

            // ---------------- bottom: events + hint
            bottom = Box(new Color(0.04f, 0.07f, 0.11f, 0.78f), 8);
            bottom.style.position = Position.Absolute; bottom.style.left = 352; bottom.style.right = 372; bottom.style.bottom = 10;
            bottom.Add(Text(Loc.T("Events"), 12, Accent, true));
            eventsLabel = Text("", 12); bottom.Add(eventsLabel);
            root.Add(bottom);
            hintLabel = Text("", 11, Dim);
            hintLabel.style.position = Position.Absolute; hintLabel.style.left = 352; hintLabel.style.top = 12;
            hintLabel.pickingMode = PickingMode.Ignore;
            root.Add(hintLabel);

            built = true;
            nextRefresh = 0;
            Refresh();
        }

        static VisualElement Chip(string s, Color c)
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginRight = 8; r.style.marginBottom = 2;
            var sw = new VisualElement(); sw.style.width = 10; sw.style.height = 10; sw.style.backgroundColor = c; sw.style.marginRight = 4;
            sw.style.borderTopLeftRadius = sw.style.borderTopRightRadius = sw.style.borderBottomLeftRadius = sw.style.borderBottomRightRadius = 5;
            r.Add(sw); r.Add(Text(s, 11, new Color(0.85f, 0.9f, 0.95f)));
            return r;
        }

        // ------------------------------------------------------------------ dynamic content

        void Update()
        {
            if (!built || app == null || !app.Ready) return;
            if (app.spawnKind.HasValue && app.view.spawnStart.HasValue && Application.isPlaying)
                app.view.hover = app.rig.ScreenToMap(app.rig.mouse, (x, z) => app.map.SurfaceY(x * 1000, z * 1000));
            else app.view.hover = null;
            if (Time.realtimeSinceStartup >= nextRefresh) { nextRefresh = Time.realtimeSinceStartup + 0.4f; Refresh(); }
        }

        void LateUpdate()
        {
            if (!built || app == null || !app.Ready) return;
            UpdatePanelsForCamera();
            PlaceLabels();
        }

        static string Clock(double t) { var ts = TimeSpan.FromSeconds(t); return $"T+{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"; }

        public void Refresh()
        {
            if (!built || app == null || !app.Ready) return;
            var sim = app.sim;
            timeLabel.text = $"{Loc.T("Sim time")}  {Clock(sim.t)}   {app.speed:0}x";
            stateLabel.text = $"{Loc.Scenario(app.scenario)} · seed {app.seed} · {(app.running ? Loc.T("running") : Loc.T("paused"))}";
            spawnHint.text = app.spawnKind.HasValue ? $"{Loc.Kind(app.spawnKind.Value)}: {(app.view.spawnStart.HasValue ? Loc.T("Click the destination") : Loc.T("Click the start point on the map"))}" : "";
            var tot = sim.Totals();
            statsLabel.text =
                $"{Loc.T("objects")} {sim.Alive}/{tot.n} · {Loc.T("detected")} {tot.detected}\n" +
                $"{Loc.T("active")} {sim.SysActive} · {Loc.T("coasting")} {sim.SysCoasting}\n" +
                $"{Loc.T("STN per object")} {tot.meanStn:0.00} · {Loc.T("continuity")} {tot.continuity * 100:0}%\n" +
                $"{Loc.T("position error")} {(double.IsNaN(tot.rms) ? "-" : (tot.rms / 1000).ToString("0.00") + " km")} · {Loc.T("handovers")} {tot.handovers}\n" +
                $"{Loc.T("link messages")} {sim.net.sent:N0} · {Loc.T("plots")} {sim.plots:N0} · {Loc.T("false alarms")} {sim.falseAlarms:N0}";
            if (app.coverageAlt > 0 && !string.IsNullOrEmpty(app.map.coverageInfo))
            {
                var c = app.map.coverageInfo.Split('|');
                coverageLabel.text = $"{Loc.T("land")} {c[0]}% / {c[1]}% · {Loc.T("sea")} {c[2]}% / {c[3]}%  ({Loc.T("1+ radars")} / {Loc.T("2+ radars")})";
            }
            else coverageLabel.text = "";
            hintLabel.text = app.view.board2D ? Loc.T("Drag: pan · Wheel: zoom · Click: select") : Loc.T("Drag: rotate · Right-drag: pan · Wheel: zoom · Click: select");
            perfLabel.text = $"sim {app.StepMs:0.0} ms/frame · radars {sim.radars.Count} · links {sim.net.edges.Count}";

            // track list
            trackList.Clear();
            var tracks = sim.c2.tracks.Values.OrderBy(s => s.state).ThenBy(s => s.stn).ToList();
            if (tracks.Count == 0) trackList.Add(Text(Loc.T("No tracks"), 12, Dim));
            foreach (var s in tracks)
            {
                var stn = s.stn;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                row.style.paddingLeft = 4; row.style.paddingTop = 2; row.style.paddingBottom = 2;
                bool sel = stn == app.view.selectedStn;
                row.style.backgroundColor = sel ? new Color(0.13f, 0.45f, 0.7f, 0.6f) : new Color(0, 0, 0, 0);
                var col = Disp.ClassColor(s.cls); if (s.state == SysState.Coasting) col.a = 0.5f;
                var sw = new VisualElement(); sw.style.width = 4; sw.style.height = 26; sw.style.backgroundColor = col; sw.style.marginRight = 6;
                row.Add(sw);
                var p = s.PosAt(sim.t);
                int holders = sim.c2.Holders(s, sim.t).Count();
                var lab = Text($"{stn:00000}  {Loc.Class(s.cls)}  {p.z / 1000:0.0} km  {s.Vel.Length * 3.6:0} km/h\n" +
                               (s.state == SysState.Coasting ? Loc.T("Coasting") : $"R2 {sim.NodeName(s.r2)} · {holders} {Loc.T("Radar")}"), 11, s.state == SysState.Coasting ? Dim : Color.white);
                lab.style.marginBottom = 0;
                row.Add(lab);
                row.RegisterCallback<ClickEvent>(_ => { app.Select(stn == app.view.selectedStn ? 0 : stn, -1); Refresh(); });
                trackList.Add(row);
            }

            // details
            detail.Clear();
            if (app.view.selectedStn != 0 && sim.c2.tracks.TryGetValue(app.view.selectedStn, out var st)) TrackDetail(st);
            else if (app.view.selectedRadar >= 0) RadarDetail(sim.radars[app.view.selectedRadar]);

            // events
            var lines = new List<string>();
            for (int i = sim.events.Count - 1; i >= 0 && lines.Count < 7; i--)
            {
                var e = sim.events[i];
                if (e.kind == EventKind.LocalDrop) continue;
                var s = Loc.Event(e, sim);
                if (!string.IsNullOrEmpty(s)) lines.Add($"{Clock(e.t)}  {s}");
            }
            eventsLabel.text = string.Join("\n", lines);
        }

        void KV(string k, string v)
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
            var a = Text(k, 12, Dim); a.style.width = 140; a.style.flexShrink = 0;
            var b = Text(v, 12);
            b.style.flexShrink = 1;
            r.Add(a); r.Add(b);
            detail.Add(r);
        }

        void TrackDetail(SystemTrack s)
        {
            var sim = app.sim;
            var p = s.PosAt(sim.t);
            detail.Add(Text($"TN {s.stn:00000}", 18, Disp.ClassColor(s.cls), true));
            KV(Loc.T("Class"), Loc.Class(s.cls));
            KV(Loc.T("State"), s.state == SysState.Active ? Loc.T("Active") : Loc.T("Coasting"));
            KV(Loc.T("Position"), Geo.LatLon(p.x, p.y));
            KV(Loc.T("Altitude"), $"{p.z:N0} m");
            KV(Loc.T("Speed "), $"{s.Vel.Length * 3.6:0} km/h ({s.Vel.Length:0} m/s)");
            KV(Loc.T("Heading"), $"{Geo.Azimuth(s.x[3], s.x[4]) / Geo.Deg:000}°");
            KV(Loc.T("Uncertainty"), $"σ {s.Sigma(sim.t):0} m");
            KV(Loc.T("Track quality"), $"{s.tq} / 15");
            KV(Loc.T("Reporting radar (R2)"), sim.NodeName(s.r2));
            KV(Loc.T("Holding radars"), string.Join(", ", sim.c2.Holders(s, sim.t).Select(sim.NodeName)));
            var chain = string.Join(" → ", s.chain.Select(c => sim.NodeName(c.node) + (c.cued ? "*" : "")));
            KV(Loc.T("Handover chain"), chain + (s.chain.Any(c => c.cued) ? Loc.T2("   (* cued)", "   (* 큐잉)") : ""));
            var tg = sim.targets.Find(x => x.id == s.truthId);
            if (tg != null)
            {
                double err = (s.PosAt(sim.t) - tg.pos).Length;
                KV(Loc.T("Ground truth (evaluation)"), $"{tg.Name} {Loc.Kind(tg.kind)} · {Loc.T("error")} {err / 1000:0.00} km");
            }
            var r = Row(); r.Add(Btn(Loc.T("Deselect"), () => { app.Select(0, -1); Refresh(); })); detail.Add(r);
        }

        void RadarDetail(RadarSite r)
        {
            var sim = app.sim;
            var node = sim.nodes[r.index];
            detail.Add(Text($"{Loc.T("Radar")} {r.name}", 18, Disp.RadarColor(r.spec.kind), true));
            KV(Loc.T("Type"), Loc.Radar(r.spec.kind));
            KV(Loc.T("Position"), Geo.LatLon(r.x, r.y));
            KV(Loc.T("Ground"), r.atSea ? Loc.T("sea") : $"{r.ground:0} m");
            KV(Loc.T("Range"), $"{r.spec.rMax / 1000:0} km (1 m² Pd 0.5: {r.spec.r0 / 1000:0} km)");
            KV(Loc.T("Rotation"), r.Rotating ? $"{r.spec.period:0.#} s" : $"{r.spec.sector:0}° @ {r.boresight / Geo.Deg:000}°, {r.spec.period:0.#} s");
            KV(Loc.T("Local tracks"), $"{node.Confirmed} / {node.tracks.Count}");
            KV(Loc.T("Cues"), node.cues.Count.ToString());
            KV(Loc.T("Plots received"), node.plotsIn.ToString("N0"));
            KV(Loc.T("Link hops to C2"), $"{sim.net.Hops(r.index, sim.C2Node)} ({sim.net.latency[r.index, sim.C2Node]:0.00} s)");
            var rr = Row(); rr.Add(Btn(Loc.T("Deselect"), () => { app.Select(0, -1); Refresh(); })); detail.Add(rr);
        }

        // ------------------------------------------------------------------ map interaction

        bool IsOverUI(Vector2 screen)
        {
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return (left != null && left.worldBound.Contains(p)) || (right != null && right.worldBound.Contains(p)) || (bottom != null && bottom.worldBound.Contains(p));
        }

        void OnMapClick(Vector2 screen)
        {
            if (app == null || !app.Ready) return;
            if (app.spawnKind.HasValue)
            {
                var mp = app.rig.ScreenToMap(screen, (x, z) => app.map.SurfaceY(x * 1000, z * 1000));
                if (!mp.HasValue) return;
                if (!app.view.spawnStart.HasValue) app.view.spawnStart = mp;
                else
                {
                    app.Spawn(app.spawnKind.Value, app.view.spawnStart.Value, mp.Value);
                    app.view.spawnStart = null; app.spawnKind = null;
                    Build();
                }
                Refresh();
                return;
            }
            var (stn, radar) = app.view.Pick(screen);
            app.Select(stn, radar);
            Refresh();
        }

        void UpdatePanelsForCamera()
        {
            if (root?.panel == null || left == null) return;
            float scale = Screen.width / Mathf.Max(1f, root.layout.width);
            if (float.IsNaN(scale) || scale <= 0) return;
            app.rig.panelLeft = (left.layout.width + 10) * scale;
            app.rig.panelRight = (right.layout.width + 10) * scale;
        }

        void PlaceLabels()
        {
            var view = app.view;
            var cam = app.rig.Cam;
            if (root?.panel == null || cam == null) return;
            int n = 0;
            if (view.showLabels || view.selectedStn != 0)
                foreach (var l in view.labels)
                {
                    if (!view.showLabels && !l.selected) continue;
                    var sp = cam.WorldToScreenPoint(l.world);
                    if (sp.z <= 0 || sp.x < 0 || sp.y < 0 || sp.x > Screen.width || sp.y > Screen.height) continue;
                    var pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
                    if (n >= labelPool.Count)
                    {
                        var nl = new Label { pickingMode = PickingMode.Ignore };
                        nl.style.position = Position.Absolute; nl.style.fontSize = 10;
                        nl.style.unityTextOutlineWidth = 0.15f; nl.style.unityTextOutlineColor = new Color(0, 0, 0, 0.9f);
                        labelLayer.Add(nl); labelPool.Add(nl);
                    }
                    var lb = labelPool[n++];
                    lb.style.display = DisplayStyle.Flex;
                    lb.text = l.text;
                    var c = l.color; c.a = Mathf.Max(c.a, l.kind == 0 ? 0.75f : 0.95f);
                    lb.style.color = c;
                    lb.style.fontSize = l.kind == 1 ? (l.selected ? 13 : 11) : 10;
                    lb.style.left = pp.x + (l.kind == 1 ? 10 : 6);
                    lb.style.top = pp.y - (l.kind == 1 ? 8 : 14);
                }
            for (int i = n; i < labelPool.Count; i++) labelPool[i].style.display = DisplayStyle.None;
        }
    }
}
