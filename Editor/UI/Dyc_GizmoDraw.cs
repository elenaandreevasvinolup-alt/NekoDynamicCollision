using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Визуализация выпуклых оболочек в сцене. Такого нет у системных коллайдеров, точность подбирается наугад.
    ///
    /// По умолчанию рисуется **bind pose**: в bind pose bone.localToWorldMatrix · bindposes[i] = I,
    /// поэтому bind pose = исходная сетка как есть. Оболочки рисуются по bindWorld и никак не зависят от текущей анимации персонажа,
    /// так что T-pose коллайдеры стабильно видны и во время проигрывания анимации.
    /// </summary>
    [InitializeOnLoad]
    public static class Dyc_GizmoDraw
    {
        const string PrefKey = "Neko.DynamicCollision.Gizmo.";

        static bool _enabled;
        static bool _onlySelected;
        static bool _bindPose;
        static bool _drawSource;
        static bool _drawUncovered;
        static bool _labels;
        static bool _drawPainted;
        static bool _occlude;
        static int _maxHulls;
        static bool _loaded;

        static readonly List<Dyc_DynamicCollision> _cache = new List<Dyc_DynamicCollision>(16);
        static readonly Dictionary<int, DycCoverageReport> _coverageCache = new Dictionary<int, DycCoverageReport>();
        static readonly Dictionary<int, Vector3[]> _uncoveredSegCache = new Dictionary<int, Vector3[]>();

        /// <summary>Закрашенные треугольники по группам. Меши переиспользуются,
        /// чтобы не плодить утечки при каждой перерисовке сцены.</summary>
        /// <summary>Закрашенные кистью треугольники, разложенные по группам.
        /// Пересобирается ТОЛЬКО при смене версии маски — никаких таймеров.</summary>
        class PaintedCache
        {
            public int version = -1;
            public List<int>[] groupTris;
            public Vector3[] verts;
            public int[] tris;
        }

        static readonly Dictionary<int, PaintedCache> _paintedCache = new Dictionary<int, PaintedCache>();

        /// <summary>Каркас исходного меша. Раньше он собирался заново КАЖДЫЙ
        /// кадр: mesh.vertices + mesh.triangles + массив сегментов — это сотни
        /// килобайт мусора на кадр и главный источник тормозов.</summary>
        class SourceWire
        {
            public int meshId;
            public Vector3[] segs;
        }

        static readonly Dictionary<int, SourceWire> _sourceWireCache = new Dictionary<int, SourceWire>();
        const int MaxSourceWireSegments = 40000;
        static readonly Vector3[] _tri3 = new Vector3[3];

        /// <summary>Потолок на число заливаемых треугольников за кадр: заливка
        /// идёт по одному вызову на треугольник, и на десятках тысяч это
        /// начинает тормозить сцену.</summary>
        const int MaxPaintedTriangles = 6000;

        public static bool Enabled
        {
            get { Load(); return _enabled; }
            set { Load(); _enabled = value; Save(); SceneView.RepaintAll(); }
        }

        public static bool OnlySelected
        {
            get { Load(); return _onlySelected; }
            set { Load(); _onlySelected = value; Save(); SceneView.RepaintAll(); }
        }

        public static bool BindPose
        {
            get { Load(); return _bindPose; }
            set { Load(); _bindPose = value; Save(); SceneView.RepaintAll(); }
        }

        public static bool DrawSource
        {
            get { Load(); return _drawSource; }
            set { Load(); _drawSource = value; Save(); SceneView.RepaintAll(); }
        }

        public static bool DrawUncovered
        {
            get { Load(); return _drawUncovered; }
            set { Load(); _drawUncovered = value; Save(); SceneView.RepaintAll(); }
        }

        public static bool Labels
        {
            get { Load(); return _labels; }
            set { Load(); _labels = value; Save(); SceneView.RepaintAll(); }
        }

        /// <summary>Показывать закрашенные кистью треугольники. Без этого
        /// непонятно, что именно попало в метку.</summary>
        public static bool DrawPainted
        {
            get { Load(); return _drawPainted; }
            set { Load(); _drawPainted = value; Save(); SceneView.RepaintAll(); }
        }

        /// <summary>
        /// Скрывать невидимое. Включает два независимых механизма:
        ///   · рёбра, повёрнутые от камеры, не рисуются (отсечение по нормалям);
        ///   · оболочки пишут глубину в невидимом проходе, поэтому рёбра
        ///     скрываются за ДРУГИМИ оболочками и за геометрией сцены — сеткой
        ///     пола, телом персонажа.
        ///
        /// Без этого получается «проволочный клубок»: видно все рёбра всех
        /// костей сразу, и по картинке невозможно понять, что где.
        /// </summary>
        public static bool Occlude
        {
            get { Load(); return _occlude; }
            set { Load(); _occlude = value; Save(); SceneView.RepaintAll(); }
        }

        public static int MaxHulls
        {
            get { Load(); return _maxHulls; }
            set { Load(); _maxHulls = Mathf.Clamp(value, 8, 2048); Save(); SceneView.RepaintAll(); }
        }

        static Dyc_GizmoDraw()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.hierarchyChanged += () => _cache.Clear();
            Load();
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _enabled = EditorPrefs.GetBool(PrefKey + "enabled", true);
            _onlySelected = EditorPrefs.GetBool(PrefKey + "onlySelected", true);
            _bindPose = EditorPrefs.GetBool(PrefKey + "bindPose", true);
            _drawSource = EditorPrefs.GetBool(PrefKey + "source", false);
            _drawUncovered = EditorPrefs.GetBool(PrefKey + "uncovered", false);
            _labels = EditorPrefs.GetBool(PrefKey + "labels", false);
            _drawPainted = EditorPrefs.GetBool(PrefKey + "painted", true);
            _occlude = EditorPrefs.GetBool(PrefKey + "occlude", true);
            _maxHulls = EditorPrefs.GetInt(PrefKey + "maxHulls", 512);
        }

        static void Save()
        {
            EditorPrefs.SetBool(PrefKey + "enabled", _enabled);
            EditorPrefs.SetBool(PrefKey + "onlySelected", _onlySelected);
            EditorPrefs.SetBool(PrefKey + "bindPose", _bindPose);
            EditorPrefs.SetBool(PrefKey + "source", _drawSource);
            EditorPrefs.SetBool(PrefKey + "uncovered", _drawUncovered);
            EditorPrefs.SetBool(PrefKey + "labels", _labels);
            EditorPrefs.SetBool(PrefKey + "painted", _drawPainted);
            EditorPrefs.SetBool(PrefKey + "occlude", _occlude);
            EditorPrefs.SetInt(PrefKey + "maxHulls", _maxHulls);
        }

        public static void InvalidateCache()
        {
            _coverageCache.Clear();
            _uncoveredSegCache.Clear();
            foreach (var kv in _paintedCache)
                if (kv.Value != null) kv.Value.version = -1;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Выбранная щелчком грань: цель и номер оболочки. Читает окно эксперта,
        /// чтобы подсветить соответствующую кость, и сразу сбрасывает, — это
        /// разовая передача, а не состояние.
        /// </summary>
        public static Dyc_DynamicCollision PickedTarget;
        public static int PickedHullIndex = -1;

        static void OnSceneGui(SceneView view)
        {
            if (!Enabled) return;
            if (Application.isPlaying) return;

            // Щелчок по грани — ДО проверки на Repaint: событие мыши приходит
            // отдельным типом, и в ветке отрисовки его бы не увидели.
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && !Event.current.alt
                && !Event.current.shift
                && !Event.current.control
                && !Event.current.command)
            {
                if (TryPickFace(view)) Event.current.Use();
            }

            if (Event.current.type != EventType.Repaint) return;

            var targets = Gather();
            if (targets.Count == 0) return;

            // ПРОВОЛОКА В ТРИ ПРОХОДА, БЕЗ ЕДИНОЙ ЗАЛИВКИ.
            //
            // Раньше здесь был невидимый проход глубины, чтобы кости перекрывали
            // друг друга. Он же и стал источником заливки: материал с ColorMask 0
            // работает только если у шейдера есть такое свойство, а где его нет —
            // оболочки рисовались тёмными ТЕЛАМИ. Просили чистые линии, получили
            // заливку. Поэтому прохода глубины больше нет вообще: ни одного
            // вызова отрисовки треугольников, только отрезки.
            //
            // Перекрытие даётся z-тестом, а невидимое — половинной прозрачностью:
            //   · за геометрией сцены (сетка пола, тело) — половина;
            //   · обратная сторона оболочки — половина;
            //   · видимая сторона — полная.
            // Greater и LessEqual взаимно дополняют друг друга, поэтому одна и та
            // же линия не рисуется дважды.
            bool occlude = Occlude;

            Handles.zTest = CompareFunction.LessEqual;
            for (int i = 0; i < targets.Count; i++)
                DrawDecorations(targets[i]);

            Handles.zTest = CompareFunction.Greater;
            DrawEdgePass(view, targets, EdgeSide.Any, occlude ? 0.5f : 1f);

            Handles.zTest = CompareFunction.LessEqual;
            DrawEdgePass(view, targets, EdgeSide.Front, 1f);
            DrawEdgePass(view, targets, EdgeSide.Back, occlude ? 0.5f : 1f);

            Handles.zTest = CompareFunction.Always;
        }

        /// <summary>Какие рёбра брать в проходе: видимые, скрытые оболочкой или все.</summary>
        enum EdgeSide
        {
            Any,
            Front,
            Back
        }

        static void DrawEdgePass(SceneView view, List<Dyc_DynamicCollision> targets, EdgeSide side, float alpha)
        {
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                var set = target.BakedSet;
                if (set == null || set.hulls == null) continue;

                int drawn = 0;
                for (int i = 0; i < set.hulls.Count && drawn < MaxHulls; i++)
                {
                    var h = set.hulls[i];
                    if (h == null || h.edges == null || h.edges.Length < 2) continue;

                    Handles.matrix = MatrixFor(target, set, h);

                    var color = ColorFor(target, h);
                    color.a = alpha;
                    Handles.color = color;

                    DrawHullEdges(view, h, side);

                    if (Labels)
                    {
                        Vector3 wc = Handles.matrix.MultiplyPoint3x4(h.localCenter);
                        Handles.Label(wc, LabelFor(target, h));
                    }

                    drawn++;
                }
            }

            Handles.matrix = Matrix4x4.identity;
            Handles.color = Color.white;
        }

        static List<Dyc_DynamicCollision> Gather()
        {
            if (OnlySelected)
            {
                _cache.Clear();
                var sel = Selection.gameObjects;
                for (int i = 0; i < sel.Length; i++)
                {
                    var c = sel[i].GetComponent<Dyc_DynamicCollision>();
                    if (c != null) _cache.Add(c);
                }
                return _cache;
            }

            if (_cache.Count == 0)
            {
                var all = Object.FindObjectsOfType<Dyc_DynamicCollision>();
                _cache.AddRange(all);
            }
            return _cache;
        }

        /// <summary>
        /// Матрица оболочки: bindWorld в позе привязки, живая матрица точки
        /// крепления — в текущей позе. Вынесено из отрисовки, потому что тот же
        /// выбор нужен выбору грани щелчком: иначе луч считался бы не в той
        /// системе координат, и попадание не совпадало бы с картинкой.
        /// </summary>
        static Matrix4x4 MatrixFor(Dyc_DynamicCollision target, Dyc_BakedSet set, DycBakedHull h)
        {
            if (BindPose) return h.bindWorld;

            Transform bone = string.IsNullOrEmpty(h.bonePath) ? null : target.transform.Find(h.bonePath);
            return bone != null ? bone.localToWorldMatrix : target.transform.localToWorldMatrix;
        }

        /// <summary>
        /// Оформление вокруг оболочек: закраска кистью, каркас исходного меша,
        /// непокрытые грани. Сами оболочки рисуются отдельно, в проходах
        /// проволоки (DrawEdgePass) — у них своя логика прозрачности.
        /// </summary>
        static void DrawDecorations(Dyc_DynamicCollision target)
        {
            // Закрашенные кистью грани показываем и до запекания: рисовать
            // метки — это первый шаг, а не последний.
            if (DrawPainted) DrawPaintedFaces(target);

            var set = target.BakedSet;
            if (set == null || set.hulls == null || set.hulls.Count == 0) return;

            Matrix4x4 meshToWorld = MeshToWorld(target, set);

            // Каркас исходной сетки (bind pose)
            if (DrawSource) DrawSourceWire(target, meshToWorld);

            // Непокрытые треугольники
            if (DrawUncovered) DrawUncoveredTris(target, set, meshToWorld);
        }

        // ------------------------------------------------------------------ рёбра и выбор грани

        static readonly List<Vector3> _visibleSegs = new List<Vector3>(4096);

        /// <summary>
        /// Рисует рёбра оболочки, НЕ рисуя те, что повёрнуты от камеры.
        ///
        /// Как это работает без шейдеров и без проходов глубины. У выпуклой
        /// оболочки всё, что не видно, закрыто ею же самой. Значит, достаточно
        /// знать, смотрит ли на камеру хотя бы одна из двух граней, смежных с
        /// ребром: если обе смотрят в сторону, ребро закрыто телом оболочки.
        /// Нормали уже посчитаны при запекании (DycBakedHull.edgeNormals),
        /// поэтому на кадр остаётся по два скалярных произведения на ребро.
        ///
        /// Отсечение считается В ЛОКАЛЬНОМ пространстве оболочки: Handles.matrix
        /// уже задан, и переводить каждую точку в мир значило бы делать ту же
        /// работу дважды. Камера переводится в локальное один раз на оболочку.
        /// </summary>
        static void DrawHullEdges(SceneView view, DycBakedHull h, EdgeSide side)
        {
            if (h.edges == null || h.edges.Length < 2) return;

            // Старое запекание: нормалей нет — отсекать нечем. Рисуем в
            // «переднем» проходе целиком, а в заднем пропускаем, чтобы не
            // задвоить линии.
            if (h.edgeNormals == null || h.edgeNormals.Length != h.edges.Length)
            {
                if (side != EdgeSide.Back) Handles.DrawLines(h.edges);
                return;
            }

            Vector3 camLocal = Handles.matrix.inverse.MultiplyPoint3x4(view.camera.transform.position);
            if (float.IsNaN(camLocal.x) || float.IsNaN(camLocal.y) || float.IsNaN(camLocal.z))
            {
                if (side != EdgeSide.Back) Handles.DrawLines(h.edges);
                return;
            }

            _visibleSegs.Clear();
            for (int i = 0; i + 1 < h.edges.Length; i += 2)
            {
                Vector3 a = h.edges[i];
                Vector3 b = h.edges[i + 1];
                Vector3 toCam = camLocal - (a + b) * 0.5f;

                Vector3 n0 = h.edgeNormals[i];
                Vector3 n1 = h.edgeNormals[i + 1];

                // Нулевая нормаль — край поверхности, он виден всегда.
                bool front = n0.sqrMagnitude < 0.5f || n1.sqrMagnitude < 0.5f
                             || Vector3.Dot(n0, toCam) > 0f
                             || Vector3.Dot(n1, toCam) > 0f;

                if (side == EdgeSide.Front && !front) continue;
                if (side == EdgeSide.Back && front) continue;

                _visibleSegs.Add(a);
                _visibleSegs.Add(b);
            }

            if (_visibleSegs.Count > 0) Handles.DrawLines(_visibleSegs.ToArray());
        }

        /// <summary>
        /// Выбор грани щелчком. Луч строится из камеры сцены через курсор и
        /// проверяется по треугольникам запечённых оболочек в их собственном
        /// пространстве — так попадание совпадает с тем, что нарисовано, при
        /// любой позе и любом масштабе.
        /// </summary>
        static bool TryPickFace(SceneView view)
        {
            var targets = Gather();
            if (targets.Count == 0) return false;

            float bestWorld = float.MaxValue;
            Dyc_DynamicCollision bestTarget = null;
            int bestIndex = -1;

            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                var set = target.BakedSet;
                if (set == null || set.hulls == null) continue;

                for (int i = 0; i < set.hulls.Count && i < MaxHulls; i++)
                {
                    var h = set.hulls[i];
                    if (h == null || h.mesh == null) continue;

                    Matrix4x4 m = MatrixFor(target, set, h);
                    if (!RayHull(view, m, h, out float world)) continue;
                    if (world >= bestWorld) continue;

                    bestWorld = world;
                    bestTarget = target;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return false;

            PickedTarget = bestTarget;
            PickedHullIndex = bestIndex;
            Selection.activeGameObject = bestTarget.gameObject;
            return true;
        }

        static bool RayHull(SceneView view, Matrix4x4 m, DycBakedHull h, out float worldDistance)
        {
            worldDistance = 0f;

            var cam = view.camera;
            if (cam == null) return false;

            Vector2 p = Event.current.mousePosition;
            // GUI отсчитывает Y сверху, вьюпорт камеры — снизу.
            var vp = new Vector3(p.x / Mathf.Max(1f, view.position.width),
                                 1f - p.y / Mathf.Max(1f, view.position.height), 0f);
            Ray ray = cam.ViewportPointToRay(vp);

            Matrix4x4 inv = m.inverse;
            Vector3 o = inv.MultiplyPoint3x4(ray.origin);
            Vector3 d = inv.MultiplyVector(ray.direction);

            var verts = h.mesh.vertices;
            var tris = h.mesh.triangles;
            if (verts == null || tris == null) return false;

            float bestT = float.MaxValue;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;
                if (!RayTriangle(o, d, verts[i0], verts[i1], verts[i2], out float t)) continue;
                if (t >= bestT) continue;
                bestT = t;
            }

            if (bestT == float.MaxValue) return false;

            Vector3 hitWorld = m.MultiplyPoint3x4(o + d * bestT);
            worldDistance = (hitWorld - ray.origin).magnitude;
            return true;
        }

        /// <summary>Пересечение луча с треугольником (Мёллер–Трумбор).</summary>
        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;

            float inv = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, p) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;

            Vector3 q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;

            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-6f;
        }

        static Color ColorFor(Dyc_DynamicCollision target, DycBakedHull h)
        {
            // Красным помечается ТОЛЬКО выпуклый коллайдер: у невыпуклой сетки
            // потолка 255 нет, и подсветка была бы ложной тревогой.
            if (!h.nonConvex && h.vertexCount > Dyc_Cluster.PhysXMaxHullVertices)
                return new Color(1f, 0.25f, 0.25f, 1f);   // Превышен лимит PhysX — красное предупреждение

            var groups = target.Groups;
            if (groups == null || h.groupIndex < 0 || h.groupIndex >= groups.Count)
                return new Color(0.35f, 0.85f, 1f, 0.9f);

            return GroupColor(h.groupIndex);
        }

        static readonly Color[] Palette =
        {
            new Color(0.35f, 0.85f, 1f, 0.9f),
            new Color(1f, 0.65f, 0.25f, 0.9f),
            new Color(0.55f, 0.95f, 0.45f, 0.9f),
            new Color(0.95f, 0.45f, 0.85f, 0.9f),
            new Color(0.95f, 0.9f, 0.35f, 0.9f),
            new Color(0.55f, 0.6f, 1f, 0.9f),
            new Color(0.9f, 0.45f, 0.45f, 0.9f),
            new Color(0.45f, 0.9f, 0.8f, 0.9f),
        };

        public static Color GroupColor(int groupIndex)
        {
            return Palette[Mathf.Abs(groupIndex) % Palette.Length];
        }

        static string LabelFor(Dyc_DynamicCollision target, DycBakedHull h)
        {
            string el = "?";
            if (h.elementIndex >= 0 && h.elementIndex < target.Elements.Count && target.Elements[h.elementIndex] != null)
                el = target.Elements[h.elementIndex].DisplayName;

            string gr = "?";
            if (h.groupIndex >= 0 && h.groupIndex < target.Groups.Count && target.Groups[h.groupIndex] != null)
                gr = target.Groups[h.groupIndex].DisplayName;

            return $"{el} / {gr}\nv{h.vertexCount}  {h.volume:F4}m³";
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Пространство сетки в bind pose → мир.
        /// По разнице между текущим положением корневой кости и положением при биндинге T-pose коллайдеры следуют за персонажем,
        /// но форма всегда остаётся в bind pose (на анимацию не реагирует).
        /// </summary>
        public static Matrix4x4 MeshToWorld(Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            if (set == null) return target.transform.localToWorldMatrix;

            if (target.Mode == DycMode.Mesh)
                return target.transform.localToWorldMatrix;

            if (string.IsNullOrEmpty(set.bindRootBonePath))
                return Matrix4x4.identity;

            Transform root = string.IsNullOrEmpty(set.bindRootBonePath)
                ? null
                : target.transform.Find(set.bindRootBonePath);

            if (root == null) return Matrix4x4.identity;

            return root.localToWorldMatrix * set.bindRootWorld.inverse;
        }

        /// <summary>
        /// Заливает треугольники, помеченные кистью, цветом их группы.
        ///
        /// Вершины берутся из общего Dyc_MeshCache — того же, что читает кисть,
        /// поэтому подсветка гарантированно совпадает с тем, что помечено.
        ///
        /// Рисуется через Handles.DrawAAConvexPolygon, а НЕ через Gizmos.DrawMesh:
        /// Gizmos внутри SceneView.duringSceneGui не рисуется, из-за чего
        /// закраска и «не появлялась». Списки треугольников пересобираются
        /// только при смене версии маски, а не по таймеру.
        /// </summary>
        static void DrawPaintedFaces(Dyc_DynamicCollision target)
        {
            var mask = target.PaintMask;
            if (mask == null || !mask.IsValid) return;

            int id = target.GetInstanceID();
            if (!_paintedCache.TryGetValue(id, out var cache))
            {
                cache = new PaintedCache();
                _paintedCache[id] = cache;
            }

            int groupCount = Mathf.Max(1, target.Groups.Count);

            // Массивы вершин запрашиваем каждый кадр — это лишь поиск в словаре
            // плюс дешёвая проверка отпечатка. Списки групп пересобираются
            // только при смене версии маски.
            if (!Dyc_MeshCache.Get(target, false, out var verts, out var tris, out _)) return;
            cache.verts = verts;
            cache.tris = tris;

            if (cache.version != mask.version)
            {
                cache.version = mask.version;

                if (cache.groupTris == null || cache.groupTris.Length != groupCount)
                {
                    cache.groupTris = new List<int>[groupCount];
                    for (int g = 0; g < groupCount; g++) cache.groupTris[g] = new List<int>(256);
                }
                else
                {
                    for (int g = 0; g < groupCount; g++) cache.groupTris[g].Clear();
                }

                int triCount = tris.Length / 3;
                for (int t = 0; t < triCount; t++)
                {
                    if (!mask.IsPainted(t)) continue;
                    int g = mask.Get(t);
                    if (g < 0 || g >= groupCount) g = 0;
                    cache.groupTris[g].Add(t);
                }
            }

            if (cache.verts == null) return;

            Handles.matrix = Matrix4x4.identity;
            int drawn = 0;

            for (int g = 0; g < cache.groupTris.Length; g++)
            {
                var list = cache.groupTris[g];
                if (list.Count == 0) continue;

                Color c = GroupColor(g);
                c.a = 0.42f;
                Handles.color = c;

                for (int i = 0; i < list.Count && drawn < MaxPaintedTriangles; i++, drawn++)
                {
                    int t = list[i];
                    _tri3[0] = cache.verts[cache.tris[t * 3]];
                    _tri3[1] = cache.verts[cache.tris[t * 3 + 1]];
                    _tri3[2] = cache.verts[cache.tris[t * 3 + 2]];
                    Handles.DrawAAConvexPolygon(_tri3);
                }
            }

            Handles.color = Color.white;
        }

        /// <summary>Каркас исходного меша строится один раз и переиспользуется.
        /// Раньше он собирался каждый кадр — это и вешало сцену.</summary>
        static void DrawSourceWire(Dyc_DynamicCollision target, Matrix4x4 meshToWorld)
        {
            var sources0 = Dyc_Baker.CollectSources(target);
            int meshId = sources0.Count > 0 && sources0[0].mesh != null ? sources0[0].mesh.GetInstanceID() : 0;

            int id = target.GetInstanceID();
            if (!_sourceWireCache.TryGetValue(id, out var entry) || entry.meshId != meshId)
            {
                var sources = sources0;
                var list = new List<Vector3>(8192);

                for (int s = 0; s < sources.Count; s++)
                {
                    var mesh = sources[s].mesh;
                    if (mesh == null) continue;

                    var verts = mesh.vertices;
                    var tris = mesh.triangles;
                    if (tris.Length / 2 > MaxSourceWireSegments) continue;

                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                        list.Add(a); list.Add(b);
                        list.Add(b); list.Add(c);
                        list.Add(c); list.Add(a);
                    }
                }

                entry = new SourceWire { meshId = meshId, segs = list.ToArray() };
                _sourceWireCache[id] = entry;
            }

            var segs = entry.segs;
            if (segs.Length == 0) return;

            Handles.color = new Color(0.6f, 0.6f, 0.6f, 0.12f);
            Handles.matrix = meshToWorld;
            Handles.DrawLines(segs);
            Handles.matrix = Matrix4x4.identity;
        }

        static void DrawUncoveredTris(Dyc_DynamicCollision target, Dyc_BakedSet set, Matrix4x4 meshToWorld)
        {
            int id = target.GetInstanceID();
            if (!_coverageCache.TryGetValue(id, out var report))
            {
                report = Dyc_Coverage.Analyze(target, set);
                _coverageCache[id] = report;
            }
            if (!report.ok || report.uncovered == null) return;

            if (!_uncoveredSegCache.TryGetValue(id, out var segs))
            {
                var sources = Dyc_Baker.CollectSources(target);
                var list = new List<Vector3>(report.uncovered.Length * 6);

                // Номера треугольников в отчёте покрытия — глобальные, из склейки всех исходных сеток подряд
                int offset = 0;
                for (int s = 0; s < sources.Count; s++)
                {
                    var mesh = sources[s].mesh;
                    if (mesh == null) continue;

                    var verts = mesh.vertices;
                    var tris = mesh.triangles;
                    int localCount = tris.Length / 3;

                    for (int i = 0; i < report.uncovered.Length; i++)
                    {
                        int g = report.uncovered[i];
                        int local = g - offset;
                        if (local < 0 || local >= localCount) continue;

                        Vector3 a = verts[tris[local * 3]];
                        Vector3 b = verts[tris[local * 3 + 1]];
                        Vector3 c = verts[tris[local * 3 + 2]];
                        list.Add(a); list.Add(b);
                        list.Add(b); list.Add(c);
                        list.Add(c); list.Add(a);
                    }

                    offset += localCount;
                }

                segs = list.ToArray();
                _uncoveredSegCache[id] = segs;
            }

            Handles.color = new Color(1f, 0.2f, 0.2f, 0.55f);
            Handles.matrix = meshToWorld;
            Handles.DrawLines(segs);
            Handles.matrix = Matrix4x4.identity;
        }
    }
}
