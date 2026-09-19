using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace NekoDynamicCollision
{
    /// <summary>Полезная нагрузка UnityEvent. Слушатели, зарегистрированные из кода, работают; в Inspector постоянные слушатели не добавить (свой struct не поддерживает ArgumentCache).</summary>
    [Serializable]
    public class DycEventHook : UnityEvent<DycEvent> { }

    public delegate void DycEventHandler(DycEvent e);

    /// <summary>
    /// Реестр строковых имён событий.
    ///
    /// Не ищем слушателей по голым строкам по всему проекту: регистрация идёт сюда, и отправка тоже,
    /// поэтому при опечатке в имени в диагностике видно "отправлено, но никто не принял".
    /// </summary>
    public static class Dyc_Events
    {
        static readonly Dictionary<string, DycEventHandler> _handlers =
            new Dictionary<string, DycEventHandler>(StringComparer.Ordinal);

        /// <summary>Статистика отправок, используется панелью диагностики.</summary>
        public static int DispatchCount { get; private set; }
        public static int UnmatchedCount { get; private set; }
        public static string LastUnmatchedName { get; private set; }
        public static string LastEventName { get; private set; }

        public static int HandlerCount => _handlers.Count;

        public static void Register(string name, DycEventHandler handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            _handlers.TryGetValue(name, out var existing);
            _handlers[name] = existing + handler;
        }

        public static void Unregister(string name, DycEventHandler handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            if (!_handlers.TryGetValue(name, out var existing)) return;
            var next = existing - handler;
            if (next == null) _handlers.Remove(name);
            else _handlers[name] = next;
        }

        public static bool HasHandler(string name)
        {
            return !string.IsNullOrEmpty(name) && _handlers.ContainsKey(name);
        }

        /// <summary>Отправка. Пустое имя — сразу возврат, без учёта в статистике.</summary>
        public static void Dispatch(string name, DycEvent e)
        {
            if (string.IsNullOrEmpty(name)) return;

            DispatchCount++;
            LastEventName = name;

            if (_handlers.TryGetValue(name, out var h) && h != null)
            {
                h(e);
                return;
            }

            UnmatchedCount++;
            LastUnmatchedName = name;
        }

        public static void ResetStats()
        {
            DispatchCount = 0;
            UnmatchedCount = 0;
            LastUnmatchedName = null;
            LastEventName = null;
        }

        /// <summary>Чистим при смене/выходе из сцены, чтобы статическая таблица не держала уничтоженные объекты.</summary>
        public static void Clear()
        {
            _handlers.Clear();
            ResetStats();
        }

        /// <summary>
        /// Сброс перед каждым запуском игры.
        ///
        /// Статическая таблица живёт в домене, а домен при «Enter Play Mode
        /// Options / domain reload disabled» между запусками НЕ пересоздаётся.
        /// Без этого сброса подписки прошлого запуска остаются и держат
        /// уничтоженные объекты; в сборке то же самое происходит при перезапуске
        /// сцены. SubsystemRegistration — самая ранняя точка, до Awake сцены.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Clear();
        }
    }
}
