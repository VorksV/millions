using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VoltrisOptimizer.Services
{
    public enum OperationState
    {
        Pending,
        Running,
        Suspended,
        Completed,
        Failed,
        Cancelled
    }

    public class ProgressEventArgs : EventArgs
    {
        public int Percentage { get; }
        public ProgressEventArgs(int percentage)
        {
            Percentage = Math.Max(0, Math.Min(100, percentage));
        }
    }

    public class OperationContext
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string Message { get; set; } = string.Empty;
        public OperationState State { get; set; } = OperationState.Pending;
        public bool IsPriority { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public bool IsCompleted { get; set; }
    }

    public sealed class OperationToken : IDisposable
    {
        private readonly Guid _id;
        private readonly GlobalProgressService _service;
        internal int _completed = 0;

        /// <summary>
        /// Id REAL da tarefa registrada. É este id que UpdateProgress/Complete/Fail
        /// precisam usar.
        ///
        /// Antes o token carregava apenas um Guid aleatório e os métodos
        /// Internal* ignoravam esse Guid, terminando sempre a PRIMEIRA tarefa
        /// ativa da lista. Resultado: todo `using var token = BeginOperation(...)`
        /// completava a tarefa errada e a sua própria ficava viva para sempre —
        /// 39 chamadas no app Affected, e o rodapé travava com a mensagem da
        /// tarefa que ninguém tinha concluído.
        /// </summary>
        private readonly string _taskId;

        internal Guid Id => _id;
        internal string TaskId => _taskId;
        public string OperationName { get; }
        public bool IsCompleted => _completed == 1;

        internal OperationToken(Guid id, GlobalProgressService service, string name, string taskId = "")
        {
            _id = id;
            _service = service;
            _taskId = taskId ?? string.Empty;
            OperationName = name;
        }

        public void UpdateProgress(int percentage, string? message = null)
        {
            if (_completed == 1) return;
            _service.InternalUpdate(_taskId, percentage, message);
        }

        public void Complete(string? finalMessage = null)
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
            {
                _service.InternalComplete(_taskId, finalMessage);
            }
        }

        public void Fail(string? errorMessage = null)
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
            {
                _service.InternalFail(_taskId, errorMessage);
            }
        }

        public void Dispose()
        {
            Complete();
        }
    }

    public class GlobalProgressService
    {
        private static GlobalProgressService? _instance;
        private static readonly object _singletonLock = new object();

        public static GlobalProgressService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLock)
                    {
                        _instance ??= new GlobalProgressService();
                    }
                }
                return _instance;
            }
        }

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationCompleted;
        public event EventHandler<TaskStateChangedEventArgs>? TaskStateChanged;

        private readonly GlobalProgressManager _manager;
        private readonly Dictionary<string, TaskToken> _tokenMap = new(StringComparer.OrdinalIgnoreCase);

        private ILoggingService? Logger => _logger ??= TryGetLogger();
        private ILoggingService? _logger;
        private static ILoggingService? TryGetLogger()
        {
            try { return App.LoggingService; } catch { return null; }
        }

        private GlobalProgressService()
        {
            _manager = GlobalProgressManager.Instance;
            _manager.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, e);
            _manager.StatusChanged += (s, e) =>
            {
                var localized = LocalizeProgressMessage(e);
                StatusChanged?.Invoke(this, localized);
            };
            _manager.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, e);
            _manager.TaskStateChanged += (s, e) => TaskStateChanged?.Invoke(this, e);
        }

        public OperationToken BeginOperation(string operationName, bool isPriority = false)
        {
            // O token precisa carregar o id REAL da tarefa. Antes devolvia um
            // Guid aleatorio, e Complete()/Dispose() acabavam encerrando a
            // primeira tarefa ativa da lista em vez desta.
            var token = _manager.RegisterTask(operationName, "", isPriority ? 1 : 0);

            lock (_tokenMap)
                _tokenMap[operationName] = token;

            return new OperationToken(Guid.NewGuid(), this, operationName, token.TaskId);
        }

        public bool StartOperation(string operationName, bool isPriority = false)
        {
            // Reaproveita a tarefa ativa com o mesmo nome.
            // Antes criava uma NOVA entrada a cada chamada, então operações
            // repetidas se acumulavam e UpdateProgress/CompleteOperation
            // passavam a mirar em tarefas diferentes.
            var existing = _manager.GetActiveTasks().FirstOrDefault(t =>
                t.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Recomeça do zero sem criar entrada duplicada.
                _manager.UpdateProgress(existing.TaskId, 0, operationName);
                return true;
            }

            var token = _manager.RegisterTask(operationName, "", isPriority ? 1 : 0);

            lock (_tokenMap)
                _tokenMap[operationName] = token;

            return true;
        }

        public void UpdateProgress(int percentage, string? message = null)
        {
            // Tem que mirar na MESMA tarefa que CompleteOperation vai encerrar.
            // Antes usava FirstOrDefault() (a mais antiga) enquanto
            // CompleteOperation usava a mais recente de maior prioridade —
            // com mais de uma ativa, o progresso ia para uma e a conclusão
            // para outra, e a barra nunca carregava.
            var current = GetCurrentTask();
            if (current != null)
            {
                _manager.UpdateProgress(current.TaskId, percentage, message);
            }
        }

        private ActiveTaskInfo? GetCurrentTask()
        {
            var active = _manager.GetActiveTasks();
            if (active.Count == 0) return null;
            return active.OrderByDescending(t => t.Priority)
                         .ThenByDescending(t => t.StartTime)
                         .FirstOrDefault();
        }

        public void CompleteOperation(string? finalMessage = null)
        {
            var active = _manager.GetActiveTasks();
            var current = active.OrderByDescending(t => t.Priority)
                                .ThenByDescending(t => t.StartTime)
                                .FirstOrDefault();
            if (current != null)
            {
                _manager.InternalComplete(current.TaskId, finalMessage);
            }
        }

        public void FailOperation(string? errorMessage = null)
        {
            var active = _manager.GetActiveTasks();
            var current = active.OrderByDescending(t => t.Priority)
                                .ThenByDescending(t => t.StartTime)
                                .FirstOrDefault();
            if (current != null)
            {
                _manager.InternalFail(current.TaskId, errorMessage);
            }
        }

        public void FinalizeOperation(string operationName, string? finalMessage = null)
        {
            var active = _manager.GetActiveTasks();
            var task = active.FirstOrDefault(t =>
                t.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase));
            if (task != null)
            {
                _manager.InternalComplete(task.TaskId, finalMessage);
            }
        }

        public void ResetQueue()
        {
            _manager.ResetAll();
        }

        public void CancelOperation() => ResetQueue();
        public void ResetProgress() => ResetQueue();

        /// <summary>
        /// Limpa a exibição do rodapé sem tocar nas operações em andamento.
        ///
        /// Existe por causa de um caso real: o rodapé só agenda o auto-ocultar
        /// do status quando NÃO há operação rodando. Se sobrar qualquer tarefa
        /// viva, o texto da última etapa fica preso na tela. Aqui emitimos um
        /// status vazio, que o rodapé interpreta como "ocultar", sem cancelar
        /// o trabalho de ninguém.
        /// </summary>
        public void ClearDisplayedStatus()
        {
            try
            {
                StatusChanged?.Invoke(this, string.Empty);
            }
            catch { }
        }

        // Estes tres metodos recebem o id REAL da tarefa do token.
        // Antes usavam active.FirstOrDefault() e ignoravam o id: um
        // `using var token = BeginOperation(...)` encerrava a primeira tarefa
        // ativa da lista, nao a sua, e a sua vazava para sempre.
        internal void InternalUpdate(string taskId, int percentage, string? message)
        {
            if (!string.IsNullOrEmpty(taskId))
            {
                _manager.UpdateProgress(taskId, percentage, message);
                return;
            }
            var current = GetCurrentTask();
            if (current != null)
                _manager.UpdateProgress(current.TaskId, percentage, message);
        }

        internal void InternalComplete(string taskId, string? finalMessage)
        {
            if (!string.IsNullOrEmpty(taskId))
            {
                _manager.InternalComplete(taskId, finalMessage);
                return;
            }
            var current = GetCurrentTask();
            if (current != null)
                _manager.InternalComplete(current.TaskId, finalMessage);
        }

        internal void InternalFail(string taskId, string? errorMessage)
        {
            if (!string.IsNullOrEmpty(taskId))
            {
                _manager.InternalFail(taskId, errorMessage);
                return;
            }
            var current = GetCurrentTask();
            if (current != null)
                _manager.InternalFail(current.TaskId, errorMessage);
        }

        public void PurgeStaleOperations(TimeSpan maxAge)
        {
        }

        public bool IsOperationRunning => _manager.IsOperationRunning;

        public string CurrentOperation => _manager.CurrentOperation;

        public IReadOnlyList<ActiveOperationInfo> GetActiveOperations()
        {
            return _manager.GetActiveTasks()
                .Select(t => new ActiveOperationInfo(
                    t.Name, t.Progress, t.Message, t.StartTime, t.Priority > 0))
                .ToList();
        }

        public int GetQueueCount() => _manager.ActiveTaskCount;


        private static string LocalizeProgressMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            string localized = LocalizationService.Instance.GetString(message);
            if (localized != message) return localized;

            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lang == Language.Portuguese) return message;

            // Localizes a benchmark fragment (sub-message emitted by BenchmarkService or BenchmarkPage),
            // recursing through LocalizationService.GetString and covering dynamic score fragments.
            string LocalizeBenchmarkFragment(string frag)
            {
                string fLocal = LocalizationService.Instance.GetString(frag);
                if (fLocal != frag) return fLocal;

                if (frag.Equals("Velocidade de Leitura", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? "Read Speed" : "Velocidad de lectura";
                if (frag.Equals("Velocidade de Escrita", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? "Write Speed" : "Velocidad de escritura";
                if (frag.Equals("Tempo de Acesso", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? "Access Time" : "Tiempo de acceso";
                if (frag.StartsWith("CPU Score: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"CPU Score: {frag.Substring("CPU Score: ".Length)}" : $"Puntuación CPU: {frag.Substring("CPU Score: ".Length)}";
                if (frag.StartsWith("Memory Score: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"Memory Score: {frag.Substring("Memory Score: ".Length)}" : $"Puntuación de memoria: {frag.Substring("Memory Score: ".Length)}";
                if (frag.StartsWith("Disk Score: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"Disk Score: {frag.Substring("Disk Score: ".Length)}" : $"Puntuación de disco: {frag.Substring("Disk Score: ".Length)}";
                if (frag.StartsWith("UI Score: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"UI Score: {frag.Substring("UI Score: ".Length)}" : $"Puntuación de UI: {frag.Substring("UI Score: ".Length)}";
                if (frag.StartsWith("Scheduler Latency: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"Scheduler Latency: {frag.Substring("Scheduler Latency: ".Length)}" : $"Latencia del scheduler: {frag.Substring("Scheduler Latency: ".Length)}";
                if (frag.StartsWith("Benchmark concluído! Score Global: ", StringComparison.OrdinalIgnoreCase))
                    return lang == Language.English ? $"Benchmark complete! Global Score: {frag.Substring("Benchmark concluído! Score Global: ".Length)}" : $"¡Benchmark completado! Puntuación global: {frag.Substring("Benchmark concluído! Score Global: ".Length)}";
                if (frag.StartsWith("Estabilizando", StringComparison.OrdinalIgnoreCase))
                    return (lang == Language.English ? "Stabilizing" : "Estabilizando") + frag.Substring("Estabilizando".Length);
                return frag;
            }

            string msg = message.Trim();

            // Strip prefixes like "✅", "⚠️", "❌", "⚠", "ℹ" for clean translations, then re-apply them.
            string prefix = "";
            if (msg.StartsWith("✅")) { prefix = "✅ "; msg = msg.Substring(1).Trim(); }
            else if (msg.StartsWith("⚠️")) { prefix = "⚠️ "; msg = msg.Substring(2).Trim(); }
            else if (msg.StartsWith("❌")) { prefix = "❌ "; msg = msg.Substring(1).Trim(); }
            else if (msg.StartsWith("⚠")) { prefix = "⚠ "; msg = msg.Substring(1).Trim(); }
            else if (msg.StartsWith("ℹ")) { prefix = "ℹ "; msg = msg.Substring(1).Trim(); }

            // Try to translate again after stripping prefix
            // A antiga tabela ProgressTranslations (108 entradas) foi removida:
            // as 108 já estão no LocalizationService, que é consultado acima e
            // logo abaixo. Manter as duas era duplicar tradução em dois lugares.
            string stripLocalized = LocalizationService.Instance.GetString(msg);
            if (stripLocalized != msg) return prefix + stripLocalized;

            // Dynamic message handling
            if (msg.StartsWith("Iniciando reparo de ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Iniciando reparo de ".Length);
                return prefix + (lang == Language.English ? $"Starting repair of {rest}" : $"Iniciando reparación de {rest}");
            }
            if (msg.StartsWith("Executando TRIM em ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Executando TRIM em ".Length);
                rest = rest.Replace("[Tipo WMI: SSD]", lang == Language.English ? "[WMI Type: SSD]" : "[Tipo WMI: SSD]")
                           .Replace("[Tipo WMI: HDD]", lang == Language.English ? "[WMI Type: HDD]" : "[Tipo WMI: HDD]")
                           .Replace("[Tipo WMI: Unknown]", lang == Language.English ? "[WMI Type: Unknown]" : "[Tipo WMI: Unknown]");
                return prefix + (lang == Language.English ? $"Executing TRIM on {rest}" : $"Ejecutando TRIM en {rest}");
            }
            if (msg.StartsWith("Desfragmentando em ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Desfragmentando em ".Length);
                rest = rest.Replace("[Tipo WMI: SSD]", lang == Language.English ? "[WMI Type: SSD]" : "[Tipo WMI: SSD]")
                           .Replace("[Tipo WMI: HDD]", lang == Language.English ? "[WMI Type: HDD]" : "[Tipo WMI: HDD]")
                           .Replace("[Tipo WMI: Unknown]", lang == Language.English ? "[WMI Type: Unknown]" : "[Tipo WMI: Unknown]");
                return prefix + (lang == Language.English ? $"Defragmenting on {rest}" : $"Desfragmentando en {rest}");
            }
            if (msg.StartsWith("Analisando em ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Analisando em ".Length);
                rest = rest.Replace("[Tipo WMI: SSD]", lang == Language.English ? "[WMI Type: SSD]" : "[Tipo WMI: SSD]")
                           .Replace("[Tipo WMI: HDD]", lang == Language.English ? "[WMI Type: HDD]" : "[Tipo WMI: HDD]")
                           .Replace("[Tipo WMI: Unknown]", lang == Language.English ? "[WMI Type: Unknown]" : "[Tipo WMI: Unknown]");
                return prefix + (lang == Language.English ? $"Analyzing on {rest}" : $"Analizando en {rest}");
            }

            // StorageOptimizerService "[Defrag]"/"[Defrag ERRO]" tagged messages (App.xaml IPC defrag feeds the bar)
            bool defragErroTag = msg.StartsWith("[Defrag ERRO] ", StringComparison.OrdinalIgnoreCase);
            bool defragTag = msg.StartsWith("[Defrag] ", StringComparison.OrdinalIgnoreCase);
            if (defragErroTag || defragTag)
            {
                string tagText = defragErroTag ? "[Defrag ERRO] " : "[Defrag] ";
                string rest = msg.Substring(tagText.Length);
                string defragLocal = LocalizationService.Instance.GetString(rest);
                if (defragLocal != rest) return prefix + tagText + defragLocal;
                if (rest.StartsWith("Detectadas ", StringComparison.OrdinalIgnoreCase) && rest.EndsWith(" unidades armazenáveis.", StringComparison.OrdinalIgnoreCase))
                {
                    string count = rest.Substring("Detectadas ".Length, rest.Length - "Detectadas ".Length - " unidades armazenáveis.".Length);
                    return prefix + tagText + (lang == Language.English ? $"Detected {count} storage drives." : $"Se detectaron {count} unidades de almacenamiento.");
                }
                if (rest.StartsWith("Ignorando mídia removível ", StringComparison.OrdinalIgnoreCase))
                {
                    string letter = rest.Substring("Ignorando mídia removível ".Length);
                    return prefix + tagText + (lang == Language.English ? $"Ignoring removable drive {letter}" : $"Omitiendo unidad extraíble {letter}");
                }
                if (rest.StartsWith("TIPO DESCONHECIDO para ", StringComparison.OrdinalIgnoreCase))
                {
                    string letter = rest.Substring("TIPO DESCONHECIDO para ".Length);
                    return prefix + tagText + (lang == Language.English ? $"UNKNOWN TYPE for {letter}" : $"TIPO DESCONOCIDO para {letter}");
                }
                if (rest.StartsWith("Disparando defrag.exe ", StringComparison.OrdinalIgnoreCase))
                {
                    string detail = rest.Substring("Disparando defrag.exe ".Length);
                    return prefix + tagText + (lang == Language.English ? $"Launching defrag.exe {detail}" : $"Lanzando defrag.exe {detail}");
                }
                if (rest.StartsWith("[DEBUG] ", StringComparison.OrdinalIgnoreCase))
                {
                    string detail = rest.Substring("[DEBUG] ".Length);
                    return prefix + tagText + (lang == Language.English ? $"Executing: {detail}" : $"Ejecutando: {detail}");
                }
                return prefix + msg;
            }

            // 🔵 [n/total] drive counter emitted by StorageOptimizerService (statusMsg recurses through the existing TRIM handlers)
            if (msg.StartsWith("🔵 [", StringComparison.Ordinal) && msg.IndexOf("] ", StringComparison.Ordinal) > 0)
            {
                int closeIdx = msg.IndexOf("] ", StringComparison.Ordinal);
                string counter = msg.Substring(0, closeIdx + 1);
                string rest = msg.Substring(closeIdx + 2);
                return prefix + counter + " " + LocalizeProgressMessage(rest);
            }
            if (msg.StartsWith("Otimização em ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith(" processada.", StringComparison.OrdinalIgnoreCase))
            {
                string letter = msg.Substring("Otimização em ".Length, msg.Length - "Otimização em ".Length - " processada.".Length);
                return prefix + (lang == Language.English ? $"Optimization on {letter} processed." : $"Optimización en {letter} procesada.");
            }
            if (msg.StartsWith("Erro em ", StringComparison.OrdinalIgnoreCase))
            {
                string letter = msg.Substring("Erro em ".Length);
                return prefix + (lang == Language.English ? $"Error on {letter}" : $"Error en {letter}");
            }
            if (msg.StartsWith("Otimização física do ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith(": concluída!", StringComparison.OrdinalIgnoreCase))
            {
                string letter = msg.Substring("Otimização física do ".Length, msg.Length - "Otimização física do ".Length - ": concluída!".Length);
                return prefix + (lang == Language.English ? $"Physical optimization of {letter}: completed!" : $"Optimización física de {letter}: ¡completada!");
            }
            if (msg.StartsWith("defrag.exe falhou com código ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("defrag.exe falhou com código ".Length);
                return prefix + (lang == Language.English ? $"defrag.exe failed with code {err}" : $"defrag.exe falló con código {err}");
            }
            if (msg.StartsWith("Exceção ao executar defrag.exe: ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("Exceção ao executar defrag.exe: ".Length);
                return prefix + (lang == Language.English ? $"Exception executing defrag.exe: {err}" : $"Excepción al ejecutar defrag.exe: {err}");
            }
            if (msg.StartsWith("INSTALAÇÃO: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("INSTALAÇÃO: ".Length);
                return prefix + (lang == Language.English ? $"INSTALLATION: {rest}" : $"INSTALACIÓN: {rest}");
            }
            if (msg.StartsWith("Manual: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Manual: ".Length);
                return prefix + (lang == Language.English ? $"Manual: {rest}" : $"Manual: {rest}");
            }
            if (msg.EndsWith(" aplicado", StringComparison.OrdinalIgnoreCase) && msg.StartsWith("DNS ", StringComparison.OrdinalIgnoreCase))
            {
                string dns = msg.Substring(4, msg.Length - 4 - 9);
                return prefix + (lang == Language.English ? $"DNS {dns} applied" : $"DNS {dns} aplicado");
            }
            if (msg.StartsWith("Falha ao aplicar DNS ", StringComparison.OrdinalIgnoreCase))
            {
                string dns = msg.Substring("Falha ao aplicar DNS ".Length);
                return prefix + (lang == Language.English ? $"Failed to apply DNS {dns}" : $"Fallo al aplicar DNS {dns}");
            }
            if (msg.StartsWith("Erro ao aplicar DNS: ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("Erro ao aplicar DNS: ".Length);
                return prefix + (lang == Language.English ? $"Error applying DNS: {err}" : $"Error al aplicar DNS: {err}");
            }
            if (msg.StartsWith("DNS de ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith(" restaurado para DHCP", StringComparison.OrdinalIgnoreCase))
            {
                string nic = msg.Substring("DNS de ".Length, msg.Length - "DNS de ".Length - " restaurado para DHCP".Length);
                return prefix + (lang == Language.English ? $"DNS of {nic} restored to DHCP" : $"DNS de {nic} restaurado a DHCP");
            }
            if (msg.StartsWith("Falha ao restaurar DNS para ", StringComparison.OrdinalIgnoreCase))
            {
                string nic = msg.Substring("Falha ao restaurar DNS para ".Length);
                return prefix + (lang == Language.English ? $"Failed to restore DNS for {nic}" : $"Fallo al restaurar DNS para {nic}");
            }
            if (msg.StartsWith("Erro ao restaurar DNS: ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("Erro ao restaurar DNS: ".Length);
                return prefix + (lang == Language.English ? $"Error restoring DNS: {err}" : $"Error al restaurar DNS: {err}");
            }
            if (msg.StartsWith("Erro ao otimizar rede: ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("Erro ao otimizar rede: ".Length);
                return prefix + (lang == Language.English ? $"Error optimizing network: {err}" : $"Error al optimizar red: {err}");
            }
            if (msg.StartsWith("Erro ao otimizar pilha: ", StringComparison.OrdinalIgnoreCase))
            {
                string err = msg.Substring("Erro ao otimizar pilha: ".Length);
                return prefix + (lang == Language.English ? $"Error optimizing stack: {err}" : $"Error al optimizar pila: {err}");
            }
            if (msg.StartsWith("Fase 1/4: ", StringComparison.OrdinalIgnoreCase) ||
                msg.StartsWith("Fase 2/4: ", StringComparison.OrdinalIgnoreCase) ||
                msg.StartsWith("Fase 3/4: ", StringComparison.OrdinalIgnoreCase) ||
                msg.StartsWith("Fase 4/4: ", StringComparison.OrdinalIgnoreCase))
            {
                string fasePrefix = msg.Substring(0, "Fase 1/4: ".Length);
                string faseRest = msg.Substring("Fase 1/4: ".Length);
                string localizedFase = fasePrefix.Replace("Fase", lang == Language.English ? "Phase" : "Fase", StringComparison.OrdinalIgnoreCase);
                return prefix + localizedFase + LocalizeBenchmarkFragment(faseRest);
            }
            if (msg.StartsWith("Benchmark: ", StringComparison.OrdinalIgnoreCase))
            {
                string label = msg.Substring("Benchmark: ".Length);
                return prefix + $"Benchmark: {LocalizeBenchmarkFragment(label)}";
            }
            if (msg.StartsWith("Benchmark ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith(" concluído", StringComparison.OrdinalIgnoreCase))
            {
                string label = msg.Substring("Benchmark ".Length, msg.Length - "Benchmark ".Length - " concluído".Length);
                return prefix + (lang == Language.English ? $"Benchmark {LocalizeBenchmarkFragment(label)} completed" : $"Benchmark {LocalizeBenchmarkFragment(label)} completado");
            }
            if (msg.StartsWith("SFC: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("SFC: ".Length);
                if (rest.Equals("Iniciando...", StringComparison.OrdinalIgnoreCase))
                    return prefix + "SFC: " + (lang == Language.English ? "Starting..." : "Iniciando...");
                return prefix + "SFC: " + rest;
            }
            if (msg.StartsWith("DISM: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("DISM: ".Length);
                if (rest.Equals("Iniciando...", StringComparison.OrdinalIgnoreCase))
                    return prefix + "DISM: " + (lang == Language.English ? "Starting..." : "Iniciando...");
                return prefix + "DISM: " + rest;
            }
            if (msg.StartsWith("[DRY-RUN] ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("[DRY-RUN] ".Length);
                string restLocalized = rest switch
                {
                    _ when rest.Equals("DISM simulado", StringComparison.OrdinalIgnoreCase) => lang == Language.English ? "simulated DISM" : "DISM simulado",
                    _ when rest.Equals("SFC simulado", StringComparison.OrdinalIgnoreCase) => lang == Language.English ? "simulated SFC" : "SFC simulado",
                    _ when rest.Equals("Limpeza simulada", StringComparison.OrdinalIgnoreCase) => lang == Language.English ? "simulated cleanup" : "limpieza simulada",
                    _ => LocalizeProgressMessage(rest)
                };
                return prefix + "[DRY-RUN] " + restLocalized;
            }

            // Exact match translations
            if (msg.Equals("Verificação concluída", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("Verificação concluída!", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Scan completed" : "Verificación completada");
            }
            if (msg.Equals("Perfil Inteligente Pronto", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Intelligent Profile Ready" : "Perfil Inteligente Listo");
            }
            if (msg.Equals("Perfil pronto!", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("Perfil pronto", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Profile ready!" : "¡Perfil listo!");
            }
            if (msg.Equals("Definições atualizadas com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Definitions updated successfully!" : "¡Definiciones actualizadas con éxito!");
            }
            if (msg.Equals("Erro ao verificar atualizações", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Error checking updates" : "Error al verificar actualizaciones");
            }
            if (msg.Equals("Erro ao instalar atualizações", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Error installing updates" : "Error al instalar actualizaciones");
            }
            if (msg.Equals("Twitch conectada", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Twitch connected" : "Twitch conectada");
            }
            if (msg.Equals("Falha ao conectar Twitch", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Failed to connect Twitch" : "Fallo al conectar Twitch");
            }
            if (msg.StartsWith("Erro ao conectar Twitch:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao conectar Twitch:".Length);
                return prefix + (lang == Language.English ? $"Error connecting Twitch:{errPart}" : $"Error al conectar Twitch:{errPart}");
            }
            if (msg.Equals("YouTube conectado", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "YouTube connected" : "YouTube conectado");
            }
            if (msg.Equals("Falha ao conectar YouTube", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Failed to connect YouTube" : "Fallo al conectar YouTube");
            }
            if (msg.StartsWith("Erro ao conectar YouTube:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao conectar YouTube:".Length);
                return prefix + (lang == Language.English ? $"Error connecting YouTube:{errPart}" : $"Error al conectar YouTube:{errPart}");
            }
            if (msg.Equals("Falha ao obter ProcessArm", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Failed to get ProcessArm" : "Fallo al obtener ProcessArm");
            }
            if (msg.StartsWith("Erro na limpeza de RAM:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro na limpeza de RAM:".Length);
                return prefix + (lang == Language.English ? $"Error during RAM cleanup:{errPart}" : $"Error en la limpieza de RAM:{errPart}");
            }
            if (msg.Equals("Rede otimizada para streaming", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Network optimized for streaming" : "Red optimizada para streaming");
            }
            if (msg.Equals("Falha ao obter NetworkArm", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Failed to get NetworkArm" : "Fallo al obtener NetworkArm");
            }
            if (msg.StartsWith("Erro na otimização de rede:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro na otimização de rede:".Length);
                return prefix + (lang == Language.English ? $"Error in network optimization:{errPart}" : $"Error en la optimización de red:{errPart}");
            }
            if (msg.Equals("Stream Hub iniciado com sucesso", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Stream Hub started successfully" : "Stream Hub iniciado con éxito");
            }
            if (msg.StartsWith("Falha ao iniciar Stream Hub:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Falha ao iniciar Stream Hub:".Length);
                return prefix + (lang == Language.English ? $"Failed to start Stream Hub:{errPart}" : $"Fallo al iniciar Stream Hub:{errPart}");
            }
            if (msg.Equals("Stream Hub parado com sucesso", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Stream Hub stopped successfully" : "Stream Hub detenido con éxito");
            }
            if (msg.StartsWith("Falha ao parar Stream Hub:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Falha ao parar Stream Hub:".Length);
                return prefix + (lang == Language.English ? $"Failed to stop Stream Hub:{errPart}" : $"Fallo al detener Stream Hub:{errPart}");
            }
            if (msg.Equals("Falha ao conectar OBS", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Failed to connect OBS" : "Fallo al conectar OBS");
            }

            // RAM Cleanup formatting: "Limpeza de RAM: 512 MB liberados"
            if (msg.StartsWith("Limpeza de RAM:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Limpeza de RAM:".Length).Trim();
                rest = rest.Replace("liberados", lang == Language.English ? "released" : "liberados");
                return prefix + (lang == Language.English ? $"RAM Cleanup: {rest}" : $"Limpieza de RAM: {rest}");
            }

            // Game detection
            if (msg.Equals("Iniciando detecção...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Starting detection..." : "Iniciando detección...");
            }
            if (msg.StartsWith("Verificando ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith("..."))
            {
                string pathPart = msg.Substring("Verificando ".Length);
                return prefix + (lang == Language.English ? $"Checking {pathPart}" : $"Verificando {pathPart}");
            }
            if (msg.Equals("Finalizando detecção...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Finalizing detection..." : "Finalizando detección...");
            }
            if (msg.Equals("Concluído", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Completed" : "Completado");
            }

            // Pre-checks
            if (msg.Equals("Verificando privilégios...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Checking privileges..." : "Verificando privilegios...");
            }
            if (msg.Equals("Verificando sistema operacional...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Checking operating system..." : "Verificando sistema operativo...");
            }
            if (msg.Equals("Verificando espaço em disco...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Checking disk space..." : "Verificando espacio en disco...");
            }
            if (msg.Equals("Verificando processos críticos...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Checking critical processes..." : "Verificando procesos críticos...");
            }
            if (msg.Equals("Pré-verificações concluídas", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Pre-checks completed" : "Preverificaciones completadas");
            }

            // Backup
            if (msg.Equals("Exportando chaves de registro...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Exporting registry keys..." : "Exportando claves de registro...");
            }
            if (msg.Equals("Exportando plano de energia...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Exporting power plan..." : "Exportando plan de energía...");
            }
            if (msg.Equals("Salvando entradas de inicialização...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Saving startup entries..." : "Guardando entradas de inicio...");
            }
            if (msg.Equals("Comprimindo backups...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Compressing backups..." : "Comprimiendo copias de seguridad...");
            }
            if (msg.Equals("Backup concluído", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Backup completed" : "Copia de seguridad completada");
            }

            // SFC / DISM
            if (msg.Equals("Iniciando verificação de arquivos do sistema...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Starting system file verification..." : "Iniciando verificación de archivos del sistema...");
            }
            if (msg.Equals("Verificando arquivos do sistema...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Checking system files..." : "Verificando archivos del sistema...");
            }
            if (msg.Equals("Iniciando reparo da imagem do Windows...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Starting Windows image repair..." : "Iniciando reparación de la imagen de Windows...");
            }
            if (msg.Equals("Reparando imagem do Windows...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Repairing Windows image..." : "Reparando la imagen de Windows...");
            }
            if (msg.Equals("Reparo concluído", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Repair completed" : "Reparación completada");
            }

            // Cleanup
            if (msg.Equals("Limpando arquivos temporários do usuário...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning user temporary files..." : "Limpiando archivos temporales del usuario...");
            }
            if (msg.Equals("Limpando arquivos temporários do Windows...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning Windows temporary files..." : "Limpiando archivos temporales de Windows...");
            }
            if (msg.Equals("Limpando cache de miniaturas...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning thumbnail cache..." : "Limpiando caché de miniaturas...");
            }
            if (msg.Equals("Limpando cache HTTP dos navegadores...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning browser HTTP cache..." : "Limpiando caché HTTP de navegadores...");
            }
            if (msg.Equals("Limpando cache do Windows Update...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning Windows Update cache..." : "Limpiando caché de Windows Update...");
            }
            if (msg.Equals("Limpando logs antigos...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning old logs..." : "Limpiando registros antiguos...");
            }
            if (msg.Equals("Limpeza concluída", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleanup completed" : "Limpieza completada");
            }

            // Network reset
            if (msg.Equals("Limpando cache DNS...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Flushing DNS cache..." : "Limpiando caché DNS...");
            }
            if (msg.Equals("Resetando Winsock...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Resetting Winsock..." : "Restableciendo Winsock...");
            }
            if (msg.Equals("Resetando configurações IP...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Resetting IP settings..." : "Restableciendo configuración de IP...");
            }
            if (msg.Equals("Renovando DHCP...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Renewing DHCP..." : "Renovando DHCP...");
            }
            if (msg.Equals("Reset de rede concluído", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Network reset completed" : "Restablecimiento de red completado");
            }

            // Shaders cleanup
            if (msg.Equals("Limpando NVIDIA shader cache...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning NVIDIA shader cache..." : "Limpiando caché de sombreadores de NVIDIA...");
            }
            if (msg.Equals("Limpando AMD shader cache...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning AMD shader cache..." : "Limpiando caché de sombreadores de AMD...");
            }
            if (msg.Equals("Limpando DirectX shader cache...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Cleaning DirectX shader cache..." : "Limpiando caché de sombreadores de DirectX...");
            }
            if (msg.Equals("Limpeza de shaders concluída", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Shader cleanup completed" : "Limpieza de sombreadores completada");
            }

            // Power
            if (msg.Equals("Salvando plano de energia atual...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Saving current power plan..." : "Guardando plan de energía actual...");
            }
            if (msg.Equals("Ativando plano de alta performance...", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Activating high performance plan..." : "Activando plan de alto rendimiento...");
            }
            if (msg.Equals("Configuração de energia concluída", StringComparison.OrdinalIgnoreCase))
            {
                return prefix + (lang == Language.English ? "Power configuration completed" : "Configuración de energía completada");
            }

            // Optimization prefix
            if (msg.StartsWith("Otimizando ", StringComparison.OrdinalIgnoreCase) && msg.EndsWith("..."))
            {
                string optPart = msg.Substring("Otimizando ".Length);
                return prefix + (lang == Language.English ? $"Optimizing {optPart}" : $"Optimizando {optPart}");
            }

            // Update download "Baixando... X / Y"
            if (msg.StartsWith("Baixando... ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Baixando... ".Length);
                return prefix + (lang == Language.English ? $"Downloading... {rest}" : $"Descargando... {rest}");
            }

            // ProgressBridge "Iniciando: {operationName}"
            if (msg.StartsWith("Iniciando: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Iniciando: ".Length).Trim();
                string restLocalized = LocalizationService.Instance.GetString(rest);
                if (restLocalized == rest) restLocalized = LocalizeBenchmarkFragment(rest);
                return prefix + (lang == Language.English ? $"Starting: {restLocalized}" : $"Iniciando: {restLocalized}");
            }

            // Recovery "Restaurando {filename}" (BeginOperation name)
            if (msg.StartsWith("Restaurando ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Restaurando ".Length);
                return prefix + (lang == Language.English ? $"Restoring {rest}" : $"Restaurando {rest}");
            }

            // ProgressBridge "✅ {operationName} concluído" (prefix already stripped)
            if (msg.EndsWith(" concluído", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring(0, msg.Length - " concluído".Length).Trim();
                string restLocalized = LocalizationService.Instance.GetString(rest);
                if (restLocalized == rest) restLocalized = LocalizeBenchmarkFragment(rest);
                return prefix + (lang == Language.English ? $"{restLocalized} completed" : $"{restLocalized} completado");
            }
            if (msg.EndsWith(" concluida", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring(0, msg.Length - " concluida".Length).Trim();
                string restLocalized = LocalizationService.Instance.GetString(rest);
                if (restLocalized == rest) restLocalized = LocalizeBenchmarkFragment(rest);
                return prefix + (lang == Language.English ? $"{restLocalized} completed" : $"{restLocalized} completada");
            }

            // ProgressBridge "❌ {operationName} falhou: {error}" (prefix already stripped)
            int falhouIdx = msg.IndexOf(" falhou: ", StringComparison.OrdinalIgnoreCase);
            if (falhouIdx > 0)
            {
                string opName = msg.Substring(0, falhouIdx).Trim();
                string opLocalized = LocalizationService.Instance.GetString(opName);
                if (opLocalized == opName) opLocalized = LocalizeBenchmarkFragment(opName);
                string err = msg.Substring(falhouIdx + " falhou: ".Length).Trim();
                return prefix + (lang == Language.English ? $"{opLocalized} failed: {err}" : $"{opLocalized} falló: {err}");
            }

            // " (Timeout)" suffix appended by callers
            if (msg.EndsWith(" (Timeout)", StringComparison.OrdinalIgnoreCase))
            {
                string baseText = msg.Substring(0, msg.Length - " (Timeout)".Length).Trim();
                string baseLocalized = LocalizationService.Instance.GetString(baseText);
                if (baseLocalized == baseText) baseLocalized = baseText;
                return prefix + (lang == Language.English ? $"{baseLocalized} (Timeout)" : $"{baseLocalized} (Tiempo de espera)");
            }

            // PreparePcManager: "Preparando PC: {stepName} ({i}/{total})..."
            if (msg.StartsWith("Preparando PC: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Preparando PC: ".Length);
                string counter = "";
                int openParen = rest.IndexOf(" (", StringComparison.Ordinal);
                if (openParen > 0)
                {
                    counter = rest.Substring(openParen);
                    rest = rest.Substring(0, openParen);
                }
                string stepLocalized = LocalizationService.Instance.GetString(rest);
                return prefix + (lang == Language.English ? $"Preparing PC: {stepLocalized}{counter}" : $"Preparando PC: {stepLocalized}{counter}");
            }

            // PreparePcManager: "{stepName}: {currentAction}" (currentAction already localized above)
            int stepColonIdx = msg.IndexOf(": ", StringComparison.Ordinal);
            if (stepColonIdx > 0)
            {
                string stepNamePart = msg.Substring(0, stepColonIdx);
                string stepTrans = LocalizationService.Instance.GetString(stepNamePart);
                if (stepTrans != stepNamePart)
                {
                    string actionPart = LocalizeProgressMessage(msg.Substring(stepColonIdx + 2));
                    return prefix + stepTrans + ": " + actionPart;
                }
            }

            // PreparePc RollbackManager: "Revertendo: {stepName}"
            if (msg.StartsWith("Revertendo: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Revertendo: ".Length);
                string stepLocalized = LocalizationService.Instance.GetString(rest);
                return prefix + (lang == Language.English ? $"Reverting: {stepLocalized}" : $"Revirtiendo: {stepLocalized}");
            }

            return prefix + msg;
        }
    }

    public sealed class ActiveOperationInfo
    {
        public string Name { get; }
        public int Progress { get; }
        public string Message { get; }
        public DateTime StartTime { get; }
        public bool IsPriority { get; }
        public TimeSpan Elapsed => DateTime.UtcNow - StartTime;

        public ActiveOperationInfo(string name, int progress, string message, DateTime startTime, bool isPriority)
        {
            Name = name;
            Progress = progress;
            Message = message;
            StartTime = startTime;
            IsPriority = isPriority;
        }

        public override string ToString() => $"[{Name}] {Progress}% | {Message} | {Elapsed.TotalSeconds:F1}s";
    }
}
