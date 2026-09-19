using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Живое обновление оболочек: пересборка по ТЕКУЩЕЙ форме в рамках бюджета
    /// процессора. Асинхронное обновление под бюджетом — и главная причина,
    /// по которой мягкое тело вообще имеет смысл в рантайме.
    ///
    /// ЧЕМ ЭТО ОТЛИЧАЕТСЯ ОТ «ПЕРЕСОБРАТЬ ВСЁ КАЖДЫЙ КАДР».
    ///
    /// Три вещи, и все три — про то, чтобы стоимость была предсказуемой, а не
    /// «как повезёт»:
    ///
    ///   1. ИНКРЕМЕНТ. Считается смещение каждого кластера с прошлого прохода,
    ///      и пересобираются только СДВИНУВШИЕСЯ. Мягкое тело висит на якоре,
    ///      край плаща трепещет, а середина почти неподвижна — и середина не
    ///      стоит ничего.
    ///
    ///   2. ПРИОРИТЕТ. Очередь упорядочена по величине смещения: сначала те
    ///      кластеры, что изменились сильнее всего. Если бюджета не хватило,
    ///      не хватило его на самых спокойных — то есть на тех, чья неточность
    ///      заметна меньше всего. Без этого порядка бюджет тратился бы на
    ///      первые по номеру, то есть случайные.
    ///
    ///   3. БЮДЖЕТ. Проход останавливается по часам, а не по числу кластеров.
    ///      Не успевшее переносится на следующий кадр, и цена кадра не зависит
    ///      от того, сколько кластеров у тела.
    ///
    /// Плюс ноль выделений в кадре: все буферы переиспользуются, а форма
    /// переписывается в ТОТ ЖЕ меш коллайдера.
    ///
    /// ЧЕГО ОНО НЕ ДЕЛАЕТ. Не трогает зоны, материалы, события и прочую логику:
    /// они читают коллайдеры, а те остаются на месте — меняется только форма.
    /// Запечённый набор не изменяется: обновляется рантайм-меш коллайдера.
    /// </summary>
    [AddComponentMenu("NekoWorks/Dynamic Collision/Live Update")]
    [DisallowMultipleComponent]
    public class Dyc_LiveUpdate : MonoBehaviour
    {
        [Tooltip("Компонент коллизий, который обслуживается. Ставится автоматически.")]
        public Dyc_DynamicCollision target;

        /// <summary>Проход упёрся в бюджет и продолжится в следующем кадре.</summary>
        public event Action<double> OnUpdateYield;

        /// <summary>Полный проход по всем изменившимся оболочкам завершён. Аргумент — мс.</summary>
        public event Action<double> OnPassComplete;

        Mesh _baked;
        Transform _meshSpace;

        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<Vector3> _scratch = new List<Vector3>(1024);
        readonly Stopwatch _watch = new Stopwatch();

        // ---- инкремент и приоритет
        Vector3[] _centroid;      // текущий центр кластера
        Vector3[] _centroidPrev;  // центр на прошлом проходе
        float[] _move;            // величина смещения
        int[] _dirty;             // номера кластеров, отсортированные по смещению
        int _dirtyCount;
        Collider[] _byHull;       // коллайдер по номеру оболочки

        Vector3[] _samples;
        int _cursor;
        bool _running;
        bool _continuous;
        bool _warnedNoVertices;

        /// <summary>Сколько кластеров реально сдвинулось в прошлом проходе.</summary>
        public int LastDirtyCount { get { return _dirtyCount; } }

        /// <summary>Сколько кластеров пересобрано в прошлом проходе.</summary>
        public int LastBuiltCount { get; private set; }

        public bool IsUpdating { get { return _running; } }

        /// <summary>Создать (или найти) компонент и запустить обновление.</summary>
        public static Dyc_LiveUpdate Attach(Dyc_DynamicCollision target)
        {
            if (target == null) return null;

            var live = target.GetComponent<Dyc_LiveUpdate>();
            if (live == null) live = target.gameObject.AddComponent<Dyc_LiveUpdate>();
            live.target = target;
            live.StartUpdating(target.Advanced.liveUpdateContinuous);
            return live;
        }

        public void StartUpdating(bool continuous)
        {
            _continuous = continuous;
            _cursor = 0;
            _running = true;
        }

        /// <summary>Остановиться СРАЗУ, не доигрывая проход.</summary>
        public void StopUpdating()
        {
            _running = false;
            _cursor = 0;
        }

        /// <summary>Доиграть текущий проход и остановиться.</summary>
        public void StopAfterPass()
        {
            _continuous = false;
        }

        /// <summary>
        /// Один полный проход немедленно, вне бюджета, по ВСЕМ кластерам.
        /// Может дать просадку кадра — это цена точности «прямо сейчас».
        /// </summary>
        public void UpdateNow()
        {
            if (!Prepare(true, true)) return;

            _watch.Restart();
            LastBuiltCount = 0;

            for (int i = 0; i < _dirtyCount; i++) RebuildHull(_dirty[i]);

            _cursor = 0;
            if (OnPassComplete != null) OnPassComplete(_watch.Elapsed.TotalMilliseconds);
        }

        void Update()
        {
            if (!_running || target == null || !target.IsBuilt) return;

            var adv = target.Advanced;
            double budget = _lastMove < adv.meshUpdateThreshold
                ? adv.idleCpuBudgetMs
                : adv.activeCpuBudgetMs;

            Step(budget);
        }

        void Step(double budgetMs)
        {
            if (!Prepare(false, false)) return;

            _watch.Restart();
            LastBuiltCount = 0;

            while (_cursor < _dirtyCount)
            {
                RebuildHull(_dirty[_cursor]);
                _cursor++;

                if (_watch.Elapsed.TotalMilliseconds >= budgetMs) break;
            }

            if (_cursor >= _dirtyCount)
            {
                _cursor = 0;
                if (OnPassComplete != null) OnPassComplete(_watch.Elapsed.TotalMilliseconds);
                if (!_continuous) _running = false;
            }
            else if (OnUpdateYield != null)
            {
                OnUpdateYield(_watch.Elapsed.TotalMilliseconds);
            }
        }

        // ------------------------------------------------------------------ подготовка

        /// <summary>
        /// Сэмплирует текущую форму, считает смещение каждого кластера и
        /// строит очередь по убыванию смещения. false — двигать нечего.
        /// </summary>
        bool Prepare(bool force, bool allClusters)
        {
            var skin = target.SourceSkin;
            if (skin == null) skin = target.GetComponentInChildren<SkinnedMeshRenderer>();

            if (skin != null && skin.sharedMesh != null)
            {
                _meshSpace = skin.transform;

                if (_baked == null || _baked.vertexCount != skin.sharedMesh.vertexCount)
                {
                    _baked = new Mesh { name = "NDC_LiveBaked" };
                    _baked.MarkDynamic();
                }

                skin.BakeMesh(_baked, true);
                _baked.GetVertices(_verts);
            }
            else
            {
                var filter = target.SourceMesh;
                if (filter == null) filter = target.GetComponentInChildren<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return false;

                _meshSpace = filter.transform;

                // Читаются CPU-вершины. Деформация целиком в шейдере или в
                // GPU-скиннинге сюда не попадает — это ограничение честное и
                // описано в руководстве.
                filter.sharedMesh.GetVertices(_verts);
            }

            if (_verts.Count == 0) return false;

            _lastMove = MeasureMove();
            if (!force && !allClusters && _lastMove < target.Advanced.meshUpdateThreshold) return false;

            EnsureMap();
            BuildDirtyQueue(allClusters);

            return _dirtyCount > 0;
        }

        /// <summary>
        /// Карта «номер оболочки → коллайдер». Строится по связи из
        /// Dyc_DynamicCollision, а не по порядку в списке: часть оболочек при
        /// сборке могла быть пропущена, и порядок разъехался бы.
        /// </summary>
        void EnsureMap()
        {
            var set = target.BakedSet;
            if (set == null || set.hulls == null) return;

            int hulls = set.hulls.Count;
            if (_byHull != null && _byHull.Length == hulls) return;

            _byHull = new Collider[hulls];
            _centroid = new Vector3[hulls];
            _centroidPrev = new Vector3[hulls];
            _move = new float[hulls];
            _dirty = new int[hulls];

            for (int i = 0; i < target.HullCount; i++)
            {
                var c = target.HullCollider(i);
                int h = target.HullIndexOf(c);
                if (h >= 0 && h < hulls) _byHull[h] = c;
            }
        }

        /// <summary>
        /// Смещение центров кластеров и очередь по убыванию смещения.
        ///
        /// Считается центр, а не лучший поворот: центр — это ровно то, что
        /// двигает оболочку целиком, а поворот на глаз заметен уже после
        /// заметного смещения. Порог отсекает «шевеление», которое всё равно
        /// не видно в столкновениях.
        /// </summary>
        void BuildDirtyQueue(bool allClusters)
        {
            var set = target.BakedSet;
            _dirtyCount = 0;
            if (set == null || set.hulls == null || _byHull == null) return;

            float threshold = target.Advanced.meshUpdateThreshold;
            var hulls = set.hulls;

            for (int h = 0; h < hulls.Count && h < _byHull.Length; h++)
            {
                var hull = hulls[h];
                if (hull == null || _byHull[h] == null) continue;
                if (hull.sourceVertices == null || hull.sourceVertices.Length == 0) continue;

                Vector3 c = CentroidOf(hull);
                _centroid[h] = c;

                if (_centroidPrev[h] == Vector3.zero && !allClusters)
                {
                    // Первый проход: считаем сдвинувшимся, иначе стартовая поза
                    // никогда не была бы записана.
                    _centroidPrev[h] = c;
                    _move[h] = float.MaxValue;
                    _dirty[_dirtyCount++] = h;
                    continue;
                }

                float d = (c - _centroidPrev[h]).magnitude;
                _move[h] = d;

                if (allClusters || d >= threshold)
                    _dirty[_dirtyCount++] = h;
            }

            // Приоритет: сильнее всего изменившиеся — первыми. Бюджет, которого
            // не хватило, достаётся самым спокойным кластерам, где неточность
            // заметна меньше всего.
            Array.Sort(_dirty, 0, _dirtyCount, Comparer<int>.Create(
                (a, b) => _move[b].CompareTo(_move[a])));
        }

        Vector3 CentroidOf(DycBakedHull hull)
        {
            var src = hull.sourceVertices;
            Vector3 sum = Vector3.zero;
            int n = 0;

            for (int i = 0; i < src.Length; i++)
            {
                int v = src[i];
                if (v < 0 || v >= _verts.Count) continue;
                sum += _verts[v];
                n++;
            }

            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>
        /// Смещение пробных вершин с прошлого прохода.
        ///
        /// Считаются не все вершины, а каждая N-я: величина нужна только чтобы
        /// выбрать бюджет (дешёвый для стоящего тела, дорогой для бегущего), а
        /// не чтобы измерить деформацию точно.
        /// </summary>
        float MeasureMove()
        {
            if (_verts.Count == 0) return 0f;

            int step = Mathf.Max(1, _verts.Count / 64);
            int count = (_verts.Count / step) + 1;

            if (_samples == null || _samples.Length != count)
            {
                _samples = new Vector3[count];
                for (int i = 0, s = 0; i < _verts.Count; i += step, s++) _samples[s] = _verts[i];
                return 1f;
            }

            float max = 0f;
            for (int i = 0, s = 0; i < _verts.Count; i += step, s++)
            {
                float d = (_verts[i] - _samples[s]).magnitude;
                if (d > max) max = d;
                _samples[s] = _verts[i];
            }
            return max;
        }

        float _lastMove = 1f;

        // ------------------------------------------------------------------ сборка

        void RebuildHull(int hullIndex)
        {
            var set = target.BakedSet;
            if (set == null || hullIndex < 0 || hullIndex >= set.hulls.Count) return;

            var hull = set.hulls[hullIndex];
            if (hull == null) return;

            var collider = hullIndex < _byHull.Length ? _byHull[hullIndex] as MeshCollider : null;
            if (collider == null) return;

            if (hull.sourceVertices == null || hull.sourceVertices.Length < 4)
            {
                if (!_warnedNoVertices)
                {
                    _warnedNoVertices = true;
                    UnityEngine.Debug.LogWarning(
                        "[NDC] Live update: у оболочек нет списка вершин (запекание старое). " +
                        "Перезапеките объект, чтобы включить живое обновление.", this);
                }
                return;
            }

            if (_meshSpace == null) return;

            var mesh = collider.sharedMesh;
            if (mesh == null) return;

            Matrix4x4 toWorld = _meshSpace.localToWorldMatrix;
            Matrix4x4 toLocal = collider.transform.worldToLocalMatrix;

            _scratch.Clear();

            var src = hull.sourceVertices;
            for (int i = 0; i < src.Length; i++)
            {
                int v = src[i];
                if (v < 0 || v >= _verts.Count) continue;

                Vector3 world = toWorld.MultiplyPoint3x4(_verts[v]);
                _scratch.Add(toLocal.MultiplyPoint3x4(world));
            }

            if (_scratch.Count < 4) return;

            DycHullResult result;
            if (!Dyc_Hull.Build(_scratch, out result, 1e-5f) || !result.ok) return;
            if (result.points == null || result.points.Length < 4) return;

            // Перезаписываем ТОТ ЖЕ меш и переназначаем его коллайдеру:
            // именно переназначение заставляет PhysX переготовить форму.
            mesh.Clear();
            mesh.SetVertices(result.points);
            mesh.SetTriangles(result.triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            collider.sharedMesh = mesh;

            // Центр запоминается как «прошлый»: кластер, который больше не
            // двигается, на следующем проходе в очередь не попадёт.
            _centroidPrev[hullIndex] = _centroid[hullIndex];
            LastBuiltCount++;
        }

        void OnDestroy()
        {
            _running = false;
            OnUpdateYield = null;
            OnPassComplete = null;
        }
    }
}
