using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Строки интерфейса.
    ///
    /// Встроен ТОЛЬКО английский (см. Dyc_L10n.En.cs) — это язык-основа, к
    /// которому откатывается любой незаполненный ключ. Все остальные языки
    /// лежат данными в Locale/&lt;code&gt;/strings.json, по папке на язык,
    /// поэтому перевод добавляется без правки кода.
    ///
    /// Языки НЕ нумеруются жёстко: индекс 0 — английский, дальше идут
    /// обнаруженные папки в порядке поля order. Добавление языка не сдвигает
    /// уже существующие индексы.
    /// </summary>
    public static partial class Dyc_L10n
    {
        const string PrefKey = "Neko.DynamicCollision.Language";

        /// <summary>Отдельный ключ для зеркалирования: язык и направление
        /// интерфейса — РАЗНЫЕ решения, и связывать их жёстко нельзя.</summary>
        const string PrefKeyMirror = "Neko.DynamicCollision.MirrorRtl";

        /// <summary>Путь к языковым пакетам относительно Application.dataPath.
        /// Лежат в корне плагина, рядом с Documents — как у NekoScriptGraph.</summary>
        public const string LocaleRootRelative = "/NekoDynamicCollision/Locale";

        /// <summary>Старое расположение (Editor/Locale). Поддерживаем, чтобы
        /// плагин не сломался у тех, кто обновился поверх прежней версии.</summary>
        const string LocaleRootLegacy = "/NekoDynamicCollision/Editor/Locale";

        class Lang
        {
            public string Code;
            public string Name;
            public int Order;
            public bool Rtl;
            public Dictionary<string, string> Table;
        }

        static readonly List<Lang> _langs = new List<Lang>();
        static int _index;
        static bool _loaded;

        public static int Count { get { Ensure(); return _langs.Count + 1; } }

        public static int Index
        {
            get { Ensure(); return _index; }
            set
            {
                Ensure();
                _index = Mathf.Clamp(value, 0, _langs.Count);
                EditorPrefs.SetInt(PrefKey, _index);
            }
        }

        public static string NameAt(int i)
        {
            Ensure();
            if (i <= 0) return "English";
            int k = i - 1;
            return k < _langs.Count ? _langs[k].Name : "?";
        }

        public static string CodeAt(int i)
        {
            Ensure();
            if (i <= 0) return "en";
            int k = i - 1;
            return k < _langs.Count ? _langs[k].Code : "en";
        }

        /// <summary>Текущий язык пишется справа налево.</summary>
        public static bool IsRtl
        {
            get
            {
                Ensure();
                if (_index <= 0) return false;
                int k = _index - 1;
                return k < _langs.Count && _langs[k].Rtl;
            }
        }

        /// <summary>
        /// Зеркалировать ли интерфейс для языков справа налево.
        ///
        /// Это ОТДЕЛЬНАЯ настройка, а не следствие языка, и вот почему.
        /// Арабский и иврит читаются справа налево, но разработчик мог привыкнуть
        /// к английской раскладке окон и хочет читать перевод, не переворачивая
        /// компоновку. Связать эти два решения жёстко значит отнять выбор.
        ///
        /// Область действия — ТОЛЬКО окно плагина. Зеркалирование никогда не
        /// трогает интерфейс Unity: инспектор компонента рисуется IMGUI и от
        /// направления не зависит, а меню Unity вообще нельзя перевернуть.
        /// Никакой глобальный стиль не меняется: см. Dyc_Rtl.
        /// </summary>
        public static bool MirrorRtl
        {
            get { return EditorPrefs.GetBool(PrefKeyMirror, true); }
            set { EditorPrefs.SetBool(PrefKeyMirror, value); }
        }

        /// <summary>Разрешённый путь к папке языков. Нужен панели настроек,
        /// чтобы сбой поиска был виден, а не молчал.</summary>
        public static string LocaleRoot
        {
            get { Ensure(); return ResolveLocaleRoot(); }
        }

        public static string T(string key)
        {
            Ensure();
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (_index > 0)
            {
                var lang = _langs[_index - 1];
                if (lang.Table != null && lang.Table.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v))
                    return v;
            }

            return English.TryGetValue(key, out string e) ? e : key;
        }

        public static string T(string key, params object[] args)
        {
            string s = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, s, args); }
            catch { return s; }
        }

        // ------------------------------------------------------------------ загрузка

        static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;

            _index = EditorPrefs.GetInt(PrefKey, 0);

            string root = ResolveLocaleRoot();
            if (!Directory.Exists(root)) { _index = 0; return; }

            var dirs = Directory.GetDirectories(root);
            for (int i = 0; i < dirs.Length; i++)
            {
                string file = Path.Combine(dirs[i], "strings.json");
                if (!File.Exists(file)) continue;

                var lang = Parse(file);
                if (lang == null) continue;
                _langs.Add(lang);
            }

            _langs.Sort((a, b) =>
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
            });

            _index = Mathf.Clamp(_index, 0, _langs.Count);
        }

        /// <summary>Плагин можно перенести: сначала стандартный путь, затем обход корня Assets.</summary>
        static string ResolveLocaleRoot()
        {
            string direct = Application.dataPath + LocaleRootRelative;
            if (Directory.Exists(direct)) return direct;

            string legacy = Application.dataPath + LocaleRootLegacy;
            if (Directory.Exists(legacy)) return legacy;

            // Плагин мог быть перенесён целиком: обходим корень Assets.
            try
            {
                var top = Directory.GetDirectories(Application.dataPath);
                for (int i = 0; i < top.Length; i++)
                {
                    string a = Path.Combine(top[i], "Locale");
                    if (Directory.Exists(a)) return a;

                    string b = Path.Combine(top[i], "Editor/Locale");
                    if (Directory.Exists(b)) return b;
                }
            }
            catch { /* откат на английский */ }

            return direct;
        }

        static Lang Parse(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var file = JsonUtility.FromJson<LocaleFile>(json);
                if (file == null) return null;

                var table = new Dictionary<string, string>(file.ui != null ? file.ui.Length : 0);
                if (file.ui != null)
                {
                    for (int i = 0; i < file.ui.Length; i++)
                    {
                        var e = file.ui[i];
                        if (e == null || string.IsNullOrEmpty(e.key)) continue;
                        table[e.key] = e.value;
                    }
                }

                return new Lang
                {
                    Code = string.IsNullOrEmpty(file.code) ? Path.GetFileName(Path.GetDirectoryName(path)) : file.code,
                    Name = string.IsNullOrEmpty(file.name) ? file.code : file.name,
                    Order = file.order,
                    Rtl = file.rtl,
                    Table = table
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NDC] Не удалось прочитать файл языка {path}: {ex.Message}");
                return null;
            }
        }

        [System.Serializable]
        public class LocaleFile
        {
            public int schemaVersion;
            public string code;
            public string name;
            public bool rtl;
            public int order;
            public Entry[] ui;
        }

        [System.Serializable]
        public class Entry
        {
            public string key;
            public string value;
        }
    }
}
