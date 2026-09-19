using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Единая точка входа для всех «действий». Кнопки быстрого исправления в панели диагностики,
    /// кнопки в Inspector, кнопки в окне и пункты меню идут сюда — так поведение всегда одинаковое.
    /// </summary>
    public static class Dyc_Menu
    {
        /// <summary>
        /// Корневой путь в верхней панели. Все плагины NekoWorks делят один слот в топбаре,
        /// каждый занимает своё подменю, так что сколько плагинов ни ставь — верхнюю панель
        /// Unity по ширине не разорвёт. NSG использует NekoWorks/NSG/, здесь — NekoWorks/NDC/.
        /// </summary>
        public const string WorksRoot = "NekoWorks/";
        public const string MenuRoot = WorksRoot + "NekoDynamicCollision/";

        // ------------------------------------------------------------------ Группы второго уровня
        //
        // ЗАЧЕМ ВТОРОЙ УРОВЕНЬ. Плоский список из двенадцати пунктов в
        // выпадающем меню читается как стена: «Collision Health» оказывается
        // рядом с «Mirror RTL Interface», и глаз не находит нужное. Группы
        // разделяют список по СМЫСЛУ действия — окно, запекание, gizmo,
        // инструменты, диагностика, справка, — и до нужного пункта остаётся
        // один уровень, а не поиск по стене.
        //
        // Имена групп — английские и не локализуются: это путь в атрибуте
        // [MenuItem], а он константа времени компиляции. Локализуются листья
        // (см. Dyc_MenuRuntime, там группа добавляется к переведённой подписи).
        public const string GWindow = MenuRoot + "Window/";
        public const string GBake = MenuRoot + "Bake/";
        public const string GGizmo = MenuRoot + "Gizmo/";
        public const string GTools = MenuRoot + "Tools/";
        public const string GDiag = MenuRoot + "Diagnostics/";
        public const string GHelp = MenuRoot + "Help/";

        /// <summary>Приоритеты меню NDC начинаются с 2000, чтобы гарантированно идти после NSG (10~300).</summary>
        public const int PMain = 2000;
        public const int PBake = 2010;
        public const int PRebuild = 2020;
        public const int PContactMap = 2030;
        public const int PGizmo = 2040;
        public const int PExpert = 2050;
        public const int PForge = 2060;
        public const int PMirror = 2070;
        public const int PIcon = 2080;
        public const int PHealth = 2090;
        public const int PAbout = 2100;

        /// <summary>Путь пункта с галочкой: нужен и в атрибуте, и в validate-функции.
        /// Раньше в подписи стояла косая черта («Show/Hide Hull Gizmo»), и Unity
        /// молча делала из пункта подменю «Show» — то есть лишний уровень там,
        /// где его не задумывали.</summary>
        const string GizmoPath = GGizmo + "Toggle Hull Gizmo";

        // ------------------------------------------------------------------ Меню

        [MenuItem(GWindow + "Open Main Window %#d", false, PMain)]
        public static void OpenWindow()
        {
            Dyc_Window.Open();
        }

        [MenuItem(GBake + "Bake Selected %#b", false, PBake)]
        public static void BakeSelected()
        {
            var sel = Selection.gameObjects;
            int n = 0;
            for (int i = 0; i < sel.Length; i++)
            {
                var c = sel[i].GetComponent<Dyc_DynamicCollision>();
                if (c != null && Bake(c)) n++;
            }
            if (n == 0) Debug.LogWarning("[NDC] В выделении нет компонента Dynamic Collision.");
        }

        [MenuItem(GizmoPath, false, PGizmo)]
        public static void ToggleGizmo()
        {
            Dyc_GizmoDraw.Enabled = !Dyc_GizmoDraw.Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(GizmoPath, true)]
        public static bool ToggleGizmoValidate()
        {
            Menu.SetChecked(GizmoPath, Dyc_GizmoDraw.Enabled);
            return true;
        }

        /// <summary>
        /// Подсветка непокрытых граней. Отдельным пунктом меню, а не только
        /// галочкой в окне: раньше включить её можно было кнопкой на вкладке
        /// покрытия, а выключить — только найдя галочку на другой вкладке, и
        /// это читалось как «включается, но не выключается».
        /// </summary>
        const string UncoveredPath = GGizmo + "Highlight Uncovered Faces";

        [MenuItem(UncoveredPath, false, PGizmo + 1)]
        public static void ToggleUncovered()
        {
            Dyc_GizmoDraw.DrawUncovered = !Dyc_GizmoDraw.DrawUncovered;
            Dyc_GizmoDraw.InvalidateCache();
            SceneView.RepaintAll();
        }

        [MenuItem(UncoveredPath, true)]
        public static bool ToggleUncoveredValidate()
        {
            Menu.SetChecked(UncoveredPath, Dyc_GizmoDraw.DrawUncovered);
            return true;
        }

        [MenuItem(GBake + "Rebuild Selected", false, PRebuild)]
        public static void RebuildSelected()
        {
            var sel = Selection.gameObjects;
            for (int i = 0; i < sel.Length; i++)
            {
                var c = sel[i].GetComponent<Dyc_DynamicCollision>();
                if (c != null) RebuildRuntime(c);
            }
        }

        [MenuItem(GDiag + "Collision Health", false, PHealth)]
        public static void OpenHealth()
        {
            Dyc_Window.OpenAtTab(5);
        }

        /// <summary>
        /// Переключатель зеркалирования интерфейса.
        ///
        /// Пункт виден ТОЛЬКО когда выбран язык справа налево: у английского и
        /// китайского зеркалить нечего, и лишний пункт в меню — шум. Меняется
        /// лишь окно плагина, интерфейс Unity не затрагивается.
        /// </summary>
        [MenuItem(GTools + "Bone Precision (Expert)", false, PExpert)]
        public static void OpenExpert()
        {
            var target = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<Dyc_DynamicCollision>()
                : null;

            if (target == null)
            {
                Debug.LogWarning("[NDC] Выберите объект с компонентом Dynamic Collision.");
                return;
            }

            Dyc_ExpertWindow.Open(target);
        }

        [MenuItem(GTools + "Bone Precision (Expert)", true)]
        static bool OpenExpertValidate()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponentInParent<Dyc_DynamicCollision>() != null;
        }

        [MenuItem(GTools + "Mirror RTL Interface", false, PMirror)]
        static void ToggleMirror()
        {
            Dyc_L10n.MirrorRtl = !Dyc_L10n.MirrorRtl;
            Dyc_Window.RefreshIfOpen();
        }

        [MenuItem(GTools + "Mirror RTL Interface", true)]
        static bool ToggleMirrorValidate()
        {
            Menu.SetChecked(GTools + "Mirror RTL Interface", Dyc_L10n.MirrorRtl);
            return Dyc_L10n.IsRtl;
        }

        [MenuItem(GTools + "Material Forge", false, PForge)]
        public static void OpenForge()
        {
            Dyc_Window.OpenAtTab(4);
        }

        [MenuItem(GHelp + "About Neko Dynamic Collision", false, PAbout)]
        public static void About()
        {
            var set = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Dyc_DynamicCollision>()
                : null;
            Debug.Log("[NDC] Neko Dynamic Collision · серия NekoWorks\n" +
                      "  путь в меню: NekoWorks → NekoDynamicCollision\n" +
                      $"  пресетов материалов: {Dyc_MaterialPresets.All.Length}\n" +
                      $"  выделено: {(set != null ? set.gameObject.name : "(ничего)")}");
        }

        [MenuItem("GameObject/Neko/Dynamic Collision", false, 10)]
        public static void AddComponent(MenuCommand cmd)
        {
            var go = cmd.context as GameObject;
            if (go == null) return;
            if (go.GetComponent<Dyc_DynamicCollision>() != null)
            {
                Debug.LogWarning("[NDC] На этом объекте уже есть Dynamic Collision. " +
                                 "Разделы делаются через element + группы материалов, второй компонент не нужен.", go);
                return;
            }
            Undo.AddComponent<Dyc_DynamicCollision>(go);
        }

        // ------------------------------------------------------------------ Сервис

        /// <summary>Открывает папку с запечёнными ассетами выделенного объекта.</summary>
        public static void OpenBakedFolder(Dyc_DynamicCollision target)
        {
            string folder = target != null ? Dyc_AssetIO.RootFor(target) : Dyc_AssetIO.DefaultRoot;
            Dyc_AssetIO.EnsureFolder(folder);
            EditorUtility.FocusProjectWindow();
            var obj = AssetDatabase.LoadAssetAtPath<Object>(folder);
            if (obj != null) Selection.activeObject = obj;
            else Debug.Log($"[NDC] Папка: {folder}");
        }

        /// <summary>Сбрасывает EditorPrefs плагина: язык, состояние гизмо, вкладку окна.</summary>
        public static void ResetPreferences()
        {
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Language");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.enabled");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.onlySelected");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.bindPose");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.source");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.uncovered");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.labels");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Gizmo.maxHulls");
            EditorPrefs.DeleteKey("Neko.DynamicCollision.Window.Tab");
            Debug.Log("[NDC] Настройки плагина сброшены. Перезапустите окно, чтобы язык применился.");
            Dyc_MenuRuntime.Rebuild();
        }

        // ------------------------------------------------------------------ Запекание

        /// <summary>
        /// Запускает запекание. Возвращает true, если оно НАЧАЛОСЬ: при своём
        /// ядре работа идёт в фоне, и результат появится через несколько
        /// итераций редактора (см. Dyc_BakeJob).
        /// </summary>
        public static bool Bake(Dyc_DynamicCollision target)
        {
            if (target == null) return false;

            // Нативное ядро синхронное и потоконебезопасное — фоновый путь для
            // него невозможен, поэтому оно идёт старым путём с системным баром.
            if (target.DecomposeSettings.kernel == DycDecomposeKernel.Native)
                return BakeSynchronous(target);

            return Dyc_BakeJob.Start(target);
        }

        /// <summary>Синхронный путь. Остаётся для нативного ядра.</summary>
        static bool BakeSynchronous(Dyc_DynamicCollision target)
        {
            Undo.RecordObject(target, "Dynamic Collision Bake");

            Dyc_Baker.EnsureGroups(target);
            Dyc_Baker.EnsureElements(target);

            string folder = Dyc_AssetIO.RootFor(target);
            string baseName = target.gameObject.name;

            // Ассет меток кисти (число треугольников и отпечаток должны совпадать, иначе при запекании метки игнорируются)
            var sources = Dyc_Baker.CollectSources(target);
            int triCount = 0;
            var hashes = new List<string>();
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].mesh == null) continue;
                triCount += sources[i].mesh.triangles.Length / 3;
                hashes.Add(Dyc_PaintMask.Hash(sources[i].mesh));
            }
            string hash = string.Join("|", hashes);

            var mask = Dyc_AssetIO.SavePaintMask(target, target.PaintMask, folder, baseName, triCount, hash);
            if (target.PaintMask == null) target.EditorSetPaintMask(mask);

            var set = Dyc_AssetIO.EnsureBakedAsset(target, folder, baseName);

            // ПРОГРЕСС И ОТМЕНА.
            //
            // Разложение — самая долгая часть запекания, и раньше она была
            // чёрным ящиком: редактор не отвечал, а отличить «считает» от
            // «повисло» было нельзя. Теперь есть прогресс-бар, кнопка отмены и
            // бюджет времени из настроек. Бар снимается в finally, иначе он
            // остался бы висеть поверх редактора при исключении.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double budgetMs = target.DecomposeSettings.EffectiveTimeBudgetSeconds * 1000.0;
            var progress = new Dyc_BakeProgressProxy(budgetMs, watch);

            DycBakeReport report;
            try
            {
                report = Dyc_Baker.Bake(target, set, true, folder, progress);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (progress.cancelled && !progress.timedOut)
            {
                Debug.LogWarning("[NDC] Запекание отменено. Прежний набор не изменён.", target);
                return false;
            }

            if (progress.timedOut && report.warnings != null)
                report.warnings.Add(
                    "Разложение остановлено по бюджету времени: набор собран из готовых частей. " +
                    "Уменьшите детализацию (крупнее воксель) или поднимите бюджет времени.");

            if (!report.ok)
            {
                Debug.LogError($"[NDC] Запекание не удалось: {report.error}", target);
                return false;
            }

            set.boneStats = report.boneStats ?? new List<DycBoneStat>();
            set = Dyc_AssetIO.SaveBaked(target, set, folder, baseName);
            target.EditorApplyBaked(set, Application.isPlaying);

            EditorUtility.SetDirty(target);
            if (target.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.gameObject.scene);

            // Отчёт печатает не только итог, но и РАСПРЕДЕЛЕНИЕ работы: сколько
            // треугольников разошлось по зонам и сколько не попало ни в одну.
            //
            // Без этого «оболочек почти нет» невозможно объяснить: итог говорит
            // «2 оболочки», а причина может быть в пустом списке зон, в слишком
            // высоком пороге веса или в том, что меш не тот. Числа ниже
            // разделяют эти случаи сразу.
            string msg = $"[NDC] Запекание готово: {report.hulls} выпуклых тел / {report.hullVertices} вершин / " +
                         $"пик {report.maxHullVertices} / объём {report.totalVolume:F4} м³ / {report.elapsedMs:F0} мс\n" +
                         $"  зон: {target.ElementCount} · треугольников источника: {report.sourceTriangles} · " +
                         $"распределено: {report.usedTriangles} · БЕЗ ЗОНЫ: {report.unassignedTriangles} · " +
                         $"вырожденных кластеров: {report.degenerateClusters}" +
                         $" · раздвинуто вершин: {report.separatedVertices}";
            if (report.warnings != null && report.warnings.Count > 0)
                for (int i = 0; i < report.warnings.Count; i++) msg += "\n  · " + report.warnings[i];

            Debug.Log(msg, target);
            return true;
        }

        public static void RebakeWithLowerPrecision(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            var p = target.Precision;
            if (p > DycPrecision.Coarse) target.EditPrecision = p - 1;
            Bake(target);
        }

        public static void ClearBaked(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Dynamic Collision Clear");
            Dyc_AssetIO.DeleteBaked(target.BakedSet);
            target.EditorApplyBaked(null, Application.isPlaying);
            EditorUtility.SetDirty(target);
        }

        public static void RebuildRuntime(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            target.ClearRuntime();
            target.Build();
        }

        // ------------------------------------------------------------------ Разделы

        static readonly (string name, HumanBodyBones[] bones, string evt, float dmg)[] HumanoidLayout =
        {
            ("Head",        new[] { HumanBodyBones.Head }, "Hit.Head", 4f),
            ("Torso",       new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck }, "Hit.Torso", 1f),
            ("ShoulderL",   new[] { HumanBodyBones.LeftShoulder }, "Hit.ShoulderL", 0.8f),
            ("ShoulderR",   new[] { HumanBodyBones.RightShoulder }, "Hit.ShoulderR", 0.8f),
            ("ArmL_Upper",  new[] { HumanBodyBones.LeftUpperArm }, "Hit.ArmL", 0.7f),
            ("ArmL_Lower",  new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, "Hit.ArmL", 0.6f),
            ("ArmR_Upper",  new[] { HumanBodyBones.RightUpperArm }, "Hit.ArmR", 0.7f),
            ("ArmR_Lower",  new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }, "Hit.ArmR", 0.6f),
            ("LegL_Upper",  new[] { HumanBodyBones.LeftUpperLeg }, "Hit.LegL", 0.8f),
            ("LegL_Lower",  new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes }, "Hit.LegL", 0.6f),
            ("LegR_Upper",  new[] { HumanBodyBones.RightUpperLeg }, "Hit.LegR", 0.8f),
            ("LegR_Lower",  new[] { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes }, "Hit.LegR", 0.6f),
        };

        public static void AutoFillElements(Dyc_DynamicCollision target)
        {
            if (target == null) return;

            var anim = target.GetComponent<Animator>();
            if (anim == null) anim = target.GetComponentInParent<Animator>();
            if (anim == null) anim = target.GetComponentInChildren<Animator>();

            if (anim == null || !anim.isHuman)
            {
                Dyc_Baker.EnsureElements(target);
                Debug.Log("[NDC] Это не Humanoid: создан один раздел «All» (с дочерними костями).", target);
                return;
            }

            Undo.RecordObject(target, "Dynamic Collision Auto Fill Elements");
            var list = target.Elements;
            list.Clear();

            int filled = 0;
            for (int i = 0; i < HumanoidLayout.Length; i++)
            {
                var layout = HumanoidLayout[i];
                Transform first = null;
                for (int b = 0; b < layout.bones.Length; b++)
                {
                    var t = anim.GetBoneTransform(layout.bones[b]);
                    if (t == null) continue;
                    if (target.IsBoneExcluded(t)) continue;
                    if (first == null) first = t;
                    // Разделы из нескольких костей (например предплечье + кисть) через includeChildren не покрыть,
                    // поэтому для всех костей, кроме первой, создаём отдельный раздел с тем же именем.
                    if (t != first)
                    {
                        list.Add(new DycElement
                        {
                            name = layout.name,
                            bone = t,
                            includeChildren = true,
                            eventName = layout.evt,
                            damageMultiplier = layout.dmg
                        });
                    }
                }
                if (first == null) continue;

                list.Add(new DycElement
                {
                    name = layout.name,
                    bone = first,
                    includeChildren = true,
                    eventName = layout.evt,
                    damageMultiplier = layout.dmg
                });
                filled++;
            }

            if (list.Count == 0) Dyc_Baker.EnsureElements(target);

            EditorUtility.SetDirty(target);
            Debug.Log($"[NDC] По Humanoid заполнено частей: {filled}, разделов: {list.Count}.", target);
        }

        /// <summary>
        /// Создаёт зоны ИЗ СКЕЛЕТА — по одной на каждую кость, которая реально
        /// влияет на вершины.
        ///
        /// Зачем отдельно от AutoFillElements. Тот работает по жёсткой таблице
        /// HumanoidLayout и требует Humanoid-рига: у не-человеческих скелетов
        /// (собака, лошадь, мех, шестиногие) он создаёт один раздел «All», и
        /// дальше запекание делит ВЕСЬ меш на оболочки по размеру — то есть
        /// лепестки вместо конечностей.
        ///
        /// Здесь риг не важен вообще: берётся список костей из
        /// SkinnedMeshRenderer и влияние каждой считается по boneWeights. Кость
        /// без влияния пропускается — иначе на скелете со служебными пустышками
        /// (цели IK, кончики, хелперы) появились бы зоны, в которые не попадает
        /// ни один треугольник.
        ///
        /// includeChildren выключен намеренно: у каждой кости своя зона, и
        /// включённое наследование заставило бы родителя проглотить детей
        /// (см. ResolveElement — побеждает ближайший предок с includeChildren).
        /// </summary>
        public static void FillElementsFromSkeleton(Dyc_DynamicCollision target)
        {
            if (target == null) return;

            var skin = target.SourceSkin;
            if (skin == null) skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null)
            {
                Debug.LogWarning("[NDC] Нет SkinnedMeshRenderer — заполнять нечего.", target);
                return;
            }

            var mesh = skin.sharedMesh;
            if (mesh == null)
            {
                Debug.LogWarning("[NDC] У SkinnedMeshRenderer нет меша.", target);
                return;
            }

            var bones = skin.bones;
            if (bones == null || bones.Length == 0)
            {
                Debug.LogWarning("[NDC] В SkinnedMeshRenderer нет костей.", target);
                return;
            }

            // Влияние на каждую кость: сумма весов по всем вершинам. Так
            // служебные кости отсеиваются сами, без списков и догадок.
            var influence = new float[bones.Length];
            var weights = mesh.boneWeights;
            if (weights != null && weights.Length > 0)
            {
                for (int v = 0; v < weights.Length; v++)
                {
                    var bw = weights[v];
                    Accumulate(bones, influence, bw.boneIndex0, bw.weight0);
                    Accumulate(bones, influence, bw.boneIndex1, bw.weight1);
                    Accumulate(bones, influence, bw.boneIndex2, bw.weight2);
                    Accumulate(bones, influence, bw.boneIndex3, bw.weight3);
                }
            }

            Undo.RecordObject(target, "Dynamic Collision Fill From Skeleton");
            var list = target.Elements;
            list.Clear();

            int created = 0, skipped = 0;
            for (int b = 0; b < bones.Length; b++)
            {
                var bone = bones[b];
                if (bone == null) continue;

                if (influence[b] <= 0f)
                {
                    skipped++;
                    continue;
                }

                // Исключённое поддерево не получает зон: иначе исключение
                // работало бы только на запекании, а в списке зон оставались бы
                // пустые пункты, которые человек только что убрал.
                if (target.IsBoneExcluded(bone))
                {
                    skipped++;
                    continue;
                }

                list.Add(new DycElement
                {
                    name = bone.name,
                    bone = bone,
                    includeChildren = false,
                    damageMultiplier = 1f
                });
                created++;
            }

            EditorUtility.SetDirty(target);

            Debug.Log(
                $"[NDC] Зон из скелета: {created}. Костей всего {bones.Length}, " +
                $"без влияния на вершины пропущено: {skipped}." +
                (created == 0 ? "\n  Ни одна кость не влияет на вершины — вероятно, у меша нет boneWeights." : "") +
                "\n  Дальше: точность Auto и запекание.", target);
        }

        static void Accumulate(Transform[] bones, float[] influence, int index, float weight)
        {
            if (weight <= 0f) return;
            if (index < 0 || index >= bones.Length) return;
            influence[index] += weight;
        }

        public static void SetDefaultHitboxLayers(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Dynamic Collision Layers");

            int bullet = LayerMask.NameToLayer("Bullet");
            if (bullet < 0) bullet = 10;
            target.EditIncludeLayers = 1 << bullet;
            target.EditRole = DycColliderRole.Hitbox;

            EditorUtility.SetDirty(target);
            Debug.Log($"[NDC] Слои взаимодействия: Bullet({bullet}); персонаж теперь только принимает попадания.", target);
        }

        public static void SetSelfCollision(Dyc_DynamicCollision target, DycSelfCollision mode)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Dynamic Collision Self Collision");
            target.EditSelfCollisionMode = mode;
            EditorUtility.SetDirty(target);
        }

        public static void EnableAutoRigidbody(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Dynamic Collision Auto Rigidbody");
            target.EditAutoRigidbody = true;
            EditorUtility.SetDirty(target);
        }

        public static void EnableAutoMass(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Dynamic Collision Auto Mass");
            target.EditAutoMass = true;
            EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// Делает все твёрдые тела владельца кинематическими.
        ///
        /// Единственное исправление, которое делает невыпуклую сетку законной:
        /// PhysX не принимает невыпуклый MeshCollider на подвижном теле. Массу и
        /// гравитацию при этом не трогаем — их и так ведёт анимация.
        /// </summary>
        public static void MakeKinematic(Dyc_DynamicCollision target)
        {
            if (target == null) return;

            var rbs = target.GetComponentsInParent<Rigidbody>();
            if (rbs == null || rbs.Length == 0)
            {
                Debug.LogWarning("[NDC] Родительского Rigidbody нет — кинематизировать нечего.", target);
                return;
            }

            int changed = 0;
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] == null || rbs[i].isKinematic) continue;
                Undo.RecordObject(rbs[i], "Dynamic Collision Kinematic");
                rbs[i].isKinematic = true;
                rbs[i].useGravity = false;
                EditorUtility.SetDirty(rbs[i]);
                changed++;
            }

            Debug.Log($"[NDC] Твёрдых тел переведено в кинематические: {changed}. " +
                      "Невыпуклая сетка работает только на кинематическом/анимационном теле.", target);
        }

        // ------------------------------------------------------------------ Группы материалов

        public static void ApplyPreset(Dyc_DynamicCollision target, int groupIndex, DycMaterialPreset preset)
        {
            ApplyPreset(target, groupIndex, preset, true);
        }

        public static void ApplyPreset(Dyc_DynamicCollision target, int groupIndex, DycMaterialPreset preset, bool log)
        {
            if (target == null) return;
            var groups = target.Groups;
            if (groupIndex < 0 || groupIndex >= groups.Count) return;

            Undo.RecordObject(target, "Dynamic Collision Material Preset");

            string folder = Dyc_AssetIO.RootFor(target) + "/Materials";
            var mat = Dyc_AssetIO.SaveMaterial(folder, "DYC_" + preset.id,
                preset.staticFriction, preset.dynamicFriction, preset.bounciness,
                Dyc_MaterialPresets.FrictionMode, Dyc_MaterialPresets.BounceMode);

            groups[groupIndex].material = mat;
            groups[groupIndex].density = preset.density;
            if (string.IsNullOrEmpty(groups[groupIndex].name) || groups[groupIndex].name == "Default")
                groups[groupIndex].name = preset.display;

            EditorUtility.SetDirty(target);

            if (!log) return;

            Debug.Log($"[NDC] Группа «{groups[groupIndex].DisplayName}» → {preset.display} " +
                      $"(трение покоя {preset.staticFriction} / скольжения {preset.dynamicFriction} / " +
                      $"упругость {preset.bounciness} / плотность {preset.density})" +
                      (string.IsNullOrEmpty(preset.notes) ? "" : "\n  " + preset.notes), target);
        }

        /// <summary>
        /// Накладывает условие поверхности на базовый материал и назначает
        /// результат группе. Это вход в генератор из меню: «сталь, но мокрая»
        /// одной строкой, без отдельного пресета в таблице.
        /// </summary>
        public static void ApplyForge(Dyc_DynamicCollision target, int groupIndex,
            DycMaterialPreset basePreset, Dyc_SurfaceCondition condition)
        {
            if (target == null) return;

            var forged = Dyc_MaterialForge.Forge(basePreset, condition);
            ApplyPreset(target, groupIndex, forged, false);

            var groups = target.Groups;
            string groupName = groupIndex >= 0 && groupIndex < groups.Count ? groups[groupIndex].DisplayName : "?";

            Debug.Log("[NDC] " + Dyc_L10n.T("forge.applied", groupName, basePreset.display,
                Dyc_MaterialForge.ConditionLabel(condition)) +
                $"\n  {forged.staticFriction} / {forged.dynamicFriction} / {forged.bounciness} / {forged.density} kg/m³" +
                (string.IsNullOrEmpty(forged.notes) ? "" : "\n  " + forged.notes), target);
        }

        /// <summary>
        /// Пишет по ассету на каждое условие поверхности.
        ///
        /// Ассеты настоящие: их видно в Project, можно положить в Addressables,
        /// сравнить по diff и отдать художнику — плагин для этого не нужен.
        /// </summary>
        public static int GenerateVariants(Dyc_DynamicCollision target, DycMaterialPreset basePreset)
        {
            if (target == null) return 0;

            string folder = Dyc_AssetIO.RootFor(target) + "/Materials";
            var variants = Dyc_MaterialForge.Variants(basePreset);

            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                Dyc_AssetIO.SaveMaterial(folder, "DYC_" + v.id,
                    v.staticFriction, v.dynamicFriction, v.bounciness,
                    Dyc_MaterialPresets.FrictionMode, Dyc_MaterialPresets.BounceMode);
            }

            Debug.Log("[NDC] " + Dyc_L10n.T("forge.generated", variants.Length, folder), target);
            return variants.Length;
        }

        public static void OpenMaterialPicker(Dyc_DynamicCollision target, int groupIndex)
        {
            if (target == null) return;

            var menu = new GenericMenu();
            var cats = Dyc_MaterialPresets.CategoryKeys();

            for (int c = 0; c < cats.Count; c++)
            {
                string catKey = cats[c];
                // Число в подписи категории: сразу видно, что таблица не из
                // десяти строк, и не надо открывать подменю, чтобы понять это.
                string catLabel = Dyc_MaterialPresets.CategoryLabel(catKey)
                                  + " (" + Dyc_MaterialPresets.CountIn(catKey) + ")";

                for (int i = 0; i < Dyc_MaterialPresets.All.Length; i++)
                {
                    var p = Dyc_MaterialPresets.All[i];
                    if (p.categoryKey != catKey) continue;

                    string path = catLabel + "/" + p.display;
                    var captured = p;
                    menu.AddItem(new GUIContent(path), false, () => ApplyPreset(target, groupIndex, captured));
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Dyc_L10n.T("mat.autoAll")), false, () => AutoAssignAllGroups(target));
            menu.AddItem(new GUIContent(Dyc_L10n.T("forge.title") + "…"), false, () => Dyc_Window.OpenAtTab(4));
            menu.ShowAsContext();
        }

        /// <summary>
        /// Имя группы → id пресета. Имя группы пользователь пишет на своём
        /// языке, поэтому корни английские, русские и китайские, а порядок
        /// строк — это порядок приоритета: первое совпадение побеждает.
        ///
        /// Части тела стоят ВЫШЕ тканей и материалов намеренно: «грудь» должна
        /// стать грудью, а не «мясом», хотя формально это и то и другое.
        /// </summary>
        static readonly string[][] AutoAssignRules =
        {
            new[] { "tissue_brain",  "brain", "мозг", "脑" },
            new[] { "tissue_lung",   "lung", "легк", "肺" },
            new[] { "tissue_organ",  "liver", "heart", "organ", "печен", "сердц", "орган", "内脏", "肝", "心" },

            new[] { "body_chest",    "chest", "breast", "torso", "груд", "胸部", "胸", "躯干" },
            new[] { "body_glute",    "glute", "buttock", "butt", "ягод", "臀" },
            new[] { "body_head",     "head", "skull", "череп", "голов", "头颅", "头" },
            new[] { "body_face",     "face", "cheek", "лицо", "щек", "脸" },
            new[] { "body_neck",     "neck", "шея", "шеи", "脖" },
            new[] { "body_abdomen",  "abdomen", "belly", "stomach", "живот", "腹" },
            new[] { "body_thigh",    "thigh", "бедр", "大腿" },
            new[] { "body_calf",     "calf", "shin", "голен", "小腿" },
            new[] { "body_bicep",    "bicep", "shoulder", "arm", "плеч", "臂" },
            new[] { "body_hand",     "hand", "palm", "fist", "кист", "ладон", "手" },
            new[] { "body_foot",     "foot", "sole", "ступн", "脚", "足" },

            new[] { "tissue_bone",   "bone", "кост", "骨" },
            new[] { "tissue_muscle", "muscle", "flesh", "мышц", "плоть", "肌肉", "肌" },
            new[] { "tissue_fat",    "fat", "adipose", "жир", "脂肪", "脂" },

            new[] { "leather",       "leather", "кожев", "皮革" },
            new[] { "tissue_skin",   "skin", "кож", "皮肤", "皮" },
            new[] { "body_armor",    "armor", "armour", "брон", "护甲" },
            new[] { "kevlar",        "kevlar", "aramid", "кевлар", "凯夫拉" },

            new[] { "rubber",        "rubber", "резин", "橡胶", "软" },
            new[] { "glass",         "glass", "стекл", "玻璃" },
            new[] { "oak",           "wood", "oak", "pine", "дерев", "木" },
            new[] { "abs",           "plastic", "abs", "пластик", "塑料", "塑" },
            new[] { "ceramic_tile",  "ceramic", "porcelain", "tile", "керамик", "плитк", "陶瓷", "瓷" },
            new[] { "ice",           "ice", "snow", "лёд", "лед", "снег", "冰", "雪" },
            new[] { "sand",          "sand", "песок", "沙" },
            new[] { "bread",         "food", "bread", "食物" },
            new[] { "concrete",      "concrete", "бетон", "混凝", "石" },
            new[] { "steel",         "metal", "steel", "стал", "металл", "钢", "金属" },
        };

        public static void AutoAssignAllGroups(Dyc_DynamicCollision target)
        {
            if (target == null) return;
            var groups = target.Groups;

            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g] == null) continue;
                string n = groups[g].DisplayName.ToLowerInvariant();

                string id = null;
                for (int r = 0; r < AutoAssignRules.Length && id == null; r++)
                {
                    var rule = AutoAssignRules[r];
                    for (int k = 1; k < rule.Length; k++)
                    {
                        if (!n.Contains(rule[k])) continue;
                        id = rule[0];
                        break;
                    }
                }

                if (id == null) id = "steel";
                if (Dyc_MaterialPresets.TryGet(id, out var preset))
                    ApplyPreset(target, g, preset);
            }
        }

        public static void MergeSmallestGroup(Dyc_DynamicCollision target, int groupIndex)
        {
            if (target == null) return;
            var mask = target.PaintMask;
            if (mask == null || !mask.IsValid)
            {
                Debug.LogWarning("[NDC] Нет меток кисти, объединять нечего.", target);
                return;
            }

            var counts = mask.CountByLabel(target.Groups.Count);
            int target2 = -1;
            for (int g = 0; g < counts.Length; g++)
            {
                if (g == groupIndex) continue;
                if (target2 < 0 || counts[g] > counts[target2]) target2 = g;
            }
            if (target2 < 0) return;

            int moved = mask.Remap((byte)groupIndex, (byte)target2);
            EditorUtility.SetDirty(mask);
            Debug.Log($"[NDC] {moved} треугольников группы {groupIndex} перенесены в группу {target2}; " +
                      "изменение вступит в силу после повторного запекания.", target);
        }
    }

    /// <summary>
    /// Мост между чистым запекателем и прогресс-баром редактора.
    ///
    /// Сам Dyc_Baker не знает про UnityEditor — он должен оставаться
    /// запускаемым из консольного теста. Поэтому редакторская часть вынесена
    /// сюда: она подписывается на отчёт и превращает его в
    /// DisplayCancelableProgressBar, а возвращённое «пользователь нажал
    /// отмену» отдаёт обратно в запекание.
    ///
    /// Бюджет времени проверяется здесь же и тоже выглядит для запекания как
    /// отмена: разложение вернёт уже готовые части, а не продолжит считать.
    /// </summary>
    class Dyc_BakeProgressProxy : DycBakeProgress
    {
        public Dyc_BakeProgressProxy(double budgetMs, System.Diagnostics.Stopwatch watch)
        {
            this.budgetMs = budgetMs;
            this.watch = watch;
            report = OnReport;
        }

        bool OnReport(float t, string stage)
        {
            string text = stage ?? Dyc_L10n.T("bake.progress.baking");

            // Бюджет времени — это тоже отмена, но не пользовательская:
            // разделяем, чтобы отчёт не путал «отменил человек» с «не успело».
            if (budgetMs > 0 && watch != null && watch.Elapsed.TotalMilliseconds > budgetMs)
            {
                string over = Dyc_L10n.T("bake.progress.budget");
                if (Dyc_BakeProgressWindow.IsOpen) Dyc_BakeProgressWindow.Update(over, 1f);
                else EditorUtility.DisplayProgressBar("NDC", over, 1f);
                return false;   // отмену выставит Stopped
            }

            // Своё окно, если оно открыто (фоновый путь), иначе системный бар:
            // системный бар — единственный способ ОТРИСОВАТЬ прогресс, когда
            // главный поток занят сплошным синхронным счётом.
            if (Dyc_BakeProgressWindow.IsOpen)
            {
                Dyc_BakeProgressWindow.Update(text, 0.1f + t * 0.85f);
                return Dyc_BakeProgressWindow.CancelRequested;
            }

            return EditorUtility.DisplayCancelableProgressBar("NDC", text, t);
        }
    }
}
