# RELATÓRIO DE AUDITORIA COMPLETA — Voltris Optimizer

**Data:** 02/08/2026
**Projeto:** `D:\APLICATIVO VOLTRIS` (WPF / C# / .NET 8 / `net8.0-windows10.0.17763.0`)
**Método:** análise estática exaustiva + build de verificação (`dotnet build -c Release`: **0 erros, 3.993 avisos**)
**Base factual:** leitura direta dos arquivos-fonte, relatórios de auditoria de segurança, arquitetura e operações de sistema.

> Este relatório separa **FATO** (o que o código realmente faz, com arquivo:linha) de **RISCO** (consequência real). Não contém opiniões sobre intenção.

---

## 1. VEREDICTO GERAL

O Voltris Optimizer é um projeto **enorme e ambicioso** (~1.400+ arquivos .cs, ~350.000+ linhas), com **funcionalidade real** (Q-Learning genuíno no Brain V2, limpeza de disco real, monitoramento de hardware via LibreHardwareMonitor, scan anti-adware real, etc.).

Porém, no estado atual ele **não é profissional, nem assinável, nem licenciável** com segurança, e **não vai gerar receita** por 3 motivos de fundo:

1. **A licença é 100% contornável** — qualquer pessoa ativa Pro grátis em minutos (seção 2).
2. **Comportamento de malware real** — driver de kernel não assinado (WinRing0), PowerShell com `Bypass`, matar processos de sistema, telemetria exfiltrada via Telegram → o **Windows Defender e antivírus marcam como HackTool/Riskware/Spyware** e o usuário desiste antes de pagar (seção 3).
3. **Experiência de "placebo"** — muitas telas "aplicam otimizações" que não fazem nada (mock/sucesso falso), o que destrói a confiança do usuário que testa o trial (seção 4).

O caminho para o sucesso NÃO é adicionar mais features — é **parar de parecer malware, blindar a licença e mostrar resultado mensurável**.

---

## 2. AUDITORIA DE LICENCIAMENTO — SEGREDO CRÍTICO EXPOSTO

### 2.1 P0 — Chave `service_role` do Supabase em texto claro (o pior achado)

A chave **service_role** (não `anon`) do Supabase dá **acesso total (read/write) a todo o banco de dados**, ignorando Row Level Security. Ela está embutida em texto claro em **3 arquivos do binário**:

| Local | Linha |
|---|---|
| `Services\License\LicenseApiService.cs` | 23 |
| `Services\License\HardwareTrialService.cs` | 886 |
| `Services\LicenseManager.cs` | 42 |
| `Services\Logging\SecurityTestingUtility.cs` | 24, 59, 257 (logs/testes) |

A chave decodifica para `role: service_role`, projeto `zamjyyzockbbugjepkhk`.

**O que um atacante faz com ela:**
- Lê/troca todas as licenças e dados de clientes (e-mail, HWID, histórico de ativação).
- **Se auto-licencia**: insere uma linha "Pro" no banco → app vira Pro sem pagar.
- Chama as Edge Functions com payloads arbitrários.
- Vaza todos os dados dos seus clientes (LGPD — risco jurídico real).

### 2.2 P0 — Ativação offline sem verificação de assinatura

- `LicenseManager.cs:798-877` (`ActivateLicenseOfflineAsync`): ativa **Pro por 7 dias** (`SetPendingValidationLicense`, linha 853) apenas se a chave **tiver formato válido** — o hash (`parts[4]`) **nunca é verificado**.
- `LicenseManager.cs:912-935` (`IsValidLicenseKeyFormat`): aceita `VOLTRIS-PRO-QUALQUER-20991231-AAAAAAAAAAAAAAAA`.
- `LicenseTokenStore.cs:285`: na restauração, remove o sufixo `_PENDING` → **pendência vira licença cheia**.

**Fluxo de crack de 2 minutos:** bloquear o domínio do Supabase no `hosts` → digitar chave falsa com formato válido → 404 → fallback offline → **Pro 7 dias**, renovável infinitamente.

### 2.3 P0 — Estado Pro restaurado de JSON em texto puro

- `LicenseJsonStore.cs:23` → `%LOCALAPPDATA%\Voltris\license.json` em texto puro.
- `LicenseTokenStore.cs:150-171, 188-234`: restaura Pro se `ExpiresAt` for futuro **ou `DateTime.MinValue`** (sem expiração) — basta editar o JSON.
- `LicenseTokenStore.cs:214`: seta `_isProActive = true` sem verificar assinatura nem vínculo de máquina.

### 2.4 P0 — Chave de assinatura de licenças hardcoded no gerador

- `LicenseGenerator\Program.cs:19`: `"VOLTRIS_SECRET_LICENSE_KEY_2025"`.
- `LicenseGenerator\Program.cs:440-457`: assinatura = SHA256 truncado para **16 hex (64 bits)**, sem HMAC. Qualquer pessoa gera licenças válidas de qualquer plano.

### 2.5 Alta — "Criptografia" local toda derivável

Toda criptografia local se apoia em segredos que o próprio usuário consegue ler/calcular:

| Local | Segredo derivável |
|---|---|
| `LicenseTokenStore.cs:366-417` | AES key = SHA256(`"VOLTRIS_TOKEN_" + MachineGuid + MachineName`); IV derivado da chave + XOR fixo |
| `HardwareTrialService.cs:87-117` | SHA256(`"VOLTRIS_HWID_CACHE_" + ...`) |
| `HardwareTrialService.cs:2227-2280` | PBKDF2 com salt fixo `"VOLTRIS-HWID-TRIAL-2026"`, **IV = 16 bytes zeros** |
| `TrialProtectionService.cs:657-702` | AES key = SHA256(`"VOLTRIS_KEY_" + machineId`), IV = primeiros bytes da chave |
| `LicenseCacheService.cs:267-306` | SHA256(`"VOLTRIS_CACHE_" + deviceId`) |
| `EnhancedLicenseSecurityService.cs:19-71, 345-350` | Fragments XOR + chave fixa `"V0LTR1S_SECURE_KEY"` |

AES sempre **CBC** (nunca autenticado/GCM), integridade com hash sem chave, e **fallbacks em Base64 puro** (`LicenseCacheService.cs:306`, `TrialProtectionService.cs:719`, `HardwareTrialService.cs:2244`, `EnhancedLicenseSecurityService.cs:160`) que "desligam" a criptografia silenciosamente.

### 2.6 Alta — Proteções anti-tamper que não bloqueiam

| Local | O que faz |
|---|---|
| `HardwareTrialService.cs:59-81` `AntiDebugCheck` | **Só loga** — não encerra |
| `HardwareTrialService.cs:119-146` `ValidateAssemblyIntegrity` | **Retorna `true` sempre** |
| `HardwareTrialService.cs:739-752` `ValidateResponseSignature` | **Retorna `true` sempre** |
| `Core\Security\RuntimeProtection.cs:301-316` | Só escreve `Logs\security.log` |
| `Security\AuthenticodeSignature.cs:18, 43-52` | Simula certificado com strings fixas, não verifica nada |

### 2.7 Alta — HWID fraco e trial resetável

- `LicenseManager.cs:715-721`: HWID = SHA256(MachineName + UserName + OSVersion) — valores públicos e mutáveis.
- `TrialProtectionService.cs:194-213`: apagar registro/arquivos → **novo trial de 7 dias infinito**.
- `LicenseOrchestrationService.cs:156` e vários outros: **fail-open** (sem rede/sem cache → acesso liberado).

### 2.8 Alta — Telemetria/PII exfiltrada via Telegram (token exposto)

- `Services\TelegramLogger.cs:13-14`: **bot token + chat ID hardcoded**.
- `App.xaml.cs:1015-1023`: a cada **inicialização** envia **usuário do Windows, modelo/clock de CPU, GPU, RAM, nome da rede, nome da máquina** para o chat Telegram.
- `App.xaml.cs:2107-2110`: envio no fechamento. `Program.cs:66`: crash com stack trace. `UpdateCheckerService.cs:203-207`: máquina+usuário.
- `LoggingService.cs:132` / `ProfessionalLogger.cs:77`: **todos os logs do app vão para o Telegram** (incluindo chaves de licença e HWIDs).
- O mesmo token aparece no instalador/desinstalador (`Instalador Voltris Optmizer\...\TelegramLogger.cs`).

**Risco:** é a assinatura clássica de *spyware*. Mesmo que seja seu próprio bot, para o Defender parece exfiltração de dados. E qualquer pessoa com o token posta no seu grupo.

### 2.9 Resumo de severidade (licenciamento)

| # | Achado | Severidade |
|---|---|---|
| 1 | `service_role` Supabase em texto claro (3 arquivos) | **Crítica** |
| 2 | Ativação offline sem assinatura (Pro 7d renovável) | **Crítica** |
| 3 | Estado Pro restaurado de JSON editável | **Crítica** |
| 4 | Secret de geração de licença hardcoded (64-bit) | **Crítica** |
| 5 | Criptografia local derivável / IV zero / fallback Base64 | Alta |
| 6 | Anti-debug/anti-tamper que só logam | Alta |
| 7 | HWID fraco + trial resetável + fail-open | Alta |
| 8 | PII exfiltrada via Telegram (token hardcoded) | Alta |
| 9 | HMAC/salts fixos no binário | Alta |
| 10 | RSA "seguro" com chaves placeholder | Alta |

---

## 3. OPERAÇÕES PERIGOSAS NO SISTEMA (por que o Defender bloqueia)

### 3.1 P0 — Driver de kernel não assinado (WinRing0)

- `Services\Performance\CpuTuning\Core\Backends\WinRing0\WinRing0Backend.cs`:
  - IOCTLs de **leitura/escrita de memória física** (linhas 30-32) e MSRs (596-708).
  - Copia `WinRing0x64.dll/.sys` para o output em runtime (94-137) e carrega no kernel (220, 252).
- Binários distribuídos **não assinados** em `WinRing0\*.sys` e **no instalador** (`artifacts_app\...\WinRing0.sys`).
- `Core\Bootstrapper.cs:189`: `WinRing0Backend` está **ativo** no DI.

**Risco:** é o **motivo nº 1 da quarentena** (`HackTool:Win32/WinRing0`). Acesso Ring 0 a memória física é vetor **BYOVD** (Bring-Your-Own-Vulnerable-Driver) — o exato padrão que o Microsoft Defender Hardening procura. Além disso, MSR/IO errado = **BSOD e possível dano de hardware**.

### 3.2 P0 — Escrita de MSR de voltagem/energia

- `Features\FivrService.cs:50-90`: escreve MSR 0x150 (OC_MAILBOX) — undervolt/overclock.
- `Core\Managers\PowerLimitManager.cs:480-519`: MSR 0x610 com lock bit 63 (linha 422-432 em `LowLevelHardwareService.cs`).
- `LowLevelHardwareService.cs:478`: `SetBdProchot` **desliga proteção térmica**.

**Risco:** undervolt/overclock errado pode **travar a CPU / causar dano físico**. Desligar BD PROCHOT remove proteção térmica.

### 3.3 P1 — PowerShell `-ExecutionPolicy Bypass` em massa

Padrão: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command/-File` com scripts gravados em `%TEMP%`. Exemplos:
- `Services\Tuning\PrivacyTuningService.cs:390-430` — remove Appx (AiFabric, WindowsIntelligence), desabilita Recall via `dism`.
- `Services\Tuning\SecurityTuningService.cs:160, 174, 515, 968-1060`.
- `Services\UltraCleanerService.cs:1492-1545, 6203-6218` — `Get-AppxPackage -AllUsers | Remove-AppxPackage`.
- `Services\WindowsUpdateService.cs:45-51, 864-960`.
- `Services\AdvancedRepairService.cs:1540` — `sc.exe sdset msiserver` (security descriptor de serviço).
- `UI\Views\RepairView.xaml.cs:826-858` — SFC.

**Risco:** `Bypass` + remover appx/pacotes é a assinatura mais comum de `Trojan:Script/Powershell`. Ataca funcionalidades do Windows 11 (Recall, pacotes AppX) sem consentimento granular.

### 3.4 P1 — Matar processos (32 ocorrências), incluindo do sistema

| Local | Alvo |
|---|---|
| `Services\Shield\Advanced\BehavioralMonitor.cs:368` | `Kill(true)` em qualquer processo classificado como "ameaça" pelo monitor próprio — **app antivírus dentro do app** |
| `Services\Gamer\Implementation\WindowsShellControlService.cs:172, 206` | `ShellExperienceHost` e `SearchHost` |
| `Services\Personalize\VoltrisBlurService.cs:276-277` | **`explorer.exe`** (kill + restart) |
| `Services\SmartRepair\Modules\ChkDskModule.cs:361` | timeout de chkdsk |
| `Services\Shield\QuarantineService.cs:120` + `ShieldViewModel.cs:1947` | qualquer processo segurando arquivo (Restart Manager) |
| `App.xaml.cs:885-890, 1819` | instâncias do próprio app (zumbis) |
| `GameRepairService.cs:270, 355` | árvore inteira do PowerShell de repair |

**Risco:** matar `explorer.exe`/`ShellExperienceHost` = **perda de janelas/arquivos não salvos**. Monitor comportamental próprio matando processos = agrava fortemente a detecção de AV.

### 3.5 P1 — Desabilitar serviços críticos do Windows

- `Services\Gamer\Optimization\WindowsServiceOptimizer.cs:46-129`: **60+ serviços**, incluindo `wuauserv` (Windows Update), `BITS`, `PrintSpooler`, `WSearch`, `SysMain`, `FrameServer`, `LanmanServer`, `RemoteRegistry`.
- `Services\Gamer\GamerModeManager\Services\KernelOptimizerService.cs:62-86, 395-404`: 22 serviços (inclui Windows Update, BITS).
- `Services\Tuning\PrivacyTuningService.cs:288-303`: DiagTrack, dmwappushservice, NvTelemetry etc.

**Risco:** desliga atualizações do Windows e serviços essenciais. Se o app não restaurar (crash/fechamento forçado), o sistema fica quebrado — e o usuário culpa o app.

### 3.6 P1 — Registro HKLM agressivo / MSI em massa

- `Services\AdvancedTweaksService.cs:383-439`: **`MSISupported=1` em dezenas de dispositivos PCI** — controladora incompatível = **dispositivo com falha**.
- `KernelOptimizerService.cs:196-205`: `Win32PrioritySeparation`, `QuantumLength`.
- `Core\PreparePc\Steps\RepairSteps.cs:224-322`: `reg.exe export`/`import` de **hive inteiro de serviços** (rollback).
- `UltraPerformanceService.cs:2061-2066`: **deleta entradas de `Run`** de outros programas.
- `UltraPerformanceService.cs:2117-2127`: `HiberbootEnabled`.

### 3.7 P1 — Rede / firewall / boot

- `AdvancedRepairService.cs:350-361`: `netsh winsock reset`, `ip reset`, `ipconfig /release /renew /flushdns`.
- `AdvancedRepairService.cs:394-445`: `Set-NetFirewallProfile ... -DefaultInboundAction Block` → **pode bloquear RDP/WinRM** (não há `New-NetFirewallRule`).
- `Performance\HpetController.cs:412-448` e `SmartRepair\Modules\HpetModule.cs`: **bcdedit `useplatformclock`** (HPET) — mexe no boot loader.

### 3.8 P1 — Download e execução remota

- `Services\Update\AutoUpdateService.cs:301-438`: baixa `VoltrisOptimizer_vX_Setup.exe` e **executa via script `.bat` em `cmd.exe`**.
- `GameRepairService.cs:1771-1789` + `GameRepairEnterprise\Providers\*`: baixa vcredist/DirectX/.NET/OpenAL e executa.
- `SecureDriverDownloader.cs:53-63, 198-214`: para intel/nvidia/amd/realtek retorna **`MOCK_SUCCESS`** (não baixa nada!) e remove MOTW de pacotes.

### 3.9 O que o código NÃO faz (verificado — não é malware no sentido clássico)

- ❌ Não desativa o Windows Defender (`UltraPerformanceService.cs:2480` força `DisableRealtimeMonitoring=0`).
- ❌ Não cria serviço de kernel no `DriverEngine.cpp` (sem `CreateService`).
- ❌ Não há injeção de código (`WriteProcessMemory`/`CreateRemoteThread`/hooks).
- ❌ Não há `New-NetFirewallRule`.

**Porém:** o conjunto de comportamentos (driver Ring0, Bypass PS, matar explorer, "telemetria" via Telegram) é **indistinguível de malware para os AVs**, mesmo sendo legítimo. **O signing Authenticode está desativado** (target comentado no csproj) → exe = "Unknown publisher".

---

## 4. ARQUITETURA — GOD OBJECTS, DUPLICAÇÃO E PLACEBOS

### 4.1 God objects (inviáveis de manter)

| Arquivo | Linhas |
|---|---|
| `Services\LocalizationService.cs` | **34.806** (traduções embutidas em código em vez de .resx/.json) |
| `Services\UltraCleanerService.cs` | 6.754 |
| `UI\MainWindow.xaml.cs` | 5.833 |
| `UI\ViewModels\GamerViewModel.cs` | 5.103 |
| `Services\UltraPerformanceService.cs` | 3.600 |
| `Services\License\HardwareTrialService.cs` | 2.507 |
| `UI\ViewModels\DashboardViewModel.cs` | 2.517 |
| `Core\Brain\V2\VoltrisBrainV2.cs` | 1.834 |

### 4.2 Duplicação massiva (o app tem N sistemas para a mesma coisa)

- **Detecção de jogos:** 5 implementações (GameDetectionService ×2, GameDetectorService, ContinuousGameDetector, GameRecognitionEngine).
- **"Cérebros"/IA:** 4+ (VoltrisBrainV2, OBrainService, UnifiedIntelligenceCore, RealIntelligenceService[obsoleto]).
- **Otimização de memória:** 8 implementações.
- **Scheduler de background:** 4 mecanismos concorrentes.
- **TelemetryService:** 3 versões.
- **SystemIntelligenceProfiler:** duplicado em Core e Services.

### 4.3 Placebos (dão prejuízo de confiança — piores para negócio)

| Local | O que faz de verdade |
|---|---|
| `Services\Enterprise\TelemetryService.cs` | Todos os métodos **vazios** (`Task.CompletedTask`) |
| `Core\Orchestration\UnifiedDecisionEngine.cs` | **Stub**, guardado por flag que nunca é `true` |
| `VoltrisPerformanceOptimizer.OptimizeStartupAsync/OptimizeServicesAsync` | Retornam "sucesso" **sem executar nada**; `OptimizeDiskAsync` é no-op |
| `SecureDriverDownloader` | Reporta **`MOCK_SUCCESS`** sem baixar driver nenhum |
| `Services\Shield\VoltrisShieldService.RunQuickScanAsync` | Etapa "análise heurística avançada" = **comentário vazio** |
| `Services\IntelligentAssistant.cs` | Execução do comando comentada; resposta fixa "processado temporariamente" |
| `PatternRecognitionService` | Treina com **dados sintéticos** quando <10 linhas reais |
| `Core\Configuration\FeatureFlagManager.cs` | URL "simulada" (comentário no código) |

### 4.4 Dependências NuGet não usadas

- `Silk.NET.Direct3D11` e `Silk.NET.DXGI` — zero usos.
- `Microsoft.Web.WebView2` — zero usos.
- `appsettings.json` — **nunca carregado** (DryRun no arquivo é ignorado).

### 4.5 Threading arriscado

- `.Wait()/.Result` (sync-over-async): `LowLevelHardwareService.cs`, `VoltrisDiagnosticSystem.cs`, `PowerPlanOrchestrator.cs`, `App.xaml.cs` — risco real de deadlock em WPF.
- ~15 `_ = Task.Run(...)` fire-and-forget no startup.
- ~50 `async void` no projeto.
- Failsafe `Environment.Exit(1)` após 30s no OnExit.
- Race condition declarada pelo próprio autor no `App.xaml.cs:1224-1227`.

### 4.6 Configuração fragmentada

4 mecanismos concorrentes (`VoltrisFeatureFlags`, `FeatureFlagManager`, `SettingsService`, `Config\`). O `VoltrisFeatureFlags` tem 9 flags **default false que ninguém liga** — todo o "V3" está morto por padrão. `SettingsService` guarda `OpenAIApiKey`/`GeminiApiKey` em **texto puro** no JSON.

### 4.7 Startup frágil

- `App.Services` estático + ~25 propriedades estáticas populadas **1,5s depois** do `MainWindow.Show()` — acesso antes disso retorna `null` silencioso.
- Inicialização em ~10 fases com `Task.Run`, semáforo, timeouts em cascata, splash failsafe de 45s.
- `GamerModeOrchestrator` **não pode ser registrado no DI** (causa deadlock) — registrado manualmente.

---

## 5. INSTALADOR / DESINSTALADOR / UPDATER

- `Instalador Voltris Optmizer\` (Installer + Uninstaller + Tests) — projeto separado compilado fora do csproj principal.
- `Update\VoltrisUpdater` — atualizador separado.
- **Bug conhecido já corrigido na main:** `System.FieldAccessException` em `LicenseService.CheckFeatureAccessAsync` (registrado no `Corrigir.txt` — acessar campo privado via reflection; confirma que há código tocando backing fields sem acesso).
- `CORRIGIR ACER.txt`: erro WMI 0x80070422 (serviço WMI desativado) em `BrainSensorHub.ReadTotalRamMB` e `HardwareCapabilityDetector` — o app **depende do serviço WMI** mas o próprio app desabilita serviços; em máquinas com WMI desligado o app erra.

---

## 6. PRIORIDADE DE CORREÇÃO — PLANO PARA VIRAR PRODUTO PAGO

### Fase 0 — URGENTE (segurança de dados seus, horas)
1. **Revogar/rotacionar a chave `service_role` do Supabase AGORA.** Trocá-la por uma `anon` com RLS + Edge Functions com validação server-side. Remover dos 3 arquivos + testes.
2. **Rotacionar o bot token do Telegram** e **remover PII** dos envios (manter só "app iniciou/versão").
3. **Remover chaves/segredos hardcoded** (`VOLTRIS_SECRET_LICENSE_KEY_2025`, `V0ltr1s_Hw1d_S3cr3t`, salts, `PROD_NVDL_KEY`/`PROD_AMD_TOKEN`).
4. **Corrigir `ValidateResponseSignature`/`ValidateAssemblyIntegrity`** (hoje retornam `true`) ou deletar — código que finge proteger é pior que não ter.

### Fase 1 — LICENÇA À PROVA (o que fazem pagar)
5. **Assinatura assimétrica real:** assinar a licença no servidor com **RSA/ECDSA privado** e verificar a **chave pública** no cliente. O formato da chave offline passa a ser irrelevante — sem assinatura válida, nada.
6. **Remover ativação offline por "formato"** — trocar por validação online + cache de duração limitada + **assinatura do payload de resposta** (como apps sérios fazem).
7. **Guardar o estado Pro só em local protegido/assinado**; parar de confiar em `license.json` editável ou registro forjável. Validar HWID real (múltiplos componentes) **contra o servidor**.
8. **Fail-closed no offline:** se não dá para validar, degrade para Trial (com aviso), não para Pro.
9. Deixar o `LicenseGenerator` **fora do app** (é hoje um CLI no mesmo repositório) e rodar só no seu servidor.

### Fase 2 — DEIXAR DE PARECER MALWARE (condição para assinar / whitelist)
10. **Remover WinRing0 do fluxo padrão.** Opções seguras: usar só `LibreHardwareMonitor` (já incluído) para leitura; para escrita de MSR, exigir **opt-in explícito + assinatura do driver** (assinatura EV ou kernel cert) e nunca distribuir .sys não assinado. Alternativa: transformar CPU tuning num **plugin opcional** não incluído no instalador.
11. **Substituir `PowerShell -ExecutionPolicy Bypass` por APIs nativas** (`reg.exe` com args, `sc.exe`, WMI/CIM, `ServiceController`, `DISM` via processo com args) e pedir confirmação por item.
12. **Nunca matar `explorer.exe`/`ShellExperienceHost`/`SearchHost` sem backup e restauração garantida;** reduzir o `BehavioralMonitor` (app-antivírus interno) a scan + quarentena com confirmação.
13. **Não desabilitar Windows Update/BITS/PrintSpooler** sem restaurar automaticamente no shutdown e alerta claro.
14. **Assinar o executável com Authenticode** (código de assinatura já existe no csproj, está comentado) e remover `MOCK_SUCCESS` de drivers.

### Fase 3 — VALOR QUE SE SENTE (para o usuário pagar)
15. **Mostrar resultado antes/depois real:** benchmark interno + "antes/depois" mensurável (FPS, latência, boot, RAM) para cada otimização. Placebo destrói o trial.
16. **Unificar as N implementações duplicadas** em uma por domínio (detecção de jogos, memória, scheduler, telemetria) — menos bugs, menos AV flags, mais rápido.
17. **Remover código morto**: Silk.NET, WebView2, flags mortas, `UnifiedDecisionEngine` stub, serviços `[Obsolete]`.
18. **Localização** em `.resx`/`.json` em vez de 34.806 linhas de dicionário C#.
19. **Trial com valor:** 7 dias com **todas** as features, mas **limite de "otimizações restantes"** e contagem regressiva honesta — converte melhor que feature cap.

### Fase 4 — JURÍDICO/PROFISSIONAL
20. EULA + tela de aceite, política de privacidade (obrigatória: você envia dados de hardware), registro no Brasil (LGPD).
21. `appsettings.json` de verdade (IConfiguration), log estruturado, testes unitários dos módulos de licença.

---

## 7. FATOS-CHAVE PARA VOCÊ DECIDIR

- **Build:** 0 erros, 3.993 avisos. O projeto **compila** hoje.
- **Funcionalidade real existe:** Q-Learning (Brain V2), limpeza de disco, monitor térmico/hardware, anti-adware, personalize, repair, overlay FPS, updater.
- **O principal bloqueador comercial é a classificação de malware**, causada por: WinRing0 (P0), PowerShell Bypass, kill de processos, Telegram PII.
- **O principal bloqueador de receita é a licença contornável** (service_role + fallback offline + JSON editável).
- **O principal bloqueador de retenção são os placebos** (sucesso falso de drivers e otimizações).
- **Não há assinatura Authenticode ativa** → todo download é "Unknown publisher" → usuário médio não instala.

---

## 8. ANDAMENTO DA CORREÇÃO — SESSÃO 02/08/2026 (CLIENT-SIDE)

> Fases 0 e 1 — parte **client-side** concluída e compilando (0 erros). A parte **server-side** fica pendente (passos manuais no painel do Supabase).

### Concluído (código do cliente)
1. **`service_role` removida de todo o cliente.** Substituída pela chave `anon` centralizada em `Services\License\SupabaseConfig.cs`. Arquivos corrigidos: `LicenseApiService.cs`, `LicenseManager.cs` (construtor), `HardwareTrialService.cs`. Em `SecurityTestingUtility.cs` as cópias da chave real viraram JWT dummy (só testes de mascaramento).
2. **Verificação de assinatura RSA de licenças** (`Services\License\LicenseSignatureVerifier.cs`, chave pública embutida, privada só no servidor):
   - `LicenseManager.ActivateLicenseAsync` — rejeita chave sem assinatura válida.
   - `LicenseManager.ActivateLicenseOfflineAsync` — **fail-closed**: sem assinatura válida, não ativa nem offline.
   - `LicenseTokenStore.TryRestoreFromRegistry` — JSON/registro com chave não assinada é **ignorado**; a data de validade da chave é **autoritativa** (impede editar `expires_at`).
   - `RevalidateWithServerAsync` — resposta `valid:true` sem assinatura → revoga localmente.
3. **Verificação de assinatura das respostas do servidor** (ativação e validação) em `LicenseApiService.cs` e no fluxo de ativação do `LicenseManager` — impede resposta forjada via MITM.
4. **Parse robusto da chave** `VOLTRIS-PLANO-ID-DATA-SIG`: SIG tolera base64 e base64url (contém `-`), não usa `Split('-')` ingênuo.
5. `ServerSideLicenseValidator.cs` passou a enviar headers `apikey`/`Authorization` com a chave anon.

### Pendente — você (server-side, passos manuais)
- **Rotacionar a chave `service_role`** no painel do Supabase (já exposta publicamente) e **regenerar a chave `anon`**.
- **Colocar a chave `anon` real** em `Services\License\SupabaseConfig.cs` (constante `FallbackAnonKey`, hoje `"COLE_AQUI_SUA_CHAVE_ANON_PUBLICA"`) ou via variável de ambiente `VOLTRIS_SUPABASE_ANON_KEY`.
- **Atualizar as Edge Functions** para: validar com RLS (não service_role) e **assinar** licenças (`VOLTRIS-PLANO-ID-DATA-SIG` com RSA-SHA256) e respostas (`signature` = RSA-SHA256 do payload canônico em `LicenseSignatureVerifier.BuildActivationPayload`/`BuildValidationPayload`). A chave privada gerada nesta sessão está em `C:\Users\VOLTRIS\AppData\Local\Temp\opencode\voltris_private.pem`.
- **Gerador**: substituir o HMAC do `LicenseGenerator\Program.cs:19` pelo par RSA novo.
- **Deferido (Fase 2+):** telemetria Telegram com PII (`Services\TelegramLogger.cs`, `App.xaml.cs:1015-1023,2107-2110`), remoção do secret em `LicenseGenerator\Program.cs:19`, itens de malware/placebo do relatório.

> **Atenção:** com essa blindagem, **chaves antigas (sem assinatura RSA) deixam de funcionar**. Antes de distribuir, emita novas chaves com o gerador server-side atualizado.

### Concluído após a sessão — WinRing0 removido do build (Fase 2 parcial)
- Removida a regra `<None Update="WinRing0\**\*.*">` do `VoltrisOptimizer.csproj` (copiaba WinRing0.dll/.sys/.vxd para o output).
- Deletados os binários do driver (`WinRing0\`) e o código do backend (`Services\Performance\CpuTuning\Core\Backends\WinRing0\`).
- `WinRing0Backend` **desativado** no DI (`Core\Bootstrapper.cs`) e em `CpuGamingOptimizerService.cs`; substituído por `SafeFallbackBackend` (`Core\Backends\SafeFallbackBackend.cs`) — sem driver, sem MSR/MMIO, vendor via `PROCESSOR_IDENTIFIER`.
- **Verificado:** `FileListAbsolute.txt` da build não lista mais WinRing0 e não há nenhum `WinRing0*` no projeto. Build: 0 erros.
- **Consequência:** ajuste fino de CPU via MSR/MMIO (power limits estilo ThrottleStop) fica desativado até existir uma alternativa assinada; telemetria continua via camada segura.

---

*Fim do relatório. Todos os caminhos referem-se a `D:\APLICATIVO VOLTRIS`. Linhas citadas foram verificadas na leitura dos fontes.*
