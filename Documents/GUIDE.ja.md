# Neko Dynamic Collision (NDC) — 導入手順とマニュアル

Unity 向けのベイク済み凸包コリジョン。高コストな処理はすべて Editor 側で行われ、ランタイムは
読み込みと振り分けだけを行います。スキンメッシュのキャラクターや静的メッシュは、ボーン単位・
領域単位の凸包の集合となり、**フレームごとのコストゼロ**、組み込みのパーティションイベント、
そしてブラシで塗った領域単位の PhysicMaterial を持ちます。

コンポーネント自体には生成コードも属性もなく、プラグインのエディタ側へのランタイム依存も
ありません。`Editor/` を削除してもランタイムは動作します。

## 目次

- [パート A — クイック導入](#sec-partA)
  - [1. 最初のキャラクターをベイクする](#sec-1)
  - [2. マテリアル領域を塗る](#sec-2)
  - [3. 10 分でわかる道のり](#sec-3)
- [パート B — マニュアル](#sec-partB)
  - [4. 中核となる概念](#sec-4)
  - [5. インストールと要件](#sec-5)
  - [6. ベイカーウィンドウ](#sec-6)
  - [7. メニューリファレンス](#sec-7)
  - [8. 精度](#sec-8)
  - [9. ブラシ](#sec-9)
  - [10. マテリアルグループとプリセット](#sec-10)
  - [11. カバレッジ診断](#sec-11)
  - [12. コリジョンの健全性](#sec-12)
  - [13. イベントと連携](#sec-13)
  - [14. Trigger のポーリング](#sec-14)
  - [15. 質量、自己衝突、LOD](#sec-15)
  - [16. ローカライズ](#sec-16)
  - [17. ディレクトリ構造](#sec-17)
  - [18. アンインストール](#sec-18)
  - [19. トラブルシューティングと FAQ](#sec-19)
  - [20. 連絡先](#sec-20)
- [付録 A. PhysicMaterial プリセット](#sec-appA)
- [付録 B. プロジェクトの物理設定チェック](#sec-appB)

---

<a id="sec-partA"></a>
# パート A — クイック導入

<a id="sec-1"></a>
## 1. 最初のキャラクターをベイクする

1. キャラクターを選択し、**Dynamic Collision** コンポーネントを追加します
   (`Add Component → Neko → Dynamic Collision`、または `GameObject` メニュー)。
   作成時にコンポーネントは自身の `SkinnedMeshRenderer` を見つけ、1 つの
   既定のマテリアルグループを作成します。何も入力する必要はありません。
2. インスペクターで **Bake** を押します。
3. シーンビューにバインドポーズのワイヤーフレームとして凸包が表示されます。キャラクターを
   選択すると確認できます。Gizmo は既定で選択に追従します。

ベイクはシーンの隣に 3 種類のアセットを書き出します:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

凸包メッシュは `_Baked.asset` のサブアセットなので、それと一緒に移動し、
Play モードやビルドでも保持されます。ランタイムで再計算されるものはありません。

<a id="sec-2"></a>
## 2. マテリアル領域を塗る

ブラシこそが「1 つのオブジェクトに複数の PhysicMaterial」を可能にします。

1. ベイカーを開きます (`NekoWorks → NekoDynamicCollision → Open Main Window`)。
2. **Materials** タブに移動し、領域ごとにグループを追加します — たとえば
   `Hard` と `Soft`。それぞれにプリセットを選択します。
3. **Paint** タブに移動し、塗りたいグループを選び、**Start painting** を押します。
4. シーンビューで、そのグループに含めたい面の上をドラッグします。塗られた
   三角形は即座にグループの色で塗りつぶされます。
5. 再ベイクします。各領域が独自の PhysicMaterial を持つ独自の凸包を取得します。

塗れるのは Play モードが停止していて、Animation ウィンドウがプレビューしていない
ときだけです。ポーズ自体は関係ありません — ラベルは三角形インデックスごとに
保存されるため、現在のポーズは無関係です。

<a id="sec-3"></a>
## 3. 10 分でわかる道のり

| 分 | やること |
|---|---|
| 0–2 | コンポーネントを追加し、Bake を押し、Gizmo を見る。 |
| 2–4 | **Health** タブを開き、赤いものをすべて修正する。 |
| 4–6 | **Bake** タブを開き、カバレッジの数値を見る。約 95% 未満なら精度を上げる。 |
| 6–9 | 領域ごとにマテリアルグループを追加し、塗り、再ベイクする。 |
| 9–10 | インタラクトレイヤーを `Bullet` に設定し、イベント名を配線し、Play モードでテストする。 |

---

<a id="sec-partB"></a>
# パート B — マニュアル

<a id="sec-4"></a>
## 4. 中核となる概念

### 三角形の寄せ集めではなく凸包

動的（非キネマティック）な `Rigidbody` は非凸の `MeshCollider` を使えません — これは
Unity の制約ではなく PhysX の制約です。したがって動くボディのすべてのコリジョン形状は
凸でなければなりません。NDC は**凸包**をベイクし、生の三角形サブセットではなく凸包
そのものを Unity に渡します。だからこそ頂点数が PhysX の上限である 255 に
達することは決してありません。

### なぜボーン固定の凸包で十分なのか

バインドポーズでは `bone.localToWorldMatrix · bindposes[i] = I` です。したがって
1 つのボーンに 100% のウェイトで重み付けされた頂点のスキニングは、正確にバインドポーズの
メッシュに評価されます。言い換えれば、**ボーンローカル空間でベイクされた凸包は、
剛体的に重み付けられた頂点に対して、毎フレーム再クックした場合とビット単位で同一**です。

異なるのは関節をまたぐブレンドウェイトの頂点だけです。それらは隣接する凸包によって
覆われ、構造上重なり合います。だからこそ NDC はランタイムでコストゼロでありながら、
重要な場所では正確であり続けられます。

### インデックス順の分割ではなく空間クラスタリング

NDC は三角形を位置でグループ化します（最遠点シードと、辺の隣接をたどる Dijkstra 風の
成長）。代替案であるインデックス順での三角形の取得は、互いに重なり合い、空気を包み込む
凸包を生成し、要求する凸包が増えるほど悪化します。

### パーツとマテリアルグループ

2 つの独立した軸があります:

- **パーツ (Element)** — *どこ*。1 つのボーン（Skin モード）またはメッシュ全体（Mesh モード）。
  子パーツは常に祖先に優先するため、イベントが二重にディスパッチされることはありません。
- **マテリアルグループ** — *何*。1 つの PhysicMaterial と 1 つの密度を共有する面の集合で、
  塗ることによって作成されます。

凸包は 1 つのパーツと 1 つのマテリアルグループの交差です。あるグループが特定のボーン内に
塗られた面を持たない場合、そのペアに対して凸包は生成されません。

### ランタイムが行うこと

1. ベイク済みセットを読み込みます。
2. 正しいボーンの下に、各凸包につき 1 つの隠された子オブジェクトを、単位変換で
   作成し、凸の `MeshCollider` を割り当てます。
3. `Collider → hull` のルックアップテーブルを構築します。
4. 凸包を所有するすべての `Rigidbody` に `Dyc_Relay` を配置します。
5. レイヤー、自己衝突、質量を設定します。

その後は停止します。任意の Trigger ポーリングと LOD 用の距離チェック以外に
`Update` の処理はありません。

<a id="sec-5"></a>
## 5. インストールと要件

- Unity 2022.3 以降。
- `Assets/NekoDynamicCollision` をプロジェクトにコピーします。設定するものは
  何もありません。アセンブリは Assembly Definition によってスコープされています。
- 2 つのアセンブリ:
  - `Neko.DynamicCollision.Runtime` — コンポーネント、リレー、Trigger ポーリング、イベント
    構造体、連携ファサード。`UnityEditor` を一切参照しません。
  - `Neko.DynamicCollision.Editor` — ベイカー、凸包演算、クラスタリング、ブラシ、
    Gizmo、ヘルスチェック、プリセット、ウィンドウ。エディタ専用プラットフォーム。

<a id="sec-6"></a>
## 6. ベイカーウィンドウ

`NekoWorks → NekoDynamicCollision → Open Main Window`（`Cmd/Ctrl+Shift+D`）。

| タブ | 機能 |
|---|---|
| **Bake** | ソース、モード、精度、ベイク/クリア/再構築、統計、カバレッジ |
| **Paint** | 塗られた三角形数の付いたグループ一覧、ブラシ設定、ポーズリセット |
| **Gizmo** | シーンビューが何をどのように描画するか |
| **Parts** | パーツ一覧 — ボーン、子を含む、イベント名、ダメージ倍率 |
| **Materials** | マテリアルグループ、プリセット、ペア挙動テーブル、プロジェクト物理監査 |
| **Health** | 100 点満点のスコア、すべての問題、ワンクリック修正 |
| **Settings** | 言語、診断、ベイク済みフォルダを開く、環境設定のリセット |

<a id="sec-7"></a>
## 7. メニューリファレンス

すべては単一のトップレベルスロットの下に存在するため、NekoWorks プラグインを
追加でインストールしてもメニューバーが広がることはありません。

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

メニューのキャプションは読み込み時と言語変更時にローカライズされます。属性内の
静的な英語文字列は、お使いのバージョンで Unity の内部メニュー API が利用できない
場合のフォールバックです。

<a id="sec-8"></a>
## 8. 精度

1 つのノブ、4 つの段階。内部的には 4 つの値に展開されます:

| 精度 | 凸包あたりの三角形数 | パーツあたりの凸包数 | シームの重なり | ウェイトしきい値 |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **凸包あたりの三角形数**は、1 つの凸包が吸収できるジオメトリ量の上限です。凸包あたりの
  三角形が多いほど、数が少なく、大きく、緩い凸包になります。
- **パーツあたりの凸包数**は、パーツあたりのクラスタの目標数です。クラスタが多いほど、
  より密着し、コライダーが増えます。
- **シームの重なり**は各凸包を外側に膨らませ、隣接領域が隙間を残す代わりに重なるように
  します。重なりは安全です（ヒットを見逃すことはなく、同一フレームの重複排除が
  二重イベントを防ぎます）。隙間は安全ではありません。
- **ウェイトしきい値**は、支配的なボーンに対するウェイトがこの値を下回る頂点を
  破棄します。高いほど、より密着し「剛体的に正しい」凸包になります。

凸包の頂点数はクラスタの一意な頂点数を超えることはなく、それは 250 に制限されています —
PhysX の上限 255 を安全に下回ります。ヘルスチェックは 255 を超える凸包を
赤で警告します。

### 8.1 凸モードは分解する — CC と標準の複雑コライダーを超えて

**凸**モードでは、凹んだメッシュがその凹みに沿って凸のパーツに切り分けられます。これは標準の複雑コライダーや V-HACD 系のツール (CC) と同じ発想です。任意のメッシュから凸のパーツを作ります。NDC はその広さを保ったまま上限を引き上げます:

| | 複雑コライダー / CC | NDC |
|---|---|---|
| 任意のメッシュ | 可 | 可 |
| 最適化されている | 可 | 可 — バックグラウンドのベイクジョブ、進捗、キャンセル、ボーンごとの予算 |
| パーツが関節を尊重する | **不可** — 純粋に幾何的で、肩が腕を飲み込むことがある | **可** — パーツはウェイトフィールドから得たボーンラベルを持つ |
| 動くボディで使えるか | **不可** — PhysX は非キネマティックな `Rigidbody` 上の非凸コライダーを拒否する | **可** — 出力は凸包 |
| ランタイムコスト | 読み込み時のコライダークッキング | ゼロ — アセットにベイク済み |
| フォールバック | — | 空間クラスタリング。退化したメッシュでもコライダーが得られる |

Expert ウィンドウは、パーツが**分解**（凹みに沿って切断）由来か**空間クラスタリング**（フォールバック）由来かを報告するので、違いは推測ではなく数値になります。

**どれだけ細かく切るか**は 1 つのスライダー — **Decomposition detail** — で決まり、そのすぐ下の数値フィールドにボクセルサイズがミリメートルで表示されます。両者は*1 つの*数値の 2 つの見方なので、常に一致します: スライダーを動かせばフィールドが追従し、フィールドに入力すればスライダーが動きます。同期させる 2 つ目の設定はありません。

- **左端** — 粗いボクセル: パーツは少なく大きく。最も安く、たいていはプロップに十分です。
- **右端** — 最も細かいボクセル。パーツがサーフェスに追従するため、コストは**非凸モードと一致**します。これ以上細かくしても得るものはなく、コストだけが増えます。

上の精度表は*クラスタフィッティング* — 各パーツがどれだけ密着するか — に適用されるのであって、得られるコライダーの数ではありません。

同じスライダーが両方のモードに現れます: 凸分解と非凸簡略化は、*どれだけのディテールが欲しいか*という同じ問いに答える 2 つの方法です。

### 8.2 非凸のディテールは 1 つのスライダー

**Collider shape → Non-convex surface** に切り替えると、**Surface detail** スライダーが現れます。

| スライダー | 結果 |
|---|---|
| 左端 | 大きく簡略化されたサーフェス — 三角形は少なく、面取りが目に見える |
| 中央 | 良い妥協点: 形状は正しく読め、コライダーは安いまま |
| **右端** | **一切簡略化しない** — サーフェスはメッシュからそのまま取得される |

スライダーであって三角形数ではない理由: 「パーツあたり何三角形か」は、メッシュが何三角形持つかを知らずに妥当に選べません — 500 は胴体には粗く、指には精密です。スライダーは、ユーザーが実際に問える唯一の問いに答えます: *正確な形をどれだけ気にするか*。右端は「ほぼ正確」ではなく正確です: 簡略化は完全に切られます。

小さなクラスタはスライダーの位置に関係なく決して簡略化されません — 細い指を粗いグリッドに引き下げると何も残らず、空のコライダーは高価なコライダーより悪いからです。

Skin モードと Mesh モードは同じスライダーを使います。

<a id="sec-9"></a>
## 9. ブラシ

ブラシは面を「除外」しません。**タグ付け**し、そのタグがベイクを駆動します。

| 操作 | 効果 |
|---|---|
| 左ドラッグ | カーソル下の三角形に現在のグループを割り当てる |
| Shift + ドラッグ | グループ 0 に戻して消去し、塗りフラグをクリアする |
| マウスホイール | ブラシ半径 |
| `X` トグル（ウィンドウ） | すべてのストロークをオブジェクトのローカル X = 0 でミラーする |

設定: 半径、X-Ray（裏向きの三角形を無視）、X ミラー。

要件は、黙って失敗するのではなく明示的なメッセージで強制されます:

1. Play モードが停止していること。
2. Animation ウィンドウがプレビューしていないこと。

リグはバインドポーズである必要は**ありません**。ブラシはメッシュに対して
**現在の**ポーズでレイキャストします。スキニングはトポロジーを変えないため、
三角形インデックスは 1 対 1 に対応し、ラベルは正しいままです。

それでもリグをバインドポーズにしたい場合、**Reset to bind pose** ボタンが
ローカル変換を `bindposes[i].inverse` から解き、アンドゥ付きで書き戻します。

<a id="sec-10"></a>
## 10. マテリアルグループとプリセット

各グループは `PhysicMaterial`、kg/m³ 単位の密度、任意のイベント名、
そしてダメージ倍率を持ちます。

**プリセット。** 14 カテゴリにわたる 224 個のプリセット（Metal, Ceramic, Plastic,
Glass, Wood, Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs,
Organic, Ice, Food, Other）— [付録 A](#appendix-a-physic-material-presets) を参照。
プリセットを適用すると `Baked/<Scene>/Materials/` に本物の `.physicMaterial` アセットが
作られるので、参照・差分比較・Addressables への格納・アーティストへの受け渡しができます。

**マテリアルフォージ。** 表の行は「何でできているか」に答えます。フォージは「今どう
なっているか」に答えます。基礎プリセットに**表面状態**（Dry, Wet, Oiled, Bloody,
Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred, Clothed, Armoured）
を掛けます。`Dry` は恒等なので `steel` の隣に `steel_dry` が並ぶことはありません。
摩擦は静摩擦係数と動摩擦係数の両方に掛かります。

**人体部位は導出であって入力ではありません。** 「人体部位」と「組織と臓器」は表の行
ではなく、組織の配合から計算されます — 密度は加算、柔らかさは加算＋クッション項、
摩擦と反発は柔らかさに従います。「胸」は 80% 脂肪 + 10% 筋肉 + 10% 皮膚、「頭蓋骨」は
95% 骨 + 5% 皮膚です。

**アセットの生成。** *バリアントアセットを生成* は状態ごとに `.physicMaterial` を
`Baked/<Scene>/Materials/` へ書き出します。

**合成戦略。** ライブラリ全体で摩擦には `Multiply` を、反発には `Maximum` を
使用します。Unity の合成優先度は
`Average < Minimum < Multiply < Maximum` なので、この戦略では滑りやすい面が摩擦の結果を
支配し、よく跳ねるマテリアルが反発の結果を支配します —
これが人々の直感的な期待です。

**ペア挙動テーブル。** Materials タブは、プロジェクト内のすべてのグループペアを Unity の
実際の優先度ルールで解決し、実際に適用される値と、平易な言葉の判定
（「グリップする / 跳ねない」）を表示します。これは「なぜ自分の氷が滑らないのか」に
答える最速の方法です。

**名前による自動割り当て。** グループ名を英語、中国語、ロシア語のキーワードと
照合して、すべてのグループをプリセットから埋めます。

**正直な制限。** `PhysicMaterial` には 4 つの数値と 2 つの合成モードしかありません。
転がり摩擦、異方性摩擦、粘性、塑性変形、温度、摩耗を表現することはできません。
ここでの「実世界のパラメータ」とは、出典のあるルックアップテーブルと使えるプリセットを
意味し、物理シミュレーションではありません。

<a id="sec-11"></a>
## 11. カバレッジ診断

Bake タブは、通常は推測に頼る疑問に答えます: **どの三角形が凸包を
まったく持たないのか？**

バインドポーズのソースメッシュを取り、すべての三角形の重心をすべての凸包の
平面に対してテストし、次を報告します:

- 全体のパーセンテージとプログレスバー;
- パーツごとの内訳;
- 覆われていない三角形の一覧。シーンビューで赤く描画できます
  （**Show uncovered faces**）。

約 95% 未満は問題とみなしてください: 精度を上げるか、パーツのボーンが実際に
骨格全体を覆っているかを確認してください。

<a id="sec-12"></a>
## 12. コリジョンの健全性

100 点満点のスコアで、すべての問題を一覧表示し、可能な場合はワンクリック修正を提示します。

チェック項目には次が含まれます: 何もベイクされていない; PhysX の頂点上限を超えた凸包;
上限に近い凸包; 縮退したクラスタ; どのパーツにも属さない三角形; 最後のベイク以降に
変更されたソースメッシュ; マテリアルのない、凸包のない、または断片化した凸包が多すぎる
マテリアルグループ; インタラクトレイヤーのない Hitbox/Trigger ロール; 親チェーンに
`Rigidbody` がない; 小さすぎるか大きすぎる Rigidbody の質量; 完全に有効なラグドール
自己衝突; リスナーのないディスパッチされたイベント; そして
[付録 B](#appendix-b-project-physics-checks) のプロジェクトレベルの物理チェック。

<a id="sec-13"></a>
## 13. イベントと連携

すべてのイベントは完全なコンテキストを運ぶので、もう何かを調べ直す必要はありません:

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

消費する方法は 3 つ:

1. **UnityEvent** — コンポーネント上の `onEvent`、コードで登録するリスナー向け。
2. **文字列レジストリ** — パーツまたはグループにイベント名を付け、
   `Dyc_Events.Register("Hit.Head", handler)` でリッスンします。名前を間違えてもエラーにはなりませんが、
   ヘルスチェックは誰も受け取らなかったディスパッチを報告します。
3. **静的ファサード** — 外部ツール向けの `Dyc_Api`:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

ダメージ倍率はパーツとグループにあり、互いに掛け合わされます —
頭 ×4 は 1 つの数値であり、グルーコードの層ではありません。

**重複排除。** シームの重なりにより、隣接する 2 つのグループが同じフレームで同じ
外部コライダーに触れることがあります。NDC はフレームごとに `(パーツ, 外部コライダー)` あたり
最大 1 つのイベントをディスパッチするため、分割されたマテリアルがイベントを二重化しません。

<a id="sec-14"></a>
## 14. Trigger のポーリング

Unity は Trigger コールバックを **Rigidbody ペアごと**に配信するため、1 体のラグドールは
1 つの Rigidbody ペアであり、物理レイヤーはどのボーンがボリュームに入ったかを
教えてくれません。すべてのボーンに独自の `Rigidbody` を与えるとゼロコストの約束が壊れます。

そこで分割された Trigger はサンプリングされます:

- 各パーツは、そのコライダーのワールド空間バウンズに対して `Physics.OverlapBoxNonAlloc` で
  テストされます。
- 実際の Trigger のみが考慮され、自身のコライダーは決して含まれません。
- パーツはスライスで処理されます: フレームごとに `elements / frames-per-pass`。
- 進入と退出は、各パーツの前回パスと差分されます。

**覚えておくべきセマンティクス:** これはサンプリングであり、イベントではありません。
非常に速い通過は見逃される可能性があります。サンプルレートを上げるか、**スイープマージン**でクエリボックスを広げてください。

<a id="sec-15"></a>
## 15. 質量、自己衝突、LOD

**密度からの質量。** NDC はすべての凸包の体積を知っているので、質量を正しく
計算できます: `mass = 凸包の体積 × グループ密度`。任意で、キャラクター全体が
目標総質量に一致するように正規化できます。これは Unity のラグドールにおける最も古い
手作業調整を不要にします。単一の Rigidbody は、所有する凸包の体積の合計を受け取ります。

**自己衝突。** `Ignore`（すべてのペア）、`Adjacent`（同じパーツ、または祖先と
子孫）、`On`。ラグドールのボーン同士が衝突することはジッターの一般的な原因であり、
通常は `Adjacent` が答えです。コライダーが 200 を超えると、`Awake` をブロックする
代わりに警告を出してこのステップはスキップされます。

**LOD。** `Disable` は一定距離を超えるとコライダーをオフにします; `Reduce` はパーツごとに
最大の凸包だけを残します。チェックは 4 フレームごとに実行されます。

**Rigidbody。** Unity は `Rigidbody` を所有する GameObject にのみコリジョンコールバックを
配信します。ラグドールでは、すべてのボーンがすでに持っています。それ以外の場合は、
**Auto-add Rigidbody** を有効にすると、NDC がコンポーネントのオブジェクトにキネマティックなものを作成します。

<a id="sec-16"></a>
## 16. ローカライズ

ウィンドウ、インスペクター、ヘルスメッセージ、メニューキャプションは
**15 言語**にローカライズされています:

`en`（組み込み） · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- 英語はアセンブリに組み込まれ、欠落したキーのフォールバックとなるため、
  部分的に翻訳された言語は壊れるのではなく劣化します。
- 他のすべての言語は `Locale/<code>/strings.json` の純粋なデータです — 追加に
  再コンパイルは不要です。
- アラビア語とヘブライ語は完全に右から左です: レイアウトは `style.direction` に頼るのではなく
  ミラーリングします。その UI Toolkit サポートは不完全でバージョン依存です。
- 言語は **Settings** タブで変更します。ウィンドウとメニューは即座に
  更新され、ドメインリロードはありません。
- Settings タブは解決されたロケールパスと、見つかった言語の数も表示するため、
  パッケージングのミスが黙って見過ごされずに可視化されます。

<a id="sec-17"></a>
## 17. ディレクトリ構造

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
## 18. アンインストール

1. プレハブとシーンから **Dynamic Collision** コンポーネントを削除します。
2. `Assets/NekoDynamicCollision` を削除します。

ベイク済みアセットはプラグインフォルダ内の `Baked/` の下にあり、それと一緒に移動します。
プラグインフォルダの外には何も書き込まれず、ランタイムにはエディタ側に依存する
コードは含まれていません。

<a id="sec-19"></a>
## 19. トラブルシューティングと FAQ

**何も衝突せず、イベントも発火しない。**
親チェーンに `Rigidbody` がありません。Unity は Rigidbody を所有するオブジェクトにのみ
コリジョンコールバックを送ります。**Auto-add Rigidbody** を有効にするか、自分で追加してください。

**凸包が表示されているものと一致しない。**
Gizmo は既定で **バインドポーズ**を描画します — それがベイクされたものです。
Gizmo タブで **Bind pose** をオフにすると、現在のポーズで表示できます。

**"Hull has N vertices, over the PhysX limit of 255."**
Unity は上限を超えた凸包を黙って無視します。精度を 1 段階下げてください;
ヘルスチェックがまさにそれをワンクリック修正として提供します。

**場所によってヒットが見逃される。**
まずカバレッジのパーセンテージを確認してください。約 95% 未満は実際の穴を意味します。
次に、現在の精度段階の **シームの重なり**を確認してください。

**ペイントストロークが 2 つの領域の間に隙間を残した。**
それがシームの問題です。精度を上げる（シームの重なりが小さくなる）か、境界を
少し超えて塗ってください。同一フレームの重複排除が、重なりによる二重イベントを
すでに防いでいます。

**1 回のヒットでイベントが 2 回発火する。**
同じフレームで 2 つの異なるパーツがヒットしたのであり、これは正当です。本当に
オブジェクトペアごとに 1 つのイベントが欲しい場合は、ハンドラ内で `elementIndex` でフィルタしてください。

**塗ったのにベイク後に何も変わらない。**
三角形数がソースメッシュと一致しない場合、ラベルは無視されます —
通常は再インポートやトポロジー変更の後です。再ペイントするか、ラベルアセットが正しい
サイズで作成されるように先にベイクしてください。

**ブラシが開始しない。**
Play モードが実行中か、Animation ウィンドウがプレビュー中です。どちらも Paint タブに
明示的な理由として表示されます。

**更新後に以前のブラシワークが消えた。**
消えるはずがありません: 塗りフラグが存在する前に作成されたマスクは移行され、
ゼロ以外のラベルは塗られたものとして扱われます。マスクが消去された場合は、再ペイントして再ベイクしてください。

**ランタイムコストは本当にゼロですか？**
定常状態でははい: 凸包はアセットであり、変換はヒエラルキーによって追従され、
メッシュの処理は一切ありません。フレームごとの処理は、任意の Trigger ポーリングと
LOD の距離チェックだけです。

**1 つのオブジェクトに 2 つの Dynamic Collision コンポーネントを付けられますか？**
いいえ、意図的にブロックされています。2 つのコンポーネントは同じ面の上に重複した凸包を
作成し、コンタクトを二重にし、イベントを二重にします。代わりにパーツと
マテリアルグループで分割してください。

<a id="sec-20"></a>
## 20. 連絡先

NekoAndreeva — リポジトリ URL は `package.json` を参照してください。

---

<a id="sec-appA"></a>
## 21. 通常の使い方と RASCAL との同等性

### 21.1 システムコライダーとしての NDC

設計方針は、`Collider` と `Rigidbody` を知っている開発者はすでに NDC を知っている、というものです。NDC は普通のコライダーを*作成*するからです: 隠された子オブジェクト上の `MeshCollider`、オブジェクト上の `Rigidbody`、標準メッセージ、普通のレイヤーと物理マテリアル。`Physics.Raycast` と `Physics.OverlapSphere` はまったく変更不要です。

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // 追加 + ビルド
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // 標準の Unity メッセージ
void OnTriggerStay(Collider other) { }     // 標準の Unity メッセージ
```

| 呼び出し | 意味 |
|---|---|
| `Find(go)` | コンポーネント（自身または親） |
| `Attach(go, generateNow)` | コンポーネントを追加してビルド |
| `Build(go)` / `Rebuild(go)` | ベイク済みセットからビルド、またはランタイムで生成 |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | 状態。すべての凸包に一度に |
| `SetTrigger(go, v)` | 各凸包の `Collider.isTrigger` |
| `SetMaterial(go, pm)` | 即時。再ビルドをまたいで保持されない |
| `GetColliders(go)` / `ForEachCollider(go, a)` | 凸包を普通の `Collider` として |
| `SetReceiver(go, t)` | 標準メッセージを `t` にも送る |

**追加なのはゾーニングだけです。** ゾーン、塗装マテリアル、LOD、イベント、密度からの質量、ヘルスチェックには NDC API が必要です — これらはシステムコライダーにできないことです。

**ベイク手順は不要です。** **Advanced ▸ Build at startup when nothing is baked** をオンにする（または `Attach` を呼ぶ）だけで、`Awake` 時にメッシュからボーンごとにコライダーが構築されます — ボーンごとに 1 つの凸包で、RASCAL の既定と同じです。ゾーン、分解、カバレッジ、精度を得る手段は依然としてベイクです。

**スクリプトに届くメッセージ。** Unity は `OnCollision*` を `Rigidbody` を持つオブジェクトに配信します。スクリプトが別の場所にある場合（Rigidbody がボーンにあり、スクリプトがキャラクターのルートにあるなど）は、`Advanced ▸ Also send OnCollision*/OnTrigger* to` を設定します — メッセージは `SendMessage` で転送され、衝突のないフレームでは何もコストがかかりません。

### 21.2 ライブ更新 — ベイクでは代替できない能力

ボーンに貼られたベイク済みの凸包は、バインドポーズでは正確ですが、その後は剛体です。強い変形 — しゃがみ、締め付けられた手足、張った布 — の下では、凸包はサーフェスを誤って報告します。ライブ更新は**現在の**スキニングポーズから凸包を再構築します。

**Advanced ▸ Live update** または `Dyc_Collision.EnableLiveUpdate(go)` で有効にします。

| 設定 | 既定 | 意味 |
|---|---|---|
| `liveUpdate` | オフ | 現在のポーズから凸包を再構築 |
| `liveUpdateContinuous` | オン | 継続する、または要求時に 1 回実行 |
| `idleCpuBudgetMs` | 0.2 | メッシュがほとんど動かないときの予算 |
| `activeCpuBudgetMs` | 1.0 | 速く動くときの予算 |
| `meshUpdateThreshold` | 0.02 | この移動量（メートル）未満ならパスをスキップ |
| `maxColliderTriangles` | 5000 | コライダーごとの上限。重いボーンが予算を食い尽くさないように |

予算はメッシュが実際にどれだけ動いたかで選ばれるので、立っているキャラクターにはアイドル料金、走っているキャラクターにはアクティブ料金が課されます。収まらない作業は次のフレームに回され、`OnUpdateYield` / `OnPassComplete` が経過ミリ秒を報告します。

**毎フレームすべてを再構築するわけではありません。** 3 つの仕組みがコストを予測可能に保ちます:

1. **インクリメンタル。** 各クラスタの中心を前回のパスと比較し、実際に動いたクラスタだけを再構築します。アンカーからぶら下がるソフトボディは裾が震え、中央はほとんど止まっています — 中央は何もコストがかかりません。
2. **優先度順。** キューは各クラスタがどれだけ動いたかで並べ替えられます。予算が尽きるなら、最も落ち着いたクラスタ — 誤差が最も見えにくいもの — で尽きます。これがなければ、予算はたまたまリストの先頭にあるものに使われてしまいます。
3. **クラスタ数ではなく時計で予算化**します: フレームごとのコストはボディが持つクラスタ数に応じて増えません。

`LastDirtyCount` と `LastBuiltCount` は、最後のパスが実際に何をしたかを報告します。これは節約を確認する最も正直な方法です。

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // 現在のパスを終えてから停止
live.UpdateNow();              // 予算外で完全なパスを 1 回
```

**前提。** 凸包にはベイクが書き込む `sourceVertices` が必要です。古いキャラクターはライブ更新を有効にするために再ベイクしてください。ランタイムコストは実際に存在します — 「毎フレームのコストゼロ」に反する唯一の機能であり、だからこそ既定でオフなのです。

### 21.3 ボーンごとの上書き

`Dyc_BoneProperties` はボーン自体に付けます (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| フィールド | 効果 |
|---|---|
| `overrideMaterial` + `physicsMaterial` | このボーンの凸包はそのマテリアルを使う |
| `overrideConvex` + `convex` | このボーンでサーフェスではなく凸包（またはその逆） |
| `overrideWeightThreshold` + `boneWeightThreshold` | ボーンごとのウェイトしきい値 |
| `exclude` | このボーンにはコライダーを作らない |

ボーンに付けるということは、名前を変えても生き残るということです — 保持するのは参照であってパスではありません。

### 21.4 ソースマテリアルによるマテリアル

`Advanced ▸ Materials by source material` は、ソースの `Material` を `PhysicMaterial` にマッピングします。凸包は主に由来するサブメッシュで割り当てられ、ベイク時に `Dyc_BakedSet.sourceMaterials` に解決されます。優先度は高い順に:

1. `Dyc_BoneProperties.physicsMaterial`;
2. 凸包のソースマテリアルに対応する関連付け;
3. 塗装グループのマテリアル。

### 21.5 頂点除外マップ

`Advanced ▸ Exclusion map` はテクスチャのチャンネル（R/G/B/A、しきい値付き）を読み、チャンネル値がしきい値以上である頂点を除外します。ブラシは*面*を、マップは*頂点*をマークします — 互いを補完します。メッシュには UV が必要で、テクスチャには **Read/Write Enabled** が必要で、三角形は 3 つの頂点すべてが除外されるときにのみ除外されます。

### 21.6 スケルトンのリターゲット

`Advanced ▸ Attach hulls to another skeleton` は、このメッシュから凸包を構築しますが、別のルートの同名ボーンに吊るします — `RetargetSkeleton` のケースで、Puppet Master や同種の構成向けです。ボーンは相対パスで解決されます。同名の対応物がない凸包は自分のスケルトンに残り、ベイクレポートがその数を示します。

### 21.7 ソフトモード — ボーンなし、コードまたはソルバー駆動

ソフトモード（`Mode → Soft`）はスキニングリグでは**ありません**。**スケルトンを持たず**、形状がソルバーやコードで生成されるメッシュ向けです — NekoDynamicSoftbody など。ソフトモードでは何もボーンを読みません。メッシュのジオメトリがそのまま使われます。

**ベイクが生成するもの。** メッシュは番号付きの空間クラスタに分割されます。各凸包は*そのクラスタ中心に対して相対的に*構築され、中心はクラスタの静止ポーズ (`clusterRest`) として保存されます。これにより、フレームが凸包を 1 つの塊として**平行移動し、回転**できます。

**ソルバーなしの場合。** フレームは静止ポーズに作成されるので、凸包は実際のメッシュジオメトリに正確に載り、オブジェクトとともに動きます。形状は正しく、単に力学がないだけです。これは意図された劣化であり失敗ではありません — これが「NDSC なしで実形状を計算する」という意味です。

**ソルバーありの場合。** ソルバーがフレームを押し（`Push` → `Apply`）、完全に引き継いで完全なシミュレーションを与えます。アドレス指定可能なプッシュは同じフレームのグローバルポーリングに上書きされません。これは複数のボディが存在するとすぐに重要になります。

**ライブ更新はスケルトンなしでも機能します。** `Dyc_LiveUpdate` は `MeshFilter` の CPU 頂点をそのまま読むので、メッシュを変形するあらゆるコード — ソフトボディソルバー、プロシージャルスクリプト、独自のデフォーマ — が、プラグイン固有の接着剤なしに正確な凸包を駆動します。凸包にはベイクが書き込む `sourceVertices` が必要です。古いアセットは再ベイクしてください。

**正直な限界:** GPU 上にのみ存在する変形（頂点シェーダー、GPU スキニング）は CPU 側で読み戻せないため、ライブ更新には見えません。変形を CPU に移すか、ベイク済みの凸包を保ってください。

### 21.8 個々の凸包を駆動する

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`、`HullIndexOf`、`HullCount`、`BakedSet`、`Elements`、`Groups` は公開されているので、外部ツールはリフレクションなしで個々の凸包を反復して駆動できます。

---

## 付録 A. PhysicMaterial プリセット

224 プリセット、14 カテゴリ。値は出典のある工学的近似を Unity の4パラメータモデルに写したものです。「人体部位」と「組織と臓器」は表に書き込むのではなく、フォージが組織の配合から導出します。

| カテゴリ | 数 | プリセット |
|---|---|---
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


各プリセットは自動質量用の **密度**（kg/m³）も持ち、ゴム系のプリセットは
跳ねるために必要な `bounceThreshold` を持ちます。

<a id="sec-appB"></a>
## 付録 B. プロジェクトの物理設定チェック

ヘルスチェックはプロジェクトの `Physics` 設定を監査します。マテリアルプリセットでは
グローバル設定を修正できないからです:

| 設定 | 重要な理由 |
|---|---|
| `bounceThreshold` | これより遅い衝撃は決して跳ねません。Unity の既定値 2 では、ゴムのプリセットが壊れて見えます。弾性マテリアルを使うには 0.2〜0.5 に下げてください。 |
| `defaultSolverVelocityIterations` | 1 では、スタックや高速衝撃がジッターしたりトンネリングしたりします。2〜4 が通常は良好で、ラグドールのジッターのよくある根本原因です。 |
| `gravity` | −9.81 でない場合、−9.81 から導かれるすべての質量と力積の直感が同じ係数だけずれ、密度プリセットの補正が必要になります。 |
| `defaultContactOffset` | 広いコンタクトギャップは、薄いオブジェクトが浮いているように見せます。 |

推奨値の適用は、Materials タブまたはヘルスチェックからの
ワンクリック操作です。
