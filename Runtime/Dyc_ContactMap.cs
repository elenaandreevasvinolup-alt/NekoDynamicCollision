using System;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Карта контактов: какие пары вершин мягкого тела ВООБЩЕ могут
    /// столкнуться, посчитанные один раз при запекании.
    ///
    /// Это ответ на самоколлизию, противоположный подходу Obi. Там широкая
    /// фаза динамическая: пространственный хэш строится на каждом подшаге,
    /// затем узкая фаза (вершина-треугольник, ребро-ребро) и симметричные
    /// итерации до сходимости. Дорого и, что важнее, НЕПРЕДСКАЗУЕМО по
    /// бюджету — а на мобильном нужен именно предсказуемый бюджет.
    ///
    /// Здесь ставка на свойство самой ткани: она сталкивается сама с собой
    /// ЛОКАЛЬНО. Складка касается только того, что и так рядом. Значит
    /// множество потенциальных пар почти фиксировано топологией и его можно
    /// испечь.
    ///
    /// Правило отбора пары (v, w):
    ///   · в rest-позе они ближе radius — иначе не встретятся никогда;
    ///   · по графу меша они дальше ringLimit рёбер — иначе это соседи,
    ///     которые «сталкиваются» просто потому, что соединены.
    ///
    /// Что здесь лежит:
    ///   · pairs       — плоский список пар, 2 индекса на пару;
    ///   · clusterOf   — номер кластера на вершину (второй уровень отбора);
    ///   · cluster*    — центр и радиус кластера для быстрой отсечки.
    ///
    /// ДВА УРОВНЯ, оба статические. Рантайм сначала проверяет сферы кластеров
    /// (их десятки, это сотни тестов), и только для пересекающихся кластеров
    /// идёт по их парам. Так большие перемещения ловятся без динамической
    /// широкой фазы.
    ///
    /// Честная граница: пара, далёкая в rest-позе, не попадёт сюда никогда,
    /// даже если ткань сложится пополам и эти места встретятся. Поэтому
    /// radius берётся с запасом в 2–3 толщины, а второй уровень закрывает
    /// крупные перемещения.
    /// </summary>
    public class Dyc_ContactMap : ScriptableObject
    {
        public string sourceHash;

        public int vertexCount;

        public float radius = 0.02f;

        public int ringLimit = 3;

        public float meanEdgeLength;

        public int[] pairs = Array.Empty<int>();

        public int clusterCount;
        public int[] clusterOf = Array.Empty<int>();
        public Vector3[] clusterCenter = Array.Empty<Vector3>();
        public float[] clusterRadius = Array.Empty<float>();

        public int PairCount { get { return pairs != null ? pairs.Length / 2 : 0; } }

        public bool IsValid
        {
            get { return pairs != null && pairs.Length > 0 && clusterOf != null && clusterOf.Length > 0; }
        }

        /// <summary>Сколько пар приходится на кластер. Нужно для раскладки
        /// бюджета рантайма: дешёвый кластер не должен получать столько же
        /// времени, сколько дорогой.</summary>
        public int[] PairsPerCluster()
        {
            var counts = new int[Mathf.Max(1, clusterCount)];
            if (pairs == null || clusterOf == null) return counts;

            for (int i = 0; i < pairs.Length; i += 2)
            {
                int c = clusterOf[pairs[i]];
                if (c < 0 || c >= counts.Length) continue;
                counts[c]++;
            }
            return counts;
        }

        /// <summary>Кластер вершины. -1 — вершина не попала ни в один кластер.</summary>
        public int ClusterOf(int vertex)
        {
            if (clusterOf == null || vertex < 0 || vertex >= clusterOf.Length) return -1;
            return clusterOf[vertex];
        }
    }
}
