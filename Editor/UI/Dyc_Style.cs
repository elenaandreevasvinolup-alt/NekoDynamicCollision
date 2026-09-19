using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Стилевые константы и виджеты UI Toolkit.
    ///
    /// Окно NSG собирается из VisualElement прямо в коде (в проекте нет .uss/.uxml),
    /// здесь применён тот же подход, но цвета и отступы вынесены в одно место для централизованной правки.
    /// </summary>
    public static class Dyc_Style
    {
        // ---- Цвета
        public static readonly Color Bg = new Color(0.153f, 0.157f, 0.169f);
        public static readonly Color CardBg = new Color(0.196f, 0.204f, 0.220f);
        public static readonly Color CardHi = new Color(0.235f, 0.243f, 0.263f);
        public static readonly Color Line = new Color(0.294f, 0.306f, 0.333f);
        public static readonly Color Text = new Color(0.878f, 0.886f, 0.902f);
        public static readonly Color Muted = new Color(0.545f, 0.565f, 0.600f);
        public static readonly Color Accent = new Color(0.357f, 0.780f, 0.980f);
        public static readonly Color Ok = new Color(0.451f, 0.851f, 0.510f);
        public static readonly Color Warn = new Color(0.949f, 0.718f, 0.302f);
        public static readonly Color Error = new Color(0.925f, 0.353f, 0.353f);

        public const int Pad = 8;
        public const int PadL = 12;
        public const int Radius = 6;

        public static VisualElement Root()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;
            root.style.backgroundColor = Bg;
            root.style.paddingTop = PadL;
            root.style.paddingBottom = PadL;
            Dyc_Rtl.PaddingStart(root.style, PadL);
            Dyc_Rtl.PaddingEnd(root.style, PadL);
            return root;
        }

        public static VisualElement Row(bool wrap = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = Dyc_Rtl.Row;
            row.style.alignItems = Align.Center;
            if (wrap) row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        public static VisualElement Card()
        {
            var c = new VisualElement();
            c.style.backgroundColor = CardBg;
            c.style.borderTopLeftRadius = Radius;
            c.style.borderTopRightRadius = Radius;
            c.style.borderBottomLeftRadius = Radius;
            c.style.borderBottomRightRadius = Radius;
            c.style.paddingTop = Pad;
            c.style.paddingBottom = Pad;
            Dyc_Rtl.PaddingStart(c.style, PadL);
            Dyc_Rtl.PaddingEnd(c.style, PadL);
            c.style.marginBottom = Pad;
            return c;
        }

        public static Label Header(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 13;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = Text;
            l.style.marginBottom = 4;
            l.style.marginTop = 2;
            return l;
        }

        public static Label Caption(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.color = Muted;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 4;
            return l;
        }

        public static Label Body(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 11;
            l.style.color = Text;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Label Pill(string text, Color color)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.color = color;
            l.style.paddingLeft = 6;
            l.style.paddingRight = 6;
            l.style.paddingTop = 1;
            l.style.paddingBottom = 1;
            Dyc_Rtl.MarginEnd(l.style, 6);
            l.style.borderTopLeftRadius = 8;
            l.style.borderTopRightRadius = 8;
            l.style.borderBottomLeftRadius = 8;
            l.style.borderBottomRightRadius = 8;
            l.style.backgroundColor = new Color(color.r, color.g, color.b, 0.16f);
            return l;
        }

        public static Button Btn(string text, Action onClick, bool primary = false)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = 22;
            b.style.fontSize = 11;
            Dyc_Rtl.MarginEnd(b.style, 6);
            b.style.marginBottom = 4;
            b.style.paddingLeft = 10;
            b.style.paddingRight = 10;
            b.style.backgroundColor = primary ? new Color(Accent.r, Accent.g, Accent.b, 0.22f) : CardHi;
            b.style.color = primary ? Accent : Text;
            b.style.borderTopWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0;
            b.style.borderRightWidth = 0;
            b.style.borderTopLeftRadius = 4;
            b.style.borderTopRightRadius = 4;
            b.style.borderBottomLeftRadius = 4;
            b.style.borderBottomRightRadius = 4;
            return b;
        }

        public static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.style.fontSize = 11;
            t.style.color = Text;
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        public static IntegerField IntField(string label, int value, Action<int> onChange)
        {
            var f = new IntegerField(label) { value = value };
            f.style.fontSize = 11;
            f.style.marginBottom = 2;
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static FloatField FloatField(string label, float value, Action<float> onChange)
        {
            var f = new FloatField(label) { value = value };
            f.style.fontSize = 11;
            f.style.marginBottom = 2;
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static Slider Slider(string label, float value, float min, float max, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value };
            s.style.fontSize = 11;
            s.style.marginBottom = 2;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        public static VisualElement Divider()
        {
            var d = new VisualElement();
            d.style.height = 1;
            d.style.backgroundColor = Line;
            d.style.marginTop = Pad;
            d.style.marginBottom = Pad;
            return d;
        }

        public static VisualElement Space(int px)
        {
            var s = new VisualElement();
            s.style.height = px;
            return s;
        }

        /// <summary>Строка «ключ — значение»: слева серый ярлык, справа значение.</summary>
        public static VisualElement KV(string key, string value, Color? valueColor = null)
        {
            var row = Row();
            row.style.marginBottom = 2;

            var k = new Label(key);
            k.style.fontSize = 11;
            k.style.color = Muted;
            k.style.minWidth = 110;

            var v = new Label(value);
            v.style.fontSize = 11;
            v.style.color = valueColor ?? Text;

            Dyc_Rtl.AddKV(row, k, v);
            return row;
        }

        public static VisualElement Scroll()
        {
            var s = new ScrollView();
            s.style.flexGrow = 1;
            return s;
        }

        /// <summary>
        /// Предупреждающая плашка: цветная рамка, текст и необязательная кнопка.
        ///
        /// Нужна там, где выбор человека ЗАКОНЕН, но имеет последствия, о которых
        /// он обязан узнать до запекания, а не после: невыпуклая сетка на
        /// подвижном теле — главный такой случай. Плашка, а не модальное окно:
        /// модальное окно пришлось бы закрывать на каждом переключении, и его
        /// перестали бы читать.
        /// </summary>
        public static VisualElement Notice(string text, Color color, string fixLabel = null, Action onFix = null)
        {
            var box = new VisualElement();
            box.style.backgroundColor = new Color(color.r, color.g, color.b, 0.10f);
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = color;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.marginTop = 4;
            box.style.marginBottom = 4;
            Dyc_Rtl.PaddingStart(box.style, 8);
            Dyc_Rtl.PaddingEnd(box.style, 8);

            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.color = Text;
            label.style.whiteSpace = WhiteSpace.Normal;
            box.Add(label);

            if (onFix != null && !string.IsNullOrEmpty(fixLabel))
            {
                var btn = Btn(fixLabel, onFix);
                btn.style.marginTop = 5;
                btn.style.alignSelf = Align.FlexStart;
                box.Add(btn);
            }

            return box;
        }
    }
}
