using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Своё окно прогресса запекания: заполняющаяся полоса вместо системного
    /// модального диалога Unity.
    ///
    /// ПОЧЕМУ СВОЁ ОКНО, А НЕ EditorUtility.DisplayCancelableProgressBar.
    /// Системный бар нельзя ни оформить, ни дополнить: он рисует одну строку
    /// текста и полоску системным шрифтом и всегда лезет поверх всего. Своё
    /// окно даёт нормальную типографику, прошедшее время, процент и кнопку
    /// отмены там, где её видно.
    ///
    /// КЛЮЧЕВОЕ УСЛОВИЕ ЖИВОСТИ: окно перерисовывается только тогда, когда
    /// главный поток возвращается к редактору. Поэтому само запекание
    /// разложено во времени (см. Dyc_BakeJob): долгое разложение считается в
    /// фоне, а главный поток каждую итерацию обновляет это окно. Если бы
    /// запекание осталось одним синхронным вызовом, полоса просто замерла бы —
    /// и никакое оформление этого не исправило бы.
    /// </summary>
    class Dyc_BakeProgressWindow : EditorWindow
    {
        static Dyc_BakeProgressWindow _instance;

        /// <summary>Что делаем прямо сейчас, человеческим языком.</summary>
        public string stage = "";
        /// <summary>Доля готовности 0..1.</summary>
        public float progress;
        /// <summary>Момент запуска, в секундах редактора.</summary>
        public double startedAt;
        /// <summary>Пользователь нажал отмену.</summary>
        public bool cancelRequested;
        /// <summary>Идёт сворачивание после отмены.</summary>
        public bool cancelling;

        static readonly Color Track = new Color(0f, 0f, 0f, 0.30f);
        static readonly Color Fill = new Color(0.35f, 0.85f, 1f, 0.95f);
        static readonly Color FillWarn = new Color(1f, 0.65f, 0.25f, 0.95f);

        public static Dyc_BakeProgressWindow Open()
        {
            if (_instance == null)
            {
                _instance = CreateInstance<Dyc_BakeProgressWindow>();
                _instance.titleContent = new GUIContent(Dyc_L10n.T("bake.progress.title"));
                _instance.minSize = new Vector2(380f, 118f);
                _instance.maxSize = new Vector2(380f, 118f);
                _instance.ShowUtility();

                // По центру главного окна, а не в углу экрана: запекание
                // запускают из окна плагина, и взгляд уже там.
                var main = EditorGUIUtility.GetMainWindowPosition();
                _instance.position = new Rect(
                    main.x + (main.width - 380f) * 0.5f,
                    main.y + (main.height - 118f) * 0.5f,
                    380f, 118f);
            }

            _instance.stage = Dyc_L10n.T("bake.progress.preparing");
            _instance.progress = 0f;
            _instance.startedAt = EditorApplication.timeSinceStartup;
            _instance.cancelRequested = false;
            _instance.cancelling = false;
            _instance.Repaint();
            return _instance;
        }

        public static bool IsOpen => _instance != null;

        public static bool CancelRequested => _instance != null && _instance.cancelRequested;

        public static void Update(string stage, float progress)
        {
            if (_instance == null) return;
            _instance.stage = stage;
            _instance.progress = Mathf.Clamp01(progress);
            _instance.Repaint();
        }

        public static void SetStage(string stage)
        {
            if (_instance == null) return;
            _instance.stage = stage;
            _instance.cancelling = true;
            _instance.Repaint();
        }

        public static void CloseIfOpen()
        {
            if (_instance == null) return;
            _instance.Close();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnGUI()
        {
            // Разметка в Layout, рисование в Repaint: если рисовать полосу в
            // Layout, прямоугольник ещё не вычислен и полоса получается нулевой.
            if (Event.current.type == EventType.Layout) return;

            EditorGUILayout.Space(10f);

            var stageRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            GUI.Label(stageRect, stage ?? "", EditorStyles.boldLabel);

            var bar = GUILayoutUtility.GetRect(0f, 16f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, Track);

            var inner = new Rect(bar.x + 1f, bar.y + 1f, Mathf.Max(0f, bar.width - 2f), bar.height - 2f);
            var fill = new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(progress), inner.height);
            EditorGUI.DrawRect(fill, cancelling ? FillWarn : Fill);

            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            EditorGUILayout.LabelField(
                string.Format("{0:0}%   ·   {1:0.0} s", progress * 100f, elapsed),
                EditorStyles.miniLabel);

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(cancelling))
            {
                if (GUILayout.Button(Dyc_L10n.T("bake.progress.cancel"), GUILayout.Height(20f)))
                {
                    cancelRequested = true;
                    SetStage(Dyc_L10n.T("bake.progress.cancelling"));
                }
            }
        }
    }
}
