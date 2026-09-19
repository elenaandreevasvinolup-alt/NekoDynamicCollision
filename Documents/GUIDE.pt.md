# Neko Dynamic Collision (NDC) — implantação e manual

Colisão de envelope convexo pré-cozida para Unity. Todo o trabalho caro acontece no
Editor; o runtime apenas carrega e encaminha. Um personagem com skin ou uma malha estática
torna-se um conjunto de envelopes convexos por osso e por região com **custo zero por frame**,
eventos de partição integrados e materiais físicos por região pintados com um pincel.

O próprio componente não contém código gerado, nem atributos, nem dependência de
runtime da metade de editor do plugin. Exclua `Editor/` e o runtime continua funcionando.

## Conteúdo

- [Parte A — Implantação rápida](#sec-partA)
  - [1. Cozinhe seu primeiro personagem](#sec-1)
  - [2. Pinte regiões de material](#sec-2)
  - [3. O caminho de 10 minutos](#sec-3)
- [Parte B — Manual](#sec-partB)
  - [4. Conceitos principais](#sec-4)
  - [5. Instalação e requisitos](#sec-5)
  - [6. A janela de cozimento](#sec-6)
  - [7. Referência de menu](#sec-7)
  - [8. Precisão](#sec-8)
  - [9. O pincel](#sec-9)
  - [10. Grupos de materiais e presets](#sec-10)
  - [11. Diagnóstico de cobertura](#sec-11)
  - [12. Saúde da colisão](#sec-12)
  - [13. Eventos e integração](#sec-13)
  - [14. Polling de Trigger](#sec-14)
  - [15. Massa, autocolisão e LOD](#sec-15)
  - [16. Localização](#sec-16)
  - [17. Estrutura de diretórios](#sec-17)
  - [18. Desinstalação](#sec-18)
  - [19. Solução de problemas e FAQ](#sec-19)
  - [20. Contato](#sec-20)
- [Apêndice A. Presets de material físico](#sec-appA)
- [Apêndice B. Verificações de física do projeto](#sec-appB)

---

<a id="sec-partA"></a>
# Parte A — Implantação rápida

<a id="sec-1"></a>
## 1. Cozinhe seu primeiro personagem

1. Selecione seu personagem e adicione o componente **Dynamic Collision**
   (`Add Component → Neko → Dynamic Collision`, ou o menu `GameObject`).
   Na criação, o componente encontra seu próprio `SkinnedMeshRenderer` e cria um
   grupo de materiais padrão. Você não precisa preencher nada.
2. Pressione **Bake** no inspector.
3. Os envelopes aparecem na visualização de Cena como um wireframe da pose de bind. Selecione o personagem para
   vê-los; o gizmo segue sua seleção por padrão.

O cozimento grava três tipos de asset ao lado da cena:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

As malhas de envelope são sub-assets de `_Baked.asset`, então elas viajam com ele e
sobrevivem ao Play mode e às builds. Nada é recalculado em runtime.

<a id="sec-2"></a>
## 2. Pinte regiões de material

O pincel é o que torna possível "um objeto, vários materiais físicos".

1. Abra o cozinheiro (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Vá para a aba **Materials** e adicione um grupo por região — por exemplo
   `Hard` e `Soft`. Escolha um preset para cada um.
3. Vá para a aba **Paint**, escolha o grupo que deseja pintar e pressione **Start painting**.
4. Na visualização de Cena, arraste sobre as faces que deseja nesse grupo. Os triângulos
   pintados são preenchidos com a cor do grupo imediatamente.
5. Recozinhe. Cada região agora recebe seus próprios envelopes convexos com seu próprio material físico.

Você só pode pintar enquanto o modo Play estiver parado e a janela Animation não estiver
pré-visualizando. A pose em si não importa — os rótulos são armazenados por índice de triângulo,
então a pose atual é irrelevante.

<a id="sec-3"></a>
## 3. O caminho de 10 minutos

| Minuto | Faça isto |
|---|---|
| 0–2 | Adicione o componente, pressione Bake, olhe o gizmo. |
| 2–4 | Abra a aba **Health**, corrija tudo que estiver vermelho. |
| 4–6 | Abra a aba **Bake**, olhe o número de cobertura. Abaixo de ~95%, aumente a precisão. |
| 6–9 | Adicione um grupo de materiais por região, pinte-o, recozinhe. |
| 9–10 | Defina a camada de interação como `Bullet`, conecte um nome de evento, teste no modo Play. |

---

<a id="sec-partB"></a>
# Parte B — Manual

<a id="sec-4"></a>
## 4. Conceitos principais

### Envelopes, não sopa de triângulos

Um `Rigidbody` dinâmico (não cinemático) não pode usar um `MeshCollider` não convexo — isso
é uma restrição do PhysX, não da Unity. Então toda forma de colisão para um corpo em movimento
precisa ser convexa. O NDC cozinha **envelopes convexos** e entrega à Unity o próprio envelope em vez
do subconjunto bruto de triângulos, e é por isso que a contagem de vértices nunca pode atingir o
teto de 255 do PhysX.

### Por que envelopes rígidos por osso são suficientes

Na pose de bind, `bone.localToWorldMatrix · bindposes[i] = I`. Portanto, a skinning de um vértice
ponderado 100% em um osso resulta exatamente na malha da pose de bind. Em
outras palavras: **um envelope cozido no espaço local do osso é bit a bit o que um
recozimento por frame produziria** para vértices rigidamente ponderados.

Apenas os vértices com peso misto — os que atravessam uma junta — diferem. Eles são cobertos
pelos envelopes vizinhos, que se sobrepõem por construção. É por isso que o NDC pode ser gratuito em
runtime e ainda ser preciso onde importa.

### Agrupamento espacial, não divisão por ordem de índice

O NDC agrupa triângulos por posição (semeadura por ponto mais distante mais crescimento ao estilo Dijkstra
sobre adjacência de arestas). A alternativa — pegar triângulos em ordem de índice — produz
envelopes que se sobrepõem e envolvem ar, e pioram quanto mais envelopes você pede.

### Partições e grupos de materiais

Duas dimensões independentes:

- **Partição** — *onde*. Um osso (modo Skin) ou a malha inteira (modo Mesh).
  Uma partição filha sempre vence uma ancestral, então os eventos nunca são disparados duas vezes.
- **Grupo de materiais** — *o quê*. Um conjunto de faces que compartilham um material físico e uma
  densidade, criado ao pintar.

Um envelope é a interseção de uma partição e um grupo de materiais. Se um grupo não tem
faces pintadas dentro de um determinado osso, nenhum envelope é produzido para esse par.

### O que o runtime faz

1. Carrega o conjunto cozido.
2. Cria um objeto filho oculto por envelope sob o osso correto, com um transform
   de identidade, e atribui um `MeshCollider` convexo.
3. Constrói uma tabela de consulta `Collider → hull`.
4. Coloca um `Dyc_Relay` em cada `Rigidbody` que possui um envelope.
5. Configura camadas, autocolisão e massa.

Depois ele para. Não há trabalho de `Update` além de um polling opcional de trigger e de uma
verificação de distância para LOD.

<a id="sec-5"></a>
## 5. Instalação e requisitos

- Unity 2022.3 ou mais recente.
- Copie `Assets/NekoDynamicCollision` para o seu projeto. Não há nada para
  configurar; as assemblies são delimitadas por definições de assembly.
- Duas assemblies:
  - `Neko.DynamicCollision.Runtime` — o componente, o relay, o polling de trigger, as structs
    de evento e a fachada de integração. Nunca referencia `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — o cozinheiro, a matemática de envelope, o agrupamento, o pincel,
    o gizmo, a verificação de saúde, os presets e a janela. Plataforma apenas de Editor.

<a id="sec-6"></a>
## 6. A janela de cozimento

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Aba | O que faz |
|---|---|
| **Bake** | Fonte, modo, precisão, cozimento/limpeza/reconstrução, estatísticas, cobertura |
| **Paint** | Lista de grupos com contagens de triângulos pintados, configurações do pincel, redefinição de pose |
| **Gizmo** | O que a visualização de Cena desenha e como |
| **Parts** | A lista de partições — osso, incluir filhos, nome do evento, multiplicador de dano |
| **Materials** | Grupos de materiais, presets, tabela de comportamento de pares, auditoria de física do projeto |
| **Health** | Pontuação de 0 a 100, cada problema, correções com um clique |
| **Settings** | Idioma, diagnósticos, abrir pasta de cozimento, redefinir preferências |

<a id="sec-7"></a>
## 7. Referência de menu

Tudo fica sob um único slot de nível superior para que instalar mais plugins NekoWorks
nunca aumente a barra de menus.

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

As legendas do menu são localizadas no momento do carregamento e na troca de idioma; as strings
estáticas em inglês nos atributos são o fallback caso a API interna de menu da Unity não esteja
disponível na sua versão.

<a id="sec-8"></a>
## 8. Precisão

Um controle, quatro níveis. Internamente ele se expande em quatro valores:

| Precisão | Triângulos por envelope | Envelopes por partição | Sobreposição de junta | Limite de peso |
|---|---|---|---|---|
| Grosseira | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fina | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Triângulos por envelope** limita quanta geometria um envelope pode absorver. Mais triângulos
  por envelope significa menos envelopes, maiores e mais frouxos.
- **Envelopes por partição** é o número alvo de clusters por partição. Mais clusters
  significa um ajuste mais firme e mais colliders.
- **Sobreposição de junta** infla cada envelope para fora para que as regiões vizinhas se sobreponham
  em vez de deixar uma lacuna. A sobreposição é segura (um acerto nunca é perdido, e a
  deduplicação no mesmo frame evita eventos duplos); uma lacuna não é.
- **Limite de peso** descarta vértices cujo peso para o osso dominante está abaixo
  do valor. Mais alto significa um envelope mais firme e mais "rigidamente correto".

A contagem de vértices de um envelope nunca pode exceder a contagem de vértices únicos do cluster, que
é limitada a 250 — com segurança abaixo do limite de 255 do PhysX. A verificação de saúde marca qualquer
envelope acima de 255 em vermelho.

### 8.1 O modo convexo decompõe — além do CC e do collider complexo integrado

No modo **convex**, uma malha côncava é cortada em peças convexas ao longo das suas concavidades. É a mesma ideia do collider complexo integrado e das ferramentas no estilo V-HACD (CC): pegue em qualquer malha e produza peças convexas. O NDC mantém a amplitude e eleva o teto:

| | Collider complexo / CC | NDC |
|---|---|---|
| Qualquer malha | sim | sim |
| Bem otimizado | sim | sim — trabalho de cozimento em segundo plano, progresso, cancelamento, orçamentos por osso |
| As peças respeitam as articulações | **não** — puramente geométrico, um ombro pode engolir um braço | **sim** — as peças carregam rótulos de osso de um campo de pesos |
| Utilizável num corpo em movimento | **não** — o PhysX recusa um collider não convexo num `Rigidbody` não cinemático | **sim** — a saída é um envelope convexo |
| Custo em runtime | cozimento do collider ao carregar | zero — cozido nos assets |
| Alternativa | — | agrupamento espacial, para que uma malha degenerada ainda obtenha um collider |

A janela Expert informa se as peças vieram da **decomposição** (cortadas ao longo das concavidades) ou do **agrupamento espacial** (a alternativa), portanto a diferença é um número e não um palpite.

**Quão fino é o corte** é um único controle deslizante — **Decomposition detail** —, com o tamanho do voxel em milímetros no campo numérico logo abaixo. Os dois são duas vistas de *um* número, por isso concordam sempre: arraste o controle e o campo acompanha, escreva no campo e o controle move-se. Não há uma segunda definição para manter em sincronia.

- **Esquerda** — um voxel grosseiro: menos peças e maiores. O mais barato, e normalmente suficiente para um props.
- **Direita** — o voxel mais fino. As peças acompanham a superfície, por isso o custo **iguala o modo não convexo**: não há nada mais fino a ganhar, só custo.

A tabela de precisão acima aplica-se então ao *ajuste dos clusters* — quão justa fica cada peça —, e não a quantos colliders você obtém.

O mesmo controle aparece nos dois modos: a decomposição convexa e a simplificação não convexa são as duas formas de responder à mesma pergunta: *quanto detalhe eu quero*.

### 8.2 O detalhe não convexo é um único controle

Mude para **Collider shape → Non-convex surface** e aparece um controle **Surface detail**.

| Controle | Resultado |
|---|---|
| Todo à esquerda | Superfície muito simplificada — poucos triângulos, facetas visíveis |
| No meio | Um bom compromisso: a forma lê-se corretamente, o collider continua barato |
| **Todo à direita** | **Nenhuma simplificação** — a superfície é retirada da malha tal como está |

A razão de ser um controle e não um número de triângulos: "quantos triângulos por peça" não se escolhe com sensatez sem saber quantos a malha tem — 500 é grosseiro para um tronco e preciso para um dedo. O controle responde à única pergunta que um utilizador pode realmente fazer: *quanto me importa a forma exata*. A posição toda à direita não é "quase exata", é exata: a simplificação está totalmente desligada.

Os clusters pequenos nunca são simplificados, independentemente do controle — puxar um dedo fino para uma grelha grosseira colapsa-o em nada, e um collider vazio é pior do que um caro.

Tanto o modo Skin como o modo Mesh usam o mesmo controle.

<a id="sec-9"></a>
## 9. O pincel

O pincel não "exclui" faces. Ele as **marca**, e as marcas guiam o cozimento.

| Ação | Efeito |
|---|---|
| Arrastar com o botão esquerdo | Atribui o grupo atual aos triângulos sob o cursor |
| Shift + arrastar | Apaga de volta para o grupo 0 e limpa o sinalizador de pintado |
| Roda do mouse | Raio do pincel |
| Alternância `X` (janela) | Espelha cada traço em torno do X = 0 local do objeto |

Configurações: raio, X-Ray (ignorar triângulos voltados para trás), espelhamento em X.

Requisitos, impostos com uma mensagem explícita em vez de uma falha silenciosa:

1. O modo Play deve estar parado.
2. A janela Animation não deve estar pré-visualizando.

O rig **não** precisa estar na pose de bind. O pincel faz raycast contra a malha
na pose **atual**; como a skinning nunca altera a topologia, os índices de triângulos
mapeiam um para um e os rótulos permanecem corretos.

Se você ainda quiser o rig na pose de bind, o botão **Reset to bind pose** resolve os
transforms locais a partir de `bindposes[i].inverse` e os grava de volta, com undo.

<a id="sec-10"></a>
## 10. Grupos de materiais e presets

Cada grupo carrega um `PhysicMaterial`, uma densidade em kg/m³, um nome de evento
opcional e um multiplicador de dano.

**Presets.** 224 presets em 14 categorias (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — ver [Apêndice A](#appendix-a-physic-material-presets).
Aplicar um preset cria um ativo `.physicMaterial` real em `Baked/<Scene>/Materials/`,
por isso pode ser referenciado, comparado, colocado em Addressables e entregue a um artista.

**A forja de materiais.** Uma linha da tabela responde «do que é feito»; a forja responde
«o que é agora». Multiplica um preset base por um **estado de superfície** (Dry, Wet,
Oiled, Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred,
Clothed, Armoured). `Dry` é a identidade, por isso nunca existe um `steel_dry` ao lado de
um `steel`. O atrito é multiplicado no coeficiente estático e no dinâmico ao mesmo tempo.

**As partes do corpo são derivadas, não escritas.** «Partes do corpo» e «Tecidos e órgãos»
não são linhas da tabela: são calculadas de uma mistura de tecidos — a densidade é
aditiva, a suavidade é aditiva mais um termo de almofada, e o atrito e o ressalto seguem
a suavidade. «Peito» é 80% gordura + 10% músculo + 10% pele; «crânio» é 95% osso + 5% pele.

**Gerar ativos.** *Gerar ativos de variantes* escreve um `.physicMaterial` por estado em
`Baked/<Scene>/Materials/`.

**Estratégia de combinação.** Toda a biblioteca usa `Multiply` para atrito e `Maximum`
para quique. A prioridade de combinação da Unity é
`Average < Minimum < Multiply < Maximum`, então com esta estratégia qualquer superfície escorregadia
domina o resultado de atrito e qualquer material quicante domina o resultado de quique —
que é o que as pessoas esperam intuitivamente.

**Tabela de comportamento de pares.** A aba Materials resolve cada par de grupos do seu
projeto usando as regras reais de prioridade da Unity e mostra o valor que realmente será
aplicado, além de um veredito em linguagem simples ("aderente / sem quique"). Esta é a forma mais rápida
de responder "por que meu gelo não está escorregadio".

**Atribuição automática por nome.** Preenche cada grupo a partir de presets combinando o nome do grupo
com palavras-chave em inglês, chinês e russo.

**Limitação honesta.** Um `PhysicMaterial` tem quatro números e dois modos de combinação. Ele
não consegue expressar atrito de rolamento, atrito anisotrópico, viscosidade, deformação
plástica, temperatura ou desgaste. "Parâmetros do mundo real" aqui significa uma tabela de
consulta com fontes e presets utilizáveis — não uma simulação física.

<a id="sec-11"></a>
## 11. Diagnóstico de cobertura

A aba Bake responde à pergunta que normalmente é um chute: **quais triângulos não têm
nenhum envelope?**

Ele pega a malha de origem na pose de bind e testa o centroide de cada triângulo contra os planos
de cada envelope, reportando:

- uma porcentagem geral e uma barra de progresso;
- uma discriminação por partição;
- a lista de triângulos não cobertos, desenhável na visualização de Cena em vermelho
  (**Show uncovered faces**).

Trate abaixo de ~95% como um problema: aumente a precisão, ou verifique se os ossos das partições
realmente cobrem todo o esqueleto.

<a id="sec-12"></a>
## 12. Saúde da colisão

Uma pontuação de 0 a 100 com cada problema listado e, quando possível, uma correção com um clique.

As verificações incluem: nada cozido; envelope acima do teto de vértices do PhysX; envelopes próximos ao
teto; clusters degenerados; triângulos que não pertencem a nenhuma partição; a malha de origem
alterada desde o último cozimento; grupos de materiais sem material, sem envelopes ou com muitos
envelopes fragmentados; um papel Hitbox/Trigger sem camadas de interação; nenhum `Rigidbody` na
cadeia de pais; massa do rigidbody muito pequena ou muito grande; autocolisão de ragdoll
totalmente ativada; eventos despachados sem listener; e as verificações de física no nível do projeto em
[Apêndice B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Eventos e integração

Cada evento carrega um contexto completo, então você nunca mais precisa procurar nada:

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

Três formas de consumi-lo:

1. **UnityEvent** — `onEvent` no componente, para listeners registrados por código.
2. **Registro por string** — dê a uma partição ou a um grupo um nome de evento e escute com
   `Dyc_Events.Register("Hit.Head", handler)`. Nomes escritos errado não geram erro, mas
   a verificação de saúde reporta despachos que ninguém recebeu.
3. **Fachada estática** — `Dyc_Api` para ferramentas externas:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Os multiplicadores de dano ficam na partição e no grupo e são multiplicados entre si —
cabeça ×4 é um número, não uma camada de código de cola.

**Deduplicação.** A sobreposição de junta significa que dois grupos adjacentes podem tocar o mesmo
collider estranho no mesmo frame. O NDC despacha no máximo um evento por
`(element, other collider)` por frame, então materiais particionados não duplicam eventos.

<a id="sec-14"></a>
## 14. Polling de Trigger

A Unity entrega callbacks de trigger **por par de Rigidbody**, então um único ragdoll é um
par de rigidbody e a camada de física simplesmente não consegue dizer qual osso entrou em um
volume. Dar a cada osso seu próprio `Rigidbody` destruiria a promessa de custo zero.

Então triggers particionados são amostrados:

- Cada partição é testada com `Physics.OverlapBoxNonAlloc` sobre os limites em espaço de mundo
  de seus colliders.
- Apenas triggers reais são considerados, e nunca seus próprios colliders.
- As partições são processadas em fatias: `elements / frames-per-pass` por frame.
- Enter e exit são comparados com a passagem anterior para cada partição.

**Semântica para lembrar:** isto é amostragem, não um evento. Uma passagem muito rápida pode ser
perdida. Aumente a taxa de amostragem, ou use a **sweep margin** para expandir a caixa de consulta.

<a id="sec-15"></a>
## 15. Massa, autocolisão e LOD

**Massa a partir da densidade.** O NDC conhece o volume de cada envelope, então pode calcular a massa corretamente:
`mass = hull volume × group density`, opcionalmente normalizada para que o personagem inteiro
corresponda a uma massa total alvo. Isso elimina a tarefa de ajuste manual mais antiga em
ragdolls da Unity. Um único rigidbody recebe a soma dos volumes dos envelopes que possui.

**Autocolisão.** `Ignore` (todos os pares), `Adjacent` (mesma partição, ou ancestral e
descendente) ou `On`. Ossos de ragdoll colidindo entre si é uma fonte comum de
jitter, e `Adjacent` é a resposta usual. Acima de 200 colliders a etapa é ignorada
com um aviso em vez de bloquear o `Awake`.

**LOD.** `Disable` desliga os colliders além de uma distância; `Reduce` mantém apenas o
maior envelope por partição. A verificação roda a cada quatro frames.

**Rigidbody.** A Unity só entrega callbacks de colisão ao GameObject que possui o
`Rigidbody`. Para um ragdoll, cada osso já tem um. Para qualquer outra coisa, ative
**Auto-add Rigidbody** e o NDC cria um cinemático no objeto do componente.

<a id="sec-16"></a>
## 16. Localização

A janela, o inspector, as mensagens de saúde e as legendas de menu são localizados
em **15 idiomas**:

`en` (built in) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- O inglês é embutido na assembly e é o fallback para qualquer chave ausente, então um
  idioma parcialmente traduzido degrada em vez de quebrar.
- Todos os outros idiomas são dados puros em `Locale/<code>/strings.json` — adicionar um
  não exige recompilação.
- Árabe e hebraico são totalmente da direita para a esquerda: o layout é espelhado em vez de depender
  de `style.direction`, cujo suporte no UI Toolkit é incompleto e dependente de versão.
- Mude o idioma na aba **Settings**. A janela e o menu são atualizados
  imediatamente, sem domain reload.
- A aba Settings também mostra o caminho de locale resolvido e quantos idiomas foram
  encontrados, então um erro de empacotamento fica visível em vez de silencioso.

<a id="sec-17"></a>
## 17. Estrutura de diretórios

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
## 18. Desinstalação

1. Remova o componente **Dynamic Collision** dos seus prefabs e cenas.
2. Exclua `Assets/NekoDynamicCollision`.

Os assets cozidos ficam em `Baked/` dentro da pasta do plugin e vão junto com ela. Nada
é escrito fora da pasta do plugin, e o runtime não contém código que dependa
da metade de editor.

<a id="sec-19"></a>
## 19. Solução de problemas e FAQ

**Nada colide, e nenhum evento dispara.**
Não há nenhum `Rigidbody` na cadeia de pais. A Unity só envia callbacks de colisão ao
objeto que possui o rigidbody. Ative **Auto-add Rigidbody**, ou adicione um você mesmo.

**Os envelopes não correspondem ao que eu vejo.**
O gizmo desenha a **pose de bind** por padrão — foi isso que foi cozido. Desligue
**Bind pose** na aba Gizmo para vê-los na pose atual.

**"O envelope tem N vértices, acima do limite de 255 do PhysX."**
A Unity ignora silenciosamente um envelope convexo acima do limite. Reduza a precisão em um passo; a
verificação de saúde oferece exatamente isso como correção com um clique.

**Acertos são perdidos em alguns lugares.**
Verifique primeiro a porcentagem de cobertura. Abaixo de ~95% significa buracos reais. Depois verifique a
**sobreposição de junta** para o nível de precisão em que você está.

**Um traço de pintura deixou uma lacuna entre duas regiões.**
Esse é o problema de junta. Aumente a precisão (o que reduz a sobreposição de junta) ou pinte um
pouco além da fronteira. A deduplicação no mesmo frame já evita eventos duplos
causados pela sobreposição.

**Eventos disparam duas vezes para um acerto.**
Duas partições diferentes foram atingidas no mesmo frame, o que é legítimo. Se você realmente
quer um evento por par de objetos, filtre por `elementIndex` no seu handler.

**Eu pintei, mas nada mudou após o cozimento.**
Os rótulos são ignorados quando a contagem de triângulos não corresponde à malha de origem —
geralmente após uma reimportação ou uma mudança de topologia. Pinte novamente, ou cozinhe primeiro para que o asset
de rótulos seja criado com o tamanho correto.

**O pincel não inicia.**
O modo Play está em execução, ou a janela Animation está pré-visualizando. Ambos são mostrados como um
motivo explícito na aba Paint.

**Meu trabalho antigo de pincel desapareceu após a atualização.**
Não deveria: máscaras criadas antes de existir o sinalizador de pintado são migradas, e qualquer
rótulo diferente de zero é tratado como pintado. Se uma máscara foi limpa, pinte e recozinhe novamente.

**O custo de runtime é realmente zero?**
Em estado estacionário, sim: os envelopes são assets, os transforms são seguidos pela hierarquia, e
não há nenhum trabalho de malha. O único trabalho por frame é o polling opcional de trigger
e a verificação de distância de LOD.

**Posso ter dois componentes Dynamic Collision em um objeto?**
Não, e isso é bloqueado de propósito. Dois componentes criariam envelopes duplicados sobre as
mesmas faces, dobrariam os contatos e dobrariam os eventos. Em vez disso, particione com partições e
grupos de materiais.

<a id="sec-20"></a>
## 20. Contato

NekoAndreeva — consulte `package.json` para a URL do repositório.

---

<a id="sec-appA"></a>
## 21. Uso comum e paridade com o RASCAL

### 21.1 O NDC como collider de sistema

A regra de conceção é que um programador que conhece `Collider` e `Rigidbody` já conhece o NDC, porque o NDC *cria* colliders comuns: `MeshCollider` em filhos ocultos, um `Rigidbody` no objeto, mensagens padrão, camadas e materiais físicos comuns. `Physics.Raycast` e `Physics.OverlapSphere` não precisam de alteração alguma.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // adicionar + construir
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // uma mensagem padrão da Unity
void OnTriggerStay(Collider other) { }     // uma mensagem padrão da Unity
```

| Chamada | Significado |
|---|---|
| `Find(go)` | O componente, no objeto ou num pai |
| `Attach(go, generateNow)` | Adicionar o componente e construir |
| `Build(go)` / `Rebuild(go)` | Construir a partir do conjunto cozido, ou gerar em runtime |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Estado, todos os envelopes de uma vez |
| `SetTrigger(go, v)` | `Collider.isTrigger` para cada envelope |
| `SetMaterial(go, pm)` | Imediato; não sobrevive a uma reconstrução |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Os envelopes, como `Collider` comuns |
| `SetReceiver(go, t)` | Enviar também mensagens padrão para `t` |

**Só o zoneamento é extra.** Zonas, materiais pintados, LOD, eventos, massa a partir da densidade e a verificação de saúde precisam da API do NDC — são as coisas que um collider de sistema não consegue fazer.

**Sem passo de cozimento.** Ative **Advanced ▸ Build at startup when nothing is baked** (ou chame `Attach`). Os colliders são construídos por osso a partir da malha no `Awake` — um envelope convexo por osso, como a predefinição do RASCAL. O cozimento continua a ser a forma de obter zonas, decomposição, cobertura e precisão.

**Mensagens que chegam ao seu script.** A Unity entrega `OnCollision*` ao objeto com o `Rigidbody`. Se o seu script estiver noutro sítio (uma raiz de personagem enquanto o Rigidbody está num osso), defina `Advanced ▸ Also send OnCollision*/OnTrigger* to` — as mensagens são então reencaminhadas com `SendMessage`, o que não custa nada em frames sem colisão.

### 21.2 Live update — a capacidade que o cozimento não pode substituir

Um envelope cozido colado a um osso é exato na pose de bind e rígido depois. Sob uma deformação forte — um agachamento, um membro comprimido, tecido esticado — o envelope reporta mal a superfície. O live update reconstrói o envelope a partir da pose de skinning **atual**.

Ative-o com **Advanced ▸ Live update** ou `Dyc_Collision.EnableLiveUpdate(go)`.

| Definição | Predefinição | Significado |
|---|---|---|
| `liveUpdate` | off | Reconstruir os envelopes a partir da pose atual |
| `liveUpdateContinuous` | on | Continuar, ou executar uma passagem a pedido |
| `idleCpuBudgetMs` | 0.2 | Orçamento enquanto a malha quase não se move |
| `activeCpuBudgetMs` | 1.0 | Orçamento enquanto se move depressa |
| `meshUpdateThreshold` | 0.02 | Saltar a passagem abaixo deste movimento (metros) |
| `maxColliderTriangles` | 5000 | Teto por collider, para que um osso pesado não coma o orçamento |

O orçamento é escolhido a partir de quanto a malha realmente se moveu, por isso um personagem parado paga a tarifa idle e um a correr paga a ativa. O trabalho que não cabe é adiado para o frame seguinte, e `OnUpdateYield` / `OnPassComplete` reportam os milissegundos decorridos.

**Não reconstrói tudo em cada frame.** Três mecanismos mantêm o custo previsível:

1. **Incremental.** O centro de cada cluster é comparado com a passagem anterior, e só os clusters que realmente se moveram são reconstruídos. Um corpo mole pendurado numa âncora tem uma orla a tremer e um meio quase parado — o meio não custa nada.
2. **Ordenado por prioridade.** A fila é ordenada por quanto cada cluster se moveu. Se o orçamento acabar, acaba nos clusters mais calmos — aqueles em que a imprecisão se nota menos. Sem isto, o orçamento seria gasto naquilo que por acaso estivesse primeiro na lista.
3. **Orçamentado pelo relógio**, e não pelo número de clusters: o custo por frame não cresce com quantos clusters o corpo tem.

`LastDirtyCount` e `LastBuiltCount` reportam o que a última passagem fez de facto, que é a forma honesta de ver a poupança.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // terminar a passagem atual e depois parar
live.UpdateNow();              // uma passagem completa, fora do orçamento
```

**Requisitos.** Os envelopes precisam de `sourceVertices`, escrito pelo cozimento; recoza um personagem mais antigo para ativar o live update. O custo em runtime é real — é a única funcionalidade que contradiz "zero custo por frame", e é exatamente por isso que vem desligada por predefinição.

### 21.3 Substituições por osso

`Dyc_BoneProperties` fica no próprio osso (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Campo | Efeito |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Os envelopes deste osso usam esse material |
| `overrideConvex` + `convex` | Envelope em vez de superfície (ou o inverso) para este osso |
| `overrideWeightThreshold` + `boneWeightThreshold` | Corte de peso por osso |
| `exclude` | Sem colliders para este osso |

Pendurá-lo no osso significa que sobrevive a renomeações — guarda uma referência, não um caminho.

### 21.4 Materiais por material de origem

`Advanced ▸ Materials by source material` mapeia um `Material` de origem para um `PhysicMaterial`. Os envelopes são atribuídos pela submallha de onde provêm maioritariamente, resolvida no cozimento em `Dyc_BakedSet.sourceMaterials`. Prioridade, da mais alta para a mais baixa:

1. `Dyc_BoneProperties.physicsMaterial`;
2. a associação de material para o material de origem do envelope;
3. o material do grupo pintado.

### 21.5 Mapa de exclusão de vértices

`Advanced ▸ Exclusion map` lê um canal da textura (R/G/B/A, com um limite) e exclui os vértices cujo valor de canal está nesse limite ou acima. O pincel marca *faces*, o mapa marca *vértices* — complementam-se. A malha precisa de UVs, a textura precisa de **Read/Write Enabled**, e um triângulo só é excluído quando os seus três vértices o são.

### 21.6 Reorientar o esqueleto

`Advanced ▸ Attach hulls to another skeleton` constrói os envelopes a partir desta malha mas pendura-os em ossos com o mesmo nome de outra raiz — o caso `RetargetSkeleton`, para o Puppet Master e configurações semelhantes. Os ossos são resolvidos por caminho relativo; um envelope sem homónimo fica no seu próprio esqueleto e o relatório de cozimento diz quantos.

### 21.7 Modo soft — sem ossos, guiado por código ou por um solver

O modo soft (`Mode → Soft`) **não** é um rig com skinning. Destina-se a uma malha **sem esqueleto** cuja forma é produzida por um solver ou por código — NekoDynamicSoftbody e semelhantes. Nada no modo soft lê ossos; a geometria da malha é tomada tal como está.

**O que o cozimento produz.** A malha é dividida em clusters espaciais numerados. Cada envelope é construído *relativamente ao centro do seu cluster*, e o centro é guardado como a pose de repouso do cluster (`clusterRest`). É isso que permite a um frame transladar **e rodar** o envelope como uma só peça.

**Sem solver.** Os frames são criados nas suas poses de repouso, por isso os envelopes assentam exatamente na geometria real da malha e movem-se com o objeto. A forma está correta; simplesmente não há dinâmica. Esta é a degradação pretendida, não uma falha — e é o que significa "calcula a forma real sem NDSC".

**Com solver.** O solver empurra os frames (`Push` → `Apply`) e assume o controlo por completo, dando simulação completa. Um push endereçável não é substituído pelo polling global no mesmo frame, o que importa assim que existe mais do que um corpo.

**O live update também funciona sem esqueleto.** `Dyc_LiveUpdate` lê os vértices de CPU de um `MeshFilter` tal como estão, por isso qualquer código que deforme a malha — um solver de corpo mole, um script procedural, um deformador personalizado — conduz envelopes precisos sem cola específica do plugin. Um envelope precisa de `sourceVertices`, escrito pelo cozimento; recoza um asset mais antigo.

**O limite honesto:** a deformação que existe apenas na GPU (um vertex shader, skinning em GPU) não pode ser relida na CPU, por isso o live update não a vê. Passe a deformação para a CPU, ou mantenha os envelopes cozidos.

### 21.8 Conduzir envelopes individuais

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` e `Groups` são públicos, por isso ferramentas externas podem iterar e conduzir envelopes individuais sem reflexão.

---

## Apêndice A. Presets de material físico

224 presets, 14 categorias. Os valores são aproximações de engenharia com fontes, mapeadas para o modelo de quatro parâmetros da Unity. «Partes do corpo» e «Tecidos e órgãos» são derivados pela forja de uma mistura de tecidos em vez de escritos na tabela.

| Categoria | Quantidade | Presets |
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


Cada preset também carrega uma **densidade** em kg/m³ para massa automática, e os presets da família
da borracha carregam o `bounceThreshold` de que precisam para quicar.

<a id="sec-appB"></a>
## Apêndice B. Verificações de física do projeto

A verificação de saúde audita as configurações de `Physics` do projeto, porque um preset de material
não consegue corrigir uma configuração global:

| Configuração | Por que importa |
|---|---|
| `bounceThreshold` | Impactos mais lentos que isto nunca quicam. No padrão da Unity de 2, um preset de borracha parece quebrado. Reduza para 0.2–0.5 para usar materiais elásticos. |
| `defaultSolverVelocityIterations` | Com 1, pilhas e impactos rápidos tremem ou atravessam. 2–4 geralmente é melhor, e é uma causa raiz comum de jitter em ragdoll. |
| `gravity` | Se não for −9.81, toda intuição de massa e impulso derivada de −9.81 fica errada pelo mesmo fator, e os presets de densidade precisam de correção. |
| `defaultContactOffset` | Um vão de contato amplo faz objetos finos parecerem flutuar. |

Aplicar os valores recomendados é uma ação de um clique a partir da aba Materials ou da
verificação de saúde.
