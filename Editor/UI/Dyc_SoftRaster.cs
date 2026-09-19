using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Крошечный программный растеризатор: буфер глубины + буфер цвета, без
    /// шейдеров, без материала, без рендер-пайплайна.
    ///
    /// ЗАЧЕМ СВОЙ. Вид скелета в окне эксперта дважды переводили на GPU
    /// (PreviewRenderUtility + MeshTopology.Lines), и оба раза он не рисовался
    /// вообще: сначала не сработал материал, потом — топология линий. Отладка
    /// такого вслепую, без запуска Unity, — это лотерея, а пользователь уже
    /// дважды получил пустую панель.
    ///
    /// Здесь нет ни одной внешней зависимости: только массивы и арифметика.
    /// Поведение полностью определено кодом ниже, поэтому его можно ПРОВЕРИТЬ
    /// в консольном тесте — что и сделано (voxcheck): грань перекрывает линию
    /// за ней и не перекрывает линию перед ней.
    ///
    /// ГРАНИ РИСУЮТСЯ ТОЛЬКО В ГЛУБИНУ, цвет не пишут. Это и есть «пустой
    /// материал»: октаэдр кости невидим, но закрывает то, что за ним, —
    /// обратную сторону себя, кости позади и сетку пола.
    ///
    /// Глубина хранится как 1/z: в экранном пространстве именно 1/z
    /// интерполируется линейно, поэтому и грань, и линия дают верную глубину
    /// без поправки на перспективу.
    /// </summary>
    public class Dyc_SoftRaster
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>
        /// Во сколько раз буфер ГЛУБИНЫ мельче цветового.
        ///
        /// Заполнение граней — самая дорогая часть кадра (около восьмидесяти
        /// процентов), но точность глубины нужна не пиксельная. Ошибка
        /// перекрытия в пару пикселей на силуэте незаметна, а работы вчетверо
        /// меньше. Цвет остаётся полного разрешения, поэтому штрих не мылится.
        /// </summary>
        public int DepthScale = 2;

        int _dw, _dh;
        float[] _invDepth;
        Color32[] _color;

        public Color32[] ColorBuffer => _color;
        public float[] DepthBuffer => _invDepth;

        // ------------------------------------------------------------------ глубинный градиент

        /// <summary>Ближняя дистанция, где штрих полной яркости.</summary>
        public float FadeNearZ = 1f;
        /// <summary>Дальняя дистанция, где штрих слабеет до FadeMin.</summary>
        public float FadeFarZ = 10f;
        /// <summary>Насколько слабеет дальний штрих. «Чуть-чуть» — это 0.6, не ниже.</summary>
        public float FadeMin = 0.6f;

        /// <summary>
        /// Допуск по глубине для линий, относительный.
        ///
        /// Ребро лежит НА поверхности своей же грани, поэтому их глубины равны,
        /// и строгое сравнение выносится на волю погрешности: часть пикселей
        /// ребра оказывается «чуть дальше» грани и пропадает — линия рвётся в
        /// пунктир. Допуск в десятые доли процента снимает это, не пропуская
        /// чужие кости: реальные кости разнесены по глубине заметно сильнее.
        /// </summary>
        public float LineDepthTolerance = 0.002f;

        float FadeAt(float z)
        {
            if (FadeFarZ <= FadeNearZ) return 1f;
            float t = Mathf.Clamp01((z - FadeNearZ) / (FadeFarZ - FadeNearZ));
            return 1f + (FadeMin - 1f) * t;
        }

        /// <summary>
        /// Строка буфера для экранной координаты y.
        ///
        /// ВНУТРИ всё считается в экранных координатах: y растёт ВНИЗ, как в
        /// GUI. Но Texture2D.SetPixels32 читает массив СНИЗУ ВВЕРХ — его индекс
        /// 0 это левый НИЖНИЙ пиксель. Без этого переворота картинка выходила
        /// вверх ногами. Переворот делается только здесь, в одном месте: менять
        /// соглашение по всему коду значило бы снова где-нибудь его перепутать.
        /// </summary>
        int Row(int y) { return (Height - 1 - y) * Width; }

        /// <summary>Строка буфера ГЛУБИНЫ: он своего размера и своего масштаба.</summary>
        int DepthRow(int y) { return (_dh - 1 - y) * _dw; }

        /// <summary>
        /// Перекрыта ли экранная точка ближней геометрией.
        ///
        /// Нужно подписям линейки: сами штрихи — обычная геометрия и закрываются
        /// костями сами, а цифры рисует IMGUI поверх кадра, и без этой проверки
        /// они висели бы прямо на костях.
        /// </summary>
        public bool Occluded(Vector2 screen, float depth)
        {
            if (depth <= 0f) return true;

            int x = Mathf.RoundToInt(screen.x);
            int y = Mathf.RoundToInt(screen.y);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return true;

            float invz = 1f / depth;
            float faceInv = _invDepth[DepthRow(y / DepthScale) + (x / DepthScale)];
            return invz < faceInv * (1f - LineDepthTolerance);
        }

        public void Begin(int width, int height, Color32 background)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            int dw = Mathf.Max(1, (width + DepthScale - 1) / DepthScale);
            int dh = Mathf.Max(1, (height + DepthScale - 1) / DepthScale);

            if (_color == null || width != Width || height != Height)
            {
                Width = width;
                Height = height;
                _color = new Color32[width * height];
            }

            if (_invDepth == null || dw != _dw || dh != _dh)
            {
                _dw = dw;
                _dh = dh;
                _invDepth = new float[dw * dh];
            }

            // Array.Clear/Fill вместо цикла: они векторизуются и на буфере в
            // полтора миллиона элементов экономят заметную часть кадра.
            // Поведение то же — меняется только скорость.
            System.Array.Clear(_invDepth, 0, _invDepth.Length);
            System.Array.Fill(_color, background);
        }

        /// <summary>
        /// Закрашивает грань ТОЛЬКО В ГЛУБИНУ. z — расстояние от камеры вдоль
        /// взгляда; внутрь передаётся 1/z, потому что именно она линейна по
        /// экрану.
        /// </summary>
        /// <summary>
        /// Закрашивает грань только в глубину.
        ///
        /// depthBias — на сколько МЕТРОВ отодвинуть грань назад. Нужен, чтобы
        /// оболочка не перекрывала СВОИ ЖЕ рёбра: непрозрачный октаэдр,
        /// смотрящий сверху, виден только наполовину — нижние рёбра закрыты его
        /// же верхними гранями. Сдвиг на собственную толщину возвращает полный
        /// каркас, а чужие кости грань по-прежнему перекрывает: она отодвинута
        /// ровно на свою толщину, а не на произвольную величину.
        /// </summary>
        public void Face(Vector2 a, float za, Vector2 b, float zb, Vector2 c, float zc, float depthBias = 0f)
        {
            if (za <= 0f || zb <= 0f || zc <= 0f) return;

            // Работаем в пространстве буфера ГЛУБИНЫ: он мельче цветового,
            // поэтому и заполнения вчетверо меньше. Пиксельная точность глубины
            // не нужна — ошибка перекрытия в пару пикселей на силуэте незаметна.
            if (DepthScale > 1)
            {
                float inv = 1f / DepthScale;
                a *= inv;
                b *= inv;
                c *= inv;
            }

            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-9f) return;

            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
            int y1 = Mathf.Min(_dh - 1, Mathf.CeilToInt(maxY));
            if (y1 < y0) return;

            float ia = 1f / za, ib = 1f / zb, ic = 1f / zc;
            float invArea = 1f / area;

            // Сдвиг переводится в пространство 1/z через производную: d(1/z)/dz
            // = -1/z². Считается один раз на грань, потому что грань маленькая и
            // её глубина почти постоянна.
            float biasInv = 0f;
            if (depthBias > 0f)
            {
                float invzCenter = (ia + ib + ic) / 3f;
                biasInv = invzCenter * invzCenter * depthBias;
            }

            // ИНКРЕМЕНТАЛЬНЫЕ ФУНКЦИИ РЁБЕР.
            //
            // Раньше все три функции считались в каждом пикселе заново — шесть
            // умножений и три вычитания на точку. Но функция ребра линейна по x,
            // поэтому вдоль строки она просто ПРИРАЩАЕТ: три сложения вместо
            // девяти операций. Именно этот цикл и был главной ценой вращения.
            //
            // Знаки не нормализуем: важно лишь, чтобы все три были одного знака.
            // Порядок обхода треугольников от этого перестаёт иметь значение.
            float dx0 = -(b.y - a.y);
            float dx1 = -(c.y - b.y);
            float dx2 = -(a.y - c.y);

            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;

                // ГРАНИЦЫ СТРОКИ, А НЕ ВЕСЬ ПРЯМОУГОЛЬНИК.
                //
                // Обход по ограничивающему прямоугольнику — это работа по
                // площади, а треугольник занимает её половину. Пересечение трёх
                // рёбер со строкой даёт точный отрезок [xs..xe], и цикл идёт
                // только по нему.
                float minX = float.MaxValue, maxX = float.MinValue;
                SpanX(a, b, py, ref minX, ref maxX);
                SpanX(b, c, py, ref minX, ref maxX);
                SpanX(c, a, py, ref minX, ref maxX);
                if (minX > maxX) continue;

                int xs = Mathf.Max(0, Mathf.FloorToInt(minX));
                int xe = Mathf.Min(_dw - 1, Mathf.CeilToInt(maxX));
                if (xe < xs) continue;

                int row = DepthRow(y);
                float px0 = xs + 0.5f;

                float e0 = (b.x - a.x) * (py - a.y) - (b.y - a.y) * (px0 - a.x);
                float e1 = (c.x - b.x) * (py - b.y) - (c.y - b.y) * (px0 - b.x);
                float e2 = (a.x - c.x) * (py - c.y) - (a.y - c.y) * (px0 - c.x);

                for (int x = xs; x <= xe; x++)
                {
                    bool inside = (e0 >= 0f && e1 >= 0f && e2 >= 0f)
                                  || (e0 <= 0f && e1 <= 0f && e2 <= 0f);

                    if (inside)
                    {
                        // Вес вершины a — функция ребра (b→c), и так далее.
                        float invz = (e1 * ia + e2 * ib + e0 * ic) * invArea - biasInv;
                        int index = row + x;
                        if (invz > _invDepth[index]) _invDepth[index] = invz;
                    }

                    e0 += dx0;
                    e1 += dx1;
                    e2 += dx2;
                }
            }
        }

        /// <summary>
        /// Пересечение ребра со строкой. Отрезок строки внутри треугольника
        /// задаётся парой крайних пересечений, и по нему цикл идёт ровно по
        /// делу, а не по всему прямоугольнику.
        /// </summary>
        static void SpanX(Vector2 p, Vector2 q, float py, ref float minX, ref float maxX)
        {
            float dy = q.y - p.y;

            // Горизонтальное ребро строку не пересекает: оно либо совпадает с
            // ней (тогда даёт сразу весь свой отрезок), либо лежит выше/ниже.
            if (Mathf.Abs(dy) < 1e-9f)
            {
                if (Mathf.Abs(py - p.y) > 0.5f) return;
                float lo = Mathf.Min(p.x, q.x);
                float hi = Mathf.Max(p.x, q.x);
                if (lo < minX) minX = lo;
                if (hi > maxX) maxX = hi;
                return;
            }

            float t = (py - p.y) / dy;
            if (t < 0f || t > 1f) return;

            float x = p.x + (q.x - p.x) * t;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }

        /// <summary>
        /// Отрезок с тестом глубины и СГЛАЖИВАНИЕМ.
        ///
        /// Раньше линия рисовалась пошагово (DDA) и закрашивала квадрат 2×2 —
        /// отсюда «毛糙», рваный край: у пикселя было только два состояния,
        /// закрашен или нет. Здесь наоборот: перебираются пиксели вокруг
        /// отрезка, считается расстояние до него, и цвет СМЕШИВАЕТСЯ с фоном
        /// пропорционально покрытию. Край получается гладким, а толщина —
        /// ровно полтора пикселя вместо двух квадратных.
        ///
        /// Глубину линия НЕ пишет: проволока не должна перекрывать проволоку,
        /// иначе пересекающиеся рёбра мерцали бы, вытесняя друг друга.
        /// </summary>
        public void Line(Vector2 a, float za, Vector2 b, float zb, Color32 color, float halfWidth = 0.75f)
        {
            if (za <= 0f || zb <= 0f) return;

            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 1e-4f) return;

            float edge = halfWidth + 0.5f;
            float edgeSq = edge * edge;

            float ia = 1f / za, ib = 1f / zb;

            // Градиент глубины считается на КОНЦАХ и линейно интерполируется
            // вместе с позицией. Считать его в каждом пикселе через 1/z значило
            // бы деление ради эффекта, который и так «чуть-чуть».
            float fadeA = FadeAt(za);
            float fadeB = FadeAt(zb);

            // ПОЛОСА ВДОЛЬ ГЛАВНОЙ ОСИ, А НЕ ПО ПРЯМОУГОЛЬНИКУ.
            //
            // Обход ограничивающего прямоугольника — это работа по ПЛОЩАДИ.
            // У отрезка в 60 px под малым углом прямоугольник занимает около
            // 1500 пикселей, а сама полоса — около 200: семь восьмых работы
            // впустую. Отрезков в кадре больше тысячи, и именно это тормозило
            // вращение.
            //
            // Вдоль главной оси на каждый шаг приходится полтора-два пикселя
            // поперёк, поэтому стоимость становится пропорциональна ДЛИНЕ.
            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
            {
                float extent = edge * length / Mathf.Abs(dx);
                float invDx = 1f / dx;

                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - edge));
                int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + edge));

                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float t = Mathf.Clamp01((px - a.x) * invDx);

                    float lx = a.x + dx * t;
                    float ly = a.y + dy * t;

                    int ya = Mathf.Max(0, Mathf.FloorToInt(ly - extent));
                    int yb = Mathf.Min(Height - 1, Mathf.CeilToInt(ly + extent));

                    float invz = ia + (ib - ia) * t;
                    float fade = fadeA + (fadeB - fadeA) * t;

                    for (int y = ya; y <= yb; y++)
                    {
                        float ddx = px - lx;
                        float ddy = (y + 0.5f) - ly;
                        float d2 = ddx * ddx + ddy * ddy;
                        if (d2 >= edgeSq) continue;

                        float coverage = edge - Mathf.Sqrt(d2);
                        if (coverage <= 0.002f) continue;

                        // Глубина берётся из уменьшенного буфера: координата
                        // делится на его масштаб.
                        float faceInv = _invDepth[DepthRow(y / DepthScale) + (x / DepthScale)];
                        if (invz < faceInv * (1f - LineDepthTolerance)) continue;
                        int index = Row(y) + x;
                        _color[index] = Blend(_color[index], color, coverage * fade);
                    }
                }
            }
            else
            {
                float extent = edge * length / Mathf.Abs(dy);
                float invDy = 1f / dy;

                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - edge));
                int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + edge));

                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    float t = Mathf.Clamp01((py - a.y) * invDy);

                    float lx = a.x + dx * t;
                    float ly = a.y + dy * t;

                    int xa = Mathf.Max(0, Mathf.FloorToInt(lx - extent));
                    int xb = Mathf.Min(Width - 1, Mathf.CeilToInt(lx + extent));

                    float invz = ia + (ib - ia) * t;
                    float fade = fadeA + (fadeB - fadeA) * t;
                    int row = Row(y);
                    int drow = DepthRow(y / DepthScale);

                    for (int x = xa; x <= xb; x++)
                    {
                        float ddx = (x + 0.5f) - lx;
                        float ddy = py - ly;
                        float d2 = ddx * ddx + ddy * ddy;
                        if (d2 >= edgeSq) continue;

                        float coverage = edge - Mathf.Sqrt(d2);
                        if (coverage <= 0.002f) continue;

                        float faceInv = _invDepth[drow + (x / DepthScale)];
                        if (invz < faceInv * (1f - LineDepthTolerance)) continue;
                        int index = row + x;
                        _color[index] = Blend(_color[index], color, coverage * fade);
                    }
                }
            }
        }

        /// <summary>
        /// Смешивание в ЛИНЕЙНОМ пространстве, а не в байтах.
        ///
        /// Воспринимаемая яркость нелинейна по значению канала (гамма ~2.2).
        /// Если смешивать байты напрямую, полупрозрачная кромка получается
        /// темнее, чем должна: линия выглядит толще и грязнее, а её край —
        /// мутным. Это и читалось как «毛糙». Пересчёт в линейное пространство
        /// и обратно даёт тонкую чистую кромку — то, ради чего вообще делают
        /// сглаживание.
        ///
        /// Степень 2 вместо 2.2 и корень вместо обратной степени — сознательное
        /// упрощение: разница на глаз неразличима, а Mathf.Pow на каждый канал
        /// каждого пикселя стоил бы дороже всей остальной растеризации.
        /// </summary>
        static Color32 Blend(Color32 dst, Color32 src, float alpha)
        {
            float inv = 1f - alpha;

            float r = dst.r * dst.r * inv + src.r * src.r * alpha;
            float g = dst.g * dst.g * inv + src.g * src.g * alpha;
            float b = dst.b * dst.b * inv + src.b * src.b * alpha;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.Sqrt(r), 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Sqrt(g), 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Sqrt(b), 0f, 255f),
                255);
        }
    }
}
