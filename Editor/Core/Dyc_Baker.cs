using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Прогресс и отмена запекания.
    ///
    /// ЗАЧЕМ. Раньше запекание было чёрным ящиком: нативное разложение считало
    /// минуты, редактор не отвечал, и отличить «считает» от «повисло» было
    /// нельзя. Теперь у долгих шагов есть точка отчёта, которую редактор
    /// показывает прогресс-баром, и точка отмены, которую он же проверяет.
    ///
    /// Класс намеренно не знает про UnityEditor: воксельное разложение — чистая
    /// математика, и её должно быть можно прогнать в консольном тесте.
    /// </summary>
    public class DycBakeProgress
    {
        /// <summary>Отчёт: (доля 0..1, что делаем) → true означает «отменить».</summary>
        public System.Func<float, string, bool> report;

        /// <summary>Часы всего запекания. Нужны для бюджета времени.</summary>
        public System.Diagnostics.Stopwatch watch;

        /// <summary>Бюджет времени в мс. 0 — без предела.</summary>
        public double budgetMs;

        /// <summary>Отмена запрошена (пользователем или по времени).</summary>
        public bool cancelled;

        /// <summary>
        /// Остановка именно по бюджету времени, а не по кнопке отмены. Разница
        /// важна: при отмене человека результат выбрасывается, при исчерпании
        /// бюджета сохраняется то, что успело посчитаться.
        /// </summary>
        public bool timedOut;

        public bool Stopped
        {
            get
            {
                if (cancelled) return true;
                if (budgetMs > 0 && watch != null && watch.Elapsed.TotalMilliseconds > budgetMs)
                {
                    cancelled = true;
                    timedOut = true;
                    return true;
                }
                return false;
            }
        }

        public void Report(float t, string stage)
        {
            if (report != null && report(Mathf.Clamp01(t), stage)) cancelled = true;
        }
    }

    public struct DycBakeReport
    {
        public bool ok;
        public string error;

        public int sourceRenderers;
        public int sourceTriangles;
        public int usedTriangles;
        public int unassignedTriangles;
        public int degenerateClusters;
        public int hulls;
        public int hullVertices;
        public int maxHullVertices;
        public float totalVolume;

        /// <summary>Что получилось на каждой кости. Заполняется разложением.</summary>
        public List<DycBoneStat> boneStats;

        /// <summary>Сколько вершин раздвинуто перед разложением.</summary>
        public int separatedVertices;

        /// <summary>
        /// Сработало ли РАЗЛОЖЕНИЕ, а не запасная кластеризация.
        ///
        /// Нужно, чтобы «части нарезаны по впадинам» было видно числом, а не
        /// на глаз: именно это отличает результат от одной затягивающей
        /// оболочки, и именно это ставит NDC вровень с лучшими офлайн-решениями и выше
        /// системного выпуклого коллайдера.
        /// </summary>
        public bool decomposed;

        public double elapsedMs;
        public List<string> warnings;
    }

    /// <summary>
    /// Запекатель: превращает меш в convex-ассет с "нулевыми вычислениями в рантайме".
    ///
    /// Пайплайн:
    ///   привязка к частям (доминирующий вес кости / приоритет ближайшего предка)
    ///   → ограничение по меткам материалов (кисть)
    ///   → преобразование в локальное пространство кости (bindposes, поза привязки)
    ///   → кластеризация в ограниченном пространстве
    ///   → собственный расчёт выпуклой оболочки (объём / чистые рёбра / плоскости покрытия)
    ///   → расширение границ (seamOverlap)
    ///   → вывод Mesh + данные
    ///
    /// В Unity отдаётся "сама выпуклая оболочка", а не сырой суп треугольников: число вершин
    /// естественно ≤ числа вершин кластера ≤ 250, лимит PhysX в 255 не превышается, а воздух не запекается.
    /// </summary>
    public static class Dyc_Baker
    {
        // ------------------------------------------------------------------ предвычисленное разложение

        /// <summary>Готовые части одного источника плюс учёт раздвинутых вершин.</summary>
        public struct DycPrecomputedDecompose
        {
            public List<DycVoxelPiece> pieces;
            public int separatedVertices;

            /// <summary>Отпечаток настроек, с которыми считались части. См. StampOf.</summary>
            public int stamp;
        }

        /// <summary>
        /// Отпечаток настроек разложения.
        ///
        /// Нужен потому, что кэш предвычисленного разложения привязан к МЕШУ, а
        /// результат зависит ещё и от параметров. Пока размер вокселя менялся
        /// только в окне эксперта и редко, расхождение было малозаметным; теперь
        /// его крутят слайдером, и без отпечатка запекание молча подхватило бы
        /// части, посчитанные по СТАРОМУ размеру вокселя, — а выглядело бы это
        /// как «слайдер не работает».
        /// </summary>
        public static int StampOf(in DycDecomposeSettings s)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + s.voxelSizeMm.GetHashCode();
                h = h * 31 + s.minConcavityVoxels;
                h = h * 31 + s.maxHulls;
                h = h * 31 + s.maxVerticesPerHull;
                h = h * 31 + s.minVolumePerHull.GetHashCode();
                h = h * 31 + (s.projectHullVertices ? 1 : 0);
                h = h * 31 + (s.separateByBones ? 1 : 0);
                h = h * 31 + s.separationMm.GetHashCode();
                h = h * 31 + (s.normalize ? 1 : 0);
                h = h * 31 + s.maxVoxels;
                h = h * 31 + s.convexFillRatio.GetHashCode();
                h = h * 31 + s.maxSplitDepth;
                h = h * 31 + s.boneWeightThreshold.GetHashCode();
                h = h * 31 + (int)s.kernel;
                return h;
            }
        }

        static readonly Dictionary<int, DycPrecomputedDecompose> _precomputed =
            new Dictionary<int, DycPrecomputedDecompose>();

        /// <summary>
        /// Кладёт результат фонового разложения. Ключ — instanceID исходного
        /// меша: он один и тот же и в задании, и в запекании, и не зависит от
        /// того, в каком порядке обходятся источники.
        /// </summary>
        public static void SetPrecomputed(int meshId, List<DycVoxelPiece> pieces, int separatedVertices,
                                          in DycDecomposeSettings settings)
        {
            lock (_precomputed)
            {
                _precomputed[meshId] = new DycPrecomputedDecompose
                {
                    pieces = pieces,
                    separatedVertices = separatedVertices,
                    stamp = StampOf(settings)
                };
            }
        }

        /// <summary>
        /// Забирает предвычисленное разложение. Именно ЗАБИРАЕТ: запись
        /// удаляется, чтобы следующий цикл запекания не подхватил устаревшие
        /// части от старой геометрии.
        /// </summary>
        public static bool TryTakePrecomputed(Mesh mesh, in DycDecomposeSettings settings,
                                              out DycPrecomputedDecompose data)
        {
            data = default;
            if (mesh == null) return false;

            lock (_precomputed)
            {
                int id = mesh.GetInstanceID();
                if (!_precomputed.TryGetValue(id, out data)) return false;
                _precomputed.Remove(id);
            }

            // Настройки изменились с момента фонового счёта — части посчитаны
            // по другим параметрам. Берём их и выбрасываем: лучше посчитать на
            // месте, чем отдать пользователю результат, не совпадающий с
            // положением слайдера.
            if (data.stamp != StampOf(settings)) return false;

            return data.pieces != null && data.pieces.Count > 0;
        }

        public static void ClearPrecomputed()
        {
            lock (_precomputed) _precomputed.Clear();
        }

        public static DycBakeReport Bake(Dyc_DynamicCollision target, Dyc_BakedSet set, bool saveAssets,
            string assetFolder, DycBakeProgress progress = null)
        {
            var report = new DycBakeReport { warnings = new List<string>() };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (target == null || set == null)
            {
                report.error = Dyc_L10n.T("bake.err.none");
                return report;
            }

            DycPrecisionInfo info = target.PrecisionInfo;
            set.mode = target.Mode;
            set.precision = target.Precision;
            set.colliderShape = target.ColliderShape;
            set.seamOverlap = info.seamOverlap;
            set.boneWeightThreshold = info.boneWeightThreshold;
            set.hulls.Clear();
            set.bindPoses.Clear();
            set.bindBonePaths.Clear();

            var sources = CollectSources(target);
            if (sources.Count == 0)
            {
                report.error = Dyc_L10n.T(target.Mode == DycMode.Skin ? "bake.err.noSkin" : "bake.err.noMesh");
                return report;
            }

            var elements = target.Elements;
            var groups = target.Groups;
            if (groups == null || groups.Count == 0)
            {
                report.error = Dyc_L10n.T("bake.err.noGroups");
                return report;
            }

            int groupCount = groups.Count;
            var hashes = new List<string>();
            var allHulls = new List<DycBakedHull>();

            // Метки кисти — это глобальные номера треугольников "всех исходных мешей по порядку", как в Dyc_PaintTool
            var mask = target.PaintMask;
            int totalTriangles = 0;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].mesh != null) totalTriangles += sources[i].mesh.triangles.Length / 3;

            bool maskUsable = mask != null && mask.IsValid;
            if (maskUsable && mask.triangleCount != totalTriangles)
            {
                report.warnings.Add(Dyc_L10n.T("bake.warn.maskMismatch", mask.triangleCount, totalTriangles));
                maskUsable = false;
            }

            int triangleOffset = 0;

            // Сквозной номер кластера мягкого тела. Именно он связывает
            // запечённую оболочку с кадром деформации, поэтому он один на весь
            // набор и не зависит от источника.
            int softCluster = 0;

            // Предупреждение, которое экономит час отладки.
            //
            // В Skin-режиме зона — это и есть будущий коллайдер. Если она одна,
            // то одна оболочка покрывает ВСЁ тело: это законный, но почти
            // бесполезный результат (большой ящик вокруг персонажа), и снаружи он
            // выглядит как «оболочки нет». Причина при этом не в запекании, а в
            // пустом списке зон, и без явного текста её ищут в кластеризации.
            if (target.Mode == DycMode.Skin && target.ElementCount <= 1)
            {
                report.warnings.Add(
                    "Skin-режим и всего одна зона: одна оболочка покроет всё тело одним ящиком. " +
                    "Нажмите «Зоны → Из скелета», чтобы получить по зоне на каждую кость.");
            }

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                Mesh mesh = src.mesh;
                if (mesh == null) continue;

                hashes.Add(Dyc_PaintMask.Hash(mesh));
                report.sourceRenderers++;
                report.sourceTriangles += mesh.triangles.Length / 3;

                var verts = mesh.vertices;
                var tris = mesh.triangles;
                int triCount = tris.Length / 3;
                if (triCount == 0) continue;

                // ---- Привязка к частям
                var partOfTri = new int[triCount];
                var labelOfTri = new byte[triCount];

                // ---- Материалы исходного меша
                //
                // Разрешаются ОДИН раз на источник: подмеш → исходный материал →
                // физический материал (карта материалов). Номер в
                // set.sourceMaterials запоминается в оболочке, поэтому в
                // рантайме не нужны ни меш, ни подмеши — только число.
                int materialBase = set.sourceMaterials.Count;
                int[] submeshOfTri = BuildSubmeshTable(target, src, mesh, triCount, set);

                // SKIN обрабатывается здесь целиком и уходит из цикла по источникам.
                //
                // Сначала пробуем РАЗЛОЖЕНИЕ (вогнутые тела), и только если ядро
                // недоступно или отказало — падаем на одну оболочку на кость.
                // Порядок именно такой: разложение описывает впадины, одиночная
                // оболочка их затягивает.
                if (target.Mode == DycMode.Skin)
                {
                    // Невыпуклая форма НЕ имеет отдельной ветки: она идёт тем же
                    // путём, что и выпуклая (зоны, кости, бюджет точности), и
                    // меняется только выход — поверхность вместо оболочки.
                    // Отдельная ветка означала бы вторую реализацию тех же
                    // правил, и они бы разошлись.
                    //
                    // Разложение в этом режиме не запускается вовсе: оно строит
                    // выпуклые части, а невыпуклому они не нужны. Гнать самое
                    // дорогое и тут же выбрасывать результат — это и была
                    // причина, по которой запекание «думало» минуты.
                    bool concave = target.ColliderShape == DycColliderShape.Concave;

                    int hulls = (src.skin != null && !concave)
                        ? BuildConcaveSkinHulls(target, src, mesh, verts, tris, info, allHulls, ref report, progress)
                        : 0;

                    // Части пришли разложением, а не одной оболочкой на кость.
                    // Флаг нужен отчёту и набору: «нарезано по впадинам» должно
                    // быть видно числом, а не на глаз.
                    if (hulls > 0) report.decomposed = true;

                    if (hulls == 0 && src.skin != null)
                    {
                        if (!concave && report.warnings.Count < 8)
                        {
                            report.warnings.Add(
                                "Разложение недоступно или отказало" +
                                (string.IsNullOrEmpty(Dyc_ConcaveKernel.LastError)
                                    ? "" : " (" + Dyc_ConcaveKernel.LastError + ")") +
                                " — использована одна оболочка на кость.");
                        }

                        hulls = BuildDirectSkinHulls(target, src, mesh, verts, tris,
                            partOfTri, labelOfTri, info, allHulls, ref report, s,
                            submeshOfTri, materialBase);
                    }

                    if (hulls == 0)
                    {
                        report.warnings.Add(
                            "Skin-режим: ни одной оболочки. Вероятно, у меша нет boneWeights " +
                            "или все кости отфильтрованы.");
                    }

                    triangleOffset += triCount;
                    continue;
                }

                // SOFT: кластеры мягкого тела БЕЗ скелета.
                //
                // Раньше мягкий режим шёл общим путём MESH: оболочки получались
                // статичными и без номера, поэтому решателю (NDSC и подобным)
                // было нечего двигать — clusterIndex не выставлялся нигде, а
                // кадры не создавались. Здесь кластеры получают номер и позу
                // покоя: без решателя они стоят и повторяют реальную форму
                // меша, с решателем он забирает кадры и даёт динамику.
                if (target.Mode == DycMode.Soft)
                {
                    int softHulls = BuildSoftHulls(target, src, mesh, verts, tris, info,
                        allHulls, ref report, ref softCluster, s, submeshOfTri, materialBase);

                    if (softHulls > 0)
                    {
                        triangleOffset += triCount;
                        continue;
                    }

                    // Ни одного кластера не собралось (вырожденный меш) — падаем
                    // на общий путь. Это деградация до статичных оболочек без
                    // номеров, а не отказ: объект всё равно получает коллизию.
                }

                // MESH: зона одна на объект, поэтому все треугольники в часть 0.
                for (int t = 0; t < triCount; t++) partOfTri[t] = 0;

                // ---- Метки материалов (кисть)
                if (maskUsable)
                {
                    for (int t = 0; t < triCount; t++)
                    {
                        byte l = mask.Get(triangleOffset + t);
                        labelOfTri[t] = l < groupCount ? l : (byte)0;
                    }
                }
                triangleOffset += triCount;

                // MESH БЕЗ КОСТЕЙ: сначала пробуем РАЗЛОЖЕНИЕ.
                //
                // Вогнутый статичный объект (ящик с открытой крышкой, ниша,
                // арка) выпуклыми оболочками затягивается: оболочка соединяет
                // края впадины. Воксельное ядро умеет работать БЕЗ скелета —
                // веса просто не передаются, — и это единственный способ
                // сохранить впадины у меша.
                //
                // Не вышло (ядро недоступно, геометрия вырождена) — падаем на
                // пространственную кластеризацию ниже. Деградация мягкая.
                // MESH БЕЗ КОСТЕЙ: сначала РАЗЛОЖЕНИЕ.
                //
                // Это и есть «как системный сложный коллайдер»: вогнутый меш
                // режется на выпуклые части, а не затягивается одной оболочкой.
                // У офлайн-разложения то же самое, и разница ровно одна: у нас части
                // помечены костями — часть никогда не пересекает сустав, — а у
                // чисто геометрические. У статичного меша костей нет, но
                // ядро, бюджеты и отчёт те же, поэтому путь один.
                //
                // Не вышло (ядро недоступно, геометрия вырождена) — падаем на
                // пространственную кластеризацию ниже. Деградация мягкая: объект
                // всё равно получит коллизию, просто менее точную.
                if (target.ColliderShape != DycColliderShape.Concave && target.Mode != DycMode.Skin)
                {
                    int vox = BuildVoxelMeshHulls(target, src, mesh, verts, tris, info,
                        allHulls, ref report, progress);

                    if (vox > 0)
                    {
                        report.decomposed = true;
                        continue;
                    }
                }

                // ---- Кластеризация по каждой части × каждой метке
                var byPart = new Dictionary<int, List<int>>();
                for (int t = 0; t < triCount; t++)
                {
                    int p = partOfTri[t];
                    if (p < 0) continue;

                    // Исключённая группа не попадает ни в кластеризацию, ни в
                    // разложение, ни в коллайдеры — ровно то, ради чего она
                    // помечена (плащ, ремни разгрузки и прочие детали, которым
                    // коллайдер не нужен).
                    if (IsGroupExcluded(groups, labelOfTri[t])) continue;

                    if (!byPart.TryGetValue(p, out var l))
                    {
                        l = new List<int>(64);
                        byPart[p] = l;
                    }
                    l.Add(t);
                }

                // ДИАГНОСТИКА ПРИВЯЗКИ.
                //
                // Печатается ДО кластеризации и говорит, сколько зон реально
                // получили треугольники. Если зон в списке 47, а групп здесь 1,
                // то искать надо в привязке треугольников к зонам, а не в
                // кластеризации — раньше именно на этом месте терялось время.
                if (target.Mode == DycMode.Skin)
                {
                    var sample = new System.Text.StringBuilder();
                    int shown = 0;
                    foreach (var kv in byPart)
                    {
                        if (shown >= 6) { sample.Append(" …"); break; }
                        if (shown > 0) sample.Append(", ");
                        sample.Append($"зона {kv.Key}: {kv.Value.Count} три");
                        shown++;
                    }

                    Debug.Log(
                        $"[DYC] Привязка: зон в списке {target.ElementCount}, " +
                        $"получили треугольники {byPart.Count}, " +
                        $"без зоны {report.unassignedTriangles} из {triCount}.\n" +
                        $"  {sample}", target);
                }

                foreach (var kv in byPart)
                {
                    int part = kv.Key;
                    var partTris = kv.Value;

                    // Кости этой части (в режиме Skin один источник на кость, поэтому берём кость по part напрямую)
                    Transform bone = target.Mode == DycMode.Skin && src.skin != null
                        ? BoneForPart(target, src, part)
                        : null;

                    var localVerts = TransformToLocal(verts, src, bone, mesh, target.transform);

                    // ДИАГНОСТИКА ПРОСТРАНСТВ.
                    //
                    // Печатается ВСЕГДА, в обоих режимах: раньше она была только
                    // для Mesh, и «почему-то не напечаталась» само превращалось в
                    // загадку. Считается расхождение для одной вершины: где она в
                    // мире и где окажется после перевода в пространство крепления
                    // (кость в Skin, корень компонента в Mesh).
                    //
                    // Совпало — пространства верные, причина в другом. Разошлось —
                    // видно, на сколько именно метров.
                    if (verts.Length > 0 && localVerts.Length > 0)
                    {
                        Transform meshSpace = src.skin != null ? src.skin.transform
                                            : (src.meshFilter != null ? src.meshFilter.transform : null);
                        Transform attach = bone != null ? bone : target.transform;

                        Vector3 world = meshSpace != null ? meshSpace.TransformPoint(verts[0]) : verts[0];
                        Vector3 viaAttach = attach.TransformPoint(localVerts[0]);
                        float gap = (world - viaAttach).magnitude;

                        // Тернарник внутри интерполяции нельзя: двоеточие в нём
                        // закрывает саму интерполяцию. Поэтому вердикт отдельно.
                        string verdict = gap < 0.001f ? "(норма)" : "(ЭТО И ЕСТЬ СМЕЩЕНИЕ)";
                        string meshName = meshSpace != null ? meshSpace.name : "—";
                        string mode = bone != null ? "Skin" : "Mesh";

                        Debug.Log(
                            $"[DYC] Пространства ({mode}): меш на '{meshName}', крепление '{attach.name}'.\n" +
                            $"  Вершина[0] в мире: {world:F4}\n" +
                            $"  Она же через крепление: {viaAttach:F4}\n" +
                            $"  Расхождение: {gap:F4} м {verdict}",
                            target);
                    }
                    var localTris = new int[partTris.Count * 3];
                    var localPart = new int[partTris.Count];
                    var localLabel = new byte[partTris.Count];
                    for (int i = 0; i < partTris.Count; i++)
                    {
                        int t = partTris[i];
                        localTris[i * 3] = tris[t * 3];
                        localTris[i * 3 + 1] = tris[t * 3 + 1];
                        localTris[i * 3 + 2] = tris[t * 3 + 2];
                        localPart[i] = part;
                        localLabel[i] = labelOfTri[t];
                    }

                    // Если внутри одной части метки различаются, кластеризация естественно разделит их по меткам
                    // Число оболочек на зону.
                    //
                    // SKIN: ВСЕГДА ОДНА. Это и есть суть подхода: одна кость —
                    // один коллайдер, построенный по её треугольникам.
                    //
                    // Пространственная кластеризация (hullsPerPart > 1) для кости
                    // ВРЕДНА, и это не вопрос настройки. Она режет зону по
                    // положению в пространстве, поэтому:
                    //   · внутри одной кости появляются ЩЕЛИ между кусками —
                    //     каждая оболочка покрывает лишь свою часть поверхности;
                    //   · соседние куски при этом ПЕРЕКРЫВАЮТСЯ — оболочка
                    //     всегда выпукла и «затягивает» вогнутости, выходя за
                    //     поверхность.
                    // То есть одновременно и дыры, и дублирование геометрии.
                    // Ровно это и было видно на персонаже.
                    //
                    // MESH: зона обычно одна на весь объект, дробить её полезно —
                    // там кластеризация работает как надо.
                    int hullsForPart = target.Mode == DycMode.Skin
                        ? 1
                        : (info.hullsPerPart > 0 ? info.hullsPerPart : AutoHullsFor(localTris.Length / 3));

                    // ОГРАНИЧЕНИЯ КЛАСТЕРИЗАЦИИ.
                    //
                    // maxTrisPerHull и maxVertsPerHull — это ЖЁСТКИЕ ПОТОЛКИ, при
                    // превышении которых кластер рекурсивно делится. Именно они, а
                    // не число оболочек, определяли результат: при точности Coarse
                    // потолок 120 треугольников резал зону из 47884 треугольников
                    // примерно на 400 кусков. Каждый кусок — обрывок поверхности,
                    // 29 из них выродились в точку и были выброшены, остальные дали
                    // «лепестки», а покрытие вышло 0.0%.
                    //
                    // Для Skin потолки снимаются: одна кость — одна оболочка, и
                    // резать её можно только тогда, когда ЭТОГО ТРЕБУЕТ PhysX
                    // (у выпуклой сетки лимит 255 вершин), а не по произвольному
                    // бюджету. Превышение лимита PhysX не молчит: см. отчёт и
                    // красную подсветку в gizmo.
                    //
                    // У НЕВЫПУКЛОЙ формы лимита 255 нет вовсе, поэтому снимаются оба
                    // потолка: дробить поверхность кости незачем, а бюджет
                    // детализации применяется позже — упрощением сетки, а не
                    // разрезанием на куски (см. BuildSurface).
                    bool concave = target.ColliderShape == DycColliderShape.Concave;
                    bool skin = target.Mode == DycMode.Skin;

                    // ЭТО ЗАПАСНОЙ ПУТЬ. Основной — разложение выше; сюда
                    // попадают только те случаи, когда ядро не дало частей.
                    //
                    // Потолки остаются прежними: для Skin и невыпуклой формы
                    // они снимаются (одна кость — одна оболочка / одна
                    // поверхность), для MESH-выпуклого режут по бюджету
                    // детализации, чтобы запасной результат всё равно был
                    // осмысленным.
                    int maxTris = (skin || concave) ? int.MaxValue : info.trisPerHull;
                    int maxVerts = (skin || concave) ? int.MaxValue : Dyc_Cluster.SafeMaxHullVertices;

                    var clusters = Dyc_Cluster.Build(
                        localVerts, localTris, localPart, localLabel,
                        hullsForPart, maxTris, maxVerts);

                    for (int c = 0; c < clusters.Count; c++)
                    {
                        if (concave)
                        {
                            var surface = BuildSurface(clusters[c], localVerts, localTris, target.ConcaveTriangleBudget, target.ConcaveExact,
                                bone, BindWorldOf(src, bone, target), target.transform, out bool degSurface);
                            if (degSurface) { report.degenerateClusters++; continue; }

                            surface.elementIndex = clusters[c].part;
                            surface.groupIndex = clusters[c].label;

                            // Список вершин и здесь: без него Mesh/Soft нельзя
                            // пересобрать по текущей форме, и «живое» обновление
                            // осталось бы только у персонажей со скелетом.
                            surface.sourceVertices = SourceVertsOf(localTris, clusters[c]);
                            surface.sourceIndex = s;
                            surface.materialIndex = DominantMaterial(submeshOfTri, materialBase, clusters[c]);
                            allHulls.Add(surface);
                            continue;
                        }

                        var hull = BuildHull(clusters[c], localVerts, localTris, info.seamOverlap, bone,
                            BindWorldOf(src, bone, target), target.transform, out bool degenerate);
                        if (degenerate)
                        {
                            report.degenerateClusters++;
                            continue;
                        }
                        hull.elementIndex = clusters[c].part;
                        hull.groupIndex = clusters[c].label;
                        hull.sourceVertices = SourceVertsOf(localTris, clusters[c]);
                        hull.sourceIndex = s;
                        hull.materialIndex = DominantMaterial(submeshOfTri, materialBase, clusters[c]);
                        allHulls.Add(hull);
                    }
                }

                // Снимок bindposes (диагностика в Editor + покрытие)
                if (src.skin != null && mesh.bindposes != null)
                {
                    for (int i = 0; i < mesh.bindposes.Length; i++)
                    {
                        set.bindPoses.Add(mesh.bindposes[i]);
                        Transform b = i < src.skin.bones.Length ? src.skin.bones[i] : null;
                        set.bindBonePaths.Add(b != null ? PathOf(target.transform, b) : null);
                    }

                    if (s == 0)
                    {
                        Transform root = src.skin.rootBone;
                        if (root == null && src.skin.bones != null && src.skin.bones.Length > 0) root = src.skin.bones[0];
                        if (root != null)
                        {
                            set.bindRootBonePath = PathOf(target.transform, root);
                            set.bindRootWorld = BindWorldOf(src, root, target);
                        }
                    }
                }
            }

            set.sourceHash = string.Join("|", hashes);
            set.sourceVertexCount = 0;
            set.sourceTriangleCount = report.sourceTriangles;
            set.sourceSubMeshCount = sources.Count;
            set.hulls = allHulls;

            // Переопределения с костей и чужой скелет — ПОСЛЕ сборки: обоим
            // нужен готовый список оболочек с путями костей.
            ApplyBoneOverrides(target, set);
            ApplyRetarget(target, set, ref report);
            allHulls = set.hulls;

            // Мягкий режим: сколько кластеров и их отпечаток. Число обязано
            // совпадать с тем, что вернёт источник кадров, — иначе двигатель
            // честно поднимет ClusterMismatch, а не разъедется молча.
            // Число кластеров пересчитывается ПОСЛЕ переопределений: они могут
            // убрать оболочку, и оставшийся максимум номера — это и есть
            // число кадров, которое должен подготовить двигатель.
            set.decomposed = report.decomposed;
            set.clusterCount = CountClusters(set.hulls);
            set.softTopologyStamp = SoftStampOf(set.hulls);
            set.unassignedTriangles = report.unassignedTriangles;
            set.degenerateClusters = report.degenerateClusters;

            report.hulls = allHulls.Count;
            for (int i = 0; i < allHulls.Count; i++)
            {
                report.hullVertices += allHulls[i].vertexCount;
                if (allHulls[i].vertexCount > report.maxHullVertices) report.maxHullVertices = allHulls[i].vertexCount;
                report.totalVolume += allHulls[i].volume;
            }
            report.usedTriangles = report.sourceTriangles - report.unassignedTriangles;

            // Предупреждение о 255 вершинах — только для выпуклой формы: это
            // ограничение convex-cooking PhysX, у невыпуклой сетки его нет.
            if (set.colliderShape == DycColliderShape.Convex &&
                report.maxHullVertices > Dyc_Cluster.PhysXMaxHullVertices)
                report.warnings.Add(Dyc_L10n.T("bake.warn.peak", report.maxHullVertices));
            if (report.unassignedTriangles > 0)
                report.warnings.Add(Dyc_L10n.T("bake.warn.unassigned", report.unassignedTriangles));
            if (report.hulls == 0)
                report.warnings.Add(Dyc_L10n.T("bake.warn.none"));

            sw.Stop();
            report.elapsedMs = sw.Elapsed.TotalMilliseconds;
            report.ok = report.hulls > 0;
            if (!report.ok && string.IsNullOrEmpty(report.error))
                report.error = Dyc_L10n.T("bake.err.empty");
            return report;
        }

        // ------------------------------------------------------------------ Источники

        public class Source
        {
            public SkinnedMeshRenderer skin;
            public MeshFilter meshFilter;
            public Mesh mesh;
        }

        public static List<Source> CollectSources(Dyc_DynamicCollision target)
        {
            var list = new List<Source>();
            if (target == null) return list;

            if (target.Mode == DycMode.Soft)
            {
                // Мягкое тело: меш нужен только ради топологии и rest-позы.
                // Ригг НЕ требуется — годится и SkinnedMeshRenderer, и обычный
                // MeshFilter, поэтому пробуем оба и не жалуемся, если скелета нет.
                var softSkin = target.SourceSkin;
                if (softSkin == null) softSkin = target.GetComponentInChildren<SkinnedMeshRenderer>();
                if (softSkin != null)
                {
                    Add(list, softSkin, target);
                    return list;
                }

                var softMesh = target.SourceMesh;
                if (softMesh == null) softMesh = target.GetComponentInChildren<MeshFilter>();
                if (softMesh != null && softMesh.sharedMesh != null)
                    list.Add(new Source { meshFilter = softMesh, mesh = softMesh.sharedMesh });

                return list;
            }

            if (target.Mode == DycMode.Skin)
            {
                var skin = target.SourceSkin;
                if (skin == null) skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null) return list;

                Add(list, skin, target);

                // Дочерние рендереры с тем же родителем (тело + одежда) запекаются вместе
                Transform parent = skin.transform.parent;
                if (parent != null)
                {
                    var siblings = parent.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for (int i = 0; i < siblings.Length; i++)
                    {
                        if (siblings[i] == null || siblings[i] == skin) continue;
                        if (siblings[i].transform.parent != parent) continue;
                        Add(list, siblings[i], target);
                    }
                }
            }
            else
            {
                // Режим Mesh: меш читается с САМОГО компонента, а дети — только
                // если это явно включено.
                //
                // Раньше здесь стоял GetComponentInChildren, который брал первый
                // попавшийся дочерний MeshFilter. На объекте с посторонними
                // дочерними мешами это означало «запекается не то, что видно», и
                // понять это по результату было невозможно: оболочка просто
                // оказывалась от другого меша. Молчаливый автопоиск убран.
                var mf = target.SourceMesh;

                if (mf == null)
                {
                    mf = target.GetComponent<MeshFilter>();

                    if (mf == null)
                    {
                        Debug.LogWarning(
                            $"[DYC] На объекте '{target.name}' нет MeshFilter. " +
                            $"Задайте Source Mesh вручную или включите чтение дочерних мешей.", target);
                        return list;
                    }
                }

                if (mf.sharedMesh != null)
                    list.Add(new Source { meshFilter = mf, mesh = mf.sharedMesh });

                if (!target.SourceIncludesChildren) return list;

                // Дети добавляются к уже найденному мешу, а не вместо него.
                var children = target.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child == null || child == mf) continue;
                    if (child.sharedMesh == null) continue;
                    list.Add(new Source { meshFilter = child, mesh = child.sharedMesh });
                }
            }

            return list;
        }

        // ------------------------------------------------------------------ Отпечаток позы

        /// <summary>Отпечаток текущей позы скелета. Нужен, чтобы не перезапекать
        /// вершины на каждое движение мыши: если матрицы не изменились, старый
        /// кэш остаётся верным и пересчёт можно пропустить.</summary>
        public static double PoseStamp(Dyc_DynamicCollision target)
        {
            return PoseStamp(CollectSources(target));
        }

        public static double PoseStamp(List<Source> sources)
        {
            double h = 17.0;
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src == null) continue;

                if (src.skin != null)
                {
                    h = Mix(h, src.skin.localToWorldMatrix);
                    if (src.skin.rootBone != null) h = Mix(h, src.skin.rootBone.localToWorldMatrix);
                }
                else if (src.meshFilter != null)
                {
                    h = Mix(h, src.meshFilter.transform.localToWorldMatrix);
                }
            }
            return h;
        }

        static double Mix(double h, Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++) h = h * 31.0 + m[i];
            return h;
        }

        static void Add(List<Source> list, SkinnedMeshRenderer skin, Dyc_DynamicCollision target)
        {
            if (skin == null || skin.sharedMesh == null) return;
            if (target != null && target.IsSkinExcluded(skin)) return;
            list.Add(new Source { skin = skin, mesh = skin.sharedMesh });
        }

        // ------------------------------------------------------------------ Привязка к частям

        static int BuildSkinParts(Dyc_DynamicCollision target, Source src, Vector3[] verts, int[] tris,
            int[] partOfTri, float threshold, List<string> warnings)
        {
            var skin = src.skin;
            var mesh = src.mesh;
            var bones = skin.bones;
            var weights = mesh.boneWeights;
            var elements = target.Elements;

            int triCount = tris.Length / 3;
            int unassigned = 0;

            if (bones == null || bones.Length == 0 || weights == null || weights.Length != verts.Length)
            {
                for (int t = 0; t < triCount; t++) partOfTri[t] = -1;
                warnings.Add(Dyc_L10n.T("bake.warn.nobones"));
                return triCount;
            }

            // Вершина → доминирующая кость
            var dominant = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                BoneWeight bw = weights[v];
                int best = -1;
                float bestW = -1f;
                Consider(bw.boneIndex0, bw.weight0, threshold, ref best, ref bestW);
                Consider(bw.boneIndex1, bw.weight1, threshold, ref best, ref bestW);
                Consider(bw.boneIndex2, bw.weight2, threshold, ref best, ref bestW);
                Consider(bw.boneIndex3, bw.weight3, threshold, ref best, ref bestW);
                dominant[v] = best;
            }

            // Кость → element (приоритет ближайшего предка)
            var boneToElement = new int[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                boneToElement[b] = ResolveElement(elements, bones[b]);

            for (int t = 0; t < triCount; t++)
            {
                int d0 = dominant[tris[t * 3]];
                int d1 = dominant[tris[t * 3 + 1]];
                int d2 = dominant[tris[t * 3 + 2]];

                int part = -1;
                // Идеально, когда доминирующая кость совпадает у всех трёх вершин; иначе берём доминирующую кость вершины с наибольшим весом
                if (d0 >= 0 && d0 == d1 && d1 == d2) part = boneToElement[d0];
                else part = PickBest(d0, d1, d2, weights, tris, t, boneToElement);

                if (part < 0)
                {
                    partOfTri[t] = -1;
                    unassigned++;
                }
                else
                {
                    partOfTri[t] = part;
                }
            }

            return unassigned;
        }

        static void Consider(int idx, float w, float threshold, ref int best, ref float bestW)
        {
            if (idx < 0 || w < threshold) return;
            if (w > bestW) { bestW = w; best = idx; }
        }

        static int PickBest(int d0, int d1, int d2, BoneWeight[] weights, int[] tris, int t, int[] boneToElement)
        {
            int bestPart = -1;
            float bestW = -1f;

            Accumulate(d0, tris[t * 3], weights, boneToElement, ref bestPart, ref bestW);
            Accumulate(d1, tris[t * 3 + 1], weights, boneToElement, ref bestPart, ref bestW);
            Accumulate(d2, tris[t * 3 + 2], weights, boneToElement, ref bestPart, ref bestW);

            return bestPart;
        }

        static void Accumulate(int bone, int vertex, BoneWeight[] weights, int[] boneToElement, ref int bestPart, ref float bestW)
        {
            if (bone < 0 || bone >= boneToElement.Length) return;
            int part = boneToElement[bone];
            if (part < 0) return;

            float w = WeightOf(weights[vertex], bone);
            if (w > bestW) { bestW = w; bestPart = part; }
        }

        static float WeightOf(BoneWeight bw, int bone)
        {
            float w = 0f;
            if (bw.boneIndex0 == bone) w += bw.weight0;
            if (bw.boneIndex1 == bone) w += bw.weight1;
            if (bw.boneIndex2 == bone) w += bw.weight2;
            if (bw.boneIndex3 == bone) w += bw.weight3;
            return w;
        }

        /// <summary>Приоритет ближайшего предка: сама кость → вверх до первого element с includeChildren.</summary>
        public static int ResolveElement(List<DycElement> elements, Transform bone)
        {
            if (elements == null || bone == null) return -1;

            for (int i = 0; i < elements.Count; i++)
                if (elements[i] != null && elements[i].bone == bone) return i;

            Transform cur = bone.parent;
            while (cur != null)
            {
                for (int i = 0; i < elements.Count; i++)
                {
                    var el = elements[i];
                    if (el == null || el.bone != cur) continue;
                    if (el.includeChildren) return i;
                }
                cur = cur.parent;
            }
            return -1;
        }

        static Transform BoneForPart(Dyc_DynamicCollision target, Source src, int part)
        {
            var elements = target.Elements;
            if (elements == null || part < 0 || part >= elements.Count) return null;
            var el = elements[part];
            if (el == null) return null;
            if (el.bone != null) return el.bone;

            // У element нет кости (например, корень с includeChildren): берём корневую кость исходного рендерера
            return src.skin != null ? src.skin.rootBone : null;
        }

        // ------------------------------------------------------------------ Локальное пространство

        static Vector3[] TransformToLocal(Vector3[] verts, Source src, Transform bone, Mesh mesh, Transform root)
        {
            var outVerts = new Vector3[verts.Length];

            if (bone == null || src.skin == null || mesh.bindposes == null)
            {
                // Режим Mesh, а также источник без скелета.
                //
                // Оболочка крепится к КОРНЮ КОМПОНЕНТА (bonePath пуст, см.
                // ResolveAttach), а вершины меша лежат в пространстве того
                // объекта, где стоит MeshFilter. Если меш не на самом объекте
                // компонента — а так устроено большинство моделей, где меш лежит
                // на дочернем объекте, — это РАЗНЫЕ пространства.
                //
                // Раньше вершины возвращались как есть, и оболочка уезжала ровно
                // на смещение MeshFilter'а: выглядело как «оболочка рядом с
                // объектом», а при масштабе на дочернем объекте ещё и
                // раздувалась/сжималась. В позе, где меш на том же объекте, что и
                // компонент, ошибки не было — поэтому её и не было видно раньше.
                Transform meshSpace = src.skin != null ? src.skin.transform
                                    : (src.meshFilter != null ? src.meshFilter.transform : null);

                if (meshSpace == null || meshSpace == root)
                {
                    System.Array.Copy(verts, outVerts, verts.Length);
                    return outVerts;
                }

                Matrix4x4 toRoot = root.worldToLocalMatrix * meshSpace.localToWorldMatrix;
                for (int i = 0; i < verts.Length; i++)
                    outVerts[i] = toRoot.MultiplyPoint3x4(verts[i]);
                return outVerts;
            }

            var bones = src.skin.bones;
            int boneIndex = -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == bone) { boneIndex = i; break; }
            }

            if (boneIndex < 0 || boneIndex >= mesh.bindposes.Length)
            {
                System.Array.Copy(verts, outVerts, verts.Length);
                return outVerts;
            }

            Matrix4x4 bind = mesh.bindposes[boneIndex];
            for (int i = 0; i < verts.Length; i++)
                outVerts[i] = bind.MultiplyPoint3x4(verts[i]);
            return outVerts;
        }

        // ------------------------------------------------------------------ Прямой путь для Skin

        /// <summary>
        /// Одна кость — одна оболочка, без зон и без кластеризации.
        ///
        /// Почему отдельным путём, а не настройкой существующего. Существующий
        /// путь устроен так: треугольники → зоны (DycElement) → группы по зонам
        /// → кластеризация по бюджету → оболочки. Каждое звено может исказить
        /// результат, и все три отказа, которые мы ловили, были именно в звеньях:
        /// зона не создана, зона схлопнула всё в одну кость, бюджет порезал зону
        /// на лепестки. Для Skin все эти звенья не нужны вообще: принадлежность
        /// кости уже известна из boneWeights, а дробить кость незачем.
        ///
        /// Возвращает число построенных оболочек.
        /// </summary>
        static int BuildDirectSkinHulls(Dyc_DynamicCollision target, Source src, Mesh mesh,
            Vector3[] verts, int[] tris, int[] partOfTri, byte[] labelOfTri,
            DycPrecisionInfo info, List<DycBakedHull> allHulls, ref DycBakeReport report,
            int sourceIndex, int[] submeshOfTri, int materialBase)
        {
            var skin = src.skin;
            var bones = skin.bones;
            if (bones == null || bones.Length == 0) return 0;

            var weights = mesh.boneWeights;
            int triCount = tris.Length / 3;
            if (weights == null || weights.Length != verts.Length || triCount == 0) return 0;

            // 1. Доминирующая кость каждого треугольника.
            //
            // Считается сумма весов по трём вершинам, а не по одной: треугольник
            // на стыке костей получает ту, что влияет на него сильнее в целом.
            // Никакого порога — треугольник обязан уйти кому-то, иначе в теле
            // появится дыра, которую потом никакая оболочка не закроет.
            var boneOfTri = new int[triCount];
            var triPerBone = new List<int>[bones.Length];

            // Карта исключений: белое в выбранном канале — вершина не попадает
            // в коллайдеры. Дополняет кисть (та размечает ГРАНИ, карта —
            // ВЕРШИНЫ), поэтому проверяется раньше раздела по костям.
            bool[] excludedVerts = BuildExclusionMask(target, mesh, verts.Length);

            for (int t = 0; t < triCount; t++)
            {
                // Треугольник исключён, только если исключены ВСЕ его вершины:
                // частичное исключение оставило бы дыру на границе карты.
                if (excludedVerts != null &&
                    Excluded(excludedVerts, tris[t * 3]) &&
                    Excluded(excludedVerts, tris[t * 3 + 1]) &&
                    Excluded(excludedVerts, tris[t * 3 + 2]))
                {
                    boneOfTri[t] = -1;
                    continue;
                }

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

                // Исключённая кость не получает треугольников: они остаются без
                // раздела, и оболочки на исключённом поддереве не появляются.
                if (bestBone >= 0 && target.IsBoneExcluded(bones[bestBone])) bestBone = -1;

                // Порог веса на кости.
                // Слабое влияние не должно тянуть треугольник на себя: иначе
                // оболочка соседней кости забирает геометрию, которая к ней
                // почти не относится. Своя настройка на кости старше общей.
                if (bestBone >= 0)
                {
                    var bp = bones[bestBone] != null ? bones[bestBone].GetComponent<Dyc_BoneProperties>() : null;
                    if (bp != null && bp.overrideWeightThreshold && bestWeight < bp.boneWeightThreshold)
                        bestBone = -1;
                }

                boneOfTri[t] = bestBone;
                if (bestBone < 0) continue;

                if (triPerBone[bestBone] == null) triPerBone[bestBone] = new List<int>(256);
                triPerBone[bestBone].Add(t);
            }

            // 2. По оболочке на кость.
            int built = 0;
            int skipped = 0;
            var perBone = new List<DycBakedHull>[bones.Length];

            for (int b = 0; b < bones.Length; b++)
            {
                var list = triPerBone[b];
                if (list == null || list.Count == 0) { skipped++; continue; }

                var bone = bones[b];
                var boneVerts = TransformToLocal(verts, src, bone, mesh, target.transform);
                var boneTris = new int[list.Count * 3];
                var partOfLocal = new int[list.Count];
                var labelOfLocal = new byte[list.Count];

                for (int i = 0; i < list.Count; i++)
                {
                    int t = list[i];
                    boneTris[i * 3] = tris[t * 3];
                    boneTris[i * 3 + 1] = tris[t * 3 + 1];
                    boneTris[i * 3 + 2] = tris[t * 3 + 2];
                    partOfLocal[i] = b;
                    labelOfLocal[i] = 0;
                }

                // Кластеризация всё же вызывается, но с ЕДИНСТВЕННОЙ целью —
                // учесть лимит PhysX: если выпуклая оболочка кости не влезает в
                // 255 вершин, её придётся разделить. Бюджет треугольников при
                // этом не ограничивает: int.MaxValue.
                //
                // У НЕВЫПУКЛОЙ формы лимита 255 нет вовсе, и дробить кость незачем:
                // одна кость — одна поверхность. Бюджет детализации применяется
                // позже и иначе — упрощением сетки до trisPerHull, а не разрезом.
                bool concave = target.ColliderShape == DycColliderShape.Concave;

                // Переопределение формы на кости.
                // Позволяет держать хвост поверхностью, а тело — оболочками,
                // не меняя глобальную настройку и не разводя два запекания.
                var boneProps = bone != null ? bone.GetComponent<Dyc_BoneProperties>() : null;
                if (boneProps != null && boneProps.overrideConvex) concave = !boneProps.convex;

                var clusters = Dyc_Cluster.Build(boneVerts, boneTris, partOfLocal, labelOfLocal,
                    1, int.MaxValue,
                    concave ? int.MaxValue : Dyc_Cluster.SafeMaxHullVertices);

                // НЕВЫПУКЛЫЙ ПУТЬ: части собираются ПО КОСТЯМ, а не пишутся в
                // набор напрямую. Так они проходят общий бюджет кости, и окно
                // эксперта получает статистику (раньше оно показывало «53 части
                // на 0 костях»: статистика заполнялась только в выпуклом пути).
                if (concave)
                {
                    var pieces = new List<DycBakedHull>(clusters.Count);

                    for (int c = 0; c < clusters.Count; c++)
                    {
                        var surface = BuildSurface(clusters[c], boneVerts, boneTris, target.ConcaveTriangleBudget, target.ConcaveExact, bone,
                            BindWorldOf(src, bone, target), target.transform, out bool degSurface);
                        if (degSurface) { report.degenerateClusters++; continue; }

                        surface.elementIndex = Dyc_Baker.ResolveElement(target.Elements, bone);
                        surface.groupIndex = 0;
                        surface.sourceVertices = SourceVertsOf(boneTris, clusters[c]);
                        surface.sourceIndex = sourceIndex;
                        surface.materialIndex = DominantMaterial(submeshOfTri, materialBase, clusters[c]);
                        pieces.Add(surface);
                    }

                    perBone[b] = pieces;
                    continue;
                }

                for (int c = 0; c < clusters.Count; c++)
                {
                    var entry = BuildHull(clusters[c], boneVerts, boneTris, info.seamOverlap, bone,
                        BindWorldOf(src, bone, target), target.transform, out bool degenerate);
                    if (degenerate) { report.degenerateClusters++; continue; }

                    entry.elementIndex = Dyc_Baker.ResolveElement(target.Elements, bone);
                    entry.groupIndex = 0;
                    entry.sourceVertices = SourceVertsOf(boneTris, clusters[c]);
                    entry.sourceIndex = sourceIndex;
                    entry.materialIndex = DominantMaterial(submeshOfTri, materialBase, clusters[c]);
                    allHulls.Add(entry);
                    built++;
                }
            }

            // Общий бюджет по костям: он же заполняет статистику, по которой
            // окно эксперта строит список костей и показывает вогнутость.
            if (target.ColliderShape == DycColliderShape.Concave)
                built = ApplyBoneBudgets(target, bones, perBone, allHulls, ref report, 0);

            if (built == 0 && skipped > 0)
            {
                Debug.LogWarning(
                    $"[NDC] Прямой путь Skin: оболочек 0, костей без треугольников {skipped} " +
                    $"из {bones.Length}. Проверьте boneWeights у меша '{mesh.name}'.", target);
            }

            return built;
        }

        // ------------------------------------------------------------------ Прямой путь для Soft

        /// <summary>
        /// Мягкое тело: кластеры, привязанные к костям.
        ///
        /// Отличие от прямого пути Skin ровно одно: кость режется на НЕСКОЛЬКО
        /// кластеров (hullsPerPart), потому что мягкая зона должна гнуться
        /// внутри кости, а не только между костями. Пока решателя нет, каждый
        /// кластер едет за своей костью жёстко — форма следует за позой, но не
        /// деформируется. Решатель забирает кадры и даёт настоящую деформацию.
        ///
        /// Вершины кластера лежат в пространстве его кости — так кадром кластера
        /// становится просто мировая матрица кости, без лишней матричной
        /// алгебры и без шансов ошибиться в порядке умножения.
        ///
        /// Возвращает 0, если скелета или весов нет: тогда вызывающий падает на
        /// общий путь, и мягкое тело остаётся статичным (как было раньше).
        /// </summary>
        static int BuildSoftHulls(Dyc_DynamicCollision target, Source src, Mesh mesh,
            Vector3[] verts, int[] tris, DycPrecisionInfo info,
            List<DycBakedHull> allHulls, ref DycBakeReport report, ref int nextCluster,
            int sourceIndex, int[] submeshOfTri, int materialBase)
        {
            int triCount = tris.Length / 3;
            if (triCount == 0) return 0;

            // СКЕЛЕТ НЕ НУЖЕН. Мягкое тело — это меш без костей, который
            // гнёт РЕШАТЕЛЬ или КОД (NDSC и подобные), а не поза скелета.
            // Раньше здесь требовался skin с boneWeights, и без него мягкий
            // режим молча падал на общий путь: кластеры получались статичными
            // и БЕЗ номера, поэтому решателю было нечего двигать.
            //
            // Геометрия берётся из меша как есть, в пространстве владельца.
            var ownerVerts = TransformToLocal(verts, src, null, mesh, target.transform);

            int hullsPerPart = info.hullsPerPart > 0 ? info.hullsPerPart : 1;

            var partOfTri = new int[triCount];
            var labelOfTri = new byte[triCount];
            for (int t = 0; t < triCount; t++) partOfTri[t] = 0;

            var clusters = Dyc_Cluster.Build(ownerVerts, tris, partOfTri, labelOfTri,
                hullsPerPart, info.trisPerHull, Dyc_Cluster.SafeMaxHullVertices);

            int built = 0;

            for (int c = 0; c < clusters.Count; c++)
            {
                var cluster = clusters[c];
                var used = SourceVertsOf(tris, cluster);
                if (used == null || used.Length < 4) { report.degenerateClusters++; continue; }

                // Центр кластера — он же поза покоя кадра. Оболочка строится
                // ОТНОСИТЕЛЬНО центра: тогда кадр может её поворачивать, а без
                // источника она просто стоит на месте и повторяет настоящую
                // форму меша — это и есть «без решателя считается реальная
                // форма, но без динамики».
                Vector3 center = Vector3.zero;
                for (int i = 0; i < used.Length; i++) center += ownerVerts[used[i]];
                center /= used.Length;

                var points = new List<Vector3>(used.Length);
                for (int i = 0; i < used.Length; i++) points.Add(ownerVerts[used[i]] - center);

                DycHullResult res;
                if (!Dyc_Hull.Build(points, out res, 1e-5f) || !res.ok || res.points == null || res.points.Length < 4)
                { report.degenerateClusters++; continue; }

                if (info.seamOverlap > 0f)
                {
                    for (int i = 0; i < res.points.Length; i++)
                    {
                        Vector3 d = res.points[i] - res.center;
                        if (d.sqrMagnitude > 1e-12f) res.points[i] += d.normalized * info.seamOverlap;
                    }

                    DycHullResult re;
                    if (Dyc_Hull.Build(res.points, out re, 1e-5f) && re.ok) res = re;
                }

                var m = new Mesh { name = "DYC_SoftCluster" };
                m.SetVertices(res.points);
                m.SetTriangles(res.triangles, 0);
                m.RecalculateBounds();
                m.RecalculateNormals();

                var hull = new DycBakedHull
                {
                    mesh = m,
                    bindWorld = Matrix4x4.identity,
                    localCenter = res.center,
                    localSize = m.bounds.size,
                    volume = res.volume,
                    vertexCount = res.points.Length,
                    triangleCount = res.triangles.Length / 3,
                    edges = res.edges,
                    edgeNormals = res.edgeNormals
                };

                hull.elementIndex = 0;
                hull.groupIndex = 0;
                hull.clusterIndex = nextCluster++;
                hull.clusterBonePath = string.Empty;
                hull.clusterRest = center;
                hull.sourceVertices = used;
                hull.sourceIndex = sourceIndex;
                hull.materialIndex = DominantMaterial(submeshOfTri, materialBase, cluster);

                allHulls.Add(hull);
                built++;
            }

            return built;
        }

        /// <summary>
        /// Отпечаток топологии мягкого тела: номер и кость каждого кластера.
        /// Изменился — привязки кадров устарели и нужна перезапечка; по этому
        /// числу это видно, не сравнивая наборы целиком.
        /// </summary>
        /// <summary>Число кластеров мягкого тела: максимум номера плюс один.</summary>
        static int CountClusters(List<DycBakedHull> hulls)
        {
            int max = -1;
            if (hulls == null) return 0;

            for (int i = 0; i < hulls.Count; i++)
            {
                var h = hulls[i];
                if (h != null && h.clusterIndex > max) max = h.clusterIndex;
            }
            return max + 1;
        }

        static int SoftStampOf(List<DycBakedHull> hulls)
        {
            int h = 17;
            if (hulls == null) return h;

            for (int i = 0; i < hulls.Count; i++)
            {
                var x = hulls[i];
                if (x == null || x.clusterIndex < 0) continue;

                h = h * 31 + x.clusterIndex;
                string p = x.clusterBonePath;
                if (!string.IsNullOrEmpty(p))
                    for (int k = 0; k < p.Length; k++) h = h * 31 + p[k];
            }
            return h;
        }

        // ------------------------------------------------------------------ Невыпуклая поверхность

        /// <summary>
        /// НЕвыпуклый коллайдер одного кластера: его настоящая поверхность,
        /// упрощённая до бюджета треугольников.
        ///
        /// ЗАЧЕМ ЭТО ВООБЩЕ. Выпуклая оболочка обязана затянуть впадину, и на
        /// стыках костей (подмышка, пах, шея) между оболочками соседей остаются
        /// дыры — это свойство оболочки, а не ошибка запекания. Невыпуклая сетка
        /// дыр не имеет вовсе, и именно так работает системный коллайдер в режиме
        /// выпуклость выключена.
        ///
        /// ЧЕМ ПЛАТИМ, и это записано в проверке здоровья, а не спрятано:
        ///   · PhysX принимает невыпуклую сетку только на кинематическом или
        ///     анимационном теле — подвижное твёрдое тело с ней не работает;
        ///   · столкновения mesh-mesh не поддерживаются;
        ///   · нет быстрой широкой фазы, поэтому на мобильных это дороже оболочек.
        ///
        /// ПОЧЕМУ УПРОЩЕНИЕ, А НЕ РАЗРЕЗ. У выпуклой формы бюджет треугольников
        /// выдерживался тем, что кластер резался на части и каждая часть
        /// заменялась оболочкой: 3000 треугольников кости превращались в 250
        /// вершин оболочки. У невыпуклой так нельзя — разрез вернул бы швы ровно
        /// там, ради чего режим и существует. Поэтому бюджет выдерживается
        /// упрощением самой сетки (см. BuildDecimatedSurface), а кластер остаётся
        /// одним куском.
        /// </summary>
        static DycBakedHull BuildSurface(DycCluster cluster, Vector3[] verts, int[] tris,
            int triBudget, bool exact, Transform bone, Matrix4x4 bindWorld, Transform root, out bool degenerate)
        {
            degenerate = false;

            // БЮДЖЕТ ЗДЕСЬ МЯГКИЙ, И ЭТО ОСОЗНАННО.
            //
            // В выпуклом пути trisPerHull — жёсткий потолок: кластер режется, и
            // каждая часть заменяется оболочкой, поэтому 3000 треугольников кости
            // спокойно превращаются в 250 вершин. У невыпуклой формы так нельзя:
            // оболочка обязана затянуть впадину, то есть ровно то, ради чего
            // режим и существует.
            //
            // Остаётся упрощение самой сетки. Но сведение вершин к узлам сетки
            // НЕ УМЕЕТ делать из тонкой геометрии грубую: палец или антенна
            // тоньше шага сетки схлопываются не в грубую форму, а В НИЧТО. Поэтому
            // мелкие кластеры берутся как есть, а упрощение включается только
            // тогда, когда кластер заметно больше разумного, и никогда не доводит
            // поверхность до пустоты.
            //
            // Цена честно записана в проверке здоровья: у невыпуклой сетки нет
            // быстрой широкой фазы, поэтому на мобильных она дороже оболочек.
            int budget = triBudget > 0 ? triBudget : 250;

            // exact — «не упрощать вовсе» (крайнее положение слайдера).
            // long, а не int: при int.MaxValue произведение переполнилось бы и
            // cap стал бы отрицательным, то есть упрощение включилось бы ровно
            // там, где его просили выключить.
            long cap = exact ? long.MaxValue : (long)budget * 8;

            Mesh m;

            if (exact || cluster.tris.Count <= cap)
            {
                // Мелкий кластер — точная поверхность, только дедупликация.
                m = BuildExactSurface(verts, tris, cluster.tris);
            }
            else
            {
                m = BuildDecimatedSurface(verts, tris, cluster.tris, budget);

                // Упрощение схлопнуло всё в ничто — тонкая геометрия. Берём
                // точную поверхность: пустой коллайдер хуже дорогого.
                if (m == null) m = BuildExactSurface(verts, tris, cluster.tris);
            }

            if (m == null)
            {
                degenerate = true;
                return null;
            }

            // Рёбра силуэта считаются по ГОТОВОЙ сетке, а не по исходной: гизмо
            // рисует то, что реально попало в коллайдер.
            var pts = m.vertices;
            var idx = m.triangles;
            var edges = Dyc_Hull.SilhouetteEdges(pts, idx, out var edgeNormals);

            return new DycBakedHull
            {
                mesh = m,
                bonePath = bone != null ? PathOf(root, bone) : string.Empty,
                bindWorld = bindWorld,
                localCenter = m.bounds.center,
                localSize = m.bounds.size,
                volume = MeshVolume(pts, idx),
                vertexCount = m.vertexCount,
                triangleCount = idx.Length / 3,
                edges = edges,
                edgeNormals = edgeNormals,
                nonConvex = true
            };
        }

        /// <summary>
        /// Точная поверхность кластера: только его треугольники, вершины
        /// дедуплицированы.
        ///
        /// Дедупликация обязательна: у меша вершины на стыке граней общие, а
        /// после выбора подмножества треугольников индексы «дырявые», и
        /// MeshCollider с неиспользованными вершинами дороже и капризнее.
        /// </summary>
        static Mesh BuildExactSurface(Vector3[] verts, int[] tris, List<int> triList)
        {
            if (verts == null || tris == null || triList == null || triList.Count == 0) return null;

            var map = new Dictionary<int, int>(triList.Count * 3);
            var pts = new List<Vector3>(triList.Count * 3);
            var outTris = new List<int>(triList.Count * 3);

            for (int i = 0; i < triList.Count; i++)
            {
                int t = triList[i];
                if (t < 0 || t * 3 + 2 >= tris.Length) continue;

                var a = new int[3];
                bool bad = false;

                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (v < 0 || v >= verts.Length) { bad = true; break; }

                    if (!map.TryGetValue(v, out int ni))
                    {
                        ni = pts.Count;
                        map[v] = ni;
                        pts.Add(verts[v]);
                    }
                    a[k] = ni;
                }

                if (bad) continue;
                if (a[0] == a[1] || a[1] == a[2] || a[0] == a[2]) continue;

                outTris.Add(a[0]);
                outTris.Add(a[1]);
                outTris.Add(a[2]);
            }

            if (pts.Count < 3 || outTris.Count < 3) return null;

            var m = new Mesh { name = "DYC_Surface" };
            m.SetVertices(pts);
            m.SetTriangles(outTris, 0);
            m.RecalculateBounds();
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Собирает поверхность кластера и упрощает её до бюджета треугольников.
        ///
        /// УПРОЩЕНИЕ — КЛАСТЕРИЗАЦИЯ ВЕРШИН ПО СЕТКЕ. Вершины притягиваются к
        /// узлам кубической сетки, попавшие в один узел сливаются, вырожденные
        /// треугольники выбрасываются. Чем МЕНЬШЕ шаг сетки, тем БОЛЬШЕ
        /// треугольников остаётся.
        ///
        /// ПОЧЕМУ ПОИСК ШАГА УСТРОЕН ИМЕННО ТАК. Зависимость не линейная: на
        /// грубой сетке вся кость сливается в одну-две вершины и треугольников
        /// не остаётся ВООБЩЕ, а уже на вдвое более мелкой их могут быть тысячи.
        /// Поэтому «уменьшать шаг, пока не влезем» — неверно (перебор), а
        /// «увеличивать, пока не появится хоть что-то» — тоже (выйдет за бюджет).
        ///
        /// Здесь сначала находится шаг, на котором треугольники вообще
        /// появляются, затем бисекция между «пусто» и «перебор» ищет САМЫЙ
        /// МЕЛКИЙ шаг, который ещё помещается в бюджет, — то есть максимум
        /// детализации при соблюдении бюджета. Если бюджет недостижим в принципе
        /// (геометрия вырождена и любой непустой результат его превышает),
        /// возвращается непустой результат: невыпуклая сетка без лимита вершин
        /// рабочая, а без геометрии — нет.
        ///
        /// Почему не «умное» упрощение с рёбрами. Здесь нет ни зависимостей, ни
        /// квадратичных проходов: на кластере в 47 тысяч треугольников это доли
        /// секунды, и результат предсказуем. Ошибка сглаживания в доли
        /// миллиметра для коллайдера hitbox'а не важна — важнее, что форма
        /// НЕвыпуклая и что бюджет соблюдён.
        /// </summary>
        static Mesh BuildDecimatedSurface(Vector3[] verts, int[] tris, List<int> triList, int triBudget)
        {
            if (verts == null || tris == null || triList == null || triList.Count == 0) return null;

            int budget = triBudget > 0 ? triBudget : 250;

            // Границы кластера — по ним выбирается стартовый шаг сетки.
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < triList.Count; i++)
            {
                int t = triList[i];
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (v < 0 || v >= verts.Length) continue;
                    var p = verts[v];
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }

            float span = (max - min).magnitude;
            if (span <= 0f) return null;

            // Переиспользуемые буферы: поиск вызывает счётчик десятки раз, и
            // каждый вызов с новой аллокацией List'ов был бы заметен.
            var pts = new List<Vector3>(1024);
            var outTris = new List<int>(4096);
            var nodeOf = new Dictionary<long, int>(4096);
            var sums = new List<Vector3>(1024);
            var counts = new List<int>(1024);

            // 1. Грубый конец: уменьшаем шаг, пока не появится хоть один
            //    треугольник. Совсем грубая сетка сливает кость в точку.
            float coarse = span * 0.5f;
            int coarseCount = 0;
            for (int i = 0; i < 24; i++)
            {
                coarseCount = SnapCount(verts, tris, triList, coarse, pts, outTris, nodeOf, sums, counts);
                if (coarseCount > 0) break;
                coarse *= 0.5f;
            }

            if (coarseCount <= 0) return null;

            // 2. Мелкий конец: если грубый шаг уже в бюджете, мельчим, пока
            //    очередной шаг не даст перебор. Это сразу даёт ответ для
            //    обычного случая и границу для бисекции в остальных.
            float fine = coarse;
            int fineCount = coarseCount;
            bool fineOver = false;

            if (coarseCount <= budget)
            {
                for (int i = 0; i < 16; i++)
                {
                    float next = fine * 0.5f;
                    int n = SnapCount(verts, tris, triList, next, pts, outTris, nodeOf, sums, counts);
                    if (n == 0) { fine = next; fineCount = 0; continue; }

                    fine = next;
                    fineCount = n;
                    if (n > budget) { fineOver = true; break; }
                }
            }

            // 3. Бисекция между «влезает» и «перебор» — если перебор вообще
            //    был достигнут. Иначе грубый шаг уже и есть максимум детализации.
            if (fineOver && fineCount > budget)
            {
                float lo = coarse;   // влезает
                float hi = fine;     // перебор
                for (int i = 0; i < 20; i++)
                {
                    float mid = Mathf.Sqrt(lo * hi);
                    int n = SnapCount(verts, tris, triList, mid, pts, outTris, nodeOf, sums, counts);
                    if (n == 0) { hi = mid; continue; }
                    if (n <= budget) lo = mid; else hi = mid;
                }
                coarse = lo;
            }

            // 4. Сборка финальной сетки.
            SnapCount(verts, tris, triList, coarse, pts, outTris, nodeOf, sums, counts);
            if (pts.Count < 3 || outTris.Count < 3) return null;

            var m = new Mesh { name = "DYC_Surface" };
            m.SetVertices(pts);
            m.SetTriangles(outTris, 0);
            m.RecalculateBounds();
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Одна попытка упрощения: возвращает число треугольников и заполняет
        /// pts/outTris. Буферы передаются снаружи и переиспользуются.
        /// </summary>
        static int SnapCount(Vector3[] verts, int[] tris, List<int> triList, float cell,
            List<Vector3> pts, List<int> outTris,
            Dictionary<long, int> nodeOf, List<Vector3> sums, List<int> counts)
        {
            pts.Clear();
            outTris.Clear();
            nodeOf.Clear();
            sums.Clear();
            counts.Clear();

            if (cell <= 0f) return 0;

            float inv = 1f / cell;
            var a = new int[3];

            for (int i = 0; i < triList.Count; i++)
            {
                int t = triList[i];
                if (t < 0 || t * 3 + 2 >= tris.Length) continue;

                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (v < 0 || v >= verts.Length) { a[k] = -1; continue; }

                    // Ключ узла: три округлённые координаты, упакованные в long.
                    // 21 бит на ось — это ±1 млн шагов сетки, заведомо больше
                    // любого разумного меша.
                    var p = verts[v];
                    long gx = (long)System.Math.Round(p.x * inv) + (1 << 20);
                    long gy = (long)System.Math.Round(p.y * inv) + (1 << 20);
                    long gz = (long)System.Math.Round(p.z * inv) + (1 << 20);
                    gx = System.Math.Min(System.Math.Max(gx, 0L), (1L << 21) - 1);
                    gy = System.Math.Min(System.Math.Max(gy, 0L), (1L << 21) - 1);
                    gz = System.Math.Min(System.Math.Max(gz, 0L), (1L << 21) - 1);
                    long node = (gx << 42) | (gy << 21) | gz;

                    if (!nodeOf.TryGetValue(node, out int ni))
                    {
                        ni = sums.Count;
                        nodeOf[node] = ni;
                        sums.Add(Vector3.zero);
                        counts.Add(0);
                    }

                    sums[ni] = sums[ni] + p;
                    counts[ni] = counts[ni] + 1;
                    a[k] = ni;
                }

                // Вырожденный треугольник (две вершины в одном узле) в коллайдер
                // не идёт: он ничего не ограничивает, но PhysX его обрабатывает.
                if (a[0] < 0 || a[1] < 0 || a[2] < 0) continue;
                if (a[0] == a[1] || a[1] == a[2] || a[0] == a[2]) continue;

                outTris.Add(a[0]);
                outTris.Add(a[1]);
                outTris.Add(a[2]);
            }

            if (sums.Count < 3 || outTris.Count < 3) return 0;

            for (int i = 0; i < sums.Count; i++)
                pts.Add(sums[i] / Mathf.Max(1, counts[i]));

            return outTris.Count / 3;
        }

        /// <summary>Выбирает кость с большим весом. Отдельный метод, потому что
        /// сравнение повторяется четыре раза на вершину и три вершины на
        /// треугольник — на 47 тысячах треугольников это заметно.</summary>
        static int Better(Transform[] bones, int index, float weight, int bestBone, ref float bestWeight)
        {
            if (weight <= bestWeight) return bestBone;
            if (index < 0 || index >= bones.Length) return bestBone;
            if (bones[index] == null) return bestBone;

            bestWeight = weight;
            return index;
        }

        // ------------------------------------------------------------------ Разложение для Skin

        /// <summary>
        /// Вогнутые коллайдеры для скелета: разложение ЦЕЛОГО меша на выпуклые
        /// части, затем распределение частей по костям.
        ///
        /// Почему разлагается ЦЕЛОЕ тело, а не каждая кость отдельно. Ядро
        /// воксельное: ему нужен ОБЪЁМ, а не поверхность. Кусок меша, выбранный
        /// по весам одной кости, — открытая поверхность, и вокселизация такого
        /// куска даёт мусор. Целый меш персонажа замкнут, поэтому разложение
        /// получается осмысленным.
        ///
        /// Почему части потом «приписываются» костям, а не режутся по костям
        /// заранее. Так границы частей определяет форма тела, а не скелет: части
        /// не разрезаны по суставам, зато каждая из них — настоящая выпуклая
        /// деталь формы. Привязка к кости нужна лишь для того, чтобы деталь
        /// двигалась вместе с телом.
        ///
        /// Возвращает число построенных оболочек; 0 означает «разложение не
        /// сработало», и вызывающий обязан откатиться на одну оболочку на кость.
        /// </summary>
        static int BuildConcaveSkinHulls(Dyc_DynamicCollision target, Source src, Mesh mesh,
            Vector3[] verts, int[] tris, DycPrecisionInfo info,
            List<DycBakedHull> allHulls, ref DycBakeReport report, DycBakeProgress progress)
        {
            if (src.skin == null || mesh == null) return 0;

            var bones = src.skin.bones;
            var weights = mesh.boneWeights;
            if (bones == null || bones.Length == 0) return 0;
            if (weights == null || weights.Length != verts.Length) return 0;

            var settings = target.DecomposeSettings;

            // РАЗДВИЖЕНИЕ ПРИЖАТЫХ ЧАСТЕЙ. Ядро геометрическое и не знает про
            // кости, поэтому руки вдоль туловища и сведённые ноги для него —
            // один сплошной объём, и резать его оно не станет. Зазор в доли
            // миллиметра даёт ему границу, по которой можно резать.
            //
            // Копия живёт только здесь: в коллайдеры и в игру уходит исходная
            // геометрия, поэтому зазор выбирается по удобству счёта.
            // ПРЕДВЫЧИСЛЕННОЕ РАЗЛОЖЕНИЕ.
            //
            // Фоновое задание (Dyc_BakeJob) делает самое долгое — разделение
            // костей и воксельное разложение — пока редактор продолжает
            // перерисовываться. Здесь остаётся только собрать оболочки из уже
            // готовых частей. Если кэша нет, всё считается на месте, как раньше:
            // запекание остаётся рабочим и без фонового пути.
            if (settings.kernel != DycDecomposeKernel.Native
                && TryTakePrecomputed(mesh, settings, out var precomputed))
            {
                report.separatedVertices += precomputed.separatedVertices;
                return AssembleVoxelPieces(target, src, mesh, bones, precomputed.pieces, allHulls, ref report);
            }

            Mesh decompositionMesh = mesh;
            Mesh separated = null;

            if (settings.separateByBones && settings.separationMm > 0f)
            {
                // ЗАЗОР ОБЯЗАН БЫТЬ БОЛЬШЕ ВОКСЕЛЯ.
                //
                // Смысл разделения — чтобы воксельная сетка УВИДЕЛА щель между
                // костями. При зазоре 2 мм и вокселе 12 мм щели для сетки не
                // существует: воксели соседних костей сливаются в один объём, и
                // часть одной кости заходит на соседнюю. Снаружи это выглядело
                // как «плечо заглатывает начало руки». Поэтому зазор поднимается
                // до полутора вокселей, если заданный меньше.
                float gapMm = Mathf.Max(settings.separationMm, settings.voxelSizeMm * 1.5f);

                separated = Dyc_BoneSeparator.Separate(mesh, weights, bones,
                    gapMm * 0.001f,
                    gapMm * 0.004f,
                    out int movedVertices);

                if (separated != null)
                {
                    decompositionMesh = separated;
                    report.separatedVertices += movedVertices;
                }
            }

            // СВОЁ ЯДРО. Части приходят с готовыми метками костей: метка ставится
            // на воксель ДО разрезания, поэтому часть не пересекает сустав, и
            // отдельный «арбитраж границы» после разложения не нужен.
            if (settings.kernel != DycDecomposeKernel.Native)
            {
                if (progress != null) progress.Report(0.05f, Dyc_L10n.T("bake.stage.decompose"));

                Vector3[] decompositionVerts = separated != null ? separated.vertices : verts;

                // Признаки костей считаются ЗДЕСЬ, на главном потоке: само
                // разложение может считаться в фоне, а обращаться к
                // UnityEngine.Object из чужого потока нельзя.
                var boneValid = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) boneValid[i] = bones[i] != null;

                bool ok = Dyc_VoxelDecomposer.Decompose(decompositionVerts, tris, weights, boneValid,
                    settings, out var voxPieces, out string ownError, progress);

                if (separated != null) Object.DestroyImmediate(separated);

                if (!ok || voxPieces == null || voxPieces.Count == 0)
                {
                    if (report.warnings.Count < 8)
                        report.warnings.Add("Своё разложение не дало частей: " +
                            (string.IsNullOrEmpty(ownError) ? "неизвестная причина" : ownError));
                    return 0;
                }

                if (progress != null) progress.Report(0.72f, Dyc_L10n.T("bake.stage.assemble"));
                return AssembleVoxelPieces(target, src, mesh, bones, voxPieces, allHulls, ref report);
            }

            // out-переменные объявляются ДО вызова: короткое замыкание
            // (ядро недоступно) оставило бы их неинициализированными, а
            // компилятор это справедливо запрещает.
            List<Mesh> pieces = null;
            string error = null;

            bool decomposed = Dyc_ConcaveKernel.Available
                && Dyc_ConcaveKernel.Decompose(decompositionMesh, settings, out pieces, out error);

            if (separated != null) Object.DestroyImmediate(separated);

            if (!decomposed)
            {
                Dyc_ConcaveKernel.ReportError(Dyc_ConcaveKernel.LastError ?? error);
                return 0;
            }
            if (pieces == null || pieces.Count == 0) return 0;

            // 1. Доминирующая кость КАЖДОЙ ВЕРШИНЫ. Именно вершины, а не
            // треугольника: части приходят новыми вершинами, и связать их с
            // исходным мешем можно только через ближайшую вершину.
            var vertexBone = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                var bw = weights[v];
                float best = -1f;
                int bestBone = -1;
                bestBone = Better(bones, bw.boneIndex0, bw.weight0, bestBone, ref best);
                bestBone = Better(bones, bw.boneIndex1, bw.weight1, bestBone, ref best);
                bestBone = Better(bones, bw.boneIndex2, bw.weight2, bestBone, ref best);
                bestBone = Better(bones, bw.boneIndex3, bw.weight3, bestBone, ref best);
                vertexBone[v] = bestBone;
            }

            var grid = new VertexGrid(verts, 64);
            var perBone = new List<DycBakedHull>[bones.Length];

            // 2. Часть → кость по большинству ближайших исходных вершин.
            int unassigned = 0;
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                if (piece == null) continue;

                var pieceVerts = piece.vertices;
                var tally = new Dictionary<int, int>();

                for (int i = 0; i < pieceVerts.Length; i++)
                {
                    int nearest = grid.Nearest(pieceVerts[i]);
                    if (nearest < 0) continue;

                    int b = vertexBone[nearest];
                    if (b < 0) continue;

                    tally.TryGetValue(b, out int count);
                    tally[b] = count + 1;
                }

                int boneIndex = -1;
                int bestCount = 0;
                foreach (var kv in tally)
                {
                    if (kv.Value <= bestCount) continue;
                    bestCount = kv.Value;
                    boneIndex = kv.Key;
                }

                if (boneIndex < 0) { unassigned++; continue; }

                var bone = bones[boneIndex];
                var local = TransformToLocal(pieceVerts, src, bone, mesh, target.transform);

                var entry = AssemblePiece(piece, local, bone, src, target);
                if (entry == null) continue;

                if (perBone[boneIndex] == null) perBone[boneIndex] = new List<DycBakedHull>(8);
                perBone[boneIndex].Add(entry);
            }

            return ApplyBoneBudgets(target, bones, perBone, allHulls, ref report, unassigned);
        }

        // ------------------------------------------------------------------ Сборка частей своего ядра

        /// <summary>
        /// Собирает оболочки из частей своего воксельного разложения.
        ///
        /// Кость у части уже есть (метка вокселя), поэтому никакой привязки по
        /// ближайшим вершинам здесь не нужно — в этом и выигрыш этапа 3.
        /// </summary>
        static int AssembleVoxelPieces(Dyc_DynamicCollision target, Source src, Mesh mesh,
            Transform[] bones, List<DycVoxelPiece> voxPieces,
            List<DycBakedHull> allHulls, ref DycBakeReport report)
        {
            var perBone = new List<DycBakedHull>[bones.Length];
            int unassigned = 0;

            for (int p = 0; p < voxPieces.Count; p++)
            {
                var piece = voxPieces[p];
                int bi = piece.boneIndex;
                if (bi < 0 || bi >= bones.Length || bones[bi] == null)
                {
                    unassigned++;
                    continue;
                }

                var bone = bones[bi];
                var local = TransformToLocal(piece.points, src, bone, mesh, target.transform);
                var entry = AssemblePiece(piece.triangles, local, bone, src, target);
                if (entry == null) continue;

                if (perBone[bi] == null) perBone[bi] = new List<DycBakedHull>(8);
                perBone[bi].Add(entry);
            }

            return ApplyBoneBudgets(target, bones, perBone, allHulls, ref report, unassigned);
        }

        /// <summary>
        /// Разложение МЕША БЕЗ КОСТЕЙ: воксельное ядро вызывается без весов и
        /// без костей. Части собираются в пространстве владельца, каждая
        /// получает номер кластера и позу покоя — значит, «живое» обновление и
        /// решатель мягкого тела могут ими двигать, как и мягкими кластерами.
        ///
        /// Возвращает 0, если ядро не дало частей: вызывающий обязан иметь
        /// запасной путь, иначе вогнутый объект остался бы вообще без коллизии.
        /// </summary>
        static int BuildVoxelMeshHulls(Dyc_DynamicCollision target, Source src, Mesh mesh,
            Vector3[] verts, int[] tris, DycPrecisionInfo info,
            List<DycBakedHull> allHulls, ref DycBakeReport report, DycBakeProgress progress)
        {
            var settings = target.DecomposeSettings;

            if (progress != null) progress.Report(0.05f, Dyc_L10n.T("bake.stage.decompose"));

            List<DycVoxelPiece> pieces;
            string error;
            bool ok = Dyc_VoxelDecomposer.Decompose(verts, tris, null, null, settings,
                out pieces, out error, progress);

            if (!ok || pieces == null || pieces.Count == 0)
            {
                if (report.warnings.Count < 8)
                    report.warnings.Add("Разложение меша не дало частей: " +
                        (string.IsNullOrEmpty(error) ? "неизвестная причина" : error));
                return 0;
            }

            if (progress != null) progress.Report(0.72f, Dyc_L10n.T("bake.stage.assemble"));

            int built = 0;

            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];

                var local = TransformToLocal(piece.points, src, null, mesh, target.transform);
                var entry = AssemblePiece(piece.triangles, local, null, src, target);
                if (entry == null) continue;

                entry.elementIndex = 0;
                entry.groupIndex = 0;
                entry.clusterIndex = -1;
                entry.bonePath = string.Empty;

                allHulls.Add(entry);
                built++;
            }

            return built;
        }

        // ------------------------------------------------------------------ Общий бюджет по костям

        /// <summary>
        /// Бюджет на кость: оставляем самые крупные части.
        ///
        /// Объединять выпуклые части нельзя — их сумма не выпукла. Поэтому
        /// лишние ОТБРАСЫВАЮТСЯ, начиная с самых мелких, и отброшенный объём
        /// честно попадает в отчёт. Так бюджет — это управляемый компромисс
        /// «покрытие против стоимости широкой фазы», а не тихая потеря.
        ///
        /// Общий для обоих ядер: и нативное V-HACD, и своё воксельное дают на
        /// выходе один и тот же perBone, поэтому и бюджет у них один.
        /// </summary>
        static int ApplyBoneBudgets(Dyc_DynamicCollision target, Transform[] bones,
            List<DycBakedHull>[] perBone, List<DycBakedHull> allHulls,
            ref DycBakeReport report, int unassigned)
        {
            if (report.boneStats == null) report.boneStats = new List<DycBoneStat>(bones.Length);

            int built = 0;
            int droppedTotal = 0;

            for (int b = 0; b < perBone.Length; b++)
            {
                var list = perBone[b];
                if (list == null) continue;

                string bonePath = PathOf(target.transform, bones[b]);
                int budget = target.BudgetFor(bonePath);

                // Вогнутость кости = 1 − объём(набор частей) / объём(выпуклая
                // оболочка того же набора).
                //
                // Считается ПО ЧАСТЯМ, а не по исходным треугольникам кости:
                // части уже собраны, их вершины немногочисленны, и оболочка по
                // ним строится мгновенно. Это ровно та величина, которая
                // объясняет пользователю, почему кость получила столько частей:
                // ноль — кость выпуклая, единица — сильно вогнутая.
                float piecesVolume = 0f;
                var piecePoints = new List<Vector3>(list.Count * 32);
                for (int i = 0; i < list.Count; i++)
                {
                    piecesVolume += list[i].volume;
                    var pv = list[i].mesh.vertices;
                    for (int v = 0; v < pv.Length; v++) piecePoints.Add(pv[v]);
                }

                float hullVolume = 0f;
                if (piecePoints.Count >= 4 && Dyc_Hull.Build(piecePoints, out var hullResult, 1e-5f))
                    hullVolume = hullResult.volume;

                float concavity = hullVolume > 1e-6f
                    ? Mathf.Clamp01(1f - piecesVolume / hullVolume)
                    : 0f;

                int dropped = 0;
                if (list.Count > budget)
                {
                    list.Sort((x, y) => y.volume.CompareTo(x.volume));
                    dropped = list.Count - budget;
                    droppedTotal += dropped;
                    list.RemoveRange(budget, list.Count - budget);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].elementIndex = ResolveElement(target.Elements, bones[b]);
                    list[i].groupIndex = 0;
                    allHulls.Add(list[i]);
                    built++;
                }

                report.boneStats.Add(new DycBoneStat
                {
                    bonePath = bonePath,
                    boneName = bones[b] != null ? bones[b].name : "?",
                    pieces = list.Count,
                    dropped = dropped,
                    concavity = concavity,
                    volume = piecesVolume
                });
            }

            if (unassigned > 0 && report.warnings.Count < 8)
                report.warnings.Add($"Разложение: частей без кости {unassigned} (отброшены).");

            if (droppedTotal > 0 && report.warnings.Count < 8)
                report.warnings.Add(
                    $"Разложение: отброшено частей по бюджету {droppedTotal}. " +
                    "Поднимите бюджет кости в окне эксперта или уменьшите вогнутость.");

            return built;
        }

        /// <summary>Собирает оболочку из готовой выпуклой части.</summary>
        static DycBakedHull AssemblePiece(Mesh piece, Vector3[] localVertices,
            Transform bone, Source src, Dyc_DynamicCollision target)
        {
            return piece == null ? null : AssemblePiece(piece.triangles, localVertices, bone, src, target);
        }

        /// <summary>
        /// То же, но треугольники задаются напрямую. Нужно своему ядру: части
        /// приходят не мешем, а массивами вершин и треугольников, и заводить
        /// ради этого временный Mesh незачем.
        /// </summary>
        static DycBakedHull AssemblePiece(int[] triangles, Vector3[] localVertices,
            Transform bone, Source src, Dyc_DynamicCollision target)
        {
            if (localVertices == null || localVertices.Length < 4) return null;
            if (triangles == null || triangles.Length < 12) return null;

            // Без hideFlags: этот меш сохраняется ПОД-АССЕТОМ набора запекания.
            // DontSave запрещает такую запись, и AssetDatabase падает с
            // ассертом — проверено на живом запекании.
            var mesh = new Mesh { name = "DYC_Piece" };
            mesh.SetVertices(localVertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            // Только настоящие рёбра (с объединением компланарных граней) плюс
            // нормали смежных граней. Полный набор рёбер триангуляции давал в
            // гизмо «залитую» поверхность с рваным силуэтом вместо чистых линий.
            var edges = Dyc_Hull.SilhouetteEdges(localVertices, triangles, out var edgeNormals);

            return new DycBakedHull
            {
                mesh = mesh,
                bonePath = bone != null ? PathOf(target.transform, bone) : string.Empty,
                bindWorld = BindWorldOf(src, bone, target),
                localCenter = mesh.bounds.center,
                localSize = mesh.bounds.size,
                volume = MeshVolume(localVertices, triangles),
                vertexCount = localVertices.Length,
                triangleCount = triangles.Length / 3,
                edges = edges,
                edgeNormals = edgeNormals
            };
        }

        /// <summary>
        /// Объём замкнутой сетки через теорему о дивергенте: сумма знаковых
        /// объёмов тетраэдров от начала координат. Для незамкнутой сетки даёт
        /// приближение — но части от ядра замкнуты по построению, поэтому здесь
        /// это честный объём.
        /// </summary>
        static float MeshVolume(Vector3[] vertices, int[] triangles)
        {
            double total = 0.0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;

                Vector3 p0 = vertices[a], p1 = vertices[b], p2 = vertices[c];
                total += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6.0;
            }
            return Mathf.Abs((float)total);
        }

        /// <summary>
        /// Рёбра сетки без дублей, ГОТОВЫЕ К ОТРИСОВКЕ: пара координат на ребро.
        ///
        /// Именно координаты, а не индексы: gizmo рисует линии через
        /// Handles.DrawLines, а он принимает готовые точки. Хранить индексы и
        /// пересчитывать при отрисовке значило бы таскать за собой ещё и
        /// вершины, которых у запечённой оболочки уже нет.
        /// </summary>
        static Vector3[] EdgesFromTriangles(Vector3[] vertices, int[] triangles)
        {
            var seen = new HashSet<long>();
            var edges = new List<Vector3>(triangles.Length);

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                AddEdge(seen, edges, vertices, a, b);
                AddEdge(seen, edges, vertices, b, c);
                AddEdge(seen, edges, vertices, c, a);
            }

            return edges.ToArray();
        }

        static void AddEdge(HashSet<long> seen, List<Vector3> edges, Vector3[] vertices, int a, int b)
        {
            if (a < 0 || b < 0 || a >= vertices.Length || b >= vertices.Length) return;

            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!seen.Add(key)) return;

            edges.Add(vertices[a]);
            edges.Add(vertices[b]);
        }

        /// <summary>
        /// Пространственная сетка исходных вершин для поиска ближайшей.
        ///
        /// Нужна потому, что ядро возвращает части НОВЫМИ вершинами, и связать
        /// их с весами костей можно только через ближайшую исходную вершину.
        /// Сетка, а не перебор: частей десятки, вершин в каждой десятки, а
        /// исходных вершин тысячи — перебор был бы квадратичным.
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

                _cell = Mathf.Max(bounds.size.magnitude / Mathf.Max(8, resolution), 1e-4f);

                for (int i = 0; i < vertices.Length; i++)
                {
                    long key = Key(vertices[i]);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<int>(8);
                        _cells[key] = list;
                    }
                    list.Add(i);
                }
            }

            /// <summary>Ближайшая вершина. -1, если сетка пуста.</summary>
            public int Nearest(Vector3 point)
            {
                if (_vertices.Length == 0) return -1;

                int best = -1;
                float bestDistance = float.MaxValue;

                // Обход 3x3x3 ячеек: точка может лежать у границы ячейки, и
                // ближайшая вершина окажется в соседней.
                int cx = Mathf.FloorToInt(point.x / _cell);
                int cy = Mathf.FloorToInt(point.y / _cell);
                int cz = Mathf.FloorToInt(point.z / _cell);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        int index = list[i];
                        float distance = (point - _vertices[index]).sqrMagnitude;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = index;
                    }
                }

                // Пустая окрестность — вершина далеко от любой ячейки. Это
                // нормально для части, вышедшей за пределы тела, и такой случай
                // просто не даёт голоса.
                return best;
            }

            long Key(Vector3 p) { return Pack(Mathf.FloorToInt(p.x / _cell), Mathf.FloorToInt(p.y / _cell), Mathf.FloorToInt(p.z / _cell)); }

            static long Pack(int x, int y, int z)
            {
                return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
            }
        }

        // ------------------------------------------------------------------ Автоматическое число оболочек

        /// <summary>
        /// Сколько оболочек дать зоне в режиме Mesh, если точность Auto.
        ///
        /// ВАЖНО: к Skin это НЕ применяется. Там всегда одна оболочка на кость
        /// (см. комментарий в запекании) — дробить кость по пространству значит
        /// получить и щели, и перекрытия одновременно.
        ///
        /// Пороги подобраны по смыслу, а не по красоте:
        ///   · до 120 треугольников — фаланга, палец, мелкая кость. Одна
        ///     оболочка. Две разделили бы её пополам, и половина поверхности
        ///     дала бы не половину пальца, а лепесток;
        ///   · до 400 — кисть, стопа, предплечье. Двух хватает, чтобы оболочка
        ///     не «спрямляла» изгиб;
        ///   · до 900 — бедро, плечо, голень. Три-четыре: у них заметная
        ///     кривизна, и одна оболочка расширяет их за пределы модели;
        ///   · больше — торс, голова с волосами, крупные куски. Восемь.
        ///
        /// Числа намеренно не «степени двойки» и не «как у других»: они
        /// подобраны под то, что выпуклая оболочка делает с поверхностью, и их
        /// можно переопределить режимом Custom, если конкретная модель спорит.
        /// </summary>
        public static int AutoHullsFor(int triangles)
        {
            if (triangles <= 120) return 1;
            if (triangles <= 400) return 2;
            if (triangles <= 900) return 4;
            return 8;
        }

        // ------------------------------------------------------------------ Выпуклая оболочка

        static DycBakedHull BuildHull(DycCluster cluster, Vector3[] verts, int[] tris,
            float seamOverlap, Transform bone, Matrix4x4 bindWorld, Transform root, out bool degenerate)
        {
            degenerate = false;

            // Собираем вершины блока (с дедупликацией)
            var map = new Dictionary<int, int>(cluster.UniqueVertexCount);
            var pts = new List<Vector3>(cluster.UniqueVertexCount);
            for (int i = 0; i < cluster.tris.Count; i++)
            {
                int t = cluster.tris[i];
                AddVert(map, pts, verts, tris[t * 3]);
                AddVert(map, pts, verts, tris[t * 3 + 1]);
                AddVert(map, pts, verts, tris[t * 3 + 2]);
            }

            if (pts.Count < 4)
            {
                degenerate = true;
                return null;
            }

            if (!Dyc_Hull.Build(pts, out var hull, 1e-5f) || hull.points.Length < 4)
            {
                degenerate = true;
                return null;
            }

            // Расширение границ: прежде всего чтобы между частями не проскакивали попадания
            if (seamOverlap > 0f)
            {
                for (int i = 0; i < hull.points.Length; i++)
                {
                    Vector3 d = hull.points[i] - hull.center;
                    if (d.sqrMagnitude > 1e-12f)
                        hull.points[i] += d.normalized * seamOverlap;
                }
                // После расширения пересчитываем объём и плоскости
                Dyc_Hull.Build(hull.points, out var re, 1e-5f);
                if (re.ok) hull = re;
            }

            var m = new Mesh { name = "DYC_Hull" };
            m.SetVertices(hull.points);
            m.SetTriangles(hull.triangles, 0);
            m.RecalculateBounds();
            m.RecalculateNormals();

            var entry = new DycBakedHull
            {
                mesh = m,
                bonePath = bone != null ? PathOf(root, bone) : string.Empty,
                bindWorld = bindWorld,
                localCenter = hull.center,
                localSize = m.bounds.size,
                volume = hull.volume,
                vertexCount = hull.points.Length,
                triangleCount = hull.triangles.Length / 3,
                edges = hull.edges,
                edgeNormals = hull.edgeNormals
            };

            return entry;
        }

        /// <summary>
        /// Индексы вершин ИСХОДНОГО меша для кластера.
        ///
        /// Нужны «живому» обновлению в рантайме: без них оно не знает, какие
        /// вершины сэмплировать в текущей позе, и оболочка может только
        /// жёстко ехать за костью, но не следовать деформации. Индексы уже
        /// лежат в локальном массиве треугольников (boneTris собирается из
        /// исходного tris), поэтому дополнительных таблиц не требуется.
        /// </summary>
        static int[] SourceVertsOf(int[] localTris, DycCluster cluster)
        {
            if (localTris == null || cluster == null || cluster.tris == null) return null;

            var seen = new HashSet<int>();
            var list = new List<int>(Mathf.Max(8, cluster.UniqueVertexCount * 2));

            for (int i = 0; i < cluster.tris.Count; i++)
            {
                int baseIndex = cluster.tris[i] * 3;
                for (int k = 0; k < 3; k++)
                {
                    int idx = baseIndex + k;
                    if (idx < 0 || idx >= localTris.Length) continue;

                    int v = localTris[idx];
                    if (v < 0) continue;
                    if (seen.Add(v)) list.Add(v);
                }
            }

            return list.Count > 0 ? list.ToArray() : null;
        }

        // ------------------------------------------------------------------ Материалы и кости

        /// <summary>
        /// Таблица «номер треугольника → номер подмеша» и разрешённые
        /// физические материалы источника, дописанные в set.sourceMaterials.
        ///
        /// Подмеши идут в mesh.triangles подряд, поэтому смещение считается
        /// накопительно, а не поиском индексов.
        /// </summary>
        static int[] BuildSubmeshTable(Dyc_DynamicCollision target, Source src, Mesh mesh,
                                       int triCount, Dyc_BakedSet set)
        {
            var table = new int[triCount];
            if (mesh == null) return table;

            int subCount = Mathf.Max(1, mesh.subMeshCount);
            var source = SourceMaterialsOf(src);
            int offset = 0;

            for (int sub = 0; sub < subCount; sub++)
            {
                var mat = source != null && sub < source.Length ? source[sub] : null;
                set.sourceMaterials.Add(ResolvePhysicsMaterial(target, mat));

                int subTriangles = (int)(mesh.GetIndexCount(sub) / 3);
                for (int t = 0; t < subTriangles; t++)
                {
                    int tri = offset + t;
                    if (tri >= 0 && tri < triCount) table[tri] = sub;
                }
                offset += subTriangles;
            }

            return table;
        }

        /// <summary>
        /// Маска исключённых вершин по карте исключений.
        ///
        /// null означает «карты нет» и не является ошибкой: кисть остаётся
        /// основным способом разметки, карта — дополнением для тех, кто готовит
        /// маску процедурно или во внешнем редакторе. Текстура обязана быть
        /// читаемой (Read/Write Enabled), иначе выборку пикселя не сделать.
        /// </summary>
        static bool[] BuildExclusionMask(Dyc_DynamicCollision target, Mesh mesh, int vertexCount)
        {
            var adv = target != null ? target.Advanced : null;
            if (adv == null || adv.exclusionMap == null) return null;

            var tex = adv.exclusionMap;
            if (!tex.isReadable)
            {
                Debug.LogWarning(
                    "[NDC] Карта исключений пропущена: у текстуры выключен Read/Write Enabled.", target);
                return null;
            }

            var uv = mesh != null ? mesh.uv : null;
            if (uv == null || uv.Length != vertexCount) return null;

            int channel = Mathf.Clamp(adv.exclusionMapChannel, 0, 3);
            float threshold = adv.exclusionMapThreshold;

            var mask = new bool[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                mask[i] = ChannelOf(tex.GetPixelBilinear(uv[i].x, uv[i].y), channel) >= threshold;

            return mask;
        }

        static float ChannelOf(Color c, int channel)
        {
            switch (channel)
            {
                case 0: return c.r;
                case 1: return c.g;
                case 2: return c.b;
                default: return c.a;
            }
        }

        static bool Excluded(bool[] mask, int index)
        {
            return index >= 0 && index < mask.Length && mask[index];
        }

        static Material[] SourceMaterialsOf(Source src)
        {
            if (src == null) return null;
            if (src.skin != null) return src.skin.sharedMaterials;

            if (src.meshFilter != null)
            {
                var r = src.meshFilter.GetComponent<Renderer>();
                if (r != null) return r.sharedMaterials;
            }
            return null;
        }

        /// <summary>Исходный материал → физический по карте материалов компонента.</summary>
        static PhysicMaterial ResolvePhysicsMaterial(Dyc_DynamicCollision target, Material material)
        {
            if (target == null || material == null) return null;

            var adv = target.Advanced;
            if (adv == null || adv.materialAssociations == null) return null;

            for (int i = 0; i < adv.materialAssociations.Count; i++)
            {
                var link = adv.materialAssociations[i];
                if (link != null && link.material == material) return link.physicsMaterial;
            }
            return null;
        }

        /// <summary>Номер материала, которому принадлежит большинство
        /// треугольников кластера. Кластер на стыке двух материалов уходит
        /// тому, чья доля больше, — как и треугольник уходит сильнейшей кости.</summary>
        static int DominantMaterial(int[] submeshOfTri, int baseIndex, DycCluster cluster)
        {
            if (submeshOfTri == null || cluster == null || cluster.tris == null || cluster.tris.Count == 0)
                return -1;

            var counts = new Dictionary<int, int>(4);
            for (int i = 0; i < cluster.tris.Count; i++)
            {
                int t = cluster.tris[i];
                if (t < 0 || t >= submeshOfTri.Length) continue;

                int sub = submeshOfTri[t];
                int c;
                counts.TryGetValue(sub, out c);
                counts[sub] = c + 1;
            }

            int best = -1, bestCount = -1;
            foreach (var kv in counts)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            }
            return best < 0 ? -1 : baseIndex + best;
        }

        /// <summary>
        /// Переопределения с костей: материал и исключение.
        ///
        /// Материал дописывается в конец sourceMaterials, и оболочка
        /// переводится на него: так переопределение не задевает соседей по
        /// материалу исходного меша.
        /// </summary>
        static void ApplyBoneOverrides(Dyc_DynamicCollision target, Dyc_BakedSet set)
        {
            if (target == null || set == null || set.hulls == null) return;

            for (int i = set.hulls.Count - 1; i >= 0; i--)
            {
                var hull = set.hulls[i];
                if (hull == null) continue;

                var bone = ResolveBone(target.transform, hull);
                if (bone == null) continue;

                var props = bone.GetComponent<Dyc_BoneProperties>();
                if (props == null) continue;

                if (props.exclude)
                {
                    set.hulls.RemoveAt(i);
                    continue;
                }

                if (props.overrideMaterial && props.physicsMaterial != null)
                {
                    set.sourceMaterials.Add(props.physicsMaterial);
                    hull.materialIndex = set.sourceMaterials.Count - 1;
                }
            }
        }

        /// <summary>
        /// Крепление к чужому скелету: путь кости переписывается на одноимённую
        /// кость в корне переназначения.
        ///
        /// Оболочки НЕ пересчитываются: предполагается скелет-двойник с той же
        /// позой привязки (Puppet Master и подобные). Если одноимённой кости
        /// нет — путь остаётся своим, и оболочка просто едет за исходным
        /// скелетом: это видно в предпросмотре и не ломает запекание.
        /// </summary>
        static void ApplyRetarget(Dyc_DynamicCollision target, Dyc_BakedSet set, ref DycBakeReport report)
        {
            var root = target != null ? target.Advanced.retargetRoot : null;
            if (root == null || set == null || set.hulls == null) return;

            // Пути НЕ переписываются: они относительные, и рантайм ищет их в
            // корне переназначения сам. Здесь только проверка — одноимённая
            // кость может отсутствовать, и без предупреждения это выглядело бы
            // как «оболочки прилипли к земле».
            int missing = 0;
            for (int i = 0; i < set.hulls.Count; i++)
            {
                var hull = set.hulls[i];
                if (hull == null) continue;

                string path = !string.IsNullOrEmpty(hull.clusterBonePath)
                    ? hull.clusterBonePath
                    : hull.bonePath;

                if (string.IsNullOrEmpty(path)) continue;
                if (root.Find(path) == null) missing++;
            }

            if (missing > 0)
            {
                report.warnings.Add(
                    "Переназначение скелета: у " + missing + " оболочк(и) нет одноимённой кости в '" +
                    root.name + "'. Они останутся на своём скелете.");
            }
        }

        /// <summary>Кость оболочки: по clusterBonePath, иначе по bonePath.</summary>
        static Transform ResolveBone(Transform root, DycBakedHull hull)
        {
            if (root == null || hull == null) return null;

            string path = !string.IsNullOrEmpty(hull.clusterBonePath) ? hull.clusterBonePath : hull.bonePath;
            if (string.IsNullOrEmpty(path)) return null;

            return root.Find(path);
        }

        /// <summary>
        /// Мировая матрица кости в позе привязки = bindposes[boneIndex].inverse.
        ///
        /// Ключевой факт: в позе привязки bone.localToWorldMatrix · bindposes[i] = I,
        /// поэтому "поза привязки = исходный меш как есть": gizmo не требует скиннинга и не зависит от текущей анимации.
        /// </summary>
        static Matrix4x4 BindWorldOf(Source src, Transform bone, Dyc_DynamicCollision target)
        {
            if (bone == null || src.skin == null || src.mesh == null) return target.transform.localToWorldMatrix;

            var bones = src.skin.bones;
            var bindposes = src.mesh.bindposes;
            if (bones == null || bindposes == null) return bone.localToWorldMatrix;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != bone) continue;
                if (i >= bindposes.Length) break;
                return bindposes[i].inverse;
            }
            return bone.localToWorldMatrix;
        }

        static void AddVert(Dictionary<int, int> map, List<Vector3> pts, Vector3[] verts, int index)
        {
            if (map.ContainsKey(index)) return;
            map[index] = pts.Count;
            pts.Add(verts[index]);
        }

        /// <summary>Группа помечена «исключить» — её треугольники в запекание не идут.</summary>
        static bool IsGroupExcluded(List<DycMaterialGroup> groups, int index)
        {
            if (groups == null || index < 0 || index >= groups.Count) return false;
            var g = groups[index];
            return g != null && g.exclude;
        }

        // ------------------------------------------------------------------ Пути

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
            if (cur != root) return null; // не под тем же корнем

            parts.Reverse();
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------ Автозаполнение частей

        /// <summary>Полуавтомат: при отсутствии частей создаёт часть "корень + с дочерними костями", чтобы первое запекание уже что-то дало.</summary>
        public static bool EnsureElements(Dyc_DynamicCollision target)
        {
            var elements = target.Elements;
            if (elements != null && elements.Count > 0) return false;

            Transform rootBone = null;
            if (target.Mode == DycMode.Skin)
            {
                var skin = target.SourceSkin;
                if (skin != null) rootBone = skin.rootBone != null ? skin.rootBone : skin.transform;
            }

            elements.Add(new DycElement
            {
                name = "All",
                bone = rootBone != null ? rootBone : target.transform,
                includeChildren = true,
                eventName = "Hit",
                damageMultiplier = 1f
            });
            return true;
        }

        public static bool EnsureGroups(Dyc_DynamicCollision target)
        {
            var groups = target.Groups;
            if (groups != null && groups.Count > 0) return false;
            groups.Add(new DycMaterialGroup { name = "Default", density = 1000f });
            return true;
        }
    }
}
