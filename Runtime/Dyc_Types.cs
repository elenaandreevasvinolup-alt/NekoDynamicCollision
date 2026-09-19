using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Источник коллайдеров. Skin следует за костями, Mesh — статичный или
    /// физический объект, Soft — мягкое тело (ткань, верёвка, желе), которое
    /// следует не за костями, а за кадрами деформации.
    ///
    /// Мягкий режим НЕ требует ни скелета, ни второго плагина: без источника
    /// деформации он вырождается в набор жёстких кластеров, которые едут
    /// вместе с объектом. Это осмысленная деградация, а не поломка.
    /// </summary>
    public enum DycMode
    {
        Skin = 0,
        Mesh = 1,
        Soft = 2
    }

    /// <summary>
    /// Вид мягкого тела. Разница только в топологии ограничений, поэтому она
    /// задаётся данными, а не отдельным компонентом на каждый вид.
    /// </summary>
    public enum DycSoftKind
    {
        /// <summary>Верёвка: цепочка, ограничения только вдоль и на изгиб.</summary>
        Rope = 0,
        /// <summary>Ткань: сетка, структурные, сдвиговые и изгибные ограничения.</summary>
        Cloth = 1,
        /// <summary>Объёмное тело: частицы внутри объёма плюс объёмное ограничение.</summary>
        Softbody = 2
    }

    /// <summary>
    /// Единственный уровень точности, который настраивает разработчик. Внутри отображается на (бюджет треугольников на оболочку, число оболочек на зону).
    /// Жёсткий предел выпуклой оболочки в PhysX — 255 вершин, поэтому бюджет работает вместе с отсечением вершин; это обрабатывается внутри плагина.
    /// </summary>
    public enum DycPrecision
    {
        Coarse = 0,
        Normal = 1,
        Fine = 2,
        Ultra = 3,

        /// <summary>
        /// Свои числа: количество оболочек на зону и бюджет треугольников
        /// задаются вручную.
        ///
        /// Зачем это нужно, а не только четыре пресета. Число оболочек — не
        /// «уровень качества», а форма приближения, и правильное значение
        /// зависит от объекта, а не от вкуса:
        ///   · стрела, кость, любой тонкий предмет — ОДНА оболочка. Две делят
        ///     тело пополам, и каждая половина даёт не половину тела, а
        ///     выпуклую оболочку половины поверхности, то есть тонкую
        ///     пластину. Снаружи это читается как «оболочки-лепестки»;
        ///   · торс или машина — наоборот, 4–8: одна оболочка на них слишком
        ///     груба, и её приходится расширять до объёма, которого в модели нет.
        ///
        /// Пресеты остаются для типовых случаев, Custom — для всех остальных.
        /// </summary>
        Custom = 4,

        /// <summary>
        /// Автоматическое число оболочек НА ЗОНУ.
        ///
        /// Одно число на всё тело — принципиально неверно, и это видно на любом
        /// персонаже: у фаланги пальца и у торса разная форма и разный масштаб.
        /// Общее значение 2 делит палец пополам (и получается лепесток вместо
        /// пальца), а торс на 2 оболочки слишком груб. Auto считает число
        /// оболочек для каждой зоны отдельно — по её размеру.
        ///
        /// Правило простое и объяснимое: маленькая зона получает одну оболочку,
        /// крупная — несколько, потому что её выпуклая оболочка иначе слишком
        /// груба. Точные пороги — в Dyc_Baker.AutoHullsFor.
        /// </summary>
        Auto = 5
    }

    /// <summary>Роль коллайдеров. Определяет, как настраиваются includeLayers / excludeLayers.</summary>
    public enum DycColliderRole
    {
        /// <summary>Участвует в физическом блокировании и расталкивании.</summary>
        Physics = 0,
        /// <summary>Только определение попаданий, без физического блокирования (hitbox).</summary>
        Hitbox = 1,
        /// <summary>Только обнаружение триггеров, без участия в физике.</summary>
        Trigger = 2
    }

    /// <summary>
    /// Форма коллайдера.
    ///
    /// Convex — оболочка (по умолчанию). Это единственная форма, которую PhysX
    /// принимает на НЕкинематическом Rigidbody и в паре mesh-mesh, поэтому она
    /// остаётся основной.
    ///
    /// Concave — НЕвыпуклая сетка: куски исходной поверхности вместо оболочек.
    /// Зачем, если выпуклость так удобна: выпуклая оболочка обязана «затянуть»
    /// впадины, и на суставах (подмышка, пах, шея) между оболочками соседних
    /// костей остаются дыры. Невыпуклая сетка их не имеет — ровно так работает
    /// системный коллайдер с выключенной выпуклостью.
    ///
    /// Цена невыпуклости честная и записана в проверке здоровья:
    ///   · только кинематическое/анимационное тело (PhysX не принимает
    ///     невыпуклую сетку на подвижном твёрдом теле);
    ///   · нет столкновений mesh-mesh;
    ///   · нет быстрой широкой фазы — на мобильных это дороже оболочек.
    /// </summary>
    public enum DycColliderShape
    {
        Convex = 0,
        Concave = 1
    }

    /// <summary>Способ обнаружения зональных Trigger. Unity не может привязать OnTriggerEnter к кости, поэтому используется только опрос.</summary>
    public enum DycTriggerMode
    {
        Off = 0,
        /// <summary>Низкочастотная выборка OverlapBox по element, бюджет распределяется по кадрам.</summary>
        Poll = 1
    }

    /// <summary>Стратегия самоколлизий рэгдолла.</summary>
    public enum DycSelfCollision
    {
        /// <summary>Все игнорируют друг друга (конфигурация, при которой рэгдолл Unity по умолчанию трясётся сильнее всего).</summary>
        Ignore = 0,
        /// <summary>Игнорируются только связи родитель-потомок и оболочки внутри одного element.</summary>
        Adjacent = 1,
        /// <summary>Всё включено, ответственность на разработчике.</summary>
        On = 2
    }

    /// <summary>Стратегия понижения детализации на расстоянии.</summary>
    public enum DycLodMode
    {
        Off = 0,
        /// <summary>При превышении расстояния все коллайдеры отключаются.</summary>
        Disable = 1,
        /// <summary>При превышении расстояния остаётся только самая крупная оболочка каждого element.</summary>
        Reduce = 2
    }

    public enum DycEventKind
    {
        CollisionEnter = 0,
        CollisionExit = 1,
        TriggerEnter = 2,
        TriggerExit = 3
    }

    /// <summary>Развёрнутые значения уровня точности.</summary>
    public struct DycPrecisionInfo
    {
        /// <summary>Бюджет треугольников на одну выпуклую оболочку.</summary>
        public int trisPerHull;
        /// <summary>Целевое число выпуклых оболочек на зону.</summary>
        public int hullsPerPart;
        /// <summary>Расширение границ зоны (в метрах), закрывает стыки и в первую очередь предотвращает пропуск попаданий.</summary>
        public float seamOverlap;
        /// <summary>Вершины с весом ниже этого значения отбрасываются, чтобы оболочка плотнее прилегала и была более "жёстко корректной".</summary>
        public float boneWeightThreshold;

        public static DycPrecisionInfo For(DycPrecision p)
        {
            switch (p)
            {
                case DycPrecision.Coarse:
                    return new DycPrecisionInfo { trisPerHull = 120, hullsPerPart = 1, seamOverlap = 0.004f, boneWeightThreshold = 0.35f };
                case DycPrecision.Fine:
                    return new DycPrecisionInfo { trisPerHull = 500, hullsPerPart = 4, seamOverlap = 0.002f, boneWeightThreshold = 0.15f };
                case DycPrecision.Ultra:
                    return new DycPrecisionInfo { trisPerHull = 1000, hullsPerPart = 8, seamOverlap = 0.001f, boneWeightThreshold = 0.05f };
                case DycPrecision.Auto:
                    // hullsPerPart = 0 — маркер «число оболочек считается по
                    // зоне» (см. Dyc_Baker.AutoHullsFor). Ноль здесь не «ничего»,
                    // а «решить на месте», поэтому он обязан быть явно обработан
                    // в запекании.
                    return new DycPrecisionInfo { trisPerHull = 400, hullsPerPart = 0, seamOverlap = 0.003f, boneWeightThreshold = 0.15f };
                default:
                    return new DycPrecisionInfo { trisPerHull = 250, hullsPerPart = 2, seamOverlap = 0.003f, boneWeightThreshold = 0.25f };
            }
        }

        public static string[] Names => new[] { "Coarse", "Normal", "Fine", "Ultra", "Custom", "Auto" };

        /// <summary>
        /// Разворачивает точность с учётом ручных чисел.
        ///
        /// РАЗДЕЛЕНИЕ ОБЯЗАННОСТЕЙ, которое здесь закодировано:
        ///
        ///   · ТРЕУГОЛЬНИКОВ НА ОБОЛОЧКУ — правится во ВСЕХ режимах, кроме Auto.
        ///     Это бюджет, а не форма: он зависит от того, сколько деталей
        ///     нужно сохранить, и решать это должен человек. Auto — единственный
        ///     режим, где и он считается сам.
        ///
        ///   · ЧИСЛО ОБОЛОЧЕК — правится только в Custom, и только для Mesh.
        ///     В Skin его ВСЕГДА решает AutoHullsFor по размеру зоны: у фаланги
        ///     и у торса разная форма, и общее число неизбежно врёт одному из
        ///     них. Раньше здесь стояло общее значение для всего тела — именно
        ///     поэтому палец делился пополам и превращался в лепесток.
        /// </summary>
        public static DycPrecisionInfo Resolve(DycPrecision precision, int customHulls, int customTris, float customWeight = 0.25f)
        {
            var info = For(precision);

            // Auto не даёт трогать ни то, ни другое: он для того и нужен.
            if (precision == DycPrecision.Auto) return info;

            // Нижняя граница 18, а не 32: выпуклая оболочка требует минимум 4
            // точки, но полезная форма начинается с двух десятков треугольников.
            info.trisPerHull = Mathf.Clamp(customTris, 18, 4000);

            if (precision == DycPrecision.Custom)
            {
                info.hullsPerPart = Mathf.Clamp(customHulls, 1, 32);
                info.boneWeightThreshold = Mathf.Clamp01(customWeight);
            }

            return info;
        }

    }

    /// <summary>
    /// Единица зонирования: одна кость (Skin) или одна целая сетка (Mesh).
    /// При includeChildren оболочки костей поддерева относятся к этому element, если их не перехватил более глубокий element
    /// (приоритет у ближайшего предка).
    /// </summary>
    [Serializable]
    public class DycElement
    {
        public string name;

        public Transform bone;

        public bool includeChildren = false;

        public string eventName;

        [Range(0f, 32f)]
        public float damageMultiplier = 1f;

        // ------------------------------------------------------------------ мягкий режим
        //
        // Эти поля читает решатель мягкого тела, а не NDC: у NDC нет ни
        // частиц, ни времени. Но живут они ЗДЕСЬ, а не в NDSC, по одной
        // причине: зона — это уже существующее понятие NDC (element), и
        // благодаря этому у мягких зон бесплатно появляются имена, события,
        // множители урона и приоритет «ближайший предок побеждает».
        //
        // Разделение обязанностей получается чистое:
        //   element.softness  — НАСКОЛЬКО мягкая зона (объём, поведение);
        //   group.material    — КАК её поверхность трётся и отскакивает.
        // Поэтому «мягкая зона с металлическим покрытием» — это не костыль,
        // а два независимых параметра.

        public DycSoftKind softKind = DycSoftKind.Cloth;

        [Range(0f, 1f)]
        public float softness = 0.5f;

        [Range(0f, 1f)]
        public float stiffness = 0.5f;

        [Range(0f, 1f)]
        public float damping = 0.1f;

        [Range(0f, 1f)]
        public float pinStrength = 1f;

        public int particleBudget = 0;

        public string DisplayName => string.IsNullOrEmpty(name)
            ? (bone != null ? bone.name : "(unnamed)")
            : name;
    }

    /// <summary>
    /// Группа материалов: набор выпуклых оболочек, сформированный разметкой граней кистью, с общим физическим материалом и плотностью.
    /// У одного объекта может быть несколько групп (жёсткие/мягкие/неразмеченные), всё получается за одно запекание.
    /// </summary>
    [Serializable]
    public class DycMaterialGroup
    {
        public string name = "Default";

        public PhysicMaterial material;

        public float density = 1000f;

        public string eventName;

        [Range(0f, 32f)]
        public float damageMultiplier = 1f;

        /// <summary>
        /// Исключить группу из запекания.
        ///
        /// Нужно для того же, для чего исключения по материалам: у
        /// персонажа бывают детали, которым коллайдер не нужен вовсе — плащ,
        /// ремень, ремешки разгрузки. Помечать их «нулевой плотностью» значило бы
        /// строить оболочку и потом её игнорировать; здесь треугольники группы не
        /// попадают ни в кластеризацию, ни в разложение, ни в коллайдеры.
        /// </summary>
        public bool exclude = false;

        public string DisplayName => string.IsNullOrEmpty(name) ? "Default" : name;
    }

    /// <summary>Полный контекст одного столкновения/срабатывания. Верхний уровень читает напрямую, без обращения к таблицам.</summary>
    public struct DycEvent
    {
        public DycEventKind kind;

        public int elementIndex;
        public string elementName;
        public Transform bone;

        public int groupIndex;
        public string groupName;
        public PhysicMaterial material;

        public Collider selfCollider;
        public Collider otherCollider;
        public Rigidbody otherBody;
        public GameObject otherRoot;

        public Vector3 point;
        public Vector3 normal;
        public float relativeSpeed;

        public float damageMultiplier;
        public string eventName;

        public float time;
    }
}
