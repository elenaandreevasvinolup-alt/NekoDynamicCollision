using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    public class DycCluster
    {
        /// <summary>Номер треугольника (не индексная позиция), каждый номер соответствует 3 подряд идущим индексам в tris.</summary>
        public readonly List<int> tris = new List<int>(64);
        public int part;
        public byte label;
        public int UniqueVertexCount;

        public int TriangleCount => tris.Count;
    }

    /// <summary>
    /// Кластеризация в ограниченном пространстве.
    ///
    /// Индексная нарезка режет блоки по порядку индексов треугольников (Take/Skip); полученные выпуклые оболочки пересекаются и захватывают воздух,
    /// и чем больше блоков, тем они "толще". Здесь иначе:
    ///   1. Сначала группировка по ограничению (зона, метка материала) — метку задаёт кисть, это и есть реализация "материала коллизии по зонам";
    ///   2. Внутри группы — посев по самым удалённым точкам + рост областей в стиле Дейкстры по рёберной смежности — гарантирует связность и пространственную плотность;
    ///   3. При превышении бюджета вершин — рекурсивное деление пополам, чтобы каждый блок был ≤ лимита PhysX в 255 вершин.
    /// </summary>
    public static class Dyc_Cluster
    {
        public const int PhysXMaxHullVertices = 255;
        public const int SafeMaxHullVertices = 250;

        public static List<DycCluster> Build(
            Vector3[] verts,
            int[] tris,
            int[] partOfTri,
            byte[] labelOfTri,
            int hullsPerPart,
            int maxTrisPerHull,
            int maxVertsPerHull = SafeMaxHullVertices)
        {
            var result = new List<DycCluster>();
            if (verts == null || tris == null) return result;

            int triCount = tris.Length / 3;
            if (triCount == 0) return result;

            if (partOfTri == null) partOfTri = new int[triCount];
            if (labelOfTri == null) labelOfTri = new byte[triCount];

            // 1. Группировка по ограничениям
            var groups = new Dictionary<long, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                int part = partOfTri[t];
                if (part < 0) continue;              // грани без принадлежности не участвуют в кластеризации
                long key = ((long)part << 8) | labelOfTri[t];
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>(64);
                    groups[key] = list;
                }
                list.Add(t);
            }

            // 2. Кластеризация внутри группы
            foreach (var kv in groups)
            {
                int part = (int)(kv.Key >> 8);
                byte label = (byte)(kv.Key & 0xff);
                var groupTris = kv.Value;

                var ctx = new Context(verts, tris, groupTris, maxVertsPerHull, maxTrisPerHull);
                var seeds = ctx.Run(hullsPerPart, 0);

                for (int i = 0; i < seeds.Count; i++)
                {
                    var c = seeds[i];
                    c.part = part;
                    c.label = label;
                    result.Add(c);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------

        class Context
        {
            readonly Vector3[] _verts;
            readonly int[] _tris;
            readonly List<int> _groupTris;
            readonly int _maxVerts;
            readonly int _maxTris;

            readonly Dictionary<int, List<int>> _vertToTris = new Dictionary<int, List<int>>();
            readonly Dictionary<int, Vector3> _centroid = new Dictionary<int, Vector3>();

            public Context(Vector3[] verts, int[] tris, List<int> groupTris, int maxVerts, int maxTris)
            {
                _verts = verts;
                _tris = tris;
                _groupTris = groupTris;
                _maxVerts = maxVerts;
                _maxTris = maxTris;

                for (int i = 0; i < groupTris.Count; i++)
                {
                    int t = groupTris[i];
                    int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                    _centroid[t] = (verts[a] + verts[b] + verts[c]) / 3f;
                    Link(a, t);
                    Link(b, t);
                    Link(c, t);
                }
            }

            void Link(int v, int t)
            {
                if (!_vertToTris.TryGetValue(v, out var list))
                {
                    list = new List<int>(6);
                    _vertToTris[v] = list;
                }
                list.Add(t);
            }

            public List<DycCluster> Run(int k, int depth)
            {
                var clusters = new List<DycCluster>();
                if (_groupTris.Count == 0) return clusters;

                k = Mathf.Clamp(k, 1, _groupTris.Count);
                if (k == 1 && _groupTris.Count <= 1)
                {
                    clusters.Add(Make(_groupTris));
                    return clusters;
                }

                // Посев по самым удалённым точкам
                var seeds = PickSeeds(k);

                // Рост областей по рёберной смежности (куча сортирует по накопленному расстоянию)
                var assign = new Dictionary<int, int>(_groupTris.Count);
                var count = new int[seeds.Count];
                var heap = new MinHeap(_groupTris.Count * 4);

                for (int i = 0; i < seeds.Count; i++)
                    heap.Push(seeds[i], 0f, i);

                while (heap.Count > 0)
                {
                    heap.Pop(out int tri, out float dist, out int seed);
                    if (assign.ContainsKey(tri)) continue;
                    if (count[seed] >= _maxTris) continue;

                    assign[tri] = seed;
                    count[seed]++;

                    int a = _tris[tri * 3], b = _tris[tri * 3 + 1], c = _tris[tri * 3 + 2];
                    PushNeighbours(a, tri, seed, dist, heap, assign);
                    PushNeighbours(b, tri, seed, dist, heap, assign);
                    PushNeighbours(c, tri, seed, dist, heap, assign);
                }

                // Сбор
                var buckets = new List<List<int>>();
                for (int i = 0; i < seeds.Count; i++) buckets.Add(new List<int>(32));
                foreach (var kv in assign) buckets[kv.Value].Add(kv.Key);

                // Не распределённые остатки (бюджет исчерпан) отправляем в ближайшую корзину
                if (assign.Count < _groupTris.Count)
                {
                    for (int i = 0; i < _groupTris.Count; i++)
                    {
                        int t = _groupTris[i];
                        if (assign.ContainsKey(t)) continue;
                        buckets[NearestBucket(t, buckets, seeds)].Add(t);
                    }
                }

                // При превышении бюджета — рекурсивное деление пополам
                for (int i = 0; i < buckets.Count; i++)
                {
                    var list = buckets[i];
                    if (list.Count == 0) continue;

                    var c = Make(list);
                    if (depth < 5 && (c.UniqueVertexCount > _maxVerts || c.TriangleCount > _maxTris))
                    {
                        var sub = new Context(_verts, _tris, list, _maxVerts, _maxTris);
                        clusters.AddRange(sub.Run(2, depth + 1));
                    }
                    else
                    {
                        clusters.Add(c);
                    }
                }

                return clusters;
            }

            int NearestBucket(int tri, List<List<int>> buckets, List<int> seeds)
            {
                Vector3 c = _centroid[tri];
                int best = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < buckets.Count; i++)
                {
                    if (buckets[i].Count == 0) continue;
                    float d = (c - _centroid[seeds[i]]).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                return best;
            }

            void PushNeighbours(int v, int from, int seed, float dist, MinHeap heap, Dictionary<int, int> assign)
            {
                if (!_vertToTris.TryGetValue(v, out var list)) return;
                for (int i = 0; i < list.Count; i++)
                {
                    int n = list[i];
                    if (n == from || assign.ContainsKey(n)) continue;
                    float nd = dist + (_centroid[n] - _centroid[from]).magnitude;
                    heap.Push(n, nd, seed);
                }
            }

            List<int> PickSeeds(int k)
            {
                var seeds = new List<int>(k) { _groupTris[0] };
                if (k == 1) return seeds;

                var best = new float[_groupTris.Count];
                for (int i = 0; i < best.Length; i++)
                    best[i] = (_centroid[_groupTris[i]] - _centroid[seeds[0]]).sqrMagnitude;

                while (seeds.Count < k)
                {
                    int next = -1;
                    float far = -1f;
                    for (int i = 0; i < _groupTris.Count; i++)
                    {
                        if (best[i] > far) { far = best[i]; next = i; }
                    }
                    if (next < 0) break;

                    int tri = _groupTris[next];
                    seeds.Add(tri);
                    best[next] = -1f;

                    for (int i = 0; i < _groupTris.Count; i++)
                    {
                        if (best[i] < 0f) continue;
                        float d = (_centroid[_groupTris[i]] - _centroid[tri]).sqrMagnitude;
                        if (d < best[i]) best[i] = d;
                    }
                }
                return seeds;
            }

            DycCluster Make(List<int> tris)
            {
                var c = new DycCluster();
                c.tris.AddRange(tris);

                var set = new HashSet<int>();
                for (int i = 0; i < tris.Count; i++)
                {
                    int t = tris[i];
                    set.Add(_tris[t * 3]);
                    set.Add(_tris[t * 3 + 1]);
                    set.Add(_tris[t * 3 + 2]);
                }
                c.UniqueVertexCount = set.Count;
                return c;
            }
        }

        /// <summary>Минимальная бинарная куча. Unity 2022.3 — это .NET Standard 2.1, без PriorityQueue.</summary>
        class MinHeap
        {
            int[] _item;
            float[] _pri;
            int[] _tag;
            int _count;

            public int Count => _count;

            public MinHeap(int capacity)
            {
                capacity = Mathf.Max(16, capacity);
                _item = new int[capacity];
                _pri = new float[capacity];
                _tag = new int[capacity];
            }

            public void Push(int item, float pri, int tag)
            {
                if (_count == _item.Length) Grow();
                int i = _count++;
                _item[i] = item; _pri[i] = pri; _tag[i] = tag;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_pri[p] <= _pri[i]) break;
                    Swap(p, i);
                    i = p;
                }
            }

            public void Pop(out int item, out float pri, out int tag)
            {
                item = _item[0]; pri = _pri[0]; tag = _tag[0];
                _count--;
                if (_count > 0)
                {
                    _item[0] = _item[_count]; _pri[0] = _pri[_count]; _tag[0] = _tag[_count];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < _count && _pri[l] < _pri[m]) m = l;
                        if (r < _count && _pri[r] < _pri[m]) m = r;
                        if (m == i) break;
                        Swap(m, i);
                        i = m;
                    }
                }
            }

            void Grow()
            {
                int n = _item.Length * 2;
                System.Array.Resize(ref _item, n);
                System.Array.Resize(ref _pri, n);
                System.Array.Resize(ref _tag, n);
            }

            void Swap(int a, int b)
            {
                (_item[a], _item[b]) = (_item[b], _item[a]);
                (_pri[a], _pri[b]) = (_pri[b], _pri[a]);
                (_tag[a], _tag[b]) = (_tag[b], _tag[a]);
            }
        }
    }
}
