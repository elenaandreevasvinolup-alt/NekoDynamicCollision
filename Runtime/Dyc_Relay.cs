using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Максимально лёгкий переадресователь коллбеков столкновений. Без Update, без аллокаций.
    ///
    /// Обязательно вешается на "объект с Rigidbody": Unity шлёт коллбеки столкновений только
    /// GameObject'у с Rigidbody. В скелетном режиме у каждой кости свой Rigidbody — по одному на кость;
    /// в режиме не-скелет / Mesh — один на корне, а какая именно оболочка, выясняется по thisCollider из contacts.
    /// </summary>
    [DisallowMultipleComponent]
    public class Dyc_Relay : MonoBehaviour
    {
        public Dyc_DynamicCollision owner;

        static readonly List<Collider> _selfBuf = new List<Collider>(8);
        static readonly HashSet<Collider> _seen = new HashSet<Collider>();

        void OnCollisionEnter(Collision c) { Forward(DycEventKind.CollisionEnter, c); }
        void OnCollisionExit(Collision c) { Forward(DycEventKind.CollisionExit, c); }

        void OnTriggerEnter(Collider other) { ForwardTrigger(DycEventKind.TriggerEnter, other); }
        void OnTriggerExit(Collider other) { ForwardTrigger(DycEventKind.TriggerExit, other); }

        void Forward(DycEventKind kind, Collision c)
        {
            if (owner == null || c == null) return;

            // Видимый в инспекторе выключатель обязан что-то выключать: без
            // этой проверки галочка «Collision» не влияла ни на что.
            if (!owner.DetectCollisions) return;

            NotifyReceiver(kind, c);

            _seen.Clear();

            // contactCount + GetContact(i) вместо c.contacts: свойство
            // c.contacts каждый раз выделяет новый массив, а класс обещает
            // «без аллокаций». Для Exit массив обычно пуст — это нормальный
            // путь, а не исключение.
            int count = c.contactCount;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    ContactPoint cp = c.GetContact(i);
                    Collider a = cp.thisCollider;
                    Collider b = cp.otherCollider;

                    Collider self, other;
                    if (!Resolve(a, b, out self, out other)) continue;
                    if (!_seen.Add(other)) continue;

                    owner.HandleContact(kind, self, other, cp.point, cp.normal, c.relativeVelocity.magnitude);
                }
                return;
            }

            // Нет контактных точек (например, только что разошлись) — откатываемся к ссылкам самого Collision.
            // У Collision из Unity есть только collider (противник), свою часть приходится угадывать
            // по Collider'у на компоненте; если не вышло — owner подстрахует "прошлой принадлежностью".
            //
            // Точки здесь брать НЕОТКУДА: contactCount == 0 — именно это условие
            // и привело нас в эту ветку. Ноль честнее, чем GetContact(0) за
            // границей массива.
            Collider s2, o2;
            if (Resolve(GetComponent<Collider>(), c.collider, out s2, out o2))
                owner.HandleContact(kind, s2, o2, Vector3.zero, Vector3.zero, c.relativeVelocity.magnitude);
            else
                owner.HandleContact(kind, null, c.collider, Vector3.zero, Vector3.zero, c.relativeVelocity.magnitude);
        }

        /// <summary>
        /// Дублирует штатное сообщение Unity назначенному получателю.
        ///
        /// У системного коллайдера OnCollisionEnter приходит на объект с
        /// Rigidbody. У NDC оболочки лежат на скрытых потомках костей, а
        /// Rigidbody — на корне или кости, поэтому скрипт, повешенный на
        /// «свой» объект, штатных сообщений не увидит. Здесь они
        /// пересылаются — и только если получатель реально задан и не
        /// совпадает с этим объектом. SendMessage вызывается на столкновение,
        /// а не на кадр, поэтому его цена не в горячем пути.
        /// </summary>
        void NotifyReceiver(DycEventKind kind, Collision c)
        {
            var recv = owner.Advanced.collisionReceiver;
            if (recv == null || recv.gameObject == gameObject) return;

            switch (kind)
            {
                case DycEventKind.CollisionEnter:
                    recv.SendMessage("OnCollisionEnter", c, SendMessageOptions.DontRequireReceiver);
                    break;
                case DycEventKind.CollisionExit:
                    recv.SendMessage("OnCollisionExit", c, SendMessageOptions.DontRequireReceiver);
                    break;
            }
        }

        void NotifyReceiver(DycEventKind kind, Collider other)
        {
            var recv = owner.Advanced.collisionReceiver;
            if (recv == null || recv.gameObject == gameObject) return;

            switch (kind)
            {
                case DycEventKind.TriggerEnter:
                    recv.SendMessage("OnTriggerEnter", other, SendMessageOptions.DontRequireReceiver);
                    break;
                case DycEventKind.TriggerExit:
                    recv.SendMessage("OnTriggerExit", other, SendMessageOptions.DontRequireReceiver);
                    break;
            }
        }

        void ForwardTrigger(DycEventKind kind, Collider other)
        {
            if (owner == null || other == null) return;
            if (!owner.DetectTriggers) return;

            NotifyReceiver(kind, other);

            // Триггерный коллбек не сообщает, какая своя оболочка сработала, поэтому перебираем все коллайдеры объекта, принадлежащие owner.
            _selfBuf.Clear();
            GetComponents(_selfBuf);
            for (int i = 0; i < _selfBuf.Count; i++)
            {
                Collider self = _selfBuf[i];
                if (self == null || self == other) continue;
                if (!owner.IsOwnCollider(self)) continue;
                owner.HandleContact(kind, self, other, self.bounds.center, Vector3.zero, 0f);
            }
        }

        /// <summary>Определяет по таблице принадлежности owner, какой коллайдер "свой", не полагаясь на размытую семантику thisCollider/otherCollider в Unity.</summary>
        bool Resolve(Collider a, Collider b, out Collider self, out Collider other)
        {
            bool aOwn = owner.IsOwnCollider(a);
            bool bOwn = owner.IsOwnCollider(b);

            if (aOwn && !bOwn) { self = a; other = b; return true; }
            if (bOwn && !aOwn) { self = b; other = a; return true; }

            self = null;
            other = null;
            return false;
        }
    }
}
