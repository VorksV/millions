using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoltrisOptimizer.Services
{
    public class HierarchicalProgressEventArgs : EventArgs
    {
        public string OperationName { get; }
        public string SubTaskName { get; }

        /// <summary>
        /// Descrição do QUE está sendo feito agora, no nível do item concreto
        /// ("Cache do Windows", "Arquivos temporários", "Rede"...).
        ///
        /// Antes desta propriedade o rodapé só conseguia mostrar o NOME da etapa
        /// ("Limpando") e a porcentagem dela. O detalhe do item existia, mas só
        /// ia para o botão circular — o usuário ficava olhando "- Limpando (54%)"
        /// sem saber o que estava sendo limpo.
        /// </summary>
        public string Detail { get; }

        public int GlobalPercentage { get; }
        public int SubTaskPercentage { get; }
        public TimeSpan TimeElapsed { get; }
        public TimeSpan? EstimatedTimeRemaining { get; }

        /// <summary>
        /// [FIX:END-STATE] Esta é a EMISSÃO TERMINAL da operação.
        ///
        /// Antes o rodapé não tinha como distinguir "ainda trabalhando na etapa
        /// Otimizando" de "a operação inteira acabou": os dois eventos tinham o
        /// mesmo formato. O efeito era o rodapé mostrar "Otimizando..." ao lado
        /// de uma barra em 100% — ou seja, dizer que ainda estava otimizando no
        /// exato instante em que havia terminado.
        ///
        /// Com o sinal explícito, a interface escolhe a mensagem de conclusão em
        /// vez do nome da última etapa.
        /// </summary>
        public bool IsCompleted { get; }

        public HierarchicalProgressEventArgs(string operationName, string subTaskName, string detail, int globalPercentage, int subTaskPercentage, TimeSpan timeElapsed, TimeSpan? estimatedTimeRemaining, bool isCompleted = false)
        {
            OperationName = operationName;
            SubTaskName = subTaskName;
            Detail = detail ?? string.Empty;
            GlobalPercentage = Math.Clamp(globalPercentage, 0, 100);
            SubTaskPercentage = Math.Clamp(subTaskPercentage, 0, 100);
            TimeElapsed = timeElapsed;
            EstimatedTimeRemaining = estimatedTimeRemaining;
            IsCompleted = isCompleted;
        }
    }

    public class ProgressSubTask
    {
        public string Name { get; }
        public double Weight { get; }
        public int Progress { get; set; }

        public ProgressSubTask(string name, double weight)
        {
            Name = name;
            Weight = weight;
            Progress = 0;
        }
    }

    public class ProgressOperation
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; }
        public DateTime StartTime { get; } = DateTime.UtcNow;
        public List<ProgressSubTask> SubTasks { get; } = new List<ProgressSubTask>();
        public ProgressSubTask? CurrentSubTask { get; set; }
        public bool IsCompleted { get; set; }

        /// <summary>
        /// Descrição do item concreto em andamento, mostrada no rodapé.
        /// Ver <see cref="HierarchicalProgressEventArgs.Detail"/>.
        /// </summary>
        public string Detail { get; set; } = string.Empty;

        public ProgressOperation(string name)
        {
            Name = name;
        }

        public void AddSubTask(string name, double weight)
        {
            SubTasks.Add(new ProgressSubTask(name, weight));
        }

        /// <summary>
        /// Maior progresso já publicado nesta operação. A barra é
        /// MONOTÔNICA por contrato: uma vez exibido, o percentual nunca
        /// retrocede, mesmo que a média ponderada caia.
        /// </summary>
        public int _peakProgress;

        /// <summary>
        /// Progresso global da operação, como média ponderada das etapas.
        /// <para>
        /// MONOTÔNICO: o resultado é limitado por <see cref="_peakProgress"/>,
        /// que guarda o maior valor já publicado. Isso é necessário porque a
        /// média ponderada <b>pode cair</b> em transições legítimas:
        /// ao final de uma etapa o agregado sobe, e quando a etapa seguinte
        /// começa a reportar, ela entra com 0 — mas enquanto ela sobe, o
        /// agregado cresce. O caso que realmente quebrava a barra era outro:
        /// uma etapa registrada com peso mas nunca executada (por exemplo a
        /// etapa de DNA, cujo <c>CompleteSubTask</c> era chamado sem ter sido
        /// registrada) mantinha o denominador inflado com peso morto, e a
        /// barra oscilava sem convergir. O teto de pico elimina a oscilação
        /// visível; o peso morto é tratado em <see cref="CompleteOperation"/>.
        /// </para>
        /// </summary>
        public int CalculateGlobalProgress()
        {
            if (SubTasks.Count == 0) return 0;
            double totalWeight = SubTasks.Sum(s => s.Weight);
            if (totalWeight == 0) return 0;

            double weightedProgress = SubTasks.Sum(s => (s.Progress / 100.0) * s.Weight);
            int calculated = (int)Math.Clamp((weightedProgress / totalWeight) * 100, 0, 100);

            if (calculated > _peakProgress)
                _peakProgress = calculated;

            return _peakProgress;
        }
    }

    public class ProgressTrackingService
    {
        private static ProgressTrackingService? _instance;
        private static readonly object _singletonLock = new object();

        public static ProgressTrackingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLock)
                    {
                        _instance ??= new ProgressTrackingService();
                    }
                }
                return _instance;
            }
        }

        public event EventHandler<HierarchicalProgressEventArgs>? ProgressUpdated;
        public event EventHandler? OperationCompleted;

        private ProgressOperation? _currentOperation;
        private readonly object _operationLock = new object();
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private Timer? _uiUpdateTimer;
        private int _lastLoggedPercentage = -1;

        private ILoggingService? Logger => _logger ??= TryGetLogger();
        private ILoggingService? _logger;
        private static ILoggingService? TryGetLogger()
        {
            try { return App.LoggingService; } catch { return null; }
        }

        private ProgressTrackingService() { }

        public ProgressOperation StartOperation(string operationName)
        {
            lock (_operationLock)
            {
                _currentOperation = new ProgressOperation(operationName);
                _lastLoggedPercentage = -1;
                
                _uiUpdateTimer?.Dispose();
                _uiUpdateTimer = new Timer(_ => EmitUpdate(), null, 1000, 1000);
                
                Logger?.LogInfo($"[PROGRESS] Operation={operationName} State=Started");
                return _currentOperation;
            }
        }

        public void RegisterSubTasks(Guid operationId, params (string name, double weight)[] subTasks)
        {
            lock (_operationLock)
            {
                if (_currentOperation == null || _currentOperation.Id != operationId) return;
                foreach (var task in subTasks)
                {
                    _currentOperation.AddSubTask(task.name, task.weight);
                }
            }
        }

        public void StartSubTask(Guid operationId, string subTaskName)
        {
            lock (_operationLock)
            {
                if (_currentOperation == null || _currentOperation.Id != operationId || _currentOperation.IsCompleted) return;

                var target = _currentOperation.SubTasks.FirstOrDefault(t => t.Name == subTaskName);
                if (target != null)
                {
                    _currentOperation.CurrentSubTask = target;
                    Logger?.LogInfo($"[PROGRESS] Operation={_currentOperation.Name} Stage={subTaskName} State=Started");
                    EmitUpdate();
                }
                else
                {
                    // Antes este caso saía em silêncio. Foi exatamente assim que a
                    // etapa de DNA ficou executando sem mover a barra: ela nunca
                    // tinha sido registrada, e o FirstOrDefault devolvia null sem
                    // deixar rastro. Registrar o erro de contrato é o que impede
                    // que a mesma falha volte em silêncio.
                    Logger?.LogWarning(
                        $"[PROGRESS] Operation={_currentOperation.Name} Stage={subTaskName} NÃO EXISTE nas etapas registradas " +
                        $"({string.Join(", ", _currentOperation.SubTasks.Select(s => s.Name))}). A etapa não vai mover a barra.");
                }
            }
        }

        /// <summary>
        /// Atualiza o progresso de uma etapa. Retorna <c>false</c> quando a
        /// etapa não existe na operação — antes esse caso era indistinguível
        /// do sucesso, o que escondia bugs de contrato.
        /// </summary>
        public bool UpdateSubTaskProgress(Guid operationId, string subTaskName, int percentage)
        {
            bool shouldEmit = false;
            bool updated;

            lock (_operationLock)
            {
                updated = false;
                if (_currentOperation == null || _currentOperation.Id != operationId || _currentOperation.IsCompleted)
                {
                    return false;
                }

                var target = _currentOperation.SubTasks.FirstOrDefault(t => t.Name == subTaskName);
                if (target != null)
                {
                    // A etapa também é monotônica: relatar um valor menor do que
                    // ela já tinha (por exemplo um callback atrasado de uma etapa
                    // anterior chegando depois do seguinte) faria a barra recuar.
                    int clamped = Math.Clamp(percentage, 0, 100);
                    bool isRegression = clamped < target.Progress;

                    if (!isRegression)
                    {
                        target.Progress = clamped;
                        _currentOperation.CurrentSubTask = target;
                    }

                    // Rate limiting: 10 FPS (100ms)
                    if ((DateTime.UtcNow - _lastUpdateTime).TotalMilliseconds > 100 || clamped == 100)
                    {
                        shouldEmit = true;
                        _lastUpdateTime = DateTime.UtcNow;
                    }

                    updated = true;
                }
            }

            if (shouldEmit)
            {
                EmitUpdate();
            }

            return updated;
        }

        /// <summary>
        /// Publica o detalhe do que está sendo feito AGORA ("Cache do Windows",
        /// "Rede"...), sem alterar a porcentagem.
        ///
        /// Existe separado de <see cref="UpdateSubTaskProgress"/> porque são dois
        /// eixos independentes: o quanto e o quê. O chamador já sabe quanto
        /// progrediu (o contador da etapa) e agora precisa poder dizer o quê,
        /// quantas vezes quiser, sem resetar opercentage para o passo anterior.
        ///
        /// Ignora chamadas de operações desconhecidas em vez de lançar: o
        /// chamador está num callback de limpeza que pode disparar depois da
        /// operação ter sido encerrada.
        /// </summary>
        public void SetOperationDetail(Guid operationId, string detail)
        {
            lock (_operationLock)
            {
                if (_currentOperation == null || _currentOperation.Id != operationId || _currentOperation.IsCompleted)
                    return;

                _currentOperation.Detail = detail ?? string.Empty;
            }
        }

        public void CompleteSubTask(Guid operationId, string subTaskName)
        {
            if (!UpdateSubTaskProgress(operationId, subTaskName, 100))
            {
                Logger?.LogWarning($"[PROGRESS] Operation={_currentOperation?.Name} Stage={subTaskName} concluída sem estar registrada — nada a atualizar.");
                return;
            }
            Logger?.LogInfo($"[PROGRESS] Operation={_currentOperation?.Name} Stage={subTaskName} State=Completed");
        }

        public void CompleteOperation(Guid operationId)
        {
            lock (_operationLock)
            {
                if (_currentOperation == null || _currentOperation.Id != operationId) return;
                
                foreach(var sub in _currentOperation.SubTasks)
                {
                    sub.Progress = 100;
                }
                _currentOperation.IsCompleted = true;
                
                _uiUpdateTimer?.Dispose();
                _uiUpdateTimer = null;
                
                Logger?.LogInfo($"[PROGRESS] Operation={_currentOperation.Name} State=Completed Duration={(DateTime.UtcNow - _currentOperation.StartTime).TotalSeconds:F1}s");
                EmitUpdate(forceComplete: true);
                
                // Clear state after a short delay
                Task.Delay(2000).ContinueWith(_ => 
                {
                    lock (_operationLock)
                    {
                        if (_currentOperation != null && _currentOperation.Id == operationId)
                        {
                            _currentOperation = null;
                            Application.Current?.Dispatcher.InvokeAsync(() => OperationCompleted?.Invoke(this, EventArgs.Empty));
                        }
                    }
                });
            }
        }

        public void CancelOperation(Guid operationId)
        {
             lock (_operationLock)
            {
                if (_currentOperation == null || _currentOperation.Id != operationId) return;
                Logger?.LogInfo($"[PROGRESS] Operation={_currentOperation.Name} State=Cancelled");
                
                _uiUpdateTimer?.Dispose();
                _uiUpdateTimer = null;
                
                _currentOperation = null;
                Application.Current?.Dispatcher.InvokeAsync(() => OperationCompleted?.Invoke(this, EventArgs.Empty));
            }
        }

        private void EmitUpdate(bool forceComplete = false)
        {
            ProgressOperation? op = null;
            lock (_operationLock)
            {
                op = _currentOperation;
            }

            if (op == null) return;

            int globalPct = forceComplete ? 100 : op.CalculateGlobalProgress();
            string subName = op.CurrentSubTask?.Name ?? LocalizationService.Instance.GetString("ProgressFinishing");
            int subPct = forceComplete ? 100 : (op.CurrentSubTask?.Progress ?? 0);

            // Na conclusão não há mais "o que está sendo feito": mostrar o último
            // item repetiria "Limpando: Cache" com a barra em 100%, o que é
            // factualmente falso e confunde. String vazia deixa o rodapé cair no
            // rótulo de etapa.
            string detail = forceComplete ? string.Empty : (op.Detail ?? string.Empty);

            
            var elapsed = DateTime.UtcNow - op.StartTime;
            TimeSpan? remaining = null;

            if (globalPct > 0 && globalPct < 100)
            {
                double totalMs = elapsed.TotalMilliseconds;
                double msPerPercent = totalMs / globalPct;
                double remainingMs = msPerPercent * (100 - globalPct);
                remaining = TimeSpan.FromMilliseconds(remainingMs);
            }

            if (globalPct % 5 == 0 && globalPct != _lastLoggedPercentage) // Loga a cada 5% para n poluir demais
            {
                // [FIX:PROGRESS-LOG] ESTE É O LOG QUE FALTAVA.
                //
                // A linha ficava em LogTrace, que o LoggingService não persiste —
                // o arquivo de log só grava Trace a partir de um nível que não é
                // o padrão. Resultado: as porcentagens NUNCA apareciam em disco,
                // e qualquer diagnóstico do comportamento da barra era
                // impossível. Foi exatamente o que impediu de fechar a causa do
                // "preso em 100%" na primeira tentativa: o log mostrava as
                // transições de etapa, mas nenhum número.
                //
                // Agora vai para Info, junto das transições, no mesmo formato, e
                // ainda com a etapa e o item. É a linha que fecha o ciclo: se
                // amanhã a barra se comportar errado, o número está no arquivo.
                Logger?.LogInfo(
                    $"[PROGRESS] Operation={op.Name} Stage={subName} " +
                    $"Item={(string.IsNullOrEmpty(detail) ? "-" : detail)} " +
                    $"Global={globalPct}% Stage={subPct}% ETA={remaining?.ToString(@"mm\:ss") ?? "N/A"}");
                _lastLoggedPercentage = globalPct;
            }

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ProgressUpdated?.Invoke(this, new HierarchicalProgressEventArgs(
                    op.Name, subName, detail, globalPct, subPct, elapsed, remaining, forceComplete
                ));
            }, DispatcherPriority.Background);
        }
    }
}
