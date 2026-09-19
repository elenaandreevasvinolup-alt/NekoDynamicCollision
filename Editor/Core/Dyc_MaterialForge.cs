using System.Collections.Generic;
using UnityEngine;

namespace NekoDynamicCollision.EditorTools
{
    /// <summary>Тип ткани. Влияет на плотность и мягкость, а из них генератор
    /// выводит трение и упругость.</summary>
    public enum Dyc_TissueKind
    {
        None = 0,
        Bone = 1,
        Cartilage = 2,
        Tendon = 3,
        Muscle = 4,
        Fat = 5,
        Skin = 6,
        Organ = 7,
        Lung = 8,
        Brain = 9,
        Keratin = 10
    }

    /// <summary>
    /// Состояние поверхности. Одно и то же тело может быть сухим, мокрым,
    /// в крови или в поту, и это единственное, что PhysicMaterial вообще
    /// способен выразить как «другое трение».
    /// </summary>
    public enum Dyc_SurfaceCondition
    {
        Dry = 0,
        Wet = 1,
        Oiled = 2,
        Bloody = 3,
        Sweaty = 4,
        Icy = 5,
        Frozen = 6,
        Dusty = 7,
        Rough = 8,
        Polished = 9,
        Rusted = 10,
        Worn = 11,
        Clothed = 12,
        Armored = 13,
        Charred = 14
    }

    /// <summary>Одна ткань: плотность (кг/м³) и мягкость (0 — кость, 1 — мозг).</summary>
    public struct DycTissueDef
    {
        public Dyc_TissueKind kind;
        public string display;
        public float density;
        public float softness;
        public string note;
    }

    /// <summary>Доля ткани в смеси. Веса не обязаны давать единицу: они нормируются.</summary>
    public struct DycTissueShare
    {
        public Dyc_TissueKind kind;
        public float weight;
    }

    /// <summary>
    /// Часть тела — это смесь тканей плюс «подушка» (толщина мягкого слоя
    /// поверх жёсткой основы). Отсюда и берутся все её числа: ничего не
    /// выдумано отдельно для груди, отдельно для бедра.
    /// </summary>
    public struct DycBodyPartDef
    {
        public string id;
        public string display;
        public string region;
        public string note;
        public DycTissueShare[] mix;
        public float cushion;
    }

    /// <summary>Множители условия поверхности, применённые к базовому материалу.</summary>
    public struct DycConditionRule
    {
        public Dyc_SurfaceCondition cond;
        public float friction;
        public float bounce;
        public float density;
        public string note;
    }

    /// <summary>
    /// Генератор материалов.
    ///
    /// Зачем он нужен, если есть таблица: таблица отвечает на вопрос «из чего
    /// это сделано», а игра спрашивает другое — «что это такое СЕЙЧАС».
    /// Сухая сталь, мокрая сталь, ржавая сталь и сталь в крови — четыре разных
    /// ощущения, и держать их отдельными строками значит 200 × 15 = 3000 строк,
    /// которые невозможно поддерживать.
    ///
    /// Поэтому два независимых вывода:
    ///   1. ТКАНЬ → числа. Смесь тканей даёт плотность (аддитивно) и мягкость
    ///      (аддитивно плюс подушка), а из мягкости выводятся трение и упругость.
    ///      Так «грудь» — это 75% жира и 25% мышцы, а не магические числа.
    ///   2. УСЛОВИЕ → множители. Мокрое, в масле, в крови, в поту, обледенелое,
    ///      отполированное, ржавое, изношенное, одетое, бронированное.
    ///
    /// Честная граница та же, что у всей библиотеки: PhysicMaterial умеет
    /// четыре числа. Мягкость здесь — ВХОД генератора, а не спрятанное
    /// свойство: она нигде не хранится в ассете и нужна только чтобы вывести
    /// упругость и показать подсказку. Анизотропия (бархат), трение качения
    /// (бревно), вязкость и пластика по-прежнему невыразимы.
    /// </summary>
    public static class Dyc_MaterialForge
    {
        // ------------------------------------------------------------------ ткани

        public static readonly DycTissueDef[] Tissues =
        {
            T(Dyc_TissueKind.Bone,      "Bone",      1900f, 0.10f, "cortical bone, the hardest tissue here"),
            T(Dyc_TissueKind.Cartilage, "Cartilage", 1100f, 0.35f, "joint lining, slippery and springy"),
            T(Dyc_TissueKind.Tendon,    "Tendon",    1200f, 0.22f, "stiff, almost no give"),
            T(Dyc_TissueKind.Muscle,    "Muscle",    1060f, 0.50f, "the default soft tissue"),
            T(Dyc_TissueKind.Fat,       "Fat",        900f, 0.85f, "soft padding, damps everything"),
            T(Dyc_TissueKind.Skin,      "Skin",      1100f, 0.45f, "thin and grippy"),
            T(Dyc_TissueKind.Organ,     "Organ",     1050f, 0.80f, "soft, mostly water"),
            T(Dyc_TissueKind.Lung,      "Lung",       400f, 0.92f, "inflated, the lightest tissue"),
            T(Dyc_TissueKind.Brain,     "Brain",     1040f, 0.95f, "the softest preset in the whole library"),
            T(Dyc_TissueKind.Keratin,   "Keratin",   1300f, 0.15f, "hair and nails, hard and dry"),
        };

        public static DycTissueDef TissueOf(Dyc_TissueKind kind)
        {
            for (int i = 0; i < Tissues.Length; i++)
                if (Tissues[i].kind == kind) return Tissues[i];

            // Неизвестная ткань не должна ронять вывод: берём мышцу.
            return Tissues[3];
        }

        static DycTissueDef T(Dyc_TissueKind kind, string display, float density, float softness, string note)
        {
            return new DycTissueDef
            {
                kind = kind,
                display = display,
                density = density,
                softness = softness,
                note = note
            };
        }

        // ------------------------------------------------------------------ части тела

        static DycTissueShare S(Dyc_TissueKind kind, float weight)
        {
            return new DycTissueShare { kind = kind, weight = weight };
        }

        /// <summary>
        /// Части тела. `cushion` — толщина мягкого слоя: 0 у черепа, 0.85 у
        /// груди и ягодицы. Он подмешивается к мягкости, потому что удар в
        /// мягкое место и удар в то же место без подушки — не одно и то же.
        /// </summary>
        public static readonly DycBodyPartDef[] BodyParts =
        {
            B("body_head",     "Head",      "head",     "skull under a thin scalp",       0.10f, S(Dyc_TissueKind.Bone, 0.70f), S(Dyc_TissueKind.Skin, 0.30f)),
            B("body_skull",    "Skull",     "head",     "hardest hitbox of the body",     0.00f, S(Dyc_TissueKind.Bone, 0.95f), S(Dyc_TissueKind.Skin, 0.05f)),
            B("body_scalp",    "Scalp",     "head",     "skin and hair over the vault",   0.15f, S(Dyc_TissueKind.Skin, 0.50f), S(Dyc_TissueKind.Keratin, 0.30f), S(Dyc_TissueKind.Bone, 0.20f)),
            B("body_hair",     "Hair",      "head",     "keratin mass, mostly air",       0.05f, S(Dyc_TissueKind.Keratin, 1.00f)),
            B("body_face",     "Face",      "head",     "thin muscle over bone",          0.25f, S(Dyc_TissueKind.Muscle, 0.45f), S(Dyc_TissueKind.Skin, 0.35f), S(Dyc_TissueKind.Fat, 0.20f)),
            B("body_cheek",    "Cheek",     "head",     "fat pad, soft and cold",         0.50f, S(Dyc_TissueKind.Fat, 0.60f), S(Dyc_TissueKind.Muscle, 0.25f), S(Dyc_TissueKind.Skin, 0.15f)),
            B("body_lip",      "Lip",       "head",     "muscle under a thin membrane",   0.60f, S(Dyc_TissueKind.Muscle, 0.70f), S(Dyc_TissueKind.Skin, 0.30f)),
            B("body_jaw",      "Jaw",       "head",     "hinged bone, thin covering",     0.05f, S(Dyc_TissueKind.Bone, 0.80f), S(Dyc_TissueKind.Muscle, 0.15f), S(Dyc_TissueKind.Skin, 0.05f)),
            B("body_nose",     "Nose",      "head",     "cartilage, gives easily",        0.10f, S(Dyc_TissueKind.Cartilage, 0.70f), S(Dyc_TissueKind.Skin, 0.30f)),
            B("body_ear",      "Ear",       "head",     "cartilage, springy",             0.15f, S(Dyc_TissueKind.Cartilage, 0.80f), S(Dyc_TissueKind.Skin, 0.20f)),
            B("body_eyeball",  "Eyeball",   "head",     "fluid under pressure",           0.10f, S(Dyc_TissueKind.Organ, 1.00f)),
            B("body_tooth",    "Tooth",     "head",     "enamel over dentine",            0.00f, S(Dyc_TissueKind.Bone, 0.85f), S(Dyc_TissueKind.Keratin, 0.15f)),
            B("body_tongue",   "Tongue",    "head",     "pure muscle, very soft",         0.50f, S(Dyc_TissueKind.Muscle, 0.90f), S(Dyc_TissueKind.Skin, 0.10f)),

            B("body_neck",     "Neck",      "torso",    "muscle over vertebrae",          0.20f, S(Dyc_TissueKind.Muscle, 0.60f), S(Dyc_TissueKind.Skin, 0.20f), S(Dyc_TissueKind.Bone, 0.20f)),
            B("body_chest",    "Chest",     "torso",    "rib cage under fat and muscle",  0.60f, S(Dyc_TissueKind.Fat, 0.75f), S(Dyc_TissueKind.Muscle, 0.25f)),
            B("body_breast",   "Breast",    "torso",    "softest large region of the body",0.85f, S(Dyc_TissueKind.Fat, 0.80f), S(Dyc_TissueKind.Muscle, 0.10f), S(Dyc_TissueKind.Skin, 0.10f)),
            B("body_abdomen",  "Abdomen",   "torso",    "fat wall over organs",           0.65f, S(Dyc_TissueKind.Fat, 0.60f), S(Dyc_TissueKind.Muscle, 0.35f), S(Dyc_TissueKind.Organ, 0.05f)),
            B("body_groin",    "Groin",     "torso",    "soft tissue, poorly protected",  0.60f, S(Dyc_TissueKind.Muscle, 0.50f), S(Dyc_TissueKind.Fat, 0.30f), S(Dyc_TissueKind.Organ, 0.20f)),
            B("body_back",     "Back",      "torso",    "large muscle mass over the spine",0.30f, S(Dyc_TissueKind.Muscle, 0.65f), S(Dyc_TissueKind.Bone, 0.20f), S(Dyc_TissueKind.Fat, 0.15f)),
            B("body_hip",      "Hip",       "torso",    "bone close to the surface",      0.20f, S(Dyc_TissueKind.Bone, 0.55f), S(Dyc_TissueKind.Muscle, 0.25f), S(Dyc_TissueKind.Fat, 0.20f)),
            B("body_pelvis",   "Pelvis",    "torso",    "heavy bone ring",                0.10f, S(Dyc_TissueKind.Bone, 0.70f), S(Dyc_TissueKind.Muscle, 0.20f), S(Dyc_TissueKind.Fat, 0.10f)),

            B("body_shoulder", "Shoulder",  "arm",      "muscle over the joint",          0.25f, S(Dyc_TissueKind.Muscle, 0.60f), S(Dyc_TissueKind.Bone, 0.30f), S(Dyc_TissueKind.Skin, 0.10f)),
            B("body_bicep",    "Bicep",     "arm",      "thick muscle belly",             0.40f, S(Dyc_TissueKind.Muscle, 0.85f), S(Dyc_TissueKind.Fat, 0.10f), S(Dyc_TissueKind.Skin, 0.05f)),
            B("body_elbow",    "Elbow",     "arm",      "bone with a thin pad",           0.20f, S(Dyc_TissueKind.Bone, 0.50f), S(Dyc_TissueKind.Cartilage, 0.30f), S(Dyc_TissueKind.Skin, 0.20f)),
            B("body_forearm",  "Forearm",   "arm",      "muscle and two bones",           0.30f, S(Dyc_TissueKind.Muscle, 0.70f), S(Dyc_TissueKind.Bone, 0.20f), S(Dyc_TissueKind.Skin, 0.10f)),
            B("body_wrist",    "Wrist",     "arm",      "many small bones, no padding",   0.20f, S(Dyc_TissueKind.Bone, 0.55f), S(Dyc_TissueKind.Cartilage, 0.25f), S(Dyc_TissueKind.Skin, 0.20f)),
            B("body_hand",     "Hand",      "arm",      "bones and thin muscle",          0.20f, S(Dyc_TissueKind.Muscle, 0.50f), S(Dyc_TissueKind.Bone, 0.35f), S(Dyc_TissueKind.Skin, 0.15f)),
            B("body_palm",     "Palm",      "arm",      "muscle and fat, grippy skin",    0.50f, S(Dyc_TissueKind.Muscle, 0.70f), S(Dyc_TissueKind.Fat, 0.20f), S(Dyc_TissueKind.Skin, 0.10f)),
            B("body_fist",     "Fist",      "arm",      "knuckles forward, almost no give",0.15f, S(Dyc_TissueKind.Bone, 0.50f), S(Dyc_TissueKind.Muscle, 0.40f), S(Dyc_TissueKind.Skin, 0.10f)),
            B("body_knuckle",  "Knuckle",   "arm",      "bone under a thin skin",         0.20f, S(Dyc_TissueKind.Bone, 0.60f), S(Dyc_TissueKind.Skin, 0.30f), S(Dyc_TissueKind.Muscle, 0.10f)),
            B("body_nail",     "Nail",      "arm",      "keratin plate",                  0.00f, S(Dyc_TissueKind.Keratin, 1.00f)),

            B("body_glute",    "Glute",     "leg",      "thickest fat pad of the body",   0.75f, S(Dyc_TissueKind.Fat, 0.80f), S(Dyc_TissueKind.Muscle, 0.20f)),
            B("body_thigh",    "Thigh",     "leg",      "muscle over the femur",          0.55f, S(Dyc_TissueKind.Muscle, 0.60f), S(Dyc_TissueKind.Fat, 0.30f), S(Dyc_TissueKind.Bone, 0.10f)),
            B("body_knee",     "Knee",      "leg",      "cartilage and bone, no padding", 0.25f, S(Dyc_TissueKind.Cartilage, 0.50f), S(Dyc_TissueKind.Bone, 0.35f), S(Dyc_TissueKind.Skin, 0.15f)),
            B("body_calf",     "Calf",      "leg",      "muscle belly, springy",          0.45f, S(Dyc_TissueKind.Muscle, 0.70f), S(Dyc_TissueKind.Fat, 0.20f), S(Dyc_TissueKind.Bone, 0.10f)),
            B("body_shin",     "Shin",      "leg",      "bone right under the skin",      0.20f, S(Dyc_TissueKind.Bone, 0.60f), S(Dyc_TissueKind.Muscle, 0.25f), S(Dyc_TissueKind.Skin, 0.15f)),
            B("body_ankle",    "Ankle",     "leg",      "bone and tendon, thin",          0.20f, S(Dyc_TissueKind.Bone, 0.60f), S(Dyc_TissueKind.Cartilage, 0.20f), S(Dyc_TissueKind.Skin, 0.20f)),
            B("body_foot",     "Foot",      "leg",      "many bones in a stiff sole",     0.30f, S(Dyc_TissueKind.Bone, 0.60f), S(Dyc_TissueKind.Muscle, 0.25f), S(Dyc_TissueKind.Skin, 0.15f)),
            B("body_sole",     "Sole",      "leg",      "thick keratinised skin",         0.50f, S(Dyc_TissueKind.Skin, 0.60f), S(Dyc_TissueKind.Fat, 0.30f), S(Dyc_TissueKind.Muscle, 0.10f)),
        };

        static DycBodyPartDef B(string id, string display, string region, string note, float cushion,
            params DycTissueShare[] mix)
        {
            return new DycBodyPartDef
            {
                id = id,
                display = display,
                region = region,
                note = note,
                mix = mix,
                cushion = cushion
            };
        }

        // ------------------------------------------------------------------ условия поверхности

        /// <summary>
        /// Множители. Трение умножается на оба коэффициента (покоя и
        /// скольжения) — иначе мокрое место оставалось бы «липким на старте».
        ///
        /// Порядок важен для чтения: Dry стоит первым и ничего не меняет, это
        /// тождество. «Одетое» и «бронированное» меняют ещё и плотность:
        /// поверх тела появляется слой, и массу считает уже он.
        /// </summary>
        public static readonly DycConditionRule[] Conditions =
        {
            C(Dyc_SurfaceCondition.Dry,      1.00f, 1.00f, 1.00f, "reference state"),
            C(Dyc_SurfaceCondition.Wet,      0.55f, 0.90f, 1.00f, "water film: a third less grip"),
            C(Dyc_SurfaceCondition.Oiled,    0.30f, 0.95f, 1.00f, "oil film: almost no grip left"),
            C(Dyc_SurfaceCondition.Bloody,   0.45f, 0.92f, 1.00f, "blood is a lubricant, not just decoration"),
            C(Dyc_SurfaceCondition.Sweaty,   0.75f, 0.95f, 1.00f, "damp skin, a quarter less grip"),
            C(Dyc_SurfaceCondition.Icy,      0.15f, 0.80f, 1.00f, "ice glaze: grip is gone"),
            C(Dyc_SurfaceCondition.Frozen,   0.50f, 1.05f, 0.92f, "stiff and brittle, slightly springier"),
            C(Dyc_SurfaceCondition.Dusty,    1.10f, 0.70f, 1.00f, "grit grips, but damps the bounce"),
            C(Dyc_SurfaceCondition.Rough,    1.35f, 0.85f, 1.00f, "roughened surface, more grip"),
            C(Dyc_SurfaceCondition.Polished, 0.60f, 1.10f, 1.00f, "polished, springier and slipperier"),
            C(Dyc_SurfaceCondition.Rusted,   1.20f, 0.70f, 0.98f, "oxide layer: grippy and dull"),
            C(Dyc_SurfaceCondition.Worn,     0.85f, 0.85f, 1.00f, "worn in, both numbers drop"),
            C(Dyc_SurfaceCondition.Clothed,  1.25f, 0.60f, 0.75f, "a cloth layer over the base"),
            C(Dyc_SurfaceCondition.Armored,  0.80f, 0.90f, 1.60f, "plates over the base: heavier, smoother"),
            C(Dyc_SurfaceCondition.Charred,  1.15f, 0.75f, 0.90f, "char layer, dull and grippy"),
        };

        static DycConditionRule C(Dyc_SurfaceCondition cond, float friction, float bounce, float density, string note)
        {
            return new DycConditionRule
            {
                cond = cond,
                friction = friction,
                bounce = bounce,
                density = density,
                note = note
            };
        }

        public static DycConditionRule RuleOf(Dyc_SurfaceCondition cond)
        {
            for (int i = 0; i < Conditions.Length; i++)
                if (Conditions[i].cond == cond) return Conditions[i];
            return Conditions[0];
        }

        /// <summary>Стабильный ASCII-суффикс: попадает в имя ассета, поэтому
        /// не зависит от языка интерфейса.</summary>
        public static string ConditionId(Dyc_SurfaceCondition cond)
        {
            switch (cond)
            {
                case Dyc_SurfaceCondition.Wet: return "wet";
                case Dyc_SurfaceCondition.Oiled: return "oiled";
                case Dyc_SurfaceCondition.Bloody: return "bloody";
                case Dyc_SurfaceCondition.Sweaty: return "sweaty";
                case Dyc_SurfaceCondition.Icy: return "icy";
                case Dyc_SurfaceCondition.Frozen: return "frozen";
                case Dyc_SurfaceCondition.Dusty: return "dusty";
                case Dyc_SurfaceCondition.Rough: return "rough";
                case Dyc_SurfaceCondition.Polished: return "polished";
                case Dyc_SurfaceCondition.Rusted: return "rusted";
                case Dyc_SurfaceCondition.Worn: return "worn";
                case Dyc_SurfaceCondition.Clothed: return "clothed";
                case Dyc_SurfaceCondition.Armored: return "armored";
                case Dyc_SurfaceCondition.Charred: return "charred";
                default: return "dry";
            }
        }

        public static string ConditionLabel(Dyc_SurfaceCondition cond)
        {
            return Dyc_L10n.T("forge.cond." + ConditionId(cond));
        }

        public static string ConditionNote(Dyc_SurfaceCondition cond)
        {
            return RuleOf(cond).note;
        }

        // ------------------------------------------------------------------ вывод чисел

        /// <summary>
        /// Выводит материал из смеси тканей.
        ///
        /// Плотность и мягкость аддитивны по весам, а трение и упругость
        /// выводятся из мягкости: мягкое цепляется сильнее и почти не
        /// отскакивает, жёсткое скользит и звенит. Это и есть «генератор» —
        /// ни одна часть тела не описана числами вручную.
        /// </summary>
        public static DycMaterialPreset FromTissues(DycTissueShare[] mix, float cushion,
            string id, string display, string category, string notes)
        {
            float weight = 0f, density = 0f, softness = 0f;
            Dyc_TissueKind dominant = Dyc_TissueKind.Muscle;
            float best = -1f;

            if (mix != null)
            {
                for (int i = 0; i < mix.Length; i++)
                {
                    float w = mix[i].weight;
                    if (w <= 0f) continue;

                    var def = TissueOf(mix[i].kind);
                    weight += w;
                    density += w * def.density;
                    softness += w * def.softness;

                    if (w > best) { best = w; dominant = mix[i].kind; }
                }
            }

            // Пустая смесь не должна давать нулевой материал: берём мышцу.
            if (weight <= 0f)
            {
                weight = 1f;
                density = TissueOf(Dyc_TissueKind.Muscle).density;
                softness = TissueOf(Dyc_TissueKind.Muscle).softness;
                dominant = Dyc_TissueKind.Muscle;
            }

            density /= weight;
            softness = Mathf.Clamp01(softness / weight + cushion * 0.12f);

            float dynamicFriction = Round2(Mathf.Clamp01(0.45f + 0.35f * softness));
            float staticFriction = Round2(Mathf.Clamp01(dynamicFriction + 0.06f + 0.03f * softness));
            float bounciness = Round2(Mathf.Clamp01(0.28f * (1f - softness) + 0.02f));

            return new DycMaterialPreset
            {
                id = id,
                display = display,
                categoryKey = category,
                staticFriction = staticFriction,
                dynamicFriction = dynamicFriction,
                bounciness = bounciness,
                density = Mathf.Round(density),
                notes = Compose(notes, mix, cushion),
                requiredBounceThreshold = 0f,
                tissue = dominant,
                softness = Round2(softness),
                derived = true
            };
        }

        /// <summary>Ткани и части тела — всё, что не является строкой таблицы.</summary>
        public static DycMaterialPreset[] DerivedPresets()
        {
            var list = new List<DycMaterialPreset>(Tissues.Length + BodyParts.Length);

            for (int i = 0; i < Tissues.Length; i++)
            {
                var def = Tissues[i];
                list.Add(FromTissues(
                    new[] { S(def.kind, 1f) },
                    0f,
                    "tissue_" + def.kind.ToString().ToLowerInvariant(),
                    def.display,
                    "tissue",
                    def.note));
            }

            for (int i = 0; i < BodyParts.Length; i++)
            {
                var part = BodyParts[i];
                list.Add(FromTissues(
                    part.mix,
                    part.cushion,
                    part.id,
                    part.display,
                    "body",
                    part.note));
            }

            return list.ToArray();
        }

        public static bool TryGetBodyPart(string id, out DycBodyPartDef part)
        {
            for (int i = 0; i < BodyParts.Length; i++)
            {
                if (BodyParts[i].id == id) { part = BodyParts[i]; return true; }
            }
            part = default;
            return false;
        }

        /// <summary>Состав в процентах плюс подушка: это и есть обоснование чисел.</summary>
        static string Compose(string notes, DycTissueShare[] mix, float cushion)
        {
            if (mix == null || mix.Length == 0) return notes;

            float weight = 0f;
            for (int i = 0; i < mix.Length; i++) if (mix[i].weight > 0f) weight += mix[i].weight;
            if (weight <= 0f) return notes;

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(notes)) sb.Append(notes).Append(" · ");

            bool first = true;
            for (int i = 0; i < mix.Length; i++)
            {
                if (mix[i].weight <= 0f) continue;
                if (!first) sb.Append(" + ");
                sb.Append(Mathf.RoundToInt(mix[i].weight / weight * 100f))
                  .Append("% ")
                  .Append(TissueOf(mix[i].kind).display.ToLowerInvariant());
                first = false;
            }

            if (cushion > 0.01f)
                sb.Append(" · cushion ").Append(cushion.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        // ------------------------------------------------------------------ условие поверх таблицы

        /// <summary>
        /// Накладывает условие поверхности на готовый пресет.
        ///
        /// Dry возвращает пресет как есть: тождество не должно плодить
        /// «steel_dry» рядом с «steel».
        /// </summary>
        public static DycMaterialPreset Forge(DycMaterialPreset basePreset, Dyc_SurfaceCondition cond)
        {
            if (cond == Dyc_SurfaceCondition.Dry) return basePreset;

            var rule = RuleOf(cond);
            var p = basePreset;

            p.id = basePreset.id + "_" + ConditionId(cond);
            p.display = basePreset.display + " · " + ConditionLabel(cond);
            p.staticFriction = Round2(Mathf.Clamp01(basePreset.staticFriction * rule.friction));
            p.dynamicFriction = Round2(Mathf.Clamp01(basePreset.dynamicFriction * rule.friction));
            p.bounciness = Round2(Mathf.Clamp01(basePreset.bounciness * rule.bounce));
            p.density = Mathf.Max(1f, Mathf.Round(basePreset.density * rule.density));

            // Порог отскока нужен и производному: мокрый мяч всё так же должен
            // отскакивать, иначе условие «выключит» сам материал.
            p.requiredBounceThreshold = basePreset.requiredBounceThreshold;
            p.derived = true;
            p.notes = (string.IsNullOrEmpty(basePreset.notes) ? "" : basePreset.notes + " · ") + rule.note;

            return p;
        }

        /// <summary>Все варианты одного пресета по условиям, Dry первым.</summary>
        public static DycMaterialPreset[] Variants(DycMaterialPreset basePreset)
        {
            var list = new List<DycMaterialPreset>(Conditions.Length);
            for (int i = 0; i < Conditions.Length; i++)
                list.Add(Forge(basePreset, Conditions[i].cond));
            return list.ToArray();
        }

        /// <summary>Варианты по id базового пресета. Пустой массив, если id неизвестен.</summary>
        public static DycMaterialPreset[] Variants(string baseId)
        {
            if (Dyc_MaterialPresets.TryGet(baseId, out var basePreset))
                return Variants(basePreset);
            return new DycMaterialPreset[0];
        }

        /// <summary>Похоже ли имя группы на часть тела: нужно авто-назначению.</summary>
        public static string GuessBodyPart(string loweredName)
        {
            if (string.IsNullOrEmpty(loweredName)) return null;

            for (int i = 0; i < BodyParts.Length; i++)
            {
                if (loweredName.Contains(BodyParts[i].id.Substring(5))) return BodyParts[i].id;
                if (loweredName.Contains(BodyParts[i].display.ToLowerInvariant())) return BodyParts[i].id;
            }
            return null;
        }

        /// <summary>
        /// Узнаёт пресет по имени ассета (DYC_&lt;id&gt;). Нужно окну: у группы
        /// лежит PhysicMaterial, и по нему не видно ни происхождения, ни того,
        /// что это вариант по условию поверхности. Имя ассета — единственное
        /// место, где эта информация вообще есть.
        /// </summary>
        public static bool Resolve(string assetName, out DycMaterialPreset preset)
        {
            preset = default;
            if (string.IsNullOrEmpty(assetName)) return false;

            string id = assetName.StartsWith("DYC_") ? assetName.Substring(4) : assetName;
            if (Dyc_MaterialPresets.TryGet(id, out preset)) return true;

            // Вариант по условию: <база>_<условие>
            int cut = id.LastIndexOf('_');
            if (cut <= 0) return false;

            string suffix = id.Substring(cut + 1);
            if (!Dyc_MaterialPresets.TryGet(id.Substring(0, cut), out var basePreset)) return false;

            for (int i = 0; i < Conditions.Length; i++)
            {
                if (ConditionId(Conditions[i].cond) != suffix) continue;
                preset = Forge(basePreset, Conditions[i].cond);
                return true;
            }
            return false;
        }

        static float Round2(float v)
        {
            return Mathf.Round(v * 100f) / 100f;
        }
    }
}
