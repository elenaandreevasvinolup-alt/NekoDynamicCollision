using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Связка «материал исходного меша → физический материал».
    ///
    /// Соответствие материалов: если у персонажа
    /// несколько материалов (кожа, броня, ткань), каждому можно сразу выдать
    /// свой PhysicMaterial, не размечая грани кистью. Кисть остаётся для
    /// случаев, когда материал один, а зоны разные.
    /// </summary>
    [Serializable]
    public class DycMaterialAssociation
    {
        public Material material;
        public PhysicMaterial physicsMaterial;
    }

    /// <summary>
    /// Продвинутые настройки: всё, что НЕ нужно для обычного использования.
    ///
    /// Зачем отдельным объектом. Компонент и без того большой, а эти поля —
    /// другой сценарий: их трогает тот, кто хочет поведение с обновлением (генерация
    /// без запекания, живое обновление под бюджет) или тонкую настройку
    /// бюджетов и исключений. Обычный пользователь ставит компонент, жмёт
    /// «запечь» и больше сюда не заходит.
    ///
    /// Значения по умолчанию подобраны так, чтобы ВЫКЛЮЧЕННЫЕ возможности
    /// ничего не стоили: без generateOnStart и liveUpdate в рантайме не
    /// происходит ни одного лишнего вычисления.
    /// </summary>
    [Serializable]
    public class DycAdvancedSettings
    {
        // ------------------------------------------------------------------
        // Обычное использование: коллайдеры как системные
        // ------------------------------------------------------------------

        /// <summary>
        /// Построить коллайдеры при запуске, если запекания нет.
        ///
        /// Это и есть «как системный коллайдер»: компонент достаточно
        /// добавить, запекание не обязательно. Оболочки строятся по костям
        /// прямо из меша — грубее запечённых (нет зон, нет разложения), зато
        /// без шага в редакторе.
        /// </summary>
        [Tooltip("Build colliders at startup when nothing has been baked. Add the component and press Play — no bake step.")]
        public bool generateOnStart = false;

        /// <summary>Коллайдеры-триггеры, как у системного Collider.isTrigger.</summary>
        [Tooltip("Make the generated colliders triggers, exactly like Collider.isTrigger.")]
        public bool isTrigger = false;

        /// <summary>Куда присылать обычные OnCollision*/OnTrigger* сообщения.
        ///
        /// По умолчанию они приходят на объект с Rigidbody — как у системных
        /// коллайдеров. Если скрипт-получатель висит на другом объекте
        /// (например, на корне персонажа, а Rigidbody на кости), укажите его
        /// здесь, и события будут продублированы туда штатными сообщениями
        /// Unity: SendMessage("OnCollisionEnter", collision) и так далее.
        /// </summary>
        [Tooltip("Extra object that should also receive standard OnCollision*/OnTrigger* messages. Leave empty for Unity's normal behaviour.")]
        public Transform collisionReceiver;

        // ------------------------------------------------------------------
        // Живое обновление
        // ------------------------------------------------------------------

        /// <summary>
        /// Пересобирать оболочки по текущей позе скелета.
        ///
        /// Выключено по умолчанию: философия плагина — «в рантайме только
        /// загрузка». Включается там, где важна точность при сильной
        /// деформации, и работает в рамках бюджета ниже.
        /// </summary>
        [Tooltip("Rebuild hulls from the current skinned pose at runtime, inside a CPU budget. Costs CPU; off by default.")]
        public bool liveUpdate = false;

        /// <summary>Обновляться постоянно или один проход по вызову.</summary>
        [Tooltip("Keep updating every frame, or run a single pass when asked.")]
        public bool liveUpdateContinuous = true;

        /// <summary>Бюджет простоя, мс на кадр.</summary>
        [Range(0.05f, 4f)]
        [Tooltip("CPU budget in milliseconds while the character is idle.")]
        public double idleCpuBudgetMs = 0.2;

        /// <summary>Бюджет активности, мс на кадр.</summary>
        [Range(0.05f, 8f)]
        [Tooltip("CPU budget in milliseconds while the character is moving fast.")]
        public double activeCpuBudgetMs = 1.0;

        /// <summary>Порог движения меша: если скин почти не изменился, обновление пропускается.</summary>
        [Range(0f, 1f)]
        [Tooltip("Skip the update when the skinned mesh has moved less than this. Saves work on a standing character.")]
        public float meshUpdateThreshold = 0.02f;

        // ------------------------------------------------------------------
        // LOD столкновений персонажа
        // ------------------------------------------------------------------

        /// <summary>
        /// Гасить часть оболочек по расстоянию до камеры.
        ///
        /// Зачем это NDC, если LOD столкновений уже существует. Обычный LOD работает
        /// по СТАТИЧЕСКИМ телам сцены (onlyStatic) и персонажа не касается.
        /// А персонаж — это сотня оболочек на каждого, и на телефоне именно они,
        /// а не статичный уровень, решают, сколько бойцов потянет сцена.
        ///
        /// Гасятся не все оболочки разом: вблизи видно всё, вдали остаётся
        /// костяк из самых крупных частей. Так дальний боец всё ещё получает
        /// попадания в корпус, но перестаёт платить за пальцы.
        /// </summary>
        [Tooltip("Disable part of the hulls by distance to the camera. Ordinary collision LOD covers static props; this covers characters.")]
        public bool collisionLod = false;

        [Tooltip("Full detail up to this distance (metres).")]
        public float lodNearDistance = 12f;

        [Tooltip("Beyond this distance only the largest hulls stay enabled.")]
        public float lodFarDistance = 40f;

        [Range(1, 64)]
        [Tooltip("How many hulls stay enabled in the far band. The largest by volume are kept.")]
        public int lodFarHullCount = 6;

        [Tooltip("Also require the character to be on screen. Off means distance only.")]
        public bool lodRequireVisibility = false;

        [Tooltip("Reference used for distance. Empty means Camera.main.")]
        public Transform lodReference;

        [Range(0f, 5f)]
        [Tooltip("Hysteresis in metres, so a character on the boundary does not flicker between bands.")]
        public float lodHysteresis = 1.5f;

        // ------------------------------------------------------------------
        // Бюджеты
        // ------------------------------------------------------------------

        /// <summary>
        /// Потолок треугольников на одну оболочку при живом обновлении.
        ///
        /// Нужен потому, что пересборка идёт в рантайме: без потолка одна
        /// тяжёлая кость съедала бы весь бюджет. Запекание этим полем не
        /// ограничивается — там бюджет задаётся точностью.
        /// </summary>
        [Range(50, 5000)]
        [Tooltip("Maximum triangles per collider when rebuilding at runtime.")]
        public int maxColliderTriangles = 5000;

        // ------------------------------------------------------------------
        // Материалы и исключения
        // ------------------------------------------------------------------

        /// <summary>Соответствие материалов меша физическим материалам.</summary>
        [Tooltip("Per-source-material physics materials. Takes priority over the painted group material.")]
        public List<DycMaterialAssociation> materialAssociations = new List<DycMaterialAssociation>();

        /// <summary>
        /// Карта исключений: белый цвет (по выбранному каналу) — вершина
        /// исключена из коллайдеров.
        ///
        /// Дополняет кисть, а не заменяет: кисть размечает ГРАНИ, карта —
        /// ВЕРШИНЫ, и карту удобно готовить во внешнем редакторе или
        /// процедурно. Требуется UV у меша.
        /// </summary>
        [Tooltip("Vertex exclusion map. A white pixel in the selected channel excludes that vertex from colliders.")]
        public Texture2D exclusionMap;

        [Range(0, 3)]
        [Tooltip("Which channel of the exclusion map is read (0=R, 1=G, 2=B, 3=A).")]
        public int exclusionMapChannel;

        [Range(0f, 1f)]
        [Tooltip("Channel value above which a vertex is treated as excluded.")]
        public float exclusionMapThreshold = 0.5f;

        // ------------------------------------------------------------------
        // Скелет
        // ------------------------------------------------------------------

        /// <summary>
        /// Другой корень скелета для крепления оболочек.
        ///
        /// Перепривязка скелета: оболочки строятся по исходному
        /// мешу, но вешаются на кости ДРУГОГО скелета — так их можно увести
        /// на риг Puppet Master или на упрощённый риг столкновений. Пусто —
        /// крепление к своему скелету.
        /// </summary>
        [Tooltip("Attach the hulls to a different skeleton root (RetargetSkeleton). Leave empty to use the mesh's own skeleton.")]
        public Transform retargetRoot;

        public DycAdvancedSettings Clone()
        {
            var c = (DycAdvancedSettings)MemberwiseClone();
            c.materialAssociations = new List<DycMaterialAssociation>(materialAssociations);
            return c;
        }
    }
}
