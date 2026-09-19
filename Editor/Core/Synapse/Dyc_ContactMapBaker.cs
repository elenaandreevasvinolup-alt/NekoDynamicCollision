using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Оболочка карты контактов: ассеты, меню и связь с набором запекания.
    /// Сам счёт живёт в <see cref="Dyc_ContactMapSolver"/> — отдельно, чтобы
    /// его можно было проверить тестом без редактора.
    ///
    /// Что здесь делается и почему именно так:
    ///   · карта кладётся РЯДОМ с результатом запекания и привязывается к
    ///     набору, а не к компоненту: она такая же часть запечённых данных,
    ///     как оболочки, и живёт по тем же правилам;
    ///   · существующий ассет обновляется на месте, а не пересоздаётся —
    ///     иначе ссылка из набора разъехалась бы с файлом;
    ///   · отпечаток меша пишется в карту, чтобы после реимпорта было видно,
    ///     что она устарела (тот же приём, что у маски кисти).
    /// </summary>
    public static class Dyc_ContactMapBaker
    {
        public const float DefaultRadius = Dyc_ContactMapSolver.DefaultRadius;

        /// <summary>Оболочка над чистым счётом: достаёт из меша голые массивы.</summary>
        public static Dyc_ContactMap Bake(Mesh mesh, float radius, out string report)
        {
            report = null;
            if (mesh == null)
            {
                report = "нет меша";
                return null;
            }

            // GetVertices/GetTriangles с переиспользуемым списком: mesh.vertices
            // и mesh.triangles копируют массивы целиком на каждом обращении.
            var verts = new List<Vector3>(4096);
            mesh.GetVertices(verts);

            var tris = new List<int>(verts.Count * 3);
            var sub = new List<int>(verts.Count * 3);
            int subCount = Mathf.Max(1, mesh.subMeshCount);
            for (int s = 0; s < subCount; s++)
            {
                sub.Clear();
                mesh.GetTriangles(sub, s);
                tris.AddRange(sub);
            }

            var data = Dyc_ContactMapSolver.Build(verts.ToArray(), tris.ToArray(), radius, out report);
            if (data == null) return null;

            var map = ScriptableObject.CreateInstance<Dyc_ContactMap>();
            map.sourceHash = Dyc_PaintMask.Hash(mesh);
            map.vertexCount = data.vertexCount;
            map.radius = data.radius;
            map.ringLimit = data.ringLimit;
            map.meanEdgeLength = data.meanEdgeLength;
            map.pairs = data.pairs;
            map.clusterOf = data.clusterOf;
            map.clusterCenter = data.clusterCenter;
            map.clusterRadius = data.clusterRadius;
            map.clusterCount = data.clusterCount;
            return map;
        }

        /// <summary>Считает и кладёт карту рядом с результатом запекания, привязывая её к набору.</summary>
        public static Dyc_ContactMap BakeFor(Dyc_DynamicCollision target, Mesh mesh, float radius)
        {
            if (target == null) return null;

            var map = Bake(mesh, radius, out string report);
            if (map == null)
            {
                Debug.LogWarning("[NDC/Synapse] Карта контактов не построена: " + report, target);
                return null;
            }

            string folder = Dyc_AssetIO.RootFor(target);
            Dyc_AssetIO.EnsureFolder(folder);
            string path = folder + "/" + Dyc_AssetIO.Sanitize(target.gameObject.name) + "_Contact.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Dyc_ContactMap>(path);
            if (existing != null)
            {
                existing.sourceHash = map.sourceHash;
                existing.vertexCount = map.vertexCount;
                existing.radius = map.radius;
                existing.ringLimit = map.ringLimit;
                existing.meanEdgeLength = map.meanEdgeLength;
                existing.pairs = map.pairs;
                existing.clusterOf = map.clusterOf;
                existing.clusterCenter = map.clusterCenter;
                existing.clusterRadius = map.clusterRadius;
                existing.clusterCount = map.clusterCount;
                UnityEngine.Object.DestroyImmediate(map);
                map = existing;
                EditorUtility.SetDirty(map);
            }
            else
            {
                AssetDatabase.CreateAsset(map, path);
            }

            var set = AssetDatabase.LoadAssetAtPath<Dyc_BakedSet>(folder + "/" +
                Dyc_AssetIO.Sanitize(target.gameObject.name) + "_Baked.asset");
            if (set != null)
            {
                set.contactMap = map;
                EditorUtility.SetDirty(set);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[NDC/Synapse] Карта контактов: {report}\n  {path}", target);
            return map;
        }

        // ------------------------------------------------------------------ меню

        [MenuItem(Dyc_Menu.GBake + "Bake Contact Map", false, Dyc_Menu.PContactMap)]
        static void BakeSelected()
        {
            var target = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<Dyc_DynamicCollision>()
                : null;

            if (target == null)
            {
                Debug.LogWarning("[NDC/Synapse] Выберите объект с компонентом Dynamic Collision.");
                return;
            }

            var sources = Dyc_Baker.CollectSources(target);
            Mesh mesh = null;
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].mesh == null) continue;
                mesh = sources[i].mesh;
                break;
            }

            BakeFor(target, mesh, DefaultRadius);
        }

        [MenuItem(Dyc_Menu.GBake + "Bake Contact Map", true)]
        static bool BakeSelectedValidate()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponentInParent<Dyc_DynamicCollision>() != null;
        }
    }
}
