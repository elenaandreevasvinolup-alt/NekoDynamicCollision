using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Сохранение результатов запекания на диск.
    ///
    /// Обязательно сохранять как ассет: если Mesh выпуклой оболочки держать только в памяти,
    /// он пропадёт при входе в Play и не будет существовать после сборки. Это условие "нулевых вычислений в рантайме".
    /// </summary>
    public static class Dyc_AssetIO
    {
        public const string DefaultRoot = "Assets/NekoDynamicCollision/Baked";

        public static string RootFor(Dyc_DynamicCollision target)
        {
            string sceneFolder = target.gameObject.scene.IsValid()
                ? Sanitize(target.gameObject.scene.name)
                : "NoScene";
            return DefaultRoot + "/" + sceneFolder;
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('.', '_');
        }

        // ------------------------------------------------------------------ владелец ассета

        /// <summary>
        /// Путь объекта-владельца: «Сцена:/Корень/Таз».
        ///
        /// Имя объекта НЕ уникально (префаб-инстансы называются одинаково), а
        /// путь в сцене — уникален. По нему ассет отличает «свой» объект от
        /// однофамильца и не даёт второму запеканию затереть первое.
        /// </summary>
        public static string SourcePathOf(Dyc_DynamicCollision target)
        {
            if (target == null) return null;

            string scene = target.gameObject.scene.IsValid()
                ? target.gameObject.scene.name
                : "NoScene";

            var sb = new StringBuilder();
            Transform t = target.transform;
            while (t != null)
            {
                sb.Insert(0, "/" + t.name);
                t = t.parent;
            }
            return scene + ":" + sb;
        }

        /// <summary>Владелец существующего ассета по пути. null — файла нет либо он старый (без поля).</summary>
        static string OwnerOf(string path)
        {
            var set = AssetDatabase.LoadAssetAtPath<Dyc_BakedSet>(path);
            if (set != null) return set.sourcePath;

            var mask = AssetDatabase.LoadAssetAtPath<Dyc_PaintMask>(path);
            if (mask != null) return mask.sourcePath;

            return null;
        }

        /// <summary>
        /// Свободный путь для ассета этого объекта.
        ///
        /// Логика намеренно консервативная: если файл занят и это НЕ наш объект
        /// (в том числе старый ассет без sourcePath), имя получает числовой
        /// суффикс. Так однофамильцы не затирают друг друга, а повторное
        /// запекание того же объекта по-прежнему пишет в свой файл.
        /// </summary>
        static string UniquePath(string folder, string baseName, string kind, string owner)
        {
            string basePath = folder + "/" + Sanitize(baseName) + kind;
            if (AssetDatabase.LoadMainAssetAtPath(basePath) == null) return basePath;
            if (OwnerOf(basePath) == owner) return basePath;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = folder + "/" + Sanitize(baseName) + "_" + i + kind;
                if (AssetDatabase.LoadMainAssetAtPath(candidate) == null) return candidate;
                if (OwnerOf(candidate) == owner) return candidate;
            }
            return basePath;
        }

        // ------------------------------------------------------------------ результаты запекания

        /// <summary>Сначала готовим ассет (пока пустой), запекатель наполняет его, а затем SaveBaked прикрепляет Mesh как подобъект.</summary>
        public static Dyc_BakedSet EnsureBakedAsset(Dyc_DynamicCollision target, string folder, string baseName)
        {
            EnsureFolder(folder);

            string owner = SourcePathOf(target);
            string path = UniquePath(folder, baseName, "_Baked.asset", owner);

            var set = AssetDatabase.LoadAssetAtPath<Dyc_BakedSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<Dyc_BakedSet>();
                AssetDatabase.CreateAsset(set, path);
            }
            set.sourcePath = owner;
            return set;
        }

        public static Dyc_BakedSet SaveBaked(Dyc_DynamicCollision target, Dyc_BakedSet set, string folder, string baseName)
        {
            EnsureFolder(folder);

            string owner = SourcePathOf(target);

            // У готового набора путь уже есть — он и главный. Свободное имя
            // подбирается только при первом сохранении.
            string path = set != null ? AssetDatabase.GetAssetPath(set) : null;
            if (string.IsNullOrEmpty(path))
                path = UniquePath(folder, baseName, "_Baked.asset", owner);

            if (set == null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Dyc_BakedSet>(path);
                set = existing != null ? existing : ScriptableObject.CreateInstance<Dyc_BakedSet>();
                if (existing == null) AssetDatabase.CreateAsset(set, path);
            }
            set.sourcePath = owner;

            // Чистим старые подобъекты, чтобы при каждом запекании не копились осиротевшие Mesh
            var loaded = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < loaded.Length; i++)
            {
                var o = loaded[i];
                if (o == null || o == set) continue;
                if (o is Mesh || o is Dyc_BakedSet) continue;
                AssetDatabase.RemoveObjectFromAsset(o);
            }
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] is Mesh m) AssetDatabase.RemoveObjectFromAsset(m);
            }

            for (int i = 0; i < set.hulls.Count; i++)
            {
                var h = set.hulls[i];
                if (h == null || h.mesh == null) continue;
                h.mesh.name = $"DYC_{Sanitize(baseName)}_e{h.elementIndex}_g{h.groupIndex}_{i}";
                AssetDatabase.AddObjectToAsset(h.mesh, set);
            }

            set.generatedUtcTicks = System.DateTime.UtcNow.Ticks;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return set;
        }

        public static void DeleteBaked(Dyc_BakedSet set)
        {
            if (set == null) return;
            string path = AssetDatabase.GetAssetPath(set);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
        }

        // ------------------------------------------------------------------ метки кисти

        public static Dyc_PaintMask SavePaintMask(Dyc_DynamicCollision target, Dyc_PaintMask mask,
            string folder, string baseName, int triangleCount, string hash)
        {
            EnsureFolder(folder);

            string owner = SourcePathOf(target);
            string path = mask != null ? AssetDatabase.GetAssetPath(mask) : null;
            if (string.IsNullOrEmpty(path))
                path = UniquePath(folder, baseName, "_Paint.asset", owner);

            if (mask == null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Dyc_PaintMask>(path);
                mask = existing != null ? existing : ScriptableObject.CreateInstance<Dyc_PaintMask>();
                if (existing == null) AssetDatabase.CreateAsset(mask, path);
            }

            mask.EnsureSize(triangleCount);
            mask.sourceHash = hash;
            mask.sourcePath = owner;
            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();
            return mask;
        }

        // ------------------------------------------------------------------ физические материалы

        public static PhysicMaterial SaveMaterial(string folder, string name, float staticFriction,
            float dynamicFriction, float bounciness, PhysicMaterialCombine frictionCombine,
            PhysicMaterialCombine bounceCombine)
        {
            EnsureFolder(folder);
            string path = folder + "/" + Sanitize(name) + ".physicMaterial";

            var mat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
            bool isNew = mat == null;
            if (isNew) mat = new PhysicMaterial();

            mat.staticFriction = staticFriction;
            mat.dynamicFriction = dynamicFriction;
            mat.bounciness = bounciness;
            mat.frictionCombine = frictionCombine;
            mat.bounceCombine = bounceCombine;

            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            return mat;
        }

        // ------------------------------------------------------------------ утилиты

        public static List<T> FindAll<T>(string folder) where T : Object
        {
            var result = new List<T>();
            if (!AssetDatabase.IsValidFolder(folder)) return result;

            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                var obj = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (obj != null) result.Add(obj);
            }
            return result;
        }
    }
}
