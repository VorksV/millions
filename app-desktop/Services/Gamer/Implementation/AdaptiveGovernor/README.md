# Adaptive Governor - Documentação Técnica

## 📋 Visão Geral

O Adaptive Governor é um sistema de governança adaptativa REAL que monitora o sistema em tempo real e toma decisões baseadas em métricas reais, aplicando apenas ações seguras e reversíveis.

## 🏗️ Arquitetura

### Componentes Principais

1. **TelemetryLayer** (`TelemetryLayer.cs`)
   - Coleta métricas reais do sistema (somente leitura)
   - Usa PerformanceCounters, WMI, TelemetryService, FpsReader
   - Nunca modifica nada

2. **DecisionEngine** (`DecisionEngine.cs`)
   - Analisa métricas e decide se ação é necessária
   - Usa thresholds claros e documentados
   - Nunca aplica ações se métricas estiverem normais

3. **ActionLayer** (`ActionLayer.cs`)
   - Aplica ações seguras e reversíveis
   - Apenas ações permitidas: prioridade, afinidade, limpeza de memória
   - Nunca altera registry crítico ou configurações permanentes

4. **RollbackManager** (`RollbackManager.cs`)
   - Registra todas as ações aplicadas
   - Reverte automaticamente em caso de:
     - Saída do jogo
     - Desativação do Modo Gamer
     - Crash
     - Erro inesperado

5. **SafetySystem** (`SafetySystem.cs`)
   - Watchdog de estabilidade
   - Detecta degradação de performance
   - Reverte ações automaticamente se FPS piorar
   - Desativa governor se muitas falhas consecutivas

6. **AdaptiveGovernorService** (`AdaptiveGovernorService.cs`)
   - Orquestra todos os componentes
   - Loop adaptativo principal
   - Gerencia estado e modo de operação

## 🔄 Fluxo de Operação

```
1. Start() → Inicializa componentes
2. Loop de Monitoramento:
   a. Coletar métricas (TelemetryLayer)
   b. Verificar estabilidade (SafetySystem)
   c. Se degradação → Rollback
   d. Se modo MONITOR → Apenas logar
   e. Se modo ACTIVE → Analisar e decidir (DecisionEngine)
   f. Se decisão positiva → Executar ação (ActionLayer)
   g. Registrar rollback (RollbackManager)
   h. Aguardar intervalo
3. Stop() → Reverter todas as ações
```

## 🎮 Modos de Operação

### OFF (Padrão)
- Nada acontece
- Governor não inicia

### MONITOR (Learning Mode)
- Apenas coleta métricas
- Apenas gera logs
- Nenhuma ação aplicada
- Usado para validação e aprendizado

### ACTIVE
- Aplica decisões adaptativas reais
- Com todos os sistemas de segurança ativos
- Ações são revertidas automaticamente se degradarem performance

## ⚙️ Ações Disponíveis

### ✅ Ações Seguras (Implementadas)

1. **IncreaseGamePriority**
   - Aumenta prioridade do processo do jogo para High
   - Nunca usa RealTime (muito perigoso)
   - Reversível

2. **ReduceBackgroundPriorities**
   - Reduz prioridade de processos em background
   - Usa ProcessPrioritizer existente
   - Reversível

3. **CleanStandbyList**
   - Limpa standby list de memória
   - Efeito temporário (não precisa rollback)
   - Seguro

4. **AdjustAffinity**
   - Ajusta afinidade de CPU
   - Apenas em CPUs com 8+ cores
   - Reserva core 0 para sistema
   - Reversível

### ❌ Ações NÃO Permitidas

- Alterar registry crítico
- Mexer em TDR
- Alterar clocks
- Alterar voltagem
- Aplicar tweaks permanentes
- Fechar processos do sistema

## 📊 Thresholds e Decisões

### Thresholds Configuráveis

```csharp
FpsLowThreshold = 30.0              // FPS < 30 = baixo
FpsTargetThreshold = 60.0            // FPS alvo
FrameTimeStutterThreshold = 33.33    // Frame time > 33.33ms = stutter
CpuUsageHighThreshold = 85.0         // CPU > 85% = alta
CpuUsageLowThreshold = 40.0         // CPU < 40% = baixa
GpuUsageHighThreshold = 90.0         // GPU > 90% = alta
GpuUsageLowThreshold = 50.0         // GPU < 50% = baixa
RamUsageHighThreshold = 85.0         // RAM > 85% = alta
ProcessorQueueLengthThreshold = 4.0  // Queue > 4 = CPU saturado
DpcPercentThreshold = 5.0           // DPC > 5% = drivers problemáticos
InterruptPercentThreshold = 3.0      // Interrupt > 3% = hardware problemático
PageFaultsHighThreshold = 1000.0     // Page faults > 1000/s = memória insuficiente
```

### Lógica de Decisão

1. **FPS Baixo + CPU/GPU Subutilizados**
   → Aumentar prioridade do jogo

2. **FPS Baixo + CPU Saturado**
   → Reduzir prioridade de processos em background

3. **FPS Baixo + GPU Saturado + RAM Alta**
   → Limpar standby list

4. **Stutter Detectado + CPU Queue Alta**
   → Reduzir prioridade de processos em background

5. **Stutter Detectado + DPC Alto**
   → Reduzir prioridade de processos em background

6. **Stutter Detectado + Page Faults Alto**
   → Limpar standby list

7. **RAM Alta + Page Faults Alto**
   → Limpar standby list

## 🛡️ Sistema de Segurança

### Watchdog de Estabilidade

- Monitora FPS, CPU, GPU continuamente
- Detecta degradação de 15% ou mais
- Reverte ações automaticamente se degradar
- Desativa governor após 3 falhas consecutivas

### Cooldown entre Ações

- Mínimo 5 segundos entre ações
- Evita ações muito frequentes
- Permite que ações tenham efeito antes de nova decisão

### Rollback Automático

- Todas as ações são registradas
- Rollback executado em:
  - Stop()
  - Crash
  - Degradação detectada
  - Dispose()

## 📝 Logs e Transparência

Todos os eventos são logados:

- Métricas coletadas
- Decisões tomadas
- Ações aplicadas
- Ações revertidas
- Motivo da decisão
- Resultado (melhorou/piorou)
- Stutters detectados

## 🔌 Integração

### Com Modo Gamer

- Só roda se Modo Gamer estiver ativo
- Só roda se jogo estiver detectado
- Para automaticamente ao sair do jogo
- Nunca interfere fora do gameplay

### Com Serviços Existentes

- Usa `TelemetryService` para métricas de GPU
- Usa `FpsReader` para FPS
- Usa `ThermalMonitorService` para temperaturas
- Usa `ProcessPrioritizer` para prioridades
- Usa `MemoryGamingOptimizer` para limpeza de memória
- **NÃO duplica funcionalidades existentes**

## ⚠️ Limitações e Observações

1. **FPS pode não estar disponível** para todos os jogos
   - Governor funciona mesmo sem FPS
   - Usa frame time e outras métricas

2. **Temperatura pode não estar disponível** para todas as GPUs
   - Governor funciona sem temperatura
   - Usa outras métricas para decisões

3. **Ações são conservadoras**
   - Prioriza estabilidade sobre performance máxima
   - Nunca aplica ações agressivas

4. **Modo ACTIVE requer validação**
   - Recomendado usar modo MONITOR primeiro
   - Validar métricas antes de ativar ACTIVE

## 🚀 Como Usar

### Habilitar Adaptive Governor

1. No Modo Gamer, habilitar "Enable Anti-Stutter" nas opções
2. Governor iniciará em modo MONITOR por padrão
3. Para ativar modo ACTIVE, usar `SetMode(GovernorMode.Active)`

### Monitorar Logs

- Verificar logs para entender decisões
- Validar que ações estão sendo aplicadas corretamente
- Verificar que rollbacks estão funcionando

### Desabilitar

- Desabilitar "Enable Anti-Stutter" nas opções
- Ou chamar `Stop()` manualmente
- Todas as ações serão revertidas automaticamente

## ✅ Checklist de Segurança

- [x] Todas as ações são reversíveis
- [x] Rollback automático implementado
- [x] Watchdog de estabilidade ativo
- [x] Cooldown entre ações
- [x] Desativação automática após falhas
- [x] Não altera registry crítico
- [x] Não mexe em TDR
- [x] Não altera clocks/voltagem
- [x] Não fecha processos do sistema
- [x] Modo OFF por padrão
- [x] Logs transparentes e detalhados
- [x] Integração sem duplicar funcionalidades

## 📚 Referências

- `TelemetryLayer.cs` - Coleta de métricas
- `DecisionEngine.cs` - Lógica de decisão
- `ActionLayer.cs` - Execução de ações
- `RollbackManager.cs` - Gerenciamento de rollback
- `SafetySystem.cs` - Sistema de segurança
- `AdaptiveGovernorService.cs` - Orquestrador principal

