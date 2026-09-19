using System;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Кисть-разметка: по одному байту на треугольник, значение — индекс DycMaterialGroup (0 = группа по умолчанию).
    ///
    /// Это носитель "зонального материала столкновений" и единая абстракция всего плагина:
    /// кисть = проставить метки граням, а метки управляют всеми решениями запекания (группа материала / точность / события / исключения).
    ///
    /// Хранятся только номера треугольников, без UV-текстуры — кисть куда нагляднее,
    /// чем UV-карта исключений, и не требует наличия UV2 у модели.
    /// </summary>
    public class Dyc_PaintMask : ScriptableObject
    {
        public string sourceHash;

        /// <summary>Путь объекта-владельца; см. Dyc_BakedSet.sourcePath — та же
        /// защита от двух одноимённых объектов в одной сцене.</summary>
        public string sourcePath;

        public int vertexCount;
        public int triangleCount;

        public byte[] labels = Array.Empty<byte>();

        /// <summary>Байт на треугольник: 1 — по нему прошлись кистью, 0 — нет.
        /// Нужен отдельно от labels, иначе группа 0 (значение по умолчанию)
        /// неотличима от «не покрашено», и подсветка заливала бы весь меш.</summary>
        public byte[] painted = Array.Empty<byte>();

        /// <summary>Счётчик правок. По нему подсветка понимает, что метки
        /// изменились, и не пересобирает меши по таймеру.</summary>
        public int version;

        public bool IsValid => labels != null && labels.Length > 0;

        /// <summary>Прошлись ли кистью по этому треугольнику.</summary>
        public bool IsPainted(int triangle)
        {
            return painted != null && triangle >= 0 && triangle < painted.Length && painted[triangle] != 0;
        }

        /// <summary>Пометить треугольник: группа + признак покраски. erase снимает признак.</summary>
        public void Mark(int triangle, byte label, bool erase)
        {
            if (labels == null || triangle < 0 || triangle >= labels.Length) return;
            labels[triangle] = erase ? (byte)0 : label;
            if (painted != null && triangle < painted.Length)
                painted[triangle] = erase ? (byte)0 : (byte)1;
            version++;
        }

        public byte Get(int triangle)
        {
            if (labels == null || triangle < 0 || triangle >= labels.Length) return 0;
            return labels[triangle];
        }

        public void Set(int triangle, byte label)
        {
            if (labels == null || triangle < 0 || triangle >= labels.Length) return;
            labels[triangle] = label;
        }

        public void EnsureSize(int triangles)
        {
            if (labels != null && labels.Length == triangles && painted != null && painted.Length == triangles)
                return;

            var nextLabels = new byte[triangles];
            var nextPainted = new byte[triangles];
            if (labels != null) Array.Copy(labels, nextLabels, Mathf.Min(labels.Length, triangles));

            if (painted != null)
            {
                Array.Copy(painted, nextPainted, Mathf.Min(painted.Length, triangles));
            }
            else if (labels != null)
            {
                // Миграция: маска создана до появления painted[]. Всё, у чего
                // метка не нулевая, когда-то было покрашено — не теряем работу.
                int n = Mathf.Min(labels.Length, triangles);
                for (int i = 0; i < n; i++) nextPainted[i] = labels[i] != 0 ? (byte)1 : (byte)0;
            }

            labels = nextLabels;
            painted = nextPainted;
            triangleCount = triangles;
            version++;
        }

        public void Clear()
        {
            if (labels != null) Array.Clear(labels, 0, labels.Length);
            if (painted != null) Array.Clear(painted, 0, painted.Length);
            version++;
        }

        /// <summary>Сколько треугольников реально покрашено по каждой группе.</summary>
        public int[] CountPaintedByLabel(int groupCount)
        {
            var counts = new int[Mathf.Max(1, groupCount)];
            if (labels == null || painted == null) return counts;
            for (int i = 0; i < labels.Length; i++)
            {
                if (painted[i] == 0) continue;
                int l = labels[i];
                if (l < 0 || l >= counts.Length) continue;
                counts[l]++;
            }
            return counts;
        }

        /// <summary>Количество треугольников по каждой метке; индекс = индекс группы.</summary>
        public int[] CountByLabel(int groupCount)
        {
            var counts = new int[Mathf.Max(1, groupCount)];
            if (labels == null) return counts;
            for (int i = 0; i < labels.Length; i++)
            {
                int l = labels[i];
                if (l < 0 || l >= counts.Length) continue;
                counts[l]++;
            }
            return counts;
        }

        /// <summary>Переназначает все метки одного значения в другое (склейка осколков).</summary>
        public int Remap(byte from, byte to)
        {
            if (labels == null) return 0;
            int n = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != from) continue;
                labels[i] = to;
                if (painted != null) painted[i] = 1;
                n++;
            }
            if (n > 0) version++;
            return n;
        }

        // ------------------------------------------------------------------ отпечаток

        public static string Hash(Mesh mesh)
        {
            if (mesh == null) return null;

            var verts = mesh.vertices;
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                h = Mix(h, (uint)BitConverter.SingleToInt32Bits(v.x));
                h = Mix(h, (uint)BitConverter.SingleToInt32Bits(v.y));
                h = Mix(h, (uint)BitConverter.SingleToInt32Bits(v.z));
            }
            h = Mix(h, (uint)verts.Length);
            h = Mix(h, (uint)mesh.triangles.Length);
            h = Mix(h, (uint)mesh.subMeshCount);
            return h.ToString("x16");
        }

        static ulong Mix(ulong h, uint v)
        {
            h ^= v;
            h *= 1099511628211UL;
            return h;
        }
    }
}
