using System;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Обычное использование NDC — «как системный коллайдер».
    ///
    /// Идея простая: разработчику не должно быть нужно изучать новый API,
    /// чтобы получить столкновения. Если он умеет работать с Collider и
    /// Rigidbody, он уже умеет работать с NDC — потому что NDC и создаёт
    /// ОБЫЧНЫЕ Collider'ы: MeshCollider на скрытых потомках, Rigidbody на
    /// объекте, штатные OnCollision*/OnTrigger*, обычные слои, физические
    /// материалы, Physics.Raycast и Physics.OverlapSphere без единой правки.
    ///
    /// Этот класс — тонкая обёртка на привычные операции, чтобы не искать
    /// компонент руками. Всё, что он делает, можно сделать и напрямую:
    /// GetComponent&lt;Dyc_DynamicCollision&gt;(). Ничего своего он не прячет.
    ///
    /// Разделение ровно такое, как хотелось:
    ///   ОБЫЧНОЕ — этот класс и стандартный Unity-физика;
    ///   ОСОБОЕ — зоны, кисть, события, материалы по зонам, LOD, живое
    ///            обновление: это на компоненте и в его продвинутых настройках.
    ///
    /// Типовой сценарий:
    /// <code>
    /// using NekoDynamicCollision;
    ///
    /// void Start()
    /// {
    ///     // Добавить коллайдеры как обычный Collider — запекание не нужно.
    ///     Dyc_Collision.Attach(gameObject);
    /// }
    ///
    /// void OnCollisionEnter(Collision c)
    /// {
    ///     // Работает как обычно: это штатное сообщение Unity.
    /// }
    /// </code>
    /// </summary>
    public static class Dyc_Collision
    {
        // ------------------------------------------------------------------
        // Как обычный Collider: наличие и сборка
        // ------------------------------------------------------------------

        /// <summary>Найти компонент NDC на объекте или у родителя. Как
        /// GetComponent&lt;Collider&gt;(), только для NDC.</summary>
        public static Dyc_DynamicCollision Find(GameObject go)
        {
            if (go == null) return null;

            var c = go.GetComponent<Dyc_DynamicCollision>();
            return c != null ? c : go.GetComponentInParent<Dyc_DynamicCollision>();
        }

        /// <summary>
        /// Добавить NDC на объект и сразу собрать коллайдеры.
        ///
        /// Аналог «повесить Collider»: если запекания нет, набор строится из
        /// меша (по оболочке на кость). Точность ниже запечённой — это цена
        /// того, что шаг в редакторе не нужен.
        /// </summary>
        public static Dyc_DynamicCollision Attach(GameObject go, bool generateNow = true)
        {
            if (go == null) return null;

            var c = go.GetComponent<Dyc_DynamicCollision>();
            if (c == null) c = go.AddComponent<Dyc_DynamicCollision>();

            if (generateNow && !c.IsBuilt) Build(go);
            return c;
        }

        /// <summary>Собрать коллайдеры: из запечённого набора, а если его нет —
        /// сгенерировать в рантайме. false — собирать было не из чего.</summary>
        public static bool Build(GameObject go)
        {
            var c = Find(go);
            if (c == null) return false;

            if (c.BakedSet != null && c.BakedSet.hulls.Count > 0)
            {
                c.Build();
                return c.IsBuilt;
            }

            var set = Dyc_RuntimeBuild.Build(c);
            if (set == null || set.hulls.Count == 0) return false;

            c.ApplyBaked(set, true);
            return c.IsBuilt;
        }

        /// <summary>Пересобрать заново (после смены настроек или набора).</summary>
        public static bool Rebuild(GameObject go)
        {
            var c = Find(go);
            if (c == null) return false;

            c.ClearRuntime();
            c.Build();
            return c.IsBuilt;
        }

        // ------------------------------------------------------------------
        // Как обычный Collider: состояние
        // ------------------------------------------------------------------

        /// <summary>Собраны ли коллайдеры прямо сейчас.</summary>
        public static bool IsBuilt(GameObject go)
        {
            var c = Find(go);
            return c != null && c.IsBuilt;
        }

        /// <summary>Включены ли коллайдеры. Как Collider.enabled, но сразу для
        /// всех оболочек персонажа.</summary>
        public static bool GetEnabled(GameObject go)
        {
            var c = Find(go);
            if (c == null || !c.IsBuilt) return false;

            var collider = c.HullCollider(0);
            return collider != null && collider.enabled;
        }

        /// <summary>Включить или выключить все оболочки сразу.</summary>
        public static void SetEnabled(GameObject go, bool enabled)
        {
            var c = Find(go);
            if (c != null) c.SetCollidersEnabled(enabled);
        }

        /// <summary>Сделать оболочки триггерами. Как Collider.isTrigger.</summary>
        public static void SetTrigger(GameObject go, bool trigger)
        {
            var c = Find(go);
            if (c == null) return;

            c.Advanced.isTrigger = trigger;

            for (int i = 0; i < c.HullCount; i++)
            {
                var col = c.HullCollider(i);
                if (col != null) col.isTrigger = trigger;
            }
        }

        /// <summary>
        /// Физический материал на все оболочки сразу.
        ///
        /// Действует немедленно, но НЕ переживает пересборку: постоянные
        /// материалы задаются зонами и картой материалов — это особая часть,
        /// а не обычная.
        /// </summary>
        public static void SetMaterial(GameObject go, PhysicMaterial material)
        {
            var c = Find(go);
            if (c == null) return;

            for (int i = 0; i < c.HullCount; i++)
            {
                var col = c.HullCollider(i);
                if (col != null) col.sharedMaterial = material;
            }
        }

        /// <summary>Все оболочки персонажа. Это ОБЫЧНЫЕ Collider'ы: их можно
        /// передавать в Physics.IgnoreCollision, класть в слои и так далее.</summary>
        public static Collider[] GetColliders(GameObject go)
        {
            var c = Find(go);
            if (c == null || !c.IsBuilt) return Array.Empty<Collider>();

            var result = new Collider[c.HullCount];
            for (int i = 0; i < result.Length; i++) result[i] = c.HullCollider(i);
            return result;
        }

        /// <summary>Пройти по оболочкам без выделения массива.</summary>
        public static void ForEachCollider(GameObject go, Action<Collider> action)
        {
            if (action == null) return;

            var c = Find(go);
            if (c == null) return;

            for (int i = 0; i < c.HullCount; i++)
            {
                var col = c.HullCollider(i);
                if (col != null) action(col);
            }
        }

        // ------------------------------------------------------------------
        // Как обычный Collider: получатель штатных сообщений
        // ------------------------------------------------------------------

        /// <summary>
        /// Куда дублировать OnCollision*/OnTrigger*, если скрипт висит не на
        /// объекте с Rigidbody. Без этого вызова всё работает как обычно:
        /// штатные сообщения приходят на объект с Rigidbody.
        /// </summary>
        public static void SetReceiver(GameObject go, Transform receiver)
        {
            var c = Find(go);
            if (c != null) c.Advanced.collisionReceiver = receiver;
        }

        /// <summary>
        /// Починить NDC после Instantiate. Клон получил копии рантайм-детей
        /// (кадры, оболочки) и, если коллайдеры строились в рантайме, пустой
        /// набор — метод чистит это и собирает заново.
        /// </summary>
        public static void FixInstantiated(GameObject clone, GameObject source = null)
        {
            var c = clone != null ? clone.GetComponent<Dyc_DynamicCollision>() : null;
            if (c == null) return;

            var src = source != null ? source.GetComponent<Dyc_DynamicCollision>() : null;
            c.FixInstantiated(src);
        }

        // ------------------------------------------------------------------
        // Особая часть: живое обновление (обычному использованию не нужно)
        // ------------------------------------------------------------------

        /// <summary>Включить пересборку оболочек по текущей позе скелета.</summary>
        public static Dyc_LiveUpdate EnableLiveUpdate(GameObject go, bool continuous = true)
        {
            var c = Find(go);
            if (c == null) return null;

            c.Advanced.liveUpdate = true;
            return Dyc_LiveUpdate.Attach(c);
        }

        public static void DisableLiveUpdate(GameObject go)
        {
            var c = Find(go);
            if (c == null) return;

            c.Advanced.liveUpdate = false;

            var live = c.GetComponent<Dyc_LiveUpdate>();
            if (live != null) live.StopUpdating();
        }
    }
}
