using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Главное окно. «Продвинутый» вход в полу-чёрный ящик:
    /// в повседневной разработке достаточно повесить компонент и нажать «Запечь»; сюда приходят ради точности, покраски материалов зон и просмотра покрытия.
    /// </summary>
    public class Dyc_Window : EditorWindow
    {
        const string TabKey = "Neko.DynamicCollision.Window.Tab";

        Dyc_DynamicCollision _target;
        int _tab;
        int _paintGroup;

        VisualElement _root;
        VisualElement _body;
        Label _targetLabel;

        DycHealthReport _health;
        bool _healthDirty = true;
        DycCoverageReport _coverage;
        bool _coverageDirty = true;

        public static void Open()
        {
            OpenAtTab(EditorPrefs.GetInt(TabKey, 0));
        }

        public static void OpenAtTab(int tab)
        {
            var w = GetWindow<Dyc_Window>();
            w.titleContent = new GUIContent(Dyc_L10n.T("win.title"));
            w.minSize = new Vector2(560, 520);
            w._tab = Mathf.Clamp(tab, 0, 6);
            EditorPrefs.SetInt(TabKey, w._tab);
            w.Show();
            if (w._body != null) w.Rebuild();
        }

        /// <summary>
        /// Пересобрать окно, если оно открыто. Нужно переключателям, которые
        /// меняют вид (например зеркалирование интерфейса): они обязаны
        /// обновить уже открытое окно, но НЕ должны открывать его принудительно.
        /// </summary>
        public static void RefreshIfOpen()
        {
            var windows = Resources.FindObjectsOfTypeAll<Dyc_Window>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] == null || windows[i]._root == null) continue;
                windows[i].Rebuild();
            }
        }

        public void CreateGUI()
        {
            _tab = EditorPrefs.GetInt(TabKey, 0);
            BuildUI();
        }

        void OnSelectionChange()
        {
            var next = ResolveTarget();
            if (next != _target)
            {
                _target = next;
                _healthDirty = true;
                _coverageDirty = true;
                _health = default;
                _coverage = default;
                if (_target != null && _target.Groups.Count > 0)
                    _paintGroup = Mathf.Clamp(_paintGroup, 0, _target.Groups.Count - 1);
            }
            if (_root != null) Rebuild();
        }

        void OnFocus()
        {
            if (_root != null) Rebuild();
        }

        static Dyc_DynamicCollision ResolveTarget()
        {
            var active = Selection.activeGameObject;
            if (active != null)
            {
                var c = active.GetComponent<Dyc_DynamicCollision>();
                if (c != null) return c;
                c = active.GetComponentInParent<Dyc_DynamicCollision>();
                if (c != null) return c;
                c = active.GetComponentInChildren<Dyc_DynamicCollision>();
                if (c != null) return c;
            }

            // Раньше здесь было «если в сцене ровно один компонент — взять его».
            // Из-за этого при снятом выделении окно продолжало показывать
            // содержимое, и понять, что объект не выбран, было нельзя. Теперь
            // нет выделения — нет цели, окно честно говорит об этом.
            return null;
        }

        // ------------------------------------------------------------------ каркас

        void BuildUI()
        {
            rootVisualElement.Clear();
            _root = Dyc_Style.Root();
            rootVisualElement.Add(_root);

            var head = Dyc_Style.Row();
            head.style.marginBottom = Dyc_Style.Pad;
            head.style.justifyContent = Justify.SpaceBetween;

            var left = Dyc_Style.Row();
            left.Add(Dyc_Style.Pill("Dynamic Collision", Dyc_Style.Accent));
            _targetLabel = Dyc_Style.Body("");
            left.Add(_targetLabel);
            head.Add(left);

            _root.Add(head);

            var langRow = Dyc_Style.Row();
            langRow.style.marginBottom = Dyc_Style.Pad;
            langRow.style.flexGrow = 0;
            langRow.Add(BuildLanguagePicker(true));
            _root.Add(langRow);

            _body = new VisualElement();
            _body.style.flexGrow = 1;
            _root.Add(_body);

            Rebuild();
        }

        /// <summary>Выпадающий список языка.
        ///
        /// Ширина НЕ фиксирована: контрол тянется по родителю. Иначе при узком
        /// окне стрелка списка уезжала за край, и казалось, что списка нет.
        /// Внутренний контейнер DropdownField тоже растягиваем — у него
        /// собственная flex-раскладка, и одного flexGrow на самом поле мало.</summary>
        VisualElement BuildLanguagePicker(bool stretch)
        {
            var choices = LanguageNames();
            int current = Mathf.Clamp(Dyc_L10n.Index, 0, Mathf.Max(0, choices.Count - 1));

            var dd = new DropdownField(Dyc_L10n.T("set.language"), choices, current);
            dd.style.fontSize = 11;
            dd.style.flexShrink = 1f;
            dd.style.minWidth = 90;

            if (stretch)
            {
                dd.style.flexGrow = 1f;
                foreach (var child in dd.Children())
                {
                    if (child is Label) continue;
                    child.style.flexGrow = 1f;
                    child.style.flexShrink = 1f;
                }
            }
            else
            {
                dd.style.width = 240;
            }

            dd.RegisterValueChangedCallback(e =>
            {
                int idx = choices.IndexOf(e.newValue);
                if (idx < 0 || idx == Dyc_L10n.Index) return;
                Dyc_L10n.Index = idx;
                Dyc_MenuRuntime.Rebuild();
                Rebuild();
            });
            return dd;
        }

        static List<string> LanguageNames()
        {
            var list = new List<string>(Dyc_L10n.Count);
            for (int i = 0; i < Dyc_L10n.Count; i++) list.Add(Dyc_L10n.NameAt(i));
            return list;
        }

        void Rebuild()
        {
            if (_body == null) return;
            _body.Clear();

            _target = ResolveTarget();
            _targetLabel.text = _target != null
                ? _target.gameObject.name + "  ·  " + _target.Mode
                : Dyc_L10n.T("win.notarget");

            var tabs = Dyc_Style.Row(true);
            tabs.style.marginBottom = Dyc_Style.Pad;
            string[] keys = { "tab.bake", "tab.paint", "tab.gizmo", "tab.parts", "tab.materials", "tab.health", "tab.settings" };
            for (int i = 0; i < keys.Length; i++)
            {
                int idx = i;
                var b = Dyc_Style.Btn(Dyc_L10n.T(keys[i]), () =>
                {
                    _tab = idx;
                    EditorPrefs.SetInt(TabKey, idx);
                    Rebuild();
                }, primary: _tab == i);
                tabs.Add(b);
            }
            _body.Add(tabs);

            if (_target == null)
            {
                var card = Dyc_Style.Card();
                card.Add(Dyc_Style.Body(Dyc_L10n.T("win.none")));
                _body.Add(card);
                return;
            }

            var scroll = Dyc_Style.Scroll();
            _body.Add(scroll);

            switch (_tab)
            {
                case 0: scroll.Add(BuildBake()); break;
                case 1: scroll.Add(BuildPaint()); break;
                case 2: scroll.Add(BuildGizmo()); break;
                case 3: scroll.Add(BuildParts()); break;
                case 4: scroll.Add(BuildMaterials()); break;
                case 5: scroll.Add(BuildHealth()); break;
                default: scroll.Add(BuildSettings()); break;
            }
        }

        void MarkDirty()
        {
            if (_target == null) return;
            EditorUtility.SetDirty(_target);
            if (_target.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_target.gameObject.scene);
        }

        // ------------------------------------------------------------------ Bake

        VisualElement BuildBake()
        {
            var wrap = new VisualElement();

            var src = Dyc_Style.Card();
            src.Add(Dyc_Style.Header(Dyc_L10n.T("lbl.source")));

            var modeField = new EnumField(Dyc_L10n.T("lbl.mode"), _target.Mode);
            modeField.style.fontSize = 11;
            modeField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(_target, "DYC Mode");
                _target.EditMode = (DycMode)e.newValue;
                MarkDirty();
                Rebuild();
            });
            src.Add(modeField);

            if (_target.Mode == DycMode.Skin)
            {
                var skinField = new ObjectField(Dyc_L10n.T("lbl.source"))
                { objectType = typeof(SkinnedMeshRenderer), value = _target.SourceSkin };
                skinField.style.fontSize = 11;
                skinField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Source");
                    _target.EditSkin = e.newValue as SkinnedMeshRenderer;
                    MarkDirty();
                });
                src.Add(skinField);
            }
            else
            {
                var meshField = new ObjectField(Dyc_L10n.T("lbl.source"))
                { objectType = typeof(MeshFilter), value = _target.SourceMesh };
                meshField.style.fontSize = 11;
                meshField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Source");
                    _target.EditMesh = e.newValue as MeshFilter;
                    MarkDirty();
                });
                src.Add(meshField);

                // Явный переключатель вместо молчаливого автопоиска по детям.
                // Пусто в Source — берётся MeshFilter самого объекта; дети
                // добавляются только если это включено.
                var childrenToggle = new Toggle(Dyc_L10n.T("lbl.childMeshes"))
                { value = _target.SourceIncludesChildren };
                childrenToggle.style.fontSize = 11;
                childrenToggle.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Child Meshes");
                    _target.EditIncludeChildMeshes = e.newValue;
                    MarkDirty();
                });
                src.Add(childrenToggle);

                if (_target.SourceMesh == null)
                    src.Add(Dyc_Style.Caption(Dyc_L10n.T("src.autoHint")));
            }

            // Форма коллайдера — рядом с источником, потому что это решение того
            // же уровня: что именно мы вообще запекаем. Оболочки и невыпуклая
            // поверхность дают разный результат на суставах, и переключатель
            // обязан быть виден ДО запекания, а не спрятан в эксперта.
            var shapeField = new EnumField(Dyc_L10n.T("lbl.shape"), _target.ColliderShape);
            shapeField.style.fontSize = 11;
            shapeField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(_target, "DYC Collider Shape");
                _target.EditColliderShape = (DycColliderShape)e.newValue;
                MarkDirty();
                Rebuild();
            });
            src.Add(shapeField);
            src.Add(Dyc_Style.Caption(Dyc_L10n.T(
                _target.ColliderShape == DycColliderShape.Concave ? "shape.concaveHint" : "shape.convexHint")));

            // ПРЕДУПРЕЖДЕНИЕ ПОКАЗЫВАЕТСЯ СРАЗУ, А НЕ ПОСЛЕ ОШИБКИ.
            //
            // Невыпуклая сетка на подвижном Rigidbody не даёт ошибки в консоли:
            // Unity молча отказывает в коллайдере, и человек ищет причину в
            // запекании. Поэтому плашка стоит прямо под переключателем, и с неё
            // же открывается экспертное окно — там настраиваются пределы.
            if (_target.ColliderShape == DycColliderShape.Concave)
            {
                src.Add(Dyc_Style.Notice(
                    Dyc_L10n.T("shape.concaveWarn"),
                    Dyc_Style.Warn,
                    Dyc_L10n.T("btn.openExpert"),
                    () => Dyc_ExpertWindow.Open(_target)));
            }

            var precField = new EnumField(Dyc_L10n.T("lbl.precision"), _target.Precision);
            precField.style.fontSize = 11;
            precField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(_target, "DYC Precision");
                _target.EditPrecision = (DycPrecision)e.newValue;

                // Ручные числа подтягиваются к новому пресету. Без этого слайдер
                // треугольников перебивал бы пресет всегда: выбрал Ultra — а
                // получил те же 250, что остались от Normal, и пресет выглядел
                // бы нерабочим.
                var preset = DycPrecisionInfo.For(_target.EditPrecision);
                _target.EditCustomTrisPerHull = preset.trisPerHull;
                _target.EditCustomHullsPerPart = Mathf.Max(1, preset.hullsPerPart);

                MarkDirty();
                Rebuild();
            });
            src.Add(precField);

            bool isAuto = _target.Precision == DycPrecision.Auto;
            bool isSkin = _target.Mode == DycMode.Skin;

            // Треугольники на оболочку правятся ВО ВСЕХ режимах, кроме Auto:
            // это бюджет детализации, и решать его должен человек.
            if (!isAuto)
            {
                src.Add(Dyc_Style.IntField(Dyc_L10n.T("lbl.trisPerHull"), _target.CustomTrisPerHull, v =>
                {
                    Undo.RecordObject(_target, "DYC Custom Tris");
                    _target.EditCustomTrisPerHull = v;
                    MarkDirty();
                    Rebuild();
                }));
            }

            // Число оболочек: в Skin его всегда решает AutoHullsFor, поэтому
            // слайдер там не показывается — неактивный контрол хуже отсутствующего.
            if (_target.Precision == DycPrecision.Custom && !isSkin)
            {
                src.Add(Dyc_Style.IntField(Dyc_L10n.T("lbl.hullsPerPart"), _target.CustomHullsPerPart, v =>
                {
                    Undo.RecordObject(_target, "DYC Custom Hulls");
                    _target.EditCustomHullsPerPart = v;
                    MarkDirty();
                    Rebuild();
                }));

                src.Add(Dyc_Style.Slider(Dyc_L10n.T("lbl.weightThreshold"),
                    _target.CustomWeightThreshold, 0f, 1f, v =>
                {
                    Undo.RecordObject(_target, "DYC Custom Weight");
                    _target.EditCustomWeightThreshold = v;
                    MarkDirty();
                    Rebuild();
                }));
            }

            src.Add(Dyc_Style.Caption(Dyc_L10n.T(isSkin ? "prec.skinHint" : "prec.customHint")));

            var info = _target.PrecisionInfo;
            src.Add(Dyc_Style.Caption(
                $"→ {info.trisPerHull} tris/hull · {(isSkin ? "auto" : info.hullsPerPart.ToString())} hull/part · seam {info.seamOverlap * 1000f:F1}mm · weight ≥ {info.boneWeightThreshold:F2}"));

            wrap.Add(src);

            var actions = Dyc_Style.Card();
            var row = Dyc_Style.Row(true);
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.bake"), () =>
            {
                if (Dyc_Menu.Bake(_target)) { _healthDirty = true; _coverageDirty = true; Rebuild(); }
            }, primary: true));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.rebuild"), () => { Dyc_Menu.RebuildRuntime(_target); Rebuild(); }));

            // Окно эксперта — рядом с запеканием: именно после него хочется
            // посмотреть, что получилось на каждой кости.
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.expert"), () => Dyc_ExpertWindow.Open(_target)));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.clear"), () => { Dyc_Menu.ClearBaked(_target); _healthDirty = true; Rebuild(); }));
            actions.Add(row);
            wrap.Add(actions);

            var stats = Dyc_Style.Card();
            stats.Add(Dyc_Style.Header(Dyc_L10n.T("bake.stats")));
            var set = _target.BakedSet;
            if (set == null || set.hulls.Count == 0)
            {
                stats.Add(Dyc_Style.Body(Dyc_L10n.T("bake.none")));
            }
            else
            {
                stats.Add(Dyc_Style.KV(Dyc_L10n.T("bake.hulls"), set.hulls.Count.ToString()));
                stats.Add(Dyc_Style.KV(Dyc_L10n.T("bake.verts"), set.TotalVertexCount.ToString()));

                // Потолок PhysX в 255 вершин относится ТОЛЬКО к выпуклой форме.
                // У невыпуклой сетки его нет, и красная цифра здесь была бы
                // ложной тревогой на здоровом результате.
                bool convex = set.colliderShape == DycColliderShape.Convex;
                stats.Add(Dyc_Style.KV(Dyc_L10n.T("bake.peak"),
                    set.MaxHullVertices.ToString(),
                    convex
                        ? (set.MaxHullVertices > Dyc_Cluster.PhysXMaxHullVertices ? Dyc_Style.Error : Dyc_Style.Ok)
                        : Dyc_Style.Text));
                if (!convex)
                    stats.Add(Dyc_Style.Body(Dyc_L10n.T("bake.concaveNote")));

                stats.Add(Dyc_Style.KV(Dyc_L10n.T("bake.volume"), $"{set.TotalVolume:F4} m³"));
                stats.Add(Dyc_Style.KV(Dyc_L10n.T("lbl.mode"), set.mode.ToString()));
                stats.Add(Dyc_Style.KV(Dyc_L10n.T("lbl.precision"), set.precision.ToString()));
                if (set.unassignedTriangles > 0)
                    stats.Add(Dyc_Style.KV("Unassigned tris", set.unassignedTriangles.ToString(), Dyc_Style.Warn));
            }
            wrap.Add(stats);

            wrap.Add(BuildCoverageCard());
            return wrap;
        }

        VisualElement BuildCoverageCard()
        {
            var card = Dyc_Style.Card();
            card.Add(Dyc_Style.Header(Dyc_L10n.T("cov.title")));

            if (_coverageDirty)
            {
                _coverage = Dyc_Coverage.Analyze(_target, _target.BakedSet);
                _coverageDirty = false;
            }

            if (!_coverage.ok)
            {
                card.Add(Dyc_Style.Body(_coverage.error ?? "-"));
            }
            else
            {
                var color = _coverage.ratio > 0.98f ? Dyc_Style.Ok
                    : _coverage.ratio > 0.92f ? Dyc_Style.Warn
                    : Dyc_Style.Error;
                card.Add(Dyc_Style.Body(Dyc_L10n.T("cov.value", _coverage.ratio * 100f, _coverage.coveredTriangles, _coverage.totalTriangles)));
                var bar = new VisualElement();
                bar.style.height = 6;
                bar.style.marginTop = 4;
                bar.style.marginBottom = 4;
                bar.style.backgroundColor = new Color(0, 0, 0, 0.25f);
                var fill = new VisualElement();
                fill.style.height = 6;
                fill.style.width = Length.Percent(Mathf.Clamp01(_coverage.ratio) * 100f);
                fill.style.backgroundColor = color;
                bar.Add(fill);
                card.Add(bar);

                int miss = _coverage.totalTriangles - _coverage.coveredTriangles;
                if (miss > 0)
                    card.Add(Dyc_Style.Caption(Dyc_L10n.T("cov.uncovered", miss)));

                if (_coverage.elementCoverage != null)
                {
                    for (int i = 0; i < _coverage.elementCoverage.Length; i++)
                    {
                        if (_coverage.elementTotal == null || i >= _coverage.elementTotal.Length) break;
                        if (_coverage.elementTotal[i] == 0) continue;
                        string name = i < _target.Elements.Count && _target.Elements[i] != null
                            ? _target.Elements[i].DisplayName
                            : ("#" + i);
                        card.Add(Dyc_Style.KV(name, $"{_coverage.elementCoverage[i] * 100f:F1}%",
                            _coverage.elementCoverage[i] > 0.95f ? Dyc_Style.Ok : Dyc_Style.Warn));
                    }
                }
            }

            var row = Dyc_Style.Row();
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("cov.analyze"), () =>
            {
                _coverageDirty = true;
                Dyc_GizmoDraw.InvalidateCache();
                Rebuild();
            }));
            // Кнопка ДВУСТОРОННЯЯ. Раньше она только включала подсветку, и
            // выключить её из этого места было нельзя — приходилось искать
            // галочку на другой вкладке. Теперь надпись и действие зависят от
            // текущего состояния, а после нажатия окно пересобирается, чтобы
            // галочка на вкладке Gizmo не разъезжалась с реальностью.
            bool highlighting = Dyc_GizmoDraw.DrawUncovered;
            row.Add(Dyc_Style.Btn(Dyc_L10n.T(highlighting ? "btn.hideUncovered" : "btn.showUncovered"), () =>
            {
                if (highlighting)
                {
                    Dyc_GizmoDraw.DrawUncovered = false;
                }
                else
                {
                    Dyc_GizmoDraw.Enabled = true;
                    Dyc_GizmoDraw.OnlySelected = true;
                    Dyc_GizmoDraw.DrawUncovered = true;
                }

                Dyc_GizmoDraw.InvalidateCache();
                SceneView.RepaintAll();
                Rebuild();
            }));
            card.Add(row);
            return card;
        }

        // ------------------------------------------------------------------ Paint

        VisualElement BuildPaint()
        {
            var wrap = new VisualElement();

            string block = Dyc_PaintTool.BlockReason(_target);
            var status = Dyc_Style.Card();
            status.Add(Dyc_Style.Header(string.IsNullOrEmpty(block) ? Dyc_L10n.T("paint.ready") : Dyc_L10n.T("paint.blocked")));
            if (!string.IsNullOrEmpty(block))
                status.Add(Dyc_Style.Caption(block));
            wrap.Add(status);

            var groupsCard = Dyc_Style.Card();
            groupsCard.Add(Dyc_Style.Header(Dyc_L10n.T("lbl.groups")));

            var mask = _target.PaintMask;
            var counts = mask != null && mask.IsValid ? mask.CountPaintedByLabel(_target.Groups.Count) : null;

            for (int g = 0; g < _target.Groups.Count; g++)
            {
                int idx = g;
                var row = Dyc_Style.Row();
                var dot = new VisualElement();
                dot.style.width = 10;
                dot.style.height = 10;
                dot.style.marginRight = 6;
                dot.style.borderTopLeftRadius = 5;
                dot.style.borderTopRightRadius = 5;
                dot.style.borderBottomLeftRadius = 5;
                dot.style.borderBottomRightRadius = 5;
                dot.style.backgroundColor = Dyc_GizmoDraw.GroupColor(g);
                row.Add(dot);

                var nameLabel = new Label(_target.Groups[g].DisplayName);
                nameLabel.style.fontSize = 11;
                nameLabel.style.color = Dyc_Style.Text;
                nameLabel.style.minWidth = 90;
                row.Add(nameLabel);

                if (counts != null && g < counts.Length)
                    row.Add(Dyc_Style.Pill(Dyc_L10n.T("paint.trisPerGroup", counts[g]), Dyc_Style.Muted));

                row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.paintThis"), () =>
                {
                    _paintGroup = idx;
                    Dyc_PaintTool.Begin(_target, idx);
                    Dyc_GizmoDraw.Enabled = true;
                    Dyc_GizmoDraw.OnlySelected = true;
                    SceneView.RepaintAll();
                    Rebuild();
                }, primary: Dyc_PaintTool.Active && Dyc_PaintTool.Target == _target && Dyc_PaintTool.GroupIndex == g));

                groupsCard.Add(row);
            }

            wrap.Add(groupsCard);

            var brush = Dyc_Style.Card();
            brush.Add(Dyc_Style.Header(Dyc_L10n.T("paint.section")));
            brush.Add(Dyc_Style.Slider(Dyc_L10n.T("paint.radius"), Dyc_PaintTool.Radius, 0.005f, 0.5f,
                v => { Dyc_PaintTool.Radius = v; SceneView.RepaintAll(); }));
            brush.Add(Dyc_Style.Check(Dyc_L10n.T("paint.xray"), Dyc_PaintTool.XRay, v => { Dyc_PaintTool.XRay = v; SceneView.RepaintAll(); }));
            brush.Add(Dyc_Style.Check(Dyc_L10n.T("paint.symmetry"), Dyc_PaintTool.SymmetryX, v => Dyc_PaintTool.SymmetryX = v));

            var row2 = Dyc_Style.Row(true);
            row2.Add(Dyc_Style.Btn(Dyc_PaintTool.Active ? Dyc_L10n.T("btn.stopPaint") : Dyc_L10n.T("btn.paint"), () =>
            {
                if (Dyc_PaintTool.Active) Dyc_PaintTool.End();
                else Dyc_PaintTool.Begin(_target, _paintGroup);
                Rebuild();
            }, primary: !Dyc_PaintTool.Active));

            row2.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.resetPose"), () =>
            {
                Dyc_PaintTool.ResetToBindPose(_target);
                Dyc_PaintTool.Invalidate();
                Rebuild();
            }));

            row2.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.clearPaint"), () =>
            {
                Dyc_PaintTool.ClearAll(_target);
                Rebuild();
            }));

            brush.Add(row2);
            wrap.Add(brush);

            var info = Dyc_Style.Card();
            info.Add(Dyc_Style.Caption(
                Dyc_L10n.T("paint.tip1")));
            info.Add(Dyc_Style.Caption(
                Dyc_L10n.T("paint.tip2")));
            wrap.Add(info);

            return wrap;
        }

        // ------------------------------------------------------------------ Gizmo

        VisualElement BuildGizmo()
        {
            var card = Dyc_Style.Card();
            card.Add(Dyc_Style.Header("Gizmo"));

            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.enabled"), Dyc_GizmoDraw.Enabled, v => Dyc_GizmoDraw.Enabled = v));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.selected"), Dyc_GizmoDraw.OnlySelected, v => Dyc_GizmoDraw.OnlySelected = v));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.bind"), Dyc_GizmoDraw.BindPose, v => Dyc_GizmoDraw.BindPose = v));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.source"), Dyc_GizmoDraw.DrawSource, v => Dyc_GizmoDraw.DrawSource = v));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.uncovered"), Dyc_GizmoDraw.DrawUncovered, v =>
            {
                Dyc_GizmoDraw.DrawUncovered = v;
                Dyc_GizmoDraw.InvalidateCache();
            }));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.occlude"), Dyc_GizmoDraw.Occlude, v =>
            {
                Dyc_GizmoDraw.Occlude = v;
                Dyc_GizmoDraw.InvalidateCache();
            }));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.painted"), Dyc_GizmoDraw.DrawPainted, v =>
            {
                Dyc_GizmoDraw.DrawPainted = v;
                Dyc_GizmoDraw.InvalidateCache();
            }));
            card.Add(Dyc_Style.Check(Dyc_L10n.T("gizmo.labels"), Dyc_GizmoDraw.Labels, v => Dyc_GizmoDraw.Labels = v));
            card.Add(Dyc_Style.IntField(Dyc_L10n.T("gizmo.max"), Dyc_GizmoDraw.MaxHulls, v => Dyc_GizmoDraw.MaxHulls = v));

            card.Add(Dyc_Style.Caption(
                Dyc_L10n.T("gizmo.tip")));

            var legend = Dyc_Style.Row(true);
            legend.style.marginTop = 6;
            legend.Add(Dyc_Style.Pill(Dyc_L10n.T("gizmo.legendOk"), Dyc_Style.Accent));
            legend.Add(Dyc_Style.Pill(Dyc_L10n.T("gizmo.legendPeak"), Dyc_Style.Error));
            legend.Add(Dyc_Style.Pill(Dyc_L10n.T("gizmo.legendUncov"), Dyc_Style.Error));
            card.Add(legend);

            return card;
        }

        // ------------------------------------------------------------------ Parts

        VisualElement BuildParts()
        {
            var wrap = new VisualElement();

            var card = Dyc_Style.Card();
            var head = Dyc_Style.Row(true);
            head.Add(Dyc_Style.Header(Dyc_L10n.T("lbl.elements")));
            head.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.autofill"), () =>
            {
                Dyc_Menu.AutoFillElements(_target);
                _healthDirty = true;
                Rebuild();
            }));

            // Заполнение из скелета — для ЛЮБОГО рига, не только Humanoid.
            // Именно оно делает Skin-режим пригодным для собак, лошадей и
            // прочих нечеловеческих скелетов: Humanoid-таблица на них даёт один
            // раздел «All», и запекание делит весь меш на лепестки.
            head.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.fromSkeleton"), () =>
            {
                Dyc_Menu.FillElementsFromSkeleton(_target);
                _healthDirty = true;
                Rebuild();
            }, primary: true));
            head.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.addPart"), () =>
            {
                Undo.RecordObject(_target, "DYC Add Element");
                _target.Elements.Add(new DycElement { name = "Part" + _target.Elements.Count, includeChildren = true });
                MarkDirty();
                Rebuild();
            }));
            card.Add(head);
            wrap.Add(card);

            for (int i = 0; i < _target.Elements.Count; i++)
            {
                int idx = i;
                var el = _target.Elements[i];
                if (el == null) continue;

                var c = Dyc_Style.Card();
                var title = Dyc_Style.Row();
                title.Add(Dyc_Style.Pill("#" + i, Dyc_Style.Muted));

                var nameField = new TextField { value = el.DisplayName };
                nameField.style.flexGrow = 1;
                nameField.style.fontSize = 11;
                nameField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Element Name");
                    el.name = e.newValue;
                    MarkDirty();
                });
                title.Add(nameField);

                title.Add(Dyc_Style.Btn("×", () =>
                {
                    Undo.RecordObject(_target, "DYC Remove Element");
                    _target.Elements.RemoveAt(idx);
                    MarkDirty();
                    Rebuild();
                }));
                c.Add(title);

                var boneField = new ObjectField(Dyc_L10n.T("part.bone")) { objectType = typeof(Transform), value = el.bone };
                boneField.style.fontSize = 11;
                boneField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Element Bone");
                    el.bone = e.newValue as Transform;
                    MarkDirty();
                    Rebuild();
                });
                c.Add(boneField);

                c.Add(Dyc_Style.Check(Dyc_L10n.T("part.children"), el.includeChildren, v =>
                {
                    Undo.RecordObject(_target, "DYC Element Children");
                    el.includeChildren = v;
                    MarkDirty();
                }));

                var evtField = new TextField(Dyc_L10n.T("lbl.eventName")) { value = el.eventName };
                evtField.style.fontSize = 11;
                evtField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Element Event");
                    el.eventName = e.newValue;
                    MarkDirty();
                });
                c.Add(evtField);

                c.Add(Dyc_Style.FloatField(Dyc_L10n.T("lbl.damage"), el.damageMultiplier, v =>
                {
                    Undo.RecordObject(_target, "DYC Element Damage");
                    el.damageMultiplier = v;
                    MarkDirty();
                }));

                wrap.Add(c);
            }

            var note = Dyc_Style.Card();
            note.Add(Dyc_Style.Caption(
                Dyc_L10n.T("part.rules")));
            wrap.Add(note);

            return wrap;
        }

        // ------------------------------------------------------------------ Materials

        VisualElement BuildMaterials()
        {
            var wrap = new VisualElement();

            var head = Dyc_Style.Card();
            var row = Dyc_Style.Row(true);
            row.Add(Dyc_Style.Header(Dyc_L10n.T("lbl.groups")));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.addGroup"), () =>
            {
                Undo.RecordObject(_target, "DYC Add Group");
                _target.Groups.Add(new DycMaterialGroup { name = "Group" + _target.Groups.Count, density = 1000f });
                MarkDirty();
                Rebuild();
            }));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.autoAssign"), () =>
            {
                Dyc_Menu.AutoAssignAllGroups(_target);
                Rebuild();
            }));
            head.Add(row);
            wrap.Add(head);

            for (int g = 0; g < _target.Groups.Count; g++)
            {
                int idx = g;
                var grp = _target.Groups[g];
                if (grp == null) continue;

                var c = Dyc_Style.Card();
                var title = Dyc_Style.Row();
                var dot = new VisualElement();
                dot.style.width = 10; dot.style.height = 10; dot.style.marginRight = 6;
                dot.style.borderTopLeftRadius = 5; dot.style.borderTopRightRadius = 5;
                dot.style.borderBottomLeftRadius = 5; dot.style.borderBottomRightRadius = 5;
                dot.style.backgroundColor = Dyc_GizmoDraw.GroupColor(g);
                title.Add(dot);

                var nameField = new TextField { value = grp.DisplayName };
                nameField.style.flexGrow = 1;
                nameField.style.fontSize = 11;
                nameField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Group Name");
                    grp.name = e.newValue;
                    MarkDirty();
                });
                title.Add(nameField);
                title.Add(Dyc_Style.Btn(Dyc_L10n.T("btn.pick"), () => Dyc_Menu.OpenMaterialPicker(_target, idx), primary: true));
                c.Add(title);

                var matField = new ObjectField(Dyc_L10n.T("lbl.material"))
                { objectType = typeof(PhysicMaterial), value = grp.material };
                matField.style.fontSize = 11;
                matField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Group Material");
                    grp.material = e.newValue as PhysicMaterial;
                    MarkDirty();
                });
                c.Add(matField);

                c.Add(Dyc_Style.FloatField(Dyc_L10n.T("lbl.density"), grp.density, v =>
                {
                    Undo.RecordObject(_target, "DYC Group Density");
                    grp.density = Mathf.Max(1f, v);
                    MarkDirty();
                }));

                var evtField = new TextField(Dyc_L10n.T("lbl.eventName")) { value = grp.eventName };
                evtField.style.fontSize = 11;
                evtField.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(_target, "DYC Group Event");
                    grp.eventName = e.newValue;
                    MarkDirty();
                });
                c.Add(evtField);

                if (grp.material != null)
                {
                    c.Add(Dyc_Style.Caption(
                        Dyc_L10n.T("mat.summary", grp.material.staticFriction, grp.material.dynamicFriction,
                            grp.material.bounciness, grp.material.frictionCombine, grp.material.bounceCombine)));

                    // Материал опознаётся по имени ассета: из самого
                    // PhysicMaterial происхождение не видно, а знать его надо —
                    // иначе «мокрая сталь» ничем не отличается от «стали».
                    if (Dyc_MaterialForge.Resolve(grp.material.name, out var known))
                        c.Add(Dyc_Style.Caption(
                            Dyc_MaterialPresets.Describe(known)
                            + (known.derived ? " · " + Dyc_L10n.T("forge.derived") : "")
                            + (string.IsNullOrEmpty(known.notes) ? "" : "\n" + known.notes)));
                }

                wrap.Add(c);
            }

            wrap.Add(BuildForgeCard());
            wrap.Add(BuildPairCard());
            wrap.Add(BuildPhysicsCard());
            return wrap;
        }

        // ------------------------------------------------------------------ генератор материалов

        static Dyc_SurfaceCondition _forgeCondition = Dyc_SurfaceCondition.Wet;
        static string _forgeBaseId = "steel";
        static int _forgeGroup;

        /// <summary>
        /// Генератор: базовый материал × условие поверхности.
        ///
        /// Отдельной карточкой, а не подменю в списке пресетов, по одной
        /// причине: 225 базовых материалов на 15 условий — это 3375 строк
        /// меню, которые невозможно листать. Здесь же видно и предпросмотр
        /// чисел, и обоснование, откуда они взялись.
        /// </summary>
        VisualElement BuildForgeCard()
        {
            var card = Dyc_Style.Card();
            card.Add(Dyc_Style.Header(Dyc_L10n.T("forge.title")));
            card.Add(Dyc_Style.Caption(Dyc_L10n.T("forge.hint")));

            var ids = new List<string>(Dyc_MaterialPresets.All.Length);
            var labels = new List<string>(Dyc_MaterialPresets.All.Length);
            for (int i = 0; i < Dyc_MaterialPresets.All.Length; i++)
            {
                ids.Add(Dyc_MaterialPresets.All[i].id);
                labels.Add(Dyc_MaterialPresets.All[i].display);
            }
            if (ids.Count == 0) return card;

            var basePopup = new PopupField<string>(Dyc_L10n.T("forge.base"), labels,
                Mathf.Clamp(ids.IndexOf(_forgeBaseId), 0, ids.Count - 1));
            basePopup.style.fontSize = 11;
            basePopup.RegisterValueChangedCallback(e =>
            {
                int idx = labels.IndexOf(e.newValue);
                if (idx < 0) return;
                _forgeBaseId = ids[idx];
                RefreshForgePreview();
            });
            card.Add(basePopup);

            var condField = new EnumField(Dyc_L10n.T("forge.condition"), _forgeCondition);
            condField.style.fontSize = 11;
            condField.RegisterValueChangedCallback(e =>
            {
                _forgeCondition = (Dyc_SurfaceCondition)e.newValue;
                RefreshForgePreview();
            });
            card.Add(condField);

            _forgePreview = Dyc_Style.Caption(string.Empty);
            card.Add(_forgePreview);
            RefreshForgePreview();

            if (_target.Groups.Count == 0)
            {
                card.Add(Dyc_Style.Caption(Dyc_L10n.T("forge.nogroup")));
                return card;
            }

            _forgeGroup = Mathf.Clamp(_forgeGroup, 0, _target.Groups.Count - 1);
            var groupLabels = new List<string>(_target.Groups.Count);
            for (int g = 0; g < _target.Groups.Count; g++) groupLabels.Add(_target.Groups[g].DisplayName);

            var groupPopup = new PopupField<string>(Dyc_L10n.T("forge.group"), groupLabels, _forgeGroup);
            groupPopup.style.fontSize = 11;
            groupPopup.RegisterValueChangedCallback(e =>
            {
                int idx = groupLabels.IndexOf(e.newValue);
                if (idx >= 0) _forgeGroup = idx;
            });
            card.Add(groupPopup);

            var row = Dyc_Style.Row(true);
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("forge.apply"), () =>
            {
                // База берётся по id, а не из захваченной локальной копии: окно
                // пересобирается на каждое изменение выбора, и копия устарела бы.
                if (!Dyc_MaterialPresets.TryGet(_forgeBaseId, out var b)) return;
                Dyc_Menu.ApplyForge(_target, _forgeGroup, b, _forgeCondition);
                Rebuild();
            }, primary: true));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("forge.generate"), () =>
            {
                if (!Dyc_MaterialPresets.TryGet(_forgeBaseId, out var b)) return;
                Dyc_Menu.GenerateVariants(_target, b);
            }));
            card.Add(row);
            card.Add(Dyc_Style.Caption(Dyc_L10n.T("forge.generateHint")));

            return card;
        }

        static Label _forgePreview;

        static void RefreshForgePreview()
        {
            if (_forgePreview == null) return;
            if (!Dyc_MaterialPresets.TryGet(_forgeBaseId, out var basePreset)) return;

            var forged = Dyc_MaterialForge.Forge(basePreset, _forgeCondition);
            _forgePreview.text =
                forged.display
                + "  ·  " + Dyc_MaterialPresets.Describe(forged)
                + "\n" + Dyc_L10n.T("forge.values", forged.staticFriction, forged.dynamicFriction,
                    forged.bounciness, forged.density)
                + (string.IsNullOrEmpty(forged.notes) ? "" : "\n" + forged.notes);
        }

        VisualElement BuildPairCard()
        {
            var card = Dyc_Style.Card();
            card.Add(Dyc_Style.Header(Dyc_L10n.T("mat.pair")));
            card.Add(Dyc_Style.Caption(Dyc_L10n.T("mat.pairHint")));

            if (_target.Groups.Count < 1) return card;

            for (int a = 0; a < _target.Groups.Count; a++)
            {
                for (int b = a; b < _target.Groups.Count; b++)
                {
                    var ma = _target.Groups[a]?.material;
                    var mb = _target.Groups[b]?.material;
                    if (ma == null && mb == null) continue;

                    float f = Dyc_MaterialPresets.ResolveFriction(ma, mb);
                    float bo = Dyc_MaterialPresets.ResolveBounce(ma, mb);
                    string label = $"{_target.Groups[a].DisplayName} × {_target.Groups[b].DisplayName}";
                    card.Add(Dyc_Style.KV(label, $"{f:F2} / {bo:F2}  {Dyc_MaterialPresets.Describe(f, bo)}"));
                }
            }

            return card;
        }

        VisualElement BuildPhysicsCard()
        {
            var card = Dyc_Style.Card();
            card.Add(Dyc_Style.Header(Dyc_L10n.T("phys.title")));

            var audit = Dyc_MaterialPresets.AuditPhysics();
            card.Add(Dyc_Style.KV(Dyc_L10n.T("phys.gravity"), $"{audit.gravity.y:F2}"));
            card.Add(Dyc_Style.KV(Dyc_L10n.T("phys.bounce"), $"{audit.bounceThreshold:F2}",
                audit.bounceThreshold > 0.6f ? Dyc_Style.Warn : Dyc_Style.Ok));
            card.Add(Dyc_Style.KV(Dyc_L10n.T("phys.velIter"), $"{audit.solverVelocityIterations}",
                audit.solverVelocityIterations < 2 ? Dyc_Style.Warn : Dyc_Style.Ok));
            card.Add(Dyc_Style.KV(Dyc_L10n.T("phys.contactOffset"), $"{audit.defaultContactOffset:F4}"));
            card.Add(Dyc_Style.KV(Dyc_L10n.T("phys.maxDepen"), $"{audit.defaultMaxDepenetrationVelocity:F2}"));

            for (int i = 0; i < audit.issues.Count; i++)
                card.Add(Dyc_Style.Caption("· " + audit.issues[i]));

            card.Add(Dyc_Style.Btn(Dyc_L10n.T("phys.apply"), () =>
            {
                Dyc_MaterialPresets.ApplyRecommendedPhysics(true, true, true);
                Rebuild();
            }, primary: true));

            return card;
        }

        // ------------------------------------------------------------------ Health

        VisualElement BuildSettings()
        {
            var wrap = new VisualElement();

            var langCard = Dyc_Style.Card();
            langCard.Add(Dyc_Style.Header(Dyc_L10n.T("set.language")));
            langCard.Add(BuildLanguagePicker(true));
            langCard.Add(Dyc_Style.Caption(Dyc_L10n.T("set.langNote")));
            wrap.Add(langCard);

            var diag = Dyc_Style.Card();
            diag.Add(Dyc_Style.Header(Dyc_L10n.T("set.diagnostics")));
            diag.Add(Dyc_Style.KV(Dyc_L10n.T("set.languagesLoaded"), Dyc_L10n.Count.ToString()));
            diag.Add(Dyc_Style.KV(Dyc_L10n.T("set.localeRoot"), Dyc_L10n.LocaleRoot));
            if (Dyc_L10n.Count <= 1)
                diag.Add(Dyc_Style.Caption(Dyc_L10n.T("set.langMissing", Dyc_L10n.LocaleRoot)));
            diag.Add(Dyc_Style.KV(Dyc_L10n.T("set.presets"),
                Dyc_L10n.T("forge.presets", Dyc_MaterialPresets.All.Length,
                    Dyc_MaterialPresets.CategoryKeys().Count)));
            diag.Add(Dyc_Style.KV(Dyc_L10n.T("set.gizmoDefault"), Dyc_GizmoDraw.Enabled ? Dyc_L10n.T("paint.on") : Dyc_L10n.T("paint.off")));
            wrap.Add(diag);

            var actions = Dyc_Style.Card();
            var row = Dyc_Style.Row(true);
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("set.openBaked"), () => Dyc_Menu.OpenBakedFolder(_target), primary: true));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("set.resetPrefs"), () => Dyc_Menu.ResetPreferences()));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("set.refreshIcon"), () =>
            {
                Dyc_Icon.Refresh();
                Debug.Log("[NDC] Иконка компонента перевешена.");
            }));
            actions.Add(row);
            wrap.Add(actions);

            return wrap;
        }

        VisualElement BuildHealth()
        {
            var wrap = new VisualElement();

            if (_healthDirty)
            {
                _health = Dyc_Health.Analyze(_target);
                _healthDirty = false;
            }

            var head = Dyc_Style.Card();
            var row = Dyc_Style.Row();
            Color scoreColor = _health.score >= 85 ? Dyc_Style.Ok
                : _health.score >= 60 ? Dyc_Style.Warn
                : Dyc_Style.Error;
            row.Add(Dyc_Style.Pill(Dyc_L10n.T("health.score", _health.score), scoreColor));
            row.Add(Dyc_Style.Pill(Dyc_L10n.T("health.grade", _health.Grade), scoreColor));
            row.Add(Dyc_Style.Btn(Dyc_L10n.T("health.rerun"), () =>
            {
                _healthDirty = true;
                _coverageDirty = true;
                Rebuild();
            }));
            head.Add(row);
            head.Add(Dyc_Style.Caption(Dyc_L10n.T("health.summary", _health.errors, _health.warnings, _health.hints)));
            wrap.Add(head);

            if (_health.issues.Count == 0)
            {
                var ok = Dyc_Style.Card();
                ok.Add(Dyc_Style.Body(Dyc_L10n.T("health.none")));
                wrap.Add(ok);
                return wrap;
            }

            for (int i = 0; i < _health.issues.Count; i++)
            {
                var issue = _health.issues[i];
                var c = Dyc_Style.Card();

                var t = Dyc_Style.Row(true);
                t.Add(Dyc_Style.Pill(issue.severity.ToString(), Dyc_Health.ColorOf(issue.severity)));
                t.Add(Dyc_Style.Body(issue.title));
                c.Add(t);

                if (!string.IsNullOrEmpty(issue.detail))
                    c.Add(Dyc_Style.Caption(issue.detail));

                if (issue.fix != null)
                {
                    var fixRow = Dyc_Style.Row();
                    fixRow.Add(Dyc_Style.Btn(issue.fixLabel ?? Dyc_L10n.T("health.fix"), () =>
                    {
                        issue.fix();
                        _healthDirty = true;
                        _coverageDirty = true;
                        Dyc_GizmoDraw.InvalidateCache();
                        Rebuild();
                    }, primary: true));
                    c.Add(fixRow);
                }

                wrap.Add(c);
            }

            return wrap;
        }
    }
}
