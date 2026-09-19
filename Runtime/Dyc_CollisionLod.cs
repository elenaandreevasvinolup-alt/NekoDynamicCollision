using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Внешний источник «насколько далеко этот объект».
    ///
    /// Реализуйте, если знаете про сцену больше, чем расстояние до камеры:
    /// например, менеджер сцены учитывает видимость, окклюзию, приоритет
    /// бойцов. NDC не навязывает сценовую сущность — он только спрашивает.
    /// </summary>
    public interface IDyc_LodProvider
    {
        bool TryGetDistance(Transform target, out float distance);
    }

    /// <summary>
    /// LOD столкновений ПЕРСОНАЖА: чем дальше боец, тем меньше оболочек
    /// участвует в столкновениях.
    ///
    /// ПОЧЕМУ ЭТО НЕ ДУБЛИРУЕТ ОБЫЧНЫЙ LOD. Обычный LOD столкновений работает
    /// по статическим телам сцены и
    /// персонажа не трогает. Между тем у персонажа сотня оболочек, и на телефоне
    /// именно они, а не статичный уровень, определяют, сколько бойцов потянет
    /// сцена. Это разные бюджеты, и один не заменяет другой.
    ///
    /// ЧТО ИМЕННО ГАСИТСЯ. Не «всё разом»: в ближней зоне включено всё, в
    /// дальней остаётся КОСТЯК — несколько самых крупных оболочек. Дальний
    /// боец продолжает ловить попадания в корпус, но перестаёт платить за
    /// фаланги пальцев. Гашение оболочки — это `Collider.enabled = false`:
    /// никакого пересчёта, только флаг.
    ///
    /// ЧЕГО ЗДЕСЬ НЕТ. Не пересобираются оболочки, не трогаются зоны, события
    /// и материалы. LOD меняет только то, СКОЛЬКО оболочек активно, и ничего
    /// больше — поэтому он не может испортить ни запекание, ни симуляцию.
    /// </summary>
    [AddComponentMenu("NekoWorks/Dynamic Collision/Collision LOD")]
    [DisallowMultipleComponent]
    public class Dyc_CollisionLod : MonoBehaviour
    {
        [Tooltip("Компонент коллизий, который обслуживается. Ставится автоматически.")]
        public Dyc_DynamicCollision target;

        /// <summary>
        /// Внешний источник расстояния.
        ///
        /// Это и есть ИНТЕРФЕЙС LOD, а не «ещё одна настройка». Смысл в том,
        /// чтобы решение «упрощать или нет» мог принимать кто угодно:
        /// собственный скрипт разработчика, будущий менеджер сцены (например,
        /// будущий менеджер сцены) — что угодно, что знает про
        /// сцену больше, чем расстояние до камеры. NDC при этом остаётся
        /// автономным: без провайдера он считает по камере сам.
        ///
        /// Менеджера сцены у NDC нет намеренно. Сценовые оптимизаторы создают его потому,
        /// что работают со ВСЕЙ сценой; NDC работает с одним объектом, и
        /// заводить ради него сценовый объект было бы лишней сущностью.
        /// </summary>
        public static IDyc_LodProvider Provider;

        /// <summary>
        /// Упрощение включилось или выключилось. Внешние системы подписываются,
        /// чтобы, например, вести свой счётчик или показать индикатор.
        /// </summary>
        public static event System.Action<Dyc_DynamicCollision, bool> Simplified;

        /// <summary>Оболочки, отсортированные по объёму: сначала самые крупные.
        /// Порядок считается один раз — объём в рантайме не меняется.</summary>
        int[] _byVolume;

        bool _far;
        bool _initialised;

        public static Dyc_CollisionLod Attach(Dyc_DynamicCollision target)
        {
            if (target == null) return null;

            var lod = target.GetComponent<Dyc_CollisionLod>();
            if (lod == null) lod = target.gameObject.AddComponent<Dyc_CollisionLod>();
            lod.target = target;
            lod._initialised = false;
            return lod;
        }

        void LateUpdate()
        {
            if (target == null || !target.IsBuilt) return;
            if (!target.Advanced.collisionLod) return;

            var adv = target.Advanced;

            if (!_initialised) Initialise();

            // РАССТОЯНИЕ. Сначала внешний провайдер, потом активная камера.
            // Объявлять камеру не нужно: берётся та, что СЕЙЧАС включена и
            // рисует. Явно указанная в настройках — только переопределение.
            float distance;
            if (Provider != null && Provider.TryGetDistance(transform, out distance))
            {
                // провайдер знает лучше
            }
            else
            {
                Transform reference = adv.lodReference != null ? adv.lodReference : ActiveCameraTransform();
                if (reference == null) return;
                distance = Vector3.Distance(reference.position, transform.position);
            }

            // ГИСТЕРЕЗИС. Без него боец, стоящий ровно на границе зоны, гасил и
            // зажигал оболочки каждый кадр: полсотни `enabled` в обе стороны на
            // каждом кадре — это и просадка, и мигающие попадания.
            float near = adv.lodNearDistance;
            float far = adv.lodFarDistance;
            if (adv.lodHysteresis > 0f)
            {
                if (_far) near += adv.lodHysteresis;
                else far -= adv.lodHysteresis;
            }

            bool shouldBeFar = distance > (far + near) * 0.5f;

            if (adv.lodRequireVisibility && shouldBeFar == false)
            {
                // Дальняя зона и так дешевле; проверка видимости нужна, чтобы не
                // считать «в кадре» того, кто за спиной. Считается только на
                // входе в дальнюю зону, а не каждый кадр.
                var renderer = GetComponentInChildren<Renderer>();
                if (renderer != null) shouldBeFar = !renderer.isVisible;
            }

            if (shouldBeFar != _far)
            {
                _far = shouldBeFar;
                Apply();
                if (Simplified != null) Simplified(target, _far);
            }
        }

        /// <summary>
        /// Камера, которая рисует прямо сейчас.
        ///
        /// Не только `Camera.main`: у него жёстко зашит тег MainCamera, а в
        /// реальных проектах рисует то одна, то другая (переключение видов,
        /// камера от третьего лица, камера из машины). Поэтому перебираются
        /// включённые камеры и берётся та, у которой больше глубина — ровно то,
        /// что видит игрок. Результат кэшируется, пока камера жива.
        /// </summary>
        static Transform ActiveCameraTransform()
        {
            if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCamera.transform;

            Camera best = null;
            var all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                var cam = all[i];
                if (cam == null || !cam.isActiveAndEnabled) continue;
                if (best == null || cam.depth > best.depth) best = cam;
            }

            // Пусто — падаем на Camera.main: он может быть выключен, но это
            // осмысленнее, чем не считать расстояние вовсе.
            if (best == null) best = Camera.main;

            if (best == null && !_warnedNoCamera)
            {
                // Молчать нельзя: LOD просто остался бы в ближней зоне, и это
                // выглядело бы как «галочка включена, а ничего не меняется».
                _warnedNoCamera = true;
                // Метод статический, поэтому контекст объекта не передаётся:
                // предупреждение относится к настройке, а не к конкретному
                // экземпляру, и его достаточно увидеть один раз.
                Debug.LogWarning(
                    "[NDC] Collision LOD: no active camera found, so the distance cannot be " +
                    "computed and the LOD stays in the near band. Set a reference, or turn the LOD off.");
            }

            _cachedCamera = best;
            return best != null ? best.transform : null;
        }

        static Camera _cachedCamera;
        static bool _warnedNoCamera;

        /// <summary>Сортировка по объёму: в дальней зоне остаются самые крупные.</summary>
        void Initialise()
        {
            _initialised = true;

            var set = target.BakedSet;
            if (set == null || set.hulls == null)
            {
                _byVolume = null;
                return;
            }

            var order = new List<int>(set.hulls.Count);
            for (int i = 0; i < set.hulls.Count; i++)
            {
                if (set.hulls[i] != null) order.Add(i);
            }

            order.Sort((a, b) => set.hulls[b].volume.CompareTo(set.hulls[a].volume));
            _byVolume = order.ToArray();
        }

        void Apply()
        {
            var adv = target.Advanced;
            int keep = _far ? Mathf.Clamp(adv.lodFarHullCount, 1, int.MaxValue) : int.MaxValue;

            // Набор оболочек берётся в порядке убывания объёма: первые `keep`
            // остаются включёнными, остальные гасятся.
            var keepSet = new HashSet<int>();
            if (_byVolume != null)
            {
                for (int i = 0; i < _byVolume.Length && i < keep; i++) keepSet.Add(_byVolume[i]);
            }

            for (int i = 0; i < target.HullCount; i++)
            {
                var collider = target.HullCollider(i);
                if (collider == null) continue;

                int hullIndex = target.HullIndexOf(collider);
                bool on = keep == int.MaxValue || keepSet.Contains(hullIndex);
                if (collider.enabled != on) collider.enabled = on;
            }
        }

        void OnDestroy()
        {
            // Компонент уходит — оболочки обязаны вернуться в рабочее
            // состояние. Иначе выключенный LOD оставил бы половину тела
            // неосязаемой, и это выглядело бы как «попадания перестали
            // засчитываться».
            if (target == null) return;

            for (int i = 0; i < target.HullCount; i++)
            {
                var collider = target.HullCollider(i);
                if (collider != null) collider.enabled = true;
            }
        }
    }
}
