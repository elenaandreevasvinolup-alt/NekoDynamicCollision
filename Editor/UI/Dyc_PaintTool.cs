using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Кисть: назначает треугольникам метки групп материалов.
    ///
    /// Два жёстких правила (всегда явно показываются в интерфейсе, без тихого отказа):
    ///   1. Работает только пока не запущен Play-режим;
    ///   2. Не работает при превью анимации в окне Animation.
    ///
    /// Поза привязки НЕ требуется: треугольники берутся по текущей позе
    /// (см. Dyc_MeshCache), а метки хранятся по номерам треугольников, поэтому
    /// поза на результат не влияет.
    ///
    /// Метка — это не "исключение", а "разбиение на зоны": метка управляет группой материалов, что даёт
    /// "тот же табурет: мягкие грани — один физический материал, жёсткие — другой".
    /// </summary>
    [InitializeOnLoad]
    public static class Dyc_PaintTool
    {
        public static bool Active;
        public static Dyc_DynamicCollision Target;
        public static int GroupIndex;
        public static float Radius = 0.06f;
        public static float Depth = 0.4f;
        public static bool XRay;
        public static bool SymmetryX = true;
        public static bool Invert;   // при удержании стирает обратно в группу по умолчанию

        // ---- Вершины мира по текущей позе живут в общем кэше: его же читает
        // подсветка закрашенных граней, поэтому набор вершин гарантированно один.
        static Vector3[] _verts;
        static int[] _tris;
        static Vector3[] _centroids;

        static Dyc_PaintTool()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.hierarchyChanged += Invalidate;
        }

        public static void Invalidate()
        {
            _verts = null;
            _tris = null;
            _centroids = null;

            // Общий кэш меша НЕ сбрасываем: он сам инвалидируется по отпечатку
            // объекта/меша/позы. Иначе каждое изменение иерархии в сцене тянуло
            // бы повторный BakeMesh.
            Dyc_GizmoDraw.InvalidateCache();
        }

        public static void Begin(Dyc_DynamicCollision target, int groupIndex)
        {
            Target = target;
            GroupIndex = groupIndex;
            Active = target != null;

            // без подсветки непонятно, что уже помечено
            Dyc_GizmoDraw.Enabled = true;
            Dyc_GizmoDraw.OnlySelected = true;
            Dyc_GizmoDraw.DrawPainted = true;
            Invalidate();
            EnsureMask(target);
            SceneView.RepaintAll();
        }

        public static void End()
        {
            Active = false;
            Target = null;
            Invalidate();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Метки кисти лежат в отдельном ассете, который раньше создавался только
        /// при запекании. Без него <see cref="Paint"/> молча выходил, и кисть
        /// выглядела нерабочей. Теперь ассет появляется при входе в режим кисти.
        /// </summary>
        public static Dyc_PaintMask EnsureMask(Dyc_DynamicCollision target)
        {
            if (target == null) return null;

            var sources = Dyc_Baker.CollectSources(target);
            int triCount = 0;
            var hashes = new List<string>();
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].mesh == null) continue;
                triCount += sources[i].mesh.triangles.Length / 3;
                hashes.Add(Dyc_PaintMask.Hash(sources[i].mesh));
            }
            if (triCount == 0) return target.PaintMask;

            var mask = Dyc_AssetIO.SavePaintMask(target, target.PaintMask,
                Dyc_AssetIO.RootFor(target), target.gameObject.name, triCount, string.Join("|", hashes));

            if (target.PaintMask == null)
            {
                Undo.RecordObject(target, "Create Collision Paint Mask");
                target.EditorSetPaintMask(mask);
                EditorUtility.SetDirty(target);
            }
            return mask;
        }

        // ------------------------------------------------------------------ проверки допуска

        /// <summary>Возвращает пустую строку, если рисовать можно; иначе — причину отказа.
        ///
        /// Проверка позы привязки убрана намеренно: кисть берёт треугольники по
        /// ТЕКУЩЕЙ позе (см. EnsureCache), а метки хранятся по номерам
        /// треугольников, поэтому поза вообще не важна. Достаточно, чтобы не
        /// шёл Play и не крутилось превью анимации.</summary>
        public static string BlockReason(Dyc_DynamicCollision target)
        {
            if (target == null) return Dyc_L10n.T("pose.notarget");
            if (Application.isPlaying) return Dyc_L10n.T("pose.noplay");
            if (AnimationMode.InAnimationMode()) return Dyc_L10n.T("pose.anim");
            return string.Empty;
        }

        public static bool CanPaint(Dyc_DynamicCollision target)
        {
            return string.IsNullOrEmpty(BlockReason(target));
        }

        /// <summary>По каждой кости сравнивает текущую мировую матрицу с bindposes[i].inverse и возвращает максимальное отклонение позиции (в метрах).</summary>
        public static float MaxBindPoseDeviation(Dyc_DynamicCollision target, out string worstBone)
        {
            worstBone = null;
            var sources = Dyc_Baker.CollectSources(target);
            float max = 0f;

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                if (src.skin == null || src.mesh == null) continue;

                var bones = src.skin.bones;
                var bindposes = src.mesh.bindposes;
                if (bones == null || bindposes == null) continue;

                int n = Mathf.Min(bones.Length, bindposes.Length);
                for (int i = 0; i < n; i++)
                {
                    if (bones[i] == null) continue;
                    Vector3 want = bindposes[i].inverse.GetColumn(3);
                    Vector3 have = bones[i].localToWorldMatrix.GetColumn(3);
                    float d = Vector3.Distance(want, have);
                    if (d > max) { max = d; worstBone = bones[i].name; }
                }
            }
            return max;
        }

        /// <summary>Из bindposes[i].inverse восстанавливает local TRS и записывает обратно. Поза привязки = исходная сетка как есть.</summary>
        public static void ResetToBindPose(Dyc_DynamicCollision target)
        {
            var sources = Dyc_Baker.CollectSources(target);
            var targets = new Dictionary<Transform, Matrix4x4>();
            var order = new List<Transform>();

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                if (src.skin == null || src.mesh == null) continue;
                var bones = src.skin.bones;
                var bindposes = src.mesh.bindposes;
                if (bones == null || bindposes == null) continue;

                int n = Mathf.Min(bones.Length, bindposes.Length);
                for (int i = 0; i < n; i++)
                {
                    if (bones[i] == null) continue;
                    targets[bones[i]] = bindposes[i].inverse;
                    if (!order.Contains(bones[i])) order.Add(bones[i]);
                }
            }

            if (order.Count == 0) return;

            var undo = new List<Object>(order.Count);
            for (int i = 0; i < order.Count; i++) undo.Add(order[i]);
            Undo.RecordObjects(undo.ToArray(), "Reset To Bind Pose");

            for (int i = 0; i < order.Count; i++)
            {
                Transform t = order[i];
                Matrix4x4 world = targets[t];

                Matrix4x4 parentWorld;
                if (t.parent == null) parentWorld = Matrix4x4.identity;
                else if (!targets.TryGetValue(t.parent, out parentWorld)) parentWorld = t.parent.localToWorldMatrix;

                Matrix4x4 local = parentWorld.inverse * world;
                t.localPosition = local.GetColumn(3);
                t.localRotation = local.rotation;
            }

            Debug.Log($"[NDC] {order.Count} костей возвращены в позу привязки.", target);
        }

        // ------------------------------------------------------------------ отрисовка

        static void OnSceneGui(SceneView view)
        {
            if (!Active || Target == null) return;

            string block = BlockReason(Target);
            DrawHud(block);

            if (!string.IsNullOrEmpty(block)) return;
            if (Event.current == null) return;

            int control = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(control);

            Event e = Event.current;

            // Кэш нужен и на Repaint: круг курсора рисуется именно там.
            // Раньше он строился только на событиях мыши, поэтому до первого
            // клика круга не было вовсе.
            EnsureCache(force: e.type == EventType.MouseDown);
            if (_verts == null) return;

            switch (e.type)
            {
                // Круг курсора обязан следовать за мышью БЕЗ нажатия кнопок,
                // поэтому на MouseMove просто просим перерисовку.
                case EventType.MouseMove:
                    view.Repaint();
                    return;

                case EventType.MouseDown:
                case EventType.MouseDrag:
                    if (e.alt) return;
                    if (!RaycastMesh(HandleUtility.GUIPointToWorldRay(e.mousePosition), out _, out Vector3 hit)) return;

                    Paint(hit, Invert || e.shift);
                    e.Use();
                    view.Repaint();
                    return;

                case EventType.ScrollWheel:
                    if (e.shift) return;
                    Radius = Mathf.Clamp(Radius - e.delta.y * 0.005f, 0.002f, 1f);
                    e.Use();
                    view.Repaint();
                    return;

                case EventType.Repaint:
                    if (RaycastMesh(HandleUtility.GUIPointToWorldRay(e.mousePosition), out _, out Vector3 cur))
                    {
                        Handles.color = new Color(1f, 0.8f, 0.2f, 0.75f);
                        Handles.DrawWireDisc(cur, view.camera.transform.forward, Radius);
                    }
                    return;
            }
        }

        static void DrawHud(string block)
        {
            Handles.BeginGUI();
            var rect = new Rect(10, 10, 330, string.IsNullOrEmpty(block) ? 74 : 96);
            GUILayout.BeginArea(rect, EditorStyles.helpBox);

            var groups = Target.Groups;
            string gname = GroupIndex >= 0 && GroupIndex < groups.Count ? groups[GroupIndex].DisplayName : "?";
            GUILayout.Label(Dyc_L10n.T("paint.hud", GroupIndex, gname, Radius * 100f), EditorStyles.boldLabel);
            GUILayout.Label(Dyc_L10n.T("paint.hudKeys", SymmetryX ? Dyc_L10n.T("paint.on") : Dyc_L10n.T("paint.off")), EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(block))
                EditorGUILayout.HelpBox(block, MessageType.Warning);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ------------------------------------------------------------------ кэш

        /// <summary>
        /// Вершины мира по текущей позе + центроиды треугольников.
        ///
        /// Всё дорогое (BakeMesh, копирование массивов) живёт в Dyc_MeshCache и
        /// пересчитывается только при смене объекта, меша или позы. Здесь
        /// добавляются только центроиды, нужные кисти для попадания по радиусу.
        /// </summary>
        static void EnsureCache(bool force)
        {
            if (!Dyc_MeshCache.Get(Target, force, out _verts, out _tris, out bool rebuilt))
            {
                _verts = null;
                _tris = null;
                _centroids = null;
                return;
            }

            if (!rebuilt && _centroids != null) return;

            _centroids = new Vector3[_tris.Length / 3];
            for (int t = 0; t < _centroids.Length; t++)
                _centroids[t] = (_verts[_tris[t * 3]] + _verts[_tris[t * 3 + 1]] + _verts[_tris[t * 3 + 2]]) / 3f;
        }

        // ------------------------------------------------------------------ луч

        static bool RaycastMesh(Ray ray, out int triangle, out Vector3 point)
        {
            triangle = -1;
            point = Vector3.zero;
            if (_tris == null) return false;

            float best = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;

            for (int t = 0; t < _centroids.Length; t++)
            {
                // сначала грубая отсечка по расстоянию до центроида, избавляет от большинства тестов треугольников
                float cd = Vector3.Dot(_centroids[t] - ray.origin, ray.direction);
                if (cd < 0f || cd > best + Radius * 2f) continue;

                Vector3 a = _verts[_tris[t * 3]];
                Vector3 b = _verts[_tris[t * 3 + 1]];
                Vector3 c = _verts[_tris[t * 3 + 2]];

                if (!RayTriangle(ray, a, b, c, out float dist, out Vector3 p)) continue;
                if (dist >= best) continue;

                if (!XRay)
                {
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                    if (Vector3.Dot(n, ray.direction) > 0f) continue; // отсечение задних граней
                }

                best = dist;
                bestPoint = p;
                triangle = t;
            }

            if (triangle < 0) return false;
            point = bestPoint;
            return true;
        }

        static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float dist, out Vector3 point)
        {
            dist = 0f;
            point = Vector3.zero;

            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;

            float inv = 1f / det;
            Vector3 tv = ray.origin - a;
            float u = Vector3.Dot(tv, p) * inv;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;

            float tHit = Vector3.Dot(e2, q) * inv;
            if (tHit < 0f) return false;

            dist = tHit;
            point = ray.origin + ray.direction * tHit;
            return true;
        }

        // ------------------------------------------------------------------ покраска

        static void Paint(Vector3 hit, bool erase)
        {
            var mask = Target.PaintMask;
            if (mask == null) mask = EnsureMask(Target);   // ассета ещё нет — создаём на месте
            if (mask == null) return;

            mask.EnsureSize(_centroids.Length);
            Undo.RecordObject(mask, erase ? "Erase Collision Label" : "Paint Collision Label");

            byte label = erase ? (byte)0 : (byte)Mathf.Clamp(GroupIndex, 0, 255);

            int painted = 0;
            painted += PaintAround(mask, hit, label, erase);

            if (SymmetryX)
            {
                // зеркалим по локальной оси X объекта
                Transform root = Target.transform;
                Vector3 local = root.worldToLocalMatrix.MultiplyPoint3x4(hit);
                local.x = -local.x;
                Vector3 mirrored = root.localToWorldMatrix.MultiplyPoint3x4(local);
                painted += PaintAround(mask, mirrored, label, erase);
            }

            if (painted > 0)
            {
                EditorUtility.SetDirty(mask);
                Dyc_GizmoDraw.InvalidateCache();
            }
        }

        static int PaintAround(Dyc_PaintMask mask, Vector3 center, byte label, bool erase)
        {
            float r2 = Radius * Radius;
            int n = 0;

            for (int t = 0; t < _centroids.Length; t++)
            {
                Vector3 c = _centroids[t];
                if ((c - center).sqrMagnitude > r2) continue;

                if (erase)
                {
                    if (!mask.IsPainted(t)) continue;
                }
                else if (mask.Get(t) == label && mask.IsPainted(t))
                {
                    continue;
                }

                mask.Mark(t, label, erase);
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------

        public static int PaintedTriangleCount(Dyc_DynamicCollision target, int group)
        {
            var mask = target != null ? target.PaintMask : null;
            if (mask == null || !mask.IsValid) return 0;
            var counts = mask.CountPaintedByLabel(target.Groups.Count);
            return group >= 0 && group < counts.Length ? counts[group] : 0;
        }

        public static void ClearAll(Dyc_DynamicCollision target)
        {
            var mask = target != null ? target.PaintMask : null;
            if (mask == null) return;
            Undo.RecordObject(mask, "Clear Collision Labels");
            mask.Clear();
            EditorUtility.SetDirty(mask);
            Dyc_GizmoDraw.InvalidateCache();
        }
    }
}
