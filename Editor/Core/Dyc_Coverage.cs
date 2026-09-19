using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    public struct DycCoverageReport
    {
        public bool ok;
        public string error;

        public int totalTriangles;
        public int coveredTriangles;
        public float ratio;

        /// <summary>Индексы непокрытых треугольников (обрезаны, для подсветки в гизмо).</summary>
        public int[] uncovered;
        public bool truncated;

        /// <summary>Покрытие по каждому разделу (индекс = element).</summary>
        public float[] elementCoverage;
        public int[] elementUncovered;
        public int[] elementTotal;
    }

    /// <summary>
    /// Диагностика покрытия.
    ///
    /// Именно это превращает «хватает ли точности» из гадания в числа: в bind-позе каждый треугольник
    /// исходного меша проверяется на «точка внутри выпуклой оболочки» против всех оболочек; всё, что не покрыто, — это пропущенные места попадания.
    ///
    /// Считать в bind-позе можно потому, что в ней bone.localToWorldMatrix · bindposes[i] = I, то есть
    /// bind-поза = исходный меш как есть: скиннинг не нужен, текущая анимация не влияет.
    /// </summary>
    public static class Dyc_Coverage
    {
        const int MaxHighlight = 4000;

        public static DycCoverageReport Analyze(Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            var report = new DycCoverageReport();
            if (target == null || set == null || set.hulls.Count == 0)
            {
                report.error = Dyc_L10n.T("cov.none");
                return report;
            }

            var sources = Dyc_Baker.CollectSources(target);
            if (sources.Count == 0)
            {
                report.error = Dyc_L10n.T("cov.nosource");
                return report;
            }

            // НЕВЫПУКЛАЯ ФОРМА: ПОКРЫТИЕ СЧИТАЕТСЯ ИНАЧЕ, и это не поблажка.
            //
            // У выпуклой формы вопрос «покрыт ли треугольник» осмыслен потому,
            // что оболочка ЗАМЕНЯЕТ геометрию: часть треугольника может оказаться
            // вне всех оболочек, и это настоящий пропуск попадания.
            //
            // Невыпуклая поверхность — это и есть геометрия меша, поэтому
            // «покрыт» здесь означает «попал в раздел, у которого есть
            // поверхность». Считать пересечение с оболочкой было бы хуже:
            // оболочка, построенная по невыпуклой сетке, затягивает впадины и
            // показала бы ЗАВЫШЕННОЕ покрытие, а точное пересечение с десятками
            // тысяч треугольников стоило бы секунды на каждое запекание.
            bool concave = set.colliderShape == DycColliderShape.Concave;

            // Плоскости выпуклых оболочек переводим в пространство меша и группируем по element
            var planesByElement = new Dictionary<int, List<Vector4>>();
            var coveredElements = new HashSet<int>();

            if (concave)
            {
                for (int i = 0; i < set.hulls.Count; i++)
                {
                    var h = set.hulls[i];
                    if (h != null && h.nonConvex) coveredElements.Add(h.elementIndex);
                }
            }
            else
            {
                foreach (var kv in BuildMeshSpaceHulls(set))
                {
                    if (!planesByElement.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<Vector4>(64);
                        planesByElement[kv.Key] = list;
                    }
                    list.AddRange(kv.Value);
                }
            }

            var allPlanes = new List<Vector4>(256);
            foreach (var kv in planesByElement) allPlanes.AddRange(kv.Value);

            int elementCount = Mathf.Max(1, target.ElementCount);
            report.elementCoverage = new float[elementCount];
            report.elementUncovered = new int[elementCount];
            report.elementTotal = new int[elementCount];

            var uncovered = new List<int>(256);
            int total = 0, covered = 0;

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                if (src.mesh == null) continue;

                var verts = src.mesh.vertices;
                var tris = src.mesh.triangles;
                int triCount = tris.Length / 3;

                int[] partOfTri = null;
                if (target.Mode == DycMode.Skin && src.skin != null)
                    partOfTri = ComputeParts(target, src, verts, tris);

                for (int t = 0; t < triCount; t++)
                {
                    Vector3 c = (verts[tris[t * 3]] + verts[tris[t * 3 + 1]] + verts[tris[t * 3 + 2]]) / 3f;
                    total++;

                    int el = partOfTri != null ? partOfTri[t] : 0;
                    if (el >= 0 && el < report.elementTotal.Length) report.elementTotal[el]++;

                    if (concave
                        ? el >= 0 && coveredElements.Contains(el)
                        : ContainsAny(allPlanes, c, 0.002f))
                    {
                        covered++;
                        continue;
                    }

                    if (el >= 0 && el < report.elementUncovered.Length) report.elementUncovered[el]++;
                    if (uncovered.Count < MaxHighlight) uncovered.Add(t);
                    else report.truncated = true;
                }
            }

            report.ok = true;
            report.totalTriangles = total;
            report.coveredTriangles = covered;
            report.ratio = total > 0 ? covered / (float)total : 0f;
            report.uncovered = uncovered.ToArray();

            for (int i = 0; i < report.elementTotal.Length; i++)
            {
                int tot = report.elementTotal[i];
                int miss = report.elementUncovered[i];
                report.elementCoverage[i] = tot > 0 ? (tot - miss) / (float)tot : 1f;
            }

            return report;
        }

        static bool ContainsAny(List<Vector4> planes, Vector3 p, float eps)
        {
            // Плоскости идут подряд по element, здесь проверяем целиком: достаточно, чтобы точка попала в одну оболочку.
            // Число плоскостей одной оболочки = число её граней, поэтому проверка «все плоскости сразу» была бы неверной;
            // поэтому перешли на проверку по каждой оболочке отдельно.
            return ContainsInAnyHull(planes, p, eps);
        }

        static bool ContainsInAnyHull(List<Vector4> planes, Vector3 p, float eps)
        {
            int i = 0;
            while (i < planes.Count)
            {
                int count = (int)planes[i].w;
                i++;
                bool inside = true;
                for (int k = 0; k < count; k++)
                {
                    Vector4 pl = planes[i + k];
                    if (pl.x * p.x + pl.y * p.y + pl.z * p.z + pl.w > eps) { inside = false; break; }
                }
                if (inside) return true;
                i += count;
            }
            return false;
        }

        /// <summary>Упаковывает плоскости каждой выпуклой оболочки в один список: сначала число граней, затем сами грани.</summary>
        static Dictionary<int, List<Vector4>> BuildMeshSpaceHulls(Dyc_BakedSet set)
        {
            var result = new Dictionary<int, List<Vector4>>();

            for (int i = 0; i < set.hulls.Count; i++)
            {
                var h = set.hulls[i];
                if (h == null || h.mesh == null) continue;

                var pts = h.mesh.vertices;
                for (int p = 0; p < pts.Length; p++)
                    pts[p] = h.bindWorld.MultiplyPoint3x4(pts[p]);

                if (!Dyc_Hull.Build(pts, out var hull, 1e-5f) || !hull.ok) continue;

                if (!result.TryGetValue(h.elementIndex, out var list))
                {
                    list = new List<Vector4>(64);
                    result[h.elementIndex] = list;
                }

                list.Add(new Vector4(0f, 0f, 0f, hull.planes.Length));
                for (int pl = 0; pl < hull.planes.Length; pl++) list.Add(hull.planes[pl]);
            }

            return result;
        }

        /// <summary>Принадлежность разделу по тем же правилам, что у запекателя (доминирующий вес + приоритет ближайшего предка).</summary>
        static int[] ComputeParts(Dyc_DynamicCollision target, Dyc_Baker.Source src, Vector3[] verts, int[] tris)
        {
            int triCount = tris.Length / 3;
            var partOfTri = new int[triCount];
            var skin = src.skin;
            var mesh = src.mesh;
            var weights = mesh.boneWeights;
            var bones = skin.bones;
            var elements = target.Elements;

            if (weights == null || weights.Length != verts.Length || bones == null || bones.Length == 0)
            {
                for (int t = 0; t < triCount; t++) partOfTri[t] = 0;
                return partOfTri;
            }

            var boneToElement = new int[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                boneToElement[b] = Dyc_Baker.ResolveElement(elements, bones[b]);

            var dominant = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                BoneWeight bw = weights[v];
                int best = -1;
                float bestW = 0.1f;
                if (bw.weight0 > bestW) { bestW = bw.weight0; best = bw.boneIndex0; }
                if (bw.weight1 > bestW) { bestW = bw.weight1; best = bw.boneIndex1; }
                if (bw.weight2 > bestW) { bestW = bw.weight2; best = bw.boneIndex2; }
                if (bw.weight3 > bestW) { bestW = bw.weight3; best = bw.boneIndex3; }
                dominant[v] = best;
            }

            for (int t = 0; t < triCount; t++)
            {
                int d0 = dominant[tris[t * 3]];
                int d1 = dominant[tris[t * 3 + 1]];
                int d2 = dominant[tris[t * 3 + 2]];
                int d = d0 >= 0 ? d0 : (d1 >= 0 ? d1 : d2);
                partOfTri[t] = d >= 0 && d < boneToElement.Length ? boneToElement[d] : -1;
            }

            return partOfTri;
        }
    }
}
