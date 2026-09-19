using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Окно эксперта: распределение точности по костям.
    ///
    /// ЧЕТЫРЕ ПАНЕЛИ, и каждая отвечает на свой вопрос:
    ///   1. Распределение — какая точность задана всему телу и сколько частей вышло;
    ///   2. Дерево костей   — что получилось на каждой кости и что ей назначено;
    ///   3. 3D вид          — как это выглядит на самом скелете;
    ///   4. Детали          — числа выбранной кости и её ручной бюджет.
    ///
    /// ЗАЧЕМ ОКНО ВООБЩЕ НУЖНО. Автоматическое распределение обязано быть
    /// объяснимым. Число «12 частей на грудь» бесполезно, если непонятно, откуда
    /// оно: поэтому дерево показывает ИЗМЕРЕННУЮ вогнутость, а 3D вид — что
    /// реально получилось. Пользователь видит причину, а не магию, и может
    /// переопределить любую кость, не теряя авторежим для остальных.
    ///
    /// 3D вид рисуется СВОИМИ линиями через GL, без PreviewRenderUtility.
    /// Причина в комментарии к DrawView: превью-утилита при малейшем
    /// несовпадении порядка вызовов даёт чёрную панель без единой ошибки, а
    /// отлаживать это вслепую невозможно.
    /// </summary>
    public class Dyc_ExpertWindow : EditorWindow
    {
        const float TreeWidth = 280f;
        const float DetailWidth = 260f;

        Dyc_DynamicCollision _target;
        Transform _selected;
        Vector2 _treeScroll;
        Vector2 _detailScroll;
        readonly HashSet<string> _collapsed = new HashSet<string>();

        // орбита
        [SerializeField] bool _invertY = true;

        /// <summary>
        /// Вторичные настройки свёрнуты по умолчанию. Причина не в экономии
        /// места: форма коллайдера, самоколлизии и LOD — решения, которые
        /// принимают один раз, а не крутят при подборе точности. Держать их
        /// развёрнутыми значит каждый раз отвлекать от главного.
        /// </summary>
        [SerializeField] bool _secondary;
        /// <summary>Центр вращения. Ставится ДВОЙНЫМ щелчком по кости, как в
        /// иерархии Unity: обычный выбор его не двигает, иначе камера прыгала бы
        /// при каждом осмотре.</summary>
        Transform _pivot;
        Vector3 _pivotOffset;

        Vector2 _mouseDown;
        bool _dragged;

        float _yaw = 30f;
        float _pitch = 12f;
        float _distance = 3f;
        Vector3 _focusOffset;


        public static void Open(Dyc_DynamicCollision target)
        {
            var window = GetWindow<Dyc_ExpertWindow>();
            window.titleContent = new GUIContent(Dyc_L10n.T("exp.title"), Dyc_Synapse.Icon);
            window.minSize = new Vector2(860f, 460f);
            window._target = target;
            window.FrameSelection();
            window.Show();
        }

        // ------------------------------------------------------------------ каркас

        /// <summary>
        /// Смена выделения. Окно при этом НЕ закрывается — оно очищается:
        /// закрывать его значило бы терять позицию и настройки, а показывать
        /// данные прошлого объекта — врать. Сбрасываем кость только когда цель
        /// реально сменилась, иначе сброс затирал бы выбор, сделанный щелчком
        /// по грани в сцене.
        /// </summary>
        void OnSelectionChange()
        {
            var next = ResolveFromSelection();
            if (next != _target)
            {
                _target = next;
                _selected = null;
            }
            Repaint();
        }

        static Dyc_DynamicCollision ResolveFromSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null) return null;

            var c = go.GetComponent<Dyc_DynamicCollision>();
            if (c == null) c = go.GetComponentInParent<Dyc_DynamicCollision>();
            if (c == null) c = go.GetComponentInChildren<Dyc_DynamicCollision>();
            return c;
        }

        /// <summary>Забирает выбор грани, сделанный щелчком в сцене.</summary>
        void ConsumePickedHull()
        {
            int idx = Dyc_GizmoDraw.PickedHullIndex;
            if (idx < 0) return;

            var picked = Dyc_GizmoDraw.PickedTarget;
            Dyc_GizmoDraw.PickedHullIndex = -1;
            Dyc_GizmoDraw.PickedTarget = null;

            if (picked == null || picked != _target) return;

            var set = _target.BakedSet;
            if (set == null || set.hulls == null || idx >= set.hulls.Count) return;

            var h = set.hulls[idx];
            if (h == null) return;

            _selected = string.IsNullOrEmpty(h.bonePath) ? _target.transform : _target.transform.Find(h.bonePath);
        }

        void OnGUI()
        {
            if (_target == null) _target = ResolveFromSelection();

            ConsumePickedHull();

            if (_target == null)
            {
                // Пустая, но ЖИВАЯ панель: заголовок на месте, содержимого нет.
                EditorGUILayout.BeginVertical();
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.title"), EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(Dyc_L10n.T("exp.noTarget"), MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawAllocation();

            // Чистый GUILayout, БЕЗ BeginArea и GetRect для панелей.
            //
            // BeginArea нельзя вызывать внутри группы GUILayout, а окно редактора
            // рисует именно в ней — поэтому панели, нарисованные через BeginArea,
            // молча выходили пустыми. Здесь раскладка целиком на BeginHorizontal
            // и BeginVertical с фиксированной шириной крайних колонок; Rect
            // запрашивается только для 3D вида, где он действительно нужен.
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(PanelStyle(), GUILayout.Width(TreeWidth));
            DrawTree();
            EditorGUILayout.EndVertical();

            // Ширина средней панели считается ЯВНО от ширины окна: при
            // «растянись на остаток» прямоугольник зависел от того, как
            // разложатся соседние группы, и центр проекции уезжал.
            float viewWidth = Mathf.Max(160f, position.width - TreeWidth - DetailWidth - 32f);
            EditorGUILayout.BeginVertical(GUILayout.Width(viewWidth));
            DrawView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(PanelStyle(), GUILayout.Width(DetailWidth));
            DrawDetail();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ панель 1: распределение

        void DrawAllocation()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var set = _target.BakedSet;
            int hulls = set != null && set.hulls != null ? set.hulls.Count : 0;
            int bones = set != null && set.boneStats != null ? set.boneStats.Count : 0;
            var settings = _target.DecomposeSettings;

            // ГРУППЫ, А НЕ СТРОКИ.
            //
            // Раньше это были пять строк подряд, и в них не читалось, что к чему
            // относится: «Voxel mm» стоял рядом с «Parts per bone», хотя первое —
            // про разложение, второе — про зоны. Теперь каждая группа лежит в
            // своей рамке с подписью — как лента в NSG.
            //
            // ДВЕ СТРОКИ, А НЕ ОДНА: IMGUI не переносит содержимое сам, и одна
            // строка из пяти групп обрезала бы правый край при узком окне —
            // именно так и терялась кнопка сброса раньше.

            // ---- строка 1: действия, точность, тело
            EditorGUILayout.BeginHorizontal();

            BeginBox("exp.groupActions");
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.totals", hulls, bones), _groupCaption);

            // Чем именно получены части. Это то, что отличает результат от
            // одной затягивающей оболочки: разложение режет по впадинам,
            // кластеризация — запасной путь, когда ядро не справилось.
            if (set != null && set.hulls != null && set.hulls.Count > 0)
            {
                EditorGUILayout.LabelField(
                    Dyc_L10n.T(set.decomposed ? "exp.decomposed" : "exp.clustered"),
                    _groupCaption);
            }

            if (GUILayout.Button(Dyc_L10n.T("exp.rebake"), GUILayout.Width(150f)))
            {
                if (Dyc_Menu.Bake(_target)) FrameSelection();
            }

            if (GUILayout.Button(Dyc_L10n.T("exp.clearManual"), GUILayout.Width(150f)))
            {
                Undo.RecordObject(_target, "NDC Clear Bone Budgets");
                _target.EditorSetBoneBudgets(new List<DycBoneBudget>());
                EditorUtility.SetDirty(_target);
            }

            if (GUILayout.Button(Dyc_L10n.T("exp.resetDefaults"), GUILayout.Width(150f)))
            {
                Undo.RecordObject(_target, "NDC Reset Decompose");
                _target.EditDecompose = DycDecomposeSettings.Default;
                EditorUtility.SetDirty(_target);
                GUI.changed = true;
            }
            EndBox();

            BeginBox("exp.groupPrecision");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.voxelSize"), GUILayout.Width(70f));
            float voxelSize = EditorGUILayout.Slider(settings.voxelSizeMm, 1f, 40f, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.minDepth"), GUILayout.Width(70f));
            int minDepth = EditorGUILayout.IntSlider(settings.minConcavityVoxels, 1, 32, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.maxHulls"), GUILayout.Width(70f));
            int maxHulls = EditorGUILayout.IntSlider(settings.maxHulls, 1, 128, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EndBox();

            BeginBox("exp.groupBody");

            EditorGUILayout.BeginHorizontal();
            bool project = EditorGUILayout.Toggle(settings.projectHullVertices, GUILayout.Width(16f));
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.project"), GUILayout.Width(52f));
            bool separate = EditorGUILayout.Toggle(settings.separateByBones, GUILayout.Width(16f));
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.separate"), GUILayout.Width(52f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.separationMm"), GUILayout.Width(70f));
            float separation = EditorGUILayout.Slider(settings.separationMm, 0.2f, 10f, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.basePrecision"), GUILayout.Width(70f));
            int budget = EditorGUILayout.IntSlider(_target.MaxHullsPerBone, 1, 32, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EndBox();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // ---- строка 2: ядро разложения и вид
            //
            // ЯДРО В ОТДЕЛЬНОЙ ГРУППЕ НЕ СЛУЧАЙНО. Настройки в ней — не «ещё
            // немного точности», а выбор между «редактор отвечает» и «редактор
            // повис». Своё ядро (по умолчанию) ограничено бюджетом вокселей и
            // временем; нативное V-HACD точнее по форме, но синхронно и без
            // встроенных пределов.
            EditorGUILayout.BeginHorizontal();

            BeginBox("exp.groupKernel");

            // Показываем ЭФФЕКТИВНЫЕ значения, а не сырые: у компонентов,
            // сохранённых до появления этих полей, они нули, и «0 вокселей»
            // читалось бы как «выключено», хотя на деле это «значение по
            // умолчанию». При первой же отрисовке значения записываются.
            int maxVoxels = settings.maxVoxels > 0 ? settings.maxVoxels : 1500000;
            float fillRatio = settings.convexFillRatio > 0f ? settings.convexFillRatio : 0.72f;
            int splitDepth = settings.maxSplitDepth > 0 ? settings.maxSplitDepth : 5;
            float timeBudget = settings.EffectiveTimeBudgetSeconds;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.kernel"), GUILayout.Width(70f));
            var kernel = (DycDecomposeKernel)EditorGUILayout.EnumPopup(settings.kernel, GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();

            if (kernel == DycDecomposeKernel.Own)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.maxVoxels"), GUILayout.Width(70f));
                maxVoxels = Mathf.Clamp(EditorGUILayout.IntField(maxVoxels, GUILayout.Width(90f)), 32000, 4000000);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.timeBudget"), GUILayout.Width(70f));
                timeBudget = Mathf.Clamp(EditorGUILayout.FloatField(timeBudget, GUILayout.Width(60f)), 1f, 300f);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.fillRatio"), GUILayout.Width(70f));
                fillRatio = EditorGUILayout.Slider(fillRatio, 0.4f, 0.98f, GUILayout.Width(120f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.splitDepth"), GUILayout.Width(70f));
                splitDepth = EditorGUILayout.IntSlider(splitDepth, 1, 8, GUILayout.Width(120f));
                EditorGUILayout.EndHorizontal();
            }
            EndBox();

            BeginBox("exp.groupView");
            EditorGUILayout.BeginHorizontal();
            bool invertY = EditorGUILayout.Toggle(_invertY, GUILayout.Width(16f));
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.invertY"), GUILayout.Width(70f));
            if (invertY != _invertY) { _invertY = invertY; Repaint(); }
            EditorGUILayout.EndHorizontal();
            EndBox();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (budget != _target.MaxHullsPerBone)
            {
                Undo.RecordObject(_target, "NDC Bone Budget");
                _target.EditMaxHullsPerBone = budget;
                EditorUtility.SetDirty(_target);
            }

            DrawSecondary();

            if (!Mathf.Approximately(voxelSize, settings.voxelSizeMm) ||
                minDepth != settings.minConcavityVoxels ||
                maxHulls != settings.maxHulls || project != settings.projectHullVertices ||
                separate != settings.separateByBones || !Mathf.Approximately(separation, settings.separationMm) ||
                kernel != settings.kernel || maxVoxels != settings.maxVoxels ||
                !Mathf.Approximately(fillRatio, settings.convexFillRatio) ||
                splitDepth != settings.maxSplitDepth ||
                !Mathf.Approximately(timeBudget, settings.timeBudgetSeconds))
            {
                Undo.RecordObject(_target, "NDC Decompose Settings");
                settings.voxelSizeMm = voxelSize;
                settings.minConcavityVoxels = minDepth;
                settings.maxHulls = maxHulls;
                settings.projectHullVertices = project;
                settings.separateByBones = separate;
                settings.separationMm = separation;
                settings.kernel = kernel;
                settings.maxVoxels = maxVoxels;
                settings.convexFillRatio = fillRatio;
                settings.maxSplitDepth = splitDepth;
                settings.timeBudgetSeconds = timeBudget;
                _target.EditDecompose = settings;
                EditorUtility.SetDirty(_target);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Вторичные настройки: форма коллайдера, самоколлизии, LOD, ручной
        /// бюджет кости.
        ///
        /// ВЫНЕСЕНО В СВЁРНУТЫЙ БЛОК НЕ РАДИ ЭКОНОМИИ МЕСТА. Это решения
        /// другого уровня, чем «сколько частей на грудь»: их принимают один раз
        /// и меняют редко, а развёрнутыми они отвлекали бы от подбора точности.
        /// Ручной бюджет при этом остаётся ЗДЕСЬ, а не в дереве: он относится к
        /// выбранной кости, и держать его рядом с остальными ручными
        /// переопределениями честнее.
        /// </summary>
        void DrawSecondary()
        {
            EditorGUILayout.Space(2f);

            _secondary = EditorGUILayout.Foldout(_secondary, Dyc_L10n.T("exp.secondary"), true);
            if (!_secondary) return;

            EditorGUI.indentLevel++;

            // ---- форма и самоколлизии
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(Dyc_L10n.T("lbl.shape"), GUILayout.Width(84f));
            var shape = (DycColliderShape)EditorGUILayout.EnumPopup(_target.ColliderShape, GUILayout.Width(150f));
            if (shape != _target.ColliderShape)
            {
                Undo.RecordObject(_target, "NDC Collider Shape");
                _target.EditColliderShape = shape;
                EditorUtility.SetDirty(_target);
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField(Dyc_L10n.T("insp.selfCollision"), GUILayout.Width(96f));
            var self = (DycSelfCollision)EditorGUILayout.EnumPopup(_target.SelfCollision, GUILayout.Width(120f));
            if (self != _target.SelfCollision)
            {
                Undo.RecordObject(_target, "NDC Self Collision");
                _target.EditSelfCollisionMode = self;
                EditorUtility.SetDirty(_target);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                Dyc_L10n.T(shape == DycColliderShape.Concave ? "shape.concaveHint" : "shape.convexHint"),
                WrapMini());

            // ---- LOD
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(Dyc_L10n.T("insp.lodMode"), GUILayout.Width(84f));
            var lod = (DycLodMode)EditorGUILayout.EnumPopup(_target.EditLodMode, GUILayout.Width(120f));
            if (lod != _target.EditLodMode)
            {
                Undo.RecordObject(_target, "NDC LOD");
                _target.EditLodMode = lod;
                EditorUtility.SetDirty(_target);
            }

            if (lod != DycLodMode.Off)
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("insp.lodDistance"), GUILayout.Width(80f));
                float dist = EditorGUILayout.FloatField(_target.EditLodDistance, GUILayout.Width(70f));
                if (!Mathf.Approximately(dist, _target.EditLodDistance))
                {
                    Undo.RecordObject(_target, "NDC LOD Distance");
                    _target.EditLodDistance = Mathf.Max(1f, dist);
                    EditorUtility.SetDirty(_target);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        static GUIStyle _wrapMini;

        static GUIStyle WrapMini()
        {
            if (_wrapMini == null)
                _wrapMini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            return _wrapMini;
        }

        static GUIStyle _groupCaption;

        /// <summary>
        /// Открывает группу настроек: рамка и подпись сверху.
        ///
        /// Рамка, а не разделитель: разделители читаются только когда настроек
        /// мало и они в одну строку. Здесь настроек больше десятка, и границу
        /// между «про разложение» и «про зоны» видно только по рамке.
        /// </summary>
        static void BeginBox(string titleKey)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(false));

            if (_groupCaption == null)
                _groupCaption = new GUIStyle(EditorStyles.miniBoldLabel);
            _groupCaption.normal.textColor = new Color(0.60f, 0.65f, 0.72f);
            _groupCaption.margin = new RectOffset(0, 0, 0, 4);

            EditorGUILayout.LabelField(Dyc_L10n.T(titleKey), _groupCaption);
        }

        static void EndBox()
        {
            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        // ------------------------------------------------------------------ панель 2: дерево костей
        void DrawTree()
        {
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.bones"), EditorStyles.boldLabel);

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, PanelStyle(),
                GUILayout.ExpandHeight(true));

            var skin = SourceSkin();
            if (skin == null || skin.bones == null || skin.bones.Length == 0)
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.noSkeleton"), EditorStyles.miniLabel);
            }
            else
            {
                var roots = Roots(skin.bones);
                for (int i = 0; i < roots.Count; i++) DrawBoneRow(roots[i], skin.bones, 0);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Кости, у которых родителя нет среди костей скелета: с них
        /// начинается обход. Так дерево строится без опоры на Humanoid.</summary>
        static List<Transform> Roots(Transform[] bones)
        {
            var set = new HashSet<Transform>(bones);
            var roots = new List<Transform>();

            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                if (bone.parent != null && set.Contains(bone.parent)) continue;
                roots.Add(bone);
            }

            return roots;
        }

        void DrawBoneRow(Transform bone, Transform[] bones, int depth)
        {
            if (bone == null) return;

            var stat = StatFor(bone);
            string path = Dyc_Baker.PathOf(_target.transform, bone);
            bool hasChildren = HasBoneChildren(bone, bones);
            bool collapsed = _collapsed.Contains(path);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(depth * 12f);

            if (hasChildren)
            {
                if (GUILayout.Button(collapsed ? "▸" : "▾", EditorStyles.label, GUILayout.Width(14f)))
                {
                    if (collapsed) _collapsed.Remove(path);
                    else _collapsed.Add(path);
                }
            }
            else
            {
                GUILayout.Space(14f);
            }

            bool isSelected = _selected == bone;
            var style = isSelected ? EditorStyles.boldLabel : EditorStyles.label;
            if (GUILayout.Button(bone.name, style)) Select(bone);

            GUILayout.FlexibleSpace();

            if (stat != null)
            {
                var color = stat.concavity > 0.5f ? Dyc_Style.Warn
                          : stat.concavity > 0.15f ? Dyc_Style.Text
                          : Dyc_Style.Muted;

                var previous = GUI.color;
                GUI.color = color;
                GUILayout.Label(Dyc_L10n.T("exp.concavityShort", stat.concavity), EditorStyles.miniLabel, GUILayout.Width(58f));
                GUI.color = previous;

                GUILayout.Label(stat.pieces.ToString(), EditorStyles.miniLabel, GUILayout.Width(24f));

                if (stat.dropped > 0)
                {
                    GUI.color = Dyc_Style.Error;
                    GUILayout.Label("−" + stat.dropped, EditorStyles.miniLabel, GUILayout.Width(26f));
                    GUI.color = previous;
                }
            }

            EditorGUILayout.EndHorizontal();

            if (hasChildren && !collapsed)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    var child = bones[i];
                    if (child == null || child.parent != bone) continue;
                    DrawBoneRow(child, bones, depth + 1);
                }
            }
        }

        static bool HasBoneChildren(Transform bone, Transform[] bones)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].parent == bone) return true;
            return false;
        }

        DycBoneStat StatFor(Transform bone)
        {
            var set = _target.BakedSet;
            if (set == null || set.boneStats == null || bone == null) return null;

            string path = Dyc_Baker.PathOf(_target.transform, bone);
            for (int i = 0; i < set.boneStats.Count; i++)
                if (set.boneStats[i] != null && set.boneStats[i].bonePath == path) return set.boneStats[i];

            return null;
        }

        void Select(Transform bone)
        {
            _selected = bone;
            Repaint();
        }

        // ------------------------------------------------------------------ панель 3: 3D

        /// <summary>
        /// Скелет рисуется САМ, через GL, без PreviewRenderUtility.
        ///
        /// Почему отказались от превью-утилиты. Она требует RenderTexture,
        /// собственной камеры и строгого порядка BeginPreview/EndPreview по
        /// событиям, и при малейшем несовпадении панель остаётся чёрной БЕЗ
        /// единой ошибки в консоли — именно это и происходило. Отладить такое
        /// вслепую нельзя.
        ///
        /// Здесь всё детерминировано: своя проекция мир → экран, и рисование
        /// линиями в тех же координатах, в которых эта проекция получена.
        /// Сетка на полу рисуется по-настоящему линиями.
        ///
        /// Выбор кости тоже идёт в экранных координатах, то есть ровно по тому,
        /// что видит пользователь, а не по лучу в мире.
        /// </summary>
        void DrawView()
        {
            var rect = GUILayoutUtility.GetRect(64f, 64f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 16f || rect.height < 16f) return;

            // Фон панели — до всего остального, чтобы под скелетом не было
            // полос от соседних панелей.
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f, 1f));

            var skin = SourceSkin();
            if (skin == null || skin.bones == null)
            {
                GUI.Box(rect, Dyc_L10n.T("exp.noSkeleton"));
                return;
            }

            HandleOrbit(rect);

            // КУБ ОРИЕНТАЦИИ: ВВОД ЗДЕСЬ, РИСОВАНИЕ ПОСЛЕ КАДРА.
            //
            // Разделение обязательно, и вот почему. Щелчок приходит отдельным
            // событием, а не перерисовкой, поэтому ввод обязан обрабатываться
            // ДО проверки на Repaint. Но нарисовать куб здесь нельзя: следом
            // RenderSkeleton3D кладёт текстуру на ВСЮ панель и затирает его —
            // именно поэтому куб и пропадал.
            ViewCubeInput(rect);

            if (Event.current.type != EventType.Repaint) return;

            // ОБРЕЗКА. Без неё GL рисует по всей ширине экрана (пиксельная
            // матрица), и сетка пола выползала на соседние панели — именно это
            // выглядело как «просвечивающие полупрозрачные панели».
            //
            // Внутри BeginClip система координат смещается к началу панели,
            // поэтому камера строится с ЛОКАЛЬНЫМ прямоугольником: проекция
            // получается в координатах панели, а GUIToScreenPoint сам добавляет
            // смещение обрезки.
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f, 1f));

            // Камера строится на локальном прямоугольнике (0,0,w,h), а обрезка
            // идёт по нему же: GL всё равно рисует в пикселях экрана, поэтому
            // координаты внутри панели совпадают с оконными.
            _clipRect = rect;
            RenderSkeleton3D(rect, skin);

            // Поверх кадра — средствами IMGUI: это плоские элементы, и в
            // буфере им делать нечего. Куб рисуется здесь, а не до кадра.
            DrawViewCube(rect);
            DrawAxisLabels(rect);
            DrawHeightRuler(rect);

            var hint = new Rect(rect.x + 8f, rect.yMax - 22f, rect.width - 16f, 18f);
            GUI.Label(hint, Dyc_L10n.T("exp.orbitHint"), EditorStyles.miniLabel);

            if (_selected != null)
            {
                var label = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 18f);
                GUI.Label(label, _selected.name, EditorStyles.boldLabel);
            }
        }

        // ------------------------------------------------------------------ настоящий 3D

        static readonly Color ViewBackground = new Color(0.16f, 0.17f, 0.19f, 1f);

        /// <summary>
        /// Потолок стороны буфера. Он высокий намеренно: буфер строится в
        /// РЕАЛЬНЫХ пикселях экрана, а на Retina их вдвое больше, чем точек.
        /// При потолке 900 панель шириной 600 точек на Retina требовала 1200
        /// пикселей и ужималась — отсюда и бралась мягкость линий.
        ///
        /// Высокий потолок безопасен только вместе с кэшем кадра ниже: полный
        /// кадр считается один раз на изменение вида, а не каждый Repaint.
        /// </summary>
        const int MaxViewSide = 1600;

        /// <summary>
        /// Потолок работы во время вращения, в пикселях буфера. Подобран так,
        /// чтобы кадр укладывался в отзывчивые ~10 мс на CPU-растеризаторе.
        /// </summary>
        const float DragPixelBudget = 300000f;

        static readonly Color32 AxisX = new Color32(205, 75, 75, 255);
        static readonly Color32 AxisZ = new Color32(75, 125, 215, 255);

        /// <summary>Ортопроекция. Переключается центром куба ориентации.</summary>
        bool _orthographic;

        readonly Dyc_SoftRaster _raster = new Dyc_SoftRaster();
        Texture2D _viewTexture;

        // ------------------------------------------------------------------ куб ориентации

        static Rect ViewCubeRect(Rect view)
        {
            const float size = 78f;
            return new Rect(view.xMax - size - 8f, view.y + 8f, size, size);
        }

        /// <summary>
        /// Куб ориентации: шесть кнопок-граней и центр.
        ///
        /// Грань разворачивает камеру вдоль своей оси, центр переключает
        /// проекцию. Куб поворачивается ВМЕСТЕ с камерой, поэтому всегда видно,
        /// откуда смотришь, и по какой грани щёлкать, чтобы посмотреть иначе.
        /// </summary>
        static GUIStyle _cubeLabel;

        static GUIStyle CubeLabel()
        {
            if (_cubeLabel != null) return _cubeLabel;
            _cubeLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            return _cubeLabel;
        }

        /// <summary>
        /// Заливает параллелограмм: начало плюс два вектора-ребра.
        ///
        /// IMGUI умеет рисовать только прямоугольники, но матрица GUI — это
        /// полноценное аффинное преобразование. Она переводит единичный квадрат
        /// ровно в нужный параллелограмм, поэтому заливка грани куба — один
        /// DrawTexture, а не набор треугольников, которых IMGUI всё равно не
        /// умеет.
        /// </summary>
        static void FillQuad(Vector2 origin, Vector2 u, Vector2 v, Color color, Texture2D texture)
        {
            if (texture == null) return;

            var matrix = new Matrix4x4();
            matrix.m00 = u.x; matrix.m01 = v.x; matrix.m03 = origin.x;
            matrix.m10 = u.y; matrix.m11 = v.y; matrix.m13 = origin.y;
            matrix.m22 = 1f; matrix.m33 = 1f;

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;

            GUI.matrix = matrix;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, 1f, 1f), texture);

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Куб ориентации: шесть ЗАЛИТЫХ граней с буквами и центр.
        ///
        /// Грани залиты, поэтому рёбра обратной стороны куба не видны — ровно
        /// как у настоящего тела. Щелчок по грани разворачивает камеру вдоль её
        /// оси, щелчок по центру переключает проекцию: центр — это квадрат
        /// «только перекрытие», без подписи, потому что жест и так очевиден.
        ///
        /// События мыши обрабатываются ВРУЧНУЮ и на всех событиях, а не только
        /// на перерисовке: кнопки GUI.Button, нарисованные лишь в Repaint, не
        /// получают щелчка — именно поэтому куб раньше не работал.
        /// </summary>
        /// <summary>Четыре угла каждой грани, обход по кругу.</summary>
        static readonly int[][] CubeFaces =
        {
            new[] { 1, 3, 7, 5 },   // +X
            new[] { 0, 4, 6, 2 },   // -X
            new[] { 2, 6, 7, 3 },   // +Y
            new[] { 0, 1, 5, 4 },   // -Y
            new[] { 4, 5, 7, 6 },   // +Z
            new[] { 0, 2, 3, 1 }    // -Z
        };

        static readonly Vector3[] CubeDirs =
            { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static readonly string[] CubeNames = { "X", "-X", "Y", "-Y", "Z", "-Z" };

        /// <summary>Углы куба в экранных координатах и нормали граней в системе вида.</summary>
        void BuildCube(Rect view, out Vector2 center, out Vector2[] corners, out Vector3[] normals)
        {
            var box = ViewCubeRect(view);
            center = new Vector2(box.center.x, box.center.y);

            var inverse = Quaternion.Inverse(Quaternion.Euler(_pitch, _yaw, 0f));
            float r = box.height * 0.30f;

            corners = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 p = inverse * v;
                corners[i] = new Vector2(center.x + p.x * r, center.y - p.y * r);
            }

            normals = new Vector3[6];
            for (int f = 0; f < 6; f++) normals[f] = inverse * CubeDirs[f];
        }

        static Rect CubeFaceRect(Vector2[] corners, int face)
        {
            var quad = CubeFaces[face];
            var a = corners[quad[0]];
            var d = corners[quad[2]];
            return new Rect((a.x + d.x) * 0.5f - 12f, (a.y + d.y) * 0.5f - 10f, 24f, 20f);
        }

        static Rect CubeCenterRect(Vector2 center)
        {
            return new Rect(center.x - 10f, center.y - 10f, 20f, 20f);
        }

        /// <summary>
        /// Обработка щелчков по кубу. Вызывается НА КАЖДОМ событии: щелчок —
        /// это не перерисовка, и на перерисовке его обработать нельзя.
        /// </summary>
        /// <summary>Что под курсором: -1 ничего, 0..5 грань, 6 центр.</summary>
        int _cubeHover = -1;

        void ViewCubeInput(Rect view)
        {
            var e = Event.current;
            if (e.type != EventType.MouseMove && e.type != EventType.MouseDrag
                && e.type != EventType.MouseDown && e.type != EventType.MouseLeaveWindow) return;

            int hover = -1;
            if (ViewCubeRect(view).Contains(e.mousePosition))
            {
                BuildCube(view, out var center, out var corners, out var normals);

                if (CubeCenterRect(center).Contains(e.mousePosition))
                {
                    hover = 6;
                }
                else
                {
                    for (int f = 0; f < 6; f++)
                    {
                        if (normals[f].z > -0.3f) continue;
                        if (!CubeFaceRect(corners, f).Contains(e.mousePosition)) continue;
                        hover = f;
                        break;
                    }
                }
            }

            // Подсветка — состояние, а не разовая реакция: перерисовку надо
            // запросить, иначе она случится только по чужому поводу.
            if (hover != _cubeHover)
            {
                _cubeHover = hover;
                Repaint();
            }

            if (e.type != EventType.MouseDown || e.button != 0 || hover < 0) return;

            if (hover == 6)
            {
                _orthographic = !_orthographic;
                _viewRendered = false;
            }
            else
            {
                SnapTo(CubeDirs[hover]);
            }

            e.Use();
        }

        /// <summary>
        /// Куб ориентации: шесть ЗАЛИТЫХ граней с буквами и центр.
        ///
        /// Грани залиты, поэтому рёбра обратной стороны не видны — как у
        /// настоящего тела. Щелчок по грани разворачивает камеру вдоль её оси,
        /// щелчок по центру переключает проекцию: центр — квадрат «только
        /// перекрытие», без подписи, потому что жест и так очевиден.
        ///
        /// Вызывается ТОЛЬКО на перерисовке и ПОСЛЕ кадра: текстура кадра
        /// занимает всю панель и затирает всё, что нарисовано раньше.
        /// </summary>
        void DrawViewCube(Rect view)
        {
            var box = ViewCubeRect(view);
            EditorGUI.DrawRect(box, new Color(0.11f, 0.12f, 0.14f, 0.92f));

            BuildCube(view, out var center, out var corners, out var normals);

            var accent = Dyc_Style.Accent;

            // ТОЛЬКО РЁБРА, БЕЗ ЗАЛИВКИ И БЕЗ ВЗАИМНОГО ПЕРЕКРЫТИЯ.
            //
            // Заливка скрывала дальние рёбра, но вместе с ними и форму: грань
            // под острым углом почти неотличима от фона, и куб читался хуже,
            // чем простой каркас. Каркас из двенадцати рёбер виден целиком, а
            // ориентация и так понятна по подписям.
            BeginLines(Dyc_Style.Line);
            for (int i = 0; i < 8; i++)
                for (int b = 0; b < 3; b++)
                    if ((i & (1 << b)) == 0) Line(corners[i], corners[i | (1 << b)]);
            EndLines();

            var style = CubeLabel();

            for (int f = 0; f < 6; f++)
            {
                // В системе вида камера смотрит вдоль +Z, поэтому на камеру
                // смотрит грань с ОТРИЦАТЕЛЬНОЙ z нормали. Знак здесь был
                // перепутан, и подписывались как раз отвёрнутые грани.
                if (normals[f].z > -0.3f) continue;

                bool hot = _cubeHover == f;
                var rect = CubeFaceRect(corners, f);
                if (hot) rect = Grow(rect, 1.4f);

                style.fontSize = hot ? 15 : 11;
                style.normal.textColor = hot ? accent : new Color(0.82f, 0.85f, 0.89f);
                GUI.Label(rect, CubeNames[f], style);
            }

            style.fontSize = 11;
            style.normal.textColor = new Color(0.82f, 0.85f, 0.89f);

            // Центр — квадрат «только контур», без заливки: он переключает
            // проекцию, и по нему это видно по цвету, когда она включена.
            var middle = CubeCenterRect(center);
            bool centerHot = _cubeHover == 6;
            if (centerHot) middle = Grow(middle, 1.4f);

            // Подсветка — ТОЛЬКО под курсором. Раньше квадрат горел всё время,
            // пока включена ортопроекция, и это сбивало: подсветка означает
            // «сюда можно щёлкнуть», а не «этот режим включён».
            var middleColor = centerHot ? accent : Dyc_Style.Line;
            BeginLines(middleColor);
            Line(new Vector2(middle.xMin, middle.yMin), new Vector2(middle.xMax, middle.yMin));
            Line(new Vector2(middle.xMax, middle.yMin), new Vector2(middle.xMax, middle.yMax));
            Line(new Vector2(middle.xMax, middle.yMax), new Vector2(middle.xMin, middle.yMax));
            Line(new Vector2(middle.xMin, middle.yMax), new Vector2(middle.xMin, middle.yMin));
            EndLines();
        }

        static Rect Grow(Rect rect, float factor)
        {
            float dx = rect.width * (factor - 1f) * 0.5f;
            float dy = rect.height * (factor - 1f) * 0.5f;
            return new Rect(rect.x - dx, rect.y - dy, rect.width + dx * 2f, rect.height + dy * 2f);
        }

        /// <summary>Разворот камеры вдоль оси грани. Камера смотрит ПРОТИВ неё.</summary>
        void SnapTo(Vector3 face)
        {
            Quaternion rot;
            if (face == Vector3.up) rot = Quaternion.Euler(90f, 0f, 0f);
            else if (face == Vector3.down) rot = Quaternion.Euler(-90f, 0f, 0f);
            else rot = Quaternion.LookRotation(-face, Vector3.up);

            Vector3 e = rot.eulerAngles;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
            _yaw = e.y;
            _viewRendered = false;
        }

        // ------------------------------------------------------------------ земля

        /// <summary>Метров на пиксель в текущем кадре.</summary>
        float UnitsPerPixel(ViewCamera camera, Rect rect)
        {
            return camera.orthographic
                ? camera.orthoHeight / Mathf.Max(1f, rect.height)
                : 2f * _distance * Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Max(1f, rect.height);
        }

        /// <summary>
        /// Земля: сетка и оси X/Z.
        ///
        /// ШАГ СЕТКИ ПОДСТРАИВАЕТСЯ ПОД МАСШТАБ. У слоновьей кости размеры в
        /// разы больше, чем у мышиной, и фиксированный шаг означал бы либо
        /// сплошную заливку, либо полное отсутствие сетки. Шаг берётся из ряда
        /// 1-5-10 таким, чтобы его экранная величина была не меньше ~9
        /// пикселей: мелкие линии всегда различимы, крупные — толще.
        ///
        /// Оси Y НЕТ намеренно: в Unity Y смотрит вверх, и его роль полностью
        /// берёт линейка роста. Стрелки и подписи осей — в DrawAxisLabels.
        /// </summary>
        void AddGround(ViewCamera camera, Rect rect)
        {
            float unitsPerPixel = UnitsPerPixel(camera, rect);

            // МЕЛЬЧЕ 0.1 М НЕ БЫВАЕТ, а нижнего предела по числу линий нет.
            //
            // Раньше ряд начинался с сантиметра и был ещё искусственный минимум
            // по размеру сетки — из-за него при приближении сетка пропадала.
            // Теперь шаг упирается в 0.1 м и дальше не мельчает, а количество
            // линий просто растёт: сетка продолжает покрывать кадр на любом
            // масштабе.
            float[] levels = { 0.1f, 0.5f, 1f, 5f, 10f, 50f, 100f, 500f, 1000f, 5000f };
            float minor = levels[0];
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] / unitsPerPixel < 9f) continue;
                minor = levels[i];
                break;
            }

            float major = minor * 5f;

            float extent = unitsPerPixel * rect.height * 0.8f;
            int halfCount = Mathf.Clamp(Mathf.CeilToInt(extent / minor), 1, 200);
            extent = halfCount * minor;

            var minorColor = new Color32(60, 64, 72, 255);
            var majorColor = new Color32(96, 102, 112, 255);

            for (int i = -halfCount; i <= halfCount; i++)
            {
                float offset = i * minor;
                float ratio = offset / major;
                bool isMajor = Mathf.Abs(ratio - Mathf.Round(ratio)) < 0.001f;
                var color = isMajor ? majorColor : minorColor;

                AddSegment(camera, new Vector3(offset, 0f, -extent), new Vector3(offset, 0f, extent), color);
                AddSegment(camera, new Vector3(-extent, 0f, offset), new Vector3(extent, 0f, offset), color);
            }

            // Стрелка масштабируется вместе с сеткой: иначе на слоне она была
            // бы точкой, а на мыши — во весь кадр.
            float head = minor * 1.4f;
            AddSegment(camera, new Vector3(-extent, 0f, 0f), new Vector3(extent, 0f, 0f), AxisX);
            AddSegment(camera, new Vector3(extent, 0f, 0f), new Vector3(extent - head, 0f, head * 0.45f), AxisX);
            AddSegment(camera, new Vector3(extent, 0f, 0f), new Vector3(extent - head, 0f, -head * 0.45f), AxisX);

            AddSegment(camera, new Vector3(0f, 0f, -extent), new Vector3(0f, 0f, extent), AxisZ);
            AddSegment(camera, new Vector3(0f, 0f, extent), new Vector3(head * 0.45f, 0f, extent - head), AxisZ);
            AddSegment(camera, new Vector3(0f, 0f, extent), new Vector3(-head * 0.45f, 0f, extent - head), AxisZ);
        }

        /// <summary>Буквы X и Z у острия осей. Только текст, поверх кадра.</summary>
        void DrawAxisLabels(Rect rect)
        {
            var camera = MakeCamera(rect);
            float extent = UnitsPerPixel(camera, rect) * rect.height * 0.8f;
            var style = EditorStyles.miniLabel;

            if (camera.Project(new Vector3(extent, 0f, 0f), out var px))
            {
                var rc = new Rect(px.x + 5f, px.y - 8f, 18f, 16f);
                if (rect.Contains(rc.center)) GUI.Label(rc, "X", style);
            }

            if (camera.Project(new Vector3(0f, 0f, extent), out var pz))
            {
                var rc = new Rect(pz.x + 5f, pz.y - 8f, 18f, 16f);
                if (rect.Contains(rc.center)) GUI.Label(rc, "Z", style);
            }
        }

        // ------------------------------------------------------------------ линейка роста

        /// <summary>
        /// Линейка роста слева: метрика справа от штока, имперская слева.
        ///
        /// Только в ортопроекции. В перспективе видимая высота зависит от
        /// глубины, и линейка врала бы — а ею именно меряют.
        ///
        /// Метрика: штрих каждые 5 см, крупный каждые 10 см с числом.
        /// Имперская: дюймы малыми штрихами, футы крупными с числом.
        /// Мировой ноль совпадает с центром кадра.
        /// </summary>
        /// <summary>
        /// Вид строго вдоль горизонтальной оси: спереди, сзади, слева, справа.
        ///
        /// Линейка роста имеет смысл только здесь. В перспективе или под
        /// наклоном она показывала бы высоту в пересчёте на глубину, то есть
        /// врала бы — а ею именно меряют.
        /// </summary>
        bool IsAxisView()
        {
            if (!_orthographic) return false;
            if (Mathf.Abs(_pitch) > 1.5f) return false;

            float yaw = Mathf.Repeat(_yaw, 90f);
            return yaw < 1.5f || yaw > 88.5f;
        }

        Vector3 _rulerFocus;
        float _rulerBack;
        bool _rulerFocusReady;

        /// <summary>
        /// Точка линейки на заданной мировой высоте.
        ///
        /// Линейка стоит слева от точки интереса, на 86% половины ширины кадра,
        /// и лежит в плоскости взгляда: в видах спереди/сзади/слева/справа она
        /// всегда у левого края. Ноль — МИРОВАЯ высота 0, а не центр панели:
        /// камера вращается вокруг точки интереса, и привязка к центру давала
        /// сдвинутую шкалу.
        /// </summary>
        Vector3 RulerPoint(ViewCamera camera, Rect rect, float height)
        {
            if (!_rulerFocusReady)
            {
                var skin = SourceSkin();
                _rulerFocus = FocusPoint(skin);

                // ЛИНЕЙКА СТАВИТСЯ ПОЗАДИ СКЕЛЕТА.
                //
                // Раньше она лежала в одной плоскости с костями, и перекрытие
                // зависело от того, ближе или дальше оказался конкретный участок
                // кости — отсюда «наполовину закрыта». Линейка — измерительная
                // опора, а не объект сцены: она обязана быть ФОНОМ, который
                // кости закрывают целиком. Смещение берётся от габарита скелета,
                // поэтому работает и для мыши, и для слона.
                float radius = skin != null ? skin.bounds.extents.magnitude : 0.5f;
                _rulerBack = Mathf.Max(radius * 1.2f, 0.05f);
                _rulerFocusReady = true;
            }

            float aspect = rect.width / Mathf.Max(1f, rect.height);
            float halfWidth = camera.orthographic
                ? camera.orthoHeight * 0.5f * aspect
                : _distance * Mathf.Tan(30f * Mathf.Deg2Rad) * aspect;

            Vector3 right = camera.rotation * Vector3.right;
            Vector3 back = camera.rotation * Vector3.forward;

            return new Vector3(_rulerFocus.x, height, _rulerFocus.z)
                   - right * (halfWidth * 0.86f)
                   - back * _rulerBack;
        }

        static Vector3 RulerTickDir(ViewCamera camera)
        {
            return camera.rotation * Vector3.right;
        }

        /// <summary>
        /// Линейка — ОБЫЧНАЯ ГЕОМЕТРИЯ, а не наложение поверх кадра.
        ///
        /// Только так её могут перекрыть кости: нарисованное поверх кадра не
        /// перекрывается ничем по определению. Штрихи уходят в растеризатор
        /// вместе с сеткой и осями, и глубинный тест делает всё сам.
        /// </summary>
        void AddRuler(ViewCamera camera, Rect rect)
        {
            if (!IsAxisView()) return;

            var color = new Color32(148, 154, 164, 255);
            Vector3 tick = RulerTickDir(camera);

            AddSegment(camera, RulerPoint(camera, rect, 0f), RulerPoint(camera, rect, 3f), color);

            // Метрика — вправо от штока: 5 см малый штрих, 10 см крупный.
            for (int cm = 0; cm <= 300; cm += 5)
            {
                float height = cm * 0.01f;
                Vector3 at = RulerPoint(camera, rect, height);
                AddSegment(camera, at, at + tick * (cm % 10 == 0 ? 0.06f : 0.03f), color);
            }

            // Имперская — влево: дюйм малый, фут крупный.
            for (int inch = 0; inch <= 120; inch++)
            {
                float height = inch * 0.0254f;
                Vector3 at = RulerPoint(camera, rect, height);
                AddSegment(camera, at, at - tick * (inch % 12 == 0 ? 0.06f : 0.025f), color);
            }
        }

        /// <summary>
        /// Цифры линейки.
        ///
        /// Текст в растр не положить, поэтому он рисуется поверх кадра — но
        /// каждая цифра проверяется по буферу глубины и пропускается там, где
        /// её закрывает кость. Без этой проверки подписи висели бы прямо на
        /// скелете.
        /// </summary>
        void DrawHeightRuler(Rect rect)
        {
            if (!IsAxisView()) return;

            var camera = MakeCamera(rect);
            Vector3 tick = RulerTickDir(camera);
            var style = EditorStyles.miniLabel;

            for (int cm = 10; cm <= 300; cm += 10)
            {
                Vector3 at = RulerPoint(camera, rect, cm * 0.01f) + tick * 0.075f;
                if (!camera.ProjectDepth(at, out var gui, out float depth)) continue;
                if (_raster.Occluded(gui, depth)) continue;

                var place = new Rect(gui.x, gui.y - 7f, 40f, 14f);
                if (rect.Contains(place.center)) GUI.Label(place, cm.ToString(), style);
            }

            for (int inch = 12; inch <= 120; inch += 12)
            {
                Vector3 at = RulerPoint(camera, rect, inch * 0.0254f) - tick * 0.075f;
                if (!camera.ProjectDepth(at, out var gui, out float depth)) continue;
                if (_raster.Occluded(gui, depth)) continue;

                var place = new Rect(gui.x - 34f, gui.y - 7f, 30f, 14f);
                if (rect.Contains(place.center)) GUI.Label(place, (inch / 12) + "'", style);
            }
        }

        /// <summary>Отпечаток вида, для которого построен текущий кадр.</summary>
        int _viewKey;
        bool _viewRendered;

        /// <summary>
        /// Почему вид нарисован СВОИМ растеризатором, а не через GPU.
        ///
        /// Сначала это был IMGUI-проекция: у неё нет буфера глубины, поэтому
        /// кости не перекрывали друг друга, а сетка пола проходила сквозь них.
        /// Потом дважды пробовали GPU (PreviewRenderUtility с MeshTopology.Lines)
        /// — и оба раза панель оставалась ПУСТОЙ: сначала не сработал материал,
        /// потом топология линий. Проверить это без запуска Unity невозможно, и
        /// каждый заход заканчивался тем, что пользователь видел пустоту.
        ///
        /// Программный растеризатор убирает эту неопределённость: буфер
        /// глубины, буфер цвета и арифметика — всё в коде ниже, и всё
        /// проверяемо в консольном тесте. Ни шейдера, ни материала, ни
        /// рендер-пайплайна.
        ///
        /// Как получается «октаэдр без граней»: грани рисуются ТОЛЬКО В ГЛУБИНУ
        /// и цвета не пишут. Они невидимы, но закрывают всё, что за ними, —
        /// поэтому видны ровно те рёбра, что попадают в кадр.
        /// </summary>
        void OnEnable()
        {
            // БЕЗ ЭТОГО ПОДСВЕТКА ПО НАВЕДЕНИЮ НЕ РАБОТАЕТ ВООБЩЕ.
            //
            // EditorWindow по умолчанию НЕ получает MouseMove: окно просто не
            // подписано на эти события. Поэтому состояние «под курсором» не
            // обновлялось, и куб никогда не подсвечивался. Кадр при этом не
            // пересчитывается зря — он кэшируется по отпечатку вида.
            wantsMouseMove = true;
        }

        void OnDisable()
        {
            _viewRendered = false;
            if (_viewTexture == null) return;
            Object.DestroyImmediate(_viewTexture);
            _viewTexture = null;
        }

        void RenderSkeleton3D(Rect rect, SkinnedMeshRenderer skin)
        {
            // ТОЧКИ ПРОТИВ ПИКСЕЛЕЙ. Панель задана в точках IMGUI, а буфер — в
            // пикселях. На Retina пикселей вдвое больше; если построить буфер по
            // точкам и растянуть, штрих мылится. Поэтому буфер строится в
            // реальных пикселях — линия остаётся резкой на любом экране.
            //
            // Во время вращения разрешение режется вдвое: важно не качество
            // отдельного кадра, а отзывчивость. Отпустил мышь — следующий кадр
            // считается в полном разрешении.
            float scale = EditorGUIUtility.pixelsPerPoint;

            if (_dragged)
            {
                // АБСОЛЮТНЫЙ БЮДЖЕТ ПИКСЕЛЕЙ, А НЕ ДОЛЯ.
                //
                // «Половина разрешения» — плохая мера: на Retina половина от
                // 1200×1600 это всё ещё 480 тысяч пикселей, и вращение остаётся
                // тяжёлым. Бюджет задаёт верхнюю границу работы прямо в
                // пикселях, поэтому поведение одинаково на любом экране.
                float wanted = rect.width * rect.height * scale * scale;
                if (wanted > DragPixelBudget)
                    scale *= Mathf.Sqrt(DragPixelBudget / wanted);
            }

            int w = Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 8, MaxViewSide);
            int h = Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 8, MaxViewSide);

            // Камера строится на ЛОКАЛЬНОМ прямоугольнике (0,0,w,h): тогда
            // проекция сразу даёт пиксели буфера, и пересчитывать координаты не
            // нужно. Соотношение сторон то же, что у панели, поэтому картинка
            // совпадает с тем, куда попадает выбор кости мышью.
            var camera = MakeCamera(new Rect(0f, 0f, w, h));

            // КЭШ КАДРА. Окно перерисовывается на каждое движение мыши, смену
            // фокуса и вообще без повода, а растеризация — самая дорогая часть
            // панели. Пока камера, выделение и поза скелета не изменились,
            // готовая текстура просто перерисовывается, и стоит это ноль.
            int key = ViewKey(skin, camera, w, h);
            if (_viewRendered && key == _viewKey && _viewTexture != null)
            {
                GUI.DrawTexture(rect, _viewTexture, ScaleMode.StretchToFill, false);
                return;
            }

            _viewKey = key;
            _viewRendered = true;

            _raster.Begin(w, h, (Color32)ViewBackground);

            // Глубинный градиент привязан к дистанции обзора, а не к мировым
            // числам: при приближении «дальним» становится то, что и должно.
            // Ослабление слабое (0.62) — задача подсказать объём, а не покрасить
            // скелет в градиент.
            _raster.FadeNearZ = Mathf.Max(0.05f, _distance * 0.55f);
            _raster.FadeFarZ = Mathf.Max(_raster.FadeNearZ + 0.1f, _distance * 1.7f);
            _raster.FadeMin = 0.62f;

            // Глубина — ПОЛНОГО разрешения.
            //
            // Половинная экономила заполнение, но ребро лежит ровно на
            // поверхности своей грани, а грубый буфер берёт глубину соседнего
            // пикселя: часть рёбер оказывалась «за» гранью и пропадала —
            // линии рвались в пунктир. Экономия не стоила сломанной картинки.
            _raster.DepthScale = 1;

            var bones = skin.bones;

            // ПРОХОД 1 — ГЛУБИНА. Грани всех октаэдров, только в буфер глубины.
            // Он обязан быть полным до первой линии: иначе линия, нарисованная
            // раньше соседней кости, не будет этой костью закрыта.
            //
            // ВО ВРЕМЯ ВРАЩЕНИЯ ПРОХОД ПРОПУСКАЕТСЯ. Заполнение граней — около
            // восьмидесяти процентов стоимости кадра, а дают они только
            // перекрытие МЕЖДУ костями. Собственная обратная сторона октаэдра и
            // так не рисуется: её отсекает разбор лицевых граней, который
            // считается без растеризации. Пока мышь ведут, разницы почти не
            // видно, а кадр дешевеет в разы; на отпускании кадр пересчитывается
            // целиком.
            // ПРОХОД 1 — ГЛУБИНА.
            //
            // Грани всех октаэдров рисуются в буфер глубины и НЕ пишут цвет:
            // «поверхность есть, её не видно». Это ровно то, что нужно — тело
            // участвует в перекрытии, но в кадре его нет.
            //
            // Именно грани, а не что-то приближённое, дают правильный ответ на
            // вопрос «видно ли это ребро»: у настоящего октаэдра, повёрнутого
            // под углом, задние рёбра закрыты его же передними гранями, и здесь
            // это получается само, без единой строчки логики про видимость.
            //
            // Проход обязан закончиться ДО первой линии: иначе ребро, нарисованное
            // раньше соседней кости, не будет этой костью закрыто.
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;

                BoneSegments(bone, bones);
                for (int s = 0; s < _segFrom.Count; s++)
                    AddOctahedronFaces(camera, _segFrom[s], _segTo[s]);
            }

            // ПРОХОД 2 — проволока. Только рёбра, и каждое проходит тест
            // глубины: всё, что закрыто любой гранью — своей или чужой, — не
            // рисуется.
            _rulerFocusReady = false;
            AddGround(camera, rect);
            AddRuler(camera, rect);

            var boneColor = (Color32)new Color(0.72f, 0.76f, 0.82f);
            var accent = (Color32)Dyc_Style.Accent;

            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;

                var color = bone == _selected ? accent : boneColor;
                BoneSegments(bone, bones);
                for (int s = 0; s < _segFrom.Count; s++)
                    AddOctahedronEdges(camera, _segFrom[s], _segTo[s], color);
            }

            Upload(rect, w, h);
        }

        /// <summary>
        /// Отпечаток вида: всё, от чего зависит кадр. Сравнивается одним целым,
        /// поэтому перерисовка происходит ровно тогда, когда картинка обязана
        /// измениться, и ни разом чаще.
        ///
        /// Позиции костей входят в отпечаток, хотя вид и статичен: скелет можно
        /// двигать, и без них кадр остался бы от прошлой позы.
        /// </summary>
        int ViewKey(SkinnedMeshRenderer skin, ViewCamera camera, int w, int h)
        {
            int hash = 17;
            hash = hash * 31 + w;
            hash = hash * 31 + h;
            hash = hash * 31 + skin.GetInstanceID();
            hash = hash * 31 + (_selected != null ? _selected.GetInstanceID() : 0);
            hash = hash * 31 + camera.position.GetHashCode();
            hash = hash * 31 + camera.rotation.GetHashCode();
            hash = hash * 31 + camera.focal.GetHashCode();

            var bones = skin.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                hash = hash * 31 + bone.position.GetHashCode();
            }

            return hash;
        }

        void Upload(Rect rect, int w, int h)
        {
            if (_viewTexture == null || _viewTexture.width != w || _viewTexture.height != h)
            {
                if (_viewTexture != null) Object.DestroyImmediate(_viewTexture);
                _viewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
            }

            _viewTexture.SetPixels32(_raster.ColorBuffer);
            _viewTexture.Apply(false);
            GUI.DrawTexture(rect, _viewTexture, ScaleMode.StretchToFill, false);
        }

        void AddSegment(ViewCamera camera, Vector3 from, Vector3 to, Color32 color)
        {
            if (!camera.ProjectDepth(from, out var a, out float za)) return;
            if (!camera.ProjectDepth(to, out var b, out float zb)) return;
            _raster.Line(a, za, b, zb, color);
        }

        /// <summary>Лицевые грани текущего октаэдра. Одна на кадр, без аллокаций.</summary>
        static readonly bool[] _faceFront = new bool[8];

        /// <summary>
        /// Определяет, какие из восьми граней смотрят на камеру. Считается ОДИН
        /// раз на октаэдр: этим пользуются и заливка глубины, и выбор толщины
        /// рёбер, а раньше признак лицевой грани вычислялся в каждом из двух
        /// проходов заново.
        /// </summary>
        void ComputeFaces(ViewCamera camera, Vector3 center, Vector3 p, Vector3 a,
            Vector3 e0, Vector3 e1, Vector3 e2, Vector3 e3)
        {
            Vector3 cam = camera.position;
            _faceFront[0] = FrontFacing(p, e0, e1, center, cam);
            _faceFront[1] = FrontFacing(p, e1, e2, center, cam);
            _faceFront[2] = FrontFacing(p, e2, e3, center, cam);
            _faceFront[3] = FrontFacing(p, e3, e0, center, cam);
            _faceFront[4] = FrontFacing(a, e0, e3, center, cam);
            _faceFront[5] = FrontFacing(a, e1, e0, center, cam);
            _faceFront[6] = FrontFacing(a, e2, e1, center, cam);
            _faceFront[7] = FrontFacing(a, e3, e2, center, cam);
        }

        /// <summary>
        /// Тело, закрывающее то, что за ним: вытянутый октаэдр кости.
        ///
        /// Хранится как центр плюс ортонормированный базис, в котором октаэдр
        /// становится единичным шаром в L1-метрике. Это и делает проверку
        /// попадания одной строчкой без всякой растеризации.
        /// </summary>
        struct Occluder
        {
            public Vector3 center;
            public Vector3 axis;
            public Vector3 right;
            public Vector3 up;
            public float radius;
            public float half;      // половина длины кости
            public float axisScale; // radius / half — перевод вдоль оси
        }

        readonly List<Occluder> _occluders = new List<Occluder>(128);

        void AddOccluder(Vector3 from, Vector3 to)
        {
            if (!Octahedron(from, to, out _, out _, out _, out _, out _, out _, out var center,
                    out var radius)) return;

            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f) return;
            axis /= length;

            Vector3 right = Vector3.Cross(axis, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(axis, right).normalized;

            float half = length * 0.5f;

            _occluders.Add(new Occluder
            {
                center = center,
                axis = axis,
                right = right,
                up = up,
                radius = radius,
                half = half,
                axisScale = radius / Mathf.Max(half, 1e-6f)
            });
        }

        /// <summary>
        /// Точка внутри октаэдра. В базисе кости октаэдр — это |u|+|v|+|w|·(r/h) ≤ r,
        /// то есть обычный ромб в L1-метрике. Никаких граней, никаких нормалей,
        /// никакой намотки — сравнение четырёх чисел.
        /// </summary>
        static bool Inside(in Occluder o, Vector3 point)
        {
            Vector3 d = point - o.center;
            float u = Mathf.Abs(Vector3.Dot(d, o.right));
            float v = Mathf.Abs(Vector3.Dot(d, o.up));
            float w = Mathf.Abs(Vector3.Dot(d, o.axis)) * o.axisScale;
            return u + v + w <= o.radius;
        }

        /// <summary>
        /// Рисует отрезок, выкидывая его ЧАСТИ, попавшие внутрь чужих костей.
        ///
        /// Именно частями, а не целиком: раньше перекрытие решалось буфером
        /// глубины, и кость, задевшая соседнюю хоть краем, пропадала вся.
        /// Здесь отрезок разбивается на пробы, каждая проверяется на попадание,
        /// и рисуются только непрерывные видимые участки. Своё собственное тело
        /// исключено по индексу — поэтому каркас кости всегда целый.
        /// </summary>
        void AddOccludedSegment(ViewCamera camera, Vector3 a, Vector3 b, Color32 color,
            float halfWidth, int self)
        {
            const int samples = 10;

            int visible = 0;
            for (int s = 0; s < samples; s++)
            {
                Vector3 point = Vector3.Lerp(a, b, (s + 0.5f) / samples);

                bool hidden = false;
                for (int o = 0; o < _occluders.Count; o++)
                {
                    if (o == self) continue;
                    if (!Inside(_occluders[o], point)) continue;
                    hidden = true;
                    break;
                }

                if (!hidden) visible |= 1 << s;
            }

            if (visible == 0) return;

            int runStart = -1;
            for (int s = 0; s <= samples; s++)
            {
                bool on = s < samples && (visible & (1 << s)) != 0;

                if (on)
                {
                    if (runStart < 0) runStart = s;
                    continue;
                }

                if (runStart < 0) continue;

                Vector3 p0 = Vector3.Lerp(a, b, runStart / (float)samples);
                Vector3 p1 = Vector3.Lerp(a, b, s / (float)samples);
                runStart = -1;

                if (!camera.ProjectDepth(p0, out var g0, out float z0)) continue;
                if (!camera.ProjectDepth(p1, out var g1, out float z1)) continue;

                _raster.Line(g0, z0, g1, z1, color, halfWidth);
            }
        }

        void AddOctahedronFaces(ViewCamera camera, Vector3 from, Vector3 to)
        {
            if (!Octahedron(from, to, out var p, out var a, out var e0, out var e1, out var e2, out var e3,
                    out var center, out _)) return;

            ComputeFaces(camera, center, p, a, e0, e1, e2, e3);

            // Задние грани не растеризуем: они всё равно проигрывают по глубине
            // лицевым, а работа — половина всей заливки.
            if (_faceFront[0]) AddFaceRaw(camera, p, e0, e1);
            if (_faceFront[1]) AddFaceRaw(camera, p, e1, e2);
            if (_faceFront[2]) AddFaceRaw(camera, p, e2, e3);
            if (_faceFront[3]) AddFaceRaw(camera, p, e3, e0);
            if (_faceFront[4]) AddFaceRaw(camera, a, e0, e3);
            if (_faceFront[5]) AddFaceRaw(camera, a, e1, e0);
            if (_faceFront[6]) AddFaceRaw(camera, a, e2, e1);
            if (_faceFront[7]) AddFaceRaw(camera, a, e3, e2);
        }

        void AddFaceRaw(ViewCamera camera, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!camera.ProjectDepth(a, out var pa, out float za)) return;
            if (!camera.ProjectDepth(b, out var pb, out float zb)) return;
            if (!camera.ProjectDepth(c, out var pc, out float zc)) return;

            _raster.Face(pa, za, pb, zb, pc, zc);
        }

        void AddOctahedronEdges(ViewCamera camera, Vector3 from, Vector3 to, Color32 color)
        {
            if (!Octahedron(from, to, out var p, out var a, out var e0, out var e1, out var e2, out var e3,
                    out var center, out _)) return;

            ComputeFaces(camera, center, p, a, e0, e1, e2, e3);

            // КОНТУР ТОЛЩЕ. Ребро между лицевой и тыльной гранью — это силуэт
            // фигуры. Именно он делает октаэдр читаемым объёмом; когда все
            // двенадцать рёбер одной толщины, получается клубок.
            //
            // Порядок аргументов — номера двух граней, которым ребро принадлежит
            // (нумерация как в ComputeFaces).
            AddEdge(camera, p, e0, 0, 3, color);
            AddEdge(camera, p, e1, 0, 1, color);
            AddEdge(camera, p, e2, 1, 2, color);
            AddEdge(camera, p, e3, 2, 3, color);

            AddEdge(camera, e0, a, 4, 5, color);
            AddEdge(camera, e1, a, 5, 6, color);
            AddEdge(camera, e2, a, 6, 7, color);
            AddEdge(camera, e3, a, 7, 4, color);

            AddEdge(camera, e0, e1, 0, 5, color);
            AddEdge(camera, e1, e2, 1, 6, color);
            AddEdge(camera, e2, e3, 2, 7, color);
            AddEdge(camera, e3, e0, 3, 4, color);
        }

        void AddEdge(ViewCamera camera, Vector3 a, Vector3 b, int faceA, int faceB, Color32 color)
        {
            // Силуэт (одна грань к камере, вторая от) рисуется толще — это
            // контур фигуры. Сама видимость решается тестом глубины: грань уже
            // лежит в буфере, и ребро за ней просто не пройдёт проверку.
            float half = _faceFront[faceA] != _faceFront[faceB] ? 1.3f : 0.6f;

            if (!camera.ProjectDepth(a, out var pa, out float za)) return;
            if (!camera.ProjectDepth(b, out var pb, out float zb)) return;

            _raster.Line(pa, za, pb, zb, color, half);
        }

        /// <summary>Шесть вершин вытянутого октаэдра: две на оси, четыре на экваторе.</summary>
        static bool Octahedron(Vector3 from, Vector3 to,
            out Vector3 p, out Vector3 a, out Vector3 e0, out Vector3 e1, out Vector3 e2, out Vector3 e3,
            out Vector3 center, out float radius)
        {
            p = from;
            a = to;
            e0 = e1 = e2 = e3 = Vector3.zero;
            radius = 0f;

            Vector3 axis = to - from;
            float length = axis.magnitude;
            center = (from + to) * 0.5f;
            if (length < 1e-5f) return false;
            axis /= length;

            Vector3 right = Vector3.Cross(axis, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(axis, right).normalized;

            radius = Mathf.Max(length * 0.12f, 0.004f);

            e0 = center + right * radius;
            e1 = center + up * radius;
            e2 = center - right * radius;
            e3 = center - up * radius;
            return true;
        }

        /// <summary>Орбитальная камера панели: положение, поворот и поле зрения.</summary>
        struct ViewCamera
        {
            public Vector3 position;
            public Quaternion rotation;

            /// <summary>
            /// Обратный поворот, посчитанный ОДИН раз при сборке камеры.
            ///
            /// Раньше Quaternion.Inverse вызывался в каждом Project — то есть
            /// на каждую вершину каждого октаэдра, дважды за кадр. На сотне
            /// костей это тысячи инверсий кватерниона на кадр, и заметная часть
            /// из них — прямо в момент вращения, когда важна каждая
            /// миллисекунда.
            /// </summary>
            public Quaternion inverseRotation;

            public float focal;     // фокус в пикселях
            public Rect rect;

            /// <summary>
            /// Ортографическая проекция. Включается щелчком по центру куба
            /// ориентации: для «вид спереди/сбоку» перспектива вредна — по ней
            /// нельзя сравнивать размеры, а именно за этим такие виды и нужны.
            /// </summary>
            public bool orthographic;

            /// <summary>Высота кадра в метрах для ортопроекции.</summary>
            public float orthoHeight;

            /// <summary>Мир → экран в нужной проекции. Общая часть обеих ветвей.</summary>
            Vector2 ToScreen(Vector3 local)
            {
                if (orthographic)
                {
                    float k = rect.height / Mathf.Max(0.01f, orthoHeight);
                    return new Vector2(
                        rect.x + rect.width * 0.5f + local.x * k,
                        rect.y + rect.height * 0.5f - local.y * k);
                }

                return new Vector2(
                    rect.x + rect.width * 0.5f + local.x * focal / local.z,
                    rect.y + rect.height * 0.5f - local.y * focal / local.z);
            }

            /// <summary>Мир → экран. false, если точка за камерой.</summary>
            public bool Project(Vector3 world, out Vector2 gui)
            {
                gui = Vector2.zero;
                Vector3 local = inverseRotation * (world - position);
                if (local.z <= 0.02f) return false;

                gui = ToScreen(local);
                return true;
            }

            /// <summary>
            /// То же, но с глубиной вдоль взгляда: она нужна растеризатору.
            /// Считается здесь, а не отдельной формулой, чтобы экранная позиция
            /// и глубина гарантированно были из одного преобразования.
            /// </summary>
            public bool ProjectDepth(Vector3 world, out Vector2 gui, out float depth)
            {
                gui = Vector2.zero;
                depth = 0f;

                Vector3 local = inverseRotation * (world - position);
                if (local.z <= 0.02f) return false;

                depth = local.z;
                gui = ToScreen(local);
                return true;
            }
        }

        Vector3 FocusPoint(SkinnedMeshRenderer skin)
        {
            if (_pivot != null) return _pivot.position + _pivotOffset;
            if (_selected != null) return _selected.position + _pivotOffset;
            return (skin != null ? skin.bounds.center : Vector3.zero) + _pivotOffset;
        }

        ViewCamera MakeCamera(Rect rect)
        {
            var skin = SourceSkin();
            Vector3 focus = FocusPoint(skin);

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            return new ViewCamera
            {
                position = focus - rotation * Vector3.forward * _distance,
                rotation = rotation,
                inverseRotation = Quaternion.Inverse(rotation),
                focal = rect.height * 0.5f / Mathf.Tan(30f * Mathf.Deg2Rad),

                // Ортокадру задаётся высота, равная кадру перспективы на той же
                // дистанции: переключение проекции не меняет масштаб, и взгляд
                // не «прыгает».
                orthographic = _orthographic,
                orthoHeight = 2f * _distance * Mathf.Tan(30f * Mathf.Deg2Rad),

                rect = rect
            };
        }

        /// <summary>Сетка на полу: то, что просили, и то, что превью-утилита не давала.</summary>
        void DrawGrid(ViewCamera camera)
        {
            const int half = 6;
            const float step = 0.25f;

            BeginLines(Dyc_Style.Line);
            for (int i = -half; i <= half; i++)
            {
                float offset = i * step;

                if (camera.Project(new Vector3(offset, 0f, -half * step), out var a1) &&
                    camera.Project(new Vector3(offset, 0f, half * step), out var b1))
                    Line(a1, b1);

                if (camera.Project(new Vector3(-half * step, 0f, offset), out var a2) &&
                    camera.Project(new Vector3(half * step, 0f, offset), out var b2))
                    Line(a2, b2);
            }
            EndLines();
        }

        void DrawSkeleton(ViewCamera camera, SkinnedMeshRenderer skin)
        {
            var bones = skin.bones;

            BeginLines(new Color(0.72f, 0.76f, 0.82f));
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                if (_selected == bone) continue;      // выделенную рисуем отдельно, поверх

                BoneSegments(bone, bones);
                for (int s = 0; s < _segFrom.Count; s++)
                    DrawBoneShape(camera, _segFrom[s], _segTo[s]);
            }
            EndLines();

            if (_selected == null) return;

            BeginLines(Dyc_Style.Accent);
            BoneSegments(_selected, bones);
            for (int s = 0; s < _segFrom.Count; s++)
                DrawBoneShape(camera, _segFrom[s], _segTo[s]);
            EndLines();
        }

        static readonly List<Vector3> _segFrom = new List<Vector3>(8);
        static readonly List<Vector3> _segTo = new List<Vector3>(8);

        /// <summary>
        /// Отрезки кости — ОДНИ И ТЕ ЖЕ для отрисовки и для выбора, иначе
        /// попадание разъезжается с картинкой.
        ///
        /// У кости с НЕСКОЛЬКИМИ детьми (кисть, таз, позвоночник) отрезок
        /// рисуется на КАЖДОГО ребёнка. Раньше брался только первый, и связки
        /// «кисть → остальные пальцы» на картинке просто отсутствовали —
        /// это и читалось как «скелет отображается неправильно».
        ///
        /// У ЛИСТОВОЙ кости детей нет, и раньше она не рисовалась и не
        /// выбиралась ВООБЩЕ: кончики пальцев и носков пропадали. Лист
        /// продолжает направление родителя на половину его длины — этого
        /// достаточно, чтобы его было видно и по нему можно было попасть.
        /// </summary>
        static void BoneSegments(Transform bone, Transform[] bones)
        {
            _segFrom.Clear();
            _segTo.Clear();

            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (b == null || b.parent != bone) continue;
                if ((b.position - bone.position).sqrMagnitude < 1e-10f) continue;

                _segFrom.Add(bone.position);
                _segTo.Add(b.position);
            }

            if (_segFrom.Count > 0) return;

            if (bone.parent == null) return;

            Vector3 dir = bone.position - bone.parent.position;
            float len = dir.magnitude;
            if (len < 1e-5f) return;

            _segFrom.Add(bone.position);
            _segTo.Add(bone.position + dir / len * (len * 0.5f));
        }

        /// <summary>
        /// Кость рисуется ПРОВОЛОЧНЫМ ВЫТЯНУТЫМ ОКТАЭДРОМ: две вершины на оси
        /// кости и четыре вершины экватора посередине, 12 рёбер.
        ///
        /// ПОЧЕМУ ИМЕННО ОКТАЭДР, А НЕ ПЛОСКИЙ РОМБ. Раньше фигура считалась в
        /// ЭКРАННЫХ координатах: от спроецированной оси брался экранный
        /// перпендикуляр, и рисовался плоский ромб. Такой ромб всегда развёрнут
        /// к камере — это не объём, а билборд. Пока смотришь сбоку, он похож на
        /// кость, а стоит посмотреть сверху или снизу — и он схлопывается в
        /// полоску: глубины у него нет по построению.
        ///
        /// Здесь вершины задаются В МИРЕ (две на оси, четыре на экваторе в
        /// плоскости, перпендикулярной оси) и только потом проецируются. Поэтому
        /// фигура объёмная: с любого ракурса у неё есть толщина.
        ///
        /// Экватор ровно посередине — это и есть вытянутый октаэдр: правильный
        /// октаэдр, растянутый вдоль кости. Радиус берётся от длины кости, чтобы
        /// фаланги не исчезали рядом с бедром.
        /// </summary>
        static void DrawBoneShape(ViewCamera camera, Vector3 from, Vector3 to)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f) return;
            axis /= length;

            // Базис экватора. Если кость смотрит почти вертикально, «вверх»
            // вырождается — берём другую ось.
            Vector3 right = Vector3.Cross(axis, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(axis, right).normalized;

            float radius = Mathf.Max(length * 0.12f, 0.004f);
            Vector3 mid = (from + to) * 0.5f;

            Vector3 e0 = mid + right * radius;
            Vector3 e1 = mid + up * radius;
            Vector3 e2 = mid - right * radius;
            Vector3 e3 = mid - up * radius;

            if (!camera.Project(from, out var pFrom)) return;
            if (!camera.Project(to, out var pTo)) return;
            if (!camera.Project(e0, out var q0)) return;
            if (!camera.Project(e1, out var q1)) return;
            if (!camera.Project(e2, out var q2)) return;
            if (!camera.Project(e3, out var q3)) return;

            // ОТСЕЧЕНИЕ ОБРАТНОЙ СТОРОНЫ.
            //
            // Оболочка октаэдра выпуклая, поэтому «невидимое» здесь — это ровно
            // то, что закрыто ею же самой. Ребро видно тогда и только тогда,
            // когда хотя бы одна из ДВУХ примыкающих к нему граней смотрит на
            // камеру. Это и есть «грани с пустым материалом»: заливки нет, но
            // закрытые рёбра не рисуются.
            //
            // Грани (8 штук, по 3 вершины) обходятся в порядке экватора, а
            // «смотрит ли грань» проверяется по направлению от центра тела к
            // центру грани — у выпуклого тела это и есть внешняя нормаль, и
            // проверка не зависит от порядка обхода вершин.
            Vector3 camPos = camera.position;
            Vector3 center = mid;

            bool fPE0E1 = FrontFacing(from, e0, e1, center, camPos);
            bool fPE1E2 = FrontFacing(from, e1, e2, center, camPos);
            bool fPE2E3 = FrontFacing(from, e2, e3, center, camPos);
            bool fPE3E0 = FrontFacing(from, e3, e0, center, camPos);
            bool fAE0E3 = FrontFacing(to, e0, e3, center, camPos);
            bool fAE1E0 = FrontFacing(to, e1, e0, center, camPos);
            bool fAE2E1 = FrontFacing(to, e2, e1, center, camPos);
            bool fAE3E2 = FrontFacing(to, e3, e2, center, camPos);

            // Верхняя половина: рёбра к вершине кости.
            if (fPE3E0 || fPE0E1) Line(pFrom, q0);
            if (fPE0E1 || fPE1E2) Line(pFrom, q1);
            if (fPE1E2 || fPE2E3) Line(pFrom, q2);
            if (fPE2E3 || fPE3E0) Line(pFrom, q3);

            // Нижняя половина: рёбра к основанию.
            if (fAE0E3 || fAE1E0) Line(q0, pTo);
            if (fAE1E0 || fAE2E1) Line(q1, pTo);
            if (fAE2E1 || fAE3E2) Line(q2, pTo);
            if (fAE3E2 || fAE0E3) Line(q3, pTo);

            // Экваториальное кольцо — оно и даёт объём при взгляде вдоль оси.
            if (fPE0E1 || fAE1E0) Line(q0, q1);
            if (fPE1E2 || fAE2E1) Line(q1, q2);
            if (fPE2E3 || fAE3E2) Line(q2, q3);
            if (fPE3E0 || fAE0E3) Line(q3, q0);
        }

        /// <summary>
        /// Смотрит ли грань на камеру. Для выпуклого тела направление от центра
        /// к центру грани — внешняя нормаль, поэтому знак не зависит от порядка
        /// обхода вершин: путаница с намоткой здесь была бы неочевидной ошибкой.
        /// </summary>
        static bool FrontFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 bodyCenter, Vector3 camPos)
        {
            Vector3 centroid = (a + b + c) / 3f;
            return Vector3.Dot(centroid - bodyCenter, camPos - centroid) > 0f;
        }

        // ------------------------------------------------------------------ оформление панелей

        static Texture2D _panelTexture;
        static GUIStyle _panelStyle;

        /// <summary>
        /// Непрозрачный фон панели.
        ///
        /// Штатный helpBox полупрозрачный, и через него было видно сетку пола из
        /// соседней панели — читалось как грязное стекло. Своя текстура 1×1
        /// нужного цвета решает это без стилевых файлов.
        /// </summary>
        static GUIStyle PanelStyle()
        {
            if (_panelStyle != null) return _panelStyle;

            _panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _panelTexture.SetPixel(0, 0, new Color(0.19f, 0.20f, 0.22f, 1f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle(EditorStyles.helpBox) { normal = { background = _panelTexture } };
            return _panelStyle;
        }

        // ------------------------------------------------------------------ рисование линиями

        /// <summary>
        /// Линии рисуются СРЕДСТВАМИ IMGUI, а не через GL.
        ///
        /// Почему отказались от GL. Он рисует в пикселях экрана, а GUI — в
        /// координатах окна, и между ними лежат два независимых множителя:
        /// масштаб экрана (Retina) и положение окна. Я ошибался в них дважды
        /// подряд — содержимое уезжало то в левый верхний угол, то в правый
        /// нижний, — и проверять каждую догадку приходилось на живой Unity.
        ///
        /// Здесь координата одна: та, которую вернула проекция. Отрезок
        /// поворачивается вокруг своего начала и рисуется текстурой 1×1 нужной
        /// длины. Никаких переводов, никаких множителей — ошибиться негде.
        /// </summary>
        static Texture2D _lineTexture;
        static Color _lineColor = Color.white;

        static Texture2D LineTexture()
        {
            if (_lineTexture != null) return _lineTexture;

            _lineTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _lineTexture.SetPixel(0, 0, Color.white);
            _lineTexture.Apply();
            return _lineTexture;
        }

        static void BeginLines(Color color)
        {
            _lineColor = color;
        }

        static void EndLines()
        {
        }

        static void Line(Vector2 a, Vector2 b)
        {
            if (!ClipSegment(ref a, ref b, _clipRect)) return;

            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.6f) return;

            var texture = LineTexture();
            if (texture == null) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;

            GUIUtility.RotateAroundPivot(angle, a);
            GUI.color = _lineColor;
            GUI.DrawTexture(new Rect(a.x, a.y - 0.5f, length, 1.2f), texture);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        static Rect _clipRect;

        /// <summary>Отсечение отрезка по прямоугольнику (Лианг–Барски).
        /// Нужно, потому что IMGUI не обрезает повёрнутые текстуры по границам
        /// группы, и без этого линии вылезали на соседние панели.</summary>
        static bool ClipSegment(ref Vector2 a, ref Vector2 b, Rect clip)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float t0 = 0f, t1 = 1f;

            if (!ClipTest(-dx, a.x - clip.xMin, ref t0, ref t1)) return false;
            if (!ClipTest(dx, clip.xMax - a.x, ref t0, ref t1)) return false;
            if (!ClipTest(-dy, a.y - clip.yMin, ref t0, ref t1)) return false;
            if (!ClipTest(dy, clip.yMax - a.y, ref t0, ref t1)) return false;

            Vector2 original = a;
            a = original + new Vector2(dx, dy) * t0;
            b = original + new Vector2(dx, dy) * t1;
            return true;
        }

        static bool ClipTest(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0f;

            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        // ------------------------------------------------------------------ ввод

        void HandleOrbit(Rect rect)
        {
            var e = Event.current;

            // Куб ориентации лежит ВНУТРИ панели, и без этого щелчок по нему
            // уходил бы в орбиту: камера начинала бы вращаться вместо
            // разворота по грани.
            if (ViewCubeRect(rect).Contains(e.mousePosition)) return;

            if (!rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseUp) _dragged = false;
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    _mouseDown = e.mousePosition;
                    _dragged = false;
                    break;

                case EventType.MouseDrag:
                    // ПРАВАЯ — вращение вокруг центра, ЛЕВАЯ — панорама.
                    // Разделение как в Scene: вращение меняет точку обзора,
                    // панорама двигает сам центр, и смешивать их в одной кнопке
                    // значит всё время попадать не туда.
                    if (e.button == 1)
                    {
                        _yaw += e.delta.x * 0.4f;

                        // Инверсия включена ПО УМОЛЧАНИЮ: тянешь мышь вниз —
                        // камера опускается.
                        float vertical = e.delta.y * 0.4f * (_invertY ? 1f : -1f);
                        _pitch = Mathf.Clamp(_pitch + vertical, -85f, 85f);

                        _dragged = true;
                        e.Use();
                        Repaint();
                    }
                    else if (e.button == 0)
                    {
                        Pan(e.delta);
                        _dragged = true;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    // Выбор — только если это был ЩЕЛЧОК, а не панорамирование:
                    // иначе каждый сдвиг камеры левой кнопкой менял бы выбор.
                    if (e.button == 0 && !_dragged) Pick(rect, e.mousePosition, e.clickCount);
                    _dragged = false;
                    break;

                case EventType.ScrollWheel:
                    _distance = Mathf.Clamp(_distance * (1f + e.delta.y * 0.05f), 0.2f, 30f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>Панорама: центр вращения сдвигается в плоскости экрана.
        /// Шаг пропорционален расстоянию, иначе вблизи панорама не двигает
        /// ничего, а вдали улетает.</summary>
        void Pan(Vector2 delta)
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            float scale = _distance * 0.0016f;

            _pivotOffset -= rotation * Vector3.right * (delta.x * scale);
            _pivotOffset += rotation * Vector3.up * (delta.y * scale);
        }

        /// <summary>
        /// Выбор кости: сравнение в ЭКРАННЫХ координатах.
        ///
        /// Кость на экране — это отрезок, и расстояние до него считается в
        /// пикселях. Порог в пикселях, а не в метрах: пользователь целится
        /// мышью, и «попасть в кость» означает попасть по тому, что он видит.
        /// </summary>
        void Pick(Rect rect, Vector2 mouse, int clickCount)
        {
            var skin = SourceSkin();
            if (skin == null || skin.bones == null) return;

            var camera = MakeCamera(rect);


            float best = float.MaxValue;
            Transform picked = null;

            var bones = skin.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;

                BoneSegments(bone, bones);
                for (int s = 0; s < _segFrom.Count; s++)
                {
                    Vector3 from = _segFrom[s], to = _segTo[s];
                    if (!camera.Project(from, out var a) || !camera.Project(to, out var b)) continue;

                    // Попадание — по расстоянию до ОТРЕЗКА кости, потому что
                    // кость теперь проволочная. Допуск не меньше шести пикселей:
                    // по линии толщиной в пиксель целиться невозможно, и именно
                    // из-за этого раньше рисовали заливку.
                    float radius = Mathf.Max(BoneHalfWidth(camera, from, to), 6f);
                    float distance = DistanceToSegment2D(mouse, a, b);
                    if (distance > radius || distance >= best) continue;

                    best = distance;
                    picked = bone;
                }
            }

            if (picked == null) return;

            Select(picked);

            // ДВОЙНОЙ ЩЕЛЧОК переносит центр вращения в кость — как двойной
            // щелчок по объекту в иерархии. Смещение панорамы при этом
            // сбрасывается, иначе новый центр оказался бы сдвинут на старую.
            if (clickCount >= 2)
            {
                _pivot = picked;
                _pivotOffset = Vector3.zero;
                Repaint();
            }
        }

        /// <summary>
        /// Радиус экватора октаэдра в экранных пикселях. Считается по ТОЙ ЖЕ
        /// формуле, что и сама фигура: если допуск для попадания взять из другой
        /// формулы, курсор будет промахиваться мимо видимых рёбер.
        /// </summary>
        static float BoneHalfWidth(ViewCamera camera, Vector3 from, Vector3 to)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f) return 0f;
            axis /= length;

            Vector3 right = Vector3.Cross(axis, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();

            float radius = Mathf.Max(length * 0.12f, 0.004f);
            Vector3 mid = (from + to) * 0.5f;

            if (!camera.Project(mid, out var center)) return 0f;
            if (!camera.Project(mid + right * radius, out var edge)) return 0f;

            return (edge - center).magnitude;
        }

        static Vector2 Perpendicular(Vector2 a, Vector2 b)
        {
            Vector2 axis = (b - a).normalized;
            return new Vector2(-axis.y, axis.x);
        }

        /// <summary>Точка внутри треугольника: три одинаковых знака векторных
        /// произведений. Дешевле и надёжнее, чем пересечение луча.</summary>
        static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(point - a, b - a);
            float d2 = Cross(point - b, c - b);
            float d3 = Cross(point - c, a - c);

            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(negative && positive);
        }

        static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        static float DistanceToSegment2D(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 1e-6f) return (point - a).magnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return (point - (a + ab * t)).magnitude;
        }

        static Transform FirstChild(Transform bone, Transform[] bones)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].parent == bone) return bones[i];
            return null;
        }

        void FrameSelection()
        {
            var skin = SourceSkin();
            if (skin == null) return;

            _distance = Mathf.Max(0.5f, skin.bounds.extents.magnitude * 2.2f);
            Repaint();
        }

        // ------------------------------------------------------------------ панель 4: детали

        void DrawDetail()
        {
            EditorGUILayout.LabelField(Dyc_L10n.T("exp.detail"), EditorStyles.boldLabel);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, PanelStyle(),
                GUILayout.ExpandHeight(true));

            if (_selected == null)
            {
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.pickBone"), EditorStyles.miniLabel);
            }
            else
            {
                string path = Dyc_Baker.PathOf(_target.transform, _selected);
                var stat = StatFor(_selected);

                EditorGUILayout.LabelField(_selected.name, EditorStyles.boldLabel);

                if (stat == null)
                {
                    EditorGUILayout.LabelField(Dyc_L10n.T("exp.noStat"), EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(Dyc_L10n.T("exp.triangles"), stat.triangles.ToString());
                    EditorGUILayout.LabelField(Dyc_L10n.T("exp.pieces"), stat.pieces.ToString());
                    EditorGUILayout.LabelField(Dyc_L10n.T("exp.concavity"), stat.concavity.ToString("0.000"));
                    EditorGUILayout.LabelField(Dyc_L10n.T("exp.volume"), stat.volume.ToString("0.0000") + " м³");

                    if (stat.dropped > 0)
                        EditorGUILayout.HelpBox(Dyc_L10n.T("exp.dropped", stat.dropped), MessageType.Warning);
                }

                EditorGUILayout.Space(6f);

                var manual = BudgetFor(path);
                int pieces = EditorGUILayout.IntSlider(Dyc_L10n.T("exp.manualPieces"),
                    manual != null ? manual.pieces : _target.BudgetFor(path), 1, 32);

                bool locked = manual != null && manual.locked;
                bool newLocked = EditorGUILayout.Toggle(Dyc_L10n.T("exp.locked"), locked);

                if (pieces != (manual != null ? manual.pieces : _target.BudgetFor(path)) || newLocked != locked)
                {
                    Undo.RecordObject(_target, "NDC Bone Budget");
                    SetBudget(path, pieces, newLocked);
                    EditorUtility.SetDirty(_target);
                }

                if (manual != null && GUILayout.Button(Dyc_L10n.T("exp.releaseBone")))
                {
                    Undo.RecordObject(_target, "NDC Release Bone Budget");
                    _target.BoneBudgets.Remove(manual);
                    EditorUtility.SetDirty(_target);
                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(Dyc_L10n.T("exp.hullList"), EditorStyles.boldLabel);
                DrawHullsOf(path);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawHullsOf(string bonePath)
        {
            var set = _target.BakedSet;
            if (set == null || set.hulls == null) return;

            int shown = 0;
            for (int i = 0; i < set.hulls.Count && shown < 24; i++)
            {
                var hull = set.hulls[i];
                if (hull == null || hull.bonePath != bonePath) continue;

                EditorGUILayout.LabelField(
                    Dyc_L10n.T("exp.hullRow", shown + 1, hull.vertexCount, hull.volume),
                    EditorStyles.miniLabel);
                shown++;
            }

            if (shown == 0) EditorGUILayout.LabelField(Dyc_L10n.T("exp.noHulls"), EditorStyles.miniLabel);
        }

        DycBoneBudget BudgetFor(string bonePath)
        {
            var list = _target.BoneBudgets;
            if (list == null) return null;

            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].bonePath == bonePath) return list[i];

            return null;
        }

        void SetBudget(string bonePath, int pieces, bool locked)
        {
            var budget = BudgetFor(bonePath);
            if (budget == null)
            {
                budget = new DycBoneBudget { bonePath = bonePath };
                _target.BoneBudgets.Add(budget);
            }

            budget.pieces = Mathf.Clamp(pieces, 1, 32);
            budget.locked = locked;
        }

        // ------------------------------------------------------------------ вспомогательное

        SkinnedMeshRenderer SourceSkin()
        {
            if (_target == null) return null;
            if (_target.SourceSkin != null) return _target.SourceSkin;
            return _target.GetComponentInChildren<SkinnedMeshRenderer>();
        }
    }
}
