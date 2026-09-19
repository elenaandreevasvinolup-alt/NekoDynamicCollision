using System;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Интерфейс для внешних инструментов. Пока оставлено место, ничего не подключено.
    ///
    /// Когда понадобится подключить NSG (граф скриптов) и подобные инструменты, достаточно использовать здешние статические методы,
    /// без рефлексии приватных полей компонента. Все точки входа не зависят от UnityEditor и работают в рантайме.
    /// </summary>
    public static class Dyc_Api
    {
        /// <summary>Сюда сначала попадает любое событие от любого компонента. Возврат true означает "я уже обработал, дальше рассылать не нужно".</summary>
        public static Func<DycEvent, bool> GlobalFilter;

        /// <summary>Обходной слушатель всех событий (не влияет на собственные UnityEvent компонента и реестр строк).</summary>
        public static event DycEventHandler GlobalEvent;

        internal static void RaiseGlobal(DycEvent e)
        {
            GlobalEvent?.Invoke(e);
        }

        /// <summary>Снять все глобальные подписки. Нужно перед новым запуском:
        /// статические делегаты переживают «domain reload disabled» и перезапуск
        /// сцены, а вместе с ними переживают и уничтоженные объекты.</summary>
        public static void ResetGlobal()
        {
            GlobalFilter = null;
            GlobalEvent = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            ResetGlobal();
            Dyc_Events.Clear();
        }

        /// <summary>Рассылка по имени (использует тот же реестр, что и внутренности компонента).</summary>
        public static void Dispatch(string eventName, DycEvent e)
        {
            Dyc_Events.Dispatch(eventName, e);
        }

        public static void Register(string eventName, DycEventHandler handler)
        {
            Dyc_Events.Register(eventName, handler);
        }

        public static void Unregister(string eventName, DycEventHandler handler)
        {
            Dyc_Events.Unregister(eventName, handler);
        }

        /// <summary>Находит компонент (сначала на самом объекте, затем у родителя).</summary>
        public static Dyc_DynamicCollision Find(GameObject go)
        {
            if (go == null) return null;
            var c = go.GetComponent<Dyc_DynamicCollision>();
            if (c != null) return c;
            return go.GetComponentInParent<Dyc_DynamicCollision>();
        }

        /// <summary>Перестраивает коллайдеры в рантайме (вызывать после смены уровня точности или baked-ассета).</summary>
        public static bool Rebuild(GameObject go)
        {
            var c = Find(go);
            if (c == null) return false;
            c.ClearRuntime();
            c.Build();
            return c.IsBuilt;
        }

        /// <summary>Меняет набор baked-ассетов и перестраивает. Для "смены LOD/точности в рантайме".</summary>
        public static bool SwapBaked(GameObject go, Dyc_BakedSet set, bool rebuild = true)
        {
            var c = Find(go);
            if (c == null) return false;
            c.ApplyBaked(set, rebuild);
            return true;
        }

        /// <summary>Временное включение/выключение коллайдеров (для катсцен/скриптовых сцен).</summary>
        public static void SetCollidersEnabled(GameObject go, bool enabled)
        {
            var c = Find(go);
            if (c == null) return;
            c.SetCollidersEnabled(enabled);
        }

        /// <summary>Текущее число элементов / оболочек — для статистики внешних инструментов.</summary>
        public static void GetStats(GameObject go, out int elements, out int hulls)
        {
            var c = Find(go);
            elements = c != null ? c.ElementCount : 0;
            hulls = c != null ? c.HullCount : 0;
        }
    }
}
