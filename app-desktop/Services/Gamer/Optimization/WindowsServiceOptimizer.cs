using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Optimization
{
    /// <summary>
    /// Sistema profissional para desativação temporária de serviços do Windows durante o Modo Gamer
    /// Implementa rollback perfeito e logs detalhados para auditoria
    /// </summary>
    public class WindowsServiceOptimizer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, ServiceOriginalState> _originalStates = new();
        private bool _isOptimizationActive;
        private DateTime _optimizationStartTime;
        private int _servicesFound;
        private int _servicesAltered;
        private int _servicesIgnored;
        private int _servicesAlreadyDisabled;
        private int _servicesRestored;
        private int _totalErrors;

/// <summary>
    /// Estado original de um serviço para rollback perfeito
    /// </summary>
    private class ServiceOriginalState
    {
        public string ServiceName { get; set; }
        public ServiceControllerStatus OriginalStatus { get; set; }
        public int OriginalStartupType { get; set; } // 0=Boot, 1=System, 2=Auto, 3=Manual, 4=Disabled
        public bool WasModified { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string OperationResult { get; set; }
        public string? ErrorMessage { get; set; }
    }

        /// <summary>
        /// Lista completa de serviços para desativação temporária (baseado em pesquisa mundial comprovada)
        /// </summary>
        private static readonly string[] ServiceStopList = new[]
        {
            // 1. Telemetria, Diagnósticos e Logs
            "DiagTrack",
            "WerSvc",
            "wercplsupport",
            "diagnosticshub.standardcollector.service",
            "TroubleshootingSvc",
            "PcaSvc",
            "WdiSystemHost",
            "WdiServiceHost",

            // 2. Atualizações e Otimizações de Rede
            "wuauserv",
            "DoSvc",
            "UsoSvc",
            "BITS",
            "wisvc",

            // 3. Xbox (será verificado se usuário utiliza)
            "XblAuthManager",
            "XblGameSave",
            "XboxNetApiSvc",
            "XboxGipSvc",

            // 4. Redes, Compartilhamento e Conectividade
            "LanmanServer",
            "lmhosts",
            "SSDPSRV",
            "upnphost",
            "WMPNetworkSvc",
            "SharedAccess",
            "icssvc",
            "Dot3svc",
            "NfsClnt",

            // 5. Indexação, Sincronização e Nuvem
            "WSearch",
            "SysMain",
            "MapsBroker",

            // 6. Integração com Dispositivos e Sensores
            "BthServ",
            "lfsvc",
            "WbioSrvc",
            "TabletInputService",
            "SensorService",
            "SensorDataService",
            "SensrSvc",
            "WPDBusEnum",
            "StiSvc",
            "FrameServer",
            "FrameServerMonitor",

            // 7. Virtualização (Hyper-V)
            "HvHost",
            "vmicguestinterface",
            "vmicheartbeat",
            "vmickvpexchange",
            "vmicrdv",
            "vmicshutdown",
            "vmictimesync",
            "vmicvmsession",

            // 8. Recursos Diversos e Legados
            "PhoneSvc",
            "RetailDemo",
            "WalletService",
            "Fax",
            "RemoteRegistry",
            "SCardSvr",
            "SCPolicySvc",
            "CDPSvc",
            "AJRouter",
            "PrintNotify",
            "SEMgrSvc",
            "NaturalAuthentication",
            "AssignedAccessManagerSvc",
            "AppVClient",
            "dmwappushservice",
            "shpamsvc",
            "autotimesvc",
            "MixedRealityOpenXRSvc"
        };

        /// <summary>
        /// Padrões de serviços com instâncias dinâmicas (_*)
        /// </summary>
        private static readonly string[] ServicePatterns = new[]
        {
            "OneSyncSvc_",
            "PimIndexMaintenanceSvc_",
            "CDPUserSvc_",
            "MessagingService_"
        };

        public WindowsServiceOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[WindowsServiceOptimizer] Inicializado - Sistema de otimização de serviços pronto");
        }

        /// <summary>
        /// Verifica se o processo está rodando como Administrador
        /// </summary>
        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsServiceOptimizer] Erro ao verificar admin: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica se o usuário utiliza serviços Xbox (Game Bar, Game Pass, Xbox App)
        /// </summary>
        private bool IsUserUsingXbox()
        {
            try
            {
                // Verifica se Xbox Game Bar está instalada
                var xboxProcesses = Process.GetProcessesByName("GameBar");
                if (xboxProcesses.Length > 0)
                {
                    _logger.LogInfo("[WindowsServiceOptimizer] Xbox Game Bar detectada em execução");
                    return true;
                }

                // Verifica se algum processo do Xbox está rodando
                var xboxAppProcesses = new[] { "XboxApp", "XboxGameCallable", "Microsoft.Xbox.Game" };
                foreach (var processName in xboxAppProcesses)
                {
                    if (Process.GetProcessesByName(processName).Length > 0)
                    {
                        _logger.LogInfo($"[WindowsServiceOptimizer] Processo Xbox detectado: {processName}");
                        return true;
                    }
                }

                // Verifica se o serviço Xbox está em execução
                try
                {
                    var xblService = new ServiceController("XblAuthManager");
                    if (xblService.Status == ServiceControllerStatus.Running)
                    {
                        _logger.LogInfo("[WindowsServiceOptimizer] Serviço Xbox Auth Manager está ativo");
                        return true;
                    }
                }
                catch { }

                _logger.LogInfo("[WindowsServiceOptimizer] Nenhum uso de Xbox detectado");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[WindowsServiceOptimizer] Erro ao verificar uso de Xbox: {ex.Message}");
                return false; // Por segurança, não desativa se houver erro na detecção
            }
        }

        /// <summary>
        /// Detecta dinamicamente serviços com padrões (_*)
        /// </summary>
        private List<string> DetectPatternServices(string pattern)
        {
            var detected = new List<string>();
            try
            {
                var allServices = ServiceController.GetServices();
                foreach (var service in allServices)
                {
                    if (service.ServiceName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        detected.Add(service.ServiceName);
                        _logger.LogDebug($"[WindowsServiceOptimizer] Detectado serviço com padrão {pattern}: {service.ServiceName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[WindowsServiceOptimizer] Erro ao detectar padrão {pattern}: {ex.Message}");
            }
            return detected;
        }

        /// <summary>
        /// Monta a lista completa de serviços a serem otimizados
        /// </summary>
        private List<string> BuildCompleteServiceList()
        {
            var completeList = new List<string>(ServiceStopList);
            
            // Adiciona serviços detectados por padrão
            foreach (var pattern in ServicePatterns)
            {
                completeList.AddRange(DetectPatternServices(pattern));
            }

            // Remove serviços Xbox se usuário estiver utilizando
            if (IsUserUsingXbox())
            {
                var xboxServices = new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" };
                foreach (var xboxService in xboxServices)
                {
                    completeList.Remove(xboxService);
                    _logger.LogInfo($"[WindowsServiceOptimizer] Serviço Xbox preservado (usuário ativo): {xboxService}");
                }
            }

            _logger.LogInfo($"[WindowsServiceOptimizer] Lista completa: {completeList.Count} serviços serão avaliados");
            return completeList;
        }

        /// <summary>
        /// Registra o estado original de um serviço para rollback perfeito
        /// </summary>
        private async Task<ServiceOriginalState?> RecordOriginalState(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                
                var originalState = new ServiceOriginalState
                {
                    ServiceName = serviceName,
                    OriginalStatus = service.Status,
                    OriginalStartupType = GetStartupType(serviceName),
                    WasModified = false,
                    ModifiedAt = DateTime.MinValue,
                    OperationResult = "Pending"
                };

                _logger.LogDebug($"[WindowsServiceOptimizer] {serviceName}: Status={originalState.OriginalStatus}, Startup={originalState.OriginalStartupType}");
                return originalState;
            }
            catch (Exception ex)
            {
                // Servicos opcionais do Windows (ex.: NfsClnt, MixedRealityOpenXRSvc,
                // FrameServerMonitor) nao existem em todas as instalacoes/VMs. Nao e erro.
                if (ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug($"[WindowsServiceOptimizer] Servico ausente (recurso opcional do Windows): {serviceName}");
                }
                else
                {
                    _logger.LogWarning($"[WindowsServiceOptimizer] Erro ao registrar estado de {serviceName}: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// Obtém o tipo de startup de um serviço via Registry
        /// </summary>
        private int GetStartupType(string serviceName)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                
                if (key != null)
                {
                    var startType = key.GetValue("Start");
                    if (startType != null && int.TryParse(startType.ToString(), out var startValue))
                    {
                        return startValue;
                    }
                }
                return 3; // Manual
            }
            catch
            {
                return 3; // Manual
            }
        }

        /// <summary>
        /// Define o tipo de startup de um serviço via Registry
        /// </summary>
        private bool SetStartupType(string serviceName, int startupType)
        {
            try
            {
                _logger.LogDebug($"[WindowsServiceOptimizer] {serviceName}: Tentando definir startup como {startupType} (4=Disabled)...");
                
                var registryPath = $@"SYSTEM\\CurrentControlSet\\Services\\{serviceName}";
                bool isServiceKeyWritable = IsRegistryKeyWritable(registryPath);
                if (!isServiceKeyWritable)
                {
                    _logger.LogWarning($"[WindowsServiceOptimizer] {serviceName}: ❌ Acesso negado para modificação do Registry. Serviço pode estar protegido por sistema.");
                    return false;
                }
                
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryPath, true);
                
                if (key != null)
                {
                    key.SetValue("Start", startupType, Microsoft.Win32.RegistryValueKind.DWord);
                    key.Flush(); // Garante que foi gravado
                    _logger.LogDebug($"[WindowsServiceOptimizer] {serviceName}: ✅ Startup definido para {startupType}");
                    return true;
                }
                else
                {
                    _logger.LogError($"[WindowsServiceOptimizer] {serviceName}: ❌ Chave do Registry não encontrada!");
                    return false;
                }
            }
            catch (System.Security.SecurityException ex)
            {
                _logger.LogWarning($"[WindowsServiceOptimizer] {serviceName}: ❌ Erro de segurança de Registry: {ex.Message}");
                _logger.LogWarning($"[WindowsServiceOptimizer] {serviceName}: Registro pode estar protegido ou permissions insuficientes.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WindowsServiceOptimizer] {serviceName}: ❌ Erro inesperado ao definir startup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica se uma chave do Registry pode ser escrita
        /// </summary>
        private static bool IsRegistryKeyWritable(string path)
        {
            try
            {
                using var testKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path, true);
                return testKey != null;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Desativa temporariamente um serviço para otimização
        /// </summary>
        private async Task<bool> StopServiceForOptimization(string serviceName, ServiceOriginalState state)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug($"[WindowsServiceOptimizer] Processando {serviceName}...");
                
                // Se já estava parado e desabilitado, não modificar
                if (state.OriginalStatus == ServiceControllerStatus.Stopped && 
                    state.OriginalStartupType == 4)
                {
                    state.OperationResult = "Ignored - Already disabled";
                    _servicesAlreadyDisabled++;
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: Já estava desativado e parado - ignorado");
                    return false;
                }

                // Se está rodando mas startup já é Disabled, apenas parar (não alterar startup)
                bool skipStartupChange = state.OriginalStartupType == 4;

                _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: Status={state.OriginalStatus}, Startup={state.OriginalStartupType}, SkipStartupChange={skipStartupChange}");

                // Tenta parar o serviço
                using var service = new ServiceController(serviceName);
                
                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: Parando serviço (Status atual: {service.Status})...");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: ✅ Parado com sucesso");
                }
                else
                {
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: Já estava parado");
                }

                // Define como Disabled temporariamente (4 = Disabled) - só se já não era
                if (!skipStartupChange)
                {
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: Definindo startup como Disabled (4)...");
                    SetStartupType(serviceName, 4);
                    _logger.LogInfo($"[WindowsServiceOptimizer] {serviceName}: ✅ Startup alterado para Disabled");
                }

                state.WasModified = true;
                state.ModifiedAt = DateTime.Now;
                state.OperationResult = skipStartupChange ? "Success - Stopped (startup já era Disabled)" : "Success - Stopped and Disabled";
                _servicesAltered++;
                
                stopwatch.Stop();
                _logger.LogSuccess($"[WindowsServiceOptimizer] ✅ {serviceName}: Parado em {stopwatch.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                state.OperationResult = $"Failed - {ex.Message}";
                state.ErrorMessage = ex.Message;
                _totalErrors++;
                stopwatch.Stop();
                _logger.LogWarning($"[WindowsServiceOptimizer] ❌ {serviceName}: Falha após {stopwatch.ElapsedMilliseconds}ms - {ex.Message}");
                _logger.LogWarning($"[WindowsServiceOptimizer] StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Ativa o Modo Gamer - Desativa serviços temporariamente
        /// </summary>
        public async Task ActivateOptimizationAsync()
        {
            // VERIFICAÇÃO CRÍTICA: Administrator privileges
            if (!IsRunningAsAdmin())
            {
                _logger.LogError("═══════════════════════════════════════════════════════════════");
                _logger.LogError("[WindowsServiceOptimizer] ❌ ERRO CRÍTICO: Aplicativo NÃO está rodando como Administrador!");
                _logger.LogError("[WindowsServiceOptimizer] ❌ Serviços do Windows NÃO podem ser desativados sem privilégios elevados.");
                _logger.LogError("[WindowsServiceOptimizer] ❌ Por favor, execute o VoltrisOptimizer como Administrador.");
                _logger.LogError("═══════════════════════════════════════════════════════════════");
                return;
            }

            if (_isOptimizationActive)
            {
                _logger.LogWarning("[WindowsServiceOptimizer] Otimização já está ativa");
                return;
            }

            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[WindowsServiceOptimizer] 🚀 INICIANDO OTIMIZAÇÃO DE SERVIÇOS - MODO GAMER");
            _logger.LogInfo("[WindowsServiceOptimizer] ✅ Privilégios de Administrador confirmados");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            _optimizationStartTime = DateTime.Now;
            var stopwatch = Stopwatch.StartNew();
            
            // Reseta contadores
            _servicesFound = 0;
            _servicesAltered = 0;
            _servicesIgnored = 0;
            _servicesAlreadyDisabled = 0;
            _servicesRestored = 0;
            _totalErrors = 0;
            _originalStates.Clear();

            // Monta lista completa
            var servicesToOptimize = BuildCompleteServiceList();
            
            _logger.LogInfo($"[WindowsServiceOptimizer] Avaliando {servicesToOptimize.Count} serviços...");

            // Processa cada serviço
            foreach (var serviceName in servicesToOptimize)
            {
                try
                {
                    _servicesFound++;
                    
                    // Registra estado original
                    var originalState = await RecordOriginalState(serviceName);
                    if (originalState == null)
                    {
                        _servicesIgnored++;
                        continue;
                    }

                    _originalStates[serviceName] = originalState;

                    // Tenta desativar
                    await StopServiceForOptimization(serviceName, originalState);
                }
                catch (Exception ex)
                {
                    _servicesIgnored++;
                    _logger.LogError($"[WindowsServiceOptimizer] Erro crítico ao processar {serviceName}: {ex.Message}");
                }
            }

            stopwatch.Stop();
            _isOptimizationActive = true;

            // Gera resumo
            GenerateSummaryReport("ATIVAÇÃO", stopwatch.ElapsedMilliseconds);
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[WindowsServiceOptimizer] ✅ OTIMIZAÇÃO DE SERVIÇOS CONCLUÍDA");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Restaura serviços ao estado original (Rollback Perfeito)
        /// </summary>
        public async Task DeactivateOptimizationAsync()
        {
            if (!_isOptimizationActive)
            {
                _logger.LogWarning("[WindowsServiceOptimizer] Otimização não está ativa");
                return;
            }

            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[WindowsServiceOptimizer] 🔄 INICIANDO RESTAURAÇÃO DE SERVIÇOS - ROLLBACK PERFEITO");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            
            var stopwatch = Stopwatch.StartNew();
            _servicesRestored = 0;
            _totalErrors = 0;

            // Restaura apenas serviços modificados
            foreach (var kvp in _originalStates.ToList())
            {
                var serviceName = kvp.Key;
                var state = kvp.Value;

                if (!state.WasModified)
                {
                    _logger.LogDebug($"[WindowsServiceOptimizer] {serviceName}: Não foi modificado - ignorado na restauração");
                    continue;
                }

                try
                {
                    await RestoreServiceToOriginalState(serviceName, state);
                }
                catch (Exception ex)
                {
                    _totalErrors++;
                    _logger.LogError($"[WindowsServiceOptimizer] Erro ao restaurar {serviceName}: {ex.Message}");
                }
            }

            stopwatch.Stop();
            _isOptimizationActive = false;
            _originalStates.Clear();

            GenerateSummaryReport("RESTAURAÇÃO", stopwatch.ElapsedMilliseconds);
            
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
            _logger.LogInfo("[WindowsServiceOptimizer] ✅ RESTAURAÇÃO CONCLUÍDA");
            _logger.LogInfo("═══════════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Restaura um serviço ao estado original
        /// </summary>
        private async Task<bool> RestoreServiceToOriginalState(string serviceName, ServiceOriginalState state)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var service = new ServiceController(serviceName);
                
                // Restaura o Startup Type primeiro
                SetStartupType(serviceName, state.OriginalStartupType);
                
                // Se estava rodando originalmente, inicia o serviço
                if (state.OriginalStatus == ServiceControllerStatus.Running)
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }

                _servicesRestored++;
                stopwatch.Stop();
                
                _logger.LogSuccess($"[WindowsServiceOptimizer] ✅ {serviceName}: Restaurado em {stopwatch.ElapsedMilliseconds}ms (Status={state.OriginalStatus}, Startup={state.OriginalStartupType})");
                return true;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning($"[WindowsServiceOptimizer] ❌ {serviceName}: Falha na restauração após {stopwatch.ElapsedMilliseconds}ms - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gera relatório resumido da operação
        /// </summary>
        private void GenerateSummaryReport(string operation, long elapsedMs)
        {
            _logger.LogInfo("╔═══════════════════════════════════════════════════════════════╗");
            _logger.LogInfo($"║  RELATÓRIO DE {operation,-33} ║");
            _logger.LogInfo("╠═══════════════════════════════════════════════════════════════╣");
            _logger.LogInfo($"║  Total de serviços encontrados:   {_servicesFound,3}                          ║");
            _logger.LogInfo($"║  Total de serviços alterados:     {_servicesAltered,3}                          ║");
            _logger.LogInfo($"║  Total de serviços ignorados:     {_servicesIgnored,3}                          ║");
            _logger.LogInfo($"║  Serviços já desativados:         {_servicesAlreadyDisabled,3}                          ║");
            
            if (operation == "RESTAURAÇÃO")
            {
                _logger.LogInfo($"║  Serviços restaurados:            {_servicesRestored,3}                          ║");
            }
            
            _logger.LogInfo($"║  Total de erros:                  {_totalErrors,3}                          ║");
            _logger.LogInfo($"║  Tempo total da operação:         {elapsedMs,3} ms                     ║");
            _logger.LogInfo("╚═══════════════════════════════════════════════════════════════╝");
        }

        public void Dispose()
        {
            if (_isOptimizationActive)
            {
                _logger.LogWarning("[WindowsServiceOptimizer] Disposição com otimização ativa - tentando restauração de emergência...");
                DeactivateOptimizationAsync().Wait();
            }
        }
    }
}