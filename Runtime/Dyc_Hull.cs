using System.Collections.Generic;
using UnityEngine;

// Пространство имён РАНТАЙМА, хотя файл исторически лежал в Editor.
//
// Причина переноса: сборка выпуклой оболочки нужна не только запеканию.
// «Живое» обновление коллайдеров в рантайме
// пересобирает оболочки из текущей позы скелета, и делать это через
// Editor-сборку невозможно. Код здесь чистый: только BCL и UnityEngine, ни
// одного обращения к UnityEditor, поэтому перенос ничего не потянул за собой.
//
// Editor-код лежит в NekoDynamicCollision.EditorTools и видит эти типы без
// using — поиск имени идёт наружу по пространствам имён.
namespace NekoDynamicCollision
{
    public struct DycHullFace
    {
        public int a, b, c;
        public Vector3 normal;
    }

    public struct DycHullResult
    {
        public bool ok;

        /// <summary>Вершины выпуклой оболочки (подмножество входного набора точек).</summary>
        public Vector3[] points;
        /// <summary>Индексы треугольников, внешняя ориентация CCW.</summary>
        public int[] triangles;
        /// <summary>Рёбра после объединения копланарных граней, хранятся парами: 0-1, 2-3…</summary>
        public Vector3[] edges;

        /// <summary>
        /// Нормали граней, смежных с каждым ребром, по две на ребро (n0, n1).
        /// Нулевая нормаль означает границу поверхности — такое ребро видно
        /// всегда. Нужны, чтобы гизмо не рисовал рёбра, повёрнутые от камеры:
        /// у выпуклой оболочки это и есть скрытие невидимых линий.
        /// </summary>
        public Vector3[] edgeNormals;
        /// <summary>Плоскости граней: xyz — нормаль, w = -d; внутри выполняется dot(n, p) + w &lt;= 0.</summary>
        public Vector4[] planes;

        public Vector3 center;
        public float volume;
        public float surfaceArea;
    }

    /// <summary>
    /// Инкрементальная выпуклая оболочка. Нужна ровно для трёх вещей, и ни одна не требует PhysX:
    ///   · объём (Auto Mass from Density)
    ///   · рёбра после объединения копланарных граней (gizmo в Editor рисует чистый контур T-pose, а не кучу диагоналей)
    ///   · плоскости граней (диагностика покрытия: тест точки внутри оболочки)
    ///
    /// Сам коллайдер по-прежнему готовит встроенный convex cooking Unity, здесь в рантайме не участвует.
    /// Набор точек маленький (≤ 250 точек на оболочку), поэтому O(n·F) вполне достаточно.
    /// </summary>
    public static class Dyc_Hull
    {
        const int MaxFaces = 4096;

        public static bool Build(IList<Vector3> input, out DycHullResult result, float eps = 1e-6f)
        {
            result = default;

            var pts = Dedupe(input, eps);
            if (pts.Count < 4) return false;

            // ---- Начальный тетраэдр: берём экстремумы вдоль осей, это устойчивее, чем только min-x
            int i0 = ExtremeIndex(pts, Vector3.right);
            int i1 = FarthestFromPoint(pts, pts[i0], out float d1);
            if (d1 < eps) return false;

            int i2 = FarthestFromLine(pts, pts[i0], pts[i1], out float d2);
            if (d2 < eps) return false;

            int i3 = FarthestFromPlane(pts, pts[i0], pts[i1], pts[i2], out float d3);
            if (d3 < eps) return false;

            var faces = new List<DycHullFace>(64);
            Vector3 tetCenter = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) * 0.25f;
            AddFace(faces, pts, i0, i1, i2, tetCenter);
            AddFace(faces, pts, i0, i2, i3, tetCenter);
            AddFace(faces, pts, i0, i3, i1, tetCenter);
            AddFace(faces, pts, i1, i3, i2, tetCenter);

            var used = new HashSet<int> { i0, i1, i2, i3 };

            // ---- Поглощаем внешние точки по одной
            var visible = new List<int>(32);
            var horizon = new List<Vector2Int>(32);
            var horizonFallback = new List<Vector3>(32);
            var edgeOwner = new Dictionary<long, int>(64);
            var edgeDir = new Dictionary<long, Vector2Int>(64);
            var edgeNormal = new Dictionary<long, Vector3>(64);

            for (int p = 0; p < pts.Count; p++)
            {
                if (used.Contains(p)) continue;

                visible.Clear();
                for (int f = 0; f < faces.Count; f++)
                {
                    if (Vector3.Dot(faces[f].normal, pts[p] - pts[faces[f].a]) > eps)
                        visible.Add(f);
                }
                if (visible.Count == 0) continue;

                // Горизонт: НЕориентированные рёбра, принадлежащие ровно одной
                // видимой грани.
                //
                // Считать направленные рёбра здесь — ошибка, и она была. Две
                // смежные видимые грани проходят общее ребро в ПРОТИВОПОЛОЖНЫХ
                // направлениях, поэтому направленный счёт давал для него две
                // разные записи по единице, ребро дважды попадало в горизонт и
                // порождало две наложенные грани конуса. На выпуклой оболочке
                // из восьми углов это не проявлялось (видимой всегда оказывалась
                // одна грань), а на наборе с компланарными точками — например на
                // воксельной сетке — объём оболочки раздувался в разы, и по нему
                // ломался весь расчёт вогнутости.
                //
                // Направление ребра сохраняется от первой видимой грани: по нему
                // строится новая грань конуса, и ориентация остаётся наружу.
                edgeOwner.Clear();
                edgeDir.Clear();
                edgeNormal.Clear();
                for (int v = 0; v < visible.Count; v++)
                {
                    DycHullFace f = faces[visible[v]];
                    RegisterEdge(edgeOwner, edgeDir, edgeNormal, f.a, f.b, f.normal);
                    RegisterEdge(edgeOwner, edgeDir, edgeNormal, f.b, f.c, f.normal);
                    RegisterEdge(edgeOwner, edgeDir, edgeNormal, f.c, f.a, f.normal);
                }

                horizon.Clear();
                horizonFallback.Clear();
                foreach (var kv in edgeOwner)
                {
                    if (kv.Value != 1) continue;
                    if (!edgeDir.TryGetValue(kv.Key, out var dir)) continue;
                    horizon.Add(dir);
                    horizonFallback.Add(edgeNormal.TryGetValue(kv.Key, out var fn) ? fn : Vector3.zero);
                }

                // Удаляем видимые грани (в обратном порядке)
                visible.Sort();
                for (int v = visible.Count - 1; v >= 0; v--)
                    faces.RemoveAt(visible[v]);

                if (faces.Count + horizon.Count > MaxFaces) return false;

                // Наращиваем конус от горизонта.
                //
                // ЗДЕСЬ НЕЛЬЗЯ ПРОПУСКАТЬ «СЛИШКОМ ТОНКИЕ» ГРАНИ, и это была
                // вторая настоящая ошибка. Раньше стояла проверка
                // n.sqrMagnitude < eps*eps с пропуском, и она сравнивала
                // ВЕЛИЧИНУ, пропорциональную КВАДРАТУ ПЛОЩАДИ, с квадратом
                // длины. У конуса от далёкой точки к близкому ребру площадь
                // мала по построению, такие грани пропускались пачками — и
                // оболочка оставалась РАЗОМКНУТОЙ. На замкнутой сетке объём
                // считается по теореме о дивергенции, поэтому разрыв давал
                // объём, больший объёма габарита (чего у выпуклой оболочки быть
                // не может), а вогнутость считалась по мусору.
                //
                // Вырожденная грань в объём вклада не даёт, поэтому её можно
                // спокойно добавить и отсеять только на выводе. Замкнутость
                // важнее красоты: без неё неверно всё остальное.
                for (int h = 0; h < horizon.Count; h++)
                {
                    int a = horizon[h].x, b = horizon[h].y;
                    var n = Vector3.Cross(pts[b] - pts[a], pts[p] - pts[a]);

                    if (n.sqrMagnitude > 1e-24f)
                    {
                        n.Normalize();
                    }
                    else if (horizonFallback[h].sqrMagnitude > 1e-12f)
                    {
                        // Точка p оказалась на одной прямой с ребром. Берём
                        // нормаль грани, которой ребро принадлежало: она
                        // заведомо смотрит наружу.
                        n = horizonFallback[h];
                    }
                    else
                    {
                        n = (pts[p] - (pts[a] + pts[b]) * 0.5f).normalized;
                    }

                    faces.Add(new DycHullFace { a = a, b = b, c = p, normal = n });
                }

                used.Add(p);
            }

            if (faces.Count < 4) return false;

            // ---- Вывод
            var usedVerts = new Dictionary<int, int>(faces.Count);
            var outPts = new List<Vector3>(faces.Count);
            var outTris = new List<int>(faces.Count * 3);
            var planes = new List<Vector4>(faces.Count);

            for (int f = 0; f < faces.Count; f++)
            {
                DycHullFace face = faces[f];

                // Вырожденные грани нужны были для замкнутости при построении,
                // но в результат им нельзя: они ломают и отрисовку рёбер, и
                // запекание. В объём они не вносят ничего, поэтому отсев здесь
                // его не меняет.
                if (Vector3.Cross(pts[face.b] - pts[face.a], pts[face.c] - pts[face.a]).sqrMagnitude < 1e-24f)
                    continue;

                outTris.Add(Idx(usedVerts, outPts, pts, face.a));
                outTris.Add(Idx(usedVerts, outPts, pts, face.b));
                outTris.Add(Idx(usedVerts, outPts, pts, face.c));
                planes.Add(new Vector4(face.normal.x, face.normal.y, face.normal.z, -Vector3.Dot(face.normal, pts[face.a])));
            }

            if (outPts.Count < 4) return false;

            result.ok = true;
            result.points = outPts.ToArray();
            result.triangles = outTris.ToArray();
            result.planes = planes.ToArray();
            result.center = Centroid(outPts);
            result.volume = Volume(result.points, result.triangles);
            result.surfaceArea = Area(result.points, result.triangles);
            result.edges = SilhouetteEdges(result.points, result.triangles, out Vector3[] edgeNormals);
            result.edgeNormals = edgeNormals;
            return true;
        }

        /// <summary>Лежит ли точка внутри оболочки (с допуском). Для диагностики покрытия.</summary>
        public static bool Contains(in DycHullResult hull, Vector3 p, float eps)
        {
            if (!hull.ok || hull.planes == null) return false;
            for (int i = 0; i < hull.planes.Length; i++)
            {
                Vector4 pl = hull.planes[i];
                if (pl.x * p.x + pl.y * p.y + pl.z * p.z + pl.w > eps) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ Внутреннее

        static int Idx(Dictionary<int, int> map, List<Vector3> outPts, List<Vector3> src, int i)
        {
            if (map.TryGetValue(i, out int idx)) return idx;
            idx = outPts.Count;
            map[i] = idx;
            outPts.Add(src[i]);
            return idx;
        }

        static void AddFace(List<DycHullFace> faces, List<Vector3> pts, int a, int b, int c, Vector3 inside)
        {
            var n = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            if (Vector3.Dot(n, pts[a] - inside) < 0f)
            {
                int t = b; b = c; c = t;
                n = -n;
            }
            faces.Add(new DycHullFace { a = a, b = b, c = c, normal = n });
        }

        /// <summary>
        /// Учитывает ребро как НЕориентированное (ключ по возрастанию индексов),
        /// но запоминает направление первого прохода: именно по нему строится
        /// новая грань конуса, поэтому ориентация оболочки остаётся наружу.
        /// </summary>
        static void RegisterEdge(Dictionary<long, int> counts, Dictionary<long, Vector2Int> dirs,
            Dictionary<long, Vector3> normals, int a, int b, Vector3 normal)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;

            counts.TryGetValue(key, out int c);
            counts[key] = c + 1;
            if (c != 0) return;

            dirs[key] = new Vector2Int(a, b);
            normals[key] = normal;
        }

        static List<Vector3> Dedupe(IList<Vector3> input, float eps)
        {
            var list = new List<Vector3>(input.Count);
            float sq = eps * eps;
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 v = input[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) continue;
                bool dup = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if ((list[j] - v).sqrMagnitude <= sq) { dup = true; break; }
                }
                if (!dup) list.Add(v);
            }
            return list;
        }

        static int ExtremeIndex(List<Vector3> pts, Vector3 dir)
        {
            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = Vector3.Dot(pts[i], dir);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        static int FarthestFromPoint(List<Vector3> pts, Vector3 p, out float dist)
        {
            int best = 0;
            dist = -1f;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (pts[i] - p).sqrMagnitude;
                if (d > dist) { dist = d; best = i; }
            }
            dist = Mathf.Sqrt(Mathf.Max(0f, dist));
            return best;
        }

        static int FarthestFromLine(List<Vector3> pts, Vector3 a, Vector3 b, out float dist)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            int best = 0;
            dist = -1f;
            if (len < 1e-9f) return best;

            Vector3 dir = ab / len;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 v = pts[i] - a;
                float d = (v - dir * Vector3.Dot(v, dir)).magnitude;
                if (d > dist) { dist = d; best = i; }
            }
            dist = Mathf.Max(0f, dist);
            return best;
        }

        static int FarthestFromPlane(List<Vector3> pts, Vector3 a, Vector3 b, Vector3 c, out float dist)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            int best = 0;
            dist = -1f;
            if (n.sqrMagnitude < 1e-18f) return best;
            n.Normalize();

            for (int i = 0; i < pts.Count; i++)
            {
                float d = Mathf.Abs(Vector3.Dot(n, pts[i] - a));
                if (d > dist) { dist = d; best = i; }
            }
            dist = Mathf.Max(0f, dist);
            return best;
        }

        static Vector3 Centroid(List<Vector3> pts)
        {
            Vector3 s = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) s += pts[i];
            return pts.Count > 0 ? s / pts.Count : Vector3.zero;
        }

        static float Volume(Vector3[] pts, int[] tris)
        {
            double v = 0;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = pts[tris[i]], b = pts[tris[i + 1]], c = pts[tris[i + 2]];
                v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            return (float)System.Math.Abs(v);
        }

        static float Area(Vector3[] pts, int[] tris)
        {
            float s = 0f;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = pts[tris[i]], b = pts[tris[i + 1]], c = pts[tris[i + 2]];
                s += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return s;
        }

        /// <summary>
        /// Объединяет копланарные треугольники, оставляя только настоящие
        /// «рёбра», чтобы гизмо был чистым. Попутно отдаёт нормали двух граней,
        /// смежных с каждым ребром, — по ним гизмо отсекает невидимую сторону.
        ///
        /// Раньше запекание кладло в оболочку ВСЕ рёбра треугольников, и гизмо
        /// рисовало триангуляцию целиком: издалека это читалось как залитая
        /// поверхность с рваным силуэтом. Здесь остаются только рёбра перехода
        /// между некомпланарными гранями — их в разы меньше.
        /// </summary>
        public static Vector3[] SilhouetteEdges(Vector3[] pts, int[] tris, out Vector3[] edgeNormals,
            float cosEps = 0.9995f)
        {
            int triCount = tris.Length / 3;
            var triNormals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = pts[tris[t * 3]], b = pts[tris[t * 3 + 1]], c = pts[tris[t * 3 + 2]];
                triNormals[t] = Vector3.Cross(b - a, c - a).normalized;
            }

            // Ребро -> список смежных граней
            var map = new Dictionary<long, List<int>>(triCount * 3);
            for (int t = 0; t < triCount; t++)
            {
                AddEdge(map, tris[t * 3], tris[t * 3 + 1], t);
                AddEdge(map, tris[t * 3 + 1], tris[t * 3 + 2], t);
                AddEdge(map, tris[t * 3 + 2], tris[t * 3], t);
            }

            var segs = new List<Vector3>(map.Count * 2);
            var norms = new List<Vector3>(map.Count * 2);

            foreach (var kv in map)
            {
                var owners = kv.Value;
                bool keep;
                if (owners.Count == 1)
                {
                    keep = true;   // граница поверхности
                }
                else if (owners.Count == 2)
                {
                    keep = Vector3.Dot(triNormals[owners[0]], triNormals[owners[1]]) < cosEps;
                }
                else
                {
                    keep = true;   // негладкое место, рисуем всегда
                }

                if (!keep) continue;

                int a = (int)(kv.Key >> 32);
                int b = (int)(kv.Key & 0xffffffffL);
                segs.Add(pts[a]);
                segs.Add(pts[b]);
                norms.Add(triNormals[owners[0]]);
                norms.Add(owners.Count > 1 ? triNormals[owners[1]] : Vector3.zero);
            }

            edgeNormals = norms.ToArray();
            return segs.ToArray();
        }

        static void AddEdge(Dictionary<long, List<int>> map, int a, int b, int tri)
        {
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(tri);
        }
    }
}
