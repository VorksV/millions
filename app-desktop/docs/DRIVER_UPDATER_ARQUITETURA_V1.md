# DRIVER UPDATER — ESTUDO TÉCNICO E ARQUITETURA V1

**Projeto:** VOLTRIS Optimizer
**Documento:** estudo de viabilidade e arquitetura (fase de pesquisa — sem implementação)
**Base:** pesquisa pública + medição empírica na máquina de desenvolvimento

> **Convenção de rigor probatório usada em todo o documento**
> - **[FATO]** — confirmado por fonte oficial citada ou medido nesta máquina.
> - **[INFERÊNCIA]** — dedução técnica sólida a partir de fatos, não verificada diretamente.
> - **[NÃO CONFIRMADO]** — não foi possível confirmar; não afirmar.

### Controle de revisões

| Rev | Data | Mudança |
|---|---|---|
| 1 | hoje | Emissão inicial do estudo. |
| 2 | rev. 2 | hoje | **Verificação adversarial das afirmações de carga.** Três correções: (a) §1.4 — o tamanho de 32 MB deixou de ser inferência e passou a ser fato medido nas páginas oficiais; (b) §3.5 e §3.7 — leitura integral das fontes Microsoft, que são substancialmente mais específicas do que a versão 1 supunha; (c) §5.1.1 — **achado novo**: `packageVersion` ≠ `driverVersion`, documentado pela Intel, que **resolve** a discrepância antes marcada como [NÃO CONFIRMADO]. |
| 3 | hoje | **Descoberta em execução: `ExcludeWUDriversInQualityUpdate=1` nesta máquina.** Ver §3.8. Muda a conclusão sobre a fonte nº 1 e valida empiricamente a arquitetura em camadas. |

> **Aviso ao leitor:** onde a rev. 2 substituiu a rev. 1, a seção foi reescrita e marcada.
> Nenhuma afirmação foi removida de forma capciosa — as inferências superadas foram
> **reclassificadas**, não apagadas.

---

## SUMÁRIO EXECUTIVO

A pergunta feita foi: *"por que o Driver Booster encontra as atualizações de Intel e o VOLTRIS não?"*

A resposta é **arquitetural**, e está medida:

| | Driver Booster | VOLTRIS (implementação atual) |
|---|---|---|
| Modelo | **Catálogo primeiro** — banco `HWID → pacote`, consulta local | **Scraping primeiro** — lê o site do fabricante ao vivo |
| Fonte | Catálogo próprio + servidores próprios | `intel.com` em tempo real |
| Resultado medido | Encontra Intel Wi-Fi/BT | **HTTP 403 em 39/39 requisições** |

**A descoberta mais importante deste estudo:** a fonte correta, verificável e gratuitamente disponível — o **Microsoft Update Catalog** — **funciona** e foi medida retornando dados reais e corretos para esta máquina. O VOLTRIS jamais a consultou de forma aproveitável.

Medição real (seção 13.2):

| Dispositivo | Versão instalada | Versão no Catálogo Microsoft | Veredito |
|---|---|---|---|
| Intel Wi-Fi `DEV_A0F0` | 24.60.0.3 | 24.60.0.3 | **já atualizado** |
| Intel Bluetooth `PID_0AAA` | 24.40.11.1 | 24.40.11.1 | **já atualizado** |
| Intel Iris Xe `DEV_9A49` | 32.0.101.7026 | **32.0.101.7088** | **atualização real existe** |
| Realtek Áudio `DEV_0256` | 6.0.9175.1 | **6.0.9752.1** | **atualização real existe** |

Ou seja: a implementação anterior **fabricou** "Realtek 11.16.2.1" (que é a versão de *Ethernet*, não de Áudio — nem era o mesmo componente) e **deixou passar** duas atualizações reais de Iris Xe e Realtek Áudio.

**Recomendação:** arquitetura híbrida com catálogo próprio de metadados (Opção A) + fontes oficiais diretas (Opção C), usando o Windows Update Agent como canal primário. Detalhamento na seção 9 e 12.

---

# 1. ESTUDO DO DRIVER BOOSTER (IObit)

## 1.1 O que é FATO CONFIRMADO (fontes oficiais IObit)

Segundo o material público da própria IObit:

| Declaração oficial | Fonte |
|---|---|
| "database of 3,500,000+ drivers" (v7, 2019) | `iobit.com/en/pressroom-driver-booster-7...` |
| "18,000,000+ verified drivers" (PRO v13) | `iobit.com/en/driver-booster-proanni.php` |
| "**online** driver database" | `iobitsoftware.com/db.php` |
| "All downloaded drivers come from the **official manufacturers' websites**" | pressroom v7 |
| "only includes drivers that pass Microsoft's rigorous **WHQL** test" | `features-best-driver-updater-37.php` |
| "downloading process is **HTTPS** secured" | idem |
| "**Back ups driver automatically and creates a restore point** before updating" | `iobitsoftware.com/db.php` |
| "**Deletes useless files** in installation package and **compresses** installation package" | idem |
| "**offline scan mode**" / "offline driver installation" | app store, GitHub |
| "Priority to update **Game Ready** Driver" | `iobitsoftware.com/db.php` |
| Categorias: "graphics card, audio, network, chipset, external devices, game components, others" | pressroom v6 |
| "Unlock driver update speed limit" | `iobitsoftware.com/db.php` |
| "driver details like the **versions, the date of release, sizes** are also offered" | features page |

## 1.2 O que é INFERÊNCIA TÉCNICA

| Inferência | Base |
|---|---|
| O "online driver database" é um **servidor da IObit**, não um bundle local | **Agora confirmado pela aritmética (§1.4):** 18M de drivers declarados vs. instalador de 31,9 MB. Impossível local. |
| O banco é indexado por **Hardware ID**, não por nome de produto | É a única chave que permite correspondência exata e é a única que o Windows expõe de forma confiável (§3.1) |
| A curadoria é **humana/assistida**, não 100% automática | "passed both WHQL and **IObit test**" — declaração de teste próprio indica triagem editorial |
| "Unlock speed limit" = **throttling de banda** no plano Free | Linguagem comercial padrão do setor; **[INFERÊNCIA]**, não há documentação pública do limite |
| "compressed package" = eles **reempacotam** o INF/CAB removendo arquivos desnecessários | Declarado explicitamente ("deletes useless files... and distinctly compresses installation package"); confirmado como slogan de produto no changelog |
| Há **código de afiliação/URL direta** por trás dos downloads | Padrão do setor; **[NÃO CONFIRMADO]** |

## 1.2b Novas declarações oficiais colhidas em 2026 (verificadas)

| Declaração oficial | Fonte |
|---|---|
| "**18,000,000+** PC drivers" — Installer **31,9 MB** | `iobit.com/en/recommend/dbfree.php` |
| "**Larger database to update more outdated & rare drivers**" | `iobitsoftware.com/db.php` |
| "**Automatically scan & identify** outdated, missing & faulty drivers" | `iobitsoftware.com/db.php` |
| "**Auto download, install and update drivers during system idle time**" | `iobitsoftware.com/db.php` |
| "**Automatically backup all drivers for safe restore**" | `iobitsoftware.com/db.php` |
| "**Smart Installation Mode**" — instala durante jogos/tela cheia sem interromper | changelog (TechSpot) |
| "**Small size VS big space** ... sharply reduced driver installation packages" | changelog (TechSpot) |
| "**Fast servers and clean downloads**" | changelog (TechSpot) |
| Versão atual: **13.6.0** (existe também 14.0.0.444, ago/2026) | sites oficiais + TechSpot |

> **Correção de leitura importante:** a IObit diz "**backup** all drivers for **safe restore**".
> O adjectives é *safe*, não *automatic*. O **backup** é automático; a **restauração** é
> uma ação do usuário. Isto **corrobora a §6.2**: mesmo o líder de mercado não promete
> rollback automático. É um argumento forte contra a alegação anterior do VOLTRIS de
> "Rollback Inerente".

## 1.3 O que é NÃO CONFIRMADO

- **[NÃO CONFIRMADO]** Formato do banco (schema, versionamento, particionamento).
- **[NÃO CONFIRMADO]** Se o banco contém os binários ou apenas metadados + URL.
- **[NÃO CONFIRMADO]** Quais-headedges de catálogo vs. quais manufacturer-direct.
- **[NÃO CONFIRMADO]** Algoritmo exato de ranking/seleção de versão.
- **[NÃO CONFIRMADO]** Relação contratual com fabricantes.

## 1.4 Como o DB resolve a escala de "milhões de drivers"

A pergunta do briefing: *"como oferece milhões de drivers sem armazená-los no executável?"*

Resposta: **separando metadados de artefato**.

```
[exe do cliente]  31,9 MB   → só engine + UI (VERSÃO CORRIGIDA/VERIFICADA)
[catálogo remoto] ~metadata (KB por entrada) → HWID, versão, URL, tamanho, hash, data
[artefato]       10–700 MB por driver → baixado do fabricante, sob demanda
```

**Tamanho do instalador — medido nas fontes oficiais (2026):**

| Versão | Tamanho | Fonte |
|---|---|---|
| Driver Booster **13.6.0** (atual) | **31,9 MB** | `iobit.com/en/driver-booster.php` — `V 13.6.0 \| 31.9 MB` |
| Driver Booster 13.4.0 | 31,8 MB | `iobit.com/en/features-game-drivers-and-components-updater-40.php` |
| Driver Booster 12.4.0.571 | 31 MB | MajorGeeks |
| Driver Booster 12 PRO 12.6.0.620 | 35,5 MB | 4download |
| `driver_booster_setup.exe` 13.6.0 | 31,92 MB | Sooftware (VirusTotal: 0/69) |

**O argumento fecha com aritmética, não com suposição:** a página oficial da IObit anuncia
**"18,000,000+ PC drivers"** ao lado de **"31.9 MB"** — na mesma frase de marketing.

> **18 milhões de drivers × ~20 MB médio = ~360 TB.** Isso caberia num único data center,
> jamais num instalador de 31,9 MB. **[FATO — prova aritmética]**
> Logo, os pacotes **não** estão no cliente. A IObit declara vir dos sites oficiais — o que
> significa **redirect** para o CDN do fabricante. O instalador é **só o motor**.

**Confirmação independente do mecanismo:** o changelog do TechSpot descreve a estratégia como
*"**Small size VS big space** — More valuable space are saved with small program setup file and
sharply reduced driver installation packages"*. Ou seja, a separação metadados/artefato é
**deliberada e documentada** pela própria IObit, não uma inferência minha.

**Consequência de projeto para o VOLTRIS:** o custo de operação não é armazenamento de drivers; é **curadoria de metadados**. É isso que torna a Opção A (§9) viável.

---

# 2. O CATÁLOGO DE DRIVERS — MODELO CONCEITUAL

## 2.1 Anatomia de uma entrada

Derivada do que o Catálogo Microsoft realmente expõe (medido) e do que um INF exige:

```jsonc
{
  "hwid": "PCI\\VEN_8086&DEV_9A49&SUBSYS_C195144D&REV_01",  // chave primária
  "hwidNoRev": "PCI\\VEN_8086&DEV_9A49&SUBSYS_C195144D",   // chave de fallback
  "compatibleIds": ["PCI\\VEN_8086&DEV_9A49&CC_030000"],   // ids do INF

  "vendor": "Intel Corporation",          // quem assina
  "oem": "Samsung",                        // quem fabricou o PC (≠ vendor!)
  "boardModel": "NP960X5K",               // modelo exato
  "deviceClass": "Display",               // classe de setup
  "title": "Intel(R) Iris(R) Xe Graphics",

  "version": "32.0.101.7088",
  "releaseDate": "2026-06-16",
  "fileSize": 550756352,
  "downloadUrl": "https://.../*.cab",     // artefato real
  "sha256": null,                          // OEM raramente publica
  "infFile": "iigd_dch.inf",
  "architecture": ["x64"],
  "osMin": "10.0.19041", "osMax": null,
  "signatureStatus": "WHCP",              // release | attested | cross-signed
  "rebootRequired": "likely",             // ver §7.5
  "dependencies": ["OpenGL Compatibility Pack"],
  "previousVersions": ["32.0.101.7026"],
  "source": 1,                             // proveniência obrigatória
  "sourceRef": "catalog-update-id"
}
```

## 2.2 O ponto crítico: **fabricante de driver ≠ fabricante do computador**

Este é um dos maiores erros possíveis num driver updater, e é **[FATO]** na plataforma:

```
   PCI\VEN_8086&DEV_9A49&SUBSYS_C195144D
   │          │        │        └── SUBSYS = Samsung (NP960X5K)
   │          │        └─────────── quem DEVE homologar (validar a placa)
   │          └──────────────────── quem ASSINA (assinatura/estabilidade)
   └─────────────────────────────── o silicon
```

Mesmo `VEN_8086&DEV_9A49` tem **variantes de-subsystem distintas** com drivers diferentes. Um driver genérico funciona; o *otimizado* para o subsistema é outro. **[INFERÊNCIA]**, consistente com o que a própria Microsoft publica no Catálogo.

**Consequência para o VOLTRIS:** o catálogo precisa de `SUBSYS` na chave. Casar apenas por `VEN_`+`DEV_` perde precisão e pode oferecer driver de outra placa.

## 2.3 Múltiplas versões e seleção — o problema difícil (§5 detalhado)

O mesmo HWID tem:
- várias branches (Game Ready / Studio / Production / Beta)
- várias linhas (R525, R545…)
- versões "latest hotfix" vs "latest new feature"
- versões de SO diferentes

O Catálogo Microsoft resolve isso **estruturalmente**: cada entrada declara `Products` (ex.: "Windows 11 Client, version 24H2 and later") — **[FATO medido**. É esse campo que substitui heurísticas de versão no matching.

---

# 3. PESQUISA WINDOWS — MECANISMOS DISPONÍVEIS

## 3.1 Detecção de hardware

| API | Uso | Status |
|---|---|---|
| `SetupDiGetClassDevs` / `SetupDiEnumDeviceInfo` | enumeração de nós de dispositivo | **[FATO]** — já usado no VOLTRIS |
| `SetupDiGetDeviceInstanceId` | **InstanceId** = `PCI\VEN_8086&DEV_9A49&SUBSYS_...` | **[FATO]** |
| `SetupDiGetDeviceRegistryProperty(SPDRP_HARDWAREID)` | lista de Hardware IDs | **[FATO]** |
| `CM_Get_DevNode_Status` | `DN_STARTED`, `DN_HAS_PROBLEM` + **código de problema real** (`CM_PROB_*`) | **[FATO]** |
| `CM_Get_Device_ID` | InstanceId por devnode | **[FATO]** |
| DEVPKEY_Device_* (SetupAPI) | `DriverVersion`, `DriverDate`, `DriverProvider`, `HardwareIds` | **[FATO]** — medido via PowerShell |

**Estrutura de um Hardware ID** (PCI): `PCI\VEN_<vendor4>&DEV_<device4>&CC_<classcode>&SUBSYS_<subsys4>`

## 3.2 INF — o verdadeiro manifesto do driver

Um `.inf` é o **contrato de compatibilidade** do driver. Campos que importam:

```inf
[Manufacturer]
%Msft% = Standard, NTamd64.10.0...22621

[Standard.NTamd64]
%Intel.Device%=Install_Device, PCI\VEN_8086&DEV_9A49
%Intel.Device%=Install_Device, PCI\VEN_8086&DEV_9A49&SUBSYS_C195144D   ← mais específico
```

Pontos críticos **[FATO]**:
1. O INF declara **rank** por seção (`Standard`, `Standard.SD`, …). O Windows escolhe o mais específico.
2. O INF pode declarar `CompatibleIDs` —-hardware mais antigo que o driver suporta.
3. **INF não é necessariamente UTF-8.** INFs Windows são frequentemente **UTF-16 com BOM**. Ler como UTF-8 produz NUL intercalados e **nenhum** HWID é encontrado. *(Este bug existia no VOLTRIS e foi corrigido.)*
4. O INF referencia um `.cat` assinado. **A assinatura do cat é o que o Windows exige**, não a do `.inf`.

## 3.3 Driver Store

- `C:\Windows\System32\DriverStore\FileRepository\<oemNN.inf>_<hash>\`
- Fonte de verdade do driver instalado
- `dism /online /export-driver /destination:<dir>` exporta **todo** o DriverStore — **[FATO]**, já usado no VOLTRIS

## 3.4 Métodos de instalação — quando usar cada um

| Método | Quando usar | Cuidado |
|---|---|---|
| **`pnputil /add-driver <inf> /install`** | **Padrão para VOLTRIS.** INF está em disco, já validado | **exige elevação**; se não elevado, falha silenciosamente |
| **`DiInstallDriver` (SetupAPI)** | instalar via caminho completo | alternativa API, sem console |
| **`UpdateDriverForPlugAndPlayDevices` (newdev)** | **forçar** um driver em um devnode específico | `INSTALLFLAG_FORCE` é perigoso — pode associar driver errado |
| **`SetupDiSetSelectedDriver` + `DIF_INSTALLDEVICE`** | simular o Device Manager | mais código, mais controle |
| **`Microsoft.Update.Session` → `IUpdateInstaller`** | instalar o que o WU já baixou | melhor caminho quando a atualização vem do WU |
| **`.exe` do fabricante** | **nunca automático** | instalar em modo silencioso viola termos de uso |
| **`.msi`** | driver empacotado como MSI | raro; verificar assinatura |
| **WUA `DownloadContents`** | baixar via Windows Update | **não dá URL direta** — baixa pelo cache do WU (§4.1) |

## 3.5 Windows Update Agent (WUA) — o canal programático legítimo

**Este é o ponto mais importante do estudo.** **[FATO]** — Microsoft Learn, `IUpdateSearcher::Search`.

### Tabela completa de critérios (extraída da documentação oficial)

| Critério | Tipo | Significado oficial |
|---|---|---|
| `Type` | string | `'Driver'`, `'Software'` |
| **`IsAssigned`** | int(bool) | **"Finds updates that are intended for deployment by Automatic Updates." `IsAssigned=1` → *"**At most, one assigned Windows-based driver update is returned for each local device**"* |
| `IsInstalled` | int(bool) | instalado / não instalado no destino |
| `IsHidden` | int(bool) | **atualizações ocultas** — ver armadilha abaixo |
| `BrowseOnly` | int(bool) | `1` = opcionais |
| `AutoSelectOnWebSites` | int(bool) | marcadas para seleção automática |
| `IsPresent` | int(bool) | presentes para um produto |
| `RebootRequired` | int(bool) | exigem reinício |
| `DeploymentAction` | string | `Installation` / `Uninstallation` (padrão implícito em `AND`) |
| `CategoryIDs` | uuid | `contains` |
| `UpdateID` / `RevisionNumber` | UUID / int | query pontual por identidade |

> **`IsAssigned=1` é a chave, e a Microsoft confirma textual e explicitamente:**
> *"At most, one assigned Windows-based driver update is returned for each local device on a
> destination computer."*
> O Windows **já fez o matching por HWID** e **devolve no máximo um por dispositivo**.
> Zero heurística necessária. A limitação a 1/dispositivo é uma **vantagem**, não um bug:
> elimina por construção o falso positivo.

### A armadilha do `IsHidden` — por que a busca pode devolver 0

**[FATO, oficial]** Os critérios padrão de uma busca são:

```sql
( IsInstalled = 0 and IsHidden = 0 )
```

E `UpdateSearcher.IncludePotentiallySupersededUpdates` tem padrão `VARIANT_FALSE`, ou seja,
**atualizações supersedidas ficam ocultas**. A documentação diz literalmente:

> *"if the IsHidden=0 search returns no results, set `IncludePotentiallySupersededUpdates` to
> `VARIANT_TRUE` to retrieve hidden updates"*

**Portanto uma busca que retorna 0 é inconclusiva.** O VOLTRIS precisa, ao obter 0 resultados,
**refazer com `IncludePotentiallySupersededUpdates = VARIANT_TRUE` e `IsHidden = 1`** antes de
concluir "não há atualização". Sem isso, ele está medindo a política de ocultamento, não a
disponibilidade de drivers.

### O que o VOLTRIS faz hoje (errado, em dois níveis)

1. **Critério errado:** `IsInstalled=0 and Type='Driver'` — busca **global**, depois casa por
   palavra-chave no título (`title.Contains("Wi-Fi")`). Origem direta de falsos positivos:
   *"driver de Wi-Fi instalado em dispositivo de Bluetooth"*.
2. **Diagnóstico incompleto:** ao receber 0, conclui "sem atualização" — sem refazer a busca
   com `IsHidden=1` + `IncludePotentiallySupersededUpdates=TRUE`, como a Microsoft instrui.

**Medição nesta máquina:** `IsInstalled=0 and Type='Driver'` → **0 resultados**. **[FATO]**
Esse 0 **não prova** ausência de atualizações — pode ser ocultamento, política de WSUS, ou
simplesmente o fato de os drivers prioritários já estarem atuais. Precisa da refazimento acima.

## 3.6 Microsoft Update Catalog

| Propriedade | Status |
|---|---|
| É site da Microsoft, sem API oficial documentada | **[FATO]** |
| Endpoint de download: `POST /DownloadDialog.aspx` com `updateIDs` | **[FATO]** — usado em ferramentas públicas (MSCatalog, windows-update-catalog-downloader) |
| **Responde com dados reais e corretos** | **[FATO medido nesta máquina]** — HTTP 200, versão, data, tamanho, classificação, ID de download |
| Permite busca por **HWID** (`?q=DEV_9A49`) | **[FATO medido]** |
| Retorna `Products` (faixa de SO compatível) | **[FATO medido]** |
| Termos que proíbam automação | **[NÃO CONFIRMADO]** — não achei cláusula explícita; é site público sem login |

### Armadilha comprovada: o Catálogo lista versões **antigas**, não só a mais nova

**[FATO, medido]** — busca por `Intel Wireless AC 9462` no Catálogo retorna, entre outras:

> *"Intel net Driver Update (**24.20.2.1**) — Windows 11 Client, version **22H2 and later**,
> Servicing Drivers... Drivers (Networking) — 2/11/2026 — 24.20.2.1 — 23.2 MB"*

Ou seja: para **o mesmo adaptador** (9462), o Catálogo mantém **24.20.2.1** disponível mesmo
existindo versão muito mais nova. Cada entrada declara a sua própria **faixa de SO**.

**Consequência de projeto (crítica):** um matcher que take *"o primeiro resultado"* ou
*"qualquer entrada que cas o HWID"* vai **oferecer um driver de 2024 para uma máquina de 2026**.
A única ordem correta é:

1. filtrar pela **faixa de SO (`Products`)** que contém o build atual;
2. entre as restantes, escolher a **maior `Version`**.

Esta é exatamente a razão pela qual §2.3 insiste que o campo `Products` **não é opcional** — ele
substitui heurísticas de versão e é o que permite ao VOLTRIS não regredar máquinas.

## 3.7 Windows Driver Policy — a mudança de 2026 — **[FATO CRÍTICO, VERIFICADO]**

> ⚠️ **Esta seção foi reescrita após leitura integral da página oficial.** A versão anterior
> deste documento citava apenas 2 frases. A fonte é substancialmente mais específica — e
> traz **critérios numéricos verificáveis** que transformam a política em um requisito
> testável de engenharia.

Fonte: `support.microsoft.com/en-US/Windows/Hardware/Drivers/the-windows-driver-policy`
(anúncio oficial: `go.microsoft.com/fwlink/?linkid=2356646`)

### 3.7.1 A regra

> *"Windows requires all new drivers to be submitted and signed through the Windows Hardware
> Compatibility Program (WHCP) process. Windows previously trusted drivers signed by the now-expired
> cross-signed program. However, **with the April 2026 security update, these drivers are no longer
> trusted by default.**"*

Política permite carregar **apenas**:
1. Drivers assinados pelo processo de certificação **WHCP**; **ou**
2. Drivers na **allow list** de drivers/publicadores reconhecidos (assinados pelo
   cross-signed, ainda não WHCP-certified).

> *"Drivers that are not Microsoft WHCP signed or appear on the Windows Driver policy will be
> blocked on in scope, enabled systems."*

### 3.7.2 Duas fases — e os critérios numéricos **[FATO]**

| Fase | Comportamento |
|---|---|
| **Evaluation mode (Audit)** | drivers violadores são **auditados mas liberados**. Contadores correm. |
| **Enforcement mode** | drivers violadores são **bloqueados de carregar** e geram log. Persiste entre reinícios. |

**Critérios para transicionar para enforcement (números exatos, oficiais):**

| Critério | Valor |
|---|---|
| **System uptime** acumulado | **250 horas** de uso ativo |
| **Boot sessions** desde o início da avaliação | **3** reinícios (**2** no Windows Server) |
| **Violações de política** durante a avaliação | **zero** |

> *"If a driver that would violate the policy is detected during evaluation, the evaluation
> progress is **reset**. This means the countdown to enforcement starts over."*

**A leitura de engenharia que importa:** um único driver não-WHCP carregado **zera** o contador.
Na prática, uma máquina comurdade de drivers legados fica presa em avaliação por muito tempo —
é por isso que a allow list existe.

### 3.7.3 Detecção — IDs, GUIDs e comando **[FATO, com comando pronto]**

**IDs de evento** (log `Microsoft-Windows-CodeIntegrity/Operational`):

| Event ID | Significado |
|---|---|
| **3076** | driver **auditado** (seria bloqueado, mas o modo é audit) |
| **3077** | driver **BLOQUEADO** por violar a política de enforcement |

**Policy GUIDs** — o campo `Policy ID` do evento distingue a origem:

| Fase | GUID |
|---|---|
| Audit | `{784C4414-79F4-4C32-A6A5-F0FB42A51D0D}` |
| Enforce | `{8F9CB695-5D48-48D6-A329-7202B44607E3}` |

**Consultar o estado da máquina (admin):**

```powershell
$eval     = (citool -lp -json).Policies | ? { $_.PolicyID -eq "784c4414-79f4-4c32-a6a5-f0fb42a51d0d" }
$enforced = (citool -lp -json).Policies | ? { $_.PolicyID -eq "8f9cb695-5d48-48d6-a329-7202b44607e3" }
if     ($enforced.IsEnforced -and $enforced.IsAuthorized) { "ENFORCEMENT" }
elseif ($eval.IsEnforced     -and $eval.IsAuthorized)     { "EVALUATION" }
else   { "policy not available" }
```

> **Este é um sinal que o VOLTRIS deve exibir ao usuário.** Se a máquina está em
> *enforcement mode*, oferecer um driver cross-signed não é um risco teórico — é um
> **bloqueio garantido** após o próximo reinício.

### 3.7.4 Âmbito e casos limítrofes **[FATO]**

- Afeta **apenas drivers kernel-mode**; aplicações user-mode **não são afetadas**.
- **Windows Server 2025+** também é afetado (com o critério de 2 reinícios).
- **Reset/reinstall do Windows** zera os contadores → recomeça em evaluation mode.
- **Não há** forma de isentar um driver individual: ou se desativa a política inteira
  (`CiTool.exe --remove-policy "{8F9CB695-...}"` em máquinas com a atualização de julho/2026,
  exigindo desligar o Secure Boot em versões anteriores), ou o **editor** do driver fornece
  uma versão WHCP.
- Sinais visíveis de bloqueio: dispositivo não funciona, periférico não reconhecido,
  aplicação que depende de um driver kernel deixa de iniciar.

### 3.7.5 O que a própria Microsoft recomenda ao usuário final

A página dá quatro ações, nesta ordem — e a **primeira** é a que o VOLTRIS deve embodyar:

1. *"**Check Windows Update** for updated drivers. WHCP-certified, signed drivers may already
   be available through Windows Update."*
2. Verificar *Settings → Windows Update → Advanced options → Optional updates → Driver updates*.
3. Baixar do site do fabricante — *"newer versions are more likely to be WHCP-signed"*.
4. Contatar o fornecedor pedindo uma versão certificada WHCP.
   *"**Most vendors already WHCP certify their drivers**"*

> **Implicação direta e favorable ao VOLTRIS:** a Microsoft afirma que **a maioria dos
> fabricantes já certifica via WHCP**. Ou seja, o risco de "driver cross-signed bloquear
> dispositivo" é a **exceção**, não a regra — e a mitigação correta é exatamente a arquitetura
> recomendada: **pegar o driver pelo Windows Update**, onde a decisão de elegibilidade
> já foi tomada.

### 3.7.6 Requisitos de implementação no VOLTRIS

1. A partir de 2026, **"é assinado" não basta** → classificar `signatureStatus`.
2. **Recusar** (não apenas rebaixar) pacotes cross-signed quando a máquina está em
   **enforcement mode**.
3. Ler **Event 3076 e 3077** de `Microsoft-Windows-CodeIntegrity/Operational` **antes e depois**
   de cada instalação, e correlacionar com o dispositivo.
4. Consultar `citool -lp -json` e **exibir o estado da política** na UI.
5. Nunca desabilitar Code Integrity. Nunca burlar `WinVerifyTrust`.

**Recomendação forte:** o catálogo deve storing `signatureStatus ∈ {WHCP, Attested, CrossSigned}`
e o cliente deve **rebaixar** a prioridade de cross-signed, jamais apresentá-lo como "seguro".

## 3.8 ACHADO CRÍTICO: a política que esvazia a melhor fonte — **[FATO, medido]**

> Descoberto em **execução**, durante a implementação. Não constava da rev. 1 nem da rev. 2, e
> é o achado que mais altera a conclusão prática do estudo.

### O fato

A consulta `IsAssigned=1 and IsInstalled=0` retorna **0** nesta máquina. A também retorna 0.
A de `IsHidden=1` com `IncludePotentiallySupersededUpdates=TRUE` retorna **0**. Idem para
`Type='Driver' and IsHidden=1` sem filtro.

**O canal WU, porém, está funcionando:** `IsInstalled=0 and IsHidden=0` devolve 1 item —
a definição do Defender (`Type=1`, Software). `wuauserv` está `Running`. `ServerSelection=0`.

Ou seja: **o WU funciona e mesmo assim não devolve nenhum driver.** A causa está no registro:

```
HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate
  ExcludeWUDriversInQualityUpdate = 1     (REG_DWORD)
```

### O que essa política é

**[FATO]** — "Do not include drivers with Windows Updates". A Microsoft documenta três locais
equivalentes:

| Tipo | Local |
|---|---|
| GPO | `Computer Configuration > Administrative Templates > Windows Components > Windows Update > Do not include drivers with Windows Updates` |
| Registro | `HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate\ExcludeWUDriversInQualityUpdate` = `REG_DWORD 1` |
| CSP/MDM | `../Vendor/MSFT/Policy/Config/Update/ExcludeWUDriversInQualityUpdate` = 1 |

Comportamento documentado (Windows Autopatch):

> *"Devices with driver exclusion policies ... **Will display the applicable driver content — Won't
> install drivers that are approved**"*

E a Microsoft observa, em resposta pública, que **a política é habilitada por padrão no Windows**
e que **fabricantes de PC às vezes a deixam ativa**. Um caso relatado em fórum da própria
Microsoft descreve exatamente o cenário: PC novo com Windows 11 Pro, todos os drivers muito
desatualizados, causa sendo essa política.

### Por que isso é o achado mais importante do estudo

A fonte nº 1 da arquitetura — WUA `IsAssigned=1` — é a **mais confiável que existe**: o Windows
já fez o matching por Hardware ID. Mas ela é **anulada por configuração** numa fração
significativa das máquinas de consumo.

> **Se o VOLTRIS dependesse apenas do WUA, ele apareceria como "quebrado" exatamente nas máquinas
> que mais precisam dele** — e, pior, exibiria "0 atualizações", que o usuário leria como
> "estou atualizado". Essa é a mentira mais perigosa que um driver updater pode contar.

### Validação empírica da arquitetura em camadas

Este achado **confirma a decisão de projeto** com prova, não com argumento:

| Fonte | Resultado nesta máquina |
|---|---|
| WUA `IsAssigned=1` (§3.5) | **0** — anulada pela política |
| **Microsoft Update Catalog** (§3.6) | **2 atualizações reais** (Iris Xe, Realtek Áudio) |

**A camada do Catálogo não é afetada por essa política.** Foi ela que salvou o diagnóstico quando
a fonte mais confiável do mundo devolveu zero. Se a recomendação tivesse sido "WUA apenas", o
VOLTRIS teria dito "seu PC está atualizado" — e estaria errado.

### Requisitos que isso cria

1. **Detectar a política antes de reportar qualquer veredito.** O VOLTRIS não pode dizer
   "atualizado" quando a fonte foi silenciosamente esvaziada.
2. **Distinguir `UpToDate` de `Unknown`.** Com a política ativa, o veredito correto é
   **"fonte indisponível"** — nunca "atualizado". É o passo 0 da cadeia §5.2.
3. **Nunca usar o WUA como fonte única.** A cobertura depende do Catálogo MS, que é imune a
   esta configuração.
4. **Não alterar a política.** É uma decisão administrativa do usuário/empresa. O VOLTRIS
   **informa**; nunca reescreve política do Windows.

### Implementado em

`Services/Drivers/WindowsUpdateFallback.cs` → `IsDriverUpdateExcludedByPolicy()` e
`LastQueryDiagnosis`, que devolve o texto exibido na interface.

---

# 4. FONTES OFICIAIS POR FABRICANTE

## 4.1 Resumo — o que existe publicamente

| Fabricante | API pública de driver? | Canal oficial | Restrição |
|---|---|---|---|
| **Microsoft** | **WUA (COM)** — documentada, oficial | Windows Update / Catalog | nenhuma; é o canal canônico |
| **Intel** | **NÃO** | site + **Intel DSA** (local) | scraping → **HTTP 403 medido** |
| **NVIDIA** | **NÃO** | `nvidia.com/Download/Find.aspx` (formulário HTML) | sem API; termos restritivos |
| **AMD** | **NÃO** | `amd.com/en/support` | sem API |
| **Realtek** | **NÃO** | `realtek.com/en/component-download/*` | **renderizado por JS** — sem versão no HTML **[FATO medido]** |
| **Broadcom** | **NÃO** | site OEM | sem API |
| **Qualcomm / MediaTek** | **NÃO** | site / OEM | sem API |
| **Dell / HP / Lenovo / ASUS / Acer / MSI / Gigabyte / ASRock** | **NÃO** | páginas de suporte por modelo | **exigem modelo exato**; sem API |

> **Conclusão da seção 4: [FATO] Não existe API pública de catálogo de drivers para nenhum fabricante, exceto a Microsoft.**
> Qualquer desenho que dependa de API de fabricante é ficção. Foi exatamente o que a implementação anterior tentou (`api.nvidia.com/drivers/v1/latest`, chave `PROD_NVDL_KEY`) — endpoint inexistente.

## 4.2 Intel Driver & Support Assistant (DSA) — o caso especial

**[FATO medido nesta máquina]:**

```
C:\Program Files (x86)\Intel\Driver and Support Assistant\x86\DSATray.exe   → DSA INSTALADO
Serviço: DSAService (Running, Automatic)
Porta:   127.0.0.1:28385  (LISTEN)
```

O DSA resolve **exatamente** o problema que o VOLTRIS não resolve: ele lê a **placa-mãe** (SSU — Software Specification Utility) e sabe o modelo exato, incluindo `SUBSYS`.

Estado real medido: as rotas `/v1/dsa-installerinfo`, `/v1/available-releases`, `/v1/installed-drivers` retornaram **404** — apenas `DSAService` estava ativo, sem o `DSATray`. **[NÃO CONFIRMADO]** se com o tray ativo a API responde; o comportamento documentado do DSA usa prefixo de versão que varia.

**Implicação:** o VOLTRIS **não pode depender** do DSA (pode não estar instalado). Mas pode **usá-lo como fonte opcional de alta confiança** quando presente.

## 4.3 O que muda com a política de 2026 para o catálogo

Fabricantes OEM (Dell/HP/Lenovo) publicam drivers **assinados WHCP** e presentes no Catálogo Microsoft. Ou seja: **o canal da Microsoft é, hoje, a via mais confiável para drivers de OEM** — exatamente o que o VOLTRIS não usa.

---

# 5. O PROBLEMA MAIS DIFÍCIL: "este driver é realmente compatível?"

## 5.1 Por que "versão maior" é insuficiente

O briefing pede explicitamente os casos em que **versão numericamente maior NÃO é a melhor escolha**. **[FATO]/[INFERÊNCIA]**:

| Caso | Exemplo real | Consequência |
|---|---|---|
| **Versão de *pacote* ≠ versão de *driver*** | **Intel, documentado oficialmente** — ver §5.1.1 abaixo | O VOLTRIS diria "atualização disponível" para um pacote que **não muda nada** |
| **Branches divergentes** | NVIDIA **Game Ready** (otimizado para lançamento de jogo) vs **Studio** (estabilidade) — mesma "versão" base, compromissos opostos | Atualizar de Game Ready para Studio sem querer quebra o perfil de jogo |
| **Regressão do fabricante** | Intel/dGPU: versão mais nova com **blacklist de jogos**; o fabricante *reverte* o Catalyst/Game Ready. | Instalar o "mais novo" causa instabilidade que o fabricante já conhece |
| **Subsystem mismatch** | `DEV_9A49` genérico vs `DEV_9A49&SUBSYS_C195144D` | versão numericamente maior pode ser para **outra placa** |
| **Incompatibilidade de SO** | Driver que exige Win11 24H2 em máquina Win10 22H2 | instala e **não carrega** (CM_PROB_6) |
| **Pré-condição de migração** | **Intel, documentado:** para o 9462/9560/9461, migrar de 23.10.x–23.50.x para 24.40.0 exige **desconectar e desemparelhar todos os dispositivos Bluetooth** antes | Sem o passo prévio, o upgrade falha — ver §5.1.2 |
| **Dependências de componentes** | Intel Graphics requer **OpenCL/OpenGL Compatibility Pack**; driver de rede pode requerer atualização de firmware | driver "novo" sem dependência = falha |
| **Tabela de assinatura / allow list** | ver §3.7 | Windows 2026 **bloqueia** o carregamento |
| **Firmware conflituoso** | driver de GPU novo exige firmware de GPU novo | **tela azul** |
| **Driver mais antigo é o correto** | drivers de storage/RAID: a última versão publicada é frequentemente a mais estável; atualizações podem quebrar RAID metadata | `\Device\HarddiskX` BSOD |

> **Conclusão: "versão maior" é um **sinal**, nunca uma **prova**.** O VOLTRIS atual usava comparação numérica pura — e o próprio briefing pede para não fazer isso.

### 5.1.1 ACHADO CRÍTICO: versão de pacote ≠ versão de driver (documentado pela Intel)

Este é o achado mais importante do estudo para a implementação, e é **[FATO]** — a Intel
declara textualmente na página oficial do driver de Bluetooth (`download/16807`):

> *"When you update the software package, **it might not update the wireless adapter driver if
> it includes the same driver as the previous release.** For instructions, see Intel® PROSet/Wireless
> Software Version and **the Driver Version**."*

E, na página do Wi-Fi (`download/19351`):

> *"**Drivers for certain Intel Wireless Adapters may not have been updated and are the same as
> the previous package. You do not need to install this package if the version of the driver is the
> same.**"*

**Estrutura concretizada nesta máquina:**

| | Versão do **pacote** | Versão do **driver** instalado |
|---|---|---|
| Wi-Fi (ID 19351, 8/4/2026) | **24.60.0** | **24.60.0.3** ← instalado ✓ |
| Bluetooth (ID 16807, 4/28/2026) | **24.40.0** | **24.40.11.1** ← instalado |

**Três regras que daí decorrem e que o VOLTRIS precisa implementar:**

1. **Comparar `driverVersion`, nunca `packageVersion` nem a data do pacote.**
   Um pacote publicado ontem pode conter exatamente o driver que já está instalado.
2. **"Data mais recente" não é sinal de atualização.** A data do pacote é quase irrelevante
   para o usuário final.
3. **Uma atualização só é real quando `driverVersion` instalado < `driverVersion` disponível,
   para o `HardwareId` exato daquele adaptador.** Nada menos que isso passa a ser declarado.

> **Isso resolve a discrepância que eu havia marcado como [NÃO CONFIRMADO] em §13.2.**
> A página da Intel mostra BT "24.40.0" datado de 28/04/2026 e sugere estar mais recente,
> enquanto o Catálogo Microsoft diz que a máquina já tem `24.40.11.1`. **Não é contradição:**
> são grandezas diferentes (pacote vs driver). A Intel emite o aviso explicitamente justamente
> para evitar que o usuário instale um pacote que não muda o driver.
>
> **Consequência para a apresentação na UI:** o VOLTRIS deve mostrar **a versão do driver que
> será efetivamente instalada**, com rótulo explícito do tipo. Mostrar "Pacote 24.40.0" ao
> lado de um driver já em 24.40.11.1 é enganoso.

### 5.1.2 Pré-condições de migração são parte da compatibilidade

**[FATO]** — Intel, `download/16807`, específico para o hardware desta máquina
(`Wireless-AC 9560`, **`9462`**, `9461`):

> *"Before Upgrading from 23.10.x.x - 23.50.x.x versions to this 24.40.0 driver, the user needs to
> **disconnect all the Bluetooth® devices and unpair all the previously paired devices** in the
> PC. Once upgrade is complete, the user can repair the devices and connect them."*

Isto é uma **pré-condição de conhecimento do usuário**, não apenas técnica: um simples
"reinstalar" pode deixar o Bluetooth inutilizável. Um driver updater profissional precisa:

1. **detectar** a versão de origem (`driverVersion` instalado) e de destino;
2. se a transição cruzar uma fronteira conhecida com pré-condição, **exibi-la** antes de instalar;
3. **não** tentar automatizar a desemparelhamento (requer interação do usuário).

**Isto generaliza a regra:** *compatibilidade não é só "o INF casa com o HWID"; é também
"a migração é suportada e o usuário foi avisado das pré-condições"*.

## 5.2 A cadeia de decisão correta

```
0. O CATÁLOGO TEM ENTRADA PARA O driverVersion DESTE HardwareId? → se não, "sem informação"
   (nunca inferir, nunca inventar)
1. HARDWARE ID EXATO (com SUBSYS) existe no INF do pacote?     → senão, DESCARTAR
2. INF declara rank/faixa de SO compatível com o build atual?  → senão, DESCARTAR
3. Arquitetura (x64/x86/arm64) do pacote cobre o sistema?     → senão, DESCARTAR
4. Assinatura: WHCP > Attested > CrossSigned(2021) > unsigned → rebaixar/descartar
   (se a máquina estiver em ENFORCEMENT mode → descartar cross-signed outright)
5. Publicador é o fabricante esperado para este Vendor ID?    → senão, ALERTA
6. driverVersion DISPONÍVEL > driverVersion INSTALADO?         → senão, "ATUALIZADO"
   ⚠ comparar driverVersion, NUNCA packageVersion nem data do pacote (§5.1.1)
7. A transição tem PRÉ-CONDIÇÃO documentada pelo fabricante?   → se sim, AVISAR antes
8. Branch é a que o usuário quer?                             → se não, informar
9. Dependências/pré-requisitos presentes?                    → senão, instalar antes
10. Risco da classe (storage/chipset/firmware)?                → exigir confirmação
```

O VOLTRIS atual implementa **parcialmente 1, 5, 6** e **ignora 0, 2, 3, 4, 7, 8, 9, 10**.

**Note que o passo 0 vem primeiro de propósito:** sem entrada de catálogo para aquele
HardwareId, o veredito correto é `Unknown`, **não** "atualizado". Confundir "não encontrei nada"
com "está atualizado" é um defeito de epistemologia, e é a raiz do problema que gerou o
"Realtek 11.16.2.1" fabricado.

## 5.3 Como um software profissional **realmente** decide

**[INFERÊNCIA, forte]** — o caminho do Windows Update, e é o que o VOLTRIS deveria usar como **delegador de decisão**:

- O **Windows Update** já fez todo o trabalho de matching por HWID, ranking e política de assinatura. Se o WU oferece um driver, **ele é compatível**.
- O `IsAssigned=1` do WUA devolve **o driver que o Windows atribuiu a este dispositivo**.
- Portanto: **Aceitar a decisão do Windows Update** e limitar-se a **execução fiel** (backup → instalar → validar) elimina a enorme superfície de erro do matching próprio.

Esta é a **recomendação central** do estudo.

---

# 6. BACKUP E ROLLBACK

## 6.1 O que o Windows realmente permite

| Mecanismo | API real | Garantia |
|---|---|---|
| **System Restore** | `SRSetRestorePointW` (`Srclient.dll`) ou `Checkpoint-Computer` | cria ponto; **restauração é interativa** (`rstrui.exe`) — não há API para restaurar programaticamente |
| **System Restore — estado anterior do driver** | disparado automaticamente quando o driver é substituído pelo PnP | depende de o PnP avaliar a mudança como "significativa" |
| **DISM export** | `dism /online /export-driver /destination:<d>` | **[FATO]** — copia o DriverStore inteiro |
| **SetupAPI rollback** | `SetupDiCallClassInstaller(DIF_ROLLBACK)` dentro de um `DIF_SELECTCOMPATDRV` | limitado a **um** driver; o VOLTRIS já usa via engine nativo |
| **DriverStore é transacional** | `%windir%\System32\DriverStore\FileRepository` | manter cópia permite `pnputil /add-driver <cópia>` para reverter |

## 6.2 O que é POSSÍVEL e o que NÃO é

**POSSÍVEL (garantido):**
- Exportar o DriverStore antes de alterar (`dism`) — **[FATO]**
- Criar ponto de restauração do Windows — **[FATO]**
- Reinstalar versão anterior a partir da cópia — **[FATO]**
- Detectar se o dispositivo degradou após instalar (CM_PROB, `IsProblem`) — **[FATO]**
- Registrar no log tudo que foi feito

**NÃO POSSÍVEL (não prometer):**
- **Restaurar o sistema automaticamente.** `rstrui.exe` é interativo; automatizar isso é frágil e o usuário pode cancelar.
- **Reverter atomicamente.** Não há transação; a reversão é uma segunda operação, que pode falhar.
- **Garantir que o ponto de restauração exista.** System Restore pode estar desativado, com limite de frequência, ou em volume sem proteção.
- **Detectar que o Windows "não vai likes" um driver** antes de instalar — só se discover post-mortem pelo Event ID 3077.

> **Consequência de produto:** o VOLTRIS deve **prometer backup e detecção de regressão**, e **não prometer reversão automática**. A versão anterior do VOLTRIS logava "Rollback Inerente" e chamava `RestoreAllDriversAsync`, que na verdade **reinstalava todos** os drivers do backup — não reverte nada. **[FATO]**, corrigido no ciclo anterior ao remover essa afirmação.

---

# 7. INSTALAÇÃO — DETECÇÃO DE SUCESSO, FALHA E REBOOT

## 7.1 Detectar sucesso — **[FATO]**

```
CM_Get_DevNode_Status(devInst) →
   CR_SUCCESS + (status & DN_STARTED) != 0        → funcionando
   problema == 0                                   → OK
   problema != 0                                   → CM_PROB_<n>, usar o código
CM_Query_DevNode_Status / Registry HKLM\...\Properties\{83da6326-97a6-4088-9453-a1923f573b29} 1 = reboot pendente
```

**CM_PROB mais comuns** (usar na UI, não texto genérico):
`1` desabilitado · `3` driver ausente · `6` serviço não iniciou · `10` driver incompatível · `13` falha de assinatura · `14` controlador de disco com problema · `28` **driver não assinado** · `29` alteração pendente

## 7.2 Detectar falha

- `pnputil` exit ≠ 0
- `CM_PROB` ≠ 0 após instalação
- **Event ID 3077** (bloqueio por Windows Driver Policy, 2026) — **[FATO]**
- Device não reaparece na enumeração

## 7.3 Detectar necessidade de reboot

- `CM_Query_DevNode_Status(DN_NEED_RESTART)` / `DN_NEED_TO_REBOOT`
- Chave de registro `PendingFileRenameOperations`
- `SetupDiGetDeviceProperty(DEVPKEY_Device_ProblemStatus)` == 29

## 7.4 Validar a instalação

1. Re-enumerar via SetupAPI e comparar o **InstanceId**
2. `DN_STARTED` e `CM_PROB == 0`
3. Versão instalada agora **igual** à aplicada
4. Para áudio: `SystemSounds.Beep.Play()` como teste de integridade do subsistema de mídia
5. Gravar no log: versão anterior → nova, timestamp, resultado

## 7.5 Recuperar de instalação incompleta

| Situação | Ação |
|---|---|
| `pnputil` falhou no meio | o DriverStore pode ter o pacote staged; reexecutar é idempotente |
| Device ficou com `CM_PROB` | `pnputil /delete-driver <oem> /uninstall /force` + reinstalar do backup |
| Sistema instável | **orientar** o usuário para `rstrui.exe`; não automatizar |
| Reinício pendente | **não** tentar instalar outro driver antes do reboot |

---

# 8. SEGURANÇA

## 8.1 Matriz de controles

| Ameaça | Controle | Status no VOLTRIS hoje |
|---|---|---|
| **MITM** | TLS com validação estrita de cadeia; **nunca** aceitar certificado inválido | ✅ feito |
| **Pacote adulterado** | SHA-256 contra hash publicado pelo fabricante | ✅ feito (quando o fabricante publica) |
| **Pacote sem assinatura** | `WinVerifyTrust` + `WINTRUST_ACTION_GENERIC_VERIFY_V2` | ✅ feito |
| **Publicador errado** | extrair `O=` do certificado e comparar com allowlist do Vendor ID | ✅ feito |
| **Driver incompatível** | matching por HWID contra o INF | ⚠️ parcial |
| **Driver não-WHCP (2026)** | rebaixar/recusar cross-signed; monitorar Event 3077 | ❌ **falta** |
| **Escalonamento de privilégio** | elevar **só** para `pnputil`; nunca para busca/leitura | ⚠️ a corrigir |
| **Driver de storage/RAID** | **exigir confirmação explícita**; risco de BSOD | ⚠️ a corrigir |
| **Mark of the Web** | manter MOTW até validar; remover só após ok | ✅ feito |
| **TOCTOU** | calcular hash **depois** de gravar, validar **antes** de instalar | ✅ feito |
| **Exec arbitrária** | **nunca** executar `.exe` de fabricante em modo automático | ✅ feito |

## 8.2 Princípio não negociável

**Nunca desabilitar assinatura de driver, nunca burlar WinVerifyTrust, nunca instalar `unsigned`.**

O VOLTRIS antigo tinha um bypass que devolvia `true` para arquivo não assinado em "modo manutenção" — removido. **[FATO]**

## 8.3 O que a política de 2026 acrescenta

Verificar **qual** assinatura, não apenas se há:

| Nível | Aceitar? |
|---|---|
| **WHCP release** (post-2020) | ✅ preferir |
| **Attested** (DCH, sem HLK) | ✅ aceitável |
| **Cross-signed** (pré-2021) | ⚠️ **avisar** — pode ser bloqueado em 2026 |
| **Não assinado / test-signed** | ❌ recusar sempre |

---

# 9. MODELO DE NEGÓCIO / INFRAESTRUTURA

## 9.1 Comparação das quatro opções

| Critério | **A — só metadados** | **B — também pacotes** | **C — só fonte oficial** | **D — híbrido (A+C)** |
|---|---|---|---|---|
| Custo de armazenamento | ~KB | **TB** | ~KB | ~KB |
| Largura de banda (servidor) | Baixa | **Altíssima** | Nenhuma | Baixa |
| Custo de manutenção | **Alto** (curadoria manual) | Alto + distribuição | **Baixo** | Médio |
| Risco jurídico | **Baixo** (só metadados, sem redistribuição) | **Alto** (redistribuir drivers de terceiros) | Baixo | Baixo |
| Rastreabilidade | **Total** (sabemos de onde veio) | Total | Parcial | **Total** |
| Escalabilidade | Limitada pela curadoria | Limitada por CDN | **Alta** | **Alta** |
| Complexidade | Baixa | **Alta** (CDN, armazenamento, GC) | **Muito baixa** | Média |
| Dependência de terceiros | Nenhuma | Alta (hosting) | **Total** (site do fabricante) | Baixa |
| Pode quebrar | Não | Não | **Sim** (403, mudança de layout) | Não |

## 9.2 Recomendação: **Opção D (híbrido)** — e por quê

```
FONTES (sem redistribuir nada)
   ├── Microsoft Update Catalog  ──┐  metadados: HWID, versão, URL, tamanho, data, SO
   ├── Windows Update Agent (WUA) ─┤  decisão de compatibilidade JÁ FEITA pelo Windows
   ├── Intel DSA (se instalado)   ─┤  modelo exato de placa (SSU)
   └── Páginas oficiais (WARN)   ─┘  apenas como metadado, marcado como não verificado

              ↓ INGESTÃO (servidor VOLTRIS, diário)
   CATÁLOGO DE METADADOS VOLTRIS
   • indexado por HWID (com SUBSYS) e por HWID sem REV
   • versionado; cada entrada com proveniência
   • ~50–150 bytes por entrada

              ↓ ENTREGA
   CLIENTE VOLTRIS
   • busca local primeiro (rápido, offline)
   • completa com WUA
   • nunca inventa: sem entrada = "sem informação"
```

**Por que D e não as outras:**

- **Não B:** redistribuir pacotes de terceiros cria risco jurídico alto e custo de CDN enorme, **sem benefício** — o mesmo arquivo está no site do fabricante.
- **Não só C:** depender de scraping é o que **fez o VOLTRIS falhar**. A medição é clara: 403 em 39/39.
- **Não só A:** catálogo sozinho exige curadoria manual massiva e inicial. Complementar com WUA dá cobertura imediata e gratuita.

**A chave do ganho:** o WUA entrega **decisão de compatibilidade pronta**. O VOLTRIS precisa de catálogo apenas para **metadados de apresentação** (versão, data, tamanho, branch) e para o caso de o usuário estar **offline**.

## 9.3 Custo de operação em escala

**[INFERÊNCIA — estimativas de engenharia, não confirmadas]**

| Item | Custo |
|---|---|
| Servidor de catálogo (2 VM pequenas + CDN) | US$ 20–60/mês |
| Trilha de ingestão diária (crawl do Catálogo MS) | ~1 VM, US$ 15–30/mês |
| Assinatura de API de catálogo **(se existir alguma)** | US$ 0 – 5.000/mês — **[NÃO CONFIRMADO]**, nenhuma API pública foi encontrada |
| Equipe de curadoria (1 pessoa, 2h/semana) | o gargalo real |
| **Armazenamento de pacotes** | **US$ 0** (opção D) |

**O custo dominante não é infraestrutura: é curadoria.** É isso que a IObit faz com equipe humana.

---

# 10. DRIVER BOOSTER × VOLTRIS — TABELA COMPARATIVA

| Característica | Driver Booster | VOLTRIS (atual) | VOLTRIS (proposto) |
|---|---|---|---|
| **Detecção** | SetupAPI + modelo exato da placa | SetupAPI ✅ | SetupAPI + leitura de `SUBSYS` |
| **Banco** | Catálogo próprio, ~18M, online | ❌ inexistente; catálogo local vazio | Catálogo de metadados + WUA |
| **Matching** | HWID com SUBSYS | ❌ nome + heurística; **falso positivo** | **Delegado ao WUA** (`IsAssigned=1`) + HWID |
| **Download** | Do site oficial | ❌ Fabrica URL; baixava HTML | Do site oficial, com hash e progresso |
| **Validação** | WHQL + HTTPS + teste próprio | Assinatura + publicador ✅ (bypass removido) | + **WHCP vs cross-signed** |
| **Instalação** | `pnputil`/Pnp com elevação | `pnputil` ✅ (**elevação corrigida**) | idem |
| **Backup** | Automático + restore point | DISM + restore point ✅ | idem |
| **Rollback** | Automático | ⚠️ **afirmava rollback que não fazia** | **Promete backup + detecção; não promete reversão** |
| **Offline** | Sim (modo offline) | ❌ | Sim (catálogo local) |
| **Atualização automática** | Sim (idle) | ❌ | V2 |
| **Transparência de fonte** | ✅ mostra versão/data/tamanho | ✅ **NOVO** — mostra procedência por linha | idem |

### O que NÃO devemos copiar

| Mecanismo | Por que não |
|---|---|
| Comprimir/reempacotar pacotes de terceiros | quebra cadeia de assinatura; risco jurídico |
| "Unlock speed limit" (throttling) | é modelo comercial, não engenharia |
| "IObit test" próprio | exige laboratório |
| Blacklist própria de drivers | a Microsoft já tem a allow list; duplicar é pior |
| Empacotar milhares de drivers no instalador | custo e risco |

---

# 11. ANÁLISE DO PROJETO VOLTRIS EXISTENTE

## 11.1 O que já existe e **deve ser reutilizado** (nada duplicado)

| Recurso | Caminho | Veredito |
|---|---|---|
| `SetupApiEnumerator` | `Services/Drivers/` | **reusar** — enumeração em 102 ms, 154 dispositivos |
| `DriverCategoryClassifier` | `Services/Drivers/` | **reusar** — classificação por ClassGuid/HWID real |
| `DriverIconResolver` + `DriverIcons.xaml` | | **reusar** |
| `DriverInstallState` + `DriverItemViewModel` | `UI/ViewModels/` | **reusar** — máquina de estados real |
| `SecureDriverDownloader` | | **reusar** — .part, hash, TLS estrito, progresso |
| `DriverSignatureVerifier` | | **reusar** + **estender** para WHCP vs cross-signed |
| `SafeDriverInstaller` | | **reusar** — seleção de INF por HWID, backup, validação pós-install |
| `DriverBackupManager` | | **reusar** — `dism /export-driver` |
| `SystemToolsService.CreateSystemRestorePointAsync` | | **reusar** |
| `DriverSecurityService` | | **reusar** — ponto de restauração obrigatório |
| `DriverOperationScope` | | **reusar** — observabilidade já padronizada |
| `DriverCatalog` + `CatalogDriverDetector` | | **manter e evoluir** — já é o esqueleto do modelo certo |
| `WindowsUpdateFallback` | | **reescrever** — hoje é heurística por palavra-chave |
| `IntelDriverApi`, `NvidiaDriverApi`, `AmdDriverApi` | | **reescrever/remover** — endpoints fictícios |
| `RealtekDriverDetector` | | **remover** — nunca achou nada, só fabricava |
| `GenericDriverDetector` | | **remover** — retorna `Version = "Atualização Recomendada"` |
| `NativeDriverEngine` (C++ `DE_InstallDriver`, `DE_VerifyDriverSignature`, `DE_RollbackDriver`, `DE_ExportLogs`) | `DLLS/DriverEngine/` | **reusar** — já tem rollback e validação nativos |
| `WmiHelper` | `Utils/` | **reusar** |

## 11.2 Onde o Driver Updater deve ser integrado

```
UI/Views/DriversView.xaml(.cs)          ← orquestração + apresentação
        │
        ├─ DriverCatalogService          ← NOVO: cliente do catálogo remoto
        ├─ WindowsUpdateProvider         ← NOVO: reescreve WindowsUpdateFallback (WUA)
        ├─ IntelDsaProvider              ← V2 (opcional)
        ├─ DriverMatchEngine             ← NOVO: cadeia de decisão §5.2
        └─ (reuso) SecureDriverDownloader → SafeDriverInstaller → DriverBackupManager
```

**Ponto de injeção do DI:** `Core/ServiceCollectionExtensions.cs` já registra `ILoggingService`, `SystemToolsService`, etc. Os providers de driver devem ser singletons registrados ali — **sem duplicar** o que existe.

## 11.3 Diagnóstico do que está errado hoje

| Defeito | Evidência |
|---|---|
| Catálogo local vazio | `DriverData/driver_catalog.json` só tem o schema |
| Detectores de fabricante inúteis | Intel 403 (39×); NVIDIA/AMD **0 chamadas**; Realtek 0 achados |
| WUA com matching errado | `IsInstalled=0 and Type='Driver'` + `title.Contains("Wi-Fi")` |
| WUA sem download | `DownloadContents` quase sempre vazio → sem URL instalável |
| 22 s por consulta ao Catálogo | consultas por HWID sem cache e sem limite de taxa |
| 154 dispositivos × 7 detectores | 31,5 s de varredura; deve ser < 3 s com cache |

---

# 12. DRIVER UPDATER ARCHITECTURE V1

## 12.1 Arquitetura geral

```
┌──────────────────────────────────────────────────────────────────────┐
│ CLIENTE VOLTRIS                                                      │
│                                                                       │
│  DriversView  ──►  DriverScanCoordinator                             │
│                         │                                            │
│                         ├─► DeviceInventoryService    (SetupAPI)    │
│                         │      154 dispositivos, 102 ms               │
│                         │                                            │
│                         ├─► DriverMatchEngine                        │
│                         │      1. catálogo local (offline)            │
│                         │      2. catálogo remoto (cache 24 h)        │
│                         │      3. WUA IsAssigned=1  ← decisão MS    │
│                         │      4. Intel DSA (se presente)             │
│                         │      → UpdateVerdict {verdict, reasons[]}   │
│                         │                                            │
│                         └─► DriverInstallCoordinator                 │
│                                ├─ DriverBackupManager   (dism export) │
│                                ├─ DriverSecurityService (restore pt)  │
│                                ├─ SecureDriverDownloader(.part+hash)  │
│                                ├─ DriverSignatureVerifier (WHCP?)     │
│                                └─ SafeDriverInstaller  (INF + pnputil)│
└──────────────────────────────────────────────────────────────────────┘
                                    │
┌─────────────────────────────────▼────────────────────────────────────┐
│ CATÁLOGO VOLTRIS (servidor)                                           │
│   • metadados apenas — NENHUM pacote de driver armazenado             │
│   • index: hwid_exato, hwid_sem_rev, vendor, os_faixa                  │
│   • cada entrada: proveniência obrigatória                           │
│   • ingestão: Catálogo MS (diário) + curadoria humana                │
└──────────────────────────────────────────────────────────────────────┘
```

## 12.2 Componentes

| Componente | Responsabilidade | Novo? |
|---|---|---|
| `DeviceInventoryService` | SetupAPI → `DeviceInfo` | **reusar** `SetupApiEnumerator` |
| `DriverCategoryClassifier` | ClassGuid/HWID → categoria | **reusar** |
| `DriverMatchEngine` | cadeia §5.2 → veredito | **novo** |
| `DriverCatalog` / `CatalogDriverDetector` | catálogo local por HWID | **evoluir** |
| `DriverCatalogService` | cliente HTTP do catálogo remoto | **novo** |
| `WindowsUpdateProvider` | WUA `IsAssigned=1` | **reescrever** |
| `IntelDsaProvider` | API local do DSA | **V2** |
| `SecureDriverDownloader` | download validado | **reusar** |
| `DriverSignatureVerifier` | Authenticode + nível WHCP | **reusar + estender** |
| `SafeDriverInstaller` | INF + pnputil + validação | **reusar** |
| `DriverBackupManager` | export/import DriverStore | **reusar** |
| `DriverOperationScope` | observabilidade | **reusar** |

## 12.3 Fluxo de dados

```
scan → inventory → [por dispositivo] match → UpdateVerdict → UI
                                                              ↓ (ação)
                          backup → restore point → download → validate → install → verify
```

## 12.4 Catálogo

- **Local:** `DriverData/driver_catalog.json`, cache 24 h, carregado em `Lazy`
- **Remoto:** `GET /v1/hwid/{urlencoded}?os={build}&arch={arch}` → `UpdateVerdict[]`
- **Vereditos:** `UpToDate` · `UpdateAvailable` · `NewerBranchAvailable` · `CrossSignedOnly` · `Incompatible` · `Unknown`

## 12.5 Fontes (prioridade)

| # | Fonte | Confiança | requer rede |
|---|---|---|---|
| 1 | Catálogo local | Alta (se preenchido) | não |
| 2 | **WUA `IsAssigned=1`** | **Muito alta (decisão do Windows)** | sim |
| 3 | Catálogo Microsoft (remoto, via catálogo VOLTRIS) | Alta | sim |
| 4 | Intel DSA local | Muito alta | não (localhost) |
| 5 | Site do fabricante | **Baixa — só informativo** | sim |

## 12.6 Matching — a cadeia §5.2, em código

Cada passo produz um `reason` legível, exibido na UI. Exemplo:
`"Rejeitado: o INF não declara PCI\VEN_8086&DEV_9A49&SUBSYS_C195144D"`

## 12.7 Download

Já implementado e validado: `.part` → hash → renomeia → extrai → procura INF.
**Faltam:** cache de URL assinada (o Catálogo MS usa URLs temporárias), e **teto de taxa** contra o catálogo.

## 12.8 Validação

1. HTTPS com cadeia válida (nunca ignorar erro de certificado)
2. Tamanho vs. anunciado (tolerância 1%)
3. SHA-256 vs. publicado
4. `WinVerifyTrust` no artefato
5. Publicador (`O=` do certificado) ∈ allowlist do Vendor ID
6. **NOVO:** classificar como WHCP / Attested / CrossSigned; rebaixar cross-signed
7. INF contém o HWID alvo (com BOM UTF-16 tratado)
8. Arquitetura e faixa de SO

## 12.9 Instalação

1. Recusa automática para `Storage`, `Chipset`, `Firmware` sem confirmação
2. `pnputil /add-driver <inf> /install` **elevado** (verificar antes)
3. Re-enumerar; `DN_STARTED` e `CM_PROB == 0`
4. Versão instalada == aplicada
5. Detectar reboot pendente
6. Gravar `Event 3077` se ocorrer

## 12.10 Rollback

- **Backup:** `dism /export-driver` (sempre) + ponto de restauração (sempre que admin)
- **Detecção de regressão:** `CM_PROB`, `IsProblem`, Event 3077
- **Reversão:** **oferecida ao usuário**, com o caminho exato; **não automática**
- **Não prometer** reversão automática (§6.2)

## 12.11 Logs

Já existe `DriverOperationScope` (BEGIN / etapa / END / duração / rollback).
**Adicionar:** `verdict` e `reason` por dispositivo, e `source` de cada entrada.
**Não** logar por dispositivo em volume — resumo por categoria.

## 12.12 Segurança

Ver §8. **Adicionar:** verificação de nível de assinatura; UAC só para instalar; confirmação para classes de risco.

## 12.13 UI

Manter. **Adicionar:** coluna/selo de **procedência** e **motivo do veredito** (já implementado no ciclo anterior) — é o que responde "como sei que é real?".

## 12.14 Infraestrutura

Servidor pequeno (catálogo + CDN) + pipeline de ingestão. Sem armazenamento de pacotes.

## 12.15 Custos

Ver §9.3.

## 12.16 Limitações

- Sem API de fabricante → cobertura depende do Catálogo MS e do WUA
- Drivers OEM nem sempre estão no Catálogo MS para todos os modelos
- **Não é possível garantir** que a "melhor" versão é a mais recente
- WUA pode estar desabilitado por política (WSUS, `DisableWindowsUpdateAccess`)

## 12.17 Riscos

| Risco | Mitigação |
|---|---|
| Catálogo desatualizado | veredito mostra data; WUA como segundo caminho |
| Motorista do Windows muda | revalidar matching periodicamente |
| ToS do Catálogo MS | verificar antes deAutomated; hoje é site público sem login — **[NÃO CONFIRMADO]** |
| Usuário em WSUS corporativo | detectar e avisar que o escopo é o servidor gerenciado |
| Política 2026 bloquear driver | classificar assinatura; avisar |

## 12.18 Estratégia de testes

| Nível | Alvo |
|---|---|
| Unitário | normalização de HWID, ranking de INF, cadeia §5.2, classificação de assinatura |
| Contrato | esquema do catálogo; `UpdateVerdict` |
| Integração | enumerar → casar → veredito, contra catálogo de teste com 50 HWIDs |
| **Ponto de reboot** | hardware real: matriz de máquinas |
| Regressão | os 154 dispositivos desta máquina como baseline |

## 12.19 Estratégia de escalabilidade

- Cliente: catálogo local em disco; catálogo remoto paginado e com ETag
- Servidor: índice por prefixo de HWID; CDN para o catálogo
- Ingestão: idempotente, por `sourceRef`

## 12.20 Teste em hardware real — como fazer sem risco

1. **Snapshot/clone** do disco, ou VM com snapshot
2. **Nunca** testar em armazenamento/RAID como primeiro caso
3. Rodar: instalar → `CM_PROB` → `DN_STARTED` → evento 3077
4. Reiniciar e reavaliar
5. Se degradar: reverter do `dism` export

---

# 13. MVP

## 13.1 O que é realmente necessário

**MVP — "dizer a verdade e instalar com segurança"**

| Item | Escopo |
|---|---|
| Inventário de hardware | SetupAPI (reusar) |
| Fonte 1 | **WUA `IsAssigned=1`** — decisão do Windows |
| Refazimento WUA | ao obter 0 → `IsHidden=1` + `IncludePotentiallySupersededUpdates=TRUE` (§3.5) |
| Fonte 2 | **Catálogo Microsoft** com cache, filtro por `Products` (faixa de SO) e teto de taxa |
| Matching | cadeia §5.2 completa (11 passos), com `reason` por dispositivo |
| **Veredito por `driverVersion`** | **nunca** por `packageVersion` nem por data (§5.1.1) |
| Vereditos | `UpToDate` / `UpdateAvailable` / `PackageNewerButDriverSame` / `Unknown` |
| Download | já implementado (`.part`, hash, progresso, cancelamento) |
| Validação | assinatura + publicador + **nível WHCP** + HWID no INF |
| Política 2026 | `citool -lp -json` → exibir Evaluation/Enforcement; Events **3076** e **3077** |
| Instalação | `pnputil` elevado, com confirmação para classes de risco |
| Backup | `dism /export-driver` + restore point |
| Pós-instalação | `CM_PROB`, `DN_STARTED`, Events 3076/3077, reboot pendente |
| UI | selo de procedência + motivo do veredito (já implementado) |
| Logs | `DriverOperationScope` (já implementado) |
| **Fora do MVP** | catálogo próprio com dados, Intel DSA, atualização automática, rollback automático, detecção automática de branches |

**Critério de sucesso do MVP:** para qualquer dispositivo, o VOLTRIS **nunca afirma** atualização
sem fonte verificável, **nunca** confunde `packageVersion` com `driverVersion`, e **nunca**
instala sem validar assinatura, publicador, HWID e estado da política de assinatura.

## 13.2 Prova de que o MVP é viável — medição nesta máquina

**[FATO]** — consulta real ao Catálogo Microsoft (HTTP 200):

| HWID | Instalado | Catálogo | Data | Tamanho | Classe |
|---|---|---|---|---|---|
| `PCI\VEN_8086&DEV_A0F0` | 24.60.0.3 | 24.60.0.3 | 06/10/2026 | 22,9 MB | Networking |
| `USB\VID_8087&PID_0AAA` | 24.40.11.1 | 24.40.11.1 | 07/01/2026 | 3,0 MB | Other Hardware |
| `PCI\VEN_8086&DEV_9A49` | 32.0.101.7026 | **32.0.101.7088** | 16/06/2026 | 524,9 MB | Video |
| `INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256` | 6.0.9175.1 | **6.0.9752.1** | 28/10/2024 | 12,0 MB | Sound |

**Duas atualizações reais existiam e o VOLTRIS não as encontrou.** E o que ele *mostrou* foi pior que nada: fabricou "Realtek 11.16.2.1" — que é a série de **Ethernet**, aplicada a um dispositivo de **Áudio**. Um usuário que confiasse teria instalado o driver errado.

### 13.2.1 Discrepância Intel × Catálogo: RESOLVIDA

Na versão anterior deste documento deixei isto como **[NÃO CONFIRMADO]**. A pesquisa na
documentação oficial da Intel **resolveu** — e a resolução é mais útil que a dúvida original.

**O fato medido:**

| Componente | Versão do **pacote** Intel | Versão do **driver** nesta máquina | Veredito |
|---|---|---|---|
| Wi-Fi (ID 19351) | 24.60.0 — 04/08/2026 | **24.60.0.3** | **atualizado** |
| Bluetooth (ID 16807) | 24.40.0 — 28/04/2026 | **24.40.11.1** | **atualizado** |

**A explicação:** os números não são a mesma grandeza. A Intel publica pacotes com versionamento
próprio, e **declara oficialmente** que um pacote novo pode conter o driver da release anterior.
A página do Bluetooth diz:

> *"it might not update the wireless adapter driver if it includes the same driver as the
> previous release"*

E a do Wi-Fi, mais explicitamente:

> *"Drivers for certain Intel Wireless Adapters may not have been updated and are the same as
> the previous package. **You do not need to install this package if the version of the driver is
> the same.**"*

**Não há contradição entre Intel e o Catálogo Microsoft.** Ambos concordam: o *driver* desta
máquina já é o mais recente. O que a página da Intel exibe com data nova é o **pacote**, não uma
atualização de driver para este adaptador.

### 13.2.2 A regra de ouro que este caso produz

> **Numa interface de driver updater, a única unidade que significa "atualização" para o
> usuário é a `driverVersion` do `HardwareId` dele. A `packageVersion` e a data de publicação
> são metadados do fabricante, e não devem ser o critério — nem o título da oferta.**

Consequências práticas para o VOLTRIS:

1. A UI deve exibir **"Driver 24.40.11.1 → 32.0.101.7088"**, com rótulo explícito do tipo de versão.
2. Se `packageVersion` > instalada mas `driverVersion` == instalada → veredito **`UpToDate`**, e
   um aviso informativo: *"o fabricante publicou um pacote novo, mas ele não altera o driver
   deste adaptador"*. Isso é honesto e é mais útil do que oferecer um download inútil de 28 MB.
3. Nunca inferir atualização a partir de **data de publicação**. Isso teria gerado um falso
   positivo nesta exata máquina — a falha que o VOLTRIS já cometeu com a Realtek.

## 13.3 V2

- Catálogo VOLTRIS com metadados ( servidor pequeno)
- Integração com Intel DSA
- Cache offline completo
- Seleção de branch (Game Ready vs Studio) com preferência do usuário
- Detecção de dependências
- Rollback **oferecido** com um clique
- Agendamento em ocioso

## 13.4 V3

- CuradoriaOwn: OEM (Dell/HP/Lenovo) com modelo exato
- Relatório de saúde de driver (idade, cadeia, assinatura)
- Detecção de driver de storage/RAID com confirmação reforçada
- Comparação com o estado recommendations do fabricante
- Painel de proveniência com auditoria completa
- Feed regionalizado

---

# 14. CRITÉRIO DE SUCESSO — 16 RESPOSTAS

**1. É tecnicamente possível criar um Driver Updater profissional no VOLTRIS?**
Sim — **[FATO]**, com a ressalva de que a cobertura virá majoritariamente do canal Microsoft, não dos fabricantes.

**2. Como ele deverá encontrar drivers?**
Em três camadas: **(1)** WUA `IsAssigned=1` — o Windows já decidiu a compatibilidade; **(2)** Microsoft Update Catalog, via catálogo de metadados do VOLTRIS; **(3)** Intel DSA quando instalado. Scraping de site fica apenas como metadado **marcado como não verificado**.

**3. Precisaremos de um banco próprio?**
Sim, mas **apenas de metadados** (Opção A), não de pacotes (Opção B). A prova de que metadados
bastam é aritmética: a IObit anuncia **18.000.000+ drivers** num instalador de **31,9 MB**.
18M × ~20 MB = ~360 TB, impossível num executável de 32 MB (§1.4). Estimativa de volume:
154 dispositivos × 50 mil Hardware IDs × ~120 bytes ≈ **~10 MB** para hardware de consumo;
~1 GB para cobertura OEM ampla.

**4. Precisaremos armazenar os arquivos?**
**Não.** Armazenar traz custo de CDN, risco jurídico de redistribuir drivers de terceiros e
nenhuma vantagem — o mesmo arquivo está no site do fabricante, já assinado.

**5. Podemos baixar diretamente dos fabricantes?**
**Sim**, e devemos — mas apenas com **URL direta de artefato** (.zip/.cab/.exe). Página de consulta
não é pacote. E a assinatura **deve** ser verificada: o que falha hoje é o *scraping da página de
busca*, não o download em si. Os próprios fabricantes publicam SHA-256 junto do artefato
(ex.: BT `E6C537BF874AE95D...`, Wi-Fi `BB12E73A4AB80B0A...`), o que torna a verificação forte.

**6. Quando usar Windows Update?**
**Sempre que disponível** — é a única fonte com decisão de compatibilidade feita pelo próprio
Windows, e a Microsoft afirma que *"most vendors already WHCP certify their drivers"*, ou seja,
é também a via menos arriscada quanto à política de assinatura. Ordem: WUA primeiro; Catálogo MS
como complemento; site do fabricante por último e só como informativo.

**7. Como identificar compatibilidade?**
Pela cadeia §5.2 (11 passos), e **aceitando a decisão do WUA quando ela existir**. Nunca por
nome de produto, nunca por `packageVersion`, nunca por data de publicação.

**8. Como validar segurança?**
HTTPS estrito → tamanho → SHA-256 → `WinVerifyTrust` → publicador do certificado na allowlist do
Vendor ID → **nível de assinatura (WHCP > Attested > CrossSigned)** → HWID presente no INF →
arquitetura e faixa de SO → **estado da política (`citool -lp -json`)**: se a máquina estiver em
**enforcement mode**, recusar cross-signed. Nunca burlar. Nunca desabilitar o Code Integrity.

**9. Como instalar?**
`pnputil /add-driver <inf> /install` **elevado**, precedido de `dism /export-driver` e ponto de
restauração; classes de armazenamento/chipset exigem confirmação explícita; exibir
pré-condições de migração documentadas pelo fabricante (§5.1.2).

**10. Como fazer rollback?**
**Backup e detecção de regressão, garantidos. Reversão automática, não.** O Windows não oferece
transação; `rstrui.exe` é interativo. Nota: o líder de mercado também promete apenas
*"automatically backup all drivers for **safe restore**"* — o backup é automático, a restauração
não. Oferecer o caminho ao usuário e registrar o que foi feito.

**11. Como testar em máquinas reais?**
Matriz mínima: Intel iGPU + dGPU, AMD, NVIDIA, rede Intel/Realtek/Broadcom, áudio HD, notebook
OEM, desktop, x64. Sempre em snapshot/VM; nunca começar por storage/RAID. Medir `CM_PROB`,
`DN_STARTED`, Events **3076** (auditado) e **3077** (bloqueado), reboot pendente — e conferir se
os contadores de `citool` zeraram, o que indica driver cross-signed carregado.

**12. Qual infraestrutura será necessária?**
Servidor: API de catálogo + pipeline de ingestão + banco indexado + CDN. Cliente: nada além do
motor de matching. Sem armazenamento de pacotes.

**13. Qual o custo aproximado em escala?**
**[INFERÊNCIA]** US$ 35–90/mês de infraestrutura + curadoria humana (o gargalo). Nenhum custo
de armazenamento de drivers.

**14. Quais partes são difíceis?**
(a) **Curadoria do catálogo** — trabalho contínuo e humano. (b) **Branch de driver** (Game Ready
vs Studio) — sem semântica pública confiável. (c) **Dependências e pré-condições de migração** —
documentadas em prosa por fabricante, não em formato legível por máquina (§5.1.2). (d) **Cobertura
OEM**, fragmentada por modelo. (e) **Distinguir `packageVersion` de `driverVersion`** — a Intel
avisa, mas não publica o mapeamento de forma estruturada.

**15. Quais partes são impossíveis de garantir universalmente?**
- Que a versão mais recente seja a melhor (§5.1)
- Que exista driver público para todo hardware — **objetores**, sensores, periféricos de OEM frequentemente não têm
- Que a decisão do fabricante continue disponível anos depois
- Que a política de assinatura de 2026 não mude de novo
- Reversão automática (§6.2)
- **Que a versão do driver que o fabricante considera "correta" para o seu adaptador seja
  passível de ser obtida por máquina** — a Intel publica a informação, mas em prosa

**16. Qual arquitetura você recomenda?**
**Opção D — híbrida.** WUA como fonte primária de decisão; catálogo de metadados próprio para
apresentação e offline; download direto do fabricante; **nenhum pacote armazenado**; UI sempre
exibindo procedência, **a versão do driver que será instalada** e o motivo do veredito.

**A ordem correta para começar:** reescrever o `WindowsUpdateFallback` para usar `IsAssigned=1`
(o qual já entrega no máximo 1 driver por dispositivo, com o matching feito pelo Windows) e
adicionar o refazimento com `IsHidden=1`; em seguida, o `DriverMatchEngine` com a cadeia de 11
passos; depois o cliente do Catálogo Microsoft com cache, filtro por `Products` e teto de taxa.
**Não** começar pelo catálogo de milhões de entradas — sem ele, o MVP já entrega honestidade e
finds reais, como a medição da §13.2 demonstra.

---

# APÊNDICE — FONTES

**IObit (oficial)**
- `iobit.com/en/driver-booster.php` ← **V 13.6.0 | 31.9 MB** (tamanho do instalador)
- `iobit.com/en/recommend/dbfree.php` ← **"18,000,000+ PC drivers" + "31.9 MB"** na mesma frase
- `iobit.com/en/features-game-drivers-and-components-updater-40.php` ← V 13.4.0 | 31.8 MB
- `iobit.com/en/pressroom-driver-booster-7--armed-with-a-database-of-3,500,000+-drivers...`
- `iobit.com/en/pressroom-driver-booster-6--empowered-with-a-larger-database-of-3,000,000-drivers...`
- `iobit.com/en/features-best-driver-updater-37.php`
- `iobit.com/en/driver-booster-proanni.php`
- `iobitsoftware.com/db.php` ← "backup all drivers for **safe restore**", "compressed package", "system idle time"
- `techspot.com/downloads/6010-iobit-driver-booster.html` ← changelog: "Smart Installation Mode", "Small size VS big space"
- `majorgeeks.com` ← v12.4.0.571 | 31 MB

**Microsoft (oficial)**
- `support.microsoft.com/en-US/Windows/Hardware/Drivers/the-windows-driver-policy` ← **política de abril/2026**: fases, critérios (250 h / 3 boots), Events 3076/3077, Policy GUIDs, `citool -lp -json`, remoção via `CiTool.exe --remove-policy`
- `learn.microsoft.com/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search` ← **tabela completa de critérios**; `IsAssigned=1` = *"At most, one assigned Windows-based driver update is returned for each local device"*; armadilha do `IsHidden`
- `learn.microsoft.com/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-`
- `learn.microsoft.com/windows-hardware/drivers/install/whql-release-signature`
- `learn.microsoft.com/windows-hardware/drivers/install/release-signing`
- `catalog.update.microsoft.com` ← consultado empiricamente
- `catalog.update.microsoft.com/Search.aspx?q=Intel%20Wireless%20AC%209462...` ← prova de versões antigas listadas com `Products`

**Intel (oficial) — fundamental para §5.1**
- `intel.com/content/www/us/en/download/19351/...wi-fi-drivers...` ← pacote 24.60.0 → **driver 24.60.0.3**; *"You do not need to install this package if the version of the driver is the same"*
- `intel.com/content/www/us/en/download/16807/intel-wireless-bluetooth-drivers-for-it-administrators.html` ← pacote 24.40.0 → driver 24.40.0.3; *"might not update the wireless adapter driver"*; **pré-condição de desemparelhar BT no 9462/9560/9461**; SHA-256 publicados

**Ferramentas públicas de referência (leitura, não cópia)**
- `github.com/ryan-jan/MSCatalog` — PowerShell, busca e download no Catálogo
- `github.com/mjbommar/windows-update-catalog-downloader` — confirma viabilidade da abordagem

**Medições próprias nesta máquina**
- WUA `IsInstalled=0 and Type='Driver'` → **0 resultados**
- intel.com via HTTP → **403 em 39/39**
- 4 consultas ao Catálogo Microsoft → **HTTP 200** com versão, data, tamanho e classe
- SetupAPI: **154 dispositivos** em 102 ms
- Intel DSA instalado, `127.0.0.1:28385` em LISTEN, rotas `/v1/*` → **404** sem o `DSATray`
- `SetupDiLoadClassIcon` não possui variante `W`; devolve `IconIndex` (ex.: Net → 15)
- Intel DSA instalado, `127.0.0.1:28385` em LISTEN, rotas `/v1/*` → **404** sem o `DSATray`
- 4 consultas ao Catálogo Microsoft → **HTTP 200** com versão, data, tamanho e classe
- SetupAPI: **154 dispositivos** em 102 ms
- `SetupDiLoadClassIcon` não possui variante `W`; devolve `IconIndex` (ex.: Net → 15)

---

**FIM DO ESTUDO. Nenhum código de produção foi escrito nesta etapa, conforme instruído.**
