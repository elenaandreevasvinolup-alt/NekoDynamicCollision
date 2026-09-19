# Neko Dynamic Collision (NDC) — 部署與手冊

為 Unity 提供烘焙式凸包碰撞。所有昂貴的計算都發生在編輯器中；
執行時不載入也不路由，只負責讀取與分發。蒙皮角色或靜態網格會變成
一組按骨骼、按區域劃分的凸包，擁有 **零逐幀開銷**、
內建的分區事件，以及用筆刷繪製的按區域物理材質。

元件本身不包含任何產生的程式碼、特性，也不在執行時依賴外掛的編輯器部分。
刪除 `Editor/` 後執行時依然可用。

## 目錄

- [第一部分 — 快速部署](#sec-partA)
  - [1. 烘焙你的第一個角色](#sec-1)
  - [2. 繪製材質區域](#sec-2)
  - [3. 十分鐘上手路徑](#sec-3)
- [第二部分 — 手冊](#sec-partB)
  - [4. 核心概念](#sec-4)
  - [5. 安裝與需求](#sec-5)
  - [6. 烘焙器視窗](#sec-6)
  - [7. 選單參考](#sec-7)
  - [8. 精度](#sec-8)
  - [9. 筆刷](#sec-9)
  - [10. 材質組與預設](#sec-10)
  - [11. 覆蓋率診斷](#sec-11)
  - [12. 碰撞健康檢查](#sec-12)
  - [13. 事件與整合](#sec-13)
  - [14. Trigger 輪詢](#sec-14)
  - [15. 質量、自碰撞與 LOD](#sec-15)
  - [16. 在地化](#sec-16)
  - [17. 目錄結構](#sec-17)
  - [18. 解除安裝](#sec-18)
  - [19. 疑難排解與常見問題](#sec-19)
  - [20. 聯絡方式](#sec-20)
- [附錄 A. 物理材質預設](#sec-appA)
- [附錄 B. 專案物理檢查](#sec-appB)

---

<a id="sec-partA"></a>
# 第一部分 — 快速部署

<a id="sec-1"></a>
## 1. 烘焙你的第一個角色

1. 選取角色並加入 **Dynamic Collision** 元件
   （`Add Component → Neko → Dynamic Collision`，或使用 `GameObject` 選單）。
   建立時元件會找到自身的 `SkinnedMeshRenderer`，並建立一個
   預設材質組。你無需填寫任何內容。
2. 在檢視面板中按下 **Bake**。
3. 凸包會以綁定姿態的線框形式出現在場景檢視中。選取角色
   即可看到；Gizmo 預設跟隨你的選取。

烘焙會在場景旁寫入三類資源：

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

凸包網格是 `_Baked.asset` 的子資源，因此會隨它一起移動，並在
播放模式與建置中保留下來。執行時不會重新計算任何東西。

<a id="sec-2"></a>
## 2. 繪製材質區域

筆刷正是讓“一個物件、多種物理材質”成為可能的東西。

1. 開啟烘焙器（`NekoWorks → NekoDynamicCollision → Open Main Window`）。
2. 進入 **Materials** 標籤頁，為每個區域加入一個組——例如
   `Hard` 和 `Soft`。為每個組選擇一個預設。
3. 進入 **Paint** 標籤頁，選擇要繪製的組，按下 **Start painting**。
4. 在場景檢視中，拖曳經過你想要歸入該組的那些面。被繪製的
   三角形會立即填滿該組的顏色。
5. 重新烘焙。現在每個區域都會取得自己的凸包，並帶有自己的物理材質。

只有在播放模式已停止、且動畫視窗未在預覽時才能繪製。姿態本身並不重要——
標籤按三角形索引儲存，
因此目前姿態與標籤無關。

<a id="sec-3"></a>
## 3. 十分鐘上手路徑

| 分鐘 | 做什麼 |
|---|---|
| 0–2 | 加入元件，按下 Bake，查看 Gizmo。 |
| 2–4 | 開啟 **Health** 標籤頁，修復所有紅色項。 |
| 4–6 | 開啟 **Bake** 標籤頁，查看覆蓋率數字。低於約 95% 時，提高精度。 |
| 6–9 | 為每個區域加入一個材質組，繪製它，然後重新烘焙。 |
| 9–10 | 將互動層設為 `Bullet`，接入一個事件名，在播放模式中測試。 |

---

<a id="sec-partB"></a>
# 第二部分 — 手冊

<a id="sec-4"></a>
## 4. 核心概念

### 是凸包，不是三角形湯

動態（非運動學）的 `Rigidbody` 不能使用非凸的 `MeshCollider`——
這是 PhysX 的限制，而不是 Unity 的。因此移動物體的每個碰撞形狀
都必須是凸的。NDC 烘焙的是 **凸包**，並把凸包本身而不是原始
三角形子集交給 Unity，這正是頂點數永遠不會觸及 PhysX 上限
255 的原因。

### 凸包，還是真實表面

凸包是預設值，因為它是 PhysX 在移動物體上唯一接受的形狀，但它有代價：
凸包必須把凹陷「封住」，所以在凹陷關節——腋下、腹股溝、脖子——相鄰兩根
骨頭的凸包無法相接。那裡會留一條縫，細小的命中會從縫裡穿過去。這是凸性
的性質，不是烘焙的缺陷。

`碰撞體形狀 → 非凸表面` 改為烘焙每根骨頭的真實表面，不做凸包膨脹。關節
變成無縫的，和取消勾選 **Convex** 的系統碰撞體完全一樣。代價來自
PhysX，健康檢查會直說：只適用於運動學或動畫驅動的主體、不支援網格對網格
碰撞、沒有快速寬相位。它適合行動裝置做命中判定，不適合做物理阻擋。這個
模式記錄在每個烘焙出的部件上，所以在這個選項出現之前烘焙的集合會保持全凸。

### 為什麼骨骼剛性凸包已經足夠

在綁定姿態下，`bone.localToWorldMatrix · bindposes[i] = I`。因此對某個
權重 100% 綁定到單根骨骼的頂點來說，蒙皮的結果恰好就是綁定姿態的網格。
換句話說：**在骨骼局部空間中烘焙的凸包，與逐幀重新烘焙的結果逐位元相同**，
對於剛性加權的頂點而言。

只有混合權重的頂點——也就是跨越關節的那些——會有所不同。它們由相鄰凸包
覆蓋，而這些凸包按構造本就互相重疊。這就是 NDC 能在執行時零開銷、
同時在關鍵之處依然準確的原因。

### 空間聚類，而不是按索引順序切塊

NDC 按位置對三角形分組（最遠點播種，加上沿邊鄰接的 Dijkstra 式生長）。
另一種做法——按索引順序取三角形——會產生互相重疊、包裹空氣的凸包，
而且你要求的凸包越多，結果越糟。

### 分區與材質組

兩條相互獨立的軸：

- **分區** —— *在哪裡*。一根骨骼（Skin 模式）或整個網格（Mesh 模式）。
  子分區總是優先於祖先，因此事件絕不會被派發兩次。
- **材質組** —— *是什麼*。一組共享同一物理材質和同一
  密度的面，透過繪製建立。

凸包是一個分區與一個材質組的交集。如果某個組在給定骨骼內沒有
被繪製的面，就不會為這一組合產生凸包。

### 執行時做什麼

1. 載入已烘焙的資料集。
2. 在正確的骨骼下為每個凸包建立一個隱藏的子物件，使用單位
   變換，並賦予一個凸的 `MeshCollider`。
3. 建構 `Collider → hull` 查找表。
4. 在每個擁有凸包的 `Rigidbody` 上放置一個 `Dyc_Relay`。
5. 設定層、自碰撞與質量。

然後就結束了。除了可選的 Trigger 輪詢和用於 LOD 的距離檢查之外，
沒有任何 `Update` 工作。

<a id="sec-5"></a>
## 5. 安裝與需求

- Unity 2022.3 或更新版本。
- 將 `Assets/NekoDynamicCollision` 複製到你的專案中。沒有任何需要
  設定的地方；組件透過 assembly definition 劃定作用域。
- 兩個組件：
  - `Neko.DynamicCollision.Runtime` —— 元件、Relay、Trigger 輪詢、事件
    結構體以及整合門面。從不參照 `UnityEditor`。
  - `Neko.DynamicCollision.Editor` —— 烘焙器、凸包數學、聚類、筆刷、
    Gizmo、健康檢查、預設以及視窗。僅限編輯器平台。

<a id="sec-6"></a>
## 6. 烘焙器視窗

`NekoWorks → NekoDynamicCollision → Open Main Window`（`Cmd/Ctrl+Shift+D`）。

| 標籤頁 | 功能 |
|---|---|
| **Bake** | 來源、模式、精度、烘焙/清除/重建、統計、覆蓋率 |
| **Paint** | 組清單（含已繪製三角形數量）、筆刷設定、姿態重置 |
| **Gizmo** | 場景檢視繪製什麼、如何繪製 |
| **Parts** | 分區清單——骨骼、包含子級、事件名、傷害倍率 |
| **Materials** | 材質組、預設、配對行為表、專案物理審計 |
| **Health** | 滿分 100 的評分、每個問題、一鍵修復 |
| **Settings** | 語言、診斷、開啟烘焙資料夾、重置偏好設定 |

**Bake** 分頁承載來源、碰撞體形狀、精度和烘焙操作。**排除項**在元件的
Advanced 摺疊區裡，它們是三件不同的事：

- **排除的骨骼**——列出的骨頭**及其整棵子樹**都不產生分區和碰撞體。IK 目標、
  骨頭末端和輔助骨骼就該放這裡。
- **排除的渲染器**——完全不參與烘焙的 `SkinnedMeshRenderer`：頭髮、獨立的
  布料渲染器、道具。
- 材質組上的 **從烘焙中排除**——該組的面完全不進烘焙，所以披風或背帶不需要
  改材質就能不產生判定盒。

專家視窗（`逐骨骼精度`）頂部有一個預設摺疊的區塊，放著不常改的決定：碰撞體
形狀、自碰撞模式和 LOD。

<a id="sec-7"></a>
## 7. 選單參考

一切都放在同一個頂級選單槽下，這樣安裝更多 NekoWorks 外掛時，
選單列永遠不會變寬。

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

選單標題在載入時以及語言變化時在地化；如果你所使用的 Unity 版本
無法使用 Unity 內部的選單 API，
特性中的靜態英文字串就是回退方案。

<a id="sec-8"></a>
## 8. 精度

一個旋鈕，四個檔位。內部會展開為四個值：

| 精度 | 每個凸包的三角形數 | 每個分區的凸包數 | 接縫外擴 | 權重閾值 |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **每個凸包的三角形數** 限制一個凸包可以吸收多少幾何體。每個凸包的三角形越多，
  凸包就越少、越大、越鬆。
- **每個分區的凸包數** 是每個分區的目標聚類數量。聚類越多，
  貼合越緊，碰撞體也越多。
- **接縫外擴** 會把每個凸包向外膨脹，使相鄰區域相互重疊
  而不是留下縫隙。重疊是安全的（命中絕不會被漏掉，且同幀
  去重會阻止重複事件）；而縫隙不安全。
- **權重閾值** 會丟棄對主導骨骼權重低於該值的頂點。
  更高的值意味著更緊、更“剛性正確”的凸包。

一個凸包的頂點數永遠不會超過該聚類的唯一頂點數，而唯一頂點數
上限為 250——安全地低於 PhysX 的 255 上限。健康檢查會把任何
超過 255 的凸包標紅。

### 8.1 凸包模式做分解——超越 CC 與系統複雜碰撞體

在**凸包**模式下，凹網格會沿凹陷切成多個凸塊。這和系統自帶的複雜碰撞體以及 V-HACD 類工具（CC）是同一個思路：任何網格都能出凸塊。NDC 保留廣度，同時把上限抬高：

| | 複雜碰撞體 / CC | NDC |
|---|---|---|
| 任何網格 | 是 | 是 |
| 最佳化良好 | 是 | 是——背景烘焙工作、進度、取消、逐骨骼預算 |
| 部件尊重關節 | **否**——純幾何，肩部會吞掉手臂 | **是**——部件帶有由權重場算出的骨骼標籤 |
| 能否用在運動中的身體上 | **否**——PhysX 拒絕把非凸碰撞體掛在非運動學 `Rigidbody` 上 | **是**——輸出是凸包 |
| 執行時開銷 | 載入時烹調碰撞體 | 零——烘成資產 |
| 兜底 | — | 空間聚類，退化網格照樣有碰撞體 |

專家視窗會報告部件來自**分解**（沿凹陷切）還是**空間聚類**（兜底），所以差別是一個數字，不是猜測。

**切得多細**由一個滑條控制——**Decomposition detail**——緊接著下方的數字框顯示體素毫米數。兩者是*同一個數字*的兩種視圖，所以永遠一致：拖滑條數字框跟著變，改數字框滑條跟著動。不存在需要同步的第二處設定。

- **最左**——體素最粗：部件更少更大，開銷最低，通常道具夠用。
- **最右**——體素最細：部件貼合表面，因此開銷**對齊非凸模式**：再細也換不來精度，只會更貴。

上面的精度表管的是*聚類擬合*——每塊貼得多緊——而不是「你會得到幾個碰撞體」。

同一個滑條在兩種模式下都出現：凸分解與非凸簡化是回答同一個問題的兩種方式——*我到底要多細*。

### 8.2 非凸精度就是一個滑條

把 **Collider shape → Non-convex surface** 切過去，就會出現 **Surface detail** 滑條。

| 滑條 | 結果 |
|---|---|
| 最左 | 大幅簡化，三角形很少，能看出稜面 |
| 中間 | 不錯的折衷：形狀讀得出來，碰撞體也不貴 |
| **最右** | **完全不簡化**——表面直接取自網格 |

為什麼是滑條而不是「每塊多少三角形」：不先知道網格有多少三角形，這個數根本沒法選得合理——500 對軀幹是粗糙，對手指是精確。滑條只回答使用者真正能回答的問題：*我有多在意精確形狀*。最右端不是「幾乎精確」，而是精確：簡化被整個關掉。

不管滑條在哪，小聚類從不簡化——把細手指拉到粗糙網格上會把它塌成「什麼都沒有」，而空碰撞體比昂貴的碰撞體更糟。

蒙皮模式和 mesh 模式共用同一個滑條。

<a id="sec-9"></a>
## 9. 筆刷

筆刷並不“排除”面。它給面 **打標籤**，而標籤驅動烘焙。

| 操作 | 效果 |
|---|---|
| 左鍵拖曳 | 將目前組賦予游標下的三角形 |
| Shift + 拖曳 | 擦除回組 0，並清除已繪製標記 |
| 滑鼠滾輪 | 筆刷半徑 |
| `X` 切換（視窗） | 將每一筆沿物件局部 X = 0 鏡像 |

設定：半徑、X-Ray（忽略背面的三角形）、沿 X 鏡像。

要求會以明確的訊息強制執行，而不是靜默失敗：

1. 播放模式必須已停止。
2. 動畫視窗必須不在預覽。

骨架**不**需要處於綁定姿態。筆刷在網格的**目前**姿態下進行射線檢測；
由於蒙皮從不改變拓撲，三角形索引一一對應，
因此標籤保持正確。

如果你還是希望骨架處於綁定姿態，**Reset to bind pose** 按鈕會由
`bindposes[i].inverse` 解出局部變換並寫回，且支援復原。

<a id="sec-10"></a>
## 10. 材質組與預設

每個組都帶有一個 `PhysicMaterial`、一個以 kg/m³ 為單位的密度、一個可選的事件名
以及一個傷害倍率。

**預設。** 14 個類別共 224 個預設（Metal、Ceramic、Plastic、Glass、Wood、
Stone & Concrete、Rubber、Fabric & Leather、Body parts、Tissue & organs、Organic、
Ice、Food、Other）——參見 [附錄 A](#appendix-a-physic-material-presets)。
套用預設會在 `Baked/<Scene>/Materials/` 下建立一個真實的 `.physicMaterial` 資源，
因此它可以被參照、做差異對比、放進 Addressables，並交給美術。

**材質鍛造器（Material Forge）。** 表格裡的一列回答的是「它由什麼構成」，
而遊戲通常問的是另一個問題：「它**現在**是什麼狀態」。
乾燥的鋼、濕的鋼、生鏽的鋼、沾血的鋼是四種完全不同的手感；
如果把它們都做成獨立的列，就意味著 224 × 15 ≈ 3400 列，沒人維護得動。
所以鍛造器把基礎預設乘以一個**表面狀態**：

| | 狀態 |
|---|---|
| 乘數 | Dry、Wet、Oiled、Bloody、Sweaty、Icy、Frozen、Dusty、Rough、Polished、Rusted、Worn、Charred |
| 覆蓋層 | Clothed（更輕、更抓地）、Armoured（更重、更光滑） |

摩擦會對靜摩擦與動摩擦兩個係數同時乘——否則「濕」表面在起步時仍然是黏的。
`Dry` 是恆等變換，原樣傳回預設，所以永遠不會出現 `steel_dry` 與 `steel` 並存。

**人體部位是推導出來的，不是手寫的。** `Body parts` 與 `Tissue & organs`
根本不是表格裡的列，而是由**組織配比**算出來的：密度按權重相加，柔軟度按權重相加
再加上「軟墊」項，摩擦與彈性則由柔軟度推導——軟組織更抓地、更不彈，硬組織相反。
「Breast」是 80% 脂肪 + 10% 肌肉 + 10% 皮膚，且軟墊很厚；「Skull」是 95% 骨 +
5% 皮膚，且沒有軟墊。這就是為什麼這些數字可以被解釋，而不是硬塞進去的。

**產生資源。** *Generate variant assets* 會把每個狀態各寫成一個 `.physicMaterial`
到 `Baked/<Scene>/Materials/`，美術或企劃可以直接從 Project 視窗取用，不必開啟外掛。

**混合策略。** 整個庫對摩擦使用 `Multiply`，對彈性使用 `Maximum`。
Unity 的混合優先級為 `Average < Minimum < Multiply < Maximum`，
因此在這種策略下，任何光滑表面都會主導摩擦結果，
任何有彈性的材質都會主導彈性結果——
這正是人們直覺上所期望的。

**配對行為表。** Materials 標籤頁會按 Unity 真實的優先級規則解析你專案中的
每一對組，顯示實際會生效的值，
並給出一句大白話結論（“抓地 / 不彈”）。這是回答
“我的冰為什麼不滑”最快的方式。

**按名稱自動分配。** 透過將組名與英語、中文和俄語關鍵字比對，
從預設填入每個組，包含人體部位——名為 “chest” 的組會變成胸部組織，
而不是籠統的 “flesh”。

**誠實的侷限。** 一個 `PhysicMaterial` 只有四個數字和兩種混合模式。它
無法表達滾動摩擦、各向異性摩擦（絲絨）、黏度、塑性變形、溫度或磨損。
柔軟度是生成器的**輸入**，不是隱藏屬性：它從不寫進資源裡，
因為根本沒有地方存它。
這裡的“真實世界參數”指的是有據可查的查找表和可用的預設——
而不是物理模擬。

<a id="sec-11"></a>
## 11. 覆蓋率診斷

Bake 標籤頁回答了一個通常只能靠猜的問題：**哪些三角形完全沒有
凸包覆蓋？**

它會取綁定姿態下的來源網格，用每個三角形質心對每個
凸包的平面做測試，並報告：

- 總體百分比和進度條；
- 按分區的細分；
- 未覆蓋三角形的清單，可在場景檢視中以紅色繪製
  （**Show uncovered faces**）。

把低於約 95% 視為問題：提高精度，或者檢查
分區骨骼是否真的覆蓋了整個骨架。

<a id="sec-12"></a>
## 12. 碰撞健康檢查

滿分 100 的評分，列出每個問題，並在可能時提供一鍵修復。

檢查內容包括：尚未烘焙；凸包超過 PhysX 頂點上限；凸包接近
上限；退化的聚類；不屬於任何分區的三角形；來源網格
自上次烘焙後發生變化；沒有材質、沒有凸包或碎片化凸包過多的材質組；
有 Hitbox/Trigger 角色但沒有互動層；父級鏈中
沒有 `Rigidbody`；剛體質量過小或過大；布娃娃自碰撞
完全開啟；派發的事件沒有監聽者；以及
[附錄 B](#appendix-b-project-physics-checks) 中的專案級物理檢查。

<a id="sec-13"></a>
## 13. 事件與整合

每個事件都攜帶完整上下文，因此你永遠不必再去查找任何東西：

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

三種使用方式：

1. **UnityEvent** —— 元件上的 `onEvent`，供程式碼註冊的監聽者使用。
2. **字串註冊表** —— 給某個分區或組一個事件名，並用
   `Dyc_Events.Register("Hit.Head", handler)` 監聽。拼錯的名字不會報錯，但
   健康檢查會報告那些無人接收的派發。
3. **靜態門面** —— 供外部工具使用的 `Dyc_Api`：

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

傷害倍率位於分區和組上，並會相乘——頭部 ×4 就是
一個數字，而不是一層膠水程式碼。

**去重。** 接縫外擴意味著兩個相鄰的組可能在同一幀都碰到同一個
外來碰撞體。NDC 每幀對每個 `(element, other collider)` 最多派發
一個事件，因此分區材質不會產生重複事件。

<a id="sec-14"></a>
## 14. Trigger 輪詢

Unity 是**按剛體對**投遞 Trigger 回呼的，因此單個布娃娃就是
一個剛體對，物理層根本無法告訴你
哪根骨骼進入了某個體積。給每根骨骼各自配一個 `Rigidbody` 又會摧毀零開銷的承諾。

因此分區 Trigger 採用取樣方式：

- 每個分區都會用 `Physics.OverlapBoxNonAlloc` 對其碰撞體的世界空間
  包圍盒做測試。
- 只考慮真正的 Trigger，且絕不包含你自己的碰撞體。
- 分區按切片處理：每幀 `elements / frames-per-pass` 個。
- 進入和離開會針對每個分區與上一輪做差異比較。

**要記住的語意：** 這是取樣，不是事件。一次非常快的經過可能
被漏掉。提高取樣率，或者使用 **sweep margin** 來擴大查詢盒。

<a id="sec-15"></a>
## 15. 質量、自碰撞與 LOD

**由密度得到質量。** NDC 知道每個凸包的體積，因此可以正確地計算質量：
`mass = hull volume × group density`，可選地做正規化，使整個角色
符合一個目標總質量。這消除了 Unity 布娃娃中最古老的手工調參工作。
單個剛體會接收它所擁有的所有凸包體積之和。

**自碰撞。** `Ignore`（所有配對）、`Adjacent`（同一分區，或祖先與
後代）或 `On`。布娃娃骨骼互相碰撞是抖動的常見來源，
而 `Adjacent` 通常就是答案。碰撞體超過 200 個時，這一步會被跳過
並給出警告，而不是阻塞 `Awake`。

**LOD。** `Disable` 會在超過一定距離後關閉碰撞體；`Reduce` 只保留
每個分區最大的凸包。該檢查每四幀執行一次。

**Rigidbody。** Unity 只向擁有 `Rigidbody` 的 GameObject 投遞碰撞回呼。
對布娃娃來說，每根骨骼都已經有一個。對其他任何東西，啟用
**Auto-add Rigidbody**，NDC 就會在元件所在物件上建立一個運動學剛體。

<a id="sec-16"></a>
## 16. 在地化

視窗、檢視面板、健康檢查訊息和選單標題都在地化為
**15 種語言**：

`en`（內建）· `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- 英語內建於組件中，是任何缺失鍵的回退方案，因此
  部分翻譯的語言會退化，而不是崩潰。
- 其他所有語言都是 `Locale/<code>/strings.json` 中的純資料——加入一種
  無需重新編譯。
- 阿拉伯語和希伯來語完全從右到左：版面會鏡像，而不是依賴
  `style.direction`，UI Toolkit 對它的支援不完整且因版本而異。
- 在 **Settings** 標籤頁中變更語言。視窗和選單會
  立即更新，無需網域重載。
- Settings 標籤頁還會顯示解析後的語言檔路徑以及找到了多少種語言，
  這樣打包錯誤就可見，而不是悄無聲息。

<a id="sec-17"></a>
## 17. 目錄結構

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
## 18. 解除安裝

1. 從你的預製體和場景中移除 **Dynamic Collision** 元件。
2. 刪除 `Assets/NekoDynamicCollision`。

烘焙資源位於外掛資料夾內的 `Baked/` 下，並隨之一起被移除。
不會在外掛資料夾之外寫入任何東西，
執行時也不包含任何依賴編輯器部分的程式碼。

<a id="sec-19"></a>
## 19. 疑難排解與常見問題

**什麼都碰撞不了，也沒有事件觸發。**
父級鏈中沒有 `Rigidbody`。Unity 只向擁有剛體的物件發送碰撞回呼。
啟用 **Auto-add Rigidbody**，或者自己加入一個。

**凸包和我看到的不一致。**
Gizmo 預設繪製 **綁定姿態**——那正是被烘焙的東西。在 Gizmo 標籤頁中
關閉 **Bind pose**，即可在目前姿態下查看它們。

**“凸包有 N 個頂點，超過 PhysX 的 255 上限。”**
Unity 會靜默忽略超限的凸包。把精度降低一檔；
健康檢查正好提供這一鍵修復。

**某些地方會漏掉命中。**
首先檢查覆蓋率百分比。低於約 95% 意味著存在真實的空洞。然後檢查
你目前所處精度檔位的 **接縫外擴**。

**一筆繪製在兩個區域之間留下了縫隙。**
這就是接縫問題。提高精度（這會降低接縫外擴），或者
稍微畫過邊界一點。同幀去重已經能從重疊中
防止重複事件。

**一次命中觸發了兩次事件。**
同一幀內命中了兩個不同的分區，這是合法的。如果你確實
想要每個物件對一個事件，就在處理函式中按 `elementIndex` 過濾。

**我繪製了，但烘焙後什麼都沒變。**
當三角形數量與來源網格不符時，標籤會被忽略——
通常發生在重新匯入或拓撲變化之後。重新繪製，或者先烘焙，
這樣標籤資源就會以正確的大小建立。

**筆刷無法啟動。**
播放模式正在執行，或者動畫視窗正在預覽。兩者都會在 Paint 標籤頁中
作為明確原因顯示出來。

**更新後我以前的筆刷工作消失了。**
不應該如此：在已繪製標記出現之前建立的遮罩會被遷移，任何
非零標籤都會被視作已繪製。如果某個遮罩被清除了，請重新繪製並重新烘焙。

**執行時開銷真的是零嗎？**
在穩定狀態下，是的：凸包是資源，變換由階層跟隨，
完全沒有網格計算。唯一的逐幀工作是選用的 Trigger 輪詢
和 LOD 距離檢查。

**我可以在一個物件上放兩個 Dynamic Collision 元件嗎？**
不可以，而且這是有意阻止的。兩個元件會在同一批面上建立重複的凸包，
使接觸和事件都翻倍。
請改用分區和材質組來劃分。

<a id="sec-20"></a>
## 20. 聯絡方式

NekoAndreeva —— 倉庫 URL 見 `package.json`。

---

<a id="sec-appA"></a>
## 21. 一般用法與 RASCAL 對等

### 21.1 把 NDC 當系統碰撞體用

設計準則是：會寫 `Collider` 和 `Rigidbody` 的人就已經會寫 NDC——因為 NDC 生成的**就是**普通碰撞體：隱藏子物件上的 `MeshCollider`、物件上的 `Rigidbody`、標準訊息、普通圖層與物理材質。`Physics.Raycast` 和 `Physics.OverlapSphere` 完全不用改。

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // 加入 + 建構
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // 標準 Unity 訊息
void OnTriggerStay(Collider other) { }     // 標準 Unity 訊息
```

| 呼叫 | 含義 |
|---|---|
| `Find(go)` | 取元件（自身或父層） |
| `Attach(go, generateNow)` | 加入元件並建構 |
| `Build(go)` / `Rebuild(go)` | 用烘焙集建構，或於執行時生成 |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | 狀態，一次作用於全部外殼 |
| `SetTrigger(go, v)` | 對全部外殼設定 `Collider.isTrigger` |
| `SetMaterial(go, pm)` | 立即生效；不跨重建保留 |
| `GetColliders(go)` / `ForEachCollider(go, a)` | 取回外殼（普通 `Collider`） |
| `SetReceiver(go, t)` | 把標準訊息也發給 `t` |

**只有分區是額外的。** 分區、筆刷材質、LOD、事件、按密度算質量、健康檢查才需要 NDC 的 API——這些正是系統碰撞體做不到的事。

**不需要烘焙。** 勾選 **Advanced ▸ Build at startup when nothing is baked**（或呼叫 `Attach`），`Awake` 時就會按骨骼從網格生成碰撞體——每骨一個凸包，和 RASCAL 的預設一致。烘焙仍然是取得分區、分解、覆蓋率與精度的途徑。

**訊息怎麼到你的指令碼。** Unity 把 `OnCollision*` 發給帶 `Rigidbody` 的物件。如果你的指令碼在別處（指令碼在角色根、Rigidbody 在某根骨頭上），就設定 `Advanced ▸ Also send OnCollision*/OnTrigger* to`——訊息會用 `SendMessage` 轉發，沒有碰撞的幀不花代價。

### 21.2 即時更新——烘焙無法替代的那一項

綁在骨頭上的烘焙外殼在綁定姿勢下是精確的，之後就是剛性的。強形變時（下蹲、肢體被擠壓、布料繃緊）它會報錯表面。即時更新會用**當前**蒙皮姿勢重建外殼。

用 **Advanced ▸ Live update** 或 `Dyc_Collision.EnableLiveUpdate(go)` 打開。

| 設定 | 預設 | 含義 |
|---|---|---|
| `liveUpdate` | 關 | 按當前姿勢重建外殼 |
| `liveUpdateContinuous` | 開 | 持續更新，或按需跑一遍 |
| `idleCpuBudgetMs` | 0.2 | 幾乎不動時的預算 |
| `activeCpuBudgetMs` | 1.0 | 快速運動時的預算 |
| `meshUpdateThreshold` | 0.02 | 位移低於此值（公尺）就跳過 |
| `maxColliderTriangles` | 5000 | 單碰撞體上限，防止一根重骨吃掉預算 |

預算按網格實際位移選擇：站著按便宜檔算，跑動按貴檔算。一幀放不下的工作順延到下一幀，`OnUpdateYield` / `OnPassComplete` 會報告耗時毫秒。

**它不會每幀重建全部。** 三個機制讓開銷可預測：

1. **增量。** 每個聚類的聚類中心與上一遍比較，只重建真的動過的聚類。掛在錨點上的軟體，下擺一直在抖、中間幾乎不動——中間不花任何代價。
2. **按優先順序排序。** 佇列按每個聚類的位移量排序。預算不夠時，不夠的是最平靜的那些聚類——也就是誤差最看不出來的那些。沒有這個排序，預算會花在清單裡恰好排前面的聚類上。
3. **按時鐘而不是按聚類數限預算**：每幀開銷不隨身體的聚類數成長。

`LastDirtyCount` 和 `LastBuiltCount` 會報告上一遍實際做了什麼——這是查看節省效果最誠實的方式。

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // 跑完當前這遍再停
live.UpdateNow();              // 立即完整跑一遍（不計預算）
```

**前提。** 外殼需要 `sourceVertices`，由烘焙寫入；舊角色要重新烘焙才能開即時更新。執行時開銷是真實存在的——這是唯一與「每幀零開銷」相矛盾的功能，所以預設關閉。

### 21.3 逐骨骼覆寫

`Dyc_BoneProperties` 掛在骨頭本身上（Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties）：

| 欄位 | 作用 |
|---|---|
| `overrideMaterial` + `physicsMaterial` | 這根骨頭的外殼用該材質 |
| `overrideConvex` + `convex` | 這根骨頭用凸包而不是表面（或反之） |
| `overrideWeightThreshold` + `boneWeightThreshold` | 逐骨骼權重閾值 |
| `exclude` | 這根骨頭不生成碰撞體 |

掛在骨頭上意味著它能扛住改名——它存的是參照，不是路徑。

### 21.4 按來源材質指定材質

`Advanced ▸ Materials by source material` 把來源 `Material` 對應到 `PhysicMaterial`。外殼按其主要來源的 submesh 歸屬，在烘焙時解析進 `Dyc_BakedSet.sourceMaterials`。優先順序從高到低：

1. `Dyc_BoneProperties.physicsMaterial`；
2. 外殼來源材質對應的關聯材質；
3. 筆刷分組的材質。

### 21.5 頂點排除貼圖

`Advanced ▸ Exclusion map` 讀取紋理的某個通道（R/G/B/A，帶閾值），通道值達到閾值的頂點被排除。筆刷標的是**面**，貼圖標的是**頂點**——兩者互補。網格需要 UV，紋理需要開啟 **Read/Write Enabled**，且只有三個頂點都被排除時三角形才被排除。

### 21.6 重定向骨架

`Advanced ▸ Attach hulls to another skeleton` 用本網格建構外殼，但掛到另一個根的同名骨頭上——即 `RetargetSkeleton` 情境，適用於 Puppet Master 一類方案。骨骼按相對路徑解析；找不到同名骨的會留在自己的骨架上，烘焙報告會說明有多少個。

### 21.7 軟體模式——無骨骼，由程式碼或求解器驅動

軟體模式（`Mode → Soft`）**不是**蒙皮骨架。它面向**沒有骨骼**、形狀由求解器或程式碼產生的網格——即 NekoDynamicSoftbody 一類。軟體模式完全不讀骨骼，幾何就是網格本身。

**烘焙產出什麼。** 網格被切成帶編號的空間聚類。每個外殼**相對於自己的聚類中心**建構，聚類中心作為該聚類的姿勢存進 `clusterRest`。正因如此，幀才能把外殼整體平移**並旋轉**。

**沒有求解器時。** 幀建立在各自的姿勢上，外殼精確落在真實網格幾何上並隨物件移動。形狀是對的，只是沒有動力學。這是有意的降級而不是失敗——也就是「沒有 NDSC 時算真實形狀」的含義。

**有求解器時。** 求解器推送幀（`Push` → `Apply`）並完全接管，給出完整模擬。地址式推送不會在同一幀被全域輪詢覆蓋——只要有多個體就會體現出來。

**沒有骨骼時即時更新同樣可用。** `Dyc_LiveUpdate` 直接讀取 `MeshFilter` 的 CPU 頂點，所以任何在程式碼裡形變網格的東西——軟體求解器、程序化指令碼、自訂形變器——都能驅動精確外殼，不需要任何針對外掛的膠水程式碼。外殼需要 `sourceVertices`，由烘焙寫入；舊資產要重烤。

**誠實的邊界：** 只存在於 GPU 的形變（頂點著色器、GPU 蒙皮）無法在 CPU 側讀回，即時更新看不到它。要嘛把形變放到 CPU，要嘛保留烘焙外殼。

### 21.8 單獨驅動某幾個外殼

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`、`HullIndexOf`、`HullCount`、`BakedSet`、`Elements` 和 `Groups` 都是公開的，外部工具不需要反射就能遍歷和驅動單個外殼。

---

## 附錄 A. 物理材質預設

224 個預設，14 個類別。數值來自有據可查的工程近似值，
對應到 Unity 的四參數模型上。`Body parts` 與 `Tissue & organs`
由鍛造器從組織配比推導，而不是手寫進表格。

| 類別 | 數量 | 預設 |
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

每個預設還帶有一個以 kg/m³ 為單位的**密度**，用於自動質量；
橡膠族預設則帶有它們實現彈跳所必需的 `bounceThreshold`。

這 224 個預設都可以再乘以 14 種非恆等的表面狀態，
所以鍛造器在不增加任何表格列的前提下，覆蓋大約 3100 種可用材質。

<a id="sec-appB"></a>
## 附錄 B. 專案物理檢查

健康檢查會審計專案的 `Physics` 設定，因為材質預設
無法修正全域設定：

| 設定 | 為什麼重要 |
|---|---|
| `bounceThreshold` | 低於該速度的碰撞永遠不會彈跳。在 Unity 預設值 2 下，橡膠預設看起來像壞的。降至 0.2–0.5 才能使用彈性材質。 |
| `defaultSolverVelocityIterations` | 為 1 時，堆疊和快速碰撞會抖動或穿透。2–4 通常更好，它也是布娃娃抖動的常見根因。 |
| `gravity` | 如果不是 −9.81，所有基於 −9.81 得出的質量與衝量直覺都會按同一係數偏移，密度預設也需要修正。 |
| `defaultContactOffset` | 過寬的接觸間隙會讓薄物件看起來像是懸浮的。 |

套用推薦值是一鍵操作，可從 Materials 標籤頁或
健康檢查中完成。
