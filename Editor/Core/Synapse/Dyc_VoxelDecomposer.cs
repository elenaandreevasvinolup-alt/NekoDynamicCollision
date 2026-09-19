using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Одна выпуклая часть, полученная своим воксельным разложением.
    ///
    /// В отличие от частей нативного ядра здесь СРАЗУ известна кость: метка
    /// ставится на воксель до разрезания, поэтому часть физически не может
    /// пересечь границу кости. Это и есть «этап 3» (интеграция весов +
    /// арбитраж границы), встроенный в «этап 4» (своё разложение).
    /// </summary>
    public struct DycVoxelPiece
    {
        /// <summary>Вершины выпуклой оболочки в пространстве исходного меша.</summary>
        public Vector3[] points;
        /// <summary>Треугольники оболочки.</summary>
        public int[] triangles;
        /// <summary>Индекс кости в массиве bones, которой принадлежит часть.</summary>
        public int boneIndex;
        /// <summary>Объём оболочки, м³.</summary>
        public float volume;
        /// <summary>Сколько вокселей вошло в часть (диагностика).</summary>
        public int voxelCount;
    }

    /// <summary>
    /// Своё разложение вогнутого меша на выпуклые части — с метками костей.
    ///
    /// ЗАЧЕМ СВОЁ, ЕСЛИ ЕСТЬ V-HACD. Нативное ядро геометрическое: оно не знает
    /// про кости, поэтому режет тело по форме и выдаёт части, пересекающие
    /// суставы. Привязать такую часть к одной кости нельзя, а разрезать по
    /// границе кости — отдельная задача. Кроме того, нативное ядро работает
    /// СИНХРОННО и на полном теле может считать минуты, замораживая редактор
    /// (именно это и выглядело как «запекание вешает Unity»).
    ///
    /// ЧТО ДЕЛАЕТСЯ ЗДЕСЬ, ПО ШАГАМ:
    ///   1. Вокселизация поверхности меша (треугольник → воксели, которые он задевает).
    ///   2. Заливка: снаружи заливается «улица», всё остальное — внутренность.
    ///   3. ИНТЕГРАЦИЯ ВЕСОВ: каждый воксель голосует весами ближайших вершин,
    ///      побеждает кость с наибольшей суммой — воксель получает метку кости.
    ///   4. АРБИТРАЖ ГРАНИЦЫ: одиночные воксели-шумы перекрашиваются в метку
    ///      большинства соседей; внутренность наследует метку ближайшей поверхности.
    ///   5. Рост компонент ТОЛЬКО внутри одной метки → часть не пересекает кость.
    ///   6. Каждая компонента рекурсивно делится плоскостью через центр масс,
    ///      пока не станет достаточно выпуклой (или пока не упрётся в бюджет).
    ///   7. Каждая часть превращается в выпуклую оболочку своим Dyc_Hull.
    ///
    /// ПОЧЕМУ ЭТО НЕ ВЕШАЕТ РЕДАКТОР. У работы есть жёсткие пределы: бюджет
    /// вокселей (mesh грубее — воксель крупнее, а не «считаем вечно»), предел
    /// глубины деления, общий предел числа частей и бюджет времени. На каждом
    /// тяжёлом шаге проверяется отмена, поэтому прогресс-бар реально работает.
    /// </summary>
    public static class Dyc_VoxelDecomposer
    {
        /// <summary>Жёсткий предел воксельной сетки. Выше — это уже гигабайты памяти и минуты счёта.</summary>
        public const int HardMaxVoxels = 4_000_000;

        /// <summary>Ниже этого числа вокселей часть не имеет смысла.</summary>
        const int MinVoxelsPerPiece = 8;

        /// <summary>Отладочная трассировка деления. null в обычной работе.</summary>
        public static System.Action<string> DebugTrace;

        /// <summary>Опорные точки последней построенной оболочки. Заполняется только при DebugTrace.</summary>
        public static List<Vector3> DebugLastSupport;

        /// <summary>Сколько точек компоненты брать для проверки выпуклости (оболочка по всем вокселям не нужна).</summary>
        const int ConcavitySamples = 1500;

        // ------------------------------------------------------------------ вход

        /// <summary>
        /// Разлагает меш на выпуклые части с метками костей.
        ///
        /// verts/tris — геометрия (уже раздвинутая разделителем костей, если он
        /// включён). weights/bones — скелет. Если весов нет, разложение всё
        /// равно работает, но все части получают кость -1, и вызывающий обязан
        /// откатиться на путь «одна оболочка на кость».
        /// </summary>
        /// <summary>
        /// Разлагает меш на выпуклые части с метками костей.
        ///
        /// ВАЖНО: вместо массива Transform сюда идёт массив ПРИЗНАКОВ
        /// «кость пригодна». Это не косметика: разложение умеет считаться в
        /// фоновом потоке, а обращение к UnityEngine.Object из чужого потока
        /// (даже простое сравнение с null — оно внутри вызывает нативный код)
        /// недопустимо. Признаки готовятся на главном потоке.
        /// </summary>
        public static bool Decompose(
            Vector3[] verts, int[] tris,
            BoneWeight[] weights, bool[] boneValid,
            DycDecomposeSettings settings,
            out List<DycVoxelPiece> pieces, out string error,
            DycBakeProgress progress)
        {
            pieces = null;
            error = null;

            if (verts == null || tris == null || verts.Length < 4 || tris.Length < 12)
            {
                error = "слишком мало геометрии";
                return false;
            }

            bool hasSkeleton = weights != null && boneValid != null && boneValid.Length > 0
                               && weights.Length == verts.Length;

            // ---- 0. Габариты и шаг вокселя
            var bounds = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Length; i++) bounds.Encapsulate(verts[i]);
            Vector3 size = bounds.size;
            if (size.sqrMagnitude < 1e-12f) { error = "нулевой габарит"; return false; }

            int budget = settings.maxVoxels > 0
                ? Mathf.Clamp(settings.maxVoxels, 32_000, HardMaxVoxels)
                : 1_500_000;

            float cell = Mathf.Clamp(settings.voxelSizeMm, 0.5f, 200f) * 0.001f;
            if (cell <= 0f) cell = 0.008f;

            int nx, ny, nz;
            Coarsen(size, ref cell, budget, out nx, out ny, out nz);

            long total = (long)nx * ny * nz;
            if (total <= 0 || total > HardMaxVoxels)
            {
                error = "воксельная сетка не помещается в бюджет";
                return false;
            }

            // Начало сетки — на воксель наружу от габарита: так граница меша
            // никогда не попадает ровно на границу сетки, и заливка «улицы»
            // всегда находит выход.
            var origin = bounds.min - new Vector3(cell, cell, cell);

            // state: 0 пусто, 1 поверхность, 2 внутренность, 3 улица
            var state = new byte[total];
            var label = new int[total];       // индекс кости + 1, 0 — нет метки

            if (progress != null) progress.Report(0.02f, "вокселизация поверхности");

            // ---- 1. Вокселизация поверхности
            RasterizeSurface(verts, tris, origin, cell, nx, ny, nz, state, progress);

            if (progress != null) progress.Report(0.18f, "заливка внутренности");
            if (progress != null && progress.Stopped)
            {
                error = StoppedMessage(progress);
                return false;
            }

            // ---- 2. Улица и внутренность
            FillInterior(state, nx, ny, nz, progress);
            if (progress != null && progress.Stopped)
            {
                error = StoppedMessage(progress);
                return false;
            }

            if (progress != null) progress.Report(0.30f, "интеграция весов костей");

            // ---- 3. Метки костей: интеграция весов
            var vertexGrid = new VertexGrid(verts, cell);
            if (hasSkeleton)
            {
                float minWeight = settings.boneWeightThreshold > 0f
                    ? Mathf.Clamp01(settings.boneWeightThreshold)
                    : 0.15f;
                LabelVoxels(verts, weights, boneValid, vertexGrid, origin, cell, nx, ny, nz, state, label,
                    minWeight, progress);
            }

            // ---- 4. Арбитраж границы
            if (hasSkeleton) SmoothLabels(state, label, nx, ny, nz);
            if (hasSkeleton) GrowLabelsIntoInterior(state, label, nx, ny, nz, progress);

            if (progress != null) progress.Report(0.42f, "рост компонент");
            if (progress != null && progress.Stopped)
            {
                error = StoppedMessage(progress);
                return false;
            }

            // ---- 5-6. Компоненты и деление
            float fillRatio = settings.convexFillRatio > 0.05f
                ? Mathf.Clamp(settings.convexFillRatio, 0.4f, 0.98f)
                : 0.72f;
            int maxDepth = settings.maxSplitDepth > 0 ? Mathf.Clamp(settings.maxSplitDepth, 1, 8) : 5;
            int maxPieces = settings.maxHulls > 0 ? Mathf.Clamp(settings.maxHulls, 1, 1024) : 64;

            pieces = new List<DycVoxelPiece>(maxPieces);

            var visited = new bool[total];
            var queue = new Queue<int>(4096);
            var component = new List<int>(4096);

            int scanned = 0;
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int idx = x + nx * (y + ny * z);
                if (visited[idx]) continue;

                byte st = state[idx];
                if (st != 1 && st != 2) continue;

                scanned++;
                if ((scanned & 0xFFF) == 0 && progress != null && progress.Stopped) break;

                // Компонента связности внутри ОДНОЙ метки: граница кости
                // непроходима, поэтому часть не может пересечь сустав.
                int lbl = label[idx];
                component.Clear();
                queue.Clear();
                queue.Enqueue(idx);
                visited[idx] = true;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    Expand6(cur, x, y, z, nx, ny, nz, state, label, lbl, visited, queue);
                }

                if (component.Count < MinVoxelsPerPiece) continue;

                if (pieces.Count >= maxPieces)
                {
                    // Бюджет частей исчерпан: оставшиеся компоненты берём как есть.
                    AcceptComponent(component, label[idx] - 1, origin, cell, nx, ny, nz, vertexGrid, verts,
                        settings.projectHullVertices, pieces);
                    continue;
                }

                Split(component, label[idx] - 1, 0, maxDepth, fillRatio, maxPieces,
                    origin, cell, nx, ny, nz, vertexGrid, verts, settings.projectHullVertices,
                    pieces, progress);
            }

            if (pieces.Count == 0)
            {
                error = progress != null && progress.Stopped
                    ? StoppedMessage(progress)
                    : "не удалось построить ни одной части";
                return false;
            }

            if (progress != null) progress.Report(0.70f, "части готовы: " + pieces.Count);
            return true;
        }

        static string StoppedMessage(DycBakeProgress p)
        {
            if (p == null) return "остановлено";
            return p.timedOut ? "остановлено по бюджету времени" : "отменено пользователем";
        }

        // ------------------------------------------------------------------ сетка

        /// <summary>
        /// Подгоняет шаг вокселя под бюджет. Точность здесь — управляемый
        /// компромисс: лучше посчитать за секунды с вокселем 10 мм, чем
        /// заморозить редактор на минуты с 6 мм.
        /// </summary>
        static void Coarsen(Vector3 size, ref float cell, int budget, out int nx, out int ny, out int nz)
        {
            nx = ny = nz = 4;
            for (int guard = 0; guard < 64; guard++)
            {
                nx = Mathf.Max(4, Mathf.CeilToInt(size.x / cell) + 3);
                ny = Mathf.Max(4, Mathf.CeilToInt(size.y / cell) + 3);
                nz = Mathf.Max(4, Mathf.CeilToInt(size.z / cell) + 3);

                long total = (long)nx * ny * nz;
                if (total <= budget) return;

                // Шаг растёт как кубический корень: сразу попасть в бюджет,
                // а не ползти по 25% за итерацию.
                float scale = Mathf.Pow(total / (float)budget, 1f / 3f);
                cell *= Mathf.Clamp(scale, 1.05f, 2f);
            }
        }

        static int Index(int x, int y, int z, int nx, int ny)
        {
            return x + nx * (y + ny * z);
        }

        static void Expand6(int idx, int x, int y, int z, int nx, int ny, int nz,
            byte[] state, int[] label, int want, bool[] visited, Queue<int> queue)
        {
            // Индексы соседей пересчитываются из idx, а не из x/y/z цикла: в
            // компоненте они меняются, а исходные координаты — нет.
            int vx = idx % nx;
            int vy = (idx / nx) % ny;
            int vz = idx / (nx * ny);

            for (int d = 0; d < 6; d++)
            {
                int cx = vx, cy = vy, cz = vz;
                switch (d)
                {
                    case 0: cx--; break;
                    case 1: cx++; break;
                    case 2: cy--; break;
                    case 3: cy++; break;
                    case 4: cz--; break;
                    default: cz++; break;
                }

                if (cx < 0 || cy < 0 || cz < 0 || cx >= nx || cy >= ny || cz >= nz) continue;

                int ni = Index(cx, cy, cz, nx, ny);
                if (visited[ni]) continue;

                byte st = state[ni];
                if (st != 1 && st != 2) continue;
                if (label[ni] != want) continue;

                visited[ni] = true;
                queue.Enqueue(ni);
            }
        }

        // ------------------------------------------------------------------ шаг 1

        static void RasterizeSurface(Vector3[] verts, int[] tris, Vector3 origin, float cell,
            int nx, int ny, int nz, byte[] state, DycBakeProgress progress)
        {
            float radius = cell * 0.87f;             // половина диагонали вокселя
            float radiusSq = radius * radius;

            int triCount = tris.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                if ((t & 0x7F) == 0 && progress != null && progress.Stopped) return;

                int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0) continue;
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;

                Vector3 a = verts[i0], b = verts[i1], c = verts[i2];

                Vector3 lo = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 hi = Vector3.Max(a, Vector3.Max(b, c));

                int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - origin.x) / cell) - 1, 0, nx - 1);
                int x1 = Mathf.Clamp(Mathf.FloorToInt((hi.x - origin.x) / cell) + 1, 0, nx - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - origin.y) / cell) - 1, 0, ny - 1);
                int y1 = Mathf.Clamp(Mathf.FloorToInt((hi.y - origin.y) / cell) + 1, 0, ny - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - origin.z) / cell) - 1, 0, nz - 1);
                int z1 = Mathf.Clamp(Mathf.FloorToInt((hi.z - origin.z) / cell) + 1, 0, nz - 1);

                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                {
                    int rowBase = nx * (y + ny * z);
                    for (int x = x0; x <= x1; x++)
                    {
                        int idx = x + rowBase;
                        if (state[idx] == 1) continue;

                        Vector3 p = origin + new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);
                        if (PointTriangleDistanceSq(p, a, b, c) <= radiusSq) state[idx] = 1;
                    }
                }
            }
        }

        /// <summary>Квадрат расстояния от точки до треугольника (Real-Time Collision Detection, упрощённо).</summary>
        static float PointTriangleDistanceSq(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return (p - (a + v * ab)).sqrMagnitude;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return (p - (a + w * ac)).sqrMagnitude;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return (p - (b + w * (c - b))).sqrMagnitude;
            }

            float denom = va + vb + vc;
            if (Mathf.Abs(denom) < 1e-12f) return ap.sqrMagnitude;
            float vv = vb / denom, ww = vc / denom;
            return (p - (a + ab * vv + ac * ww)).sqrMagnitude;
        }

        // ------------------------------------------------------------------ шаг 2

        static void FillInterior(byte[] state, int nx, int ny, int nz, DycBakeProgress progress)
        {
            var queue = new Queue<int>(4096);

            // Старт заливки — вся внешняя оболочка сетки, кроме поверхности.
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                if (x != 0 && y != 0 && z != 0 && x != nx - 1 && y != ny - 1 && z != nz - 1) continue;

                int idx = Index(x, y, z, nx, ny);
                if (state[idx] == 1 || state[idx] == 3) continue;
                state[idx] = 3;
                queue.Enqueue(idx);
            }

            int popped = 0;
            while (queue.Count > 0)
            {
                if ((++popped & 0x1FFF) == 0 && progress != null && progress.Stopped) return;

                int idx = queue.Dequeue();
                int vx = idx % nx;
                int vy = (idx / nx) % ny;
                int vz = idx / (nx * ny);

                for (int d = 0; d < 6; d++)
                {
                    int cx = vx, cy = vy, cz = vz;
                    switch (d)
                    {
                        case 0: cx--; break;
                        case 1: cx++; break;
                        case 2: cy--; break;
                        case 3: cy++; break;
                        case 4: cz--; break;
                        default: cz++; break;
                    }
                    if (cx < 0 || cy < 0 || cz < 0 || cx >= nx || cy >= ny || cz >= nz) continue;

                    int ni = Index(cx, cy, cz, nx, ny);
                    if (state[ni] != 0) continue;
                    state[ni] = 3;
                    queue.Enqueue(ni);
                }
            }

            // Всё, что не улица и не поверхность, — внутренность.
            for (int i = 0; i < state.Length; i++)
                if (state[i] == 0) state[i] = 2;
        }

        // ------------------------------------------------------------------ шаг 3

        /// <summary>
        /// Интеграция весов: воксель голосует весами ВСЕХ вершин в радиусе,
        /// а не одной ближайшей. Это и есть «интеграция», а не «взяли
        /// доминирующую кость вершины»: на стыке костей голос честно делится.
        /// </summary>
        static void LabelVoxels(Vector3[] verts, BoneWeight[] weights, bool[] boneValid,
            VertexGrid grid, Vector3 origin, float cell, int nx, int ny, int nz,
            byte[] state, int[] label, float minWeight, DycBakeProgress progress)
        {
            // Радиус голосования — ПОЛТОРА вокселя, а не два с лишним.
            //
            // Большой радиус был главной причиной «заглатывания» соседней
            // кости: на плече воксель собирал голоса вершин и плеча, и руки, и
            // побеждала та, чей вес чуть больше. Плечо и предплечье — разные
            // кости, и граница между ними должна быть резкой. Полтора вокселя
            // достаточно, чтобы воксель не остался без голосов вовсе, но мало,
            // чтобы тянуть вес через сустав.
            float radius = cell * 1.5f;
            float radiusSq = radius * radius;

            var voteBone = new List<int>(8);
            var voteValue = new List<float>(8);
            var candidates = new List<int>(32);

            int done = 0;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] != 1) continue;
                if ((++done & 0x1FF) == 0 && progress != null && progress.Stopped) return;

                int x = i % nx;
                int y = (i / nx) % ny;
                int z = i / (nx * ny);
                Vector3 p = origin + new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);

                candidates.Clear();
                grid.Near(p, radius, candidates);
                if (candidates.Count == 0) continue;

                voteBone.Clear();
                voteValue.Clear();

                for (int c = 0; c < candidates.Count; c++)
                {
                    int v = candidates[c];
                    float d2 = (p - verts[v]).sqrMagnitude;
                    if (d2 > radiusSq) continue;

                    float spatial = 1f / (d2 + 1e-8f);
                    var bw = weights[v];

                    // Веса ниже порога не голосуют. Без этого воксель на стыке
                    // получал по чуть-чуть от четырёх костей сразу, и метка
                    // решалась шумом, а не реальной принадлежностью.
                    if (bw.weight0 >= minWeight) AddVote(voteBone, voteValue, bw.boneIndex0, bw.weight0 * spatial);
                    if (bw.weight1 >= minWeight) AddVote(voteBone, voteValue, bw.boneIndex1, bw.weight1 * spatial);
                    if (bw.weight2 >= minWeight) AddVote(voteBone, voteValue, bw.boneIndex2, bw.weight2 * spatial);
                    if (bw.weight3 >= minWeight) AddVote(voteBone, voteValue, bw.boneIndex3, bw.weight3 * spatial);
                }

                int bestBone = -1;
                float bestVote = 0f;
                for (int k = 0; k < voteBone.Count; k++)
                {
                    int b = voteBone[k];
                    if (b < 0 || b >= boneValid.Length) continue;
                    if (!boneValid[b]) continue;
                    if (voteValue[k] <= bestVote) continue;
                    bestVote = voteValue[k];
                    bestBone = b;
                }

                if (bestBone >= 0) label[i] = bestBone + 1;
            }
        }

        static void AddVote(List<int> bone, List<float> value, int index, float weight)
        {
            if (index < 0 || weight <= 0f) return;
            for (int i = 0; i < bone.Count; i++)
            {
                if (bone[i] != index) continue;
                value[i] += weight;
                return;
            }
            bone.Add(index);
            value.Add(weight);
        }

        // ------------------------------------------------------------------ шаг 4

        /// <summary>
        /// Арбитраж границы: воксель, у которого нет ни одного соседа со своей
        /// меткой, но есть явное большинство чужой, перекрашивается. Убирает
        /// одиночные воксели-шум, из-за которых рождались части из трёх штук.
        /// </summary>
        static void SmoothLabels(byte[] state, int[] label, int nx, int ny, int nz)
        {
            var counts = new Dictionary<int, int>(8);
            for (int z = 1; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
            for (int x = 1; x < nx - 1; x++)
            {
                int idx = Index(x, y, z, nx, ny);
                byte st = state[idx];
                if (st != 1 && st != 2) continue;

                int own = label[idx];
                if (own == 0) continue;

                counts.Clear();
                int same = 0;
                for (int d = 0; d < 6; d++)
                {
                    int cx = x, cy = y, cz = z;
                    switch (d)
                    {
                        case 0: cx--; break;
                        case 1: cx++; break;
                        case 2: cy--; break;
                        case 3: cy++; break;
                        case 4: cz--; break;
                        default: cz++; break;
                    }

                    int ni = Index(cx, cy, cz, nx, ny);
                    int nl = label[ni];
                    if (nl == 0) continue;
                    if (nl == own) { same++; continue; }

                    counts.TryGetValue(nl, out int c);
                    counts[nl] = c + 1;
                }

                if (same > 0) continue;   // своя метка рядом — воксель на месте

                int majority = 0, best = 0;
                foreach (var kv in counts)
                {
                    if (kv.Value <= best) continue;
                    best = kv.Value;
                    majority = kv.Key;
                }

                if (best >= 4) label[idx] = majority;
            }
        }

        /// <summary>
        /// Все твёрдые воксели без метки наследуют метку ближайшего помеченного.
        ///
        /// Раньше заливка шла только по внутренности, и поверхностные воксели,
        /// которым не досталось голосов (например на сглаженном стыке, где все
        /// веса оказались ниже порога), оставались без метки. Такие воксели
        /// собирались в «части без кости» и отбрасывались — в отчёте это было
        /// «частей без кости 4», а на деле это дырки в коллизии. Теперь метка
        /// распространяется по ВСЕМУ объёму, и без метки не остаётся ничего.
        /// </summary>
        static void GrowLabelsIntoInterior(byte[] state, int[] label, int nx, int ny, int nz,
            DycBakeProgress progress)
        {
            var queue = new Queue<int>(4096);

            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] != 1 && state[i] != 2) continue;
                if (label[i] == 0) continue;
                queue.Enqueue(i);
            }

            int popped = 0;
            while (queue.Count > 0)
            {
                if ((++popped & 0x1FFF) == 0 && progress != null && progress.Stopped) return;

                int idx = queue.Dequeue();
                int lbl = label[idx];
                if (lbl == 0) continue;

                int vx = idx % nx;
                int vy = (idx / nx) % ny;
                int vz = idx / (nx * ny);

                for (int d = 0; d < 6; d++)
                {
                    int cx = vx, cy = vy, cz = vz;
                    switch (d)
                    {
                        case 0: cx--; break;
                        case 1: cx++; break;
                        case 2: cy--; break;
                        case 3: cy++; break;
                        case 4: cz--; break;
                        default: cz++; break;
                    }
                    if (cx < 0 || cy < 0 || cz < 0 || cx >= nx || cy >= ny || cz >= nz) continue;

                    int ni = Index(cx, cy, cz, nx, ny);
                    if (state[ni] != 1 && state[ni] != 2) continue;
                    if (label[ni] != 0) continue;
                    label[ni] = lbl;
                    queue.Enqueue(ni);
                }
            }
        }

        // ------------------------------------------------------------------ шаги 5-6

        /// <summary>
        /// Рекурсивное деление компоненты, пока она не станет достаточно
        /// выпуклой. Мера выпуклости — заполнение: объём вокселей / объём их
        /// выпуклой оболочки. Ноль — идеально выпукло.
        /// </summary>
        static void Split(List<int> voxels, int boneIndex, int depth, int maxDepth, float fillRatio,
            int maxPieces, Vector3 origin, float cell, int nx, int ny, int nz,
            VertexGrid grid, Vector3[] verts, bool project,
            List<DycVoxelPiece> pieces, DycBakeProgress progress)
        {
            if (voxels.Count < MinVoxelsPerPiece) return;
            if (pieces.Count >= maxPieces)
            {
                AcceptComponent(voxels, boneIndex, origin, cell, nx, ny, nz, grid, verts, project, pieces);
                return;
            }

            if (progress != null && progress.Stopped) return;

            var centers = SampleCenters(voxels, origin, cell, nx, ny, ConcavitySamples);
            float fill = FillRatio(centers, cell, voxels.Count * cell * cell * cell);

            if (fill >= fillRatio || depth >= maxDepth)
            {
                AcceptComponent(voxels, boneIndex, origin, cell, nx, ny, nz, grid, verts, project, pieces);
                return;
            }

            // Плоскость деления — через центр масс, нормаль — главная ось.
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < centers.Count; i++) centroid += centers[i];
            centroid /= Mathf.Max(1, centers.Count);

            Vector3 axis = PrincipalAxis(centers, centroid);
            var left = new List<int>(voxels.Count / 2);
            var right = new List<int>(voxels.Count / 2);

            Partition(voxels, centroid, axis, nx, ny, origin, cell, left, right);

            if (left.Count == 0 || right.Count == 0)
            {
                // Вырожденный случай (все воксели на плоскости): делим по
                // наибольшей стороне габарита на медиане — это всегда работает.
                if (!PartitionByExtent(voxels, nx, ny, origin, cell, left, right)
                    || left.Count == 0 || right.Count == 0)
                {
                    AcceptComponent(voxels, boneIndex, origin, cell, nx, ny, nz, grid, verts, project, pieces);
                    return;
                }
            }

            Split(left, boneIndex, depth + 1, maxDepth, fillRatio, maxPieces,
                origin, cell, nx, ny, nz, grid, verts, project, pieces, progress);
            Split(right, boneIndex, depth + 1, maxDepth, fillRatio, maxPieces,
                origin, cell, nx, ny, nz, grid, verts, project, pieces, progress);
        }

        static void Partition(List<int> voxels, Vector3 centroid, Vector3 axis,
            int nx, int ny, Vector3 origin, float cell, List<int> left, List<int> right)
        {
            for (int i = 0; i < voxels.Count; i++)
            {
                int idx = voxels[i];
                int x = idx % nx;
                int y = (idx / nx) % ny;
                int z = idx / (nx * ny);
                Vector3 c = origin + new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);

                if (Vector3.Dot(c - centroid, axis) >= 0f) right.Add(idx);
                else left.Add(idx);
            }
        }

        static bool PartitionByExtent(List<int> voxels, int nx, int ny, Vector3 origin, float cell,
            List<int> left, List<int> right)
        {
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < voxels.Count; i++)
            {
                Vector3 c = CenterOf(voxels[i], nx, ny, origin, cell);
                lo = Vector3.Min(lo, c);
                hi = Vector3.Max(hi, c);
            }

            Vector3 size = hi - lo;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            float mid = (axis == 0 ? lo.x + hi.x : axis == 1 ? lo.y + hi.y : lo.z + hi.z) * 0.5f;

            for (int i = 0; i < voxels.Count; i++)
            {
                Vector3 c = CenterOf(voxels[i], nx, ny, origin, cell);
                float v = axis == 0 ? c.x : (axis == 1 ? c.y : c.z);
                if (v >= mid) right.Add(voxels[i]);
                else left.Add(voxels[i]);
            }

            return true;
        }

        static Vector3 CenterOf(int idx, int nx, int ny, Vector3 origin, float cell)
        {
            int x = idx % nx;
            int y = (idx / nx) % ny;
            int z = idx / (nx * ny);
            return origin + new Vector3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);
        }

        /// <summary>
        /// Прореживает воксели до предела точек. Оболочка по прореженному
        /// набору отличается от оболочки по полному не сильнее шага сетки, а
        /// стоит на порядки дешевле: у Dyc_Hull дедупликация квадратичная, и
        /// скармливать ей десятки тысяч углов вокселей нельзя — это и есть
        /// «зависло на запекании».
        /// </summary>
        static List<Vector3> SampleCenters(List<int> voxels, Vector3 origin, float cell, int nx, int ny, int maxPoints)
        {
            if (voxels.Count == 0) return new List<Vector3>();
            int stride = Mathf.Max(1, voxels.Count / Mathf.Max(8, maxPoints));

            var list = new List<Vector3>(Mathf.Min(voxels.Count, maxPoints) + 1);
            for (int i = 0; i < voxels.Count; i += stride)
                list.Add(CenterOf(voxels[i], nx, ny, origin, cell));
            return list;
        }

        /// <summary>
        /// Направления опорных точек: шесть осей плюс равномерная сфера.
        ///
        /// Зачем не отдавать Dyc_Hull сразу центры вокселей. Он построен на
        /// инкрементальном алгоритме и рассчитан на РАЗРЕЖЕННЫЙ набор вершин
        /// меша. Регулярная воксельная сетка — худший для него вход: тысячи
        /// компланарных точек, и на них он либо честно упирается в предел
        /// граней и отказывает, либо выдаёт «оболочку» в сотни почти
        /// компланарных вершин. Опорные точки снимают обе проблемы: их не
        /// больше числа направлений, они заведомо не компланарны, и оболочка
        /// по ним укладывается в лимит PhysX.
        /// </summary>
        static readonly Vector3[] SupportDirections = BuildSupportDirections();

        static Vector3[] BuildSupportDirections()
        {
            var list = new List<Vector3>(96)
            {
                Vector3.right, Vector3.left,
                Vector3.up, Vector3.down,
                Vector3.forward, Vector3.back
            };

            // 156 направлений + 6 осей. Опорная оболочка — ВНУТРЕННЯЯ оценка
            // (она проходит через снятые опорные точки, а не по касательным
            // плоскостям), поэтому чем гуще направления, тем меньше она
            // занижает объём. При 90 направлениях занижение на вытянутых
            // впадинах доходило до единиц процентов, и вогнутость
            // недооценивалась — деталь не дробилась. 162 точки всё ещё дают
            // оболочку заметно ниже лимита PhysX в 255 вершин.
            const int n = 156;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float th = golden * i;
                list.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
            }

            return list.ToArray();
        }

        /// <summary>
        /// Экстремальные точки по каждому направлению. Их не больше, чем
        /// направлений.
        ///
        /// Точка сдвигается из ЦЕНТРА вокселя к его дальнему УГЛУ вдоль
        /// направления. Это принципиально: тело состоит не из центров, а из
        /// кубиков, и опора воксельного объёма — именно угол. Раньше здесь
        /// брались центры, а недостающее компенсировалось радиальным
        /// расширением всей оболочки на полвокселя — и это раздувало её
        /// заметно сильнее, чем нужно, потому что расширение по радиусу
        /// вытягивает углы по диагонали. Взятие угла даёт точную опору без
        /// всякого расширения.
        /// </summary>
        static List<Vector3> SupportPoints(List<Vector3> centers, Vector3[] dirs, float half)
        {
            var result = new List<Vector3>(dirs.Length);

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 dir = dirs[d];
                int best = -1;
                float bestDot = float.MinValue;

                for (int i = 0; i < centers.Count; i++)
                {
                    float dot = Vector3.Dot(centers[i], dir);
                    if (dot > bestDot) { bestDot = dot; best = i; }
                }
                if (best < 0) continue;

                Vector3 p = centers[best] + new Vector3(
                    dir.x > 0.01f ? half : (dir.x < -0.01f ? -half : 0f),
                    dir.y > 0.01f ? half : (dir.y < -0.01f ? -half : 0f),
                    dir.z > 0.01f ? half : (dir.z < -0.01f ? -half : 0f));

                bool dup = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if ((result[i] - p).sqrMagnitude >= 1e-12f) continue;
                    dup = true;
                    break;
                }
                if (!dup) result.Add(p);
            }

            return result;
        }

        /// <summary>
        /// Оболочка по опорным углам вокселей. Никакого расширения: опорные
        /// точки и так лежат на границе воксельного объёма, а лишний «запас»
        /// только раздувал оболочки.
        /// </summary>
        static bool BuildSupportHull(List<Vector3> centers, float cell, out DycHullResult hull)
        {
            hull = default;
            if (centers.Count < 4) return false;

            var support = SupportPoints(centers, SupportDirections, cell * 0.5f);
            if (support.Count < 4) return false;
            if (DebugTrace != null) DebugLastSupport = support;

            // Допуск — малая доля вокселя. Больше не нужно: настоящей причиной
            // «раздутого» объёма был не допуск, а разрыв оболочки из-за
            // пропущенных тонких граней конуса (см. Dyc_Hull.Build).
            if (!Dyc_Hull.Build(support, out hull, cell * 0.05f)) return false;
            if (hull.points == null || hull.points.Length < 4) return false;
            return true;
        }

        static float FillRatio(List<Vector3> centers, float cell, float voxelVolume)
        {
            if (!BuildSupportHull(centers, cell, out var hull))
            {
                if (DebugTrace != null) DebugTrace("fill: hull FAILED");
                return 1f;
            }
            if (hull.volume <= 1e-9f)
            {
                if (DebugTrace != null) DebugTrace("fill: hull volume ~0");
                return 1f;
            }

            float r = Mathf.Clamp01(voxelVolume / hull.volume);
            if (DebugTrace != null)
            {
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < hull.points.Length; i++)
                {
                    lo = Vector3.Min(lo, hull.points[i]);
                    hi = Vector3.Max(hi, hull.points[i]);
                }
                Vector3 cLo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 cHi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < centers.Count; i++)
                {
                    cLo = Vector3.Min(cLo, centers[i]);
                    cHi = Vector3.Max(cHi, centers[i]);
                }
                DebugTrace($"fill: vox={voxelVolume:E3} hull={hull.volume:E3} pts={hull.points.Length} r={r:F3}\n" +
                           $"      hullSize={hi - lo} centersSize={cHi - cLo} cell={cell:F4}");
            }
            return r;
        }

        static Vector3 PrincipalAxis(List<Vector3> pts, Vector3 centroid)
        {
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - centroid;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            Vector3 v = new Vector3(0.577f, 0.577f, 0.577f);
            for (int i = 0; i < 24; i++)
            {
                var nv = new Vector3(
                    (float)(xx * v.x + xy * v.y + xz * v.z),
                    (float)(xy * v.x + yy * v.y + yz * v.z),
                    (float)(xz * v.x + yz * v.y + zz * v.z));

                if (nv.sqrMagnitude < 1e-20f) return Vector3.right;
                v = nv.normalized;
            }
            return v;
        }

        // ------------------------------------------------------------------ шаг 7

        static void AcceptComponent(List<int> voxels, int boneIndex,
            Vector3 origin, float cell, int nx, int ny, int nz,
            VertexGrid grid, Vector3[] verts, bool project,
            List<DycVoxelPiece> pieces)
        {
            if (voxels.Count < MinVoxelsPerPiece) return;

            var centers = SampleCenters(voxels, origin, cell, nx, ny, ConcavitySamples);
            if (centers.Count < 4) return;

            if (!BuildSupportHull(centers, cell, out var hull)) return;
            if (hull.points == null || hull.points.Length < 4) return;

            // Проекция на поверхность меша делается ПОСЛЕ расширения: вершина
            // оболочки, у которой рядом есть настоящая вершина меша, прилипает
            // к ней. Иначе расширение отклеило бы оболочку от модели обратно.
            if (project && verts != null)
            {
                float radius = cell * 1.8f;
                float radiusSq = radius * radius;
                var snapped = new List<Vector3>(hull.points.Length);
                for (int i = 0; i < hull.points.Length; i++)
                {
                    int nearest = grid.Nearest(hull.points[i]);
                    if (nearest >= 0)
                    {
                        float d2 = (hull.points[i] - verts[nearest]).sqrMagnitude;
                        snapped.Add(d2 <= radiusSq ? verts[nearest] : hull.points[i]);
                    }
                    else snapped.Add(hull.points[i]);
                }

                if (Dyc_Hull.Build(snapped, out var projected, 1e-5f) && projected.points != null
                    && projected.points.Length >= 4)
                    hull = projected;
            }

            if (hull.triangles == null || hull.triangles.Length < 12) return;

            pieces.Add(new DycVoxelPiece
            {
                points = hull.points,
                triangles = hull.triangles,
                boneIndex = boneIndex,
                volume = hull.volume,
                voxelCount = voxels.Count
            });
        }

        // ------------------------------------------------------------------ вспомогательное

        /// <summary>
        /// Сетка вершин для поиска ближайших. Отдельная от запекателя, потому
        /// что здесь шаг ячейки задаётся РАЗМЕРОМ ВОКСЕЛЯ: запрос идёт в
        /// масштабе воксельной сетки, и это единственный правильный масштаб.
        /// </summary>
        class VertexGrid
        {
            readonly Vector3[] _vertices;
            readonly float _cell;
            readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            public VertexGrid(Vector3[] vertices, float cell)
            {
                _vertices = vertices;
                _cell = Mathf.Max(cell, 1e-5f);

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

            /// <summary>Все вершины в кубе радиуса reach вокруг точки.</summary>
            public void Near(Vector3 p, float reach, List<int> result)
            {
                int span = Mathf.Clamp(Mathf.CeilToInt(reach / _cell), 1, 3);
                int cx = Mathf.FloorToInt(p.x / _cell);
                int cy = Mathf.FloorToInt(p.y / _cell);
                int cz = Mathf.FloorToInt(p.z / _cell);

                for (int dx = -span; dx <= span; dx++)
                for (int dy = -span; dy <= span; dy++)
                for (int dz = -span; dz <= span; dz++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var list)) continue;
                    result.AddRange(list);
                }
            }

            public int Nearest(Vector3 p)
            {
                int best = -1;
                float bestSq = float.MaxValue;

                int cx = Mathf.FloorToInt(p.x / _cell);
                int cy = Mathf.FloorToInt(p.y / _cell);
                int cz = Mathf.FloorToInt(p.z / _cell);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        int v = list[i];
                        float d = (p - _vertices[v]).sqrMagnitude;
                        if (d >= bestSq) continue;
                        bestSq = d;
                        best = v;
                    }
                }

                return best;
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
