using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Общий кэш вершин меша в МИРОВЫХ координатах по ТЕКУЩЕЙ позе.
    ///
    /// Зачем отдельный класс: и кисть, и подсветка закрашенных граней должны
    /// видеть ровно один и тот же набор вершин, иначе подсветка не совпадёт с
    /// тем, что реально помечено. Раньше каждый из них запекал меш сам, и оба
    /// делали это по нескольку раз в секунду — отсюда и тормоза.
    ///
    /// Ключевые правила, без которых снова появятся лаги:
    ///   · Пересборка ТОЛЬКО при смене объекта, меша или позы. Никаких
    ///     таймеров «на всякий случай».
    ///   · Никаких mesh.vertices / mesh.triangles — они каждый раз копируют
    ///     весь массив. Используются перегруженные GetVertices(List) и
    ///     GetTriangles(List), которые пишут в переиспользуемые списки.
    ///   · Запекание позы идёт в один переиспользуемый черновой Mesh.
    /// </summary>
    public static class Dyc_MeshCache
    {
        class Entry
        {
            public Vector3[] verts;
            public int[] tris;
            public long stamp;
            public double lastBake;
        }

        static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>(4);

        static readonly List<Vector3> _v = new List<Vector3>(8192);
        static readonly List<int> _t = new List<int>(8192);
        static readonly List<Vector3> _subV = new List<Vector3>(4096);
        static readonly List<int> _subT = new List<int>(4096);
        static Mesh _scratch;

        public static void Invalidate()
        {
            _entries.Clear();
        }

        public static void Invalidate(Dyc_DynamicCollision target)
        {
            if (target != null) _entries.Remove(target.GetInstanceID());
        }

        /// <summary>
        /// Возвращает вершины (мир, текущая поза) и сквозной список
        /// треугольников. force = true заставляет перезапечь позу — это дёшево
        /// и нужно только в начале мазка.
        /// </summary>
        public static bool Get(Dyc_DynamicCollision target, bool force,
            out Vector3[] verts, out int[] tris, out bool rebuilt)
        {
            verts = null;
            tris = null;
            rebuilt = false;
            if (target == null) return false;

            int id = target.GetInstanceID();
            if (!_entries.TryGetValue(id, out var e))
            {
                e = new Entry();
                _entries[id] = e;
            }

            var sources = Dyc_Baker.CollectSources(target);
            long stamp = Stamp(sources);

            bool stale = force || e.verts == null || e.stamp != stamp;
            if (!stale)
            {
                verts = e.verts;
                tris = e.tris;
                return true;
            }

            Build(target, sources, e);
            verts = e.verts;
            tris = e.tris;
            rebuilt = true;
            return verts != null && verts.Length > 0;
        }

        // ------------------------------------------------------------------

        /// <summary>Отпечаток источника: объекты мешей + поза корневой кости.
        /// Считается целыми числами и матрицей, без единого выделения памяти.</summary>
        static long Stamp(List<Dyc_Baker.Source> sources)
        {
            long h = 17;
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                h = h * 31 + (s.mesh != null ? s.mesh.GetInstanceID() : 0);
                h = h * 31 + (s.skin != null ? s.skin.GetInstanceID() : 0);
                h = h * 31 + (s.meshFilter != null ? s.meshFilter.GetInstanceID() : 0);
            }

            for (int i = 0; i < sources.Count; i++)
            {
                var skin = sources[i].skin;
                if (skin == null) continue;

                Transform root = skin.rootBone != null ? skin.rootBone : skin.transform;
                Matrix4x4 m = root.localToWorldMatrix;
                for (int k = 0; k < 16; k++) h = h * 31 + (long)(m[k] * 1000f);
                break;
            }

            return h;
        }

        static void Build(Dyc_DynamicCollision target, List<Dyc_Baker.Source> sources, Entry e)
        {
            e.stamp = Stamp(sources);
            e.lastBake = UnityEditor.EditorApplication.timeSinceStartup;

            _v.Clear();
            _t.Clear();

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                if (src.mesh == null) continue;

                // Временные списки обязательны: перегруженные GetVertices /
                // GetTriangles заполняют список С НУЛЯ, поэтому при нескольких
                // источниках данные предыдущего были бы затёрты.
                int offset = _v.Count;

                if (src.skin != null)
                {
                    if (_scratch == null)
                        _scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };

                    src.skin.BakeMesh(_scratch);
                    _subV.Clear();
                    _scratch.GetVertices(_subV);

                    Matrix4x4 m = src.skin.localToWorldMatrix;
                    for (int i = 0; i < _subV.Count; i++)
                        _v.Add(m.MultiplyPoint3x4(_subV[i]));
                }
                else
                {
                    _subV.Clear();
                    src.mesh.GetVertices(_subV);

                    Matrix4x4 m = src.meshFilter != null
                        ? src.meshFilter.transform.localToWorldMatrix
                        : target.transform.localToWorldMatrix;
                    for (int i = 0; i < _subV.Count; i++)
                        _v.Add(m.MultiplyPoint3x4(_subV[i]));
                }

                int subCount = Mathf.Max(1, src.mesh.subMeshCount);
                for (int sub = 0; sub < subCount; sub++)
                {
                    _subT.Clear();
                    src.mesh.GetTriangles(_subT, sub);
                    for (int i = 0; i < _subT.Count; i++) _t.Add(_subT[i] + offset);
                }
            }

            e.verts = _v.ToArray();
            e.tris = _t.ToArray();
        }
    }
}
