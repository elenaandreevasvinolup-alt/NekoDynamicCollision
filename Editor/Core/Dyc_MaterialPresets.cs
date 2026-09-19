using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    public struct DycMaterialPreset
    {
        public string id;

        /// <summary>Техническое название. Остаётся английским намеренно: так
        /// подписаны библиотеки материалов в Unity и Unreal, и это избавляет
        /// от сотен переводов, которые всё равно никто не читает.</summary>
        public string display;

        /// <summary>Ключ категории без префикса; подпись берётся из cat.&lt;key&gt;.</summary>
        public string categoryKey;

        public float staticFriction;
        public float dynamicFriction;
        public float bounciness;

        /// <summary>кг/м³. Нужна для Auto Mass.</summary>
        public float density;

        /// <summary>Реальный ориентир, только для показа.</summary>
        public string notes;

        /// <summary>0 — ничего особенного не требуется. Иначе пресет просит
        /// опустить Physics.bounceThreshold до этого значения, иначе он «не отскакивает».</summary>
        public float requiredBounceThreshold;

        /// <summary>
        /// Преобладающая ткань. None у всего небиологического.
        ///
        /// Поле нужно генератору и подсказкам: по нему видно, почему у части
        /// тела такие числа. В PhysicMaterial оно не попадает — там его
        /// просто негде хранить.
        /// </summary>
        public Dyc_TissueKind tissue;

        /// <summary>
        /// 0 — жёсткое, 1 — предельно мягкое. ЭТО ВХОД ГЕНЕРАТОРА, а не
        /// спрятанное свойство: из мягкости выводятся трение и упругость, а в
        /// ассете она не хранится, потому что PhysicMaterial её не выражает.
        /// </summary>
        public float softness;

        /// <summary>true — посчитано генератором (ткани, части тела, варианты по
        /// условию поверхности), а не взято строкой из таблицы.</summary>
        public bool derived;
    }

    public enum DycPhysicsIssueKind
    {
        Info = 0,
        Bounce = 1,
        VelocityIterations = 2,
        ContactOffset = 3,
        Gravity = 4
    }

    public class DycPhysicsIssue
    {
        public DycPhysicsIssueKind kind;
        public string titleKey;
        public string detailKey;
        public object[] args;
    }

    /// <summary>
    /// Библиотека пресетов физических материалов: поведение и арифметика.
    ///
    /// Сама таблица живёт в <see cref="Dyc_MaterialLibrary"/>, вывод значений из
    /// тканей и условий поверхности — в <see cref="Dyc_MaterialForge"/>. Здесь
    /// остаётся то, что не является ни данными, ни генератором: правила
    /// сочетания пар, аудит физики проекта и словесная оценка.
    ///
    /// Честная граница (это надо знать, иначе ожидания не сойдутся):
    /// у PhysicMaterial в Unity всего 4 числа и 2 режима combine. Он не умеет
    /// трение качения, анизотропное трение, вязкость, пластику и температуру.
    /// Поэтому здесь «табличное отображение с обоснованием + рабочие пресеты»,
    /// а не физическая симуляция.
    ///
    /// Стратегия combine (единая на всю библиотеку, иначе результат непредсказуем):
    ///   · трение — Multiply, чтобы любая скользкая поверхность доминировала;
    ///   · отскок  — Maximum,  чтобы любой упругий материал доминировал.
    /// Приоритет в Unity: Average &lt; Minimum &lt; Multiply &lt; Maximum.
    /// </summary>
    public static class Dyc_MaterialPresets
    {
        public const PhysicMaterialCombine FrictionMode = PhysicMaterialCombine.Multiply;
        public const PhysicMaterialCombine BounceMode = PhysicMaterialCombine.Maximum;

        /// <summary>Полная библиотека: таблица плюс выведенные генератором.</summary>
        public static readonly DycMaterialPreset[] All = Dyc_MaterialLibrary.All;

        /// <summary>Сколько всего пресетов. Показывается в окне и в меню.</summary>
        public static int Count { get { return All.Length; } }

        /// <summary>
        /// Порядок категорий в меню и в списках. Он не совпадает с порядком
        /// строк в таблице, потому что части тела логично стоят рядом с
        /// биологией, а не в самом конце. Категория, которой здесь нет,
        /// дописывается в конец: таблицу можно расширять, не трогая это.
        /// </summary>
        static readonly string[] CategoryOrder =
        {
            "metal", "ceramic", "plastic", "glass", "wood", "stone",
            "rubber", "fabric", "body", "tissue", "organic", "ice", "food", "other"
        };

        public static List<string> CategoryKeys()
        {
            var found = new List<string>();
            for (int i = 0; i < All.Length; i++)
                if (!found.Contains(All[i].categoryKey)) found.Add(All[i].categoryKey);

            var ordered = new List<string>(found.Count);
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                if (!found.Contains(CategoryOrder[i])) continue;
                ordered.Add(CategoryOrder[i]);
                found.Remove(CategoryOrder[i]);
            }
            ordered.AddRange(found);
            return ordered;
        }

        public static string CategoryLabel(string key)
        {
            return Dyc_L10n.T("cat." + key);
        }

        public static int CountIn(string categoryKey)
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++)
                if (All[i].categoryKey == categoryKey) n++;
            return n;
        }

        public static bool TryGet(string id, out DycMaterialPreset preset)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].id == id) { preset = All[i]; return true; }
            }
            preset = default;
            return false;
        }

        public static string TissueLabel(Dyc_TissueKind kind)
        {
            if (kind == Dyc_TissueKind.None) return string.Empty;

            string key = "forge.tissue." + kind.ToString().ToLowerInvariant();
            string s = Dyc_L10n.T(key);
            return s == key ? Dyc_MaterialForge.TissueOf(kind).display : s;
        }

        // ------------------------------------------------------------------ поведение пары

        static int Priority(PhysicMaterialCombine m)
        {
            switch (m)
            {
                case PhysicMaterialCombine.Minimum: return 1;
                case PhysicMaterialCombine.Multiply: return 2;
                case PhysicMaterialCombine.Maximum: return 3;
                default: return 0; // Average
            }
        }

        /// <summary>Считает реально действующее значение для пары — по тому же
        /// приоритету, что применяет Unity.</summary>
        public static float Combine(float a, float b, PhysicMaterialCombine ma, PhysicMaterialCombine mb)
        {
            PhysicMaterialCombine mode = Priority(ma) >= Priority(mb) ? ma : mb;
            switch (mode)
            {
                case PhysicMaterialCombine.Minimum: return Mathf.Min(a, b);
                case PhysicMaterialCombine.Multiply: return a * b;
                case PhysicMaterialCombine.Maximum: return Mathf.Max(a, b);
                default: return (a + b) * 0.5f;
            }
        }

        public static float ResolveFriction(PhysicMaterial a, PhysicMaterial b)
        {
            if (a == null || b == null) return 0.6f;
            return Combine(a.dynamicFriction, b.dynamicFriction, a.frictionCombine, b.frictionCombine);
        }

        public static float ResolveBounce(PhysicMaterial a, PhysicMaterial b)
        {
            if (a == null || b == null) return 0f;
            return Combine(a.bounciness, b.bounciness, a.bounceCombine, b.bounceCombine);
        }

        public static string Describe(float friction, float bounce)
        {
            string f =
                friction < 0.10f ? Dyc_L10n.T("desc.verySlippery") :
                friction < 0.30f ? Dyc_L10n.T("desc.slippery") :
                friction < 0.55f ? Dyc_L10n.T("desc.normal") :
                friction < 0.75f ? Dyc_L10n.T("desc.grippy") :
                                   Dyc_L10n.T("desc.veryGrippy");

            string b =
                bounce < 0.05f ? Dyc_L10n.T("desc.noBounce") :
                bounce < 0.25f ? Dyc_L10n.T("desc.lowBounce") :
                bounce < 0.55f ? Dyc_L10n.T("desc.bouncy") :
                                 Dyc_L10n.T("desc.veryBouncy");

            return f + " / " + b;
        }

        /// <summary>Словесная оценка мягкости. Показывается там, где у
        /// материала есть тканевое происхождение: у стали её просто нет.</summary>
        public static string SoftnessWord(float softness)
        {
            return softness < 0.20f ? Dyc_L10n.T("soft.hard") :
                   softness < 0.45f ? Dyc_L10n.T("soft.firm") :
                   softness < 0.70f ? Dyc_L10n.T("soft.soft") :
                   softness < 0.90f ? Dyc_L10n.T("soft.verySoft") :
                                     Dyc_L10n.T("soft.gel");
        }

        /// <summary>Строка описания пресета: оценка пары плюс происхождение.</summary>
        public static string Describe(DycMaterialPreset preset)
        {
            string s = Describe(preset.dynamicFriction, preset.bounciness);
            if (preset.tissue != Dyc_TissueKind.None)
                s += " · " + TissueLabel(preset.tissue) + " · " + SoftnessWord(preset.softness);
            return s;
        }

        // ------------------------------------------------------------------ физика проекта

        public struct PhysicsAudit
        {
            public Vector3 gravity;
            public float bounceThreshold;
            public float defaultContactOffset;
            public float defaultMaxDepenetrationVelocity;
            public float sleepThreshold;
            public int solverIterations;
            public int solverVelocityIterations;

            public List<DycPhysicsIssue> issues;
        }

        public static PhysicsAudit AuditPhysics()
        {
            var a = new PhysicsAudit
            {
                gravity = Physics.gravity,
                bounceThreshold = Physics.bounceThreshold,
                defaultContactOffset = Physics.defaultContactOffset,
                defaultMaxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity,
                sleepThreshold = Physics.sleepThreshold,
                solverIterations = Physics.defaultSolverIterations,
                solverVelocityIterations = Physics.defaultSolverVelocityIterations,
                issues = new List<DycPhysicsIssue>()
            };

            if (a.bounceThreshold > 0.6f)
            {
                a.issues.Add(new DycPhysicsIssue
                {
                    kind = DycPhysicsIssueKind.Bounce,
                    titleKey = "h.physicsBounce.title",
                    detailKey = "h.physicsBounce.detail",
                    args = new object[] { a.bounceThreshold }
                });
            }

            if (a.solverVelocityIterations < 2)
            {
                a.issues.Add(new DycPhysicsIssue
                {
                    kind = DycPhysicsIssueKind.VelocityIterations,
                    titleKey = "h.physicsVelocity.title",
                    detailKey = "h.physicsVelocity.detail",
                    args = new object[] { a.solverVelocityIterations }
                });
            }

            if (Mathf.Abs(a.gravity.y + 9.81f) > 0.5f)
            {
                float factor = Mathf.Abs(9.81f / a.gravity.y);
                a.issues.Add(new DycPhysicsIssue
                {
                    kind = DycPhysicsIssueKind.Gravity,
                    titleKey = "h.physicsGravity.title",
                    detailKey = "h.physicsGravity.detail",
                    args = new object[] { a.gravity.y, factor }
                });
            }

            if (a.defaultContactOffset > 0.02f)
            {
                a.issues.Add(new DycPhysicsIssue
                {
                    kind = DycPhysicsIssueKind.ContactOffset,
                    titleKey = "h.physicsContact.title",
                    detailKey = "h.physicsContact.detail",
                    args = new object[] { a.defaultContactOffset }
                });
            }

            return a;
        }

        public static void ApplyRecommendedPhysics(bool lowerBounce, bool raiseVelocityIterations, bool tightenContact)
        {
            if (lowerBounce && Physics.bounceThreshold > 0.5f) Physics.bounceThreshold = 0.3f;
            if (raiseVelocityIterations && Physics.defaultSolverVelocityIterations < 2)
                Physics.defaultSolverVelocityIterations = 2;
            if (tightenContact && Physics.defaultContactOffset > 0.01f)
                Physics.defaultContactOffset = 0.01f;
        }
    }
}
