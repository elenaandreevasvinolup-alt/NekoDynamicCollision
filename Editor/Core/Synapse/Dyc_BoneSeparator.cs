using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Раздвигает близкие вершины, принадлежащие РАЗНЫМ костям.
    ///
    /// ЗАЧЕМ. Ядро разложения — чисто геометрическое: оно знает вершины и
    /// треугольники и не знает, что такое кость. Поэтому там, где две части тела
    /// прижаты друг к другу (руки опущены вдоль туловища, ноги сведены, пальцы
    /// вместе), воксельная модель видит ОДИН СПЛОШНОЙ объём. Резать сплошной
    /// объём у алгоритма нет причин, и он выдаёт одну часть на две ноги — а в
    /// анимации ноги разъезжаются, и эта часть начинает их растягивать.
    ///
    /// РЕШЕНИЕ. Перед разложением раздвинуть такие вершины на доли миллиметра —
    /// ровно настолько, чтобы в воксельной сетке появился зазор. Ядро разрежет
    /// по зазору само, потому что теперь там действительно пусто.
    ///
    /// ЭТО ТОЛЬКО ДЛЯ РАЗЛОЖЕНИЯ. Сдвинутая копия меша живёт внутри запекания и
    /// уничтожается сразу после: в коллайдеры и в игру она не попадает. Поэтому
    /// зазор можно выбирать по удобству счёта, не думая о геометрии.
    ///
    /// ВЕС УЧИТЫВАЕТСЯ. Вершина на стыке костей (50% бедра, 50% таза) —
    /// законно общая, и раздвигать её нельзя: она держит непрерывность тела.
    /// Поэтому сдвиг умножается на «чистоту» вершины: доля веса, которая НЕ
    /// принадлежит соседней кости. Чистая вершина уезжает полностью, спорная —
    /// почти стоит.
    /// </summary>
    public static class Dyc_BoneSeparator
    {
        /// <summary>
        /// Возвращает копию меша с раздвинутыми вершинами.
        ///
        /// null означает «раздвигать нечего»: вызывающий обязан взять
        /// ИСХОДНЫЙ меш, а не пустоту.
        /// </summary>
        public static Mesh Separate(Mesh source, BoneWeight[] weights, Transform[] bones,
            float gap, float searchRadius, out int movedVertices)
        {
            movedVertices = 0;
            if (source == null || weights == null || bones == null) return null;
            if (gap <= 0f) return null;

            var vertices = source.vertices;
            if (vertices.Length == 0 || weights.Length != vertices.Length) return null;

            // 1. Главная кость каждой вершины.
            var boneOf = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                boneOf[i] = DominantBone(bones, weights[i]);

            var grid = new VertexGrid(vertices, 96);
            var moved = (Vector3[])vertices.Clone();
            int count = 0;

            // 2. Каждая вершина ищет ближайшую «чужую» и отходит от неё.
            //
            // Сдвиг считается для всех вершин независимо, и обе стороны пары
            // получают противоположные направления — поэтому зазор
            // раскрывается на полную величину, а центр тела не уезжает.
            for (int v = 0; v < vertices.Length; v++)
            {
                int ownBone = boneOf[v];
                if (ownBone < 0) continue;

                if (!grid.NearestOtherBone(vertices[v], boneOf, ownBone, searchRadius, out int other, out float distance))
                    continue;

                if (distance < 1e-6f)
                {
                    // Полное совпадение: направление взять неоткуда, но и
                    // оставлять вершины в одной точке нельзя — разводим по оси Y.
                    moved[v] += Vector3.up * gap * 0.5f;
                    count++;
                    continue;
                }

                // Вес: доля, которая НЕ принадлежит соседней кости. Чистая
                // вершина уезжает целиком, спорная на стыке — почти стоит.
                float foreign = WeightOf(weights[v], boneOf[other]);
                float purity = Mathf.Clamp01(1f - foreign);
                if (purity <= 0.01f) continue;

                Vector3 direction = (vertices[v] - vertices[other]).normalized;
                moved[v] += direction * (gap * 0.5f * purity);
                count++;
            }

            if (count == 0) return null;

            // 3. Копия меша. Топология та же — меняются только позиции, поэтому
            // треугольники, подмеши и всё остальное переносятся как есть.
            var result = new Mesh { name = source.name + "_Separated", hideFlags = HideFlags.DontSave };
            result.SetVertices(moved);
            result.indexFormat = source.indexFormat;

            int subCount = source.subMeshCount;
            result.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
                result.SetTriangles(source.GetTriangles(s), s);

            result.RecalculateBounds();
            movedVertices = count;
            return result;
        }

        // ------------------------------------------------------------------ вспомогательное

        static int DominantBone(Transform[] bones, BoneWeight weight)
        {
            int best = -1;
            float bestWeight = 0f;

            best = Better(bones, weight.boneIndex0, weight.weight0, best, ref bestWeight);
            best = Better(bones, weight.boneIndex1, weight.weight1, best, ref bestWeight);
            best = Better(bones, weight.boneIndex2, weight.weight2, best, ref bestWeight);
            best = Better(bones, weight.boneIndex3, weight.weight3, best, ref bestWeight);

            return best;
        }

        static int Better(Transform[] bones, int index, float weight, int best, ref float bestWeight)
        {
            if (weight <= bestWeight) return best;
            if (index < 0 || index >= bones.Length) return best;
            if (bones[index] == null) return best;

            bestWeight = weight;
            return index;
        }

        static float WeightOf(BoneWeight weight, int boneIndex)
        {
            float total = 0f;
            if (weight.boneIndex0 == boneIndex) total += weight.weight0;
            if (weight.boneIndex1 == boneIndex) total += weight.weight1;
            if (weight.boneIndex2 == boneIndex) total += weight.weight2;
            if (weight.boneIndex3 == boneIndex) total += weight.weight3;
            return total;
        }

        /// <summary>
        /// Сетка вершин с поиском ближайшей ЧУЖОЙ вершины.
        ///
        /// Отдельный тип, а не переиспользование VertexGrid из запекателя:
        /// здесь нужен другой критерий (кость), и попытка приспособить общий
        /// класс сделала бы оба хуже.
        /// </summary>
        class VertexGrid
        {
            readonly Vector3[] _vertices;
            readonly float _cell;
            readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            public VertexGrid(Vector3[] vertices, int resolution)
            {
                _vertices = vertices;

                var bounds = new Bounds(vertices.Length > 0 ? vertices[0] : Vector3.zero, Vector3.one);
                for (int i = 0; i < vertices.Length; i++) bounds.Encapsulate(vertices[i]);

                _cell = Mathf.Max(bounds.size.magnitude / Mathf.Max(8, resolution), 1e-5f);

                for (int i = 0; i < vertices.Length; i++)
                {
                    long key = Pack(vertices[i]);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<int>(8);
                        _cells[key] = list;
                    }
                    list.Add(i);
                }
            }

            public bool NearestOtherBone(Vector3 point, int[] boneOf, int ownBone,
                float maxDistance, out int other, out float distance)
            {
                other = -1;
                distance = maxDistance;

                int cx = Mathf.FloorToInt(point.x / _cell);
                int cy = Mathf.FloorToInt(point.y / _cell);
                int cz = Mathf.FloorToInt(point.z / _cell);

                // Радиус обхода зависит от того, сколько ячеек покрывает
                // допустимое расстояние: у крупных мешей одной ячейки мало.
                int reach = Mathf.Clamp(Mathf.CeilToInt(maxDistance / _cell), 1, 4);

                for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        int index = list[i];
                        if (boneOf[index] == ownBone) continue;

                        float d = (point - _vertices[index]).sqrMagnitude;
                        if (d >= distance * distance) continue;

                        distance = Mathf.Sqrt(d);
                        other = index;
                    }
                }

                return other >= 0;
            }

            long Pack(Vector3 p)
            {
                return Pack(Mathf.FloorToInt(p.x / _cell), Mathf.FloorToInt(p.y / _cell), Mathf.FloorToInt(p.z / _cell));
            }

            static long Pack(int x, int y, int z)
            {
                return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
            }
        }
    }
}
