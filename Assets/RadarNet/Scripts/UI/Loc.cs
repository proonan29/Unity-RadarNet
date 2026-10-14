using System.Collections.Generic;
using RadarNet.Core;
using UnityEngine;

namespace RadarNet.UI
{
    /// <summary>Korean/English strings. English text is the key; the choice is stored in PlayerPrefs.</summary>
    public static class Loc
    {
        static bool? korean;
        public static bool Korean
        {
            get { if (korean == null) korean = PlayerPrefs.GetInt("RadarNet.lang", 1) == 1; return korean.Value; }
            set { korean = value; PlayerPrefs.SetInt("RadarNet.lang", value ? 1 : 0); }
        }

        static readonly Dictionary<string, string> ko = new Dictionary<string, string>
        {
            { "Radar data link tracking simulation", "레이더 데이터링크 추적 시뮬레이션" },
            { "Scenario", "시나리오" }, { "Run", "실행" }, { "Start", "시작" }, { "Pause", "일시정지" }, { "Restart", "다시 시작" },
            { "New seed", "새 시드" }, { "Speed", "속도" }, { "Sim time", "시뮬레이션 시각" },
            { "Add object", "물체 추가" }, { "Click the start point on the map", "지도에서 출발점을 클릭하세요" },
            { "Click the destination", "도착점을 클릭하세요" }, { "Cancel", "취소" },
            { "View", "보기" }, { "3D terrain", "3D 지형" }, { "2D board", "2D 상황판" },
            { "Coverage (1 m², Pd ≥ 0.5)", "탐지 범위 (1 m², Pd ≥ 0.5)" }, { "Off", "끄기" },
            { "Radar rings", "레이더 범위" }, { "Beams", "빔" }, { "Data link", "데이터링크" }, { "Ground truth", "실제 위치" },
            { "Trails", "항적선" }, { "Labels", "라벨" }, { "Holder lines", "추적 레이더 선" }, { "Follow selected", "선택 항적 따라가기" },
            { "Statistics", "통계" }, { "Settings", "설정" }, { "Language", "언어" }, { "Height exaggeration", "높이 과장" },
            { "Legend", "범례" }, { "System tracks (C2)", "시스템 항적 (관제소)" }, { "No tracks", "항적 없음" },
            { "Events", "이벤트" }, { "Selected", "선택" }, { "Deselect", "선택 해제" },
            { "objects", "물체" }, { "detected", "탐지" }, { "active", "추적 중" }, { "coasting", "외삽" },
            { "STN per object", "물체당 항적번호" }, { "continuity", "연속성" }, { "position error", "위치 오차" },
            { "handovers", "핸드오버" }, { "link messages", "링크 메시지" }, { "plots", "플롯" }, { "false alarms", "오경보" },
            { "land", "육지" }, { "sea", "바다" }, { "1+ radars", "1개 이상" }, { "2+ radars", "2개 이상" },
            { "Class", "분류" }, { "State", "상태" }, { "Position", "위치" }, { "Altitude", "고도" }, { "Speed ", "속도 " },
            { "Heading", "방위" }, { "Uncertainty", "불확실성" }, { "Track quality", "항적 품질" }, { "Reporting radar (R2)", "보고 책임 레이더 (R2)" },
            { "Holding radars", "추적 중인 레이더" }, { "Handover chain", "핸드오버 경로" }, { "Ground truth (evaluation)", "실제 물체 (평가용)" },
            { "error", "오차" }, { "Active", "추적" }, { "Coasting", "외삽(코스팅)" },
            { "Radar", "레이더" }, { "Type", "종류" }, { "Ground", "지면" }, { "Range", "탐지거리" }, { "Rotation", "회전 주기" },
            { "Local tracks", "국지 항적" }, { "Cues", "큐" }, { "Plots received", "받은 플롯" }, { "Link hops to C2", "관제소까지 홉" },
            { "Terrain: AWS Terrain Tiles (SRTM, GMTED, ETOPO1). Radar sites are fictional.", "지형: AWS Terrain Tiles (SRTM, GMTED, ETOPO1). 레이더 위치는 가상입니다." },
            { "Drag: rotate · Right-drag: pan · Wheel: zoom · Click: select", "드래그: 회전 · 오른쪽 드래그: 이동 · 휠: 확대 · 클릭: 선택" },
            { "Drag: pan · Wheel: zoom · Click: select", "드래그: 이동 · 휠: 확대 · 클릭: 선택" },
            { "running", "실행 중" }, { "paused", "일시정지" },
        };

        public static string T(string en) => Korean && ko.TryGetValue(en, out var s) ? s : en;

        public static string Scenario(ScenarioKind k) => k switch
        {
            ScenarioKind.Mixed => T2("Mixed traffic", "혼합 교통"),
            ScenarioKind.CruiseMissiles => T2("Low-level cruise missiles", "저고도 순항미사일"),
            ScenarioKind.DroneSwarm => T2("Drone swarm", "드론 편대"),
            ScenarioKind.Ballistic => T2("Ballistic missiles", "탄도미사일"),
            ScenarioKind.Helicopters => T2("Coastal helicopters", "해안 저고도 헬기"),
            ScenarioKind.Airliners => T2("Airliners", "민항기"),
            ScenarioKind.AllTypes => T2("All types", "전체"),
            _ => T2("Empty", "빈 지도"),
        };

        public static string Kind(PlatformKind k) => k switch
        {
            PlatformKind.Drone => T2("Drone", "드론"),
            PlatformKind.Helicopter => T2("Helicopter", "헬기"),
            PlatformKind.Fighter => T2("Fighter", "전투기"),
            PlatformKind.Airliner => T2("Airliner", "민항기"),
            PlatformKind.CruiseMissile => T2("Cruise missile", "순항미사일"),
            _ => T2("Ballistic missile", "탄도미사일"),
        };

        public static string Class(TrackClass c) => c switch
        {
            TrackClass.Drone => T2("Drone?", "드론 추정"),
            TrackClass.Helicopter => T2("Helicopter?", "헬기 추정"),
            TrackClass.Jet => T2("Jet", "제트기"),
            TrackClass.Airliner => T2("Airliner?", "민항기 추정"),
            TrackClass.CruiseMissile => T2("Cruise missile?", "순항미사일 추정"),
            TrackClass.Ballistic => T2("Ballistic", "탄도 비행체"),
            _ => T2("Unknown", "미상"),
        };

        public static string Radar(RadarKind k) => k switch
        {
            RadarKind.LongRange => T2("Long-range surveillance", "장거리 감시"),
            RadarKind.MediumRange => T2("Medium-range", "중거리"),
            RadarKind.GapFiller => T2("Low-altitude gap filler", "저고도 갭필러"),
            RadarKind.Coastal => T2("Coastal surveillance", "해안 감시"),
            RadarKind.Naval => T2("Naval (ship)", "함정"),
            _ => T2("Phased array (early warning)", "위상배열 조기경보"),
        };

        public static string T2(string en, string k) => Korean ? k : en;

        public static string Event(SimEvent e, Sim sim)
        {
            string R(int i) => sim.NodeName(i);
            string tn = $"TN {e.stn:00000}";
            switch (e.kind)
            {
                case EventKind.Spawned: { var tg = sim.targets.Find(x => x.id == e.aux); return tg == null ? "" : T2($"{tg.Name} {Kind(tg.kind)} appeared", $"{tg.Name} {Kind(tg.kind)} 출현"); }
                case EventKind.Ended: { var tg = sim.targets.Find(x => x.id == e.aux); return tg == null ? "" : T2($"{tg.Name} {Kind(tg.kind)} {tg.endReason}", $"{tg.Name} {Kind(tg.kind)} 비행 종료"); }
                case EventKind.NewTrack: return T2($"{R(e.radar)} detected a new object → {tn}", $"{R(e.radar)} 새 물체 포착 → {tn} 생성");
                case EventKind.Adopted when e.radar == e.other: return T2($"{R(e.radar)} re-acquired its remembered {tn}", $"{R(e.radar)} 기억된 {tn} 재포착 (같은 번호 유지)");
                case EventKind.Cued when e.radar == e.other: return T2($"{R(e.radar)} re-acquired {tn} at the remembered position", $"{R(e.radar)} 기억된 위치에서 {tn} 즉시 재포착");
                case EventKind.Adopted: return T2($"{R(e.radar)} correlated with {tn} from {R(e.other)} (data link)", $"{R(e.radar)}가 {R(e.other)}의 {tn}과 상관 → 같은 번호로 추적");
                case EventKind.Cued: return T2($"{R(e.radar)} acquired {tn} on cue from {R(e.other)}", $"{R(e.radar)}가 {R(e.other)}의 큐로 {tn} 즉시 포착 (핸드오버)");
                case EventKind.Handover: return T2($"{tn} reporting {R(e.other)} → {R(e.radar)}", $"{tn} 보고 책임 {R(e.other)} → {R(e.radar)}");
                case EventKind.Merged: return T2($"{R(e.radar)} merged duplicate TN {e.aux:00000} into {tn}", $"{R(e.radar)} 중복 TN {e.aux:00000} → {tn} 통합");
                case EventKind.LocalDrop: return T2($"{R(e.radar)} lost {tn}", $"{R(e.radar)} {tn} 놓침");
                case EventKind.Coasting: return T2($"{tn} coasting (no radar holds it)", $"{tn} 외삽 추적 (보유 레이더 없음)");
                case EventKind.Resumed: return T2($"{tn} re-acquired by {R(e.radar)}", $"{tn} {R(e.radar)}가 재포착");
                case EventKind.Lost: return e.aux != 0 ? T2($"{tn} merged into TN {e.aux:00000}", $"{tn} → TN {e.aux:00000} 통합") : T2($"{tn} dropped", $"{tn} 항적 종료");
            }
            return e.kind.ToString();
        }
    }
}
