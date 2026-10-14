using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RadarNet.Core;
using RadarNet.UI;
using RadarNet.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RadarNet.EditorTools
{
    /// <summary>Scene setup and app control commands: `unity command rn_*`.</summary>
    public static class RadarNetCommands
    {
        const string ScenePath = "Assets/RadarNet/Scenes/RadarNet.unity";
        const string ResFolder = "Assets/RadarNet/Resources";
        const string UiFolder = "Assets/RadarNet/UI";
        const string PanelSettingsPath = UiFolder + "/RadarNetPanelSettings.asset";
        const string ThemePath = UiFolder + "/RadarNetTheme.tss";
        const string TextSettingsPath = UiFolder + "/RadarNetTextSettings.asset";
        const string FontPath = UiFolder + "/Fonts/NotoSansKR-Regular.otf";
        const string FontAssetPath = UiFolder + "/Fonts/NotoSansKR-Regular SDF.asset";

        static Material EnsureMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) { m.shader = shader; EditorUtility.SetDirty(m); }
            return m;
        }

        [CliCommand("rn_setup_scene", "Create the RadarNet scene: camera rig, light, map (terrain), simulation view, app controller and UI. Saves the scene.")]
        public static string SetupScene([CliArg("scene_path", "Scene asset path")] string scenePath = ScenePath)
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var dem = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(TerrainCommands.DemPath);
            if (dem == null) return "error: run rn_fetch_terrain first";
            var terrainShader = Shader.Find("RadarNet/Terrain"); var overlayShader = Shader.Find("RadarNet/Overlay");
            if (terrainShader == null || overlayShader == null) return "error: RadarNet shaders not found";
            var terrainMat = EnsureMaterial(ResFolder + "/RN_Terrain.mat", terrainShader);
            var overlayMat = EnsureMaterial(ResFolder + "/RN_Overlay.mat", overlayShader);
            var litMat = EnsureMaterial(ResFolder + "/RN_Lit.mat", Shader.Find("Universal Render Pipeline/Lit"));
            PlayerSettings.runInBackground = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.skybox = null;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; camData.stopNaN = true;
            var rig = camGo.AddComponent<CameraRig>();

            var mapGo = new GameObject("Map");
            var map = mapGo.AddComponent<MapView>(); map.terrainMaterial = terrainMat;
            var viewGo = new GameObject("SimView");
            var view = viewGo.AddComponent<SimView>(); view.overlayMaterial = overlayMat; view.litMaterial = litMat;

            var appGo = new GameObject("App");
            appGo.SetActive(false);
            var app = appGo.AddComponent<AppController>();
            app.dem = dem; app.map = map; app.view = view; app.rig = rig;
            app.scenario = ScenarioKind.Mixed;

            var uiGo = new GameObject("UI");
            uiGo.SetActive(false);
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = EnsurePanelSettings();
            var ui = uiGo.AddComponent<RadarNetUI>(); ui.app = app;
            SetupFont(false);
            appGo.SetActive(true);
            uiGo.SetActive(true);
            app.Advance(120);

            EditorSceneManager.SaveScene(scene, scenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != scenePath && !s.path.EndsWith("SampleScene.unity")).ToList();
            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            return $"scene={scenePath} build_ms={sw.ElapsedMilliseconds} radars={app.sim.radars.Count} sim_build_ms={app.sim.buildMs:0}\n{app.sim.Report()}";
        }

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            return ps;
        }

        [CliCommand("rn_setup_font", "Create the Noto Sans KR dynamic font asset and register it as a UI fallback font.")]
        public static string SetupFont([CliArg("force", "Recreate the font asset")] bool force = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return $"error: font not found: {FontPath}";
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fa != null && force) { AssetDatabase.DeleteAsset(FontAssetPath); fa = null; }
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) return "error: CreateFontAsset failed";
                fa.name = "NotoSansKR-Regular SDF";
                fa.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                if (fa.atlasTextures != null) foreach (var tex in fa.atlasTextures) if (tex != null) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                EditorUtility.SetDirty(fa);
            }
            var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, TextSettingsPath); }
            if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new List<FontAsset>();
            if (!ts.fallbackFontAssets.Contains(fa)) ts.fallbackFontAssets.Add(fa);
            ts.clearDynamicDataOnBuild = true;
            EditorUtility.SetDirty(ts);
            var ps = EnsurePanelSettings();
            ps.textSettings = ts;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return $"font={FontAssetPath} hangul={fa.HasCharacter('한', true, true)}";
        }

        // ------------------------------------------------------------------ helpers

        static AppController App(out string error)
        {
            error = null;
            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app == null) { error = "error: no AppController in the active scene (run rn_setup_scene)"; return null; }
            app.EnsureBuilt();
            if (!app.Ready) { error = "error: " + app.status; return null; }
            return app;
        }

        static Vector3 ParseLL(string s)
        {
            var p = s.Split(',');
            Geo.Forward(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture), out var x, out var y);
            return new Vector3((float)(x / 1000), 0, (float)(y / 1000));
        }

        static string Status(AppController app)
        {
            var sim = app.sim;
            var sb = new StringBuilder();
            sb.AppendLine($"play={Application.isPlaying} running={app.running} speed={app.speed} view={(app.view.board2D ? "2D" : "3D")} coverage={app.coverageAlt}m exag={Disp.exag} selected={app.view.selectedStn}/{app.view.selectedRadar}");
            sb.Append(sim.Report());
            sb.AppendLine("system tracks:");
            foreach (var s in sim.c2.tracks.Values.OrderBy(s => s.stn))
            {
                var p = s.PosAt(sim.t);
                sb.AppendLine($"  {s.stn:00000} {s.state,-8} {s.cls,-13} {Geo.LatLon(p.x, p.y)} alt={p.z:0} v={s.Vel.Length:0} R2={sim.NodeName(s.r2)} holders=[{string.Join(",", sim.c2.Holders(s, sim.t).Select(sim.NodeName))}] chain={string.Join(">", s.chain.Select(c => sim.NodeName(c.node) + (c.cued ? "*" : "")))} truth=T{s.truthId:00}");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ app control

        [CliCommand("rn_status", "Simulation state: per-object statistics and the control centre's system tracks.")]
        public static string StatusCmd()
        {
            var app = App(out var err); if (app == null) return err;
            return Status(app);
        }

        [CliCommand("rn_load", "Load a scenario (Mixed, CruiseMissiles, DroneSwarm, Ballistic, Helicopters, Airliners, AllTypes, Empty) and optionally advance it.")]
        public static string Load([CliArg("scenario", "Scenario name")] string scenario = "Mixed",
                                  [CliArg("seed", "Random seed")] int seed = 1,
                                  [CliArg("advance", "Seconds to simulate right away")] double advance = 0)
        {
            var app = App(out var err); if (app == null) return err;
            if (!Enum.TryParse<ScenarioKind>(scenario, true, out var k)) return $"error: unknown scenario {scenario}";
            app.LoadScenario(k, seed);
            if (advance > 0) app.Advance(advance);
            MarkDirty(app);
            return Status(app);
        }

        [CliCommand("rn_advance", "Advance the simulation by N seconds (edit or play mode).")]
        public static string Advance([CliArg("seconds", "Simulated seconds")] double seconds = 60)
        {
            var app = App(out var err); if (app == null) return err;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            app.Advance(seconds);
            return $"advanced {seconds}s in {sw.ElapsedMilliseconds} ms\n" + Status(app);
        }

        [CliCommand("rn_spawn", "Add an object flying from one lat,lon to another (kind: Drone, Helicopter, Fighter, Airliner, CruiseMissile, BallisticMissile).")]
        public static string Spawn([CliArg("kind", "Platform kind")] string kind = "Fighter",
                                   [CliArg("from", "lat,lon")] string from = "36.0,124.5",
                                   [CliArg("to", "lat,lon")] string to = "37.0,129.5")
        {
            var app = App(out var err); if (app == null) return err;
            if (!Enum.TryParse<PlatformKind>(kind, true, out var k)) return $"error: unknown kind {kind}";
            var tg = app.Spawn(k, ParseLL(from), ParseLL(to));
            return $"spawned {tg.Name} {tg.kind} at t={tg.tSpawn:0.0}";
        }

        [CliCommand("rn_set", "Set run state and display options. Empty = unchanged.")]
        public static string Set([CliArg("running", "true/false")] string running = "",
                                 [CliArg("speed", "Simulation speed (x)")] float speed = -1,
                                 [CliArg("view", "3d or 2d")] string view = "",
                                 [CliArg("coverage", "Coverage overlay height above surface (m), 0 = off")] int coverage = -1,
                                 [CliArg("exag", "Height exaggeration")] float exag = -1,
                                 [CliArg("select", "STN to select (0 = none)")] int select = -1,
                                 [CliArg("radar", "Radar name to select")] string radar = "",
                                 [CliArg("toggles", "Comma list, e.g. rings=0,links=1,truth=1,beams=1,labels=1,trails=1,holders=1")] string toggles = "",
                                 [CliArg("lang", "ko or en")] string lang = "")
        {
            var app = App(out var err); if (app == null) return err;
            if (running == "true") app.running = true; else if (running == "false") app.running = false;
            if (speed > 0) app.speed = speed;
            if (view == "2d") app.SetView2D(true); else if (view == "3d") app.SetView2D(false);
            if (coverage >= 0) app.SetCoverage(coverage);
            if (exag > 0) app.SetExaggeration(exag);
            if (select >= 0) app.Select(select, -1);
            if (radar != "") { int i = app.sim.radars.FindIndex(r => r.name.Equals(radar, StringComparison.OrdinalIgnoreCase)); app.Select(0, i); }
            if (lang == "ko") Loc.Korean = true; else if (lang == "en") Loc.Korean = false;
            foreach (var kv in toggles.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('='); bool on = p.Length < 2 || p[1] == "1" || p[1] == "true";
                var v = app.view;
                switch (p[0].Trim())
                {
                    case "rings": v.showRings = on; break; case "links": v.showLinks = on; break; case "truth": v.showTruth = on; break;
                    case "beams": v.showBeams = on; break; case "labels": v.showLabels = on; break; case "trails": v.showTrails = on; break;
                    case "holders": v.showHolders = on; break;
                }
            }
            var ui = UnityEngine.Object.FindAnyObjectByType<RadarNetUI>();
            if (ui != null) ui.Build();
            MarkDirty(app);
            return $"running={app.running} speed={app.speed} view={(app.view.board2D ? "2D" : "3D")} coverage={app.coverageAlt} ({app.map.coverageInfo}) exag={Disp.exag} selected={app.view.selectedStn}/{app.view.selectedRadar}";
        }

        [CliCommand("rn_camera", "Place the camera. 3D: focus lat,lon + yaw/pitch/distance (km). 2D: centre lat,lon + size (half height, km).")]
        public static string CameraCmd([CliArg("focus", "lat,lon (empty = keep)")] string focus = "",
                                       [CliArg("yaw", "deg")] float yaw = float.NaN,
                                       [CliArg("pitch", "deg")] float pitch = float.NaN,
                                       [CliArg("distance", "km")] float distance = float.NaN,
                                       [CliArg("size", "2D half height (km)")] float size = float.NaN,
                                       [CliArg("height", "focus height (units)")] float height = float.NaN,
                                       [CliArg("reset", "Reset the view")] bool reset = false)
        {
            var app = App(out var err); if (app == null) return err;
            var rig = app.rig;
            if (reset) { if (rig.ortho2D) rig.Reset2D(); else rig.Reset3D(); }
            if (focus != "")
            {
                var f = ParseLL(focus);
                f.y = app.map.SurfaceY(f.x * 1000, f.z * 1000);
                rig.focus = f; rig.center2D = new Vector2(f.x, f.z);
                app.followSelected = false;
            }
            if (!float.IsNaN(height)) rig.focus.y = height;
            if (!float.IsNaN(yaw)) rig.yaw = yaw;
            if (!float.IsNaN(pitch)) rig.pitch = pitch;
            if (!float.IsNaN(distance)) rig.distance = distance;
            if (!float.IsNaN(size)) rig.orthoSize = size;
            rig.Place();
            MarkDirty(app);
            return $"focus={rig.focus} yaw={rig.yaw} pitch={rig.pitch} distance={rig.distance} 2d={rig.ortho2D} centre={rig.center2D} size={rig.orthoSize}";
        }

        // ------------------------------------------------------------------ screenshots

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }

        [CliCommand("rn_capture", "Render the main camera (map, radars, tracks — no UI panels) to Screenshots/<name>.png.")]
        public static string Capture([CliArg("name", "File name")] string name = "shot", [CliArg("width", "Width")] int width = 1600, [CliArg("height", "Height")] int height = 1000)
        {
            var app = App(out var err); if (app == null) return err;
            var rig = app.rig; var cam = rig.Cam;
            float pl = rig.panelLeft, pr = rig.panelRight;
            rig.panelLeft = rig.panelRight = 0;
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;   // so pixel sizes (line widths) match the output
            rig.Place();
            app.view.Rebuild(cam);
            cam.targetTexture = null;
            var tex = Render(cam, width, height);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            rig.panelLeft = pl; rig.panelRight = pr;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name.EndsWith(".png") ? name : name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rig.Place();
            return $"saved {path} (overlay segments {app.view.labels.Count} labels)";
        }

        static void MarkDirty(AppController app) { if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(app.gameObject.scene); }
    }
}
