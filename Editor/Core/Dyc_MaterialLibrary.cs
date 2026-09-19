namespace NekoDynamicCollision.EditorTools
{
    /// <summary>
    /// Таблица физических материалов: только данные, никакого поведения.
    ///
    /// Разделение намеренное: здесь лежат строки таблицы, а вся арифметика
    /// (combine, аудит физики проекта, оценка пары) живёт в
    /// <see cref="Dyc_MaterialPresets"/>, а вывод значений из тканей и условий
    /// поверхности — в <see cref="Dyc_MaterialForge"/>. Так таблицу можно
    /// расширять, не трогая логику, и наоборот.
    ///
    /// Значения — инженерные приближения из справочников (трение скольжения по
    /// сухой стали, плотность материала), приведённые к четырём числам
    /// PhysicMaterial. Это НЕ симуляция: см. честную границу в
    /// <see cref="Dyc_MaterialPresets"/>.
    ///
    /// Плотность — кг/м³. Для тканей это плотность УПАКОВАННОГО полотна
    /// (реальная ткань почти вся воздух), иначе Auto Mass давал бы пыль.
    ///
    /// `display` и `notes` намеренно английские: так подписаны библиотеки
    /// материалов в Unity и Unreal, и это избавляет от 225×15 переводов,
    /// которые всё равно никто не читает. Локализуются только НАЗВАНИЯ
    /// КАТЕГОРИЙ (cat.&lt;key&gt;) и подписи интерфейса.
    /// </summary>
    public static class Dyc_MaterialLibrary
    {
        /// <summary>
        /// Строки таблицы. Категории `body` и `tissue` сюда НЕ входят: они
        /// выводятся генератором из тканевых смесей, поэтому у них один
        /// источник правды — <see cref="Dyc_MaterialForge"/>.
        /// </summary>
        static readonly DycMaterialPreset[] Base =
        {
            // ============================================================== металлы
            P("steel",         "Steel",              "metal",   0.74f, 0.57f, 0.25f, 7850f,  "structural steel, most common"),
            P("stainless",     "Stainless Steel",    "metal",   0.60f, 0.50f, 0.28f, 8000f,  "slightly more slippery than carbon steel"),
            P("iron",          "Cast Iron",          "metal",   0.70f, 0.55f, 0.25f, 7870f,  "graphite flakes make it dull"),
            P("aluminum",      "Aluminium",          "metal",   0.61f, 0.47f, 0.20f, 2700f,  "light, dents easily"),
            P("cast_aluminum", "Cast Aluminium",     "metal",   0.58f, 0.45f, 0.15f, 2650f,  "porous, dulls impacts"),
            P("anodized",      "Anodised Aluminium", "metal",   0.50f, 0.38f, 0.20f, 2700f,  "oxide layer, smoother than bare"),
            P("copper",        "Copper",             "metal",   0.53f, 0.36f, 0.20f, 8960f,  "soft, grabs"),
            P("brass",         "Brass",              "metal",   0.51f, 0.44f, 0.22f, 8500f,  "cartridge cases"),
            P("bronze",        "Bronze",             "metal",   0.55f, 0.42f, 0.22f, 8800f,  "bearings and statues"),
            P("titanium",      "Titanium",           "metal",   0.60f, 0.45f, 0.22f, 4500f,  "light and hard"),
            P("lead",          "Lead",               "metal",   0.60f, 0.40f, 0.05f, 11340f, "very heavy, almost no bounce"),
            P("tungsten",      "Tungsten",           "metal",   0.60f, 0.45f, 0.10f, 19300f, "very dense, penetrators"),
            P("chrome",        "Chrome",             "metal",   0.42f, 0.30f, 0.25f, 7150f,  "hard and polished, low friction"),
            P("nickel",        "Nickel",             "metal",   0.56f, 0.44f, 0.22f, 8900f,  null),
            P("zinc",          "Zinc",               "metal",   0.55f, 0.42f, 0.18f, 7140f,  null),
            P("magnesium",     "Magnesium",          "metal",   0.55f, 0.42f, 0.20f, 1740f,  "lightest structural metal"),
            P("gold",          "Gold",               "metal",   0.55f, 0.40f, 0.20f, 19300f, "soft and dense, jewellery"),
            P("silver",        "Silver",             "metal",   0.53f, 0.40f, 0.22f, 10490f, null),
            P("platinum",      "Platinum",           "metal",   0.50f, 0.38f, 0.20f, 21450f, null),
            P("galvanized",    "Galvanised Steel",   "metal",   0.62f, 0.50f, 0.20f, 7850f,  "zinc coating, slightly rougher"),
            P("rusted_steel",  "Rusted Steel",       "metal",   0.80f, 0.70f, 0.15f, 7850f,  "oxide layer, grippy and dull"),
            P("gun_steel",     "Gun Steel",          "metal",   0.62f, 0.48f, 0.20f, 7850f,  "hardened, blued finish"),
            P("tool_steel",    "Tool Steel",         "metal",   0.65f, 0.52f, 0.22f, 7800f,  null),
            P("armor_steel",   "Armour Steel (RHA)", "metal",   0.55f, 0.42f, 0.12f, 7850f,  "rolled homogeneous, plate armour"),
            P("sheet_metal",   "Sheet Metal",        "metal",   0.58f, 0.45f, 0.30f, 7850f,  "thin panels ring like a drum"),
            P("rebar",         "Rebar",              "metal",   0.78f, 0.65f, 0.15f, 7850f,  "ribbed, bites into concrete"),

            // ============================================================== керамика
            P("porcelain",     "Porcelain",          "ceramic", 0.50f, 0.40f, 0.35f, 2400f,  "glazed, rings when struck"),
            P("bone_china",    "Bone China",         "ceramic", 0.48f, 0.38f, 0.32f, 2350f,  null),
            P("ceramic_tile",  "Ceramic Tile",       "ceramic", 0.55f, 0.45f, 0.30f, 2300f,  "floor and wall tile"),
            P("terracotta",    "Terracotta",         "ceramic", 0.65f, 0.55f, 0.20f, 1800f,  "unglazed fired clay, porous"),
            P("alumina",       "Alumina (Al₂O₃)",    "ceramic", 0.60f, 0.48f, 0.25f, 3900f,  "industrial wear plates"),
            P("silicon_carbide","Silicon Carbide",   "ceramic", 0.65f, 0.52f, 0.28f, 3100f,  "very hard, armour inserts"),
            P("zirconia",      "Zirconia",           "ceramic", 0.55f, 0.44f, 0.30f, 6000f,  "dense and tough as ceramics go"),
            P("glass_ceramic", "Glass-Ceramic",      "ceramic", 0.45f, 0.35f, 0.35f, 2500f,  "cooktops, survives thermal shock"),
            P("enamel",        "Enamel",             "ceramic", 0.45f, 0.35f, 0.30f, 2600f,  "fused glass over metal"),
            P("clay_unfired",  "Unfired Clay",       "ceramic", 0.85f, 0.75f, 0.02f, 1600f,  "plastic, absorbs everything"),

            // ============================================================== пластики
            P("abs",           "ABS",                "plastic", 0.50f, 0.35f, 0.30f, 1050f,  "housings and gun frames"),
            P("pvc",           "PVC",                "plastic", 0.45f, 0.32f, 0.25f, 1380f,  null),
            P("nylon",         "Nylon",              "plastic", 0.35f, 0.28f, 0.30f, 1150f,  "self-lubricating"),
            P("acrylic",       "Acrylic",            "plastic", 0.50f, 0.40f, 0.30f, 1180f,  "plexiglass-like"),
            P("polycarb",      "Polycarbonate",      "plastic", 0.45f, 0.35f, 0.35f, 1200f,  "ballistic grade, tough"),
            P("hdpe",          "HDPE",               "plastic", 0.30f, 0.22f, 0.25f, 950f,   "very slippery"),
            P("ptfe",          "PTFE (Teflon)",      "plastic", 0.10f, 0.05f, 0.20f, 2200f,  "extremely low friction"),
            P("peek",          "PEEK",               "plastic", 0.30f, 0.22f, 0.30f, 1320f,  "high-performance, low friction"),
            P("pom",           "POM (Delrin)",       "plastic", 0.35f, 0.25f, 0.35f, 1410f,  "springy and self-lubricating"),
            P("pet",           "PET",                "plastic", 0.45f, 0.34f, 0.28f, 1380f,  "bottles, clear"),
            P("polypropylene", "Polypropylene",      "plastic", 0.35f, 0.26f, 0.25f, 905f,   "living hinges"),
            P("polystyrene",   "Polystyrene",        "plastic", 0.50f, 0.40f, 0.28f, 1050f,  "rigid and brittle"),
            P("tpu",           "TPU",                "plastic", 0.75f, 0.65f, 0.60f, 1200f,  "flexible, rubber-like"),
            P("pla",           "PLA",                "plastic", 0.50f, 0.40f, 0.25f, 1240f,  "3D print, brittle"),
            P("bakelite",      "Bakelite",           "plastic", 0.55f, 0.45f, 0.20f, 1400f,  "old thermoset, hard"),
            P("melamine",      "Melamine",           "plastic", 0.50f, 0.40f, 0.25f, 1500f,  "hard tableware"),
            P("vinyl",         "Vinyl",              "plastic", 0.55f, 0.45f, 0.20f, 1300f,  null),
            P("urethane_rigid","Rigid Urethane",     "plastic", 0.60f, 0.50f, 0.35f, 1100f,  "cast parts, damping"),

            // ============================================================== стекло
            P("glass",         "Glass",              "glass",   0.55f, 0.40f, 0.40f, 2500f,  "soda-lime"),
            P("glass_tempered","Tempered Glass",     "glass",   0.60f, 0.45f, 0.45f, 2500f,  "bouncier than plain glass"),
            P("glass_laminated","Laminated Glass",   "glass",   0.60f, 0.48f, 0.35f, 2500f,  "windshield, holds together"),
            P("glass_frosted","Frosted Glass",       "glass",   0.62f, 0.52f, 0.30f, 2500f,  "etched surface, grippier"),
            P("glass_thick",   "Thick Glass",        "glass",   0.55f, 0.42f, 0.45f, 2500f,  "shop window, 10 mm and up"),
            P("glass_armored", "Armoured Glass",     "glass",   0.55f, 0.45f, 0.20f, 2500f,  "multi-layer, absorbs a shot"),
            P("mirror",        "Mirror",             "glass",   0.45f, 0.35f, 0.40f, 2500f,  "backed glass"),
            P("crystal",       "Crystal",            "glass",   0.45f, 0.35f, 0.45f, 3000f,  "lead crystal, bright ring"),
            P("pyrex",         "Borosilicate",       "glass",   0.48f, 0.38f, 0.40f, 2230f,  "thermal shock proof"),

            // ============================================================== дерево
            P("oak",           "Oak",                "wood",    0.55f, 0.45f, 0.35f, 700f,   null),
            P("pine",          "Pine",               "wood",    0.50f, 0.40f, 0.30f, 500f,   null),
            P("plywood",       "Plywood",            "wood",    0.50f, 0.40f, 0.30f, 600f,   null),
            P("cork",          "Cork",               "wood",    0.60f, 0.50f, 0.15f, 240f,   "absorbs energy"),
            P("birch",         "Birch",              "wood",    0.50f, 0.40f, 0.32f, 650f,   null),
            P("maple",         "Maple",              "wood",    0.55f, 0.45f, 0.35f, 740f,   "hard, bats and floors"),
            P("walnut",        "Walnut",             "wood",    0.52f, 0.42f, 0.30f, 660f,   null),
            P("teak",          "Teak",               "wood",    0.55f, 0.45f, 0.28f, 660f,   "oily, weather resistant"),
            P("mahogany",      "Mahogany",           "wood",    0.50f, 0.40f, 0.30f, 700f,   null),
            P("balsa",         "Balsa",              "wood",    0.55f, 0.45f, 0.25f, 160f,   "extremely light"),
            P("mdf",           "MDF",                "wood",    0.55f, 0.45f, 0.15f, 750f,   "glued fibre, dull"),
            P("particleboard", "Particleboard",      "wood",    0.60f, 0.50f, 0.12f, 700f,   "chipboard, crumbles"),
            P("bamboo",        "Bamboo",             "wood",    0.55f, 0.45f, 0.35f, 700f,   "hard and springy"),
            P("wet_wood",      "Wet Wood",           "wood",    0.70f, 0.60f, 0.25f, 900f,   "swollen, grippier"),
            P("charred_wood",  "Charred Wood",       "wood",    0.75f, 0.65f, 0.15f, 600f,   "char layer, dull and grippy"),
            P("log",           "Round Timber",       "wood",    0.65f, 0.55f, 0.25f, 700f,   "rolls; rolling friction is not expressible"),

            // ============================================================== камень и бетон
            P("concrete",      "Concrete",           "stone",   0.70f, 0.60f, 0.20f, 2400f,  null),
            P("concrete_wet",  "Wet Concrete",       "stone",   0.60f, 0.48f, 0.18f, 2400f,  "water film on the surface"),
            P("brick",         "Brick",              "stone",   0.65f, 0.55f, 0.20f, 1900f,  null),
            P("asphalt",       "Asphalt",            "stone",   0.80f, 0.70f, 0.10f, 2300f,  "road surface, grippy"),
            P("granite",       "Granite",            "stone",   0.65f, 0.55f, 0.20f, 2700f,  null),
            P("marble",        "Marble",             "stone",   0.55f, 0.45f, 0.25f, 2700f,  null),
            P("polished_marble","Polished Marble",   "stone",   0.40f, 0.30f, 0.30f, 2700f,  "slippery, especially when waxed"),
            P("limestone",     "Limestone",          "stone",   0.65f, 0.55f, 0.20f, 2400f,  null),
            P("sandstone",     "Sandstone",          "stone",   0.70f, 0.60f, 0.18f, 2200f,  "gritty"),
            P("slate",         "Slate",              "stone",   0.50f, 0.40f, 0.25f, 2700f,  "smooth sheets"),
            P("basalt",        "Basalt",             "stone",   0.68f, 0.58f, 0.20f, 2900f,  null),
            P("cobblestone",   "Cobblestone",        "stone",   0.75f, 0.65f, 0.15f, 2500f,  null),
            P("gravel",        "Gravel",             "stone",   0.80f, 0.70f, 0.10f, 1700f,  null),
            P("rubble",        "Rubble",             "stone",   0.85f, 0.75f, 0.05f, 2000f,  "loose debris, damping"),
            P("sand",          "Sand",               "stone",   0.90f, 0.75f, 0.05f, 1600f,  "loose bulk density"),

            // ============================================================== резина
            P("rubber",        "Rubber",             "rubber",  0.90f, 0.85f, 0.80f, 1100f,  "needs bounceThreshold ≤ 0.5", 0.5f),
            P("tire",          "Tire Rubber",        "rubber",  0.95f, 0.90f, 0.70f, 1200f,  "needs bounceThreshold ≤ 0.5", 0.5f),
            P("sbr",           "SBR Rubber",         "rubber",  0.95f, 0.90f, 0.75f, 1200f,  "shoe soles", 0.5f),
            P("latex",         "Latex Sheet",        "rubber",  0.85f, 0.80f, 0.85f, 950f,   "thin, very elastic", 0.5f),
            P("epdm",          "EPDM",               "rubber",  0.90f, 0.85f, 0.75f, 1150f,  "weather seals", 0.5f),
            P("butyl",         "Butyl Rubber",       "rubber",  0.90f, 0.85f, 0.60f, 1100f,  "inner tubes, damping", 0.5f),
            P("neoprene",      "Neoprene",           "rubber",  0.85f, 0.80f, 0.70f, 1250f,  null, 0.5f),
            P("silicone",      "Silicone",           "rubber",  0.80f, 0.75f, 0.75f, 1100f,  "soft, high damping", 0.5f),
            P("hose_rubber",   "Hose Rubber",        "rubber",  0.85f, 0.78f, 0.65f, 1150f,  "reinforced tube", 0.5f),
            P("mat_rubber",    "Rubber Mat",         "rubber",  0.92f, 0.88f, 0.70f, 1200f,  "anti-fatigue floor mat", 0.5f),
            P("foam_rubber",   "Rubber Foam",        "rubber",  0.80f, 0.70f, 0.55f, 300f,   "sponge, absorbs", 0.5f),
            P("insole_gel",    "Shock Gel",          "rubber",  0.90f, 0.85f, 0.35f, 1000f,  "insole gel, eats impacts", 0.5f),

            // ============================================================== ткань и кожа
            P("leather",       "Leather",            "fabric",  0.60f, 0.55f, 0.20f, 860f,   null),
            P("canvas",        "Canvas",             "fabric",  0.70f, 0.60f, 0.10f, 700f,   "heavy plain weave"),
            P("carpet",        "Carpet",             "fabric",  0.80f, 0.70f, 0.05f, 400f,   "almost no bounce"),
            P("kevlar",        "Kevlar",             "fabric",  0.60f, 0.50f, 0.15f, 1440f,  "aramid weave, body armour"),
            P("body_armor",    "Body Armour",        "fabric",  0.60f, 0.50f, 0.08f, 1200f,  "plates over aramid, absorbs"),
            P("vest_fabric",   "Vest Shell",         "fabric",  0.70f, 0.60f, 0.06f, 500f,   "carrier over ballistic panels"),
            P("cotton",        "Cotton",             "fabric",  0.70f, 0.60f, 0.05f, 300f,   "plain weave"),
            P("denim",         "Denim",              "fabric",  0.75f, 0.65f, 0.05f, 400f,   "heavy twill"),
            P("silk",          "Silk",               "fabric",  0.35f, 0.25f, 0.08f, 200f,   "very smooth, slides"),
            P("satin",         "Satin",              "fabric",  0.30f, 0.20f, 0.10f, 250f,   "glossy weave"),
            P("wool",          "Wool",               "fabric",  0.80f, 0.70f, 0.05f, 350f,   "hairy, grippy"),
            P("linen",         "Linen",              "fabric",  0.65f, 0.55f, 0.06f, 300f,   null),
            P("burlap",        "Burlap",             "fabric",  0.85f, 0.75f, 0.03f, 350f,   "coarse jute"),
            P("velvet",        "Velvet",             "fabric",  0.85f, 0.78f, 0.04f, 350f,   "pile; friction is direction dependent and is not expressible"),
            P("felt",          "Felt",               "fabric",  0.80f, 0.72f, 0.06f, 250f,   null),
            P("nomex",         "Nomex",              "fabric",  0.65f, 0.55f, 0.10f, 700f,   "flame resistant, race suits"),
            P("spandex",       "Spandex",            "fabric",  0.80f, 0.72f, 0.15f, 400f,   "stretch, clings"),
            P("goretex",       "Gore-Tex",           "fabric",  0.55f, 0.45f, 0.08f, 300f,   "laminated shell"),
            P("parachute",     "Parachute Nylon",    "fabric",  0.45f, 0.35f, 0.10f, 150f,   "ripstop, very light"),
            P("nylon_fabric",  "Nylon Fabric",       "fabric",  0.55f, 0.45f, 0.12f, 250f,   "smooth synthetic"),
            P("suit_fabric",   "Suit Fabric",        "fabric",  0.55f, 0.45f, 0.08f, 350f,   "worsted wool, uniform cloth"),
            P("upholstery",    "Upholstery",         "fabric",  0.75f, 0.65f, 0.10f, 450f,   "padded furniture"),
            P("tarp_heavy",    "Heavy Tarp",         "fabric",  0.50f, 0.40f, 0.15f, 600f,   "PVC-coated, sheds water"),
            P("blanket",       "Blanket",            "fabric",  0.80f, 0.72f, 0.05f, 350f,   "soft, high damping"),
            P("towel",         "Towel",              "fabric",  0.90f, 0.82f, 0.03f, 350f,   "terry, absorbs"),

            // ============================================================== биология
            P("flesh",         "Flesh",              "organic", 0.75f, 0.65f, 0.10f, 1000f,  "close to water density", 0f, Dyc_TissueKind.Muscle,  0.50f),
            P("bone",          "Bone",               "organic", 0.55f, 0.45f, 0.20f, 1900f,  "cortical bone",          0f, Dyc_TissueKind.Bone,    0.10f),
            P("ballisticgel",  "Ballistic Gel",      "organic", 0.70f, 0.60f, 0.05f, 1000f,  "standard 10% gelatin",   0f, Dyc_TissueKind.Organ,   0.90f),

            // ============================================================== лёд, снег, грязь
            P("ice",           "Ice",                "ice",     0.10f, 0.03f, 0.10f, 917f,    null),
            P("ice_wet",       "Wet Ice",            "ice",     0.05f, 0.02f, 0.05f, 917f,    "the most slippery"),
            P("ice_black",     "Black Ice",          "ice",     0.03f, 0.01f, 0.05f, 917f,    "melted and refrozen, the worst case"),
            P("snow",          "Snow",               "ice",     0.30f, 0.20f, 0.02f, 300f,    null),
            P("snow_powder",   "Powder Snow",        "ice",     0.20f, 0.12f, 0.02f, 150f,    "fresh, very light"),
            P("snow_packed",   "Packed Snow",        "ice",     0.35f, 0.25f, 0.05f, 400f,    "trail, holds weight"),
            P("slush",         "Slush",              "ice",     0.25f, 0.15f, 0.02f, 900f,    "half melted, drags"),
            P("hail",          "Hail",               "ice",     0.35f, 0.25f, 0.45f, 700f,    "hard pellets, bounces"),
            P("frozen_ground", "Frozen Ground",      "ice",     0.45f, 0.35f, 0.15f, 1900f,   null),
            P("mud",           "Mud",                "ice",     0.60f, 0.50f, 0.02f, 1700f,   "high damping"),

            // ============================================================== еда
            P("bread",         "Bread",              "food",    0.70f, 0.60f, 0.15f, 300f,    "soft crumb"),
            P("fruit",         "Fruit",              "food",    0.60f, 0.50f, 0.45f, 800f,    "an apple bounces when dropped"),
            P("vegetable",     "Vegetable",          "food",    0.65f, 0.55f, 0.35f, 700f,    null),
            P("meat_raw",      "Raw Meat",           "food",    0.80f, 0.72f, 0.10f, 1050f,   "close to muscle tissue", 0f, Dyc_TissueKind.Muscle, 0.55f),
            P("meat_cooked",   "Cooked Meat",        "food",    0.75f, 0.65f, 0.12f, 1000f,   null,                     0f, Dyc_TissueKind.Muscle, 0.60f),
            P("fish",          "Fish",               "food",    0.45f, 0.35f, 0.20f, 950f,    "slippery while fresh"),
            P("cheese",        "Cheese",             "food",    0.65f, 0.55f, 0.20f, 1000f,   null),
            P("chocolate",     "Chocolate",          "food",    0.50f, 0.40f, 0.20f, 1300f,   null),
            P("ice_cream",     "Ice Cream",          "food",    0.60f, 0.50f, 0.05f, 600f,    null),

            // ============================================================== прочее
            P("cardboard",     "Cardboard",          "other",   0.50f, 0.40f, 0.10f, 200f,    null),
            P("cardboard_wet", "Wet Cardboard",      "other",   0.75f, 0.65f, 0.03f, 400f,    "soggy, falls apart"),
            P("paper",         "Paper",              "other",   0.45f, 0.35f, 0.05f, 800f,    null),
            P("foam",          "Foam",               "other",   0.70f, 0.60f, 0.30f, 50f,     "very light, absorbs energy"),
            P("sponge",        "Sponge",             "other",   0.85f, 0.78f, 0.40f, 200f,    "porous, springs back"),
            P("bubble_wrap",   "Bubble Wrap",        "other",   0.60f, 0.50f, 0.55f, 100f,    "pops, then stops bouncing"),
            P("drywall",       "Drywall",            "other",   0.60f, 0.50f, 0.15f, 700f,    null),
            P("tarp",          "Tarp",               "other",   0.40f, 0.30f, 0.10f, 500f,    null),
            P("sandbag",       "Sandbag",            "other",   0.90f, 0.80f, 0.03f, 1600f,   "dense, absorbs a shot"),
            P("dirt",          "Dirt",               "other",   0.80f, 0.70f, 0.05f, 1500f,   null),
            P("grass",         "Grass Turf",         "other",   0.75f, 0.65f, 0.10f, 600f,    "root mat, damping"),
            P("hay",           "Hay",                "other",   0.80f, 0.70f, 0.08f, 200f,    null),
            P("leaves",        "Leaf Litter",        "other",   0.60f, 0.50f, 0.10f, 150f,    "loose, slippery when wet"),
            P("trash_bag",     "Trash Bag",          "other",   0.55f, 0.45f, 0.15f, 400f,    null),
            P("glass_wool",    "Mineral Wool",       "other",   0.80f, 0.72f, 0.05f, 100f,    "insulation, very light"),
            P("rope",          "Rope",               "other",   0.75f, 0.65f, 0.15f, 800f,    "coiled, grippy"),
            P("net",           "Net",                "other",   0.60f, 0.50f, 0.30f, 300f,    "springy mesh"),
            P("coal",          "Coal",               "other",   0.60f, 0.50f, 0.15f, 1300f,   null),
            P("ash",           "Ash",                "other",   0.75f, 0.65f, 0.05f, 700f,    null),
            P("salt",          "Rock Salt",          "other",   0.60f, 0.50f, 0.25f, 1300f,   "crystalline, brittle"),
            P("sugar",         "Sugar",              "other",   0.65f, 0.55f, 0.25f, 1300f,   null),
            P("wax",           "Wax",                "other",   0.40f, 0.30f, 0.15f, 900f,    "slippery, deforms with heat"),
        };

        /// <summary>
        /// Полная библиотека: строки таблицы плюс выведенные генератором
        /// (ткани и части тела). Один массив на всё приложение — по нему
        /// строятся и меню, и окно, и авто-назначение.
        /// </summary>
        public static readonly DycMaterialPreset[] All = BuildAll();

        static DycMaterialPreset[] BuildAll()
        {
            var derived = Dyc_MaterialForge.DerivedPresets();
            var all = new DycMaterialPreset[Base.Length + derived.Length];

            for (int i = 0; i < Base.Length; i++) all[i] = Base[i];
            for (int i = 0; i < derived.Length; i++) all[Base.Length + i] = derived[i];
            return all;
        }

        /// <summary>
        /// Помощник строки таблицы. Позиционные параметры повторяют прежний
        /// формат, поэтому старые строки не пришлось переписывать; ткань и
        /// мягкость добавлены в конец и нужны только генератору и подсказкам.
        /// </summary>
        static DycMaterialPreset P(string id, string display, string cat,
            float sf, float df, float bounce, float density, string notes = null,
            float requiredBounce = 0f, Dyc_TissueKind tissue = Dyc_TissueKind.None, float softness = 0f)
        {
            return new DycMaterialPreset
            {
                id = id,
                display = display,
                categoryKey = cat,
                staticFriction = sf,
                dynamicFriction = df,
                bounciness = bounce,
                density = density,
                notes = notes,
                requiredBounceThreshold = requiredBounce,
                tissue = tissue,
                softness = softness,
                derived = false
            };
        }
    }
}
