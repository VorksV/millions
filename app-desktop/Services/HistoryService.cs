using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Origem da execução de uma operação registrada no histórico.
    /// </summary>
    public enum HistoryOrigin
    {
        /// <summary>Disparada interativamente pelo usuário (botão, toggle, atalho).</summary>
        Manual = 0,
        /// <summary>Disparada pelo Agendador (Task Scheduler do Windows ou timer interno).</summary>
        Scheduled = 1,
        /// <summary>Disparada internamente por outro serviço do VOLTRIS.</summary>
        Automatic = 2
    }

    public class HistoryService
    {
        private const int MaxEntries = 1000;

        private readonly ILoggingService _logger;
        private readonly string _historyPath;
        private readonly string _historyBackupPath;
        private readonly object _fileLock = new object();
        private List<OptimizationHistory> _history = new List<OptimizationHistory>();

        /// <summary>
        /// Caminho do primário quando ele foi encontrado CORROMPIDO no carregamento.
        ///
        /// PERIGO QUE ESTE CAMPO EVITA:
        /// <see cref="TryBackupCurrentFile"/> copia o primário atual por cima do .bak
        /// antes de cada gravação. Se o primário estiver corrompido e a aplicação tiver
        /// carregado os dados a partir do backup, a primeira gravação copiaria o JSON
        /// ILEGÍVEL por cima do ÚNICO arquivo bom — trocando um dado recuperável por um
        /// irrecuperável, exatamente na operação que deveria proteger os dados.
        ///
        /// Com este marcador, o primário corrompido é PRESERVADO para perícia e o
        /// backup válido nunca é sobrescrito por conteúdo inválido.
        /// </summary>
        private string? _corruptPrimaryPath;

        private static HistoryService? _instance;
        private static readonly object _instanceLock = new object();

        public static HistoryService Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_instanceLock)
                {
                    // App.LoggingService pode ser nulo durante o bootstrap muito inicial.
                    // Nunca lançar aqui: o construtor de HistoryService é usado em handlers
                    // de services que já podem estar executando.
                    return _instance ??= new HistoryService(App.LoggingService!);
                }
            }
        }

        public HistoryService(ILoggingService logger)
        {
            _logger = logger;
            _historyPath = AppDataPaths.GetPath("History/history.json");
            _historyBackupPath = _historyPath + ".bak";
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_historyPath));
            LoadHistory();
        }

        public event EventHandler<OptimizationHistory>? EntryAdded;

        public void AddHistoryEntry(OptimizationHistory entry)
        {
            if (entry == null) return;

            if (string.IsNullOrEmpty(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            if (entry.Timestamp == default)
                entry.Timestamp = DateTime.Now;

            lock (_fileLock)
            {
                _history.Insert(0, entry);

                if (_history.Count > MaxEntries)
                    _history = _history.Take(MaxEntries).ToList();

                SaveHistoryLocked();
            }

            // Notificação fora do lock: o consumidor (UI) pode reentrar no serviço.
            try { EntryAdded?.Invoke(this, entry); }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Warning, LogCategory.General,
                    $"[HISTORY] Handler de EntryAdded launchou exceção para '{entry.ActionType}': {ex.Message}");
            }
        }

        public List<OptimizationHistory> GetHistory(int? limit = null)
        {
            lock (_fileLock)
            {
                return limit.HasValue ? _history.Take(limit.Value).ToList() : _history.ToList();
            }
        }

        public OptimizationStats GetStats()
        {
            List<OptimizationHistory> snapshot;
            lock (_fileLock) { snapshot = _history.ToList(); }

            var last30Days = snapshot.Where(h => h.Timestamp >= DateTime.Now.AddDays(-30)).ToList();

            return new OptimizationStats
            {
                TotalOptimizations = snapshot.Count,
                Last30DaysCount = last30Days.Count,
                TotalSpaceFreed = snapshot.Sum(h => h.SpaceFreed),
                Last30DaysSpaceFreed = last30Days.Sum(h => h.SpaceFreed),
                AverageTime = snapshot.Any() ? snapshot.Average(h => h.Duration.TotalSeconds) : 0,
                MostUsedAction = snapshot
                    .GroupBy(h => h.ActionType)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "Nenhuma",
                FailedCount = snapshot.Count(h => !h.Success),
                PartialFailureCount = snapshot.Count(h => h.PartialFailure)
            };
        }

        public void ClearHistory()
        {
            lock (_fileLock)
            {
                _history.Clear();
                SaveHistoryLocked();
            }
            _logger?.LogInfo("Histórico de otimizações limpo");
        }

        /// <summary>
        /// Registra uma atividade profissional no histórico (entrada estática de conveniência).
        /// </summary>
        public static void RecordActivity(string actionType, string description, bool success = true, long spaceFreed = 0)
        {
            Instance.RecordActivityInstance(actionType, description, success, spaceFreed);
        }

        /// <summary>
        /// Registra uma atividade profissional no histórico com metadados completos:
        /// origem da execução, duração, quantidade de itens e mensagem de erro.
        /// </summary>
        public static void RecordActivity(
            string actionType,
            string description,
            bool success,
            long spaceFreed,
            HistoryOrigin origin,
            TimeSpan duration,
            int itemCount,
            string? errorMessage = null)
        {
            Instance.RecordActivityInstance(actionType, description, success, spaceFreed, origin, duration, itemCount, errorMessage);
        }

        /// <summary>
        /// Registra uma atividade profissional no histórico para o Dashboard (Instância).
        /// </summary>
        public void RecordActivityInstance(string actionType, string description, bool success = true, long spaceFreed = 0)
        {
            RecordActivityInstance(actionType, description, success, spaceFreed, HistoryOrigin.Manual, TimeSpan.Zero, 0, null);
        }

        /// <summary>
        /// Registra uma atividade com todos os campos exigidos pela auditoria:
        /// data/hora, tipo, resultado, sucesso/falha, quantidade, espaço liberado,
        /// duração, erro e origem da execução.
        /// </summary>
        public void RecordActivityInstance(
            string actionType,
            string description,
            bool success,
            long spaceFreed,
            HistoryOrigin origin,
            TimeSpan duration,
            int itemCount,
            string? errorMessage = null,
            Dictionary<string, object>? extraDetails = null)
        {
            var details = new Dictionary<string, object>();
            if (extraDetails != null)
            {
                foreach (var kv in extraDetails) details[kv.Key] = kv.Value;
            }
            if (itemCount > 0) details["ItemCount"] = itemCount;
            if (origin == HistoryOrigin.Scheduled) details["Trigger"] = "Scheduled";
            if (!string.IsNullOrEmpty(errorMessage)) details["Error"] = errorMessage;

            var entry = new OptimizationHistory
            {
                ActionType = actionType,
                Description = description,
                Timestamp = DateTime.Now,
                Success = success,
                SpaceFreed = spaceFreed,
                Duration = duration,
                ItemCount = itemCount,
                Origin = origin,
                ErrorMessage = errorMessage,
                Details = details
            };

            _logger?.Log(LogLevel.Info, LogCategory.General,
                $"[HISTORY] Gravando: tipo='{actionType}' sucesso={success} origem={origin} duracao={duration.TotalSeconds:F1}s itens={itemCount} bytes={spaceFreed}{(string.IsNullOrEmpty(errorMessage) ? "" : $" erro='{errorMessage}'")}");

            AddHistoryEntry(entry);
        }

        public void RecordOptimization(DateTime timestamp)
        {
            var entry = new OptimizationHistory
            {
                ActionType = HistoryActionTypes.SystemOptimization,
                Description = "Otimização geral do sistema",
                Timestamp = timestamp,
                Success = true,
                Origin = HistoryOrigin.Manual
            };
            AddHistoryEntry(entry);
        }

        /// <summary>
        /// Obtém o timestamp da última otimização realizada.
        /// </summary>
        public DateTime? GetLastOptimizationTimestamp()
        {
            List<OptimizationHistory> snapshot;
            lock (_fileLock) { snapshot = _history.ToList(); }

            var lastOptimization = snapshot
                .Where(h => HistoryActionTypes.IsOptimizationLike(h.ActionType))
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            return lastOptimization?.Timestamp;
        }

        private void LoadHistory()
        {
            lock (_fileLock)
            {
                var loaded = TryLoadFrom(_historyPath);
                if (loaded == null)
                {
                    // Arquivo principal corrompido/ilegível: tenta o backup antes de
                    // desistir. Sem isso, o próximo Save sobrescreveria o histórico real.
                    loaded = TryLoadFrom(_historyBackupPath);
                    if (loaded != null)
                    {
                        // O primário está ruim e o .bak é o ÚNICO bom. Marca o primário
                        // para que TryBackupCurrentFile NÃO o copie por cima do .bak.
                        _corruptPrimaryPath = _historyPath;
                        _logger?.Log(LogLevel.Warning, LogCategory.General,
                            $"[HISTORY] history.json ilegível. Restaurado do backup '{_historyBackupPath}' ({loaded.Count} registros). " +
                            $"O arquivo corrompido será preservado e NÃO será usado como backup.");
                    }
                }

                if (loaded != null)
                {
                    _history = loaded;
                    return;
                }

                if (File.Exists(_historyPath))
                {
                    _logger?.Log(LogLevel.Error, LogCategory.General,
                        $"[HISTORY] history.json existe mas não pôde ser lido NEM recuperado do backup. " +
                        $"Os dados em disco NÃO serão apagados nesta sessão. Arquivo preservado em: {_historyPath}");
                }

                _history = new List<OptimizationHistory>();
            }
        }

        /// <summary>
        /// Tenta desserializar o histórico. Retorna null (sem tocar em disco) quando o
        /// arquivo não existe ou está corrompido, para que o chamador possa tentar o backup.
        /// </summary>
        private List<OptimizationHistory>? TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger?.Log(LogLevel.Warning, LogCategory.General, $"[HISTORY] Arquivo '{path}' está vazio.");
                    return null;
                }

                return JsonSerializer.Deserialize<List<OptimizationHistory>>(json) ?? new List<OptimizationHistory>();
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.General, $"[HISTORY] Falha ao ler '{path}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private void SaveHistoryLocked()
        {
            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                string? tempPath = null;
                try
                {
                    var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                    });

                    // Escrita atômica: grava em temporário e substitui. Uma falha/crash
                    // no meio da escrita deixa o history.json original intacto em vez de
                    // truncá-lo (o que antes causava perda total do histórico).
                    tempPath = _historyPath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    // Preserva o último bom estado como backup antes de substituir.
                    // ATENÇÃO: quando o primário está corrompido (recuperamos do
                    // backup), NÃO pode ser copiado para o .bak — isso trocaria
                    // um backup íntegro por lixo. Ver TryBackupCurrentFile.
                    TryBackupCurrentFile();

                    // Substituição ATÔMICA quando o destino já existe: o Windows
                    // troca o arquivo numa única operação de renome, portanto um
                    // crash no meio não deixa history.json truncado. File.Copy
                    // NÃO é atômica — abre o destino para escrita e pode deixá-lo
                    // pela metade, que era a causa original de perda total.
                    if (File.Exists(_historyPath))
                    {
                        try
                        {
                            File.Replace(tempPath, _historyPath, null, ignoreMetadataErrors: true);
                            tempPath = null; // Replace consumiu o temporário
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // Sistema de arquivos sem suporte a Replace (ex.: alguns
                            // volumes de rede): cai para cópia, ainda com o .bak intacto.
                            File.Copy(tempPath, _historyPath, overwrite: true);
                        }
                        catch (IOException)
                        {
                            File.Copy(tempPath, _historyPath, overwrite: true);
                        }
                    }
                    else
                    {
                        File.Move(tempPath, _historyPath);
                        tempPath = null;
                    }

                    // A gravação bem-sucedida tornou o primário íntegro: o marcador
                    // pode ser limpo, e as próximas gravações voltam a manter o .bak
                    // a partir do arquivo bom.
                    _corruptPrimaryPath = null;
                    return; // Sucesso
                }
                catch (IOException ex)
                {
                    _logger?.Log(LogLevel.Warning, LogCategory.General,
                        $"[HISTORY] Falha de IO ao salvar histórico (tentativa {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt < maxRetries) Thread.Sleep(120 * attempt);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.Log(LogLevel.Error, LogCategory.General,
                        $"[HISTORY] Sem permissão para gravar '{_historyPath}': {ex.Message}. " +
                        "Verifique as permissões da pasta de AppData.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.Log(LogLevel.Error, LogCategory.General, $"[HISTORY] Erro ao salvar histórico: {ex.GetType().Name}: {ex.Message}");
                    break;
                }
                finally
                {
                    if (tempPath != null)
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* limpeza best-effort */ }
                    }
                }
            }

            _logger?.Log(LogLevel.Error, LogCategory.General,
                $"[HISTORY] Não foi possível persistir o histórico após {maxRetries} tentativas. " +
                "O registro permanece em memória e será perdido ao fechar o aplicativo.");
        }

        private void TryBackupCurrentFile()
        {
            // Se o primário foi detectado como CORROMPIDO no carregamento, copiá-lo
            // para o .bak destruiria a única cópia íntegra do histórico. Nesse caso o
            // arquivo ruim é apenas PRESERVADO (para perícia/recuperação manual) e o
            // backup válido permanece intacto.
            if (_corruptPrimaryPath != null)
            {
                QuarantineCorruptPrimary();
                return;
            }

            try
            {
                if (File.Exists(_historyPath))
                    File.Copy(_historyPath, _historyBackupPath, overwrite: true);
            }
            catch (Exception ex)
            {
                // Backup é mecanismo de segurança: falhar aqui não deve abortar a gravação.
                _logger?.Log(LogLevel.Debug, LogCategory.General,
                    $"[HISTORY] Não foi possível atualizar o backup: {ex.Message}");
            }
        }

        /// <summary>
        /// Move o primário corrompido para um arquivo datado e limpa o marcador, para
        /// que as próximas gravações voltem a manter backup normal.
        ///
        /// Por que MOVER e não apagar: o usuário pode ter motivo para inspecionar o
        /// arquivo quebrado, e apagar dado corrompido é justamente o que o VOLTRIS
        /// evita fazer. O arquivo fica intacto, apenas fora do caminho de uso.
        /// </summary>
        private void QuarantineCorruptPrimary()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    var quarantine = $"{_historyPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                    File.Move(_historyPath, quarantine, overwrite: true);
                    _logger?.Log(LogLevel.Warning, LogCategory.General,
                        $"[HISTORY] history.json corrompido preservado em '{quarantine}'. " +
                        "O backup válido foi mantido como .bak e o histórico segue a partir dele.");
                }
            }
            catch (Exception ex)
            {
                // Não conseguir mover NÃO pode reabilitar o backup a partir do primário
                // corrompido. O marcador é preservado de propósito: enquanto o arquivo
                // ruim estiver no lugar, ele jamais copiado para o .bak. A gravação
                // seguinte usa File.Replace/Move, que substitui o primário por um bom
                // e então o marcador pode ser limpo com segurança.
                _logger?.Log(LogLevel.Warning, LogCategory.General,
                    $"[HISTORY] Não foi possível isolar o history.json corrompido: {ex.Message}. " +
                    "O backup válido será preservado e o arquivo ruim não será usado como backup.");
            }
        }
    }

    /// <summary>
    /// Tipos de ação canônicos registrados no histórico.
    /// Usar constantes evita divergência entre ViewModels e a consulta
    /// "última otimização" (GetLastOptimizationTimestamp).
    /// </summary>
    public static class HistoryActionTypes
    {
        public const string SystemOptimization = "System Optimization";
        public const string QuickOptimization = "Quick Optimization";
        public const string MasterOptimization = "Master Optimization";
        public const string QuickCleanup = "Quick Cleanup";
        public const string UltraClean = "Ultra Clean";
        public const string DeepCleanup = "Limpeza Profunda";
        public const string GamerMode = "Gamer Mode";
        public const string StreamMode = "Stream Mode";
        public const string SystemRepair = "System Repair";
        public const string GameRepair = "Game Repair";
        public const string SmartRepair = "Smart Repair";
        public const string Diagnostics = "Diagnostics";
        public const string Energy = "Energy";
        public const string Personalization = "Personalization";
        public const string ScheduledAutomation = "Scheduled Automation";
        public const string NetworkOptimization = "Network Optimization";
        public const string SecurityScan = "Security Scan";
        public const string Debloat = "Debloat";
        public const string DriverUpdate = "Driver Update";

        /// <summary>
        /// Tipos que contam como "otimização" para efeitos de "última otimização".
        /// </summary>
        public static bool IsOptimizationLike(string? actionType) =>
            actionType == SystemOptimization ||
            actionType == QuickOptimization ||
            actionType == MasterOptimization ||
            actionType == UltraClean ||
            actionType == DeepCleanup ||
            actionType == QuickCleanup ||
            actionType == GamerMode;
    }

    public class OptimizationHistory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ActionType { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public TimeSpan Duration { get; set; }
        public long SpaceFreed { get; set; }

        /// <summary>
        /// Resultado real da operação. Nunca pode ser assumido como verdadeiro pelo
        /// chamador: deve refletir o desfecho efetivamente verificado da execução.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>Quantidade de itens processados (arquivos, chaves, processos...). 0 quando não aplicável.</summary>
        public int ItemCount { get; set; }

        /// <summary>Origem da execução: manual, agendada ou automática.</summary>
        public HistoryOrigin Origin { get; set; } = HistoryOrigin.Manual;

        /// <summary>Mensagem de erro real quando <see cref="Success"/> é falso.</summary>
        public string? ErrorMessage { get; set; }

        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();

        /// <summary>Indica se houve alguma falha parcial (parte das ações falhou).</summary>
        public bool PartialFailure { get; set; }
    }

    public class OptimizationStats
    {
        public int TotalOptimizations { get; set; }
        public int Last30DaysCount { get; set; }
        public long TotalSpaceFreed { get; set; }
        public long Last30DaysSpaceFreed { get; set; }
        public double AverageTime { get; set; }
        public string MostUsedAction { get; set; } = "";
        public int FailedCount { get; set; }
        public int PartialFailureCount { get; set; }
    }
} 

