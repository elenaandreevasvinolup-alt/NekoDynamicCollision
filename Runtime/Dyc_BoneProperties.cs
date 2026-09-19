using UnityEngine;

namespace NekoDynamicCollision
{
    /// <summary>
    /// Переопределения для ОДНОЙ кости: материал, порог веса, форма,
    /// исключение. Переопределения для одной кости.
    ///
    /// Зачем отдельным компонентом, а не полями в общем списке. Костей
    /// десятки, и задавать исключение «вот этой» через общий список — значит
    /// перечислять пути и следить за их совпадением. Компонент вешается прямо
    /// на кость: он виден в инспекторе там же, где кость, и переживает
    /// переименования, потому что держит ссылку, а не строку.
    ///
    /// Приоритет (от высшего к низшему):
    ///   материал: карта исключений → кость → зона кисти → материал группы;
    ///   остальное: кость → настройки компонента.
    /// </summary>
    [AddComponentMenu("NekoWorks/Dynamic Collision/Bone Properties")]
    [DisallowMultipleComponent]
    public class Dyc_BoneProperties : MonoBehaviour
    {
        [Header("Material")]

        [Tooltip("Use the physics material below instead of the painted group's material for this bone.")]
        public bool overrideMaterial;

        [Tooltip("Physics material applied to every collider of this bone.")]
        public PhysicMaterial physicsMaterial;

        [Header("Shape")]

        [Tooltip("Use the convex flag below for this bone, overriding the component setting.")]
        public bool overrideConvex;

        [Tooltip("true — convex hull (PhysX 255-vertex limit), false — the actual surface.")]
        public bool convex = true;

        [Header("Weight")]

        [Tooltip("Use the bone weight threshold below for this bone.")]
        public bool overrideWeightThreshold;

        [Range(0f, 1f)]
        [Tooltip("Vertices below this bone weight are not assigned to this bone.")]
        public float boneWeightThreshold = 0.25f;

        [Header("Exclusion")]

        [Tooltip("Generate no colliders for this bone and its subtree.")]
        public bool exclude;
    }
}
