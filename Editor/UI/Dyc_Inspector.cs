using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Полу-чёрный ящик Inspector: в повседневной работе только 4 контрола и кнопка запекания.
    /// Нужны тонкости — нажми «Открыть запекатель», там полный набор функций.
    /// </summary>
    [CustomEditor(typeof(Dyc_DynamicCollision))]
    [CanEditMultipleObjects]
    public class Dyc_Inspector : Editor
    {
        Dyc_DynamicCollision T => (Dyc_DynamicCollision)target;

        // Свёрнутость блока тонкостей следует за режимом: в простом он
        // закрыт, в экспертном открыт. Пользователь всё равно может
        // раскрыть его вручную — режим задаёт начальное состояние, а не запрет.
        bool _advanced = Dyc_Expert.Enabled;
        DycHealthReport _health;
        double _healthTime;

        public override void OnInspectorGUI()
        {
            var t = T;
            if (t == null) return;

            DrawBrand();

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox(Dyc_L10n.T("insp.multi"), MessageType.Info);
                if (GUILayout.Button(Dyc_L10n.T("btn.batchBake"), GUILayout.Height(24)))
                    Dyc_Menu.BakeSelected();
                return;
            }

            Undo.RecordObject(t, "DYC Inspector");

            // Переключатель режима — САМЫЙ первый контрол. Простой режим — это
            // «запеки и иди играть»: четыре контрола и свёрнутый блок тонкостей.
            // Экспертный раскрывает техническую сторону, но не меняет состав
            // настроек — расхождение между двумя экранами невозможно.
            bool expert = EditorGUILayout.ToggleLeft(
                Dyc_L10n.T("insp.expertMode"), Dyc_Expert.Enabled);
            if (expert != Dyc_Expert.Enabled)
            {
                Dyc_Expert.Enabled = expert;
                _advanced = expert;
                GUI.changed = true;
            }
            EditorGUILayout.Space(4);

            DrawCore(t);
            EditorGUILayout.Space(4);
            DrawStatus(t);
            EditorGUILayout.Space(4);
            DrawActions(t);
            EditorGUILayout.Space(4);
            DrawAdvanced(t);

            if (GUI.changed)
            {
                EditorUtility.SetDirty(t);
                if (t.gameObject.scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }

        void DrawBrand()
        {
            var rect = EditorGUILayout.GetControlRect(false, 22);
            var prev = GUI.color;
            GUI.color = new Color(Dyc_Style.Accent.r, Dyc_Style.Accent.g, Dyc_Style.Accent.b, 0.16f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = prev;

            var label = new Rect(rect.x + 8, rect.y, rect.width - 8, rect.height);
            GUI.Label(label, "Dynamic Collision", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft });
        }

        void DrawCore(Dyc_DynamicCollision t)
        {
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionBasic"), EditorStyles.boldLabel);

            var mode = (DycMode)EditorGUILayout.EnumPopup("Mode", t.Mode);
            if (mode != t.Mode)
            {
                t.EditMode = mode;
                GUI.changed = true;
            }

            if (t.Mode == DycMode.Skin)
            {
                var skin = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source", t.SourceSkin, typeof(SkinnedMeshRenderer), true);
                if (skin != t.SourceSkin) { t.EditSkin = skin; GUI.changed = true; }
            }
            else
            {
                var mf = (MeshFilter)EditorGUILayout.ObjectField("Source", t.SourceMesh, typeof(MeshFilter), true);
                if (mf != t.SourceMesh) { t.EditMesh = mf; GUI.changed = true; }
            }

            var prec = (DycPrecision)EditorGUILayout.EnumPopup("Precision", t.Precision);
            if (prec != t.Precision) { t.EditPrecision = prec; GUI.changed = true; }

            var info = t.PrecisionInfo;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                $"{info.trisPerHull} tris/hull · {info.hullsPerPart} hull/part · seam {info.seamOverlap * 1000f:F1}mm",
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionCollision"), EditorStyles.boldLabel);

            var role = (DycColliderRole)EditorGUILayout.EnumPopup("Role", t.Role);
            if (role != t.Role) { t.EditRole = role; GUI.changed = true; }

            var shape = (DycColliderShape)EditorGUILayout.EnumPopup(Dyc_L10n.T("lbl.shape"), t.ColliderShape);
            if (shape != t.ColliderShape) { t.EditColliderShape = shape; GUI.changed = true; }
            EditorGUILayout.LabelField(
                Dyc_L10n.T(shape == DycColliderShape.Concave ? "shape.concaveHint" : "shape.convexHint"),
                WrapHint());

            // ВЫПУКЛАЯ ФОРМА: точность разложения.
            //
            // Слайдер и числовое поле — два вида ОДНОГО числа (размера вокселя),
            // поэтому они совпадают всегда: значение хранится только в
            // DycDecomposeSettings, а оба контрола его читают и пишут. Правка
            // поля двигает слайдер, правка слайдера — поле.
            if (shape == DycColliderShape.Convex)
            {
                float detail = EditorGUILayout.Slider(Dyc_L10n.T("lbl.decomposeDetail"), t.DecomposeDetail, 0f, 1f);
                if (!Mathf.Approximately(detail, t.DecomposeDetail)) { t.EditDecomposeDetail = detail; GUI.changed = true; }

                int mm = EditorGUILayout.IntField(Dyc_L10n.T("lbl.decomposeVoxel"), t.DecomposeVoxelMm);
                if (mm != t.DecomposeVoxelMm) { t.EditDecomposeVoxelMm = mm; GUI.changed = true; }

                EditorGUILayout.LabelField(
                    t.DecomposeDetail >= 0.999f
                        ? Dyc_L10n.T("decomp.fineHint")
                        : Dyc_L10n.T("decomp.hint", t.DecomposeVoxelMm),
                    WrapHint());
            }

            // Невыпуклая сетка на подвижном теле не даёт ошибки — Unity просто
            // молча отказывает в коллайдере. Поэтому предупреждение стоит прямо
            // под переключателем, до запекания.
            if (shape == DycColliderShape.Concave)
            {
                EditorGUILayout.HelpBox(Dyc_L10n.T("shape.concaveWarn"), MessageType.Warning);

                // Слайдер точности. Одно число вместо «сколько треугольников
                // на кусок»: последнее невозможно выбрать осмысленно, не зная
                // размеров меша. Крайнее правое положение — «не упрощать».
                float detail = EditorGUILayout.Slider(Dyc_L10n.T("lbl.concaveDetail"), t.ConcaveDetail, 0f, 1f);
                if (!Mathf.Approximately(detail, t.ConcaveDetail)) { t.EditConcaveDetail = detail; GUI.changed = true; }

                EditorGUILayout.LabelField(
                    t.ConcaveExact
                        ? Dyc_L10n.T("concave.exact")
                        : Dyc_L10n.T("concave.budget", t.ConcaveTriangleBudget),
                    WrapHint());

                if (GUILayout.Button(Dyc_L10n.T("btn.openExpert")))
                    Dyc_ExpertWindow.Open(t);
            }

            var layers = EditorGUILayout.MaskField("Interact Layers", t.EditIncludeLayers, LayerNames());
            if (layers != t.EditIncludeLayers.value) { t.EditIncludeLayers = layers; GUI.changed = true; }

            var detectRow = EditorGUILayout.GetControlRect();
            var half = detectRow.width * 0.5f;
            var c1 = new Rect(detectRow.x, detectRow.y, half - 2, detectRow.height);
            var c2 = new Rect(detectRow.x + half + 2, detectRow.y, half - 2, detectRow.height);
            var dc = EditorGUI.ToggleLeft(c1, "  Collision", t.EditDetectCollisions);
            var dt = EditorGUI.ToggleLeft(c2, "  Trigger (Poll)", t.EditDetectTriggers);
            if (dc != t.EditDetectCollisions) { t.EditDetectCollisions = dc; GUI.changed = true; }
            if (dt != t.EditDetectTriggers)
            {
                t.EditDetectTriggers = dt;
                t.EditTriggerMode = dt ? DycTriggerMode.Poll : DycTriggerMode.Off;
                GUI.changed = true;
            }
        }

        void DrawStatus(Dyc_DynamicCollision t)
        {
            var set = t.BakedSet;
            var card = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (set == null || set.hulls.Count == 0)
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.noBake"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.noBakeHint"), EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.baked", set.hulls.Count), EditorStyles.boldLabel);

                int peak = set.MaxHullVertices;
                bool convex = set.colliderShape == DycColliderShape.Convex;
                var peakColor = convex && peak > Dyc_Cluster.PhysXMaxHullVertices ? Dyc_Style.Error : Dyc_Style.Ok;
                DrawKV(Dyc_L10n.T("insp.peakVerts"), $"{set.TotalVertexCount} / {peak}",
                    convex && peak > 255 ? peakColor : Dyc_Style.Text);
                DrawKV(Dyc_L10n.T("insp.volume"), $"{set.TotalVolume:F4} m³");
                DrawKV(Dyc_L10n.T("insp.partsGroups"), $"{t.Elements.Count} / {t.Groups.Count}");

                if (set.unassignedTriangles > 0)
                    DrawKV(Dyc_L10n.T("insp.unassigned"), set.unassignedTriangles.ToString(), Dyc_Style.Warn);
            }

            // Оценка состояния (кэш 1 секунда, не гонять каждый кадр)
            if (EditorApplication.timeSinceStartup - _healthTime > 1.0)
            {
                _health = Dyc_Health.Analyze(t);
                _healthTime = EditorApplication.timeSinceStartup;
            }

            Color scoreColor = _health.score >= 85 ? Dyc_Style.Ok : _health.score >= 60 ? Dyc_Style.Warn : Dyc_Style.Error;
            DrawKV(Dyc_L10n.T("insp.health"), $"{_health.score}/100  ({_health.Grade})", scoreColor);
            if (_health.errors > 0 || _health.warnings > 0)
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.tally", _health.errors, _health.warnings, _health.hints), EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        static string[] _layerNames;

        static GUIStyle _hint;

        /// <summary>Подпись-пояснение: обычный miniLabel не переносится и обрезается по краю окна.</summary>
        static GUIStyle WrapHint()
        {
            if (_hint == null)
                _hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            return _hint;
        }

        static string[] LayerNames()
        {
            if (_layerNames != null) return _layerNames;
            _layerNames = new string[32];
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                _layerNames[i] = string.IsNullOrEmpty(n) ? ("Layer " + i) : n;
            }
            return _layerNames;
        }

        static void DrawKV(string k, string v, Color? color = null)        {
            var row = EditorGUILayout.GetControlRect(false, 16);
            var kRect = new Rect(row.x, row.y, row.width * 0.42f, row.height);
            var vRect = new Rect(row.x + row.width * 0.42f, row.y, row.width * 0.58f, row.height);

            EditorGUI.LabelField(kRect, k, EditorStyles.miniLabel);
            var prev = GUI.contentColor;
            if (color.HasValue) GUI.contentColor = color.Value;
            EditorGUI.LabelField(vRect, v, EditorStyles.miniLabel);
            GUI.contentColor = prev;
        }

        void DrawActions(Dyc_DynamicCollision t)
        {
            var row = EditorGUILayout.GetControlRect(false, 24);
            float w = (row.width - 6) / 2f;

            if (GUI.Button(new Rect(row.x, row.y, w, row.height), Dyc_L10n.T("btn.bake"), EditorStyles.miniButtonLeft))
            {
                Dyc_Menu.Bake(t);
                _healthTime = 0;
            }

            if (GUI.Button(new Rect(row.x + w + 6, row.y, w, row.height), Dyc_L10n.T("insp.openBaker"), EditorStyles.miniButtonRight))
                Dyc_Window.Open();

            var row2 = EditorGUILayout.GetControlRect(false, 20);
            float w2 = (row2.width - 6) / 2f;

            if (GUI.Button(new Rect(row2.x, row2.y, w2, row2.height), Dyc_L10n.T("btn.autofill"), EditorStyles.miniButtonLeft))
            {
                Dyc_Menu.AutoFillElements(t);
                _healthTime = 0;
            }

            bool gizmo = Dyc_GizmoDraw.Enabled;
            bool next = GUI.Toggle(new Rect(row2.x + w2 + 6, row2.y, w2, row2.height), gizmo, Dyc_L10n.T("insp.showGizmo"));
            if (next != gizmo) Dyc_GizmoDraw.Enabled = next;
        }

        void DrawAdvanced(Dyc_DynamicCollision t)
        {
            _advanced = EditorGUILayout.Foldout(_advanced, "Advanced", true);
            if (!_advanced) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionPhysics"), EditorStyles.boldLabel);

            bool autoRb = EditorGUILayout.Toggle(Dyc_L10n.T("insp.autoRb"), t.AutoRigidbody);
            if (autoRb != t.AutoRigidbody) { t.EditAutoRigidbody = autoRb; GUI.changed = true; }

            var self = (DycSelfCollision)EditorGUILayout.EnumPopup(Dyc_L10n.T("insp.selfCollision"), t.SelfCollision);
            if (self != t.SelfCollision) { t.EditSelfCollisionMode = self; GUI.changed = true; }

            bool autoMass = EditorGUILayout.Toggle(Dyc_L10n.T("insp.autoMass"), t.AutoMassFromDensity);
            if (autoMass != t.AutoMassFromDensity) { t.EditAutoMass = autoMass; GUI.changed = true; }

            if (t.AutoMassFromDensity)
            {
                EditorGUI.indentLevel++;
                float target = EditorGUILayout.FloatField(Dyc_L10n.T("insp.targetMass"), t.TargetTotalMass);
                if (!Mathf.Approximately(target, t.TargetTotalMass)) { t.EditTargetTotalMass = Mathf.Max(0f, target); GUI.changed = true; }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            DrawExclusions(t);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Trigger", EditorStyles.boldLabel);

            var tm = (DycTriggerMode)EditorGUILayout.EnumPopup(Dyc_L10n.T("insp.triggerMode"), t.EditTriggerMode);
            if (tm != t.EditTriggerMode) { t.EditTriggerMode = tm; GUI.changed = true; }

            if (t.EditTriggerMode == DycTriggerMode.Poll)
            {
                EditorGUI.indentLevel++;
                int div = EditorGUILayout.IntSlider(Dyc_L10n.T("insp.pollDivisor"), t.EditPollDivisor, 1, 16);
                if (div != t.EditPollDivisor) { t.EditPollDivisor = div; GUI.changed = true; }

                float sweep = EditorGUILayout.FloatField(Dyc_L10n.T("insp.pollSweep"), t.EditPollSweepMargin);
                if (!Mathf.Approximately(sweep, t.EditPollSweepMargin)) { t.EditPollSweepMargin = Mathf.Max(0f, sweep); GUI.changed = true; }

                EditorGUILayout.LabelField(Dyc_L10n.T("insp.pollNote"), EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("LOD", EditorStyles.boldLabel);

            var lod = (DycLodMode)EditorGUILayout.EnumPopup(Dyc_L10n.T("insp.lodMode"), t.EditLodMode);
            if (lod != t.EditLodMode) { t.EditLodMode = lod; GUI.changed = true; }

            if (t.EditLodMode != DycLodMode.Off)
            {
                EditorGUI.indentLevel++;
                float dist = EditorGUILayout.FloatField(Dyc_L10n.T("insp.lodDistance"), t.EditLodDistance);
                if (!Mathf.Approximately(dist, t.EditLodDistance)) { t.EditLodDistance = Mathf.Max(1f, dist); GUI.changed = true; }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            bool dbg = EditorGUILayout.Toggle(Dyc_L10n.T("insp.debugLog"), t.EditDebugLog);
            if (dbg != t.EditDebugLog) { t.EditDebugLog = dbg; GUI.changed = true; }

            if (GUILayout.Button(Dyc_L10n.T("insp.rebuild")))
                Dyc_Menu.RebuildRuntime(t);

            // ---- Продвинутые настройки: всё, что не нужно обычному использованию.
            DrawParity(t);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Продвинутый блок: обычное использование, живое
        /// обновление, материалы по исходным материалам, карта исключений,
        /// чужой скелет.
        ///
        /// Всё это лежит в DycAdvancedSettings, а не в полях компонента:
        /// обычный сценарий не должен тонуть в опциях, нужных только для
        /// режима точного обновления.
        /// </summary>
        void DrawParity(Dyc_DynamicCollision t)
        {
            var adv = t.Advanced;

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionSimple"), EditorStyles.boldLabel);

            bool gen = EditorGUILayout.Toggle(Dyc_L10n.T("insp.generateOnStart"), adv.generateOnStart);
            if (gen != adv.generateOnStart) { adv.generateOnStart = gen; GUI.changed = true; }

            if (adv.generateOnStart)
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.generateHint"), WrapHint());

            bool trig = EditorGUILayout.Toggle(Dyc_L10n.T("insp.isTrigger"), adv.isTrigger);
            if (trig != adv.isTrigger) { adv.isTrigger = trig; GUI.changed = true; }

            var recv = (Transform)EditorGUILayout.ObjectField(
                Dyc_L10n.T("insp.receiver"), adv.collisionReceiver, typeof(Transform), true);
            if (recv != adv.collisionReceiver) { adv.collisionReceiver = recv; GUI.changed = true; }
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.receiverHint"), WrapHint());

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionLive"), EditorStyles.boldLabel);

            bool live = EditorGUILayout.Toggle(Dyc_L10n.T("insp.liveUpdate"), adv.liveUpdate);
            if (live != adv.liveUpdate) { adv.liveUpdate = live; GUI.changed = true; }

            if (adv.liveUpdate)
            {
                EditorGUI.indentLevel++;

                bool cont = EditorGUILayout.Toggle(Dyc_L10n.T("insp.liveContinuous"), adv.liveUpdateContinuous);
                if (cont != adv.liveUpdateContinuous) { adv.liveUpdateContinuous = cont; GUI.changed = true; }

                float idle = EditorGUILayout.Slider(Dyc_L10n.T("insp.idleBudget"),
                    (float)adv.idleCpuBudgetMs, 0.05f, 4f);
                if (!Mathf.Approximately(idle, (float)adv.idleCpuBudgetMs)) { adv.idleCpuBudgetMs = idle; GUI.changed = true; }

                float active = EditorGUILayout.Slider(Dyc_L10n.T("insp.activeBudget"),
                    (float)adv.activeCpuBudgetMs, 0.05f, 8f);
                if (!Mathf.Approximately(active, (float)adv.activeCpuBudgetMs)) { adv.activeCpuBudgetMs = active; GUI.changed = true; }

                float thr = EditorGUILayout.Slider(Dyc_L10n.T("insp.updateThreshold"), adv.meshUpdateThreshold, 0f, 1f);
                if (!Mathf.Approximately(thr, adv.meshUpdateThreshold)) { adv.meshUpdateThreshold = thr; GUI.changed = true; }

                int maxTri = EditorGUILayout.IntSlider(Dyc_L10n.T("insp.maxColliderTriangles"),
                    adv.maxColliderTriangles, 50, 5000);
                if (maxTri != adv.maxColliderTriangles) { adv.maxColliderTriangles = maxTri; GUI.changed = true; }

                EditorGUILayout.LabelField(Dyc_L10n.T("insp.liveHint"), WrapHint());
                EditorGUI.indentLevel--;
            }

            // ---- LOD столкновений персонажа
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionCharLod"), EditorStyles.boldLabel);

            bool lod = EditorGUILayout.Toggle(Dyc_L10n.T("insp.collisionLod"), adv.collisionLod);
            if (lod != adv.collisionLod) { adv.collisionLod = lod; GUI.changed = true; }

            if (adv.collisionLod)
            {
                EditorGUI.indentLevel++;

                float near = EditorGUILayout.Slider(Dyc_L10n.T("insp.lodNear"), adv.lodNearDistance, 1f, 200f);
                if (!Mathf.Approximately(near, adv.lodNearDistance)) { adv.lodNearDistance = near; GUI.changed = true; }

                float farD = EditorGUILayout.Slider(Dyc_L10n.T("insp.lodFar"), adv.lodFarDistance, 1f, 400f);
                if (!Mathf.Approximately(farD, adv.lodFarDistance)) { adv.lodFarDistance = farD; GUI.changed = true; }

                int keep = EditorGUILayout.IntSlider(Dyc_L10n.T("insp.lodKeep"), adv.lodFarHullCount, 1, 64);
                if (keep != adv.lodFarHullCount) { adv.lodFarHullCount = keep; GUI.changed = true; }

                float hyst = EditorGUILayout.Slider(Dyc_L10n.T("insp.lodHysteresis"), adv.lodHysteresis, 0f, 5f);
                if (!Mathf.Approximately(hyst, adv.lodHysteresis)) { adv.lodHysteresis = hyst; GUI.changed = true; }

                bool vis = EditorGUILayout.Toggle(Dyc_L10n.T("insp.lodVisibility"), adv.lodRequireVisibility);
                if (vis != adv.lodRequireVisibility) { adv.lodRequireVisibility = vis; GUI.changed = true; }

                var refT = (Transform)EditorGUILayout.ObjectField(
                    Dyc_L10n.T("insp.lodReference"), adv.lodReference, typeof(Transform), true);
                if (refT != adv.lodReference) { adv.lodReference = refT; GUI.changed = true; }

                EditorGUILayout.LabelField(Dyc_L10n.T("insp.lodHint"), WrapHint());
                EditorGUI.indentLevel--;
            }

            // ТЕХНИЧЕСКАЯ ЧАСТЬ: материалы по исходным материалам, карта
            // исключений и чужой скелет. Это тонкая настройка на конкретный
            // пайплайн, а не то, что нужно при первом запекании, поэтому в
            // простом режиме она не показывается вовсе — но и не теряется:
            // переключатель режима стоит вверху инспектора.
            if (!Dyc_Expert.Enabled) return;

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionMaterials"), EditorStyles.boldLabel);
            DrawMaterialAssociations(t);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionExclusionMap"), EditorStyles.boldLabel);

            var map = (Texture2D)EditorGUILayout.ObjectField(
                Dyc_L10n.T("insp.exclusionMap"), adv.exclusionMap, typeof(Texture2D), false);
            if (map != adv.exclusionMap) { adv.exclusionMap = map; GUI.changed = true; }

            if (adv.exclusionMap != null)
            {
                EditorGUI.indentLevel++;

                int ch = EditorGUILayout.IntPopup(Dyc_L10n.T("insp.exclusionChannel"),
                    adv.exclusionMapChannel, new[] { "R", "G", "B", "A" }, new[] { 0, 1, 2, 3 });
                if (ch != adv.exclusionMapChannel) { adv.exclusionMapChannel = ch; GUI.changed = true; }

                float eth = EditorGUILayout.Slider(Dyc_L10n.T("insp.exclusionThreshold"),
                    adv.exclusionMapThreshold, 0f, 1f);
                if (!Mathf.Approximately(eth, adv.exclusionMapThreshold)) { adv.exclusionMapThreshold = eth; GUI.changed = true; }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.LabelField(Dyc_L10n.T("insp.exclusionMapHint"), WrapHint());

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionSkeleton"), EditorStyles.boldLabel);

            var retarget = (Transform)EditorGUILayout.ObjectField(
                Dyc_L10n.T("insp.retargetRoot"), adv.retargetRoot, typeof(Transform), true);
            if (retarget != adv.retargetRoot) { adv.retargetRoot = retarget; GUI.changed = true; }
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.retargetHint"), WrapHint());

            if (GUI.changed) EditorUtility.SetDirty(t);
        }

        /// <summary>Пары «материал меша → физический материал». Строки, как и
        /// у исключений: ReorderableList ломает раскладку в свёрнутом блоке.</summary>
        void DrawMaterialAssociations(Dyc_DynamicCollision t)
        {
            var list = t.Advanced.materialAssociations;
            if (list == null) { list = new List<DycMaterialAssociation>(); t.Advanced.materialAssociations = list; }

            EditorGUILayout.LabelField(Dyc_L10n.T("insp.materialSource") + " → " + Dyc_L10n.T("insp.materialPhysics"),
                EditorStyles.miniBoldLabel);

            int remove = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) list[i] = new DycMaterialAssociation();

                EditorGUILayout.BeginHorizontal();
                var mat = (Material)EditorGUILayout.ObjectField(list[i].material, typeof(Material), false);
                if (mat != list[i].material) { list[i].material = mat; GUI.changed = true; }

                var pm = (PhysicMaterial)EditorGUILayout.ObjectField(list[i].physicsMaterial, typeof(PhysicMaterial), false);
                if (pm != list[i].physicsMaterial) { list[i].physicsMaterial = pm; GUI.changed = true; }

                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f))) remove = i;
                EditorGUILayout.EndHorizontal();
            }

            if (remove >= 0) { list.RemoveAt(remove); GUI.changed = true; }
            if (GUILayout.Button("+", EditorStyles.miniButton)) { list.Add(new DycMaterialAssociation()); GUI.changed = true; }

            EditorGUILayout.LabelField(Dyc_L10n.T("insp.materialHint"), WrapHint());
        }

        /// <summary>
        /// Исключения: кости, рендереры и группы, которые в запекание не идут.
        ///
        /// Списки рисуются простыми строками «объект + ×», а не
        /// ReorderableList: у ReorderableList своя высота и свои отступы, и в
        /// свёрнутом «Advanced» она ломает раскладку инспектора.
        /// </summary>
        void DrawExclusions(Dyc_DynamicCollision t)
        {
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.sectionExclusions"), EditorStyles.boldLabel);

            // ---- кости
            EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludedBones"), EditorStyles.miniBoldLabel);
            var bones = t.ExcludedBones;
            int removeBone = -1;
            for (int i = 0; i < bones.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var nb = (Transform)EditorGUILayout.ObjectField(bones[i], typeof(Transform), true);
                if (nb != bones[i]) { bones[i] = nb; GUI.changed = true; }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f))) removeBone = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeBone >= 0) { bones.RemoveAt(removeBone); GUI.changed = true; }
            if (GUILayout.Button("+", EditorStyles.miniButton)) { bones.Add(null); GUI.changed = true; }
            EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludedBonesHint"), WrapHint());

            // ---- рендереры
            EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludedSkins"), EditorStyles.miniBoldLabel);
            var skins = t.ExcludedSkins;
            int removeSkin = -1;
            for (int i = 0; i < skins.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var ns = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(skins[i], typeof(SkinnedMeshRenderer), true);
                if (ns != skins[i]) { skins[i] = ns; GUI.changed = true; }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f))) removeSkin = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeSkin >= 0) { skins.RemoveAt(removeSkin); GUI.changed = true; }
            if (GUILayout.Button("+", EditorStyles.miniButton)) { skins.Add(null); GUI.changed = true; }
            EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludedSkinsHint"), WrapHint());

            // ---- группы
            var groups = t.Groups;
            if (groups != null && groups.Count > 0)
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludeGroup"), EditorStyles.miniBoldLabel);
                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g] == null) continue;
                    bool ex = EditorGUILayout.ToggleLeft("  " + groups[g].DisplayName, groups[g].exclude);
                    if (ex != groups[g].exclude) { groups[g].exclude = ex; GUI.changed = true; }
                }
                EditorGUILayout.LabelField(Dyc_L10n.T("lbl.excludeGroupHint"), WrapHint());
            }
        }
    }
}
