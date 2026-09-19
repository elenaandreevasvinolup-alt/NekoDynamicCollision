# Neko Dynamic Collision (NDC) — kurulum ve kılavuz

Unity için önceden pişirilmiş dışbükey gövde çarpışması. Tüm masraflı işler
Editor'da yapılır; runtime yalnızca yükler ve yönlendirir. Skinli bir karakter veya statik bir mesh,
**kare başına sıfır maliyetle** kemik başına ve bölge başına dışbükey gövdeler kümesine,
yerleşik bölüm olaylarına ve fırçayla boyanmış bölge başına fizik materyallerine dönüşür.

Bileşenin kendisi üretilmiş kod, öznitelik ve eklentinin editor yarısına runtime
bağımlılığı içermez. `Editor/` klasörünü silin, runtime yine de çalışır.

## İçindekiler

- [Bölüm A — Hızlı kurulum](#sec-partA)
  - [1. İlk karakterinizi pişirin](#sec-1)
  - [2. Materyal bölgelerini boyayın](#sec-2)
  - [3. 10 dakikalık yol](#sec-3)
- [Bölüm B — Kılavuz](#sec-partB)
  - [4. Temel kavramlar](#sec-4)
  - [5. Kurulum ve gereksinimler](#sec-5)
  - [6. Pişirme penceresi](#sec-6)
  - [7. Menü referansı](#sec-7)
  - [8. Hassasiyet](#sec-8)
  - [9. Fırça](#sec-9)
  - [10. Malzeme grupları ve ön ayarlar](#sec-10)
  - [11. Kapsama teşhisi](#sec-11)
  - [12. Çarpışma sağlığı](#sec-12)
  - [13. Olaylar ve entegrasyon](#sec-13)
  - [14. Trigger yoklaması](#sec-14)
  - [15. Kütle, kendisiyle çarpışma ve LOD](#sec-15)
  - [16. Yerelleştirme](#sec-16)
  - [17. Dizin yapısı](#sec-17)
  - [18. Kaldırma](#sec-18)
  - [19. Sorun giderme ve SSS](#sec-19)
  - [20. İletişim](#sec-20)
- [Ek A. Fizik materyali ön ayarları](#sec-appA)
- [Ek B. Proje fizik kontrolleri](#sec-appB)

---

<a id="sec-partA"></a>
# Bölüm A — Hızlı kurulum

<a id="sec-1"></a>
## 1. İlk karakterinizi pişirin

1. Karakterinizi seçin ve **Dynamic Collision** bileşenini ekleyin
   (`Add Component → Neko → Dynamic Collision` veya `GameObject` menüsü).
   Oluşturulduğunda bileşen kendi `SkinnedMeshRenderer`'ını bulur ve bir
   varsayılan malzeme grubu oluşturur. Hiçbir şey doldurmanız gerekmez.
2. Inspector'da **Bake** düğmesine basın.
3. Gövdeler, Sahne görünümünde bind pozunda bir wireframe olarak görünür. Onları görmek için karakteri
   seçin; gizmo varsayılan olarak seçiminizi takip eder.

Pişirme, sahnenin yanına üç tür asset yazar:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Gövde mesh'leri `_Baked.asset`'in alt asset'leridir, bu yüzden onunla birlikte taşınır ve
Play mode ile build'lerden sağ çıkar. Runtime'da hiçbir şey yeniden hesaplanmaz.

<a id="sec-2"></a>
## 2. Materyal bölgelerini boyayın

Fırça, "tek nesne, birkaç fizik materyali"ni mümkün kılan şeydir.

1. Pişiriciyi açın (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. **Materials** sekmesine gidin ve her bölge için bir grup ekleyin — örneğin
   `Hard` ve `Soft`. Her biri için bir ön ayar seçin.
3. **Paint** sekmesine gidin, boyamak istediğiniz grubu seçin ve **Start painting**'e basın.
4. Sahne görünümünde, o grupta istediğiniz yüzlerin üzerine sürükleyin. Boyanan
   üçgenler anında grup rengiyle doldurulur.
5. Yeniden pişirin. Her bölge artık kendi fizik materyaliyle kendi dışbükey gövdelerini alır.

Yalnızca Play mode durdurulmuşken ve Animation penceresi önizleme yapmıyorken
boyayabilirsiniz. Pozun kendisi önemli değildir — etiketler üçgen indeksi başına saklanır,
bu yüzden mevcut poz önemsizdir.

<a id="sec-3"></a>
## 3. 10 dakikalık yol

| Dakika | Şunu yapın |
|---|---|
| 0–2 | Bileşeni ekleyin, Bake'e basın, gizmo'ya bakın. |
| 2–4 | **Health** sekmesini açın, kırmızı olan her şeyi düzeltin. |
| 4–6 | **Bake** sekmesini açın, kapsama sayısına bakın. ~%95'in altındaysa hassasiyeti artırın. |
| 6–9 | Her bölge için bir malzeme grubu ekleyin, boyayın, yeniden pişirin. |
| 9–10 | Etkileşim katmanını `Bullet` olarak ayarlayın, bir olay adı bağlayın, Play mode'da test edin. |

---

<a id="sec-partB"></a>
# Bölüm B — Kılavuz

<a id="sec-4"></a>
## 4. Temel kavramlar

### Gövdeler, üçgen çorbası değil

Dinamik (kinematik olmayan) bir `Rigidbody` dışbükey olmayan bir `MeshCollider` kullanamaz — bu
bir PhysX kısıtlamasıdır, Unity'nin değil. Bu yüzden hareket eden bir gövde için her çarpışma şekli
dışbükey olmalıdır. NDC **dışbükey gövdeler** pişirir ve Unity'ye ham üçgen alt kümesi yerine
gövdenin kendisini verir; bu yüzden köşe sayısı PhysX'in 255
sınırına asla ulaşamaz.

### Kemik katı gövdeler neden yeterli

Bind pozunda, `bone.localToWorldMatrix · bindposes[i] = I`. Bu nedenle %100 tek bir kemiğe
ağırlıklandırılmış bir köşe için skinning tam olarak bind pozu mesh'ini verir. Diğer bir
deyişle: **kemik yerel uzayında pişirilmiş bir gövde, katı ağırlıklı köşeler için
kare başına yeniden pişirmenin üreteceği şeyin bit bit aynısıdır**.

Yalnızca karışım ağırlıklı köşeler — bir eklemi geçenler — farklıdır. Bunlar, yapı
gereği örtüşen komşu gövdeler tarafından kapsanır. NDC'nin runtime'da bedava olup
önemli olduğu yerde doğru kalabilmesinin nedeni budur.

### İndeks sırasıyla parçalama değil, uzamsal kümeleme

NDC üçgenleri konuma göre gruplar (en uzak noktadan tohumlama artı kenar komşuluğu
üzerinde Dijkstra tarzı büyüme). Alternatif — üçgenleri indeks sırasına göre almak — birbiriyle
örtüşen ve havayı saran gövdeler üretir ve ne kadar çok gövde isterseniz o kadar kötüleşir.

### Bölümler ve malzeme grupları

Birbirinden bağımsız iki eksen:

- **Bölüm** — *nerede*. Bir kemik (Skin modu) veya tüm mesh (Mesh modu).
  Bir alt bölüm her zaman bir üsttekini yener, bu yüzden olaylar asla iki kez gönderilmez.
- **Malzeme grubu** — *ne*. Bir fizik materyalini ve bir yoğunluğu paylaşan,
  boyayarak oluşturulan bir yüz kümesi.

Bir gövde, bir bölüm ile bir malzeme grubunun kesişimidir. Bir grubun belirli bir kemiğin
içinde boyanmış yüzü yoksa, o çift için hiçbir gövde üretilmez.

### Runtime ne yapar

1. Pişirilmiş kümeyi yükler.
2. Doğru kemiğin altında her gövde için birim transform'lu bir gizli alt nesne oluşturur
   ve dışbükey bir `MeshCollider` atar.
3. Bir `Collider → hull` arama tablosu oluşturur.
4. Bir gövdeye sahip her `Rigidbody` üzerine bir `Dyc_Relay` yerleştirir.
5. Katmanları, kendisiyle çarpışmayı ve kütleyi yapılandırır.

Sonra durur. İsteğe bağlı bir trigger yoklaması ve LOD için bir mesafe kontrolü dışında
hiçbir `Update` işi yoktur.

<a id="sec-5"></a>
## 5. Kurulum ve gereksinimler

- Unity 2022.3 veya daha yeni.
- `Assets/NekoDynamicCollision` klasörünü projenize kopyalayın. Yapılandırılacak
  hiçbir şey yok; assembly'ler assembly tanımlarıyla kapsamlandırılmıştır.
- İki assembly:
  - `Neko.DynamicCollision.Runtime` — bileşen, relay, trigger yoklaması, olay
    struct'ları ve entegrasyon arayüzü. `UnityEditor`'a asla başvurmaz.
  - `Neko.DynamicCollision.Editor` — pişirici, gövde matematiği, kümeleme, fırça,
    gizmo, sağlık kontrolü, ön ayarlar ve pencere. Yalnızca Editor platformu.

<a id="sec-6"></a>
## 6. Pişirme penceresi

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Sekme | Ne yapar |
|---|---|
| **Bake** | Kaynak, mod, hassasiyet, pişir/temizle/yeniden oluştur, istatistikler, kapsama |
| **Paint** | Boyanmış üçgen sayılarıyla grup listesi, fırça ayarları, poz sıfırlama |
| **Gizmo** | Sahne görünümünün ne çizdiği ve nasıl çizdiği |
| **Parts** | Bölüm listesi — kemik, alt öğeleri dahil et, olay adı, hasar çarpanı |
| **Materials** | Malzeme grupları, ön ayarlar, çift davranışı tablosu, proje fizik denetimi |
| **Health** | 100 üzerinden puan, her sorun, tek tıkla düzeltmeler |
| **Settings** | Dil, teşhis, pişirme klasörünü aç, tercihleri sıfırla |

<a id="sec-7"></a>
## 7. Menü referansı

Her şey tek bir üst düzey yuvada yaşar, böylece daha fazla NekoWorks
eklentisi kurmak menü çubuğunu asla genişletmez.

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

Menü başlıkları yükleme sırasında ve dil değişiminde yerelleştirilir; özniteliklerdeki
statik İngilizce dizeler, Unity'nin dahili menü API'si sürümünüzde
mevcut değilse yedektir.

<a id="sec-8"></a>
## 8. Hassasiyet

Tek bir düğme, dört kademe. Dahili olarak dört değere açılır:

| Hassasiyet | Gövde başına üçgen | Bölüm başına gövde | Dikiş örtüşmesi | Ağırlık eşiği |
|---|---|---|---|---|
| Kaba | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| İnce | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Gövde başına üçgen**, bir gövdenin ne kadar geometri emebileceğini sınırlar. Gövde başına daha fazla üçgen,
  daha az, daha büyük ve daha gevşek gövde anlamına gelir.
- **Bölüm başına gövde**, bölüm başına hedef küme sayısıdır. Daha fazla küme,
  daha sıkı bir uyum ve daha fazla collider anlamına gelir.
- **Dikiş örtüşmesi**, komşu bölgelerin boşluk bırakmak yerine örtüşmesi için her gövdoyu
  dışa doğru şişirir. Örtüşme güvenlidir (bir isabet asla kaçmaz ve aynı karede
  tekilleştirme çift olayları durdurur); boşluk değildir.
- **Ağırlık eşiği**, baskın kemik için ağırlığı bu değerin altında olan köşeleri
  atar. Daha yüksek, daha sıkı ve daha "katı biçimde doğru" bir gövde anlamına gelir.

Bir gövdenin köşe sayısı, kümenin benzersiz köşe sayısını asla aşamaz; bu sayı
250 ile sınırlıdır — PhysX'in 255 sınırının güvenli biçimde altında. Sağlık kontrolü 255'in
üzerindeki her gövdoyu kırmızı olarak işaretler.

### 8.1 Dışbükey mod ayrıştırır — CC ve yerleşik karmaşık collider'ın ötesinde

**Dışbükey** modda içbükey bir mesh, içbükeylikleri boyunca dışbükey parçalara kesilir. Bu, yerleşik karmaşık collider ve V-HACD tarzı araçlarla (CC) aynı fikirdir: herhangi bir mesh al, dışbükey parçalar üret. NDC genişliği korur ve tavanı yükseltir:

| | Karmaşık collider / CC | NDC |
|---|---|---|
| Herhangi bir mesh | evet | evet |
| İyi optimize edilmiş | evet | evet — arka plan pişirme işi, ilerleme, iptal, kemik başına bütçe |
| Parçalar eklemleri gözetir | **hayır** — tamamen geometrik, bir omuz bir kolu yutabilir | **evet** — parçalar bir ağırlık alanından gelen kemik etiketleri taşır |
| Hareket eden bir gövdede kullanılabilir | **hayır** — PhysX, kinematik olmayan bir `Rigidbody` üzerinde içbükey olmayan collider'ı reddeder | **evet** — çıktı bir dışbükey gövdedir |
| Çalışma zamanı maliyeti | yüklemede collider pişirme | sıfır — asset'lere pişirilir |
| Yedek | — | uzamsal kümeleme, böylece dejenere bir mesh yine de collider alır |

Expert penceresi parçaların **ayrıştırmadan** (içbükeylikler boyunca kesilmiş) mi yoksa **uzamsal kümelemeden** (yedek) mi geldiğini bildirir, yani fark bir tahmin değil bir sayıdır.

**Kesimin ne kadar ince olduğunu** tek bir kaydırıcı belirler — **Decomposition detail** — ve hemen altındaki sayı alanında voksel boyutu milimetre cinsinden görünür. İkisi *tek* bir sayının iki görünümüdür, bu yüzden her zaman uyuşurlar: kaydırıcıyı sürükleyin, alan izler; alana yazın, kaydırıcı hareket eder. Senkronize tutulacak ikinci bir ayar yoktur.

- **Sol** — kaba bir voksel: daha az, daha büyük parça. En ucuzu ve genelde bir props için yeterli.
- **Sağ** — en ince voksel. Parçalar yüzeyi takip eder, bu yüzden maliyet **içbükey olmayan moda eşitlenir**: daha incesinden kazanç yoktur, yalnızca maliyet vardır.

Yukarıdaki hassasiyet tablosu o zaman *küme uydurmaya* — her parçanın ne kadar sıkı oturduğuna — uygulanır, kaç collider elde ettiğinize değil.

Aynı kaydırıcı her iki modda da görünür: dışbükey ayrıştırma ve içbükey olmayan basitleştirme, aynı soruya cevap vermenin iki yoludur: *ne kadar ayrıntı istiyorum*.

### 8.2 İçbükey olmayan ayrıntı tek bir kaydırıcıdır

**Collider shape → Non-convex surface** seçeneğine geçin, bir **Surface detail** kaydırıcısı belirir.

| Kaydırıcı | Sonuç |
|---|---|
| En sol | Ağır biçimde basitleştirilmiş yüzey — az üçgen, görünür yüzey kırılmaları |
| Orta | İyi bir denge: şekil doğru okunur, collider ucuz kalır |
| **En sağ** | **Hiç basitleştirme yok** — yüzey mesh'ten olduğu gibi alınır |

Kaydırıcı olmasının ve üçgen sayısı olmamasının nedeni: "parça başına kaç üçgen" mesh'te kaç tane olduğunu bilmeden makul biçimde seçilemez — 500 bir gövde için kaba, bir parmak için hassastır. Kaydırıcı, kullanıcının gerçekten sorabileceği tek soruyu yanıtlar: *tam şekli ne kadar önemsiyorum*. En sağ konum "neredeyse tam" değil, tamdır: basitleştirme tamamen kapatılır.

Küçük kümeler kaydırıcının konumundan bağımsız olarak asla basitleştirilmez — ince bir parmağı kaba bir ızgaraya indirmek onu hiçliğe çökertir ve boş bir collider pahalı olandan daha kötüdür.

Hem Skin hem de Mesh modu aynı kaydırıcıyı kullanır.

<a id="sec-9"></a>
## 9. Fırça

Fırça yüzleri "dışlamaz". Onları **etiketler** ve etiketler pişirmeyi yönlendirir.

| Eylem | Etki |
|---|---|
| Sol sürükleme | Geçerli grubu imlecin altındaki üçgenlere atar |
| Shift + sürükleme | Grup 0'a geri siler ve boyanmış bayrağını temizler |
| Fare tekerleği | Fırça yarıçapı |
| `X` değiştirici (pencere) | Her vuruşu nesnenin yerel X = 0 düzleminde yansıtır |

Ayarlar: yarıçap, X-Ray (arkaya bakan üçgenleri yok say), X'te yansıtma.

Gereksinimler, sessiz bir başarısızlık yerine açık bir mesajla uygulanır:

1. Play mode durdurulmuş olmalıdır.
2. Animation penceresi önizleme yapmıyor olmalıdır.

Rig'in **bind pozunda olması gerekmez**. Fırça, mesh'e **mevcut** pozunda raycast
yapar; skinning topolojiyi asla değiştirmediği için üçgen indeksleri
bire bir eşlenir ve etiketler doğru kalır.

Rig'i yine de bind pozuna getirmek istiyorsanız, **Reset to bind pose** düğmesi yerel
transform'ları `bindposes[i].inverse` üzerinden çözer ve undo ile geri yazar.

<a id="sec-10"></a>
## 10. Malzeme grupları ve ön ayarlar

Her grup bir `PhysicMaterial`, kg/m³ cinsinden bir yoğunluk, isteğe bağlı bir olay adı
ve bir hasar çarpanı taşır.

**Ön ayarlar.** 14 kategoride 224 ön ayar (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — bkz. [Ek A](#appendix-a-physic-material-presets).
Bir ön ayar uygulamak `Baked/<Scene>/Materials/` altında gerçek bir `.physicMaterial`
varlığı oluşturur; böylece referans verilebilir, karşılaştırılabilir, Addressables'a
konabilir ve bir sanatçıya teslim edilebilir.

**Malzeme ocağı.** Tablo satırı «neden yapıldığını» söyler; ocak «şu an ne olduğunu»
söyler. Temel ön ayarı bir **yüzey durumuyla** çarpar (Dry, Wet, Oiled, Bloody, Sweaty,
Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred, Clothed, Armoured). `Dry`
birimdir, bu yüzden `steel` yanında asla `steel_dry` olmaz. Sürtünme hem statik hem
dinamik katsayıya birlikte çarpılır.

**Vücut bölgeleri yazılmaz, türetilir.** «Vücut bölgeleri» ve «Doku ve organlar» tablo
satırı değildir: bir doku karışımından hesaplanır — yoğunluk toplanır, yumuşaklık toplanır
artı bir yastık terimi, sürtünme ve sıçrama yumuşaklığı izler. «Göğüs» %80 yağ + %10 kas
+ %10 deridir; «kafatası» %95 kemik + %5 deridir.

**Varlık üretimi.** *Varyant varlıkları üret* her durum için bir `.physicMaterial` dosyasını
`Baked/<Scene>/Materials/` altına yazar.

**Birleştirme stratejisi.** Kütüphanenin tamamı sürtünme için `Multiply`, sıçrama için `Maximum`
kullanır. Unity'nin birleştirme önceliği
`Average < Minimum < Multiply < Maximum` şeklindedir, bu yüzden bu stratejiyle herhangi bir kaygan yüzey
sürtünme sonucuna, herhangi bir sıçrayan materyal de sıçrama sonucuna hâkim olur —
insanların sezgisel olarak beklediği şey budur.

**Çift davranışı tablosu.** Materials sekmesi, projenizdeki her grup çiftini
Unity'nin gerçek öncelik kurallarını kullanarak çözer ve gerçekte uygulanacak
değeri, ayrıca sade bir dille bir yargı ("tutucu / sıçrama yok") gösterir. Bu,
"buzum neden kaygan değil" sorusunu yanıtlamanın en hızlı yoludur.

**Ada göre otomatik atama.** Grup adını İngilizce, Çince ve Rusça anahtar
sözcüklerle eşleştirerek her grubu ön ayarlardan doldurur.

**Dürüst sınırlama.** Bir `PhysicMaterial` dört sayı ve iki birleştirme moduna sahiptir. Yuvarlanma
sürtünmesini, anizotropik sürtünmeyi, viskoziteyi, plastik deformasyonu,
sıcaklığı veya aşınmayı ifade edemez. Buradaki "gerçek dünya parametreleri" kaynaklı bir
arama tablosu ve kullanılabilir ön ayarlar anlamına gelir — fiziksel bir simülasyon değil.

<a id="sec-11"></a>
## 11. Kapsama teşhisi

Bake sekmesi, genellikle tahmin edilen soruyu yanıtlar: **hangi üçgenlerin hiç
gövdesi yok?**

Kaynak mesh'i bind pozunda alır ve her üçgen merkezini her gövdenin
düzlemlerine karşı test eder, şunları bildirir:

- genel bir yüzde ve bir ilerleme çubuğu;
- bölüm başına bir döküm;
- kapsanmayan üçgenlerin listesi, Sahne görünümünde kırmızı olarak çizilebilir
  (**Show uncovered faces**).

~%95'in altını sorun olarak kabul edin: hassasiyeti artırın veya bölüm kemiklerinin
gerçekten tüm iskeleti kapsadığını kontrol edin.

<a id="sec-12"></a>
## 12. Çarpışma sağlığı

Her sorunun listelendiği ve mümkün olduğunda tek tıkla düzeltmenin bulunduğu 100 üzerinden bir puan.

Kontroller şunları içerir: hiçbir şey pişirilmemiş; PhysX köşe sınırının üzerinde gövde; sınıra
yakın gövdeler; dejenere kümeler; hiçbir bölüme ait olmayan üçgenler; son pişirmeden
beri değişen kaynak mesh; materyali, gövdesi olmayan veya çok fazla parçalanmış
gövdesi olan malzeme grupları; etkileşim katmanı olmayan bir Hitbox/Trigger rolü; üst
zincirinde `Rigidbody` olmaması; çok küçük veya çok büyük rigidbody kütlesi; ragdoll kendisiyle
çarpışmanın tamamen açık olması; dinleyicisi olmayan gönderilmiş olaylar; ve proje düzeyindeki
fizik kontrolleri [Ek B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Olaylar ve entegrasyon

Her olay tam bir bağlam taşır, böylece bir daha hiçbir şeyi aramanız gerekmez:

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

Onu tüketmenin üç yolu:

1. **UnityEvent** — kodla kaydedilmiş dinleyiciler için bileşendeki `onEvent`.
2. **String kaydı** — bir bölüme veya gruba bir olay adı verin ve
   `Dyc_Events.Register("Hit.Head", handler)` ile dinleyin. Yanlış yazılmış adlar hata vermez, ancak
   sağlık kontrolü kimsenin almadığı gönderimleri bildirir.
3. **Statik arayüz** — harici araçlar için `Dyc_Api`:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Hasar çarpanları bölümde ve grupta bulunur ve birbiriyle çarpılır —
kafa ×4 bir sayıdır, bir katman yapıştırma kodu değil.

**Tekilleştirme.** Dikiş örtüşmesi, iki komşu grubun aynı karede aynı yabancı
collider'a dokunabileceği anlamına gelir. NDC, kare başına `(element, other collider)`
başına en fazla bir olay gönderir, böylece bölümlenmiş materyaller olayları ikiye katlamaz.

<a id="sec-14"></a>
## 14. Trigger yoklaması

Unity, trigger geri çağırmalarını **Rigidbody çifti başına** iletir, bu yüzden tek bir ragdoll bir
rigidbody çiftidir ve fizik katmanı bir hacme hangi kemiğin girdiğini
size söyleyemez. Her kemiğe kendi `Rigidbody`'sini vermek sıfır maliyet vaadini yok eder.

Bu yüzden bölümlenmiş trigger'lar bunun yerine örneklenir:

- Her bölüm, collider'larının dünya uzayındaki sınırları üzerinde
  `Physics.OverlapBoxNonAlloc` ile test edilir.
- Yalnızca gerçek trigger'lar dikkate alınır ve asla kendi collider'larınız dikkate alınmaz.
- Bölümler dilimler halinde işlenir: kare başına `elements / frames-per-pass`.
- Enter ve exit, her bölüm için önceki geçişle karşılaştırılır.

**Hatırlanacak anlam:** bu bir olay değil, örneklemedir. Çok hızlı bir geçiş
kaçırılabilir. Örnekleme oranını artırın veya sorgu kutusunu genişletmek için **sweep margin** kullanın.

<a id="sec-15"></a>
## 15. Kütle, kendisiyle çarpışma ve LOD

**Yoğunluktan kütle.** NDC her gövdenin hacmini bilir, bu yüzden kütleyi doğru hesaplayabilir:
`mass = hull volume × group density`, isteğe bağlı olarak tüm karakter hedef toplam kütleyle
eşleşecek şekilde normalleştirilir. Bu, Unity ragdoll'larındaki en eski elle ayar
işini ortadan kaldırır. Tek bir rigidbody, sahip olduğu gövdelerin hacimlerinin toplamını alır.

**Kendisiyle çarpışma.** `Ignore` (tüm çiftler), `Adjacent` (aynı bölüm veya ata ve
torun) ya da `On`. Ragdoll kemiklerinin birbiriyle çarpışması yaygın bir
titreme kaynağıdır ve `Adjacent` genellikle yanıttır. 200 collider'ın üzerinde bu adım
`Awake`'i engellemek yerine bir uyarıyla atlanır.

**LOD.** `Disable` belirli bir mesafenin ötesinde collider'ları kapatır; `Reduce` bölüm başına
yalnızca en büyük gövdeyi tutar. Kontrol her dördüncü karede çalışır.

**Rigidbody.** Unity çarpışma geri çağırmalarını yalnızca `Rigidbody`'ye sahip GameObject'e
iletir. Bir ragdoll için her kemikte zaten bir tane vardır. Başka her şey için
**Auto-add Rigidbody**'yi etkinleştirin; NDC bileşenin nesnesinde kinematik bir tane oluşturur.

<a id="sec-16"></a>
## 16. Yerelleştirme

Pencere, inspector, sağlık mesajları ve menü başlıkları **15 dile**
yerelleştirilir:

`en` (built in) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- İngilizce assembly'ye gömülüdür ve eksik her anahtar için yedektir, bu yüzden
  kısmen çevrilmiş bir dil bozulmak yerine geriler.
- Diğer tüm diller `Locale/<code>/strings.json` içinde saf veridir — bir tane eklemek
  yeniden derleme gerektirmez.
- Arapça ve İbranice tamamen sağdan sola çalışır: düzen, UI Toolkit desteği eksik ve
  sürüme bağlı olan `style.direction`'a güvenmek yerine aynalanır.
- Dili **Settings** sekmesinden değiştirin. Pencere ve menü hemen
  güncellenir, domain reload yok.
- Settings sekmesi ayrıca çözümlenen locale yolunu ve kaç dil bulunduğunu gösterir,
  böylece bir paketleme hatası sessiz kalmak yerine görünür olur.

<a id="sec-17"></a>
## 17. Dizin yapısı

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
## 18. Kaldırma

1. **Dynamic Collision** bileşenini prefab'larınızdan ve sahnelerinizden kaldırın.
2. `Assets/NekoDynamicCollision` klasörünü silin.

Pişirilmiş asset'ler eklenti klasörünün içindeki `Baked/` altında bulunur ve onunla birlikte gider. Eklenti
klasörünün dışına hiçbir şey yazılmaz ve runtime, editor yarısına bağlı
hiçbir kod içermez.

<a id="sec-19"></a>
## 19. Sorun giderme ve SSS

**Hiçbir şey çarpışmıyor ve hiçbir olay tetiklenmiyor.**
Üst zincirinde `Rigidbody` yok. Unity çarpışma geri çağırmalarını yalnızca rigidbody'ye
sahip nesneye gönderir. **Auto-add Rigidbody**'yi etkinleştirin veya kendiniz bir tane ekleyin.

**Gövdeler gördüğümle uyuşmuyor.**
Gizmo varsayılan olarak **bind pozu** çizer — pişirilen buydu. Mevcut pozda görmek için
Gizmo sekmesinde **Bind pose**'u kapatın.

**"Gövdenin N köşesi var, PhysX'in 255 sınırının üzerinde."**
Unity, sınırı aşan bir dışbükey gövdeyi sessizce yok sayar. Hassasiyeti bir kademe düşürün;
sağlık kontrolü tam olarak bunu tek tıkla düzeltme olarak sunar.

**İsabetler bazı yerlerde kaçırılıyor.**
Önce kapsama yüzdesini kontrol edin. ~%95'in altı gerçek delikler anlamına gelir. Sonra
bulunduğunuz hassasiyet kademesi için **dikiş örtüşmesini** kontrol edin.

**Bir boyama vuruşu iki bölge arasında boşluk bıraktı.**
Bu dikiş sorunudur. Hassasiyeti artırın (bu, dikiş örtüşmesini azaltır) veya sınırın
biraz ötesine boyayın. Aynı karede tekilleştirme, örtüşmeden kaynaklanan çift olayları
zaten engeller.

**Tek bir isabet için olaylar iki kez tetikleniyor.**
Aynı karede iki farklı bölüme isabet edildi; bu meşrudur. Gerçekten nesne çifti başına
bir olay istiyorsanız, işleyicinizde `elementIndex` ile filtreleyin.

**Boyadım ama pişirmeden sonra hiçbir şey değişmedi.**
Üçgen sayısı kaynak mesh ile eşleşmediğinde etiketler yok sayılır —
genellikle bir yeniden içe aktarmadan veya topoloji değişikliğinden sonra. Yeniden boyayın veya etiket asset'i
doğru boyutta oluşturulsun diye önce pişirin.

**Fırça başlamıyor.**
Play mode çalışıyor veya Animation penceresi önizleme yapıyor. İkisi de Paint sekmesinde
açık bir neden olarak gösterilir.

**Güncellemeden sonra eski fırça çalışmam kayboldu.**
Kaybolmamalı: boyanmış bayrağı yokken oluşturulan maskeler taşınır ve sıfır olmayan her
etiket boyanmış sayılır. Bir maske temizlendiyse yeniden boyayın ve yeniden pişirin.

**Runtime maliyeti gerçekten sıfır mı?**
Kararlı durumda evet: gövdeler asset'tir, transform'lar hiyerarşi tarafından izlenir ve
hiç mesh işi yoktur. Kare başına tek iş, isteğe bağlı trigger yoklaması
ve LOD mesafe kontrolüdür.

**Tek bir nesnede iki Dynamic Collision bileşenim olabilir mi?**
Hayır ve bu kasıtlı olarak engellenmiştir. İki bileşen aynı yüzler üzerinde yinelenen gövdeler
oluşturur, temasları ikiye katlar ve olayları ikiye katlar. Bunun yerine bölümler ve
malzeme gruplarıyla bölümleyin.

<a id="sec-20"></a>
## 20. İletişim

NekoAndreeva — depo URL'si için `package.json`'a bakın.

---

<a id="sec-appA"></a>
## 21. Olağan kullanım ve RASCAL eşdeğerliği

### 21.1 Sistem collider'ı olarak NDC

Tasarım kuralı şudur: `Collider` ve `Rigidbody` bilen bir geliştirici zaten NDC'yi bilir, çünkü NDC olağan collider'ları *oluşturur*: gizli çocuklarda `MeshCollider`, nesnede bir `Rigidbody`, standart mesajlar, olağan katmanlar ve fizik materyalleri. `Physics.Raycast` ve `Physics.OverlapSphere` hiçbir değişiklik gerektirmez.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // ekle + oluştur
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // standart bir Unity mesajı
void OnTriggerStay(Collider other) { }     // standart bir Unity mesajı
```

| Çağrı | Anlamı |
|---|---|
| `Find(go)` | Bileşen, nesnede veya bir üst öğede |
| `Attach(go, generateNow)` | Bileşeni ekle ve oluştur |
| `Build(go)` / `Rebuild(go)` | Pişirilmiş kümeden oluştur veya çalışma zamanında üret |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Durum, tüm gövdeler için birden |
| `SetTrigger(go, v)` | Her gövde için `Collider.isTrigger` |
| `SetMaterial(go, pm)` | Anında; yeniden oluşturmayı atlatmaz |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Gövdeler, olağan `Collider` olarak |
| `SetReceiver(go, t)` | Standart mesajları `t`'ye de gönder |

**Fazladan olan yalnızca bölgelemedir.** Bölgeler, boyanmış materyaller, LOD, olaylar, yoğunluktan kütle ve sağlık kontrolü NDC API'sini gerektirir — bunlar bir sistem collider'ının yapamadığı şeylerdir.

**Pişirme adımı yok.** **Advanced ▸ Build at startup when nothing is baked** seçeneğini işaretleyin (veya `Attach` çağırın). Collider'lar `Awake`'te mesh'ten kemik başına oluşturulur — kemik başına bir dışbükey gövde, RASCAL'ın varsayılanı gibi. Pişirme, bölgeler, ayrıştırma, kapsama ve hassasiyet elde etmenin yolu olmaya devam eder.

**Betiğinize ulaşan mesajlar.** Unity `OnCollision*` mesajlarını `Rigidbody`'si olan nesneye iletir. Betiğiniz başka bir yerdeyse (gövde kökünde bir betik, `Rigidbody` bir kemikteyse), `Advanced ▸ Also send OnCollision*/OnTrigger* to` ayarını yapın — mesajlar o zaman `SendMessage` ile iletilir, bu da çarpışmanın olmadığı karelerde hiçbir maliyet doğurmaz.

### 21.2 Canlı güncelleme — pişirmenin yerini alamayacağı yetenek

Bir kemiğe yapıştırılmış pişirilmiş gövde, bind pozunda tamdır ve sonrasında katıdır. Güçlü deformasyon altında — çömelme, sıkışmış uzuv, gergin kumaş — gövde yüzeyi yanlış bildirir. Canlı güncelleme gövdeyi **geçerli** skinning pozundan yeniden oluşturur.

**Advanced ▸ Live update** veya `Dyc_Collision.EnableLiveUpdate(go)` ile açın.

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `liveUpdate` | kapalı | Gövdeleri geçerli pozadan yeniden oluştur |
| `liveUpdateContinuous` | açık | Devam et veya istek üzerine bir geçiş çalıştır |
| `idleCpuBudgetMs` | 0.2 | Mesh neredeyse hiç hareket etmezken bütçe |
| `activeCpuBudgetMs` | 1.0 | Hızlı hareket ederken bütçe |
| `meshUpdateThreshold` | 0.02 | Bu hareketin (metre) altında geçişi atla |
| `maxColliderTriangles` | 5000 | Collider başına tavan, ağır bir kemik bütçeyi yemesin diye |

Bütçe, mesh'in gerçekte ne kadar hareket ettiğine göre seçilir; bu yüzden duran bir karakter boşta tarifesini, koşan biri etkin tarifeyi öder. Sığmayan iş bir sonraki kareye ertelenir ve `OnUpdateYield` / `OnPassComplete` geçen milisaniyeleri bildirir.

**Her karede her şeyi yeniden oluşturmaz.** Üç mekanizma maliyeti öngörülebilir tutar:

1. **Artımlı.** Her kümenin merkezi önceki geçişle karşılaştırılır ve yalnızca gerçekten hareket eden kümeler yeniden oluşturulur. Bir çapaya asılı yumuşak gövdenin titreyen bir eteği ve neredeyse hareketsiz bir ortası vardır — orta hiçbir maliyet doğurmaz.
2. **Öncelik sırasına göre.** Kuyruk, her kümenin ne kadar hareket ettiğine göre sıralanır. Bütçe tükenirse en sakin kümelerde tükenir — yanlışlığın en az göründüğü olanlarda. Bu olmasaydı bütçe, listede tesadüfen ilk sırada olana harcanırdı.
3. **Küme sayısına göre değil, saate göre bütçelenir**: kare başına maliyet, gövdenin kaç kümesi olduğuyla artmaz.

`LastDirtyCount` ve `LastBuiltCount`, son geçişin gerçekte ne yaptığını bildirir; tasarrufu görmenin dürüst yolu budur.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // geçerli geçişi bitir, sonra dur
live.UpdateNow();              // bütçe dışında tam bir geçiş
```

**Gereksinimler.** Gövdeler, pişirme tarafından yazılan `sourceVertices`'a ihtiyaç duyar; canlı güncellemeyi açmak için eski bir karakteri yeniden pişirin. Çalışma zamanı maliyeti gerçektir — "kare başına sıfır maliyet" ile çelişen tek özelliktir ve tam da bu yüzden varsayılan olarak kapalıdır.

### 21.3 Kemik başına geçersiz kılmalar

`Dyc_BoneProperties` doğrudan kemiğe konur (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Alan | Etkisi |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Bu kemiğin gövdeleri o materyali kullanır |
| `overrideConvex` + `convex` | Bu kemik için yüzey yerine gövde (veya tersi) |
| `overrideWeightThreshold` + `boneWeightThreshold` | Kemik başına ağırlık eşiği |
| `exclude` | Bu kemik için collider yok |

Kemiğe takmak, yeniden adlandırmalardan sağ çıkması demektir — yol değil, referans tutar.

### 21.4 Kaynak materyale göre materyaller

`Advanced ▸ Materials by source material`, bir kaynak `Material`'ı bir `PhysicMaterial`'a eşler. Gövdeler çoğunlukla geldikleri alt mesh'e göre atanır ve bu, pişirme sırasında `Dyc_BakedSet.sourceMaterials` içine çözülür. Öncelik, en yüksekten:

1. `Dyc_BoneProperties.physicsMaterial`;
2. gövdenin kaynak materyali için materyal ilişkilendirmesi;
3. boyanmış grubun materyali.

### 21.5 Köşe dışlama haritası

`Advanced ▸ Exclusion map` bir doku kanalını (R/G/B/A, bir eşikle) okur ve kanal değeri eşiğe eşit veya üstünde olan köşeleri dışlar. Fırça *yüzleri*, harita *köşeleri* işaretler — birbirlerini tamamlarlar. Mesh'in UV'lere, dokunun **Read/Write Enabled** olmasına ihtiyacı vardır ve bir üçgen yalnızca üç köşesi de dışlandığında dışlanır.

### 21.6 İskeleti yeniden hedefleme

`Advanced ▸ Attach hulls to another skeleton`, gövdeleri bu mesh'ten oluşturur ama başka bir kökün aynı adlı kemiklerine asar — `RetargetSkeleton` durumu, Puppet Master ve benzeri kurulumlar için. Kemikler göreli yola göre çözülür; aynı adlı karşılığı olmayan bir gövde kendi iskeletinde kalır ve pişirme raporu kaç tanesinin böyle olduğunu söyler.

### 21.7 Yumuşak mod — kemiksiz, kod veya çözücü güdümlü

Yumuşak mod (`Mode → Soft`) bir skinning rig'i **değildir**. **İskeleti olmayan** ve şekli bir çözücü veya kod tarafından üretilen bir mesh içindir — NekoDynamicSoftbody ve benzerleri. Yumuşak modda hiçbir şey kemikleri okumaz; mesh geometrisi olduğu gibi alınır.

**Pişirmenin ürettiği şey.** Mesh, numaralanmış uzamsal kümelere bölünür. Her gövde *kendi küme merkezine göre* oluşturulur ve merkez, kümenin dinlenme pozu (`clusterRest`) olarak saklanır. Bir karenin gövdeyi tek parça olarak **ötelenmesini ve döndürmesini** sağlayan da budur.

**Çözücü olmadan.** Kareler dinlenme pozlarında oluşturulur, bu yüzden gövdeler tam olarak gerçek mesh geometrisine oturur ve nesneyle birlikte hareket eder. Şekil doğrudur; yalnızca dinamik yoktur. Bu, amaçlanan bozulmadır, başarısızlık değil — ve "NDSC olmadan gerçek şekli hesaplar" tam olarak bunu demektir.

**Çözücü ile.** Çözücü kareleri iter (`Push` → `Apply`) ve tamamen devralır, tam simülasyon verir. Adreslenebilir bir push, aynı karedeki genel yoklama tarafından üzerine yazılmaz; bu, birden fazla gövde olduğu anda önem kazanır.

**Canlı güncelleme iskeletsiz de çalışır.** `Dyc_LiveUpdate`, bir `MeshFilter`'ın CPU köşelerini olduğu gibi okur, bu yüzden mesh'i deforme eden her kod — bir yumuşak gövde çözücüsü, prosedürel bir betik, özel bir deforme edici — eklentiye özel tutkal olmadan doğru gövdeleri sürer. Bir gövde, pişirme tarafından yazılan `sourceVertices`'a ihtiyaç duyar; eski bir asset'i yeniden pişirin.

**Dürüst sınır:** yalnızca GPU'da var olan deformasyon (bir vertex shader, GPU skinning) CPU'da geri okunamaz, bu yüzden canlı güncelleme onu görmez. Deformasyonu CPU'ya taşıyın veya pişirilmiş gövdeleri koruyun.

### 21.8 Tek tek gövdeleri sürme

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` ve `Groups` geneldir, bu yüzden harici araçlar yansıma olmadan tek tek gövdeleri dolaşıp sürebilir.

---

## Ek A. Fizik materyali ön ayarları

224 ön ayar, 14 kategori. Değerler kaynaklı mühendislik yaklaşımlarıdır ve Unity'nin dört parametreli modeline eşlenmiştir. «Vücut bölgeleri» ile «Doku ve organlar» tabloya yazılmaz; ocak bunları bir doku karışımından türetir.

| Kategori | Sayı | Ön ayarlar |
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


Her ön ayar ayrıca otomatik kütle için kg/m³ cinsinden bir **yoğunluk** taşır ve kauçuk
ailesi ön ayarları, hiç sıçrayabilmeleri için ihtiyaç duydukları `bounceThreshold`'u taşır.

<a id="sec-appB"></a>
## Ek B. Proje fizik kontrolleri

Sağlık kontrolü projenin `Physics` ayarlarını denetler, çünkü bir materyal ön ayarı
genel bir ayarı düzeltemez:

| Ayar | Neden önemli |
|---|---|
| `bounceThreshold` | Bundan daha yavaş çarpışmalar asla sıçramaz. Unity'nin varsayılanı olan 2'de bir kauçuk ön ayarı bozuk görünür. Elastik materyaller kullanmak için 0.2–0.5'e düşürün. |
| `defaultSolverVelocityIterations` | 1'de yığınlar ve hızlı çarpışmalar titrer veya içinden geçer. 2–4 genellikle daha iyidir ve ragdoll titremesinin yaygın bir kök nedenidir. |
| `gravity` | −9.81 değilse, −9.81'den türetilen her kütle ve itki sezgisi aynı oranda yanlıştır ve yoğunluk ön ayarlarının düzeltilmesi gerekir. |
| `defaultContactOffset` | Geniş bir temas boşluğu ince nesnelerin havada duruyormuş gibi görünmesine neden olur. |

Önerilen değerleri uygulamak, Materials sekmesinden veya sağlık kontrolünden
tek tıkla yapılan bir eylemdir.
