using System;
using RadarNet.Core;
using RadarNet.World;
using UnityEngine;

namespace RadarNet
{
    /// <summary>
    /// Owns the simulation (built from the heightmap at enable), advances it in play mode at the chosen speed, and exposes
    /// the actions used by the UI and the CLI: scenarios, spawning objects, view mode, coverage overlay, selection.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class AppController : MonoBehaviour
    {
        public TextAsset dem;
        public MapView map;
        public SimView view;
        public CameraRig rig;

        public ScenarioKind scenario = ScenarioKind.Mixed;
        public int seed = 1;
        public float speed = 20;
        public bool running = true;
        public float exaggeration = 8;
        public int coverageAlt;
        public float coverageRcs = 1;
        public bool followSelected = true;

        [NonSerialized] public Sim sim;
        [NonSerialized] public PlatformKind? spawnKind;
        [NonSerialized] public string status = "";
        public event Action onChanged;
        double acc;
        float lastStepMs;

        public bool Ready => sim != null;
        public float StepMs => lastStepMs;

        void OnEnable() => EnsureBuilt();

        /// <summary>Builds the simulation once, and (re)builds the generated map/view objects whenever they are missing
        /// (they are DontSave objects, destroyed on play-mode transitions when domain/scene reload is disabled).</summary>
        public void EnsureBuilt()
        {
            if (sim != null && map.IsBuilt && view.IsBuilt) return;
            if (sim == null)
            {
                if (dem == null) dem = Resources.Load<TextAsset>("RadarNetDEM");
                if (dem == null) { status = "heightmap missing (rn_fetch_terrain)"; return; }
                Disp.exag = exaggeration;
                sim = new Sim(Heightmap.Load(dem.bytes));
                sim.Reset(scenario, seed);
            }
            Disp.exag = exaggeration;
            if (!map.IsBuilt) map.Build(sim.hm);
            if (!view.IsBuilt) view.Init(sim, map);
            view.cam = rig.Cam;
            view.speed = speed;
            rig.follow = FollowTarget;
            SetView2D(rig.ortho2D);
            if (coverageAlt > 0) map.ComputeCoverage(sim, coverageAlt, coverageRcs);
        }

        void Update()
        {
            if (sim != null && (!map.IsBuilt || !view.IsBuilt)) EnsureBuilt();
            if (sim == null || !Application.isPlaying) return;
            view.speed = running ? speed : 0;
            if (!running) return;
            acc += Time.unscaledDeltaTime * speed;
            int n = (int)(acc / Sim.Dt);
            int max = 6000;
            if (n > max) { n = max; acc = 0; } else acc -= n * Sim.Dt;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) sim.Step();
            lastStepMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        Vector3? FollowTarget()
        {
            if (!followSelected || sim == null || view.selectedStn == 0) return null;
            if (!sim.c2.tracks.TryGetValue(view.selectedStn, out var s)) return null;
            return view.TrackPos(s.PosAt(sim.t));
        }

        // ------------------------------------------------------------------ actions

        public void LoadScenario(ScenarioKind k, int newSeed)
        {
            EnsureBuilt();
            scenario = k; seed = newSeed;
            sim.Reset(k, newSeed);
            acc = 0;
            view.selectedStn = 0; view.selectedRadar = -1;
            onChanged?.Invoke();
        }

        public void Restart() => LoadScenario(scenario, seed);

        public Target Spawn(PlatformKind kind, Vector3 from, Vector3 to)
        {
            EnsureBuilt();
            var tg = sim.AddTarget(kind, Disp.Map(from), Disp.Map(to));
            onChanged?.Invoke();
            return tg;
        }

        public void SetView2D(bool on)
        {
            view.board2D = on;
            map.SetBoard(on ? 1 : 0);
            if (on && !rig.ortho2D) rig.Reset2D();
            if (!on && rig.ortho2D) rig.Reset3D();
            rig.ortho2D = on;
            rig.follow = FollowTarget;
        }

        public void SetCoverage(int altMeters)
        {
            coverageAlt = altMeters;
            map.ComputeCoverage(sim, altMeters, coverageRcs);
            onChanged?.Invoke();
        }

        public void SetExaggeration(float e)
        {
            exaggeration = e; Disp.exag = e;
            map.Build(sim.hm);
            if (coverageAlt > 0) map.ComputeCoverage(sim, coverageAlt, coverageRcs);
        }

        public void Select(int stn, int radar)
        {
            view.selectedStn = stn; view.selectedRadar = radar;
            rig.follow = FollowTarget;
            onChanged?.Invoke();
        }

        public void Advance(double seconds) { EnsureBuilt(); sim.Advance(seconds); }
    }
}
