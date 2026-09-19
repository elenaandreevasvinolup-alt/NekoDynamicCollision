using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Trigger-детекция по элементам.
    ///
    /// Unity-евский OnTriggerEnter срабатывает по паре Rigidbody, а одна ragdoll — это всего одна пара rigidbody,
    /// поэтому физический слой не может сказать, "какая именно кость вошла в зону". Добавление Rigidbody каждой кости сломало бы
    /// обещание нулевых накладных расходов. Единственный верный путь — низкочастотная выборка OverlapBox + дельта по каждому element.
    ///
    /// Семантику надо прописать чётко: это выборка, а не событие. Очень быстрое прохождение можно пропустить, компенсируется расширением sweepMargin.
    /// </summary>
    public class Dyc_TriggerPoll
    {
        readonly Dyc_DynamicCollision _owner;
        readonly List<Collider>[] _elementColliders;

        readonly Dictionary<int, List<Collider>> _prev = new Dictionary<int, List<Collider>>();
        readonly Collider[] _buffer = new Collider[32];

        int _cursor;
        int _perFrame = 1;

        public Dyc_TriggerPoll(Dyc_DynamicCollision owner, List<Collider>[] elementColliders)
        {
            _owner = owner;
            _elementColliders = elementColliders;
        }

        public int ElementCount => _elementColliders != null ? _elementColliders.Length : 0;

        /// <summary>Сколько element обрабатывать за кадр. По умолчанию 1/4 в разбивке по кадрам, минимум 1.</summary>
        public void Configure(int perFrame)
        {
            _perFrame = Mathf.Max(1, perFrame);
        }

        public void Tick(float sweepMargin, LayerMask mask)
        {
            if (_elementColliders == null || _elementColliders.Length == 0) return;

            int n = Mathf.Min(_perFrame, _elementColliders.Length);
            for (int k = 0; k < n; k++)
            {
                if (_cursor >= _elementColliders.Length) _cursor = 0;
                PollElement(_cursor, sweepMargin, mask);
                _cursor++;
            }
        }

        void PollElement(int element, float sweepMargin, LayerMask mask)
        {
            var cols = _elementColliders[element];
            if (cols == null || cols.Count == 0) return;

            if (!TryGetWorldBounds(cols, out Bounds b)) return;
            b.Expand(sweepMargin * 2f);

            int hitCount = Physics.OverlapBoxNonAlloc(b.center, b.extents, _buffer, Quaternion.identity, mask, QueryTriggerInteraction.Collide);

            if (!_prev.TryGetValue(element, out var prevList))
            {
                prevList = new List<Collider>(4);
                _prev[element] = prevList;
            }

            // Новые (вошли)
            for (int i = 0; i < hitCount; i++)
            {
                Collider other = _buffer[i];
                if (!IsValidOther(other)) continue;
                if (!prevList.Contains(other))
                {
                    prevList.Add(other);
                    _owner.HandleContact(DycEventKind.TriggerEnter, null, other, other.bounds.center, Vector3.zero, 0f, element);
                }
            }

            // Ушедшие (вышли)
            for (int i = prevList.Count - 1; i >= 0; i--)
            {
                Collider was = prevList[i];
                bool still = false;
                for (int j = 0; j < hitCount; j++)
                {
                    if (_buffer[j] == was) { still = true; break; }
                }
                if (still) continue;

                prevList.RemoveAt(i);
                if (was != null)
                    _owner.HandleContact(DycEventKind.TriggerExit, null, was, was.bounds.center, Vector3.zero, 0f, element);
            }
        }

        bool IsValidOther(Collider other)
        {
            if (other == null) return false;
            if (!other.isTrigger) return false;          // Только trigger-детекция, физические столкновения отданы коллбэкам
            if (_owner.IsOwnCollider(other)) return false; // Не сталкиваемся с самими собой
            if (other.transform.IsChildOf(_owner.transform)) return false;
            return true;
        }

        static bool TryGetWorldBounds(List<Collider> cols, out Bounds b)
        {
            b = default;
            bool any = false;
            for (int i = 0; i < cols.Count; i++)
            {
                Collider c = cols[i];
                if (c == null || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            return any;
        }

        public void Reset()
        {
            _prev.Clear();
            _cursor = 0;
        }
    }
}
