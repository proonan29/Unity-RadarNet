using System.Linq;
using System.Text;
using UnityEngine;

// Scene diagnostics: generated objects, camera, renderers.
public static class Diag
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var cam = Camera.main;
        sb.AppendLine($"play={Application.isPlaying} cam={(cam ? cam.transform.position.ToString() : "none")} rot={(cam ? cam.transform.eulerAngles.ToString() : "")} ortho={(cam ? cam.orthographic : false)} size={(cam ? cam.orthographicSize : 0)} near={(cam ? cam.nearClipPlane : 0)} far={(cam ? cam.farClipPlane : 0)} pix={(cam ? cam.pixelWidth + "x" + cam.pixelHeight : "")} mask={(cam ? cam.cullingMask : 0)}");
        var all = Resources.FindObjectsOfTypeAll<MeshRenderer>().Where(r => r.gameObject.scene.IsValid() || r.hideFlags != HideFlags.None).ToList();
        sb.AppendLine($"mesh renderers={all.Count} active={all.Count(r => r.gameObject.activeInHierarchy)}");
        foreach (var g in all.GroupBy(r => r.transform.root.name)) sb.AppendLine($"  root '{g.Key}': {g.Count()} active={g.Count(r => r.gameObject.activeInHierarchy)} scene={g.First().gameObject.scene.name} flags={g.First().hideFlags} mat={(g.First().sharedMaterial ? g.First().sharedMaterial.shader.name : "null")}");
        foreach (var mv in Object.FindObjectsByType<RadarNet.World.MapView>()) sb.AppendLine($"MapView children={mv.transform.childCount} hm={(mv.hm != null)}");
        foreach (var sv in Object.FindObjectsByType<RadarNet.World.SimView>()) sb.AppendLine($"SimView children={sv.transform.childCount} sim={(sv.sim != null)} cam={(sv.cam ? sv.cam.name : "null")} labels={sv.labels.Count}");
        return sb.ToString();
    }
}
