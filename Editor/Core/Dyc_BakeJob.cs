using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Запекание, разложенное во времени.
    ///
    /// ЗАЧЕМ. Всё запекание — один длинный синхронный вызов, и пока он идёт,
    /// редактор не перерисовывается. Значит, никакой прогресс-бар, кроме
    /// системного модального, в принципе не может быть живым: окно просто не
    /// получает кадров. Проблема не в оформлении, а в том, что главный поток
    /// занят.
    ///
    /// РЕШЕНИЕ. Самое долгое — разделение костей и воксельное разложение — это
    /// чистая математика над массивами, ей не нужен ни Unity, ни главный поток.
    /// Она уходит в фон, а главный поток каждую итерацию EditorApplication.update
    /// только обновляет окно. Сборка оболочек из готовых частей и запись ассетов
    /// остаются на главном потоке, но занимают миллисекунды.
    ///
    /// ЧТО ЭТО ДАЁТ: редактор не замирает, полоса честно растёт, кнопка отмены
    /// работает, а бюджет времени из настроек продолжает действовать.
    ///
    /// ЕСЛИ ФОНОВЫЙ ПУТЬ НЕВОЗМОЖЕН (нативное ядро V-HACD — оно внутри себя
    /// синхронное и потоконебезопасное), задание не запускается, и запекание
    /// идёт старым синхронным путём с системным баром.
    /// </summary>
    class Dyc_BakeJob
    {
        static Dyc_BakeJob _current;
        static readonly Queue<Dyc_DynamicCollision> _queue = new Queue<Dyc_DynamicCollision>();

        Dyc_DynamicCollision _target;
        Dyc_BakedSet _set;
        string _folder;
        string _baseName;

        readonly List<System.Threading.Tasks.Task> _tasks = new List<System.Threading.Tasks.Task>();
        int _totalTasks;
        int _doneTasks;
        bool _ticking;

        public static bool Start(Dyc_DynamicCollision target)
        {
            if (target == null) return false;

            // Очередь, а не отказ: «запечь всё выделенное» запускает задание на
            // каждый объект, и молча терять остальные нельзя.
            if (_current != null)
            {
                if (!_queue.Contains(target)) _queue.Enqueue(target);
                return true;
            }

            var job = new Dyc_BakeJob { _target = target };
            if (!job.Begin())
            {
                job.Detach();
                return false;
            }

            _current = job;
            return true;
        }

        /// <summary>Идёт ли запекание прямо сейчас (нужно окну, чтобы показать статус).</summary>
        public static bool Running => _current != null;

        static void FinishCurrent()
        {
            _current = null;
            if (_queue.Count == 0) return;

            var next = _queue.Dequeue();
            Start(next);
        }

        // ------------------------------------------------------------------ запуск

        bool Begin()
        {
            Undo.RecordObject(_target, "Dynamic Collision Bake");

            Dyc_Baker.EnsureGroups(_target);
            Dyc_Baker.EnsureElements(_target);

            _folder = Dyc_AssetIO.RootFor(_target);
            _baseName = _target.gameObject.name;

            var sources = Dyc_Baker.CollectSources(_target);
            int triCount = 0;
            var hashes = new List<string>();
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].mesh == null) continue;
                triCount += sources[i].mesh.triangles.Length / 3;
                hashes.Add(Dyc_PaintMask.Hash(sources[i].mesh));
            }

            var mask = Dyc_AssetIO.SavePaintMask(_target, _target.PaintMask, _folder, _baseName,
                triCount, string.Join("|", hashes));
            if (_target.PaintMask == null) _target.EditorSetPaintMask(mask);

            _set = Dyc_AssetIO.EnsureBakedAsset(_target, _folder, _baseName);

            // Старые предвычисления не должны пережить новый запуск: геометрия
            // или настройки могли измениться, а части от прошлого раза — нет.
            Dyc_Baker.ClearPrecomputed();

            StartTasks(sources);

            Dyc_BakeProgressWindow.Open();
            _ticking = true;
            EditorApplication.update += Tick;
            return true;
        }

        void StartTasks(List<Dyc_Baker.Source> sources)
        {
            var settings = _target.DecomposeSettings;

            // Невыпуклому режиму разложение не нужно вовсе: он берёт исходную
            // поверхность, а не строит выпуклые части. Запускать фон значило бы
            // считать самое дорогое и тут же выбросить результат.
            if (_target.ColliderShape == DycColliderShape.Concave) return;

            if (settings.kernel == DycDecomposeKernel.Native) return;

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var mesh = src.mesh;
                if (src.skin == null || mesh == null) continue;

                var bones = src.skin.bones;
                var weights = mesh.boneWeights;
                if (bones == null || bones.Length == 0) continue;
                if (weights == null || weights.Length != mesh.vertices.Length) continue;

                Vector3[] decompositionVerts = mesh.vertices;
                int movedVertices = 0;

                if (settings.separateByBones && settings.separationMm > 0f)
                {
                    float gapMm = Mathf.Max(settings.separationMm, settings.voxelSizeMm * 1.5f);
                    var separated = Dyc_BoneSeparator.Separate(mesh, weights, bones,
                        gapMm * 0.001f, gapMm * 0.004f, out movedVertices);
                    if (separated != null)
                    {
                        decompositionVerts = separated.vertices;
                        Object.DestroyImmediate(separated);
                    }
                }

                var boneValid = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    boneValid[i] = bones[i] != null && !_target.IsBoneExcluded(bones[i]);

                int meshId = mesh.GetInstanceID();
                var localVerts = decompositionVerts;
                var localTris = mesh.triangles;
                var localWeights = weights;
                var localValid = boneValid;
                var localSettings = settings;
                int localMoved = movedVertices;

                _totalTasks++;
                _tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        if (Dyc_VoxelDecomposer.Decompose(localVerts, localTris, localWeights, localValid,
                                localSettings, out var pieces, out _, null))
                        {
                            Dyc_Baker.SetPrecomputed(meshId, pieces, localMoved, localSettings);
                        }
                    }
                    catch (System.Exception e)
                    {
                        // Ошибка фона не должна ронять запекание: без кэша
                        // разложение честно повторится на главном потоке.
                        Debug.LogWarning("[NDC] Фоновое разложение не удалось: " + e.Message);
                    }
                    finally
                    {
                        System.Threading.Interlocked.Increment(ref _doneTasks);
                    }
                }));
            }
        }

        // ------------------------------------------------------------------ ожидание

        void Tick()
        {
            if (!_ticking) return;

            int done = System.Threading.Volatile.Read(ref _doneTasks);
            float t = _totalTasks == 0
                ? 0.5f
                : 0.05f + 0.75f * (done / (float)_totalTasks);

            Dyc_BakeProgressWindow.Update(
                _totalTasks == 0 ? Dyc_L10n.T("bake.progress.baking") : Dyc_L10n.T("bake.progress.decomposing"),
                t);

            if (Dyc_BakeProgressWindow.CancelRequested)
            {
                Detach();
                Dyc_Baker.ClearPrecomputed();
                Dyc_BakeProgressWindow.CloseIfOpen();
                Debug.LogWarning("[NDC] Запекание отменено. Прежний набор не изменён.", _target);
                FinishCurrent();
                return;
            }

            for (int i = 0; i < _tasks.Count; i++)
                if (!_tasks[i].IsCompleted) return;

            Detach();

            Dyc_BakeProgressWindow.Update(Dyc_L10n.T("bake.progress.assembling"), 0.85f);
            Complete();
            Dyc_BakeProgressWindow.CloseIfOpen();
            FinishCurrent();
        }

        void Detach()
        {
            if (!_ticking) return;
            _ticking = false;
            EditorApplication.update -= Tick;
        }

        // ------------------------------------------------------------------ завершение

        void Complete()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double budgetMs = _target.DecomposeSettings.EffectiveTimeBudgetSeconds * 1000.0;
            var progress = new Dyc_BakeProgressProxy(budgetMs, watch);

            var report = Dyc_Baker.Bake(_target, _set, true, _folder, progress);

            if (progress.cancelled && !progress.timedOut)
            {
                Debug.LogWarning("[NDC] Запекание отменено. Прежний набор не изменён.", _target);
                return;
            }

            if (progress.timedOut && report.warnings != null)
                report.warnings.Add(
                    "Разложение остановлено по бюджету времени: набор собран из готовых частей. " +
                    "Уменьшите детализацию (крупнее воксель) или поднимите бюджет времени.");

            if (!report.ok)
            {
                Debug.LogError($"[NDC] Запекание не удалось: {report.error}", _target);
                return;
            }

            _set.boneStats = report.boneStats ?? new List<DycBoneStat>();
            _set = Dyc_AssetIO.SaveBaked(_target, _set, _folder, _baseName);
            _target.EditorApplyBaked(_set, Application.isPlaying);

            EditorUtility.SetDirty(_target);
            if (_target.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_target.gameObject.scene);

            // Отчёт печатает не только итог, но и РАСПРЕДЕЛЕНИЕ работы: сколько
            // треугольников разошлось по зонам и сколько не попало ни в одну.
            // Без этого «оболочек почти нет» невозможно объяснить: итог говорит
            // «2 оболочки», а причина может быть в пустом списке зон, в слишком
            // высоком пороге веса или в том, что меш не тот.
            string msg = $"[NDC] Запекание готово: {report.hulls} выпуклых тел / {report.hullVertices} вершин / " +
                         $"пик {report.maxHullVertices} / объём {report.totalVolume:F4} м³ / {report.elapsedMs:F0} мс\n" +
                         $"  зон: {_target.ElementCount} · треугольников источника: {report.sourceTriangles} · " +
                         $"распределено: {report.usedTriangles} · БЕЗ ЗОНЫ: {report.unassignedTriangles} · " +
                         $"вырожденных кластеров: {report.degenerateClusters}" +
                         $" · раздвинуто вершин: {report.separatedVertices}";
            if (report.warnings != null && report.warnings.Count > 0)
                for (int i = 0; i < report.warnings.Count; i++) msg += "\n  · " + report.warnings[i];

            Debug.Log(msg, _target);
        }
    }
}
