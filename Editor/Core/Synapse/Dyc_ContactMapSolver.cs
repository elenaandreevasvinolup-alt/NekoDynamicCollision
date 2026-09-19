using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Результат счёта карты контактов: голые массивы, без Unity-ассетов.
    /// Отдельный тип нужен, чтобы алгоритм проверялся тестом вне редактора:
    /// ScriptableObject в тестовом стенде не создать.
    /// </summary>
    public sealed class Dyc_ContactMapData
    {
        public int vertexCount;
        public float radius;
        public int ringLimit;
        public float meanEdgeLength;

        /// <summary>Плоский список пар: 0-1, 2-3… Индексы вершин, всегда по возрастанию.</summary>
        public int[] pairs;

        public int clusterCount;
        public int[] clusterOf;
        public Vector3[] clusterCenter;
        public float[] clusterRadius;

        public int PairCount { get { return pairs != null ? pairs.Length / 2 : 0; } }
    }

    /// <summary>
    /// Чистый счёт карты контактов (ядро Synapse). Ни одного Unity-объекта:
    /// только Vector3 и Mathf, поэтому проверяется тестом вне редактора.
    ///
    /// Здесь считается то, что в Obi считается каждый подшаг: множество пар,
    /// которые могут столкнуться. Разница принципиальная — здесь это делается
    /// ОДИН РАЗ при запекании, потому что для ткани и верёвки это множество
    /// почти не зависит от позы, только от топологии.
    ///
    /// Почему так вообще можно. Мягкое тело сталкивается само с собой
    /// ЛОКАЛЬНО: складка касается только того, что и так рядом. Значит
    /// множество потенциальных пар фиксировано топологией и его можно испечь
    /// в массив. Динамическая широкая фаза (пространственный хэш на каждом
    /// подшаге) существует ровно для того, чтобы найти это множество заново —
    /// а мы его уже знаем.
    ///
    /// Правило отбора пары (v, w):
    ///   · в rest-позе они ближе radius — иначе не встретятся никогда;
    ///   · по графу меша они дальше ringLimit рёбер — иначе это соседи,
    ///     которые «сталкиваются» просто потому, что соединены.
    ///
    /// Честная граница: пара, далёкая в rest-позе, не попадёт сюда никогда,
    /// даже если ткань сложится пополам и эти места встретятся. Поэтому
    /// radius берётся с запасом в 2–3 толщины, а второй уровень (кластеры)
    /// закрывает крупные перемещения.
    ///
    /// Стоимость: O(V × (k·deg + ячейки в радиусе)), k = ringLimit. Для 1000
    /// вершин это миллисекунды, то есть цена нажатия кнопки, а не кадра.
    /// </summary>
    public static class Dyc_ContactMapSolver
    {
        /// <summary>Радиус контакта по умолчанию, метры: два слоя ткани плюс запас.</summary>
        public const float DefaultRadius = 0.02f;

        /// <summary>Шаг сетки кластеров в радиусах: крупнее — меньше кластеров,
        /// но хуже жёсткое приближение кадра.</summary>
        const float ClusterCellFactor = 2.5f;

        /// <summary>Предел ringLimit: дальше уже не соседи, а половина меша.</summary>
        const int MaxRingLimit = 8;

        public static Dyc_ContactMapData Build(Vector3[] positions, int[] triangles, float radius, out string report)
        {
            report = null;
            if (positions == null || positions.Length < 4)
            {
                report = "слишком мало вершин";
                return null;
            }
            if (triangles == null || triangles.Length < 3)
            {
                report = "нет треугольников";
                return null;
            }
            if (radius <= 0f) radius = DefaultRadius;

            int n = positions.Length;

            // --- смежность: CSR. Списки List<int> на каждую вершину дали бы
            // тысячи мелких аллокаций, а BFS идёт по всем вершинам.
            //
            // По ДВА соседа на вершину в каждом треугольнике (a→b и a→c), а не
            // по одному: треугольник даёт вершине двух соседей, и если посчитать
            // по одному, запись выйдет за границы массива.
            var degree = new int[n];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                degree[triangles[t]] += 2;
                degree[triangles[t + 1]] += 2;
                degree[triangles[t + 2]] += 2;
            }

            var start = new int[n + 1];
            for (int i = 0; i < n; i++) start[i + 1] = start[i] + degree[i];

            var adjacency = new int[start[n]];
            var cursor = new int[n];
            double edgeSum = 0.0;
            int edgeCount = 0;

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                edgeSum += (positions[a] - positions[b]).magnitude
                         + (positions[b] - positions[c]).magnitude
                         + (positions[c] - positions[a]).magnitude;
                edgeCount += 3;

                adjacency[start[a] + cursor[a]++] = b;
                adjacency[start[a] + cursor[a]++] = c;
                adjacency[start[b] + cursor[b]++] = a;
                adjacency[start[b] + cursor[b]++] = c;
                adjacency[start[c] + cursor[c]++] = a;
                adjacency[start[c] + cursor[c]++] = b;
            }

            float meanEdge = edgeCount > 0 ? (float)(edgeSum / edgeCount) : radius * 0.5f;
            if (meanEdge <= 1e-6f) meanEdge = radius * 0.5f;

            // Сколько рёбер отделяет «своих». Если радиус контакта покрывает k
            // рёбер, то вершины ближе k рёбер — заведомо соседи, и сталкивать
            // их нельзя: иначе ткань отталкивает сама себя в покое и «кипит».
            int ringLimit = Mathf.Clamp(Mathf.CeilToInt(radius / meanEdge) + 1, 2, MaxRingLimit);

            // --- сетка для поиска кандидатов
            float cell = Mathf.Max(radius, 1e-4f);
            var grid = new Dictionary<long, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                long key = Cell(positions[i], cell);
                if (!grid.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(8);
                    grid[key] = bucket;
                }
                bucket.Add(i);
            }

            // --- пары
            var pairs = new List<int>(n * 4);
            var mark = new int[n];
            for (int i = 0; i < n; i++) mark[i] = -1;
            var queue = new int[n];
            float r2 = radius * radius;

            for (int v = 0; v < n; v++)
            {
                MarkNeighbours(v, adjacency, start, ringLimit, mark, queue);

                Vector3 pv = positions[v];
                int cx = Mathf.FloorToInt(pv.x / cell);
                int cy = Mathf.FloorToInt(pv.y / cell);
                int cz = Mathf.FloorToInt(pv.z / cell);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var bucket)) continue;

                    for (int k = 0; k < bucket.Count; k++)
                    {
                        int w = bucket[k];
                        if (w <= v) continue;              // пара один раз, самопар нет
                        if (mark[w] == v) continue;        // топологический сосед
                        if ((positions[w] - pv).sqrMagnitude > r2) continue;

                        pairs.Add(v);
                        pairs.Add(w);
                    }
                }
            }

            var cluster = BuildClusters(positions, cell * ClusterCellFactor, out var centers, out var radii);

            report = $"{pairs.Count / 2} пар / {n} вершин · кластеров {centers.Length} · " +
                     $"ringLimit {ringLimit} · среднее ребро {meanEdge * 1000f:F1} мм";

            return new Dyc_ContactMapData
            {
                vertexCount = n,
                radius = radius,
                ringLimit = ringLimit,
                meanEdgeLength = meanEdge,
                pairs = pairs.ToArray(),
                clusterOf = cluster,
                clusterCenter = centers,
                clusterRadius = radii,
                clusterCount = centers.Length
            };
        }

        // ------------------------------------------------------------------ вспомогательное

        /// <summary>BFS в глубину ringLimit: помечает всех, кто ближе по графу.
        /// Метка — номер текущей вершины, поэтому очистка между проходами не нужна.</summary>
        static void MarkNeighbours(int v, int[] adjacency, int[] start, int ringLimit,
            int[] mark, int[] queue)
        {
            int head = 0, tail = 0;
            queue[tail++] = v;
            mark[v] = v;

            for (int depth = 0; depth < ringLimit && head < tail; depth++)
            {
                int levelEnd = tail;
                while (head < levelEnd)
                {
                    int cur = queue[head++];
                    for (int e = start[cur]; e < start[cur + 1]; e++)
                    {
                        int next = adjacency[e];
                        if (mark[next] == v) continue;
                        mark[next] = v;
                        queue[tail++] = next;
                    }
                }
            }
        }

        /// <summary>
        /// Кластеры заливкой по занятым ячейкам сетки. Своя, а не из запекателя
        /// оболочек: там кластер растёт по месту и допускает рыхлую форму, а
        /// кадру деформации нужна КОМПАКТНАЯ — иначе жёсткое приближение врёт.
        /// </summary>
        static int[] BuildClusters(Vector3[] positions, float cell,
            out Vector3[] centers, out float[] radii)
        {
            int n = positions.Length;
            var cellOf = new Dictionary<long, int>(n);      // ячейка → индекс кластера
            var vertexCell = new long[n];

            for (int i = 0; i < n; i++)
            {
                long key = Cell(positions[i], cell);
                vertexCell[i] = key;
                if (!cellOf.ContainsKey(key)) cellOf[key] = -1;
            }

            var occupied = new List<long>(cellOf.Keys);
            int clusters = 0;
            var stack = new Stack<long>();

            for (int i = 0; i < occupied.Count; i++)
            {
                long seed = occupied[i];
                if (cellOf[seed] >= 0) continue;

                int id = clusters++;
                cellOf[seed] = id;
                stack.Push(seed);

                while (stack.Count > 0)
                {
                    long cur = stack.Pop();
                    Unpack(cur, out int cx, out int cy, out int cz);

                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz) != 1) continue; // 6-связность
                        long next = Pack(cx + dx, cy + dy, cz + dz);
                        if (!cellOf.TryGetValue(next, out int state) || state >= 0) continue;
                        cellOf[next] = id;
                        stack.Push(next);
                    }
                }
            }

            var result = new int[n];
            int count = Mathf.Max(1, clusters);
            centers = new Vector3[count];
            radii = new float[count];
            var sums = new Vector3[count];
            var counts = new int[count];

            for (int i = 0; i < n; i++)
            {
                int id = cellOf.TryGetValue(vertexCell[i], out int c) && c >= 0 ? c : 0;
                result[i] = id;
                sums[id] += positions[i];
                counts[id]++;
            }

            for (int c = 0; c < clusters; c++)
            {
                centers[c] = counts[c] > 0 ? sums[c] / counts[c] : Vector3.zero;
                // Полклетки: это отсечка широкой фазы, а не физический радиус,
                // точность здесь не нужна и стоила бы лишнего прохода.
                radii[c] = cell * 0.5f;
            }

            return result;
        }

        static long Cell(Vector3 p, float cell)
        {
            return Pack(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
        }

        static long Pack(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        static void Unpack(long key, out int x, out int y, out int z)
        {
            x = (int)((key >> 42) & 0x1FFFFF);
            y = (int)((key >> 21) & 0x1FFFFF);
            z = (int)(key & 0x1FFFFF);
        }
    }
}
