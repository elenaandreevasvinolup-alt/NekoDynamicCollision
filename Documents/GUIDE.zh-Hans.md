# Neko Dynamic Collision (NDC) — 部署与手册

为 Unity 提供烘焙式凸包碰撞。所有昂贵的计算都发生在编辑器中；
运行时不加载也不路由，只负责读取与分发。蒙皮角色或静态网格会变成
一组按骨骼、按区域划分的凸包，拥有 **零逐帧开销**、
内置的分区事件，以及用笔刷绘制的按区域物理材质。

组件本身不包含任何生成代码、特性，也不在运行时依赖插件的编辑器部分。
删除 `Editor/` 后运行时依然可用。

## 目录

- [第一部分 — 快速部署](#sec-partA)
  - [1. 烘焙你的第一个角色](#sec-1)
  - [2. 绘制材质区域](#sec-2)
  - [3. 十分钟上手路径](#sec-3)
- [第二部分 — 手册](#sec-partB)
  - [4. 核心概念](#sec-4)
  - [5. 安装与要求](#sec-5)
  - [6. 烘焙器窗口](#sec-6)
  - [7. 菜单参考](#sec-7)
  - [8. 精度](#sec-8)
  - [9. 笔刷](#sec-9)
  - [10. 材质组与预设](#sec-10)
  - [11. 覆盖率诊断](#sec-11)
  - [12. 碰撞健康检查](#sec-12)
  - [13. 事件与集成](#sec-13)
  - [14. Trigger 轮询](#sec-14)
  - [15. 质量、自碰撞与 LOD](#sec-15)
  - [16. 本地化](#sec-16)
  - [17. 目录结构](#sec-17)
  - [18. 卸载](#sec-18)
  - [19. 故障排查与常见问题](#sec-19)
  - [20. 联系方式](#sec-20)
- [附录 A. 物理材质预设](#sec-appA)
- [附录 B. 项目物理检查](#sec-appB)

---

<a id="sec-partA"></a>
# 第一部分 — 快速部署

<a id="sec-1"></a>
## 1. 烘焙你的第一个角色

1. 选中角色并添加 **Dynamic Collision** 组件
   （`Add Component → Neko → Dynamic Collision`，或使用 `GameObject` 菜单）。
   创建时组件会找到自身的 `SkinnedMeshRenderer`，并创建一个
   默认材质组。你无需填写任何内容。
2. 在检视面板中按下 **Bake**。
3. 凸包会以绑定姿态的线框形式出现在场景视图中。选中角色
   即可看到；Gizmo 默认跟随你的选择。

烘焙会在场景旁写入三类资源：

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

凸包网格是 `_Baked.asset` 的子资源，因此会随它一起移动，并在
播放模式与构建中保留下来。运行时不会重新计算任何东西。

<a id="sec-2"></a>
## 2. 绘制材质区域

笔刷正是让“一个对象、多种物理材质”成为可能的东西。

1. 打开烘焙器（`NekoWorks → NekoDynamicCollision → Open Main Window`）。
2. 进入 **Materials** 标签页，为每个区域添加一个组——例如
   `Hard` 和 `Soft`。为每个组选择一个预设。
3. 进入 **Paint** 标签页，选择要绘制的组，按下 **Start painting**。
4. 在场景视图中，拖拽经过你想要归入该组的那些面。被绘制的
   三角形会立即填充该组的颜色。
5. 重新烘焙。现在每个区域都会获得自己的凸包，并带有自己的物理材质。

只有在播放模式已停止、且动画窗口未在预览时才能绘制。姿态本身并不重要——
标签按三角形索引存储，
因此当前姿态与标签无关。

<a id="sec-3"></a>
## 3. 十分钟上手路径

| 分钟 | 做什么 |
|---|---|
| 0–2 | 添加组件，按下 Bake，查看 Gizmo。 |
| 2–4 | 打开 **Health** 标签页，修复所有红色项。 |
| 4–6 | 打开 **Bake** 标签页，查看覆盖率数字。低于约 95% 时，提高精度。 |
| 6–9 | 为每个区域添加一个材质组，绘制它，然后重新烘焙。 |
| 9–10 | 将交互层设为 `Bullet`，接入一个事件名，在播放模式中测试。 |

---

<a id="sec-partB"></a>
# 第二部分 — 手册

<a id="sec-4"></a>
## 4. 核心概念

### 是凸包，不是三角形汤

动态（非运动学）的 `Rigidbody` 不能使用非凸的 `MeshCollider`——
这是 PhysX 的限制，而不是 Unity 的。因此运动物体的每个碰撞形状
都必须是凸的。NDC 烘焙的是 **凸包**，并把凸包本身而不是原始
三角形子集交给 Unity，这正是顶点数永远不会触及 PhysX 上限
255 的原因。

### 凸包，还是真实表面

凸包是默认值，因为它是 PhysX 在运动物体上唯一接受的形状，但它有代价：
凸包必须把凹陷「封住」，所以在凹陷关节——腋下、腹股沟、脖子——相邻两根
骨头的凸包无法相接。那里会留一条缝，细小的命中会从缝里穿过去。这是凸性
的性质，不是烘焙的缺陷。

`碰撞体形状 → 非凸表面` 改为烘焙每根骨头的真实表面，不做凸包膨胀。关节
变成无缝的，和取消勾选 **Convex** 的系统碰撞体完全一样。代价来自
PhysX，健康检查会直说：只适用于运动学或动画驱动的主体、不支持网格对网格
碰撞、没有快速宽相位。它适合移动端做命中判定，不适合做物理阻挡。这个模式
记录在每个烘焙出的部件上，所以在这个选项出现之前烘焙的集合会保持全凸。

### 为什么骨骼刚性凸包已经足够

在绑定姿态下，`bone.localToWorldMatrix · bindposes[i] = I`。因此对某个
权重 100% 绑定到单根骨骼的顶点来说，蒙皮的结果恰好就是绑定姿态的网格。
换句话说：**在骨骼局部空间中烘焙的凸包，与逐帧重新烘焙的结果逐位相同**，
对于刚性加权的顶点而言。

只有混合权重的顶点——也就是跨越关节的那些——会有所不同。它们由相邻凸包
覆盖，而这些凸包按构造本就互相重叠。这就是 NDC 能在运行时零开销、
同时在关键之处依然准确的原因。

### 空间聚类，而不是按索引顺序切块

NDC 按位置对三角形分组（最远点播种，加上沿边邻接的 Dijkstra 式生长）。
另一种做法——按索引顺序取三角形——会产生互相重叠、包裹空气的凸包，
而且你要求的凸包越多，结果越糟。

### 分区与材质组

两条相互独立的轴：

- **分区** —— *在哪里*。一根骨骼（Skin 模式）或整个网格（Mesh 模式）。
  子分区总是优先于祖先，因此事件绝不会被派发两次。
- **材质组** —— *是什么*。一组共享同一物理材质和同一
  密度的面，通过绘制创建。

凸包是一个分区与一个材质组的交集。如果某个组在给定骨骼内没有
被绘制的面，就不会为这一组合生成凸包。

### 运行时做什么

1. 加载已烘焙的数据集。
2. 在正确的骨骼下为每个凸包创建一个隐藏的子对象，使用单位
   变换，并赋予一个凸的 `MeshCollider`。
3. 构建 `Collider → hull` 查找表。
4. 在每个拥有凸包的 `Rigidbody` 上放置一个 `Dyc_Relay`。
5. 配置层、自碰撞与质量。

然后就结束了。除了可选的 Trigger 轮询和用于 LOD 的距离检查之外，
没有任何 `Update` 工作。

<a id="sec-5"></a>
## 5. 安装与要求

- Unity 2022.3 或更新版本。
- 将 `Assets/NekoDynamicCollision` 复制到你的项目中。没有任何需要
  配置的地方；程序集通过 assembly definition 划定作用域。
- 两个程序集：
  - `Neko.DynamicCollision.Runtime` —— 组件、Relay、Trigger 轮询、事件
    结构体以及集成门面。从不引用 `UnityEditor`。
  - `Neko.DynamicCollision.Editor` —— 烘焙器、凸包数学、聚类、笔刷、
    Gizmo、健康检查、预设以及窗口。仅限编辑器平台。

<a id="sec-6"></a>
## 6. 烘焙器窗口

`NekoWorks → NekoDynamicCollision → Open Main Window`（`Cmd/Ctrl+Shift+D`）。

| 标签页 | 功能 |
|---|---|
| **Bake** | 源、模式、精度、烘焙/清除/重建、统计、覆盖率 |
| **Paint** | 组列表（含已绘制三角形数量）、笔刷设置、姿态重置 |
| **Gizmo** | 场景视图绘制什么、如何绘制 |
| **Parts** | 分区列表——骨骼、包含子级、事件名、伤害倍率 |
| **Materials** | 材质组、预设、配对行为表、项目物理审计 |
| **Health** | 满分 100 的评分、每个问题、一键修复 |
| **Settings** | 语言、诊断、打开烘焙文件夹、重置偏好设置 |

**Bake** 标签页承载来源、碰撞体形状、精度和烘焙操作。**排除项**在组件的
Advanced 折叠区里，它们是三件不同的事：

- **排除的骨骼**——列出的骨头**及其整棵子树**都不生成分区和碰撞体。IK 目标、
  骨头末端和辅助骨骼就该放这里。
- **排除的渲染器**——完全不参与烘焙的 `SkinnedMeshRenderer`：头发、单独的
  布料渲染器、道具。
- 材质组上的 **从烘焙中排除**——该组的面完全不进烘焙，所以披风或背带不需要
  改材质就能不生成判定盒。

专家窗口（`逐骨骼精度`）顶部有一个默认折叠的区块，放着不常改的决定：碰撞体
形状、自碰撞模式和 LOD。

<a id="sec-7"></a>
## 7. 菜单参考

一切都放在同一个顶级菜单槽下，这样安装更多 NekoWorks 插件时，
菜单栏永远不会变宽。

```
NekoWorks
└── NekoDynamicCollision
    ├── Window
    │   └── Open Main Window              Cmd/Ctrl+Shift+D
    ├── Bake
    │   ├── Bake Selected                 Cmd/Ctrl+Shift+B
    │   ├── Rebuild Selected
    │   └── Bake Contact Map
    ├── Gizmo
    │   ├── Toggle Hull Gizmo
    │   └── Highlight Uncovered Faces
    ├── Tools
    │   ├── Bone Precision (Expert)
    │   ├── Material Forge
    │   ├── Mirror RTL Interface
    │   └── Refresh Component Icon
    ├── Diagnostics
    │   └── Collision Health
    └── Help
        └── About Neko Dynamic Collision
```

菜单标题在加载时以及语言变化时本地化；如果你所使用的 Unity 版本
无法使用 Unity 内部的菜单 API，
特性中的静态英文字符串就是回退方案。

<a id="sec-8"></a>
## 8. 精度

一个旋钮，四个档位。内部会展开为四个值：

| 精度 | 每个凸包的三角形数 | 每个分区的凸包数 | 接缝外扩 | 权重阈值 |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **每个凸包的三角形数** 限制一个凸包可以吸收多少几何体。每个凸包的三角形越多，
  凸包就越少、越大、越松。
- **每个分区的凸包数** 是每个分区的目标聚类数量。聚类越多，
  贴合越紧，碰撞体也越多。
- **接缝外扩** 会把每个凸包向外膨胀，使相邻区域相互重叠
  而不是留下缝隙。重叠是安全的（命中绝不会被漏掉，且同帧
  去重会阻止重复事件）；而缝隙不安全。
- **权重阈值** 会丢弃对主导骨骼权重低于该值的顶点。
  更高的值意味着更紧、更“刚性正确”的凸包。

一个凸包的顶点数永远不会超过该聚类的唯一顶点数，而唯一顶点数
上限为 250——安全地低于 PhysX 的 255 上限。健康检查会把任何
超过 255 的凸包标红。

### 8.1 凸包模式做分解——超越系统复杂碰撞体

在**凸包**模式下，凹网格会沿凹陷切成多个凸块。这和系统自带的复杂碰撞体是同一个思路：任何网格都能出凸块。NDC 保留广度，同时把上限抬高：

| | 系统复杂碰撞体 | NDC |
|---|---|---|
| 任何网格 | 是 | 是 |
| 优化好 | 是 | 是——后台烘焙任务、进度、取消、逐骨骼预算 |
| 部件不跨关节 | **否**——纯几何，肩部会吞掉手臂根部 | **是**——部件带权重场算出的骨骼标签 |
| 能否用在运动刚体上 | **否**——PhysX 拒绝非凸碰撞体挂在非运动学 `Rigidbody` 上 | **是**——输出是凸包 |
| 运行时开销 | 加载时烹饪碰撞体 | 零——烘成资产 |
| 兜底 | — | 空间聚类，退化网格照样有碰撞体 |

专家窗口会报告部件来自**分解**（沿凹陷切）还是**空间聚类**（兜底），所以差别是一个数字，不是猜测。

**切得多细**由一个滑条控制——**分解精度**——紧挨着下方的数字框显示体素毫米数。两者是*同一个数字*的两种视图，所以永远一致：拖滑条数字框跟着变，改数字框滑条跟着动。不存在需要同步的第二处设置。

- **最左**——体素最粗：部件更少更大，开销最低，通常道具够用。
- **最右**——体素最细：部件贴合表面，因此开销**对齐非凸模式**：再细也换不来精度，只会更贵。

上面的精度表管的是*聚类拟合*——每块贴得多紧——而不是"你会得到几个碰撞体"。

同一个滑条在两种模式下都出现：凸分解和非凸简化是回答同一个问题的两种方式——*我到底要多细*。

### 8.2 非凸精度就是一个滑条

把 **Collider shape → Non-convex surface** 切过去，就会出现 **Surface detail** 滑条。

| 滑条 | 结果 |
|---|---|
| 最左 | 大幅简化，三角形很少，能看出棱面 |
| 中间 | 不错的折中：形状读得出来，碰撞体也不贵 |
| **最右** | **完全不简化**——表面直接取自网格 |

为什么是滑条而不是"每块多少三角形"：不先知道网格有多少三角形，这个数根本没法选得合理——500 对躯干是粗糙，对手指是精确。滑条只回答用户真正能回答的问题：*我有多在意精确形状*。最右端不是"几乎精确"，而是精确：简化被整个关掉。

不管滑条在哪，小簇从不简化——把细手指拉到粗糙网格上会把它塌成"什么都没有"，而空碰撞体比昂贵的碰撞体更糟。

蒙皮模式和 mesh 模式共用同一个滑条。

<a id="sec-9"></a>
## 9. 笔刷

笔刷并不“排除”面。它给面 **打标签**，而标签驱动烘焙。

| 操作 | 效果 |
|---|---|
| 左键拖拽 | 将当前组赋予光标下的三角形 |
| Shift + 拖拽 | 擦除回组 0，并清除已绘制标记 |
| 鼠标滚轮 | 笔刷半径 |
| `X` 切换（窗口） | 将每一笔沿对象局部 X = 0 镜像 |

设置：半径、X-Ray（忽略背面的三角形）、沿 X 镜像。

要求会以明确的消息强制执行，而不是静默失败：

1. 播放模式必须已停止。
2. 动画窗口必须不在预览。

骨架**不**需要处于绑定姿态。笔刷在网格的**当前**姿态下进行射线检测；
由于蒙皮从不改变拓扑，三角形索引一一对应，
因此标签保持正确。

如果你还是希望骨架处于绑定姿态，**Reset to bind pose** 按钮会由
`bindposes[i].inverse` 解出局部变换并写回，且支持撤销。

<a id="sec-10"></a>
## 10. 材质组与预设

每个组都带有一个 `PhysicMaterial`、一个以 kg/m³ 为单位的密度、一个可选的事件名
以及一个伤害倍率。

**预设。** 14 个类别共 224 个预设（Metal、Ceramic、Plastic、Glass、Wood、
Stone & Concrete、Rubber、Fabric & Leather、Body parts、Tissue & organs、Organic、
Ice、Food、Other）——参见 [附录 A](#appendix-a-physic-material-presets)。
应用预设会在 `Baked/<Scene>/Materials/` 下创建一个真实的 `.physicMaterial` 资源，
因此它可以被引用、做差异对比、放进 Addressables，并交给美术。

**材质锻造器（Material Forge）。** 表格里的一行回答的是“它由什么构成”，
而游戏通常问的是另一个问题：“它**现在**是什么状态”。
干燥的钢、湿的钢、生锈的钢、沾血的钢是四种完全不同的手感；
如果把它们都做成独立的行，就意味着 224 × 15 ≈ 3400 行，没人维护得动。
所以锻造器把基础预设乘以一个**表面状态**：

| | 状态 |
|---|---|
| 乘数 | Dry、Wet、Oiled、Bloody、Sweaty、Icy、Frozen、Dusty、Rough、Polished、Rusted、Worn、Charred |
| 覆盖层 | Clothed（更轻、更抓地）、Armoured（更重、更光滑） |

摩擦会对静摩擦和动摩擦两个系数同时乘——否则“湿”表面在起步时仍然是黏的。
`Dry` 是恒等变换，原样返回预设，所以永远不会出现 `steel_dry` 和 `steel` 并存。

**人体部位是推导出来的，不是手写的。** `Body parts` 与 `Tissue & organs`
根本不是表格里的行，而是由**组织配比**算出来的：密度按权重相加，柔软度按权重相加
再加上“软垫”项，摩擦与弹性则由柔软度推导——软组织更抓地、更不弹，硬组织相反。
“Breast”是 80% 脂肪 + 10% 肌肉 + 10% 皮肤，且软垫很厚；“Skull”是 95% 骨 +
5% 皮肤，且没有软垫。这就是为什么这些数字可以被解释，而不是硬塞进去的。

**生成资源。** *Generate variant assets* 会把每个状态各写成一个 `.physicMaterial`
到 `Baked/<Scene>/Materials/`，美术或策划可以直接从 Project 窗口取用，不必打开插件。

**混合策略。** 整个库对摩擦使用 `Multiply`，对弹性使用 `Maximum`。
Unity 的混合优先级为 `Average < Minimum < Multiply < Maximum`，
因此在这种策略下，任何光滑表面都会主导摩擦结果，
任何有弹性的材质都会主导弹性结果——
这正是人们直觉上所期望的。

**配对行为表。** Materials 标签页会按 Unity 真实的优先级规则解析你项目中的
每一对组，显示实际会生效的值，
并给出一句大白话结论（“抓地 / 不弹”）。这是回答
“我的冰为什么不滑”最快的方式。

**按名称自动分配。** 通过将组名与英语、中文和俄语关键词匹配，
从预设填充每个组，包含人体部位——名为 “chest” 的组会变成胸部组织，
而不是笼统的 “flesh”。

**诚实的局限。** 一个 `PhysicMaterial` 只有四个数字和两种混合模式。它
无法表达滚动摩擦、各向异性摩擦（丝绒）、黏度、塑性变形、温度或磨损。
柔软度是生成器的**输入**，不是隐藏属性：它从不写进资源里，
因为根本没有地方存它。
这里的“真实世界参数”指的是有据可查的查找表和可用的预设——
而不是物理模拟。

<a id="sec-11"></a>
## 11. 覆盖率诊断

Bake 标签页回答了一个通常只能靠猜的问题：**哪些三角形完全没有
凸包覆盖？**

它会取绑定姿态下的源网格，用每个三角形质心对每个
凸包的平面做测试，并报告：

- 总体百分比和进度条；
- 按分区的细分；
- 未覆盖三角形的列表，可在场景视图中以红色绘制
  （**Show uncovered faces**）。

把低于约 95% 视为问题：提高精度，或者检查
分区骨骼是否真的覆盖了整个骨架。

<a id="sec-12"></a>
## 12. 碰撞健康检查

满分 100 的评分，列出每个问题，并在可能时提供一键修复。

检查内容包括：尚未烘焙；凸包超过 PhysX 顶点上限；凸包接近
上限；退化的聚类；不属于任何分区的三角形；源网格
自上次烘焙后发生变化；没有材质、没有凸包或碎片化凸包过多的材质组；
有 Hitbox/Trigger 角色但没有交互层；父级链中
没有 `Rigidbody`；刚体质量过小或过大；布娃娃自碰撞
完全开启；派发的事件没有监听者；以及
[附录 B](#appendix-b-project-physics-checks) 中的项目级物理检查。

<a id="sec-13"></a>
## 13. 事件与集成

每个事件都携带完整上下文，因此你永远不必再去查找任何东西：

```csharp
public struct DycEvent
{
    public DycEventKind kind;          // CollisionEnter/Exit, TriggerEnter/Exit
    public int elementIndex;
    public string elementName;
    public Transform bone;             // the bone the hull is attached to
    public int groupIndex;
    public string groupName;
    public PhysicMaterial material;
    public Collider selfCollider;      // which of your hulls
    public Collider otherCollider;
    public Rigidbody otherBody;
    public GameObject otherRoot;
    public Vector3 point;
    public Vector3 normal;
    public float relativeSpeed;
    public float damageMultiplier;     // element × group
    public string eventName;
    public float time;
}
```

三种使用方式：

1. **UnityEvent** —— 组件上的 `onEvent`，供代码注册的监听者使用。
2. **字符串注册表** —— 给某个分区或组一个事件名，并用
   `Dyc_Events.Register("Hit.Head", handler)` 监听。拼错的名字不会报错，但
   健康检查会报告那些无人接收的派发。
3. **静态门面** —— 供外部工具使用的 `Dyc_Api`：

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

伤害倍率位于分区和组上，并会相乘——头部 ×4 就是
一个数字，而不是一层胶水代码。

**去重。** 接缝外扩意味着两个相邻的组可能在同一帧都碰到同一个
外来碰撞体。NDC 每帧对每个 `(element, other collider)` 最多派发
一个事件，因此分区材质不会产生重复事件。

<a id="sec-14"></a>
## 14. Trigger 轮询

Unity 是**按刚体对**投递 Trigger 回调的，因此单个布娃娃就是
一个刚体对，物理层根本无法告诉你
哪根骨骼进入了某个体积。给每根骨骼各自配一个 `Rigidbody` 又会摧毁零开销的承诺。

因此分区 Trigger 采用采样方式：

- 每个分区都会用 `Physics.OverlapBoxNonAlloc` 对其碰撞体的世界空间
  包围盒做测试。
- 只考虑真正的 Trigger，且绝不包含你自己的碰撞体。
- 分区按切片处理：每帧 `elements / frames-per-pass` 个。
- 进入和退出会针对每个分区与上一轮做差异比较。

**要记住的语义：** 这是采样，不是事件。一次非常快的经过可能
被漏掉。提高采样率，或者使用 **sweep margin** 来扩大查询盒。

<a id="sec-15"></a>
## 15. 质量、自碰撞与 LOD

**由密度得到质量。** NDC 知道每个凸包的体积，因此可以正确地计算质量：
`mass = hull volume × group density`，可选地做归一化，使整个角色
匹配一个目标总质量。这消除了 Unity 布娃娃中最古老的手工调参工作。
单个刚体会接收它所拥有的所有凸包体积之和。

**自碰撞。** `Ignore`（所有配对）、`Adjacent`（同一分区，或祖先与
后代）或 `On`。布娃娃骨骼互相碰撞是抖动的常见来源，
而 `Adjacent` 通常就是答案。碰撞体超过 200 个时，这一步会被跳过
并给出警告，而不是阻塞 `Awake`。

**LOD。** `Disable` 会在超过一定距离后关闭碰撞体；`Reduce` 只保留
每个分区最大的凸包。该检查每四帧运行一次。

**Rigidbody。** Unity 只向拥有 `Rigidbody` 的 GameObject 投递碰撞回调。
对布娃娃来说，每根骨骼都已经有一个。对其他任何东西，启用
**Auto-add Rigidbody**，NDC 就会在组件所在对象上创建一个运动学刚体。

<a id="sec-16"></a>
## 16. 本地化

窗口、检视面板、健康检查消息和菜单标题都本地化为
**15 种语言**：

`en`（内置）· `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- 英语内置于程序集中，是任何缺失键的回退方案，因此
  部分翻译的语言会退化，而不是崩溃。
- 其他所有语言都是 `Locale/<code>/strings.json` 中的纯数据——添加一种
  无需重新编译。
- 阿拉伯语和希伯来语完全从右到左：布局会镜像，而不是依赖
  `style.direction`，UI Toolkit 对它的支持不完整且因版本而异。
- 在 **Settings** 标签页中更改语言。窗口和菜单会
  立即更新，无需域重载。
- Settings 标签页还会显示解析后的语言文件路径以及找到了多少种语言，
  这样打包错误就可见，而不是悄无声息。

<a id="sec-17"></a>
## 17. 目录结构

```
Assets/NekoDynamicCollision/
├── Runtime/                  runtime assembly (no UnityEditor references)
│   ├── Dyc_DynamicCollision.cs    the component
│   ├── Dyc_BakedSet.cs            baked data asset
│   ├── Dyc_PaintMask.cs           brush labels
│   ├── Dyc_Types.cs               enums, elements, groups, event struct
│   ├── Dyc_Events.cs              string registry
│   ├── Dyc_Relay.cs               collision forwarding
│   ├── Dyc_TriggerPoll.cs         partitioned trigger sampling
│   └── Dyc_Api.cs                 integration facade
├── Editor/                   editor-only assembly
│   ├── Core/                      baker, hull maths, clustering, health, presets
│   └── UI/                        window, inspector, brush, gizmo, icon, style
├── Locale/<code>/strings.json     14 language packs (+ built-in English)
├── Documents/GUIDE.<code>.md      15 manuals
├── Baked/<Scene>/                 bake output (generated)
├── README.md
└── package.json
```

<a id="sec-18"></a>
## 18. 卸载

1. 从你的预制体和场景中移除 **Dynamic Collision** 组件。
2. 删除 `Assets/NekoDynamicCollision`。

烘焙资源位于插件文件夹内的 `Baked/` 下，并随之一起被移除。
不会在插件文件夹之外写入任何东西，
运行时也不包含任何依赖编辑器部分的代码。

<a id="sec-19"></a>
## 19. 故障排查与常见问题

**什么都碰撞不了，也没有事件触发。**
父级链中没有 `Rigidbody`。Unity 只向拥有刚体的对象发送碰撞回调。
启用 **Auto-add Rigidbody**，或者自己添加一个。

**凸包和我看到的不一致。**
Gizmo 默认绘制 **绑定姿态**——那正是被烘焙的东西。在 Gizmo 标签页中
关闭 **Bind pose**，即可在当前姿态下查看它们。

**“凸包有 N 个顶点，超过 PhysX 的 255 上限。”**
Unity 会静默忽略超限的凸包。把精度降低一档；
健康检查正好提供这一键修复。

**某些地方会漏掉命中。**
首先检查覆盖率百分比。低于约 95% 意味着存在真实的空洞。然后检查
你当前所处精度档位的 **接缝外扩**。

**一笔绘制在两个区域之间留下了缝隙。**
这就是接缝问题。提高精度（这会降低接缝外扩），或者
稍微画过边界一点。同帧去重已经能从重叠中
防止重复事件。

**一次命中触发了两次事件。**
同一帧内命中了两个不同的分区，这是合法的。如果你确实
想要每个对象对一个事件，就在处理函数中按 `elementIndex` 过滤。

**我绘制了，但烘焙后什么都没变。**
当三角形数量与源网格不匹配时，标签会被忽略——
通常发生在重新导入或拓扑变化之后。重新绘制，或者先烘焙，
这样标签资源就会以正确的大小创建。

**笔刷无法启动。**
播放模式正在运行，或者动画窗口正在预览。两者都会在 Paint 标签页中
作为明确原因显示出来。

**更新后我以前的笔刷工作消失了。**
不应该如此：在已绘制标记出现之前创建的遮罩会被迁移，任何
非零标签都会被视作已绘制。如果某个遮罩被清除了，请重新绘制并重新烘焙。

**运行时开销真的是零吗？**
在稳定状态下，是的：凸包是资源，变换由层级跟随，
完全没有网格计算。唯一的逐帧工作是可选的 Trigger 轮询
和 LOD 距离检查。

**我可以在一个对象上放两个 Dynamic Collision 组件吗？**
不可以，而且这是有意阻止的。两个组件会在同一批面上创建重复的凸包，
使接触和事件都翻倍。
请改用分区和材质组来划分。

<a id="sec-20"></a>
## 20. 联系方式

NekoAndreeva —— 仓库 URL 见 `package.json`。

---

<a id="sec-appA"></a>
## 21. 普通用法与技术面

### 21.1 把 NDC 当系统碰撞体用

设计准则是：会写 `Collider` 和 `Rigidbody` 的人就已经会写 NDC——因为 NDC 生成的**就是**普通碰撞体：隐藏子物体上的 `MeshCollider`、物体上的 `Rigidbody`、标准消息、普通层级与物理材质。`Physics.Raycast` 和 `Physics.OverlapSphere` 完全不用改。

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // 添加 + 构建
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // 标准 Unity 消息
void OnTriggerStay(Collider other) { }     // 标准 Unity 消息
```

| 调用 | 含义 |
|---|---|
| `Find(go)` | 取组件（自身或父级） |
| `Attach(go, generateNow)` | 添加组件并构建 |
| `Build(go)` / `Rebuild(go)` | 用烘焙集构建，没有就运行时生成 |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | 状态，一次作用于全部外壳 |
| `SetTrigger(go, v)` | 对全部外壳设置 `Collider.isTrigger` |
| `SetMaterial(go, pm)` | 立即生效；不跨重建保留 |
| `GetColliders(go)` / `ForEachCollider(go, a)` | 取回外壳（普通 `Collider`） |
| `SetReceiver(go, t)` | 把标准消息也发给 `t` |

**只有分区是额外的。** 分区、笔刷材质、LOD、事件、按密度算质量、健康体检才需要 NDC 的 API——这些正是系统碰撞体做不到的事。

**不需要烘焙。** 勾选 **Advanced ▸ Build at startup when nothing is baked**（或调用 `Attach`），`Awake` 时就会按骨骼从网格生成碰撞体——每骨一个凸包，和 系统碰撞体 默认一致。烘焙仍然是获得分区、分解、覆盖率与精度的途径。

**消息怎么到你的脚本。** Unity 把 `OnCollision*` 发给带 `Rigidbody` 的物体。如果你的脚本在别处（脚本在角色根、Rigidbody 在某根骨头上），就设置 `Advanced ▸ Also send OnCollision*/OnTrigger* to`——消息会用 `SendMessage` 转发，没有碰撞的帧不花代价。

### 21.2 实时更新——烘焙无法替代的那一项

绑在骨头上的烘焙外壳在绑定姿势下是精确的，之后就是刚性的。强形变时（下蹲、肢体被挤压、布料绷紧）它会报错表面。实时更新会用**当前**蒙皮姿势重建外壳。

用 **Advanced ▸ Live update** 或 `Dyc_Collision.EnableLiveUpdate(go)` 打开。

| 设置 | 默认 | 含义 |
|---|---|---|
| `liveUpdate` | 关 | 按当前姿势重建外壳 |
| `liveUpdateContinuous` | 开 | 持续更新，或按需跑一遍 |
| `idleCpuBudgetMs` | 0.2 | 几乎不动时的预算 |
| `activeCpuBudgetMs` | 1.0 | 快速运动时的预算 |
| `meshUpdateThreshold` | 0.02 | 位移低于此值（米）就跳过 |
| `maxColliderTriangles` | 5000 | 单碰撞体上限，防止一根重骨吃掉预算 |

预算按网格实际位移选择：站着按便宜档算，跑动按贵档算。一帧放不下的工作顺延到下一帧，`OnUpdateYield` / `OnPassComplete` 会报告耗时毫秒。

**它不会每帧重建全部。** 三个机制让开销可预测：

1. **增量。** 每个簇的簇心与上一遍比较，只重建真的动过的簇。挂在锚点上的软体，下摆一直在抖、中间几乎不动——中间不花任何代价。
2. **按优先级排序。** 队列按每个簇的位移量排序。预算不够时，不够的是最平静的那些簇——也就是误差最看不出来的那些。没有这个排序，预算会花在列表里恰好排前面的簇上。
3. **按时钟而不是按簇数限预算**：每帧开销不随身体的簇数增长。

`LastDirtyCount` 和 `LastBuiltCount` 会报告上一遍实际做了什么——这是查看节省效果最诚实的方式。

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"一遍耗时 {ms:F2} ms");
live.StopAfterPass();          // 跑完当前这遍再停
live.UpdateNow();              // 立即完整跑一遍（不计预算）
```

**前提。** 外壳需要 `sourceVertices`，由烘焙写入；旧角色要重新烘焙才能开实时更新。运行时开销是真实存在的——这是唯一与"每帧零开销"相矛盾的功能，所以默认关闭。

### 21.3 逐骨骼覆盖

`Dyc_BoneProperties` 挂在骨头本身上（Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties）：

| 字段 | 作用 |
|---|---|
| `overrideMaterial` + `physicsMaterial` | 这根骨头的外壳用该材质 |
| `overrideConvex` + `convex` | 这根骨头用凸包而不是表面（或反之） |
| `overrideWeightThreshold` + `boneWeightThreshold` | 逐骨骼权重阈值 |
| `exclude` | 这根骨头不生成碰撞体 |

挂在骨头上意味着它能扛住改名——它存的是引用，不是路径。

### 21.4 按源材质指定材质

`Advanced ▸ Materials by source material` 把源 `Material` 映射到 `PhysicMaterial`。外壳按其主要来源的 submesh 归属，在烘焙时解析进 `Dyc_BakedSet.sourceMaterials`。优先级从高到低：

1. `Dyc_BoneProperties.physicsMaterial`；
2. 外壳源材质对应的关联材质；
3. 笔刷分组的材质。

### 21.5 顶点排除贴图

`Advanced ▸ Exclusion map` 读取纹理的某个通道（R/G/B/A，带阈值），通道值达到阈值的顶点被排除。笔刷标的是**面**，贴图标的是**顶点**——两者互补。网格需要 UV，纹理需要开启 **Read/Write Enabled**，且只有三个顶点都被排除时三角形才被排除。

### 21.6 重定向骨架

`Advanced ▸ Attach hulls to another skeleton` 用本网格构建外壳，但挂到另一个根的同名骨头上——即 `RetargetSkeleton` 场景，适用于 Puppet Master 一类方案。骨骼按相对路径解析；找不到同名骨的会留在自己的骨架上，烘焙报告会说明有多少个。

### 21.7 软体模式——无骨骼，由代码或求解器驱动

软体模式（`Mode → Soft`）**不是**蒙皮骨架。它面向**没有骨骼**、形状由求解器或代码产生的网格——即 NekoDynamicSoftbody 一类。软体模式完全不读骨骼，几何就是网格本身。

**烘焙产出什么。** 网格被切成带编号的空间簇。每个外壳**相对于自己的簇心**构建，簇心作为该簇的姿势存进 `clusterRest`。正因如此，帧才能把外壳整体平移**并旋转**。

**没有求解器时。** 帧创建在各自的姿势上，外壳精确落在真实网格几何上并随物体移动。形状是对的，只是没有动力学。这是有意的降级而不是失败——也就是"没有 NDSC 时算真实形状"的含义。

**有求解器时。** 求解器推送帧（`Push` → `Apply`）并完全接管，给出完整模拟。地址式推送不会在同一帧被全局轮询覆盖——只要有多个体就会体现出来。

**没有骨骼时实时更新同样可用。** `Dyc_LiveUpdate` 直接读取 `MeshFilter` 的 CPU 顶点，所以任何在代码里形变网格的东西——软体求解器、程序化脚本、自定义形变器——都能驱动精确外壳，不需要任何针对插件的胶水代码。外壳需要 `sourceVertices`，由烘焙写入；旧资产要重烤。

**诚实的边界：** 只存在于 GPU 的形变（顶点着色器、GPU 蒙皮）无法在 CPU 侧读回，实时更新看不到它。要么把形变放到 CPU，要么保留烘焙外壳。

### 21.8 单独驱动某几个外壳

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`、`HullIndexOf`、`HullCount`、`BakedSet`、`Elements`、`Groups` 都是公开的，外部工具不需要反射就能遍历和驱动单个外壳。

---

### 21.9 角色碰撞 LOD

这个 LOD 是给**角色**的，不是给场景的。普通碰撞 LOD 处理静态道具；而一个角色本身就是上百个外壳，手机上是**一群角色**——不是关卡——决定帧预算。

用 **Expert settings ▸ Character collision LOD** 打开，或设置 `Advanced.collisionLod`。

| 设置 | 默认 | 含义 |
|---|---|---|
| `collisionLod` | 关 | 按距离熄灭部分外壳 |
| `lodNearDistance` | 12 | 此距离内完整细节（米） |
| `lodFarDistance` | 40 | 超过此距离只保留最大的一些 |
| `lodFarHullCount` | 6 | 远档保留多少个外壳 |
| `lodHysteresis` | 1.5 | 迟滞（米），避免边界上的角色反复闪烁 |
| `lodRequireVisibility` | 关 | 同时要求在屏幕上可见 |
| `lodReference` | 空 | 距离参照；留空表示用当前活动摄像机 |

远档保留的是**体积最大**的那些外壳，按体积在构建时排序一次。远处角色仍然能被躯干命中，但不再为手指付费。熄灭一个外壳就是 `Collider.enabled` 标志——不重建、不重烤。

**摄像机会被自动找到。** 不只是 `Camera.main`：它被 MainCamera 标签锁死，而真实项目里画的是当前启用的那台——切视角、第三人称、车内视角。NDC 遍历所有启用的摄像机，取 **depth 最大**的那台，也就是玩家真正看到的那台。一台都没有时它会警告一次，并让 LOD 停在近档，而不是静默失效。

**自己决定要不要简化。** 实现这个接口，NDC 就不再自己量距离，而是来问你：

```csharp
public interface IDyc_LodProvider
{
    bool TryGetDistance(Transform target, out float distance);
}

Dyc_CollisionLod.Provider = myProvider;
Dyc_CollisionLod.Simplified += (target, far) => { /* ... */ };
```

优先级：**外部 Provider → 显式参照 → 当前活动摄像机。**

NDC 刻意**没有场景管理器**。场景优化器需要它，是因为它们处理整个场景；NDC 只处理一个对象，为它单独造一个场景级对象只是多一个要维护的实体。接口留给想接管这个决定的人。

### 21.10 范围与许可

**NDC 刻意不做的事。** 这些是顶端功能；做了就等于放弃轻量化定位，所以是主动不要，而不是漏做：

- GPU 蒙皮碰撞——只存在于顶点着色器里的碰撞无法在 CPU 侧读回，实时更新看不到它
- 逐顶点布料级碰撞
- 求解器级自碰撞精度
- 多线程求解

**运行时占用。** 运行时程序集**零依赖**——不需要 Burst、不需要 Jobs、不含原生库。所有重代码都在 Editor-only 的程序集定义里，永远不会进玩家构建。内置的分解库位于 `Editor/` 下，只由编辑器加载。

**本地化。** 随包提供 14 种语言，每份都带完整键集，阿拉伯语与希伯来语含完整从右到左排版。

**许可。** MIT——见 `LICENSE`。随包分发的第三方组件列在 `THIRD PARTY NOTICES.md`，对这些组件以该文件为准。


---

## 附录 A. 物理材质预设

224 个预设，14 个类别。数值来自有据可查的工程近似值，
映射到 Unity 的四参数模型上。`Body parts` 与 `Tissue & organs`
由锻造器从组织配比推导，而不是手写进表格。

| 类别 | 数量 | 预设 |
|---|---|---|
| Metal | 26 | Steel, Stainless, Cast Iron, Aluminium, Cast Aluminium, Anodised, Copper, Brass, Bronze, Titanium, Lead, Tungsten, Chrome, Nickel, Zinc, Magnesium, Gold, Silver, Platinum, Galvanised, Rusted Steel, Gun Steel, Tool Steel, Armour Steel, Sheet Metal, Rebar |
| Ceramic | 10 | Porcelain, Bone China, Ceramic Tile, Terracotta, Alumina, Silicon Carbide, Zirconia, Glass-Ceramic, Enamel, Unfired Clay |
| Plastic | 18 | ABS, PVC, Nylon, Acrylic, Polycarbonate, HDPE, PTFE, PEEK, POM, PET, Polypropylene, Polystyrene, TPU, PLA, Bakelite, Melamine, Vinyl, Rigid Urethane |
| Glass | 9 | Glass, Tempered, Laminated, Frosted, Thick, Armoured, Mirror, Crystal, Borosilicate |
| Wood | 16 | Oak, Pine, Plywood, Cork, Birch, Maple, Walnut, Teak, Mahogany, Balsa, MDF, Particleboard, Bamboo, Wet Wood, Charred Wood, Round Timber |
| Stone & Concrete | 15 | Concrete, Wet Concrete, Brick, Asphalt, Granite, Marble, Polished Marble, Limestone, Sandstone, Slate, Basalt, Cobblestone, Gravel, Rubble, Sand |
| Rubber | 12 | Rubber, Tire, SBR, Latex, EPDM, Butyl, Neoprene, Silicone, Hose Rubber, Rubber Mat, Rubber Foam, Shock Gel |
| Fabric & Leather | 25 | Leather, Canvas, Carpet, Kevlar, Body Armour, Vest Shell, Cotton, Denim, Silk, Satin, Wool, Linen, Burlap, Velvet, Felt, Nomex, Spandex, Gore-Tex, Parachute, Nylon, Suit Fabric, Upholstery, Heavy Tarp, Blanket, Towel |
| Body parts | 39 | Head, Skull, Scalp, Hair, Face, Cheek, Lip, Jaw, Nose, Ear, Eyeball, Tooth, Tongue, Neck, **Chest**, Breast, Abdomen, Groin, Back, Hip, Pelvis, Shoulder, Bicep, Elbow, Forearm, Wrist, Hand, Palm, Fist, Knuckle, Nail, Glute, Thigh, Knee, Calf, Shin, Ankle, Foot, Sole |
| Tissue & organs | 10 | Bone, Cartilage, Tendon, Muscle, Fat, Skin, Organ, Lung, Brain, Keratin |
| Organic | 3 | Flesh, Bone, Ballistic Gel |
| Ice, snow & mud | 10 | Ice, Wet Ice, Black Ice, Snow, Powder Snow, Packed Snow, Slush, Hail, Frozen Ground, Mud |
| Food | 9 | Bread, Fruit, Vegetable, Raw Meat, Cooked Meat, Fish, Cheese, Chocolate, Ice Cream |
| Other | 22 | Cardboard, Wet Cardboard, Paper, Foam, Sponge, Bubble Wrap, Drywall, Tarp, Sandbag, Dirt, Grass, Hay, Leaf Litter, Trash Bag, Mineral Wool, Rope, Net, Coal, Ash, Rock Salt, Sugar, Wax |

每个预设还带有一个以 kg/m³ 为单位的**密度**，用于自动质量；
橡胶族预设则带有它们实现弹跳所必需的 `bounceThreshold`。

这 224 个预设都可以再乘以 14 种非恒等的表面状态，
所以锻造器在不增加任何表格行的前提下，覆盖大约 3100 种可用材质。

<a id="sec-appB"></a>
## 附录 B. 项目物理检查

健康检查会审计项目的 `Physics` 设置，因为材质预设
无法修正全局设置：

| 设置 | 为什么重要 |
|---|---|
| `bounceThreshold` | 低于该速度的碰撞永远不会弹跳。在 Unity 默认值 2 下，橡胶预设看起来像坏的。降至 0.2–0.5 才能使用弹性材质。 |
| `defaultSolverVelocityIterations` | 为 1 时，堆叠和快速碰撞会抖动或穿透。2–4 通常更好，它也是布娃娃抖动的常见根因。 |
| `gravity` | 如果不是 −9.81，所有基于 −9.81 得出的质量与冲量直觉都会按同一系数偏移，密度预设也需要修正。 |
| `defaultContactOffset` | 过宽的接触间隙会让薄物体看起来像是悬浮的。 |

应用推荐值是一键操作，可从 Materials 标签页或
健康检查中完成。
