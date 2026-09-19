using UnityEngine;
using UnityEngine.UIElements;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Зеркалирование интерфейса для языков, которые пишутся справа налево
    /// (арабский, иврит).
    ///
    /// Намеренно НЕ используется <c>style.direction = Direction.Rtl</c>:
    /// поддержка direction в UI Toolkit неполная и меняется от версии к
    /// версии. Поэтому логические «начало» и «конец» расставлены явно, и
    /// результат предсказуем на любой версии Unity.
    ///
    /// Что остаётся английским и не зеркалится: пункты меню Unity
    /// (NekoWorks/NekoDynamicCollision/...) — см. Dyc_MenuRuntime.
    /// </summary>
    public static class Dyc_Rtl
    {
        /// <summary>
        /// Включено ли зеркалирование. Условие составное намеренно: язык должен
        /// писаться справа налево И пользователь должен не выключить зеркало
        /// (см. Dyc_L10n.MirrorRtl).
        ///
        /// Область действия ограничена по построению: все методы ниже пишут
        /// стили ТОЛЬКО тех элементов, которые создал плагин, и вызываются
        /// только из Dyc_Style. Ни одного глобального стиля, ни
        /// style.direction на общем корне, ни правки EditorStyles — поэтому
        /// перевернуть чужой интерфейс Unity физически нечем.
        /// </summary>
        public static bool On { get { return Dyc_L10n.IsRtl && Dyc_L10n.MirrorRtl; } }


        // ------------------------------------------------------------------
        // Ось строки
        // ------------------------------------------------------------------

        /// <summary>Направление строки: Row в LTR, RowReverse в RTL.</summary>
        public static FlexDirection Row => On ? FlexDirection.RowReverse : FlexDirection.Row;

        /// <summary>Выравнивание текста по началу строки.</summary>
        public static TextAnchor TextAlign => On ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

        /// <summary>Сторона, к которой прижимается «хвост» строки.</summary>
        public static Justify JustifyEnd => On ? Justify.FlexStart : Justify.FlexEnd;

        // ------------------------------------------------------------------
        // Отступы
        // ------------------------------------------------------------------

        public static void PaddingStart(IStyle s, float v)
        {
            if (On) s.paddingRight = v; else s.paddingLeft = v;
        }

        public static void PaddingEnd(IStyle s, float v)
        {
            if (On) s.paddingLeft = v; else s.paddingRight = v;
        }

        public static void MarginStart(IStyle s, float v)
        {
            if (On) s.marginRight = v; else s.marginLeft = v;
        }

        public static void MarginEnd(IStyle s, float v)
        {
            if (On) s.marginLeft = v; else s.marginRight = v;
        }

        /// <summary>Порядок «метка → значение» меняется на обратный в RTL.</summary>
        public static void AddKV(VisualElement row, VisualElement key, VisualElement value)
        {
            if (On)
            {
                value.style.marginLeft = 0;
                value.style.marginRight = 0;
                value.style.unityTextAlign = TextAnchor.MiddleRight;
                key.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(value);
                row.Add(key);
            }
            else
            {
                key.style.unityTextAlign = TextAnchor.MiddleLeft;
                value.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(key);
                row.Add(value);
            }
        }
    }
}
