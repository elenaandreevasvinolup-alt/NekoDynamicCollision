using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Версии. Плагин и ядро версионируются РАЗДЕЛЬНО, как в NSG
    /// (плагин / Esketamine): ядро Synapse можно обновлять, не трогая
    /// таблицы материалов, окно и запекатель, и наоборот.
    ///
    /// Правило простое и его надо соблюдать:
    ///   · Plugin меняется, когда меняется пользовательское поведение
    ///     (UI, пресеты, форматы ассетов);
    ///   · Synapse меняется, когда меняется ТОЛЬКО ядро — запекание,
    ///     кластеризация, карта контактов, обмен кадрами.
    /// </summary>
    public static class Dyc_Version
    {
        public const string Plugin = "1.3.0";

        /// <summary>Версия ядра Synapse, лежащего внутри плагина.</summary>
        public const string Core = Dyc_Synapse.Version;

        /// <summary>Строка для окна настроек: «plugin 1.1.0 · core Synapse 1.0.0».</summary>
        public static string Line
        {
            get { return "plugin " + Plugin + " · core " + Dyc_Synapse.Name + " " + Dyc_Synapse.Version; }
        }
    }

    /// <summary>
    /// Ядро Synapse.
    ///
    /// Имя выбрано не для красоты: синапс — это МЕСТО ОБМЕНА. Ровно то, чем
    /// занимается ядро: оно запекает карту контактов (какие пары вершин вообще
    /// могут встретиться) и обменивает кадры деформации между решателем
    /// мягкого тела и выпуклыми оболочками. Отсюда и разделение труда:
    ///
    ///   Synapse (здесь)     — повар: топология, кластеры, карта контактов,
    ///                         контракт кадров. Работает в редакторе.
    ///   Плагин (вокруг)     — лицо: окно, пресеты, кисти, здоровье, меню.
    ///   Пластичность (NDSC) — решатель: частицы, ограничения, время.
    ///
    /// Ядро НЕ знает ни одного типа решателя и не ссылается на него: контракт
    /// кадров объявлен в рантайме как интерфейс из чистых типов BCL
    /// (см. Dyc_IDeformSource). Поэтому Synapse работает и без NDSC — просто
    /// кадры никто не пишет, и мягкие зоны ведут себя как жёсткие кластеры.
    /// </summary>
    public static class Dyc_Synapse
    {
        public const string Name = "Synapse";
        public const string Version = "1.0.0";

        /// <summary>
        /// Фирменный цвет ядра. Он же — акцент всего NDC: голубой «разряд»
        /// между двумя терминалами. NDSC использует свой (Plasticity),
        /// поэтому два плагина не путаются в глазах.
        /// </summary>
        public static readonly Color Accent = new Color(0.357f, 0.780f, 0.980f);

        const int IconSize = 64;
        static Texture2D _icon;

        /// <summary>
        /// Процедурный глиф ядра: два терминала и щель с медиаторами между
        /// ними. Рисуется в память при первом обращении — PNG в проекте не
        /// появляется, ровно как у иконки компонента (см. Dyc_Icon).
        ///
        /// Почему именно эта фигура: на 16 пикселях в инспекторе гексагон
        /// (иконка компонента) читается как «выпуклое тело», а два круга с
        /// искрой между ними — как «обмен», и их невозможно перепутать.
        /// </summary>
        public static Texture2D Icon
        {
            get
            {
                if (_icon == null) _icon = Build();
                return _icon;
            }
        }

        static readonly Color Bg = new Color(0.157f, 0.169f, 0.192f, 1f);
        static readonly Color Ink = new Color(0.925f, 0.945f, 0.965f, 1f);

        static Texture2D Build()
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                name = "Dyc_SynapseIcon",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Vector2 center = new Vector2(IconSize * 0.5f, IconSize * 0.5f);
            Vector2 left = center + new Vector2(-13.5f, 0f);
            Vector2 right = center + new Vector2(13.5f, 0f);
            const float radius = 10.5f;

            // Медиаторы в щели: три точки, а не одна — одна читалась бы как
            // «плюс» или «двоеточие».
            var dots = new[]
            {
                center + new Vector2(0f, 6.5f),
                center + new Vector2(-5f, -2.5f),
                center + new Vector2(5f, -2.5f)
            };

            var px = new Color[IconSize * IconSize];

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                    float dRect = RoundRect(p, center, new Vector2(IconSize * 0.5f - 2.5f, IconSize * 0.5f - 2.5f), 13f);
                    float aBg = Aa(dRect, 0f, 1.1f);
                    float aBorder = Aa(Mathf.Abs(dRect + 1.6f), 0f, 1.0f);

                    float aTerminal = Mathf.Max(
                        Aa((p - left).magnitude - radius, 0f, 1.1f),
                        Aa((p - right).magnitude - radius, 0f, 1.1f));

                    float dDot = float.MaxValue;
                    for (int i = 0; i < dots.Length; i++)
                        dDot = Mathf.Min(dDot, (p - dots[i]).magnitude);
                    float aDot = Aa(dDot - 2.4f, 0f, 1.0f);

                    Color c = Bg;
                    c = Color.Lerp(c, Accent, aBorder);
                    c = Color.Lerp(c, Accent, aTerminal * 0.95f);
                    c = Color.Lerp(c, Ink, aDot);

                    c.a = Mathf.Max(aBg, Mathf.Max(aBorder, Mathf.Max(aTerminal, aDot)));
                    px[y * IconSize + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Сглаживание по расстоянию: 1 внутри, 0 снаружи.</summary>
        static float Aa(float dist, float halfWidth, float softness)
        {
            return Mathf.Clamp01((halfWidth - dist) / softness + 0.5f);
        }

        static float RoundRect(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - Vector2.one * radius);
            Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }
    }
}
