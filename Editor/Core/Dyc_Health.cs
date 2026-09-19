using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    public enum DycSeverity
    {
        Hint = 0,
        Warning = 1,
        Error = 2
    }

    public class DycIssue
    {
        public DycSeverity severity;
        public string title;
        public string detail;

        /// <summary>Исправление в один клик. null — только вручную.</summary>
        public Action fix;
        public string fixLabel;
    }

    public struct DycHealthReport
    {
        public int score;
        public List<DycIssue> issues;
        public int errors;
        public int warnings;
        public int hints;

        public string Grade => score >= 95 ? "S" : score >= 85 ? "A" : score >= 70 ? "B" : score >= 50 ? "C" : "D";
    }

    /// <summary>
    /// Проверка здоровья столкновений. Повторяет подход NSG (оценка + список
    /// проблем + исправление в один клик).
    ///
    /// Всё считается в редакторе, в рантайме это не стоит ничего.
    /// </summary>
    public static class Dyc_Health
    {
        public static DycHealthReport Analyze(Dyc_DynamicCollision target)
        {
            var report = new DycHealthReport { score = 100, issues = new List<DycIssue>() };
            if (target == null)
            {
                report.score = 0;
                return report;
            }

            var set = target.BakedSet;

            // ---- 1. запекание
            if (set == null || set.hulls.Count == 0)
            {
                Add(report, DycSeverity.Error,
                    Dyc_L10n.T("h.nobake.title"),
                    Dyc_L10n.T("h.nobake.detail"),
                    () => Dyc_Menu.Bake(target),
                    Dyc_L10n.T("h.nobake.fix"));
            }
            else
            {
                // ---- 2. потолок вершин
                //
                // Только для выпуклой формы: потолок 255 — ограничение
                // convex-cooking PhysX. У невыпуклой сетки его нет, и проверять
                // его там значило бы ругаться на здоровый результат.
                int maxV = set.MaxHullVertices;
                if (set.colliderShape == DycColliderShape.Convex && maxV > Dyc_Cluster.PhysXMaxHullVertices)
                {
                    Add(report, DycSeverity.Error,
                        Dyc_L10n.T("h.peakOver.title", maxV),
                        Dyc_L10n.T("h.peakOver.detail"),
                        () => Dyc_Menu.RebakeWithLowerPrecision(target),
                        Dyc_L10n.T("h.peakOver.fix"));
                }
                else if (maxV > 200)
                {
                    Add(report, DycSeverity.Hint,
                        Dyc_L10n.T("h.peakNear.title", maxV),
                        Dyc_L10n.T("h.peakNear.detail"),
                        null, null);
                }

                // ---- 2b. форма коллайдеров
                CheckShape(report, target, set);

                // ---- 3. вырожденные кластеры
                if (set.degenerateClusters > 0)
                {
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.degenerate.title", set.degenerateClusters),
                        Dyc_L10n.T("h.degenerate.detail"),
                        null, null);
                }

                // ---- 4. треугольники без раздела
                if (set.unassignedTriangles > 0)
                {
                    float pct = set.sourceTriangleCount > 0
                        ? set.unassignedTriangles * 100f / set.sourceTriangleCount
                        : 0f;

                    Add(report, pct > 5f ? DycSeverity.Error : DycSeverity.Warning,
                        Dyc_L10n.T("h.unassigned.title", set.unassignedTriangles, pct),
                        Dyc_L10n.T("h.unassigned.detail"),
                        () => Dyc_Menu.AutoFillElements(target),
                        Dyc_L10n.T("h.unassigned.fix"));
                }

                // ---- 5. отпечаток исходного меша
                var sources = Dyc_Baker.CollectSources(target);
                if (sources.Count > 0 && !string.IsNullOrEmpty(set.sourceHash))
                {
                    var hashes = new List<string>();
                    for (int i = 0; i < sources.Count; i++)
                        if (sources[i].mesh != null) hashes.Add(Dyc_PaintMask.Hash(sources[i].mesh));

                    if (string.Join("|", hashes) != set.sourceHash)
                    {
                        Add(report, DycSeverity.Error,
                            Dyc_L10n.T("h.hashMismatch.title"),
                            Dyc_L10n.T("h.hashMismatch.detail"),
                            () => Dyc_Menu.Bake(target),
                            Dyc_L10n.T("h.hashMismatch.fix"));
                    }
                }

                // ---- 6. группы материалов
                CheckGroups(report, target, set);

                // ---- 6b. исключения (кости, рендереры, группы)
                CheckExclusions(report, target, set);

                // ---- 6c. мягкие зоны (кадры деформации, самоколлизии)
                CheckSoft(report, target, set);

                // ---- 7. фильтр слоёв
                if (target.Role != DycColliderRole.Physics && target.EditIncludeLayers.value == 0)
                {
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.layers.title", target.Role),
                        Dyc_L10n.T("h.layers.detail"),
                        () => Dyc_Menu.SetDefaultHitboxLayers(target),
                        Dyc_L10n.T("h.layers.fix"));
                }
            }

            // ---- 8. физическая сборка
            CheckPhysicsSetup(report, target);

            // ---- 9. имена событий
            if (Dyc_Events.UnmatchedCount > 0)
            {
                Add(report, DycSeverity.Hint,
                    Dyc_L10n.T("h.dispatch.title", Dyc_Events.UnmatchedCount, Dyc_Events.LastUnmatchedName),
                    Dyc_L10n.T("h.dispatch.detail"),
                    () => Dyc_Events.ResetStats(),
                    Dyc_L10n.T("h.dispatch.fix"));
            }

            // ---- 10. настройки физики проекта
            var audit = Dyc_MaterialPresets.AuditPhysics();
            for (int i = 0; i < audit.issues.Count; i++)
            {
                var issue = audit.issues[i];

                Action fix = null;
                string fixLabel = null;
                if (issue.kind == DycPhysicsIssueKind.Bounce)
                {
                    fix = () => Dyc_MaterialPresets.ApplyRecommendedPhysics(true, false, false);
                    fixLabel = Dyc_L10n.T("h.physicsBounce.fix");
                }
                else if (issue.kind == DycPhysicsIssueKind.VelocityIterations)
                {
                    fix = () => Dyc_MaterialPresets.ApplyRecommendedPhysics(false, true, false);
                    fixLabel = Dyc_L10n.T("h.physicsVelocity.fix");
                }

                Add(report,
                    issue.kind == DycPhysicsIssueKind.Bounce ? DycSeverity.Warning : DycSeverity.Hint,
                    Dyc_L10n.T(issue.titleKey, issue.args),
                    Dyc_L10n.T(issue.detailKey, issue.args),
                    fix, fixLabel);
            }

            // ---- итог
            for (int i = 0; i < report.issues.Count; i++)
            {
                switch (report.issues[i].severity)
                {
                    case DycSeverity.Error: report.errors++; report.score -= 15; break;
                    case DycSeverity.Warning: report.warnings++; report.score -= 7; break;
                    default: report.hints++; report.score -= 2; break;
                }
            }
            report.score = Mathf.Clamp(report.score, 0, 100);
            return report;
        }

        static void CheckGroups(DycHealthReport report, Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            var groups = target.Groups;
            if (groups == null || groups.Count == 0)
            {
                Add(report, DycSeverity.Error,
                    Dyc_L10n.T("h.noGroups.title"),
                    Dyc_L10n.T("h.noGroups.detail"),
                    () => Dyc_Baker.EnsureGroups(target),
                    Dyc_L10n.T("h.noGroups.fix"));
                return;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g] == null) continue;

                if (groups[g].material == null)
                {
                    int gi = g;
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.noMaterial.title", groups[g].DisplayName),
                        Dyc_L10n.T("h.noMaterial.detail"),
                        () => Dyc_Menu.OpenMaterialPicker(target, gi),
                        Dyc_L10n.T("h.noMaterial.fix"));
                }

                int hullCount = 0;
                float volume = 0f;
                for (int i = 0; i < set.hulls.Count; i++)
                {
                    if (set.hulls[i] == null || set.hulls[i].groupIndex != g) continue;
                    hullCount++;
                    volume += set.hulls[i].volume;
                }

                if (hullCount == 0)
                {
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.emptyGroup.title", groups[g].DisplayName),
                        Dyc_L10n.T("h.emptyGroup.detail"),
                        null, null);
                }
                else if (volume > 0f && hullCount > 24)
                {
                    int gi = g;
                    Add(report, DycSeverity.Hint,
                        Dyc_L10n.T("h.fragmented.title", groups[g].DisplayName, hullCount),
                        Dyc_L10n.T("h.fragmented.detail"),
                        () => Dyc_Menu.MergeSmallestGroup(target, gi),
                        Dyc_L10n.T("h.fragmented.fix"));
                }
            }
        }

        /// <summary>
        /// Проверки, зависящие от ФОРМЫ коллайдера.
        ///
        /// Невыпуклая сетка — не «оболочка поточнее», а другой контракт с PhysX,
        /// и у него есть жёсткие ограничения. Молчать о них нельзя: проект
        /// соберётся, а в игре коллайдер не заработает или уронит физику.
        /// </summary>
        static void CheckShape(DycHealthReport report, Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            if (set.colliderShape != DycColliderShape.Concave) return;

            // 1. PhysX не принимает невыпуклую сетку на подвижном твёрдом теле.
            var rbs = target.GetComponentsInParent<Rigidbody>();
            bool dynamic = false;
            for (int i = 0; i < rbs.Length; i++)
                if (rbs[i] != null && !rbs[i].isKinematic) dynamic = true;

            if (dynamic)
            {
                Add(report, DycSeverity.Error,
                    Dyc_L10n.T("h.concaveDynamic.title"),
                    Dyc_L10n.T("h.concaveDynamic.detail"),
                    () => Dyc_Menu.MakeKinematic(target),
                    Dyc_L10n.T("h.concaveDynamic.fix"));
            }

            // 2. Неравномерный масштаб: невыпуклая сетка считается в локальном
            //    пространстве и при растяжении перестаёт совпадать с моделью.
            Vector3 s = target.transform.lossyScale;
            if (Mathf.Abs(s.x - s.y) > 0.01f || Mathf.Abs(s.y - s.z) > 0.01f || s.x < 0f)
            {
                Add(report, DycSeverity.Warning,
                    Dyc_L10n.T("h.concaveScale.title", s.x, s.y, s.z),
                    Dyc_L10n.T("h.concaveScale.detail"),
                    null, null);
            }

            // 3. Столкновения mesh-mesh не поддерживаются — это важно тем, у
            //    кого персонаж бьётся о другого персонажа, а не об окружение.
            Add(report, DycSeverity.Hint,
                Dyc_L10n.T("h.concaveMeshMesh.title"),
                Dyc_L10n.T("h.concaveMeshMesh.detail"),
                null, null);
        }

        /// <summary>Проверки исключений: мёртвая настройка и «исключено всё».</summary>
        static void CheckExclusions(DycHealthReport report, Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            var groups = target.Groups;

            if (groups != null && groups.Count > 0)
            {
                bool any = false;
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i] != null && !groups[i].exclude) { any = true; break; }

                if (!any)
                {
                    Add(report, DycSeverity.Error,
                        Dyc_L10n.T("h.allExcluded.title"),
                        Dyc_L10n.T("h.allExcluded.detail"),
                        null, null);
                }
            }

            var skin = target.SourceSkin;
            if (skin == null) skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
            var bones = skin != null ? skin.bones : null;
            var excluded = target.ExcludedBones;

            if (bones == null || excluded == null) return;

            for (int e = 0; e < excluded.Count; e++)
            {
                var b = excluded[e];
                if (b == null) continue;

                bool found = false;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] == b) { found = true; break; }

                if (!found)
                {
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.excludeDead.title", b.name),
                        Dyc_L10n.T("h.excludeDead.detail"),
                        null, null);
                }
            }
        }

        /// <summary>Мягкие зоны: самоколлизии и наличие источника кадров.</summary>
        static void CheckSoft(DycHealthReport report, Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            if (set.clusterCount <= 0) return;

            if (target.SelfCollision == DycSelfCollision.On)
            {
                Add(report, DycSeverity.Warning,
                    Dyc_L10n.T("h.softSelfCollision.title"),
                    Dyc_L10n.T("h.softSelfCollision.detail"),
                    () => Dyc_Menu.SetSelfCollision(target, DycSelfCollision.Adjacent),
                    Dyc_L10n.T("h.softSelfCollision.fix"));
            }

            if (!Dyc_DeformRegistry.Installed)
            {
                Add(report, DycSeverity.Hint,
                    Dyc_L10n.T("h.softNoSource.title"),
                    Dyc_L10n.T("h.softNoSource.detail"),
                    null, null);
            }
        }

        static void CheckPhysicsSetup(DycHealthReport report, Dyc_DynamicCollision target)
        {
            if (!Application.isPlaying)
            {
                // В редакторе можно судить только статически
                if (!target.AutoRigidbody && target.GetComponentInParent<Rigidbody>() == null)
                {
                    Add(report, DycSeverity.Error,
                        Dyc_L10n.T("h.noRigidbody.title"),
                        Dyc_L10n.T("h.noRigidbody.detail"),
                        () => Dyc_Menu.EnableAutoRigidbody(target),
                        Dyc_L10n.T("h.noRigidbody.fix"));
                }
            }

            var rbs = target.GetComponentsInParent<Rigidbody>();
            for (int i = 0; i < rbs.Length; i++)
            {
                var rb = rbs[i];
                if (rb == null || rb.isKinematic) continue;

                if (rb.mass < 0.01f)
                {
                    Add(report, DycSeverity.Warning,
                        Dyc_L10n.T("h.massSmall.title", rb.name, rb.mass),
                        Dyc_L10n.T("h.massSmall.detail"),
                        () => Dyc_Menu.EnableAutoMass(target),
                        Dyc_L10n.T("h.massSmall.fix"));
                }
                else if (rb.mass > 500f)
                {
                    Add(report, DycSeverity.Hint,
                        Dyc_L10n.T("h.massLarge.title", rb.name, rb.mass),
                        Dyc_L10n.T("h.massLarge.detail"),
                        null, null);
                }
            }

            if (target.SelfCollision == DycSelfCollision.On && target.Mode == DycMode.Skin)
            {
                Add(report, DycSeverity.Hint,
                    Dyc_L10n.T("h.selfCollision.title"),
                    Dyc_L10n.T("h.selfCollision.detail"),
                    () => Dyc_Menu.SetSelfCollision(target, DycSelfCollision.Adjacent),
                    Dyc_L10n.T("h.selfCollision.fix"));
            }
        }

        static void Add(DycHealthReport report, DycSeverity sev, string title, string detail, Action fix, string fixLabel)
        {
            report.issues.Add(new DycIssue
            {
                severity = sev,
                title = title,
                detail = detail,
                fix = fix,
                fixLabel = fixLabel
            });
        }

        public static Color ColorOf(DycSeverity sev)
        {
            switch (sev)
            {
                case DycSeverity.Error: return new Color(0.92f, 0.35f, 0.35f);
                case DycSeverity.Warning: return new Color(0.95f, 0.72f, 0.30f);
                default: return new Color(0.55f, 0.70f, 0.85f);
            }
        }
    }
}
