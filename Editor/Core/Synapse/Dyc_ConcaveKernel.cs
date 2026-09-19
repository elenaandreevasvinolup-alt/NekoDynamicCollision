using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Ядро разложения вогнутого меша на выпуклые части.
    ///
    /// ЭТО ЕДИНСТВЕННОЕ МЕСТО В NDC, ГДЕ ЕСТЬ НАТИВНЫЙ КОД, и оно осознанное.
    /// Воксельное разложение с поиском плоскостей разреза — это отдельная
    /// серьёзная算法, и писать её с нуля означает месяцы на качество, которого
    /// всё равно не будет. Поэтому здесь стоит готовая библиотека V-HACD
    /// (BSD-3, Khaled Mamou), а обёртка написана своя.
    ///
    /// ПОЧЕМУ ЭТО НЕ ЛОМАЕТ ПРАВИЛО «NDC БЕЗ ЗАВИСИМОСТЕЙ»:
    /// нативные библиотеки лежат в Editor/ и грузятся ТОЛЬКО редактором.
    /// В сборку они не попадают вообще, поэтому на игре их присутствие не
    /// отражается ни байтом. Это цена дискового места в репозитории, а не в包体.
    ///
    /// ЗАЧЕМ ВООБЩЕ: выпуклая оболочка не может описать вогнутое тело — впадины
    /// подмышек, паха, шеи она обязана «затянуть». Набор выпуклых частей ту же
    /// впадину описывает точно, оставаясь при этом НАБОРОМ ВЫПУКЛЫХ тел, то есть
    /// не теряя ни одной возможности PhysX (mesh-mesh столкновения, обычные
    /// rigidbody, быстрая широкая фаза) — в отличие от невыпуклого MeshCollider.
    ///
    /// ЧЕСТНАЯ ГРАНИЦА: качество разложения ниже, чем у V-HACD с настройками
    /// «на максимум». Мы сознательно ускоряем счёт, потому что это интерактивная
    /// операция в редакторе, а не финальный рендер.
    /// </summary>
    public static class Dyc_ConcaveKernel
    {
        /// <summary>Имя нативной библиотеки. Должно совпадать с именем файла.</summary>
        const string Library = "libvhacd";

        // ------------------------------------------------------------------ нативная сторона
        //
        // Сигнатуры и РАСКЛАДКА СТРУКТУР обязаны совпадать с C++ до байта.
        // Порядок полей в DycVhacdParameters менять нельзя: нативная сторона
        // читает их по смещениям, и перестановка молча превратит параметры в мусор.

        [StructLayout(LayoutKind.Sequential)]
        struct DycVhacdParameters
        {
            public double concavity;
            public double alpha;
            public double beta;
            public double minVolumePerHull;

            public IntPtr callback;
            public IntPtr logger;

            public uint resolution;
            public uint maxVerticesPerHull;
            public uint planeDownsampling;
            public uint convexhullDownsampling;
            public uint pca;
            public uint mode;
            public uint convexhullApproximation;
            public uint oclAcceleration;
            public uint maxHulls;

            // Обычный bool, а не MarshalAs(I1): нативная сторона объявляет это
            // поле как BOOL, и раскладка обязана совпасть побайтно. Поле
            // последнее, поэтому его размер влияет только на хвостовое
            // выравнивание — но менять то, что уже работает, здесь незачем.
            public bool projectHullVertices;
        }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct DycVhacdHull
        {
            public double* points;
            public uint* triangles;
            public uint pointCount;
            public uint triangleCount;
            public double volume;
            public fixed double center[3];
        }

        [DllImport(Library)] static extern unsafe IntPtr CreateVHACD();
        [DllImport(Library)] static extern unsafe void DestroyVHACD(IntPtr instance);

        [DllImport(Library)]
        static extern unsafe bool ComputeFloat(IntPtr instance, float* points, uint pointCount,
            uint* triangles, uint triangleCount, DycVhacdParameters* parameters);

        [DllImport(Library)] static extern unsafe uint GetNConvexHulls(IntPtr instance);
        [DllImport(Library)] static extern unsafe void GetConvexHull(IntPtr instance, uint index, DycVhacdHull* hull);

        /// <summary>Установлено ли ядро. Если нативная библиотека не загрузилась
        /// (нет под платформу), запекание должно вернуться к одной оболочке, а не
        /// упасть с исключением.</summary>
        public static bool Available
        {
            get
            {
                if (_availabilityChecked) return _available;
                _availabilityChecked = true;

                try
                {
                    var instance = CreateVHACD();
                    if (instance != IntPtr.Zero) DestroyVHACD(instance);
                    _available = true;
                }
                catch (Exception e)
                {
                    _available = false;
                    LastError = "нативная библиотека " + Library + " недоступна: " + e.GetType().Name;
                }

                return _available;
            }
        }

        static bool _availabilityChecked;
        static bool _available;

        /// <summary>Человеческое объяснение последнего отказа. null — всё хорошо.</summary>
        public static string LastError { get; private set; }

        static string _reportedError;

        /// <summary>
        /// Сообщает об отказе разложения один раз на текст ошибки.
        ///
        /// Запекание зовёт ядро по разу на источник, и без этой защиты одна и та
        /// же причина (например отсутствующая библиотека под платформу) попала бы
        /// в консоль столько раз, сколько источников, — а причина у них общая.
        /// </summary>
        public static void ReportError(string error)
        {
            if (string.IsNullOrEmpty(error)) return;
            if (_reportedError == error) return;

            _reportedError = error;
            UnityEngine.Debug.LogWarning("[NDC/Synapse] Разложение недоступно: " + error +
                "\n  Оболочки будут построены по одной на кость — впадины окажутся затянутыми.");
        }

        // ------------------------------------------------------------------ разложение

        /// <summary>
        /// Разлагает меш на выпуклые части.
        ///
        /// Меш ОБЯЗАН быть замкнутым (или почти замкнутым): ядро работает через
        /// вокселизацию, то есть ему нужен объём, а не поверхность. Открытая
        /// поверхность даёт мусор. Для скелета это значит, что разлагать надо
        /// ЦЕЛОЕ тело, а части потом раскладывать по костям — а не по одной
        /// кости, у которой меш заведомо открыт.
        /// </summary>
        public static bool Decompose(Mesh mesh, DycDecomposeSettings settings,
            out List<Mesh> pieces, out string error)
        {
            pieces = null;
            error = null;

            if (mesh == null) { error = "нет меша"; return false; }
            if (!Available) { error = LastError; return false; }

            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            if (vertices.Length < 4 || triangles.Length < 12)
            {
                error = "слишком мало геометрии";
                return false;
            }

            var parameters = ToNative(mesh, settings);
            IntPtr instance = IntPtr.Zero;

            try
            {
                instance = CreateVHACD();
                if (instance == IntPtr.Zero) { error = "ядро не создалось"; return false; }

                unsafe
                {
                    fixed (Vector3* pVertices = vertices)
                    fixed (int* pTriangles = triangles)
                    {
                        ComputeFloat(instance, (float*)pVertices, (uint)vertices.Length,
                            (uint*)pTriangles, (uint)(triangles.Length / 3), &parameters);
                    }
                }

                uint count = GetNConvexHulls(instance);
                if (count == 0) { error = "ядро вернуло 0 частей"; return false; }

                pieces = new List<Mesh>((int)count);
                for (uint i = 0; i < count; i++) pieces.Add(ReadPiece(instance, i));

                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
            finally
            {
                if (instance != IntPtr.Zero) DestroyVHACD(instance);
            }
        }

        /// <summary>Читает одну часть и превращает её в меш.</summary>
        static unsafe Mesh ReadPiece(IntPtr instance, uint index)
        {
            DycVhacdHull hull;
            GetConvexHull(instance, index, &hull);

            var vertices = new Vector3[hull.pointCount];
            double* source = hull.points;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3((float)source[0], (float)source[1], (float)source[2]);
                source += 3;
            }

            var triangles = new int[hull.triangleCount * 3];
            Marshal.Copy((IntPtr)hull.triangles, triangles, 0, triangles.Length);

            var mesh = new Mesh { name = "NDC_Piece_" + index, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Сколько «единиц вогнутости» приходится на один воксель.
        ///
        /// Величина эмпирическая: у V-HACD порог вогнутости безразмерный, и
        /// точной формулы перевода из вокселей нет. Коэффициент подобран так,
        /// чтобы привычная настройка (8 вокселей) давала то же поведение, что
        /// прежние 0.008 — то есть уже отсмотренный результат не меняется.
        /// </summary>
        const float ConcavityPerVoxel = 0.001f;

        static DycVhacdParameters ToNative(Mesh mesh, DycDecomposeSettings settings)
        {
            // Число вокселей считается ИЗ РАЗМЕРА МЕША и размера вокселя.
            // Это и есть переход к физической величине: «5 мм» означает 5 мм
            // независимо от того, персонаж это или палец.
            var size = mesh.bounds.size;
            float diagonalMm = size.magnitude * 1000f;
            if (diagonalMm < 1f) diagonalMm = 1f;

            float voxelMm = Mathf.Clamp(settings.voxelSizeMm, 1f, 200f);
            double voxels = Math.Pow(diagonalMm / voxelMm, 3.0);

            return new DycVhacdParameters
            {
                // ВЕРХНЯЯ ГРАНИЦА — НАША, А НЕ ЯДРА.
                //
                // У самого V-HACD предела почти нет, и именно поэтому запекание
                // «вешало» редактор: персонаж 1.8 м при вокселе 8 мм давал около
                // 59 млн ячеек, ядро честно пыталось их посчитать, и это минуты
                // синхронной работы без возможности отмены. Ограничение до
                // 2.5 млн держит счёт в пределах десятков секунд.
                //
                // Если нужна точность выше — это делает своё ядро (Own): у него
                // бюджет задаётся явно и есть отмена.
                resolution = (uint)Mathf.Clamp((float)voxels, 10000f, 2500000f),
                concavity = Mathf.Clamp(settings.minConcavityVoxels * ConcavityPerVoxel, 0.0001f, 1f),
                alpha = 0.05,
                beta = 0.05,
                minVolumePerHull = Mathf.Clamp(settings.minVolumePerHull, 0f, 0.05f),
                callback = IntPtr.Zero,
                logger = IntPtr.Zero,
                maxVerticesPerHull = (uint)Mathf.Clamp(settings.maxVerticesPerHull, 4, 255),
                planeDownsampling = 4,
                convexhullDownsampling = 4,
                pca = settings.normalize ? 1u : 0u,
                mode = 0,                       // 0 — воксельный, он рекомендован и стабильнее
                convexhullApproximation = 1,
                oclAcceleration = 0,            // OpenCL не используем: на CPU предсказуемее
                maxHulls = (uint)Mathf.Clamp(settings.maxHulls, 1, 1024),
                projectHullVertices = settings.projectHullVertices
            };
        }
    }
}
