using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Иконка компонента Dynamic Collision.
    ///
    /// Глиф — выпуклое тело: шестиугольник с рёбрами и точками в вершинах.
    /// Это ровно то, из чего состоит весь плагин, и в 16 пикселях инспектора
    /// такая фигура читается лучше любой сюжетной сцены.
    ///
    /// Иконка рисуется в память при загрузке домена и вешается на MonoScript
    /// через EditorGUIUtility.SetIconForObject. Именно MonoScript, а не на
    /// экземпляр: тогда она видна и в заголовке инспектора, и в меню
    /// Add Component, и в окне Project — сразу для всех объектов.
    ///
    /// Файл PNG намеренно НЕ создаётся: Unity всё равно не хранит иконку в
    /// .meta и перерисовывает её при каждой перезагрузке домена, так что
    /// незачем оставлять в проекте неиспользуемый ассет.
    /// </summary>
    [InitializeOnLoad]
    public static class Dyc_Icon
    {
        const int Size = 64;
        const float Corner = 13f;

        static Texture2D _texture;

        static readonly Color Bg = new Color(0.157f, 0.169f, 0.192f, 1f);
        static readonly Color Accent = new Color(0.357f, 0.780f, 0.980f, 1f);
        static readonly Color Vertex = new Color(0.925f, 0.945f, 0.965f, 1f);

        static Dyc_Icon()
        {
            EditorApplication.delayCall += Apply;
        }

        /// <summary>Перевесить иконку. Вызывается при загрузке домена и кнопкой
        /// в панели настроек — на случай, если Unity её потеряла.</summary>
        public static void Refresh()
        {
            _texture = null;
            Apply();
        }

        [MenuItem(Dyc_Menu.GTools + "Refresh Component Icon", false, Dyc_Menu.PIcon)]
        static void Apply()
        {
            var script = FindScript();
            if (script == null) return;

            if (_texture == null) _texture = Build();
            EditorGUIUtility.SetIconForObject(script, _texture);
        }

        static MonoScript FindScript()
        {
            const string root = "Assets/NekoDynamicCollision";
            if (!AssetDatabase.IsValidFolder(root)) return null;

            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { root });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == typeof(Dyc_DynamicCollision)) return ms;
            }
            return null;
        }

        /// <summary>Текстура иконки — если её захочет забрать другое окно плагина.</summary>
        public static Texture2D Texture
        {
            get
            {
                if (_texture == null) _texture = Build();
                return _texture;
            }
        }

        // ------------------------------------------------------------------ рисование

        static Texture2D Build()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Dyc_ComponentIcon",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Глиф: выпуклый многоугольник — то, из чего состоит весь плагин.
            Vector2 center = new Vector2(Size * 0.5f, Size * 0.5f);
            var poly = new Vector2[6];
            for (int i = 0; i < poly.Length; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI * 2f / poly.Length;
                poly[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 17f;
            }

            var px = new Color[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                    // фон: скруглённый квадрат
                    float dRect = RoundRect(p, center, new Vector2(Size * 0.5f - 2.5f, Size * 0.5f - 2.5f), Corner);
                    float aBg = Aa(dRect, 0f, 1.1f);

                    // рамка
                    float aBorder = Aa(Mathf.Abs(dRect + 1.6f), 0f, 1.0f);

                    // рёбра глифа
                    float dEdge = float.MaxValue;
                    for (int i = 0; i < poly.Length; i++)
                        dEdge = Mathf.Min(dEdge, Segment(p, poly[i], poly[(i + 1) % poly.Length]));
                    float aEdge = Aa(dEdge, 1.6f, 1.1f);

                    Color c = Bg;
                    c = Color.Lerp(c, Accent, aBorder);
                    c = Color.Lerp(c, Accent, aEdge * 0.95f);

                    // вершины выпуклого тела
                    float dVertex = float.MaxValue;
                    for (int i = 0; i < poly.Length; i++)
                        dVertex = Mathf.Min(dVertex, (p - poly[i]).magnitude);
                    float aVertex = Aa(dVertex, 2.6f, 1.1f);
                    c = Color.Lerp(c, Vertex, aVertex);

                    c.a = Mathf.Max(aBg, Mathf.Max(aBorder, Mathf.Max(aEdge, aVertex)));
                    px[y * Size + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Сглаживание по расстоянию: 1 внутри, 0 снаружи, мягко на границе.</summary>
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

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
