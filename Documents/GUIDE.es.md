# Neko Dynamic Collision (NDC) — despliegue y manual

Colisión de envolvente convexa pre-horneada para Unity. Todo el trabajo costoso ocurre en el
Editor; el runtime solo carga y enruta. Un personaje con skin o una malla estática
se convierte en un conjunto de envolventes convexas por hueso y por región con **coste nulo por fotograma**,
eventos de partición integrados y materiales físicos por región pintados con un pincel.

El propio componente no contiene código generado, ni atributos, ni dependencia en runtime
de la mitad de editor del plugin. Borra `Editor/` y el runtime sigue funcionando.

## Contenido

- [Parte A — Despliegue rápido](#sec-partA)
  - [1. Hornea tu primer personaje](#sec-1)
  - [2. Pinta regiones de material](#sec-2)
  - [3. La ruta de 10 minutos](#sec-3)
- [Parte B — Manual](#sec-partB)
  - [4. Conceptos básicos](#sec-4)
  - [5. Instalación y requisitos](#sec-5)
  - [6. La ventana del horneador](#sec-6)
  - [7. Referencia del menú](#sec-7)
  - [8. Precisión](#sec-8)
  - [9. El pincel](#sec-9)
  - [10. Grupos de materiales y presets](#sec-10)
  - [11. Diagnóstico de cobertura](#sec-11)
  - [12. Salud de colisiones](#sec-12)
  - [13. Eventos e integración](#sec-13)
  - [14. Sondeo de Trigger](#sec-14)
  - [15. Masa, autocolisión y LOD](#sec-15)
  - [16. Localización](#sec-16)
  - [17. Estructura de directorios](#sec-17)
  - [18. Desinstalación](#sec-18)
  - [19. Solución de problemas y FAQ](#sec-19)
  - [20. Contacto](#sec-20)
- [Apéndice A. Presets de materiales físicos](#sec-appA)
- [Apéndice B. Comprobaciones de física del proyecto](#sec-appB)

---

<a id="sec-partA"></a>
# Parte A — Despliegue rápido

<a id="sec-1"></a>
## 1. Hornea tu primer personaje

1. Selecciona tu personaje y añade el componente **Dynamic Collision**
   (`Add Component → Neko → Dynamic Collision`, o el menú `GameObject`).
   Al crearse, el componente encuentra su propio `SkinnedMeshRenderer` y crea un
   grupo de materiales predeterminado. No tienes que rellenar nada.
2. Pulsa **Bake** en el inspector.
3. Las envolventes aparecen en la Scene view como un wireframe en pose de bind. Selecciona el personaje para
   verlas; el Gizmo sigue tu selección por defecto.

El horneado escribe tres tipos de asset junto a la escena:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Las mallas de las envolventes son sub-assets de `_Baked.asset`, así que viajan con él y
sobreviven al modo Play y a las builds. Nada se recalcula en runtime.

<a id="sec-2"></a>
## 2. Pinta regiones de material

El pincel es lo que hace posible "un objeto, varios materiales físicos".

1. Abre el horneador (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Ve a la pestaña **Materials** y añade un grupo por región — por ejemplo
   `Hard` y `Soft`. Elige un preset para cada uno.
3. Ve a la pestaña **Paint**, elige el grupo que quieres pintar, pulsa **Start painting**.
4. En la Scene view, arrastra sobre las caras que quieras en ese grupo. Los triángulos
   pintados se rellenan con el color del grupo de inmediato.
5. Vuelve a hornear. Cada región ahora obtiene sus propias envolventes convexas con su propio material físico.

Solo puedes pintar mientras el modo Play está detenido y la ventana Animation no está
en previsualización. La pose en sí no importa — las etiquetas se guardan por índice de triángulo,
así que la pose actual es irrelevante.

<a id="sec-3"></a>
## 3. La ruta de 10 minutos

| Minuto | Haz esto |
|---|---|
| 0–2 | Añade el componente, pulsa Bake, mira el Gizmo. |
| 2–4 | Abre la pestaña **Health**, corrige todo lo que esté en rojo. |
| 4–6 | Abre la pestaña **Bake**, mira el número de cobertura. Por debajo de ~95%, sube la precisión. |
| 6–9 | Añade un grupo de materiales por región, píntalo, vuelve a hornear. |
| 9–10 | Ajusta la capa de interacción a `Bullet`, conecta un nombre de evento, prueba en modo Play. |

---

<a id="sec-partB"></a>
# Parte B — Manual

<a id="sec-4"></a>
## 4. Conceptos básicos

### Envolventes, no sopa de triángulos

Un `Rigidbody` dinámico (no cinemático) no puede usar un `MeshCollider` no convexo — esa
es una restricción de PhysX, no de Unity. Así que toda forma de colisión para un cuerpo en movimiento
debe ser convexa. NDC hornea **envolventes convexas** y le da a Unity la envolvente en sí
en lugar del subconjunto crudo de triángulos, y por eso el recuento de vértices nunca puede alcanzar
el techo de PhysX de 255.

### Por qué bastan las envolventes rígidas por hueso

En pose de bind, `bone.localToWorldMatrix · bindposes[i] = I`. El skinning de un vértice
ponderado al 100% a un hueso se evalúa por tanto exactamente como la malla en pose de bind. En
otras palabras: **una envolvente horneada en el espacio local del hueso es bit a bit lo que produciría
un re-horneado por fotograma** para vértices con peso rígido.

Solo difieren los vértices con peso mezclado — los que cruzan una junta. Esos quedan cubiertos
por las envolventes vecinas, que se solapan por construcción. Por eso NDC puede ser gratuito en
runtime y aun así ser preciso donde importa.

### Agrupación espacial, no troceado por orden de índice

NDC agrupa los triángulos por posición (siembra del punto más lejano más crecimiento estilo Dijkstra
sobre la adyacencia de aristas). La alternativa — tomar los triángulos en orden de índice — produce
envolventes que se solapan entre sí y envuelven aire, y empeoran cuantas más envolventes pidas.

### Particiones y grupos de materiales

Dos ejes independientes:

- **Partición** — *dónde*. Un hueso (modo Skin) o toda la malla (modo Mesh).
  Una partición hija siempre gana a un ancestro, así que los eventos nunca se despachan dos veces.
- **Grupo de materiales** — *qué*. Un conjunto de caras que comparten un material físico y una
  densidad, creado pintando.

Una envolvente es la intersección de una partición y un grupo de materiales. Si un grupo no tiene
caras pintadas dentro de un hueso dado, no se produce ninguna envolvente para ese par.

### Qué hace el runtime

1. Carga el conjunto horneado.
2. Crea un objeto hijo oculto por envolvente bajo el hueso correcto, con un transform
   identidad, y asigna un `MeshCollider` convexo.
3. Construye una tabla de búsqueda `Collider → hull`.
4. Coloca un `Dyc_Relay` en cada `Rigidbody` que posee una envolvente.
5. Configura capas, autocolisión y masa.

Luego se detiene. No hay trabajo en `Update` más allá de un sondeo opcional de triggers y una
comprobación de distancia para el LOD.

<a id="sec-5"></a>
## 5. Instalación y requisitos

- Unity 2022.3 o superior.
- Copia `Assets/NekoDynamicCollision` en tu proyecto. No hay nada que
  configurar; los ensamblados están delimitados por definiciones de ensamblado.
- Dos ensamblados:
  - `Neko.DynamicCollision.Runtime` — el componente, el relay, el sondeo de triggers, las structs
    de eventos y la fachada de integración. Nunca referencia `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — el horneador, las matemáticas de envolventes, la agrupación, el pincel,
    el Gizmo, la comprobación de salud, los presets y la ventana. Plataforma solo-Editor.

<a id="sec-6"></a>
## 6. La ventana del horneador

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Pestaña | Qué hace |
|---|---|
| **Bake** | Origen, modo, precisión, bake/clear/rebuild, estadísticas, cobertura |
| **Paint** | Lista de grupos con recuentos de triángulos pintados, ajustes del pincel, restablecer pose |
| **Gizmo** | Qué dibuja la Scene view y cómo |
| **Parts** | La lista de particiones — hueso, incluir hijos, nombre de evento, multiplicador de daño |
| **Materials** | Grupos de materiales, presets, tabla de comportamiento de pares, auditoría de física del proyecto |
| **Health** | Puntuación sobre 100, cada problema, correcciones con un clic |
| **Settings** | Idioma, diagnóstico, abrir carpeta de horneados, restablecer preferencias |

<a id="sec-7"></a>
## 7. Referencia del menú

Todo vive bajo una única ranura de nivel superior, de modo que instalar más plugins de NekoWorks
nunca ensanche la barra de menús.

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

Los títulos de los menús se localizan al cargarse y al cambiar de idioma; las cadenas
estáticas en inglés de los atributos son el fallback si la API interna de menús de Unity no está
disponible en tu versión.

<a id="sec-8"></a>
## 8. Precisión

Un solo mando, cuatro pasos. Internamente se expande a cuatro valores:

| Precisión | Triángulos por envolvente | Envolventes por parte | Solapamiento de junta | Umbral de peso |
|---|---|---|---|---|
| Gruesa | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fina | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Triángulos por envolvente** limita cuánta geometría puede absorber una envolvente. Más triángulos
  por envolvente significa menos envolventes, más grandes y más holgadas.
- **Envolventes por parte** es el número objetivo de clústeres por partición. Más clústeres
  significa un ajuste más ceñido y más colliders.
- **Solapamiento de junta** infla cada envolvente hacia fuera para que las regiones vecinas se solapen
  en lugar de dejar un hueco. El solapamiento es seguro (nunca se pierde un impacto, y la
  de-duplicación en el mismo fotograma impide eventos dobles); un hueco no lo es.
- **Umbral de peso** descarta los vértices cuyo peso para el hueso dominante está por debajo
  del valor. Más alto significa una envolvente más ceñida y más "rígidamente correcta".

El recuento de vértices de una envolvente nunca puede superar el recuento de vértices únicos del clúster, que
está limitado a 250 — cómodamente por debajo del límite de PhysX de 255. La comprobación de salud marca en rojo
cualquier envolvente por encima de 255.

### 8.1 El modo convexo descompone — más allá de CC y del complejo collider integrado

En modo **convexo** una malla cóncava se corta en piezas convexas siguiendo sus concavidades. Es la misma idea que el complejo collider integrado y las herramientas estilo V-HACD (CC): toma cualquier malla y produce piezas convexas. NDC conserva la amplitud y sube el techo:

| | Complejo collider / CC | NDC |
|---|---|---|
| Cualquier malla | sí | sí |
| Bien optimizado | sí | sí — trabajo de horneado en segundo plano, progreso, cancelación, presupuestos por hueso |
| Las piezas respetan las articulaciones | **no** — puramente geométrico, un hombro puede tragarse un brazo | **sí** — las piezas llevan etiquetas de hueso de un campo de pesos |
| Utilizable en un cuerpo en movimiento | **no** — PhysX rechaza un collider no convexo en un `Rigidbody` no cinemático | **sí** — la salida es una envolvente convexa |
| Coste en runtime | cocción del collider al cargar | cero — horneado en assets |
| Respaldo | — | agrupación espacial, así una malla degenerada igual obtiene un collider |

La ventana Expert informa si las piezas provienen de la **descomposición** (cortadas por las concavidades) o de la **agrupación espacial** (el respaldo), así que la diferencia es un número y no una conjetura.

**Cuán fino es el corte** lo controla un solo deslizador — **Decomposition detail** —, con el tamaño de vóxel en milímetros en el campo numérico justo debajo. Los dos son dos vistas de *un* número, así que siempre coinciden: arrastra el deslizador y el campo lo sigue, escribe en el campo y el deslizador se mueve. No hay un segundo ajuste que mantener sincronizado.

- **Izquierda** — un vóxel grueso: menos piezas y más grandes. Lo más barato, y normalmente suficiente para un props.
- **Derecha** — el vóxel más fino. Las piezas siguen la superficie, así que el coste **iguala al modo no convexo**: no hay nada más fino que ganar, solo coste.

La tabla de precisión de arriba se aplica entonces al *ajuste de clústeres* — cuán ceñida queda cada pieza —, no a cuántos colliders obtienes.

El mismo deslizador aparece en ambos modos: la descomposición convexa y la simplificación no convexa son las dos formas de responder a la misma pregunta: *cuánto detalle quiero*.

### 8.2 El detalle no convexo es un solo deslizador

Cambia **Collider shape → Non-convex surface** y aparece un deslizador **Surface detail**.

| Deslizador | Resultado |
|---|---|
| Todo a la izquierda | Superficie muy simplificada — pocos triángulos, facetas visibles |
| En medio | Un buen equilibrio: la forma se lee bien, el collider sigue siendo barato |
| **Todo a la derecha** | **Ninguna simplificación** — la superficie se toma de la malla tal cual |

La razón de que sea un deslizador y no un número de triángulos: "cuántos triángulos por pieza" no se puede elegir con criterio sin saber cuántos tiene la malla — 500 es grueso para un torso y preciso para un dedo. El deslizador responde a la única pregunta que un usuario puede plantear de verdad: *cuánto me importa la forma exacta*. La posición de la derecha no es "casi exacta", es exacta: la simplificación se apaga por completo.

Los clústeres pequeños nunca se simplifican, esté donde esté el deslizador — arrastrar un dedo fino a una rejilla gruesa lo colapsa a nada, y un collider vacío es peor que uno caro.

Tanto el modo Skin como el modo Mesh usan el mismo deslizador.

<a id="sec-9"></a>
## 9. El pincel

El pincel no "excluye" caras. Las **etiqueta**, y las etiquetas guían el horneado.

| Acción | Efecto |
|---|---|
| Arrastrar con el botón izquierdo | Asigna el grupo actual a los triángulos bajo el cursor |
| Shift + arrastrar | Borra volviendo al grupo 0 y limpia el flag de pintado |
| Rueda del ratón | Radio del pincel |
| Conmutador `X` (ventana) | Refleja cada trazo respecto al X local = 0 del objeto |

Ajustes: radio, X-Ray (ignorar triángulos orientados hacia atrás), espejo en X.

Requisitos, aplicados con un mensaje explícito en lugar de un fallo silencioso:

1. El modo Play debe estar detenido.
2. La ventana Animation no debe estar en previsualización.

El rig **no** necesita estar en pose de bind. El pincel lanza raycasts contra la malla
en su pose **actual**; como el skinning nunca cambia la topología, los índices de triángulo
se corresponden uno a uno y las etiquetas siguen siendo correctas.

Si aun así quieres el rig en pose de bind, el botón **Reset to bind pose** resuelve las
transformaciones locales a partir de `bindposes[i].inverse` y las reescribe, con undo.

<a id="sec-10"></a>
## 10. Grupos de materiales y presets

Cada grupo lleva un `PhysicMaterial`, una densidad en kg/m³, un nombre de evento opcional
y un multiplicador de daño.

**Presets.** 224 presets en 14 categorías (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — véase [Apéndice A](#appendix-a-physic-material-presets).
Aplicar un preset crea un activo `.physicMaterial` real en `Baked/<Scene>/Materials/`,
así que se puede referenciar, comparar, meter en Addressables y entregar a un artista.

**La forja de materiales.** Una fila de la tabla responde «de qué está hecho»; la forja
responde «qué es ahora mismo». Multiplica un preset base por un **estado de superficie**
(Dry, Wet, Oiled, Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn,
Charred, Clothed, Armoured). `Dry` es la identidad, por eso nunca hay un `steel_dry`
junto a un `steel`. La fricción se multiplica en el coeficiente estático y el dinámico.

**Las partes del cuerpo se derivan, no se escriben.** «Partes del cuerpo» y «Tejidos y
órganos» no son filas de la tabla: se calculan de una mezcla de tejidos — la densidad es
aditiva, la suavidad es aditiva más un término de acolchado, y la fricción y el rebote
siguen a la suavidad. «Pecho» es 80 % grasa + 10 % músculo + 10 % piel; «cráneo» es
95 % hueso + 5 % piel.

**Generar activos.** *Generar activos de variantes* escribe un `.physicMaterial` por
estado en `Baked/<Scene>/Materials/`.

**Estrategia de combinación.** Toda la biblioteca usa `Multiply` para la fricción y `Maximum`
para el rebote. La prioridad de combinación de Unity es
`Average < Minimum < Multiply < Maximum`, así que con esta estrategia cualquier superficie resbaladiza
domina el resultado de la fricción y cualquier material rebotante domina el resultado del rebote —
que es lo que la gente espera intuitivamente.

**Tabla de comportamiento de pares.** La pestaña Materials resuelve cada par de grupos de tu
proyecto usando las reglas de prioridad reales de Unity y muestra el valor que se aplicará
realmente, más un veredicto en lenguaje llano ("agarra / sin rebote"). Esta es la forma más rápida
de responder a "por qué mi hielo no resbala".

**Asignación automática por nombre.** Rellena cada grupo a partir de presets haciendo coincidir el nombre del grupo
con palabras clave en inglés, chino y ruso.

**Limitación honesta.** Un `PhysicMaterial` tiene cuatro números y dos modos de combinación. No
puede expresar fricción de rodadura, fricción anisotrópica, viscosidad, deformación
plástica, temperatura o desgaste. "Parámetros del mundo real" aquí significa una tabla de consulta
con fuentes y presets utilizables — no una simulación física.

<a id="sec-11"></a>
## 11. Diagnóstico de cobertura

La pestaña Bake responde a la pregunta que normalmente es una incógnita: **¿qué triángulos no
tienen ninguna envolvente?**

Toma la malla de origen en pose de bind y comprueba el centroide de cada triángulo contra los planos
de cada envolvente, informando:

- un porcentaje global y una barra de progreso;
- un desglose por partición;
- la lista de triángulos no cubiertos, dibujable en la Scene view en rojo
  (**Show uncovered faces**).

Considera un valor por debajo de ~95% como un problema: sube la precisión, o comprueba que los huesos de las particiones
cubran realmente todo el esqueleto.

<a id="sec-12"></a>
## 12. Salud de colisiones

Una puntuación sobre 100 con cada problema listado y, cuando es posible, una corrección con un clic.

Las comprobaciones incluyen: nada horneado; envolvente por encima del techo de vértices de PhysX; envolventes cerca
del techo; clústeres degenerados; triángulos que no pertenecen a ninguna partición; la malla de origen
cambiada desde el último horneado; grupos de materiales sin material, sin envolventes o con demasiadas
envolventes fragmentadas; un rol Hitbox/Trigger sin capas de interacción; ningún `Rigidbody` en la
cadena de padres; masa del rigidbody demasiado pequeña o demasiado grande; autocolisión del ragdoll
totalmente activada; eventos despachados sin ningún listener; y las comprobaciones de física a nivel de proyecto del
[Apéndice B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Eventos e integración

Cada evento lleva un contexto completo, así que nunca tienes que volver a buscar nada:

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

Tres formas de consumirlo:

1. **UnityEvent** — `onEvent` en el componente, para listeners registrados por código.
2. **Registro de cadenas** — da a una partición o a un grupo un nombre de evento y escucha con
   `Dyc_Events.Register("Hit.Head", handler)`. Los nombres mal escritos no provocan error, pero
   la comprobación de salud informa de despachos que nadie recibió.
3. **Fachada estática** — `Dyc_Api` para herramientas externas:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Los multiplicadores de daño viven en la partición y en el grupo y se multiplican entre sí —
cabeza ×4 es un número, no una capa de código pegamento.

**De-duplicación.** El solapamiento de junta significa que dos grupos adyacentes pueden tocar el mismo
collider ajeno en un fotograma. NDC despacha como máximo un evento por
`(partición, otro collider)` por fotograma, así que los materiales particionados no duplican eventos.

<a id="sec-14"></a>
## 14. Sondeo de Trigger

Unity entrega los callbacks de trigger **por par de Rigidbody**, así que un solo ragdoll es un
único par de rigidbody y la capa de física simplemente no puede decirte qué hueso entró en un
volumen. Dar a cada hueso su propio `Rigidbody` destruiría la promesa de coste cero.

Así que los triggers particionados se muestrean en su lugar:

- Cada partición se prueba con `Physics.OverlapBoxNonAlloc` sobre los bounds en espacio de mundo
  de sus colliders.
- Solo se consideran los triggers reales, y nunca tus propios colliders.
- Las particiones se procesan en porciones: `elements / frames-per-pass` por fotograma.
- La entrada y la salida se comparan con el paso anterior para cada partición.

**Semántica que recordar:** esto es muestreo, no un evento. Un paso muy rápido puede
pasarse por alto. Sube la frecuencia de muestreo, o usa el **sweep margin** para expandir la caja de consulta.

<a id="sec-15"></a>
## 15. Masa, autocolisión y LOD

**Masa a partir de la densidad.** NDC conoce el volumen de cada envolvente, así que puede calcular la masa correctamente:
`mass = hull volume × group density`, opcionalmente normalizada para que todo el personaje
coincida con una masa total objetivo. Esto elimina la más antigua tarea de ajuste manual en los
ragdolls de Unity. Un único rigidbody recibe la suma de los volúmenes de las envolventes que posee.

**Autocolisión.** `Ignore` (todos los pares), `Adjacent` (misma partición, o ancestro y
descendiente) u `On`. Que los huesos del ragdoll colisionen entre sí es una fuente común de
jitter, y `Adjacent` es la respuesta habitual. Por encima de 200 colliders el paso se omite
con una advertencia en lugar de bloquear `Awake`.

**LOD.** `Disable` desactiva los colliders más allá de una distancia; `Reduce` conserva solo
la envolvente más grande por partición. La comprobación se ejecuta cada cuatro fotogramas.

**Rigidbody.** Unity solo entrega los callbacks de colisión al GameObject que posee el
`Rigidbody`. Para un ragdoll, cada hueso ya tiene uno. Para cualquier otra cosa, activa
**Auto-add Rigidbody** y NDC crea uno cinemático en el objeto del componente.

<a id="sec-16"></a>
## 16. Localización

La ventana, el inspector, los mensajes de salud y los títulos de los menús están localizados
en **15 idiomas**:

`en` (integrado) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- El inglés está integrado en el ensamblado y es el fallback para cualquier clave ausente, así que un
  idioma parcialmente traducido se degrada en lugar de romperse.
- Todos los demás idiomas son datos puros en `Locale/<code>/strings.json` — añadir uno
  no requiere recompilar.
- El árabe y el hebreo son totalmente de derecha a izquierda: el diseño se refleja en lugar de depender
  de `style.direction`, cuyo soporte en UI Toolkit es incompleto y depende de la versión.
- Cambia el idioma en la pestaña **Settings**. La ventana y el menú se actualizan
  inmediatamente, sin domain reload.
- La pestaña Settings también muestra la ruta de locale resuelta y cuántos idiomas se
  encontraron, así que un error de empaquetado es visible en lugar de silencioso.

<a id="sec-17"></a>
## 17. Estructura de directorios

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
## 18. Desinstalación

1. Elimina el componente **Dynamic Collision** de tus prefabs y escenas.
2. Borra `Assets/NekoDynamicCollision`.

Los assets horneados viven bajo `Baked/` dentro de la carpeta del plugin y se van con ella. No
se escribe nada fuera de la carpeta del plugin, y el runtime no contiene código que dependa
de la mitad de editor.

<a id="sec-19"></a>
## 19. Solución de problemas y FAQ

**No colisiona nada, y no se dispara ningún evento.**
No hay ningún `Rigidbody` en la cadena de padres. Unity solo envía callbacks de colisión
al objeto que posee el rigidbody. Activa **Auto-add Rigidbody**, o añade uno tú mismo.

**Las envolventes no coinciden con lo que veo.**
El Gizmo dibuja la **pose de bind** por defecto — eso es lo que se horneó. Desactiva
**Bind pose** en la pestaña Gizmo para verlas en la pose actual.

**"La envolvente tiene N vértices, por encima del límite de PhysX de 255."**
Unity ignora silenciosamente una envolvente convexa por encima del límite. Baja la precisión un paso; la
comprobación de salud ofrece exactamente eso como corrección con un clic.

**Se pierden impactos en algunos sitios.**
Comprueba primero el porcentaje de cobertura. Por debajo de ~95% significa agujeros reales. Luego comprueba el
**solapamiento de junta** para el paso de precisión en el que estás.

**Un trazo de pintura dejó un hueco entre dos regiones.**
Ese es el problema de la junta. Sube la precisión (que reduce el solapamiento de junta) o pinta un
poco más allá del límite. La de-duplicación en el mismo fotograma ya impide eventos dobles
por el solapamiento.

**Los eventos se disparan dos veces por un solo impacto.**
Se golpearon dos particiones distintas en el mismo fotograma, lo cual es legítimo. Si de verdad
quieres un evento por par de objetos, filtra por `elementIndex` en tu handler.

**He pintado pero nada cambió tras el horneado.**
Las etiquetas se ignoran cuando el recuento de triángulos no coincide con la malla de origen —
normalmente tras un re-import o un cambio de topología. Vuelve a pintar, o hornea primero para que el asset
de etiquetas se cree con el tamaño correcto.

**El pincel no arranca.**
El modo Play está en ejecución, o la ventana Animation está en previsualización. Ambos se muestran como
motivo explícito en la pestaña Paint.

**Mi trabajo antiguo con el pincel desapareció tras actualizar.**
No debería: las máscaras creadas antes de que existiera el flag de pintado se migran, y cualquier
etiqueta distinta de cero se trata como pintada. Si se limpió una máscara, vuelve a pintar y a hornear.

**¿El coste en runtime es realmente cero?**
En estado estable, sí: las envolventes son assets, los transforms los sigue la jerarquía, y
no hay ningún trabajo de malla. El único trabajo por fotograma es el sondeo opcional de triggers
y la comprobación de distancia del LOD.

**¿Puedo tener dos componentes Dynamic Collision en un objeto?**
No, y está bloqueado a propósito. Dos componentes crearían envolventes duplicadas sobre las
mismas caras, duplicarían los contactos y duplicarían los eventos. Particiona con particiones y
grupos de materiales en su lugar.

<a id="sec-20"></a>
## 20. Contacto

NekoAndreeva — consulta `package.json` para la URL del repositorio.

---

<a id="sec-appA"></a>
## 21. Uso ordinario y paridad con RASCAL

### 21.1 NDC como collider de sistema

La regla de diseño es que un desarrollador que conoce `Collider` y `Rigidbody` ya conoce NDC, porque NDC *crea* colliders ordinarios: `MeshCollider` en hijos ocultos, un `Rigidbody` en el objeto, mensajes estándar, capas ordinarias y materiales físicos. `Physics.Raycast` y `Physics.OverlapSphere` no necesitan ningún cambio.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // añadir + construir
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // un mensaje estándar de Unity
void OnTriggerStay(Collider other) { }     // un mensaje estándar de Unity
```

| Llamada | Significado |
|---|---|
| `Find(go)` | El componente, en el objeto o en un padre |
| `Attach(go, generateNow)` | Añadir el componente y construir |
| `Build(go)` / `Rebuild(go)` | Construir desde el conjunto horneado, o generar en runtime |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Estado, todas las envolventes a la vez |
| `SetTrigger(go, v)` | `Collider.isTrigger` para cada envolvente |
| `SetMaterial(go, pm)` | Inmediato; no sobrevive a una reconstrucción |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Las envolventes, como `Collider` ordinarios |
| `SetReceiver(go, t)` | Enviar también mensajes estándar a `t` |

**Solo la zonificación es extra.** Las zonas, los materiales pintados, el LOD, los eventos, la masa a partir de la densidad y la comprobación de salud necesitan la API de NDC — son las cosas que un collider de sistema no puede hacer.

**Sin paso de horneado.** Marca **Advanced ▸ Build at startup when nothing is baked** (o llama a `Attach`). Los colliders se construyen por hueso a partir de la malla en `Awake` — una envolvente convexa por hueso, como el ajuste por defecto de RASCAL. Hornear sigue siendo la vía para obtener zonas, descomposición, cobertura y precisión.

**Mensajes que llegan a tu script.** Unity entrega `OnCollision*` al objeto con el `Rigidbody`. Si tu script está en otro sitio (una raíz de personaje mientras el Rigidbody está en un hueso), configura `Advanced ▸ Also send OnCollision*/OnTrigger* to` — los mensajes se reenvían entonces con `SendMessage`, que no cuesta nada en fotogramas sin colisión.

### 21.2 Live update — la capacidad que el horneado no puede sustituir

Una envolvente horneada pegada a un hueso es exacta en la pose de bind y rígida después. Bajo una deformación fuerte — una flexión, un miembro apretado, tela tensada — la envolvente informa mal de la superficie. Live update reconstruye la envolvente a partir de la pose de skinning **actual**.

Actívalo con **Advanced ▸ Live update** o `Dyc_Collision.EnableLiveUpdate(go)`.

| Ajuste | Por defecto | Significado |
|---|---|---|
| `liveUpdate` | off | Reconstruir las envolventes desde la pose actual |
| `liveUpdateContinuous` | on | Seguir, o ejecutar una pasada a petición |
| `idleCpuBudgetMs` | 0.2 | Presupuesto mientras la malla apenas se mueve |
| `activeCpuBudgetMs` | 1.0 | Presupuesto mientras se mueve rápido |
| `meshUpdateThreshold` | 0.02 | Saltar la pasada por debajo de este movimiento (metros) |
| `maxColliderTriangles` | 5000 | Techo por collider, para que un hueso pesado no se coma el presupuesto |

El presupuesto se elige según cuánto se movió realmente la malla, así que a un personaje de pie se le cobra la tarifa idle y a uno corriendo la activa. El trabajo que no cabe se aplaza al siguiente fotograma, y `OnUpdateYield` / `OnPassComplete` informan de los milisegundos transcurridos.

**No reconstruye todo cada fotograma.** Tres mecanismos mantienen el coste predecible:

1. **Incremental.** El centro de cada clúster se compara con la pasada anterior, y solo se reconstruyen los clústeres que se movieron de verdad. Un cuerpo blando colgando de un ancla tiene un borde tembloroso y un centro casi quieto — el centro no cuesta nada.
2. **Ordenado por prioridad.** La cola se ordena por cuánto se movió cada clúster. Si el presupuesto se agota, se agota en los clústeres más calmados — aquellos donde la inexactitud se ve menos. Sin esto, el presupuesto se gastaría en lo que diera la casualidad de ir primero en la lista.
3. **Presupuestado por el reloj**, no por el número de clústeres: el coste por fotograma no crece con cuántos clústeres tenga el cuerpo.

`LastDirtyCount` y `LastBuiltCount` informan de lo que hizo realmente la última pasada, que es la forma honesta de ver el ahorro.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // terminar la pasada actual y luego parar
live.UpdateNow();              // una pasada completa, fuera del presupuesto
```

**Requisitos.** Las envolventes necesitan `sourceVertices`, que escribe el horneado; vuelve a hornear un personaje antiguo para activar live update. El coste en runtime es real — es la única función que contradice "cero coste por fotograma", que es justo por lo que está desactivada por defecto.

### 21.3 Anulaciones por hueso

`Dyc_BoneProperties` va en el propio hueso (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Campo | Efecto |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Las envolventes de este hueso usan ese material |
| `overrideConvex` + `convex` | Envolvente en vez de superficie (o al revés) para este hueso |
| `overrideWeightThreshold` + `boneWeightThreshold` | Corte de peso por hueso |
| `exclude` | Sin colliders para este hueso |

Colgarlo del hueso significa que sobrevive a los renombrados — guarda una referencia, no una ruta.

### 21.4 Materiales por material de origen

`Advanced ▸ Materials by source material` asigna un `Material` de origen a un `PhysicMaterial`. Las envolventes se atribuyen por la submalla de la que provienen mayoritariamente, resuelta al hornear en `Dyc_BakedSet.sourceMaterials`. Prioridad, de mayor a menor:

1. `Dyc_BoneProperties.physicsMaterial`;
2. la asociación de material para el material de origen de la envolvente;
3. el material del grupo pintado.

### 21.5 Mapa de exclusión de vértices

`Advanced ▸ Exclusion map` lee un canal de la textura (R/G/B/A, con un umbral) y excluye los vértices cuyo valor de canal está en o por encima de él. El pincel marca *caras*, el mapa marca *vértices* — se complementan. La malla necesita UVs, la textura necesita **Read/Write Enabled**, y un triángulo se excluye solo cuando lo están sus tres vértices.

### 21.6 Reorientar el esqueleto

`Advanced ▸ Attach hulls to another skeleton` construye las envolventes a partir de esta malla pero las cuelga de huesos con el mismo nombre de otra raíz — el caso `RetargetSkeleton`, para Puppet Master y montajes similares. Los huesos se resuelven por ruta relativa; una envolvente sin homónimo se queda en su propio esqueleto y el informe de horneado dice cuántas.

### 21.7 Modo soft — sin huesos, dirigido por código o por un solver

El modo soft (`Mode → Soft`) **no** es un rig con skinning. Es para una malla **sin esqueleto** cuya forma produce un solver o el código — NekoDynamicSoftbody y similares. Nada en el modo soft lee huesos; la geometría de la malla se toma tal cual.

**Qué produce el horneado.** La malla se divide en clústeres espaciales numerados. Cada envolvente se construye *relativa al centro de su clúster*, y el centro se guarda como la pose de reposo del clúster (`clusterRest`). Eso es lo que permite a un frame trasladar **y rotar** la envolvente como una sola pieza.

**Sin solver.** Los frames se crean en sus poses de reposo, así que las envolventes se asientan exactamente sobre la geometría real de la malla y se mueven con el objeto. La forma es correcta; simplemente no hay dinámica. Esta es la degradación prevista, no un fallo — y es lo que significa "calcula la forma real sin NDSC".

**Con solver.** El solver empuja los frames (`Push` → `Apply`) y toma el control por completo, dando simulación completa. Un push direccionable no es sobrescrito por el sondeo global en el mismo fotograma, lo que importa en cuanto existe más de un cuerpo.

**Live update también funciona sin esqueleto.** `Dyc_LiveUpdate` lee los vértices de CPU de un `MeshFilter` tal cual, así que cualquier código que deforme la malla — un solver de cuerpo blando, un script procedural, un deformador propio — impulsa envolventes precisas sin pegamento específico del plugin. Una envolvente necesita `sourceVertices`, que escribe el horneado; vuelve a hornear un asset antiguo.

**El límite honesto:** la deformación que existe solo en la GPU (un vertex shader, skinning en GPU) no puede leerse de vuelta en la CPU, así que live update no la ve. Lleva la deformación a la CPU, o quédate con las envolventes horneadas.

### 21.8 Dirigir envolventes individuales

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` y `Groups` son públicos, así que herramientas externas pueden iterar y dirigir envolventes individuales sin reflexión.

---

## Apéndice A. Presets de materiales físicos

224 presets, 14 categorías. Los valores son aproximaciones de ingeniería con fuentes, mapeadas sobre el modelo de cuatro parámetros de Unity. «Partes del cuerpo» y «Tejidos y órganos» los deriva la forja de una mezcla de tejidos en vez de escribirse en la tabla.

| Categoría | Cantidad | Presets |
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


Cada preset también lleva una **densidad** en kg/m³ para la masa automática, y los presets de la familia
del caucho llevan el `bounceThreshold` que necesitan para rebotar siquiera.

<a id="sec-appB"></a>
## Apéndice B. Comprobaciones de física del proyecto

La comprobación de salud audita los ajustes `Physics` del proyecto, porque un preset de material
no puede corregir un ajuste global:

| Ajuste | Por qué importa |
|---|---|
| `bounceThreshold` | Los impactos más lentos que esto nunca rebotan. En el valor predeterminado de Unity de 2, un preset de caucho parece roto. Bájalo a 0.2–0.5 para usar materiales elásticos. |
| `defaultSolverVelocityIterations` | En 1, las pilas y los impactos rápidos vibran o atraviesan. 2–4 suele ser mejor, y es una causa raíz común del jitter del ragdoll. |
| `gravity` | Si no es −9.81, toda intuición sobre masa e impulso derivada de −9.81 está desviada por el mismo factor, y los presets de densidad necesitan corrección. |
| `defaultContactOffset` | Un hueco de contacto amplio hace que los objetos finos parezcan flotar. |

Aplicar los valores recomendados es una acción de un clic desde la pestaña Materials o desde la
comprobación de salud.
