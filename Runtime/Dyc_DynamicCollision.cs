using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Динамические коллизии: запечённые выпуклые оболочки собираются в коллайдеры во время выполнения, плюс встроенная маршрутизация событий по зонам.
    ///
    /// Проектные ограничения (сделано намеренно):
    ///   · Во время выполнения не выполняются вычисления сетки, построение выпуклых оболочек и пространственная кластеризация —— всё запекается в Editor.
    ///   · Выпуклая оболочка каждой кости запекается в локальном пространстве кости, форма никогда не пересчитывается, слежение только через Transform.
    ///     Кости жёсткие, поэтому математически это полностью эквивалентно "перезапеканию каждый кадр" для вершин с жёсткими весами,
    ///     и лишь у вершин со смешанными весами на стыках суставов есть ничтожное отклонение, покрываемое перекрытием соседних оболочек + seamOverlap.
    ///   · Для одного объекта допускается только один такой компонент: зонирование идёт по element (кость) × materialGroup (метка кисти),
    ///     два компонента дадут дублирующиеся коллайдеры и удвоенные события.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Neko/Dynamic Collision")]
    public class Dyc_DynamicCollision : MonoBehaviour
    {
        // ------------------------------------------------------------------ Конфигурация

        [SerializeField] DycMode _mode = DycMode.Skin;
        [SerializeField] SkinnedMeshRenderer _skin;
        [SerializeField] MeshFilter _mesh;

        /// <summary>
        /// Читать меши и с дочерних объектов, а не только с самого компонента.
        ///
        /// По умолчанию выключено. Раньше автопоиск брал ПЕРВЫЙ MeshFilter в
        /// детях, и на объекте с посторонними дочерними мешами запекалось не то,
        /// что видно: оболочка оказывалась от другого меша. Молчаливый выбор
        /// «какой-нибудь меш из детей» хуже, чем отсутствие выбора, поэтому
        /// теперь это ЯВНЫЙ переключатель.
        /// </summary>
        [SerializeField] bool _includeChildMeshes;

        /// <summary>
        /// Форма коллайдеров. Convex — оболочки (по умолчанию), Concave —
        /// невыпуклая поверхность (см. DycColliderShape).
        ///
        /// Хранится на компоненте, а не только в наборе: запекание обязано знать
        /// форму ЗАРАНЕЕ, потому что для невыпуклого пути оно строит не оболочки,
        /// а куски поверхности.
        /// </summary>
        [SerializeField] DycColliderShape _colliderShape = DycColliderShape.Convex;

        /// <summary>
        /// Точность НЕВЫПУКЛОЙ поверхности, 0..1. **1 — не упрощать вовсе.**
        ///
        /// Зачем одним слайдером, а не числом треугольников. «Сколько
        /// треугольников на кусок» — величина, которую невозможно выбрать
        /// осмысленно, не зная, сколько их в меше: 500 — это грубо для торса и
        /// точно для пальца. Слайдер отвечает на единственный вопрос, который
        /// пользователь может задать: «насколько мне важна точность формы».
        ///
        /// Крайнее значение — не «почти точно», а ИМЕННО точно: упрощение
        /// выключается целиком, и поверхность берётся из меша как есть.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float _concaveDetail = 0.5f;

        /// <summary>
        /// Кости, исключённые из зон и запекания.
        ///
        /// Исключается и само поддерево: список IK-целей, кончиков и хелперов
        /// удобнее задать одним родителем, чем перечислять каждую кость. У системных
        /// это excludedBones; здесь поведение то же, но с наследованием вниз,
        /// потому что кости в скелете иерархичны.
        /// </summary>
        [SerializeField] List<Transform> _excludedBones = new List<Transform>();

        /// <summary>
        /// SkinnedMeshRenderer'ы, исключённые из запекания.
        ///
        /// Нужно там, где персонаж собран из нескольких рендереров и часть из них
        /// коллайдеров не требует (например, отдельный рендерер волос или
        /// одежды). Исключение целых рендереров.
        /// </summary>
        [SerializeField] List<SkinnedMeshRenderer> _excludedSkins = new List<SkinnedMeshRenderer>();

        [SerializeField] DycPrecision _precision = DycPrecision.Normal;

        /// <summary>
        /// Настройки разложения на выпуклые части (вогнутые коллайдеры).
        ///
        /// Живут на компоненте, а не берутся из ядра при каждом запекании:
        /// значения по умолчанию ядра рассчитаны на «один объект за раз» и для
        /// персонажа означают минуты счёта, поэтому набор обязан быть
        /// настраиваемым и сохраняемым.
        /// </summary>
        [SerializeField] DycDecomposeSettings _decompose = DycDecomposeSettings.Default;

        /// <summary>Предел числа частей НА КОСТЬ. Прямо определяет число
        /// коллайдеров и стоимость широкой фазы.</summary>
        [SerializeField] int _maxHullsPerBone = 8;

        /// <summary>Ручные бюджеты по костям. Пусто — работает общий предел.</summary>
        [SerializeField] List<DycBoneBudget> _boneBudgets = new List<DycBoneBudget>();

        /// <summary>Ручные числа для DycPrecision.Custom: оболочек на зону.</summary>
        [SerializeField] int _customHullsPerPart = 1;

        /// <summary>Ручные числа для DycPrecision.Custom: треугольников на оболочку.</summary>
        [SerializeField] int _customTrisPerHull = 250;

        /// <summary>
        /// Порог веса кости для Custom.
        ///
        /// Это третий и самый коварный параметр. Он решает, какие вершины
        /// вообще считаются принадлежащими кости: вершина с весом ниже порога
        /// отбрасывается, и её кость получает НЕ всю поверхность, а кусок с
        /// дырами — выпуклая оболочка такого куска и выглядит как лепесток.
        /// Значение по умолчанию 0.25 подходит не всем скелетам.
        /// </summary>
        [SerializeField] float _customWeightThreshold = 0.25f;

        [SerializeField] Dyc_BakedSet _baked;
        [SerializeField] Dyc_PaintMask _paintMask;

        [SerializeField] List<DycElement> _elements = new List<DycElement>();
        [SerializeField] List<DycMaterialGroup> _groups = new List<DycMaterialGroup>();

        [SerializeField] DycColliderRole _role = DycColliderRole.Hitbox;
        [SerializeField] LayerMask _includeLayers;
        [SerializeField] bool _detectCollisions = true;
        [SerializeField] bool _detectTriggers;

        [SerializeField] bool _autoRigidbody = true;
        [SerializeField] DycSelfCollision _selfCollision = DycSelfCollision.Adjacent;
        [SerializeField] bool _autoMassFromDensity = true;
        [SerializeField] float _targetTotalMass = 0f;

        [SerializeField] DycTriggerMode _triggerMode = DycTriggerMode.Off;
        [SerializeField, Range(1, 16)] int _pollDivisor = 4;
        [SerializeField] float _pollSweepMargin = 0f;
        [SerializeField] DycLodMode _lodMode = DycLodMode.Off;
        [SerializeField] float _lodDistance = 60f;

        [SerializeField] DycEventHook _onEvent = new DycEventHook();
        [SerializeField] bool _debugLog;

        /// <summary>
        /// Продвинутые настройки. Отдельным объектом, чтобы обычный сценарий
        /// («добавил компонент, запекал, играешь») не тонул в опциях, нужных
        /// только для режима с обновлением в рантайме.
        /// </summary>
        [SerializeField] DycAdvancedSettings _advanced = new DycAdvancedSettings();

        /// <summary>Набор, собранный в рантайме (generateOnStart). Держим ссылку,
        /// иначе ScriptableObject соберёт GC и коллайдеры потеряют меши.</summary>
        Dyc_BakedSet _runtimeBaked;

        // ------------------------------------------------------------------ Состояние во время выполнения

        readonly Dictionary<Collider, int> _route = new Dictionary<Collider, int>(64);
        readonly List<GameObject> _created = new List<GameObject>(64);
        readonly List<Collider> _colliders = new List<Collider>(64);
        readonly List<Transform> _hullBone = new List<Transform>(64);
        readonly List<int> _hullElement = new List<int>(64);
        readonly List<int> _hullGroup = new List<int>(64);
        readonly List<Dyc_Relay> _relays = new List<Dyc_Relay>(8);

        List<Collider>[] _elementColliders;
        Dyc_TriggerPoll _poll;

        readonly HashSet<long> _dedupe = new HashSet<long>();
        int _dedupeFrame = -1;

        /// <summary>Коллайдер противника → зона последнего попадания. У CollisionExit массив contacts часто пуст,
        /// поэтому по нему событие выхода тоже привязывается к правильной зоне.</summary>
        readonly Dictionary<Collider, int> _lastElementByOther = new Dictionary<Collider, int>(16);

        int _lodFrame;
        bool _lodDisabled;

        /// <summary>Присылать события столкновений. Relay обязан это проверять:
        /// иначе выключатель в инспекторе ничего не выключает.</summary>
        public bool DetectCollisions => _detectCollisions;

        /// <summary>Присылать события триггеров. См. <see cref="DetectCollisions"/>.</summary>
        public bool DetectTriggers => _detectTriggers;

        public bool IsBuilt => _colliders.Count > 0;
        public int HullCount => _colliders.Count;

        /// <summary>Коллайдер оболочки по порядковому номеру. Нужен «живому»
        /// обновлению и внешним инструментам, которые хотят пройти по всем
        /// оболочкам, не заглядывая в приватные поля.</summary>
        public Collider HullCollider(int index)
        {
            if (index < 0 || index >= _colliders.Count) return null;
            return _colliders[index];
        }

        /// <summary>Номер оболочки в запечённом наборе для этого коллайдера.
        /// -1 — коллайдер не наш. Именно эта связь, а не порядок в списке,
        /// потому что часть оболочек при сборке может быть пропущена.</summary>
        public int HullIndexOf(Collider c)
        {
            int i;
            return (c != null && _route.TryGetValue(c, out i)) ? i : -1;
        }
        public int ElementCount => _elements != null ? _elements.Count : 0;
        public Dyc_BakedSet BakedSet => _baked;
        public Dyc_PaintMask PaintMask => _paintMask;
        public DycMode Mode => _mode;

        /// <summary>Продвинутые настройки. Не null: создаётся при объявлении,
        /// поэтому старые сцены без поля получают значения по умолчанию.</summary>
        public DycAdvancedSettings Advanced
        {
            get
            {
                if (_advanced == null) _advanced = new DycAdvancedSettings();
                return _advanced;
            }
        }

        public DycPrecision Precision => _precision;
        public DycDecomposeSettings DecomposeSettings => _decompose;
        public int MaxHullsPerBone => _maxHullsPerBone;
        public List<DycBoneBudget> BoneBudgets => _boneBudgets;

        /// <summary>
        /// Предел частей для конкретной кости.
        ///
        /// Сначала ищется ручной бюджет по пути, и только потом берётся общий.
        /// Так автоматика работает для всего скелета, а исключения задаются
        /// точечно — без необходимости расписывать все кости.
        /// </summary>
        public int BudgetFor(string bonePath)
        {
            if (_boneBudgets != null && !string.IsNullOrEmpty(bonePath))
            {
                for (int i = 0; i < _boneBudgets.Count; i++)
                {
                    var b = _boneBudgets[i];
                    if (b != null && b.bonePath == bonePath) return Mathf.Max(1, b.pieces);
                }
            }
            return Mathf.Max(1, _maxHullsPerBone);
        }
        public int CustomHullsPerPart => _customHullsPerPart;
        public int CustomTrisPerHull => _customTrisPerHull;
        public float CustomWeightThreshold => _customWeightThreshold;

        /// <summary>Развёрнутая точность с учётом ручных чисел. Единственная
        /// точка, где точность превращается в числа — чтобы запекание, окно и
        /// инспектор не разошлись в трактовке Custom.</summary>
        public DycDecomposeSettings EditDecompose { get => _decompose; set => _decompose = value; }
        public int EditMaxHullsPerBone { get => _maxHullsPerBone; set => _maxHullsPerBone = Mathf.Clamp(value, 1, 64); }
        public void EditorSetBoneBudgets(List<DycBoneBudget> budgets) => _boneBudgets = budgets ?? new List<DycBoneBudget>();
        public DycPrecisionInfo PrecisionInfo => DycPrecisionInfo.Resolve(_precision, _customHullsPerPart, _customTrisPerHull, _customWeightThreshold);
        public DycColliderRole Role => _role;
        public DycColliderShape ColliderShape => _colliderShape;

        // ------------------------------------------------------------------
        // Точность разложения: слайдер над размером вокселя
        // ------------------------------------------------------------------

        /// <summary>Самый грубый воксель, мм. Левое положение слайдера.</summary>
        public const float CoarseVoxelMm = 100f;

        /// <summary>
        /// Самый мелкий воксель, мм. Правое положение слайдера.
        ///
        /// 1 мм — это предел, за которым части уже повторяют поверхность, и
        /// стоимость разложения сравнивается со стоимостью НЕВЫПУКЛОЙ формы:
        /// дальше уточнять нечего, разница только в цене. Поэтому правый край
        /// слайдера — это и есть «дорого, как невыпуклая».
        /// </summary>
        public const float FineVoxelMm = 1f;

        /// <summary>
        /// Точность разложения, 0..1 — удобная ручка над размером вокселя.
        ///
        /// Хранится НЕ здесь, а в DycDecomposeSettings.voxelSizeMm. Два
        /// источника правды разошлись бы при первой же правке из окна эксперта:
        /// там размер вокселя редактируется напрямую. Слайдер и числовое поле —
        /// два вида ОДНОГО числа, поэтому они совпадают всегда, без синхронизации.
        /// </summary>
        public float DecomposeDetail
        {
            get { return Mathf.InverseLerp(CoarseVoxelMm, FineVoxelMm, _decompose.voxelSizeMm); }
            set { _decompose.voxelSizeMm = Mathf.Lerp(CoarseVoxelMm, FineVoxelMm, Mathf.Clamp01(value)); }
        }

        /// <summary>Размер вокселя в мм — то же число, что показывает поле под слайдером.</summary>
        public int DecomposeVoxelMm
        {
            get { return Mathf.Clamp(Mathf.RoundToInt(_decompose.voxelSizeMm), 1, 100); }
            set { _decompose.voxelSizeMm = Mathf.Clamp(value, 1, 100); }
        }

        /// <summary>Разложение вообще включено: ядро не «нативное».</summary>
        public bool DecomposeEnabled => _decompose.kernel != DycDecomposeKernel.Native;

        /// <summary>Точность невыпуклой поверхности, 0..1. 1 — не упрощать.</summary>
        public float ConcaveDetail => _concaveDetail;

        /// <summary>Упрощение выключено: поверхность берётся из меша как есть.</summary>
        public bool ConcaveExact => _concaveDetail >= 0.999f;

        /// <summary>
        /// Бюджет треугольников на кусок невыпуклой поверхности, выведенный из
        /// слайдера. int.MaxValue — «не упрощать».
        /// </summary>
        public int ConcaveTriangleBudget
        {
            get
            {
                if (ConcaveExact) return int.MaxValue;

                // 80 — грубо (видна огранка), 1200 — почти неотличимо от
                // исходной формы. Дальше смысла нет: следующий шаг уже
                // «не упрощать вовсе».
                return Mathf.RoundToInt(Mathf.Lerp(80f, 1200f, _concaveDetail));
            }
        }
        public List<Transform> ExcludedBones => _excludedBones;
        public List<SkinnedMeshRenderer> ExcludedSkins => _excludedSkins;

        /// <summary>
        /// Кость исключена, если она сама в списке или является потомком
        /// исключённой. Обход идёт вверх по иерархии, поэтому проверка стоит
        /// O(глубины), а не O(списка) на каждую кость скелета.
        /// </summary>
        public bool IsBoneExcluded(Transform bone)
        {
            if (bone == null || _excludedBones == null || _excludedBones.Count == 0) return false;

            Transform cur = bone;
            while (cur != null)
            {
                for (int i = 0; i < _excludedBones.Count; i++)
                    if (_excludedBones[i] == cur) return true;
                cur = cur.parent;
            }
            return false;
        }

        public bool IsSkinExcluded(SkinnedMeshRenderer skin)
        {
            if (skin == null || _excludedSkins == null) return false;
            for (int i = 0; i < _excludedSkins.Count; i++)
                if (_excludedSkins[i] == skin) return true;
            return false;
        }

        public DycSelfCollision SelfCollision => _selfCollision;
        public bool AutoMassFromDensity => _autoMassFromDensity;
        public float TargetTotalMass => _targetTotalMass;
        public bool AutoRigidbody => _autoRigidbody;
        public List<DycElement> Elements => _elements;
        public List<DycMaterialGroup> Groups => _groups;
        public DycEventHook EventHook => _onEvent;

#if UNITY_EDITOR
        // Позволяет Editor менять эти поля, не нарушая инкапсуляцию
        public DycMode EditMode { get => _mode; set => _mode = value; }
        public int EditCustomHullsPerPart { get => _customHullsPerPart; set => _customHullsPerPart = Mathf.Clamp(value, 1, 32); }
        public int EditCustomTrisPerHull { get => _customTrisPerHull; set => _customTrisPerHull = Mathf.Clamp(value, 18, 4000); }
        public float EditCustomWeightThreshold { get => _customWeightThreshold; set => _customWeightThreshold = Mathf.Clamp(value, 0f, 1f); }
        public DycPrecision EditPrecision { get => _precision; set => _precision = value; }
        public SkinnedMeshRenderer EditSkin { get => _skin; set => _skin = value; }
        public MeshFilter EditMesh { get => _mesh; set => _mesh = value; }
        public Dyc_BakedSet EditBaked { get => _baked; set => _baked = value; }
        public DycColliderRole EditRole { get => _role; set => _role = value; }
        public DycColliderShape EditColliderShape { get => _colliderShape; set => _colliderShape = value; }
        public float EditConcaveDetail { get => _concaveDetail; set => _concaveDetail = Mathf.Clamp01(value); }
        public float EditDecomposeDetail { get => DecomposeDetail; set => DecomposeDetail = value; }
        public int EditDecomposeVoxelMm { get => DecomposeVoxelMm; set => DecomposeVoxelMm = value; }
        public void EditorSetExcludedBones(List<Transform> bones) => _excludedBones = bones ?? new List<Transform>();
        public void EditorSetExcludedSkins(List<SkinnedMeshRenderer> skins) => _excludedSkins = skins ?? new List<SkinnedMeshRenderer>();
        public LayerMask EditIncludeLayers { get => _includeLayers; set => _includeLayers = value; }
        public bool EditDetectCollisions { get => _detectCollisions; set => _detectCollisions = value; }
        public bool EditDetectTriggers { get => _detectTriggers; set => _detectTriggers = value; }
        public DycTriggerMode EditTriggerMode { get => _triggerMode; set => _triggerMode = value; }
        public int EditPollDivisor { get => _pollDivisor; set => _pollDivisor = value; }
        public float EditPollSweepMargin { get => _pollSweepMargin; set => _pollSweepMargin = value; }
        public DycLodMode EditLodMode { get => _lodMode; set => _lodMode = value; }
        public float EditLodDistance { get => _lodDistance; set => _lodDistance = value; }
        public DycSelfCollision EditSelfCollisionMode { get => _selfCollision; set => _selfCollision = value; }
        public bool EditSelfCollisionEnabled { get => _selfCollision != DycSelfCollision.On; set => _selfCollision = value ? DycSelfCollision.Adjacent : DycSelfCollision.On; }
        public void EditorSetPaintMask(Dyc_PaintMask mask) => _paintMask = mask;
        public bool EditAutoMass { get => _autoMassFromDensity; set => _autoMassFromDensity = value; }
        public float EditTargetTotalMass { get => _targetTotalMass; set => _targetTotalMass = value; }
        public bool EditAutoRigidbody { get => _autoRigidbody; set => _autoRigidbody = value; }
        public bool EditDebugLog { get => _debugLog; set => _debugLog = value; }
        public SkinnedMeshRenderer SourceSkin => _skin;
        public MeshFilter SourceMesh => _mesh;
        public bool SourceIncludesChildren => _includeChildMeshes;
        public bool EditIncludeChildMeshes { get => _includeChildMeshes; set => _includeChildMeshes = value; }
#endif

        // ------------------------------------------------------------------ Жизненный цикл

        void Awake()
        {
            if (_baked != null && _baked.hulls.Count > 0)
            {
                Build();
            }
            else if (Advanced.generateOnStart)
            {
                // Обычное использование: компонент добавлен — коллайдеры уже
                // есть, запекание не обязательно. Строим по костям прямо из
                // меша; это грубее запечённого набора, зато без шага в редакторе.
                _runtimeBaked = Dyc_RuntimeBuild.Build(this);
                if (_runtimeBaked != null && _runtimeBaked.hulls.Count > 0)
                    ApplyBaked(_runtimeBaked, true);
                else if (_debugLog)
                    Debug.LogWarning("[NDC] generateOnStart: оболочки построить не удалось " +
                                     "(нужен SkinnedMeshRenderer с boneWeights или MeshFilter).", this);
            }

            if (Advanced.liveUpdate && IsBuilt)
                Dyc_LiveUpdate.Attach(this);

            if (Advanced.collisionLod && IsBuilt)
                Dyc_CollisionLod.Attach(this);
        }

        /// <summary>Полуавтоматика: при добавлении компонента источник находится сам, разработчику ничего заполнять не нужно.</summary>
        void Reset()
        {
            if (_skin == null) _skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_mesh == null) _mesh = GetComponentInChildren<MeshFilter>();

            _mode = _skin != null ? DycMode.Skin : DycMode.Mesh;

            if (_skin == null && _mesh == null)
            {
                // Если компонент висит на дочернем объекте, поискать выше по иерархии
                _skin = GetComponentInParent<SkinnedMeshRenderer>();
                if (_skin != null) _mode = DycMode.Skin;
            }

            if (_elements == null) _elements = new List<DycElement>();
            if (_groups == null) _groups = new List<DycMaterialGroup>();
            if (_groups.Count == 0)
                _groups.Add(new DycMaterialGroup { name = "Default", density = 1000f });
        }

        void OnDestroy()
        {
            ClearRuntime();
        }

        void Update()
        {
            if (_poll != null && _triggerMode == DycTriggerMode.Poll)
                _poll.Tick(_pollSweepMargin, ResolveMask());

            if (_lodMode != DycLodMode.Off && _colliders.Count > 0 && (++_lodFrame & 3) == 0)
                TickLod();
        }

        // ------------------------------------------------------------------ Сборка

        /// <summary>Удаляет старые коллайдеры и собирает заново по текущему _baked. Предпросмотр в редакторе идёт тем же путём.</summary>
        public void Build()
        {
            ClearRuntime();

            if (_baked == null || _baked.hulls.Count == 0)
            {
                if (_debugLog) Debug.LogWarning("[DYC] Нет результата запекания, сначала выполните запекание.", this);
                return;
            }

            if (_autoRigidbody) EnsureRigidbody();

            var hulls = _baked.hulls;
            _elementColliders = new List<Collider>[_elements.Count];

            for (int i = 0; i < hulls.Count; i++)
            {
                DycBakedHull h = hulls[i];
                if (h == null || h.mesh == null) continue;

                Transform parent = ResolveAttach(h);
                if (parent == null)
                {
                    Debug.LogWarning($"[DYC] Точка крепления '{h.bonePath}' не найдена, оболочка пропущена.", this);
                    continue;
                }

                var go = new GameObject("DYC_Hull_" + i);
                go.hideFlags = HideFlags.HideInHierarchy;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = h.mesh;

                // Форма берётся С КАЖДОЙ ЧАСТИ, а не с компонента: набор,
                // запечённый до появления невыпуклого режима, обязан остаться
                // выпуклым, даже если на компоненте уже стоит Concave.
                mc.convex = !h.nonConvex;
                mc.sharedMaterial = MaterialOf(h);

                _created.Add(go);
                _colliders.Add(mc);
                _route[mc] = i;
                _hullBone.Add(parent);
                _hullElement.Add(h.elementIndex);
                _hullGroup.Add(h.groupIndex);

                if (h.elementIndex >= 0 && h.elementIndex < _elementColliders.Length)
                {
                    if (_elementColliders[h.elementIndex] == null)
                        _elementColliders[h.elementIndex] = new List<Collider>(4);
                    _elementColliders[h.elementIndex].Add(mc);
                }
            }

            ApplyRole();
            SetupRelays();
            ApplySelfCollision();
            ApplyMass();

            if (_triggerMode == DycTriggerMode.Poll && _elementColliders != null)
            {
                _poll = new Dyc_TriggerPoll(this, _elementColliders);
                _poll.Configure(Mathf.Max(1, Mathf.CeilToInt(_elementColliders.Length / (float)Mathf.Max(1, _pollDivisor))));
            }

            if (_debugLog)
                Debug.Log($"[DYC] Сборка завершена: {_colliders.Count} оболочек / {CountElements()} зон / {_relays.Count} relay.", this);
        }

        int CountElements()
        {
            if (_elementColliders == null) return 0;
            int n = 0;
            for (int i = 0; i < _elementColliders.Length; i++)
                if (_elementColliders[i] != null && _elementColliders[i].Count > 0) n++;
            return n;
        }

        public void ClearRuntime()
        {
            _route.Clear();
            _colliders.Clear();
            _hullBone.Clear();
            _hullElement.Clear();
            _hullGroup.Clear();
            _elementColliders = null;
            _poll = null;
            _dedupe.Clear();
            _dedupeFrame = -1;
            _lastElementByOther.Clear();
            _lodDisabled = false;

            for (int i = 0; i < _relays.Count; i++)
                if (_relays[i] != null) DestroyObject(_relays[i].gameObject);
            _relays.Clear();

            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) DestroyObject(_created[i]);
            _created.Clear();
        }

        void DestroyObject(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        Transform ResolveAttach(DycBakedHull h)
        {
            // Мягкий режим: оболочка крепится не к кости, а к кадру деформации,
            // который пишет Dyc_DeformDriver. Кадров ровно столько, сколько
            // кластеров. Если источника деформации нет, кадры остаются
            // единичными и оболочки просто едут вместе с объектом — это и есть
            // обещанная деградация мягкого режима без второго плагина.
            if (h.clusterIndex >= 0)
            {
                Transform frame = Dyc_DeformDriver.FrameOf(this, h.clusterIndex);
                if (frame != null) return frame;
            }

            if (string.IsNullOrEmpty(h.bonePath)) return transform;
            if (h.bonePath == ".") return transform;

            // Крепление к чужому скелету (RetargetSkeleton): путь ищется в
            // корне переназначения, а не в своём. Пусто — ищем у себя, как
            // раньше, поэтому старые наборы работают без изменений.
            var root = Advanced.retargetRoot;
            if (root != null)
            {
                var retargeted = root.Find(h.bonePath);
                if (retargeted != null) return retargeted;
            }

            return transform.Find(h.bonePath);
        }

        /// <summary>
        /// Физический материал оболочки.
        ///
        /// Порядок разрешения — от частного к общему:
        ///   1. материал исходного подмеша (карта материалов и переопределение
        ///      на кости разрешены ещё при запекании);
        ///   2. материал зоны, к которой оболочка отнесена кистью.
        /// </summary>
        PhysicMaterial MaterialOf(DycBakedHull hull)
        {
            if (hull == null) return null;

            var set = _baked;
            if (set != null && set.sourceMaterials != null &&
                hull.materialIndex >= 0 && hull.materialIndex < set.sourceMaterials.Count)
            {
                var m = set.sourceMaterials[hull.materialIndex];
                if (m != null) return m;
            }

            return MaterialOf(hull.groupIndex);
        }

        PhysicMaterial MaterialOf(int groupIndex)
        {
            if (groupIndex < 0) return null;

            // Материал, разрешённый при запекании (карта материалов + кости),
            // старше списка групп: он не зависит от того, что сейчас в
            // компоненте, и переживает переименование групп.
            if (_baked != null && _baked.groupMaterials != null &&
                groupIndex < _baked.groupMaterials.Count)
            {
                var bakedMat = _baked.groupMaterials[groupIndex];
                if (bakedMat != null) return bakedMat;
            }

            if (_groups == null || groupIndex >= _groups.Count) return null;
            return _groups[groupIndex] != null ? _groups[groupIndex].material : null;
        }

        // ------------------------------------------------------------------ Роль / фильтрация

        LayerMask ResolveMask()
        {
            return _includeLayers.value != 0 ? _includeLayers : (LayerMask)~0;
        }

        void ApplyRole()
        {
            int include = _includeLayers.value;
            bool restrict = include != 0;

            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;

                // Роль Trigger — это NDC-режим «только события». Продвинутая
                // галочка isTrigger — обычный Collider.isTrigger, независимо
                // от роли: так компонент ведёт себя как системный коллайдер.
                c.isTrigger = _role == DycColliderRole.Trigger || Advanced.isTrigger;

                if (_role == DycColliderRole.Physics) continue;

                // Hitbox / Trigger: взаимодействует только с указанными слоями, не участвует в столкновениях с окружением/персонажами/предметами
                if (restrict)
                {
                    c.includeLayers = include;
                    c.excludeLayers = ~include;
                }
            }
        }

        // ------------------------------------------------------------------ Rigidbody / relay

        void EnsureRigidbody()
        {
            if (GetComponentInParent<Rigidbody>() != null) return;
            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        void SetupRelays()
        {
            // Колбэки столкновений приходят только GameObject с Rigidbody, поэтому на каждого владельца Rigidbody создаётся один relay.
            var owners = new HashSet<Rigidbody>();

            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;

                Rigidbody rb = c.attachedRigidbody;
                if (rb == null) rb = c.GetComponentInParent<Rigidbody>();
                if (rb != null) owners.Add(rb);
            }

            foreach (var rb in owners)
            {
                var relay = rb.GetComponent<Dyc_Relay>();
                if (relay == null) relay = rb.gameObject.AddComponent<Dyc_Relay>();
                relay.owner = this;
                _relays.Add(relay);
            }

            if (_relays.Count == 0 && _debugLog)
                Debug.LogWarning("[DYC] Нет ни одного Rigidbody, события столкновений не будут срабатывать. Включите autoRigidbody или добавьте родителю kinematic Rigidbody.", this);
        }

        // ------------------------------------------------------------------ Самоколлизии

        void ApplySelfCollision()
        {
            if (_selfCollision == DycSelfCollision.On) return;
            if (_colliders.Count > 200)
            {
                Debug.LogWarning($"[DYC] Коллайдеров: {_colliders.Count}, настройка самоколлизий пропущена, чтобы Awake не тормозил.", this);
                return;
            }

            for (int i = 0; i < _colliders.Count; i++)
            {
                for (int j = i + 1; j < _colliders.Count; j++)
                {
                    if (_selfCollision == DycSelfCollision.Adjacent && !IsAdjacent(i, j)) continue;
                    Physics.IgnoreCollision(_colliders[i], _colliders[j], true);
                }
            }
        }

        bool IsAdjacent(int a, int b)
        {
            int ea = _hullElement[a], eb = _hullElement[b];
            if (ea == eb) return true;

            Transform ta = _hullBone[a], tb = _hullBone[b];
            if (ta == null || tb == null) return false;
            return ta.IsChildOf(tb) || tb.IsChildOf(ta);
        }

        // ------------------------------------------------------------------ Масса

        void ApplyMass()
        {
            if (!_autoMassFromDensity || _baked == null) return;

            var hulls = _baked.hulls;
            var masses = new Dictionary<Rigidbody, float>();

            for (int i = 0; i < _colliders.Count && i < hulls.Count; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;

                Rigidbody rb = c.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;

                float density = 1000f;
                int g = _hullGroup[i];
                if (_groups != null && g >= 0 && g < _groups.Count && _groups[g] != null)
                    density = Mathf.Max(1f, _groups[g].density);

                float m = Mathf.Max(1e-5f, hulls[i].volume) * density;
                masses.TryGetValue(rb, out float cur);
                masses[rb] = cur + m;
            }

            if (masses.Count == 0) return;

            float total = 0f;
            foreach (var kv in masses) total += kv.Value;

            float scale = 1f;
            if (_targetTotalMass > 0f && total > 0f) scale = _targetTotalMass / total;

            foreach (var kv in masses)
            {
                var rb = kv.Key;
                if (rb == null) continue;
                rb.mass = Mathf.Max(1e-4f, kv.Value * scale);
            }

            if (_debugLog)
                Debug.Log($"[DYC] Масса рассчитана по плотности: итого {total * scale:F2} кг / {masses.Count} твёрдых тел.", this);
        }

        // ------------------------------------------------------------------ LOD

        void TickLod()
        {
            var cam = Camera.main;
            if (cam == null) return;

            float d = (cam.transform.position - transform.position).sqrMagnitude;
            bool far = d > _lodDistance * _lodDistance;

            if (_lodMode == DycLodMode.Disable)
            {
                if (far == _lodDisabled) return;
                _lodDisabled = far;
                for (int i = 0; i < _colliders.Count; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = !far;
                return;
            }

            if (_lodMode == DycLodMode.Reduce)
            {
                // На расстоянии оставляется только самая крупная оболочка каждого element
                for (int i = 0; i < _colliders.Count; i++)
                {
                    Collider c = _colliders[i];
                    if (c == null) continue;
                    c.enabled = !far || IsBiggestOfElement(i);
                }
            }
        }

        bool IsBiggestOfElement(int index)
        {
            if (_baked == null || index >= _baked.hulls.Count) return true;
            int e = _hullElement[index];
            float v = _baked.hulls[index].volume;
            for (int i = 0; i < _colliders.Count; i++)
            {
                if (i == index || _hullElement[i] != e) continue;
                if (i < _baked.hulls.Count && _baked.hulls[i].volume > v) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ Маршрутизация событий

        public bool IsOwnCollider(Collider c)
        {
            return c != null && _route.ContainsKey(c);
        }

        /// <summary>Единая точка входа для Relay и опроса Trigger.</summary>
        public void HandleContact(DycEventKind kind, Collider self, Collider other,
            Vector3 point, Vector3 normal, float speed, int forcedElement = -1)
        {
            if (other == null) return;
            if (IsOwnCollider(other)) return;

            int element, group;
            Transform bone;

            if (forcedElement >= 0)
            {
                element = forcedElement;
                group = -1;
                bone = BoneOfElement(element);
            }
            else if (self != null && _route.TryGetValue(self, out int hull))
            {
                element = _hullElement[hull];
                group = _hullGroup[hull];
                bone = _hullBone[hull];
            }
            else if (_lastElementByOther.TryGetValue(other, out int cached))
            {
                // Когда свой коллайдер недоступен (у события выхода contacts часто пуст), используется предыдущая привязка
                element = cached;
                group = -1;
                bone = BoneOfElement(element);
            }
            else
            {
                return;
            }

            if (_lastElementByOther.Count > 256) _lastElementByOther.Clear();
            _lastElementByOther[other] = element;

            if (kind == DycEventKind.CollisionEnter)
            {
                if (_dedupeFrame != Time.frameCount) { _dedupeFrame = Time.frameCount; _dedupe.Clear(); }
                long key = ((long)element << 32) | (uint)other.GetInstanceID();
                if (!_dedupe.Add(key)) return;
            }

            DycElement el = ElementAt(element);
            DycMaterialGroup gr = GroupAt(group);

            var e = new DycEvent
            {
                kind = kind,
                elementIndex = element,
                elementName = el != null ? el.DisplayName : null,
                bone = bone,
                groupIndex = group,
                groupName = gr != null ? gr.DisplayName : null,
                material = gr != null ? gr.material : null,
                selfCollider = self,
                otherCollider = other,
                otherBody = other.attachedRigidbody,
                otherRoot = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject,
                point = point,
                normal = normal,
                relativeSpeed = speed,
                damageMultiplier = (el != null ? Mathf.Max(0f, el.damageMultiplier) : 1f)
                                 * (gr != null ? Mathf.Max(0f, gr.damageMultiplier) : 1f),
                eventName = el != null ? el.eventName : null,
                time = Time.time
            };

            if (_onEvent != null) _onEvent.Invoke(e);

            Dyc_Api.RaiseGlobal(e);

            if (Dyc_Api.GlobalFilter != null && Dyc_Api.GlobalFilter(e)) return;

            Dyc_Events.Dispatch(e.eventName, e);
            if (gr != null && !string.IsNullOrEmpty(gr.eventName) && gr.eventName != e.eventName)
                Dyc_Events.Dispatch(gr.eventName, e);
        }

        /// <summary>Заменяет набор запечённых данных и пересобирает. Версия, доступная во время выполнения (EditorApplyBaked в редакторе вызывает её).</summary>
        public void ApplyBaked(Dyc_BakedSet set, bool rebuild)
        {
            _baked = set;

            // Набор другой — кадры и позы покоя другого набора. Старые надо
            // снять, иначе мягкие кластеры останутся на местах прежнего меша.
            var driver = GetComponent<Dyc_DeformDriver>();
            if (driver != null) driver.ResetFrames();

            if (rebuild) Build();
        }

        /// <summary>
        /// Привести клон в рабочее состояние после Instantiate.
        ///
        /// Зачем это нужно, если компонент копируется целиком. Копируются и
        /// рантайм-дети: кадры деформации и оболочки. У клона они указывают на
        /// объекты оригинала — или наоборот, в зависимости от порядка. Плюс
        /// рантайм-набор (generateOnStart) не сериализуется и у клона пуст.
        /// Поэтому клон чистится и собирается заново по СВОИМ данным.
        ///
        /// Параметр оставлен для совместимости сигнатуры
        /// сигнатуры и может быть null.
        /// </summary>
        public void FixInstantiated(Dyc_DynamicCollision source)
        {
            ClearRuntime();

            var driver = GetComponent<Dyc_DeformDriver>();
            if (driver != null) driver.ResetFrames();

            _runtimeBaked = null;

            if (_baked != null && _baked.hulls.Count > 0)
            {
                Build();
                return;
            }

            if (Advanced.generateOnStart)
            {
                _runtimeBaked = Dyc_RuntimeBuild.Build(this);
                if (_runtimeBaked != null && _runtimeBaked.hulls.Count > 0)
                    ApplyBaked(_runtimeBaked, true);
            }
        }

        /// <summary>Временно включает или отключает все коллайдеры.</summary>
        public void SetCollidersEnabled(bool enabled)
        {
            for (int i = 0; i < _colliders.Count; i++)
                if (_colliders[i] != null) _colliders[i].enabled = enabled;
        }

        DycElement ElementAt(int i)
        {
            if (_elements == null || i < 0 || i >= _elements.Count) return null;
            return _elements[i];
        }

        DycMaterialGroup GroupAt(int i)
        {
            if (_groups == null || i < 0 || i >= _groups.Count) return null;
            return _groups[i];
        }

        Transform BoneOfElement(int i)
        {
            var el = ElementAt(i);
            if (el != null && el.bone != null) return el.bone;
            return transform;
        }

        // ------------------------------------------------------------------ Поддержка Editor

#if UNITY_EDITOR
        /// <summary>Для предпросмотра/обновления в редакторе: сразу заменяет набор запечённых данных и пересобирает.</summary>
        public void EditorApplyBaked(Dyc_BakedSet set, bool rebuild)
        {
            ApplyBaked(set, rebuild);
        }
#endif
    }
}
