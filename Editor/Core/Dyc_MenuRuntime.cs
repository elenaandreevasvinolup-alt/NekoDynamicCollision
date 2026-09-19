using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Пересобирает меню Unity с локализованными подписями.
    ///
    /// Атрибут <c>[MenuItem]</c> — константа времени компиляции, поэтому
    /// подпись из атрибута не может зависеть от выбранного языка: Unity
    /// регистрирует пункты один раз при старте. Единственный способ менять
    /// подписи на лету — внутренний API <c>UnityEditor.Menu</c> через рефлексию.
    ///
    /// Если API недоступен (другая версия Unity), остаются статические
    /// английские подписи из атрибутов — деградация без поломки.
    ///
    /// Пункт с галочкой «Toggle Hull Gizmo» намеренно НЕ локализуется:
    /// его состояние рисует validate-функция, а внутренний делегат меню
    /// возвращает void, и подпись с возвратом bool на него не ложится.
    /// </summary>
    public static class Dyc_MenuRuntime
    {
        const string Root = Dyc_Menu.MenuRoot;

        public class Item
        {
            public string Group;      // сегмент пути второго уровня (английский, не локализуется)
            public string Key;        // ключ локализации
            public string Fallback;   // английская подпись, как в атрибуте
            public string Handler;    // имя статического метода в Dyc_Menu
            public int Priority;
        }

        static readonly Item[] Items =
        {
            new Item { Group = "Window",      Key = "menu.main",    Fallback = "Open Main Window",           Handler = "OpenWindow",      Priority = Dyc_Menu.PMain },
            new Item { Group = "Bake",        Key = "menu.bake",    Fallback = "Bake Selected",              Handler = "BakeSelected",    Priority = Dyc_Menu.PBake },
            new Item { Group = "Bake",        Key = "menu.rebuild", Fallback = "Rebuild Selected",           Handler = "RebuildSelected", Priority = Dyc_Menu.PRebuild },
            new Item { Group = "Diagnostics", Key = "menu.health",  Fallback = "Collision Health",           Handler = "OpenHealth",      Priority = Dyc_Menu.PHealth },
            new Item { Group = "Help",        Key = "menu.about",   Fallback = "About Neko Dynamic Collision", Handler = "About",         Priority = Dyc_Menu.PAbout },
        };

        /// <summary>Полный путь пункта: корень + группа + подпись.
        ///
        /// Группа стоит В ПУТИ, но не в подписи: она одинакова на всех языках,
        /// а переводится только лист. Иначе пришлось бы переводить и имена
        /// групп, а они — часть пути в атрибуте [MenuItem], то есть константа
        /// времени компиляции.</summary>
        static string PathOf(Item item, string leaf)
        {
            return string.IsNullOrEmpty(item.Group)
                ? Root + leaf
                : Root + item.Group + "/" + leaf;
        }

        static Type _menuType;
        static MethodInfo _remove;
        static MethodInfo _add;
        static Type _delegateType;
        static bool _probed;
        static bool _warned;

        public static bool Available
        {
            get
            {
                Probe();
                return _add != null && _remove != null && _delegateType != null;
            }
        }

        static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                var asm = typeof(EditorApplication).Assembly;
                _menuType = asm.GetType("UnityEditor.Menu");
                if (_menuType == null) return;

                _delegateType = _menuType.GetNestedType("MenuFunction",
                                   BindingFlags.Public | BindingFlags.NonPublic)
                               ?? asm.GetType("UnityEditor.MenuFunction");
                if (_delegateType == null) return;

                _remove = _menuType.GetMethod("RemoveMenuItem",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);

                _add = _menuType.GetMethod("AddMenuItem",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(bool), typeof(int), _delegateType }, null);
            }
            catch
            {
                _menuType = null;
                _remove = null;
                _add = null;
                _delegateType = null;
            }
        }

        [InitializeOnLoadMethod]
        static void Boot()
        {
            EditorApplication.delayCall += Rebuild;
        }

        /// <summary>Пересобирает пункты подписями текущего языка. Вызывается
        /// при загрузке домена и при смене языка.</summary>
        public static void Rebuild()
        {
            Probe();

            if (!Available)
            {
                if (!_warned)
                {
                    _warned = true;
                    UnityEngine.Debug.LogWarning(
                        "[NDC] Внутренний API меню недоступен: подписи пунктов останутся " +
                        "английскими. Остальной интерфейс локализуется нормально.");
                }
                return;
            }

            var menuType = typeof(Dyc_Menu);

            // Сначала снимаем статические английские пункты из атрибутов, затем
            // локализованные от прошлого языка. Иначе после первой же смены
            // языка в меню оказались бы оба варианта.
            for (int i = 0; i < Items.Length; i++)
            {
                TryRemove(PathOf(Items[i], Items[i].Fallback));
                TryRemove(PathOf(Items[i], Label(Items[i])));
            }

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];

                var method = menuType.GetMethod(item.Handler,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) continue;

                Delegate del;
                try { del = Delegate.CreateDelegate(_delegateType, method); }
                catch { continue; }

                try { _add.Invoke(null, new object[] { PathOf(item, Label(item)), false, item.Priority, del }); }
                catch { /* останется статический пункт из атрибута */ }
            }
        }

        static void TryRemove(string path)
        {
            try { _remove.Invoke(null, new object[] { path }); }
            catch { /* пункта могло и не быть — это нормально */ }
        }

        public static string Label(Item item)
        {
            string s = Dyc_L10n.T(item.Key);
            // T() возвращает сам ключ, если перевода нет.
            return s == item.Key ? item.Fallback : s;
        }
    }
}
