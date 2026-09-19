using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Сборка коллайдеров В РАНТАЙМЕ, без шага запекания в редакторе.
    ///
    /// Зачем это здесь, если плагин гордится «нулевыми вычислениями в
    /// рантайме». Затем, что обычный сценарий должен быть обычным: добавил
    /// компонент — играешь. Запекание остаётся способом получить точность
    /// (зоны, разложение, покрытие), а не обязательным обрядом.
    ///
    /// Что получается на выходе: по одной выпуклой оболочке на кость, как в
    /// режиме Skin. Зон, разложения и бюджета точности здесь нет
    /// намеренно — это ровно то, что отличает «просто работает» от
    /// «настроено под персонажа».
    ///
    /// Формат результата — обычный Dyc_BakedSet, поэтому весь рантайм
    /// (материалы, события, роли, самоколлизии, LOD, живое обновление) дальше
    /// работает без единой ветки «а это набор из рантайма или с диска».
    /// </summary>
    public static class Dyc_RuntimeBuild
    {
        /// <summary>Собрать набор под текущий компонент. null — не из чего.</summary>
        public static Dyc_BakedSet Build(Dyc_DynamicCollision target)
        {
            if (target == null) return null;

            var skin = target.SourceSkin;
            if (skin == null) skin = target.GetComponentInChildren<SkinnedMeshRenderer>();

            if (skin != null && skin.sharedMesh != null)
            {
                var skinned = BuildFromSkin(target, skin);
                if (skinned != null) return skinned;

                // Скелета нет или весов нет — меш всё равно деформируется:
                // блендшейпами, решателем или кодом. Строим пространственные
                // кластеры, и «живое» обновление сможет за ними следить.
                return BuildFromMeshGeometry(target, skin.sharedMesh, skin.transform, DycMode.Skin);
            }

            var mf = target.SourceMesh;
            if (mf == null) mf = target.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return BuildFromMeshGeometry(target, mf.sharedMesh, mf.transform, DycMode.Mesh);

            return null;
        }

        // ------------------------------------------------------------------ Skin

        static Dyc_BakedSet BuildFromSkin(Dyc_DynamicCollision target, SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            var weights = mesh.boneWeights;

            if (bones == null || bones.Length == 0) return null;
            if (weights == null || weights.Length != mesh.vertexCount) return null;

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            var bindposes = mesh.bindposes;
            if (bindposes == null || bindposes.Length == 0) return null;

            int triCount = tris.Length / 3;
            if (triCount == 0) return null;

            // Доминирующая кость каждого треугольника — та же логика, что при
            // запекании: сумма весов по трём вершинам, чтобы треугольник на
            // стыке ушёл той кости, что влияет сильнее в целом.
            var triPerBone = new List<int>[bones.Length];
            for (int t = 0; t < triCount; t++)
            {
                float bestWeight = -1f;
                int bestBone = -1;

                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (v < 0 || v >= weights.Length) continue;
                    var bw = weights[v];

                    bestBone = Better(bones, bw.boneIndex0, bw.weight0, bestBone, ref bestWeight);
                    bestBone = Better(bones, bw.boneIndex1, bw.weight1, bestBone, ref bestWeight);
                    bestBone = Better(bones, bw.boneIndex2, bw.weight2, bestBone, ref bestWeight);
                    bestBone = Better(bones, bw.boneIndex3, bw.weight3, bestBone, ref bestWeight);
                }

                if (bestBone < 0) continue;
                if (Excluded(target, bones[bestBone])) continue;

                if (triPerBone[bestBone] == null) triPerBone[bestBone] = new List<int>(256);
                triPerBone[bestBone].Add(t);
            }

            var set = NewSet(target);
            set.mode = DycMode.Skin;

            int pointCap = Mathf.Max(16, target.Advanced.maxColliderTriangles);

            for (int b = 0; b < bones.Length; b++)
            {
                var list = triPerBone[b];
                if (list == null || list.Count == 0) continue;

                var bone = bones[b];
                if (bone == null) continue;

                var used = new List<int>(list.Count * 3);
                var seen = new HashSet<int>();
                for (int i = 0; i < list.Count; i++)
                {
                    int t = list[i];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t * 3 + k];
                        if (v < 0 || v >= verts.Length) continue;
                        if (seen.Add(v)) used.Add(v);
                    }
                }
                if (used.Count < 4) continue;

                // Вершины — в пространстве кости. Оболочка потом просто едет
                // за костью, как и запечённая.
                var bind = b < bindposes.Length ? bindposes[b] : Matrix4x4.identity;
                var points = new List<Vector3>(used.Count);
                int stride = Mathf.Max(1, used.Count / Mathf.Max(16, pointCap));
                for (int i = 0; i < used.Count; i += stride)
                    points.Add(bind.MultiplyPoint3x4(verts[used[i]]));

                var hull = MakeHull(points);
                if (hull == null) continue;

                hull.bonePath = PathOf(target.transform, bone);
                hull.elementIndex = ElementOf(target, bone);
                hull.groupIndex = 0;
                hull.sourceVertices = used.ToArray();
                hull.sourceIndex = 0;
                hull.nonConvex = false;

                set.hulls.Add(hull);
            }

            ApplyMaterialAssociations(target, skin.sharedMaterials, set);
            Finish(target, set);
            return set;
        }

        // ------------------------------------------------------------------ Mesh

        /// <summary>
        /// Меш без костей: блендшейпы, решатель мягкого тела, процедурная
        /// деформация. Кости не нужны — геометрия берётся как есть.
        ///
        /// Кластеры нарезаются ПРОСТРАНСТВЕННО (по bounding box), а не по
        /// порядку вершин: оболочка не должна заворачивать воздух между
        /// далёкими частями меша. Каждый кластер получает номер и позу покоя,
        /// поэтому его можно двигать решателем или пересобирать «живым»
        /// обновлением.
        /// </summary>
        static Dyc_BakedSet BuildFromMeshGeometry(Dyc_DynamicCollision target, Mesh mesh,
                                                  Transform meshSpace, DycMode mode)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            if (verts.Length < 4 || tris.Length < 12) return null;

            var set = NewSet(target);
            set.mode = mode;

            Matrix4x4 toOwner = target.transform.worldToLocalMatrix *
                                (meshSpace != null ? meshSpace.localToWorldMatrix : target.transform.localToWorldMatrix);

            var ownerVerts = new List<Vector3>(verts.Length);
            for (int i = 0; i < verts.Length; i++) ownerVerts.Add(toOwner.MultiplyPoint3x4(verts[i]));

            // Вершины, которые вообще используются треугольниками.
            var used = new List<int>(verts.Length);
            var seen = new bool[verts.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                int v = tris[i];
                if (v < 0 || v >= verts.Length || seen[v]) continue;
                seen[v] = true;
                used.Add(v);
            }

            int budget = Mathf.Max(64, target.Advanced.maxColliderTriangles);
            var chunks = new List<List<int>>();
            SplitSpatial(ownerVerts, used, budget, 0, chunks);

            for (int c = 0; c < chunks.Count; c++)
            {
                var chunk = chunks[c];
                if (chunk.Count < 4) continue;

                Vector3 center = Vector3.zero;
                for (int i = 0; i < chunk.Count; i++) center += ownerVerts[chunk[i]];
                center /= chunk.Count;

                var points = new List<Vector3>(chunk.Count);
                for (int i = 0; i < chunk.Count; i++) points.Add(ownerVerts[chunk[i]] - center);

                var hull = MakeHull(points);
                if (hull == null) continue;

                hull.bonePath = string.Empty;
                hull.elementIndex = 0;
                hull.groupIndex = 0;
                hull.clusterIndex = set.clusterCount++;
                hull.clusterRest = center;
                hull.sourceVertices = chunk.ToArray();
                hull.sourceIndex = 0;
                set.hulls.Add(hull);
            }

            ApplyMaterialAssociations(target, RendererMaterials(meshSpace), set);
            Finish(target, set);
            return set;
        }

        static Material[] RendererMaterials(Transform space)
        {
            if (space == null) return null;

            var r = space.GetComponent<Renderer>();
            return r != null ? r.sharedMaterials : null;
        }

        /// <summary>
        /// Делит набор вершин по bounding box, пока в куске не останется
        /// budget точек. Разрез идёт по самой длинной оси и по медиане, поэтому
        /// куски получаются компактными, а не вытянутыми.
        /// </summary>
        static void SplitSpatial(List<Vector3> pts, List<int> indices, int budget, int depth,
                                 List<List<int>> outChunks)
        {
            if (indices.Count <= budget || depth >= 8)
            {
                outChunks.Add(indices);
                return;
            }

            Vector3 min = pts[indices[0]];
            Vector3 max = min;
            for (int i = 1; i < indices.Count; i++)
            {
                var p = pts[indices[i]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Vector3 size = max - min;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);

            indices.Sort((a, b) => Axis(pts[a], axis).CompareTo(Axis(pts[b], axis)));

            int half = indices.Count / 2;
            var left = indices.GetRange(0, half);
            var right = indices.GetRange(half, indices.Count - half);

            SplitSpatial(pts, left, budget, depth + 1, outChunks);
            SplitSpatial(pts, right, budget, depth + 1, outChunks);
        }

        static float Axis(Vector3 v, int axis)
        {
            return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
        }

        // ------------------------------------------------------------------ общее

        static Dyc_BakedSet NewSet(Dyc_DynamicCollision target)
        {
            var set = ScriptableObject.CreateInstance<Dyc_BakedSet>();
            set.name = "NDC_Runtime_" + target.gameObject.name;
            set.colliderShape = DycColliderShape.Convex;
            set.precision = target.Precision;
            set.sourcePath = null;
            set.clusterCount = 0;
            set.hulls = new List<DycBakedHull>();
            return set;
        }

        static void Finish(Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            set.sourceVertexCount = 0;
            set.generatedUtcTicks = System.DateTime.UtcNow.Ticks;
        }

        /// <summary>Связывает материалы меша с физическими материалами и
        /// раскладывает их по номерам групп набора.</summary>
        static void ApplyMaterialAssociations(Dyc_DynamicCollision target, Material[] materials, Dyc_BakedSet set)
        {
            set.groupMaterials = new List<PhysicMaterial>();
            if (materials == null || materials.Length == 0) return;

            var assoc = target.Advanced.materialAssociations;
            if (assoc == null || assoc.Count == 0) return;

            for (int i = 0; i < materials.Length; i++)
            {
                PhysicMaterial pm = null;
                for (int a = 0; a < assoc.Count; a++)
                {
                    var link = assoc[a];
                    if (link != null && link.material == materials[i])
                    {
                        pm = link.physicsMaterial;
                        break;
                    }
                }
                set.groupMaterials.Add(pm);
            }
        }

        /// <summary>Выпуклая оболочка из точек; null — вырожденный набор.</summary>
        static DycBakedHull MakeHull(List<Vector3> points)
        {
            if (points == null || points.Count < 4) return null;

            DycHullResult hull;
            if (!Dyc_Hull.Build(points, out hull, 1e-5f) || !hull.ok || hull.points == null || hull.points.Length < 4)
                return null;

            var m = new Mesh { name = "NDC_RuntimeHull" };
            m.SetVertices(hull.points);
            m.SetTriangles(hull.triangles, 0);
            m.RecalculateBounds();
            m.RecalculateNormals();

            return new DycBakedHull
            {
                mesh = m,
                bindWorld = Matrix4x4.identity,
                localCenter = hull.center,
                localSize = m.bounds.size,
                volume = hull.volume,
                vertexCount = hull.points.Length,
                triangleCount = hull.triangles.Length / 3,
                edges = hull.edges,
                edgeNormals = hull.edgeNormals,
                clusterIndex = -1
            };
        }

        static int Better(Transform[] bones, int index, float weight, int best, ref float bestWeight)
        {
            if (index < 0 || index >= bones.Length || bones[index] == null) return best;
            if (weight <= bestWeight) return best;

            bestWeight = weight;
            return index;
        }

        /// <summary>Кость исключена: списком компонента или своим Dyc_BoneProperties.</summary>
        static bool Excluded(Dyc_DynamicCollision target, Transform bone)
        {
            if (bone == null) return true;
            if (target.IsBoneExcluded(bone)) return true;

            var props = bone.GetComponent<Dyc_BoneProperties>();
            return props != null && props.exclude;
        }

        static int ElementOf(Dyc_DynamicCollision target, Transform bone)
        {
            var elements = target.Elements;
            if (elements == null) return -1;

            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] != null && elements[i].bone == bone) return i;
            }
            return -1;
        }

        /// <summary>Путь кости относительно корня компонента; совместим с Transform.Find.</summary>
        public static string PathOf(Transform root, Transform t)
        {
            if (t == null) return null;
            if (root == null || t == root) return string.Empty;

            var parts = new List<string>(8);
            Transform cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            if (cur != root) return null;

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
