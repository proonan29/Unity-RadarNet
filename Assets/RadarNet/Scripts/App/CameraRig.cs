using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RadarNet
{
    /// <summary>
    /// 3D orbit camera (left-drag rotate, right/middle-drag pan, wheel zoom) and 2D top-down board (drag pan, wheel zoom).
    /// A click without dragging is reported through onClick. The projection is shifted so the map centre sits between the UI panels.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0, 0, -30);
        public float yaw = 0, pitch = 50, distance = 1080;
        public bool ortho2D;
        public Vector2 center2D = Vector2.zero;
        public float orthoSize = 520;
        [Tooltip("Left/right UI panel widths in screen pixels")] public float panelLeft, panelRight;
        public Func<Vector2, bool> isOverUI;
        public Func<Vector3?> follow;
        public event Action<Vector2> onClick;
        [NonSerialized] public Vector2 mouse;

        Camera cam;
        Vector2 pressPos, last;
        bool pressed, dragging, panDrag;
        public Camera Cam => cam != null ? cam : cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (Application.isPlaying) HandleInput();
            if (follow != null)
            {
                var f = follow();
                if (f.HasValue)
                {
                    float k = Application.isPlaying ? 1 - Mathf.Exp(-Time.unscaledDeltaTime * 4) : 1;
                    if (ortho2D) center2D = Vector2.Lerp(center2D, new Vector2(f.Value.x, f.Value.z), k);
                    else focus = Vector3.Lerp(focus, f.Value, k);
                }
            }
            Place();
        }

        void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return;
            Vector2 pos = m.position.ReadValue();
            mouse = pos;
            bool overUI = isOverUI != null && isOverUI(pos);
            bool l = m.leftButton.isPressed, r = m.rightButton.isPressed || m.middleButton.isPressed;
            if ((m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) && !overUI)
            {
                pressed = true; dragging = false; pressPos = last = pos; panDrag = !m.leftButton.wasPressedThisFrame;
            }
            if (pressed && (l || r))
            {
                var d = pos - last; last = pos;
                if ((pos - pressPos).sqrMagnitude > 25) dragging = true;
                if (dragging)
                {
                    if (ortho2D || panDrag) Pan(d);
                    else { yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, 12f, 89f); }
                }
            }
            if (pressed && !l && !r)
            {
                if (!dragging && !panDrag) onClick?.Invoke(pos);
                pressed = false; dragging = false;
            }
            float wheel = m.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !overUI)
            {
                float k = wheel > 0 ? 0.88f : 1.13f;
                if (ortho2D) orthoSize = Mathf.Clamp(orthoSize * k, 15f, 650f);
                else distance = Mathf.Clamp(distance * k, 20f, 2600f);
            }
#endif
        }

        void Pan(Vector2 d)
        {
            var c = Cam;
            float wpp = ortho2D ? orthoSize * 2 / Mathf.Max(1, c.pixelHeight) : distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2 / Mathf.Max(1, c.pixelHeight);
            if (ortho2D) { center2D -= d * wpp; return; }
            follow = null;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            focus -= (right * d.x + fwd * d.y / Mathf.Max(0.3f, Mathf.Sin(pitch * Mathf.Deg2Rad))) * wpp;
        }

        public void Place()
        {
            var c = Cam;
            c.ResetProjectionMatrix();
            if (ortho2D)
            {
                c.orthographic = true;
                c.orthographicSize = orthoSize;
                transform.position = new Vector3(center2D.x, 2000f, center2D.y);
                transform.rotation = Quaternion.Euler(90, 0, 0);
                c.nearClipPlane = 1; c.farClipPlane = 4000;
            }
            else
            {
                c.orthographic = false;
                var rot = Quaternion.Euler(pitch, yaw, 0);
                transform.position = focus + rot * new Vector3(0, 0, -distance);
                transform.rotation = rot;
                c.nearClipPlane = Mathf.Max(0.05f, distance * 0.002f); c.farClipPlane = 8000;
            }
            if (Screen.width > 0 && (panelLeft > 0 || panelRight > 0))
            {
                float f = (panelLeft - panelRight) / Mathf.Max(1, c.pixelWidth);
                var p = c.projectionMatrix;
                if (ortho2D) p.m03 = f; else p.m02 = -f;
                c.projectionMatrix = p;
            }
        }

        public void Reset3D() { ortho2D = false; focus = new Vector3(0, 0, -30); yaw = 0; pitch = 50; distance = 1080; follow = null; }
        public void Reset2D() { ortho2D = true; center2D = Vector2.zero; orthoSize = 520; follow = null; }

        /// <summary>Screen point → map point on the terrain (km units), iterating against the surface height.</summary>
        public Vector3? ScreenToMap(Vector2 screen, Func<float, float, float> surfaceY)
        {
            var ray = Cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
            float y = 0;
            Vector3 p = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                float tt = (y - ray.origin.y) / ray.direction.y;
                if (tt < 0) return null;
                p = ray.origin + ray.direction * tt;
                y = ortho2D ? 0 : surfaceY(p.x, p.z);
            }
            return new Vector3(p.x, y, p.z);
        }
    }
}
