using System;
using System.Reflection;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Контракт источника деформации: кто-то снаружи умеет посчитать, как
    /// повёрнут и где стоит каждый кластер мягкого тела прямо сейчас.
    ///
    /// Это ВСЯ поверхность стыка между NDC и решателем мягкого тела (NDSC).
    /// Типы здесь только из BCL — ни Matrix4x4[], ни int, ни bool больше
    /// ничего не пересекает границу. Причина не в красоте: если бы контракт
    /// тянул за собой типы решателя, NDC перестал бы собираться без него, и
    /// «два независимых плагина» превратились бы в один большой.
    ///
    /// Реализация обязана быть обычным классом с конструктором без параметров:
    /// MonoBehaviour здесь не годится, потому что создаётся он рефлексией,
    /// вне сцены. Обновлять его должен тот, кто владеет данными (решатель),
    /// а не Unity.
    /// </summary>
    public interface Dyc_IDeformSource
    {
        /// <summary>Готов ли источник отдавать кадры. false — мягкие зоны ведут себя как жёсткие.</summary>
        bool Ready { get; }

        /// <summary>Сколько кадров. ОБЯЗАНО совпадать с числом кластеров при запекании.</summary>
        int ClusterCount { get; }

        /// <summary>Отпечаток топологии. Изменился — карта контактов и кластеры устарели, нужна перезапечка.</summary>
        int TopologyStamp { get; }

        /// <summary>Записать кадры в МИРОВЫХ координатах. Длина массива — не меньше ClusterCount.</summary>
        void GetFrames(Matrix4x4[] frames);
    }

    /// <summary>
    /// Поиск установленного решателя мягкого тела.
    ///
    /// Приём тот же, что у NSG для ProgramNeko и языковых пакетов: ядро не
    /// ссылается на необязательный плагин, а находит его рефлексией. Но здесь
    /// есть тонкость, которой нет в NSG.
    ///
    /// NDSC не может реализовать <see cref="Dyc_IDeformSource"/> по имени:
    /// чтобы «NDSC работал без NDC», он не ссылается на сборку NDC вообще, а
    /// значит не видит этот интерфейс. Поэтому соответствие проверяется
    /// СТРУКТУРНО: ищется класс, у которого есть ровно эти четыре члена
    /// (Ready, ClusterCount, TopologyStamp, GetFrames), и он заворачивается в
    /// адаптер с кэшированными делегатами.
    ///
    /// Отсюда правило: после первого поиска в горячем пути НЕТ рефлексии —
    /// только четыре делегата. Ошибка соответствия не молчит: она попадает
    /// в <see cref="LastDiagnostic"/> и показывается в окне.
    /// </summary>
    public static class Dyc_DeformRegistry
    {
        /// <summary>Имя метода контракта. Используется и в диагностике, и в поиске.</summary>
        public const string FrameMethod = "GetFrames";

        static Dyc_IDeformSource _source;
        static bool _scanned;

        /// <summary>Человеческое объяснение, почему источник не найден. null — всё в порядке.</summary>
        public static string LastDiagnostic { get; private set; }

        /// <summary>Установлен ли решатель мягкого тела.</summary>
        public static bool Installed
        {
            get { Scan(); return _source != null; }
        }

        /// <summary>Установленный источник или null. Без него мягкие зоны статичны.</summary>
        public static Dyc_IDeformSource Source
        {
            get { Scan(); return _source; }
        }

        /// <summary>Явная регистрация: для того, у кого есть ссылка на сборку NDC.
        /// Рефлекторный поиск при этом больше не нужен.</summary>
        public static void Register(Dyc_IDeformSource source)
        {
            _source = source;
            _scanned = true;
            LastDiagnostic = source == null ? "источник снят" : null;
        }

        /// <summary>Сброс кэша: после установки или удаления NDSC.</summary>
        public static void Rediscover()
        {
            _source = null;
            _scanned = false;
            LastDiagnostic = null;
        }

        // ------------------------------------------------------------------ поиск

        static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch
                {
                    continue;
                }
                if (types == null) continue;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (typeof(MonoBehaviour).IsAssignableFrom(t)) continue;

                    var wrapped = Wrap(t);
                    if (wrapped == null) continue;

                    _source = wrapped;
                    return;
                }
            }

            if (LastDiagnostic == null)
                LastDiagnostic = "решатель мягкого тела не найден: нет класса с " +
                                 "Ready / ClusterCount / TopologyStamp / " + FrameMethod + "(Matrix4x4[])";
        }

        /// <summary>Структурное сопоставление: только имена и сигнатуры, без ссылок на типы.</summary>
        static Dyc_IDeformSource Wrap(Type type)
        {
            // Структурное совпадение — это ещё не повод выполнять чужой код.
            // Публичный тип с публичным конструктором без параметров: ровно то,
            // что обещает контракт. Всё остальное пропускаем, не создавая.
            if (!type.IsPublic) return null;
            if (type.GetConstructor(Type.EmptyTypes) == null) return null;

            var ready = type.GetProperty("Ready", BindingFlags.Public | BindingFlags.Instance);
            var count = type.GetProperty("ClusterCount", BindingFlags.Public | BindingFlags.Instance);
            var stamp = type.GetProperty("TopologyStamp", BindingFlags.Public | BindingFlags.Instance);
            var frames = type.GetMethod(FrameMethod, BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(Matrix4x4[]) }, null);

            if (ready == null || count == null || stamp == null || frames == null) return null;
            if (ready.PropertyType != typeof(bool)) return null;
            if (count.PropertyType != typeof(int) || stamp.PropertyType != typeof(int)) return null;
            if (frames.ReturnType != typeof(void)) return null;

            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                LastDiagnostic = type.FullName + ": не удалось создать экземпляр (" + ex.GetType().Name + ")";
                return null;
            }
            if (instance == null) return null;

            try
            {
                // Делегаты вместо MethodInfo.Invoke: Invoke упаковывает аргументы
                // и стоит в разы дороже, а вызывается это каждый кадр.
                return new ReflectedSource(
                    type.FullName,
                    (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), instance, ready.GetGetMethod()),
                    (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), instance, count.GetGetMethod()),
                    (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), instance, stamp.GetGetMethod()),
                    (Action<Matrix4x4[]>)Delegate.CreateDelegate(typeof(Action<Matrix4x4[]>), instance, frames));
            }
            catch (Exception ex)
            {
                LastDiagnostic = type.FullName + ": контракт не совпал по сигнатуре (" + ex.GetType().Name + ")";
                return null;
            }
        }

        /// <summary>Адаптер найденного класса. Ни одного MethodInfo в горячем пути.</summary>
        sealed class ReflectedSource : Dyc_IDeformSource
        {
            readonly string _name;
            readonly Func<bool> _ready;
            readonly Func<int> _count;
            readonly Func<int> _stamp;
            readonly Action<Matrix4x4[]> _frames;

            public ReflectedSource(string name, Func<bool> ready, Func<int> count,
                Func<int> stamp, Action<Matrix4x4[]> frames)
            {
                _name = name;
                _ready = ready;
                _count = count;
                _stamp = stamp;
                _frames = frames;
            }

            public bool Ready { get { return _ready(); } }
            public int ClusterCount { get { return _count(); } }
            public int TopologyStamp { get { return _stamp(); } }
            public void GetFrames(Matrix4x4[] frames) { _frames(frames); }

            public override string ToString() { return _name; }
        }
    }

    /// <summary>
    /// Двигатель кадров: пишет трансформы, к которым крепятся оболочки мягких
    /// зон. Это единственное, что NDC делает с мягким телом сам.
    ///
    /// Как это стыкуется с уже готовым кодом: оболочка крепится туда, откуда
    /// возвращает ResolveAttach. Для костей это Transform кости, для мягкого
    /// режима — кадр отсюда. Дальше всё как раньше: материал группы, события,
    /// покрытие, авто-масса — ни одна из этих подсистем не знает, что кадр
    /// кто-то пишет каждый кадр.
    ///
    /// Порядок выполнения. PhysX читает трансформы в начале шага, поэтому
    /// кадры обязаны быть записаны ДО него. Решатель ходит в LateUpdate,
    /// значит наш порядок — заведомо позже всех. Точное число не важно, важно
    /// что оно большое; при смене порядка у решателя это единственное место,
    /// которое надо поправить.
    ///
    /// Без установленного решателя кадры остаются единичными, и мягкая зона
    /// вырождается в набор жёстких кластеров, которые едут вместе с объектом.
    /// Это не ошибка, а осмысленная деградация: система столкновений работает,
    /// просто не следует за деформацией.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public class Dyc_DeformDriver : MonoBehaviour
    {
        public const int ExecutionOrder = 10000;

        public Dyc_DynamicCollision owner;

        public bool pollSource = true;

        Transform[] _frames;
        Matrix4x4[] _buffer;
        Dyc_IDeformSource _source;
        int _applied = -1;
        int _stamp;
        bool _mismatch;

        /// <summary>Сколько кадров реально запрошено. Массив может быть длиннее
        /// (растёт степенями двойки), но лишние объекты создавать незачем.</summary>
        int _needed;

        /// <summary>Номер кадра, в котором кадры пришли ИЗВНЕ (Push от решателя).
        /// Нужен, чтобы не перетереть адресную запись глобальным опросом.</summary>
        int _lastExternalApply = -1;

        /// <summary>Поза покоя каждого кадра: смещение кластера в пространстве
        /// владельца. Без источника кадры остаются здесь, и оболочки совпадают
        /// с настоящей формой меша.</summary>
        Vector3[] _rest;

        /// <summary>Сколько кадров записано в прошлый раз. -1 — ни разу.</summary>
        public int AppliedFrames { get { return _applied; } }

        /// <summary>Число кадров разошлось с источником: карта и кластеры устарели.</summary>
        public bool ClusterMismatch { get { return _mismatch; } }

        /// <summary>Отпечаток топологии, полученный от источника.</summary>
        public int TopologyStamp { get { return _stamp; } }

        // ------------------------------------------------------------------ доступ

        /// <summary>Кадр кластера. Создаётся по требованию: оболочка спрашивает
        /// точку крепления при сборке, а не раньше.</summary>
        public static Transform FrameOf(Dyc_DynamicCollision owner, int clusterIndex)
        {
            if (owner == null || clusterIndex < 0) return null;
            var driver = Ensure(owner);
            return driver != null ? driver.Frame(clusterIndex) : null;
        }

        /// <summary>Двигатель владельца: на компоненте или создаётся.</summary>
        public static Dyc_DeformDriver Ensure(Dyc_DynamicCollision owner)
        {
            if (owner == null) return null;

            var driver = owner.GetComponent<Dyc_DeformDriver>();
            if (driver != null) return driver;

            // Только во время работы или превью в редакторе: AddComponent в
            // момент импорта ассета недопустим.
            if (!Application.isPlaying && !owner.gameObject.scene.IsValid()) return null;

            driver = owner.gameObject.AddComponent<Dyc_DeformDriver>();
            driver.owner = owner;
            return driver;
        }

        public Transform Frame(int index)
        {
            if (index < 0) return null;
            EnsureFrames(index + 1);
            return _frames[index];
        }

        void EnsureFrames(int count)
        {
            // Набор вырос — таблица поз покоя пересобирается: она строится под
            // конкретное число кластеров, и оставлять её короткой значило бы
            // ставить новые кадры в ноль.
            if (count > _needed)
            {
                _needed = count;
                _rest = null;
            }

            if (_frames == null)
            {
                _frames = new Transform[Mathf.Max(4, count)];
                _buffer = new Matrix4x4[_frames.Length];
            }
            else if (count > _frames.Length)
            {
                int next = Mathf.NextPowerOfTwo(count);
                Array.Resize(ref _frames, next);
                Array.Resize(ref _buffer, next);
            }

            CreateFrames();
        }

        /// <summary>
        /// Создаёт недостающие кадры-трансформы.
        ///
        /// Без этого мягкий режим был мёртв целиком: Frame() всегда возвращал
        /// null, ResolveAttach падал на кость/корень, а Apply() пропускал каждый
        /// кадр — и собственный источник, и Push от решателя уходили в никуда.
        ///
        /// Кадры — обычные дочерние объекты владельца: решатель пишет в них
        /// мировые матрицы, а оболочки крепятся к ним как к костям.
        /// </summary>
        void CreateFrames()
        {
            if (_frames == null || _needed <= 0) return;

            if (_rest == null) _rest = BuildRestTable();

            Transform parent = owner != null ? owner.transform : transform;

            for (int i = 0; i < _needed && i < _frames.Length; i++)
            {
                if (_frames[i] != null) continue;

                var go = new GameObject("DYC_Frame_" + i);
                go.hideFlags = HideFlags.HideInHierarchy;
                go.transform.SetParent(parent, false);

                // Кадр встаёт в позу покоя СВОЕГО кластера. Раньше все кадры
                // создавались в начале координат владельца, и без источника
                // деформации оболочки мягкого тела собирались в одну точку.
                Vector3 rest = i < _rest.Length ? _rest[i] : Vector3.zero;
                go.transform.localPosition = rest;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                _frames[i] = go.transform;
            }
        }

        /// <summary>Смещение покоя по номеру кластера. Строится один раз: обход
        /// набора на каждый кадр был бы квадратичным.</summary>
        Vector3[] BuildRestTable()
        {
            var table = new Vector3[_needed];
            var set = owner != null ? owner.BakedSet : null;
            if (set == null || set.hulls == null) return table;

            for (int i = 0; i < set.hulls.Count; i++)
            {
                var h = set.hulls[i];
                if (h == null || h.clusterIndex < 0 || h.clusterIndex >= table.Length) continue;
                table[h.clusterIndex] = h.clusterRest;
            }
            return table;
        }

        // ------------------------------------------------------------------ запись кадров

        void LateUpdate()
        {
            if (!pollSource) return;

            // Адресная запись (Push от решателя) приоритетнее глобального
            // опроса. Иначе на кадре, где решатель уже положил ПРАВИЛЬНЫЕ кадры
            // своего тела, глобальный источник перетёр бы их кадрами другого
            // тела — а при нескольких телах это разные тела.
            if (_lastExternalApply >= 0 && Time.frameCount - _lastExternalApply <= 1) return;

            if (_source == null) _source = Dyc_DeformRegistry.Source;

            // Источник — решатель мягкого тела (NDSC и подобные). Нет источника
            // — кадры остаются там, где их поставил CreateFrames, то есть в позе
            // покоя кластеров. Это и есть «без решателя считается реальная форма
            // меша, но без динамики»: оболочки совпадают с настоящей геометрией
            // и едут вместе с объектом.
            if (_source != null && _source.Ready) ApplyFromSource(_source);
        }

        /// <summary>Опрос источника: путь для того, у кого нет ссылки на NDC,
        /// но кто умеет писать кадры сам (и тогда pollSource выключают).</summary>
        public int ApplyFromSource(Dyc_IDeformSource source)
        {
            if (source == null) return 0;
            if (!source.Ready) return 0;

            int count = source.ClusterCount;
            if (count <= 0) return 0;

            EnsureFrames(count);
            if (_buffer.Length < count) _buffer = new Matrix4x4[count];

            source.GetFrames(_buffer);
            _stamp = source.TopologyStamp;
            return WriteFrames(_buffer, count);
        }

        /// <summary>
        /// Записать кадры напрямую. Вызывается решателем сразу после шага
        /// симуляции — тогда кадры попадают в тот же физический шаг, а не в
        /// следующий, и мягкая зона не отстаёт на кадр.
        /// </summary>
        public int Apply(Matrix4x4[] frames, int count)
        {
            _lastExternalApply = Time.frameCount;
            return WriteFrames(frames, count);
        }

        /// <summary>
        /// Общая запись кадров. Кадры приходят в МИРОВЫХ координатах (так
        /// гласит контракт), а кадры-трансформы — дочерние объекты владельца,
        /// поэтому мировая матрица переводится в локальную. Без этого перевода
        /// тело уезжало при любом смещении владельца в иерархии.
        /// </summary>
        int WriteFrames(Matrix4x4[] frames, int count)
        {
            if (frames == null || count <= 0) return 0;

            EnsureFrames(count);
            count = Mathf.Min(count, _frames.Length);

            Matrix4x4 worldToLocal = owner != null
                ? owner.transform.worldToLocalMatrix
                : transform.worldToLocalMatrix;

            for (int i = 0; i < count; i++)
            {
                var t = _frames[i];
                if (t == null) continue;

                Matrix4x4 local = worldToLocal * frames[i];
                t.localPosition = local.GetColumn(3);
                t.localRotation = local.rotation;
                t.localScale = Vector3.one;
            }

            _applied = count;

            // Расхождение числа кадров и кластеров — не тихий отказ: оболочки
            // без кадра останутся на месте и попадания разъедутся с мешем.
            if (owner != null && owner.HullCount > 0)
            {
                int want = Dyc_ClusterCountOf(owner);
                _mismatch = want > 0 && want != count;
            }

            return count;
        }

        /// <summary>Сколько кластеров ожидает собранный набор. 0 — обычный, не мягкий.</summary>
        static int Dyc_ClusterCountOf(Dyc_DynamicCollision owner)
        {
            var set = owner != null ? owner.BakedSet : null;
            return set != null ? set.clusterCount : 0;
        }

        void OnDestroy()
        {
            if (_frames == null) return;
            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i] == null) continue;
                if (Application.isPlaying) Destroy(_frames[i].gameObject);
                else DestroyImmediate(_frames[i].gameObject);
            }
            _frames = null;
            _rest = null;
        }

        /// <summary>
        /// Набор подменили в рантайме: старые кадры и позы покоя больше не
        /// описывают объект. Кадры удаляются, при следующем обращении
        /// создаются заново уже по новому набору.
        /// </summary>
        public void ResetFrames()
        {
            if (_frames != null)
            {
                for (int i = 0; i < _frames.Length; i++)
                {
                    if (_frames[i] == null) continue;
                    if (Application.isPlaying) Destroy(_frames[i].gameObject);
                    else DestroyImmediate(_frames[i].gameObject);
                }
            }

            _frames = null;
            _buffer = null;
            _rest = null;
            _needed = 0;
            _applied = -1;
        }
    }
}
