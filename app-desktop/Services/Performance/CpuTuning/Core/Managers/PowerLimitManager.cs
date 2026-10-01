using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Models;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Core.Managers
{
    public class PowerLimitManager
    {
        private readonly IHardwareBackend _backend;
        private readonly ILoggingService _logger;
        private readonly IHardwareCapabilityDetector _capDetector;

        private const uint MSR_PKG_POWER_LIMIT = 0x610;
        
        // Estado original para restauração
        private int? _originalPl1;
        private int? _originalPl2;
        private bool _originalLocked;
        private ulong _originalMsrValue;

        // New: Thermal Monitoring and dynamic scaling fields
        private readonly IGlobalThermalMonitorService _thermalService;
        private int _currentPl1;
        private int _currentPl2;
        private int _targetMaxPl1;
        private int _targetMaxPl2;
        private CancellationTokenSource? _rollbackWatchdogCts;
        private Task? _rollbackWatchdogTask;
        private bool _mmioSyncAvailable = false;
        private bool _mmioSyncAttempted = false;
        private bool _mmioSyncSupported = false;
        private int _watchdogIntervalMs = 30000;
        private ulong _cachedMchBar = 0;
        private bool _mchBarDiscoveryAttempted = false;
        private DateTime _lastMmioLog = DateTime.MinValue;
        private string _lastOperationContext = "";
        private double _raplPowerUnit = 0;

        // §2 - TDP base detectado para cálculo inteligente
        private int _tdpBase = 0;
        private int _tdpMax = 0;
        // §3 - Segurança
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 3;
        private const int MaxRetryAttempts = 3;
        private const double MaxTdpMultiplier = 1.5;
        private const int MinimumPl1 = 15;
        private const int MinimumPl2 = 25;

        // Factory defaults — capturados UMA ÚNICA VEZ na primeira execução e persistidos em disco
        private int? _factoryPl1;
        private int? _factoryPl2;
        private bool _factoryDefaultsCaptured;
        private static readonly string _factoryDefaultsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Voltris", "factory_pl_v2.dat");

        // §5 - AI Hook Points (placeholder para integração Vanguard)
        private double _aiSuggestedPl1 = 0;
        private double _aiSuggestedPl2 = 0;
        private bool _aiSuggestionApplied = false;

        // Flag para evitar spam de log quando SafeFallbackBackend (SecurityBlocked) está ativo
        // O log informativo é emitido apenas na PRIMEIRA chamada — silêncio nas chamadas subsequentes.
        private bool _securityBlockedLoggedOnce = false;

        // Emitido apenas UMA vez quando a inicialização do backend falha por motivo ambiental
        // (driver ausente, sem admin, OS não suportado) — evita repetir o mesmo aviso a cada operação.
        private bool _backendInitFailureLoggedOnce = false;

        public PowerLimitManager(IHardwareBackend backend, ILoggingService logger, IHardwareCapabilityDetector capDetector, IGlobalThermalMonitorService thermalService)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _capDetector = capDetector ?? throw new ArgumentNullException(nameof(capDetector));
            _thermalService = thermalService ?? throw new ArgumentNullException(nameof(thermalService));

            // Subscribe to thermal metrics updates for dynamic power scaling
            _thermalService.MetricsUpdated += OnMetricsUpdated;
            _logger.LogInfo("[PowerLimitManager] Subscrito a MetricsUpdated para loop térmico.");
        }

        // §2 - Descoberta do TDP base via MSR 0x614 (PKG_POWER_INFO)
        private bool DiscoverTdpBase(out int tdpBase, out int tdpMax)
        {
            tdpBase = _capDetector.GetCapabilities().BaseTdp;
            tdpMax = _capDetector.GetCapabilities().MaxTdp;

            if (_backend.ReadMsr(0x614, out ulong pkgPowerInfo))
            {
                double powerUnit = 1.0;
                if (_backend.ReadMsr(0x606, out ulong raplUnit))
                {
                    powerUnit = 1.0 / Math.Pow(2, (byte)(raplUnit & 0xF));
                }
                int tdpFromMsr = (int)((pkgPowerInfo & 0x7FFF) * powerUnit);
                int maxFromMsr = (int)(((pkgPowerInfo >> 32) & 0x7FFF) * powerUnit);

                if (tdpFromMsr > 0) tdpBase = tdpFromMsr;
                if (maxFromMsr > 0) tdpMax = maxFromMsr;
            }

            if (tdpBase <= 0) tdpBase = 65;
            if (tdpMax <= 0 || tdpMax < tdpBase) tdpMax = (int)(tdpBase * MaxTdpMultiplier);

            _tdpBase = tdpBase;
            _tdpMax = tdpMax;
            return true;
        }

        private double GetRaplPowerUnit()
        {
            if (_raplPowerUnit > 0) return _raplPowerUnit;
            if (_backend.GetStatus() == BackendStatus.Ready && _backend.ReadMsr(0x606, out ulong raplUnit))
            {
                _raplPowerUnit = 1.0 / Math.Pow(2, (byte)(raplUnit & 0xF));
            }
            else
            {
                _raplPowerUnit = 0.125; // fallback 1/8
            }
            return _raplPowerUnit;
        }

        // §3 - Detecção simplificada de bateria fraca
        private static bool IsBatteryLow()
        {
            try
            {
                var battery = System.Windows.Forms.SystemInformation.PowerStatus;
                if (battery.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery))
                    return false;
                return battery.BatteryLifePercent < 0.20f;
            }
            catch
            {
                return false;
            }
        }

        // §5 - AI Hook Point: placeholder para integração futura com Vanguard IA
        private double GetAiSuggestedPl1(double calculatedPl1, double cpuTemp)
        {
            if (_aiSuggestionApplied && _aiSuggestedPl1 > 0)
                return _aiSuggestedPl1;
            return calculatedPl1;
        }

        private double GetAiSuggestedPl2(double calculatedPl2, double cpuTemp)
        {
            if (_aiSuggestionApplied && _aiSuggestedPl2 > 0)
                return _aiSuggestedPl2;
            return calculatedPl2;
        }

        // §2 - Cálculo inteligente de PL1/PL2 baseado em TDP, temperatura e perfil
        private (int pl1, int pl2, int tau) CalculateDynamicPowerLimits(bool isLaptop, double cpuTemp)
        {
            var caps = _capDetector.GetCapabilities();
            if (caps.GamerPl1Override.HasValue && caps.GamerPl1Override.Value > 0 &&
                caps.GamerPl2Override.HasValue && caps.GamerPl2Override.Value > 0)
            {
                _logger?.LogInfo($"[PowerLimitManager] Aplicando overrides do usuário (UI): PL1={caps.GamerPl1Override.Value}W, PL2={caps.GamerPl2Override.Value}W");
                return (caps.GamerPl1Override.Value, caps.GamerPl2Override.Value, isLaptop ? 28 : 56);
            }

            // Utilizar SEMPRE os factory defaults (capturados uma única vez do MSR ou do disco)
            // como base de cálculo. Isso garante que mesmo que o MSR atual tenha sido alterado
            // (por throttling, outro software, ou reinicialização do app), a base permanece a original.
            int factoryPl1 = _factoryPl1 ?? _originalPl1 ?? (_tdpBase > 0 ? _tdpBase : 65);
            int factoryPl2 = _factoryPl2 ?? _originalPl2 ?? (_tdpMax > 0 ? _tdpMax : (int)(factoryPl1 * MaxTdpMultiplier));

            _logger?.LogInfo($"[PowerLimitManager] Calculando limites dinâmicos sobre base de fábrica: PL1={factoryPl1}W, PL2={factoryPl2}W");

            if (isLaptop)
            {
                double safeTemp = 85.0;
                double criticalTemp = 95.0;
                double thermalHeadroom = Math.Max(0, (safeTemp - cpuTemp) / (safeTemp - criticalTemp));
                thermalHeadroom = Math.Min(1.0, Math.Max(0, thermalHeadroom));

                // Aumenta de forma inteligente: +25% do valor original se a temperatura estiver fria,
                // caindo gradualmente até 1.0x (retorno ao padrão) quando quente.
                double multiplier = 1.0 + (thermalHeadroom * 0.25);

                int calculatedPl1 = (int)(factoryPl1 * multiplier);
                int calculatedPl2 = (int)(factoryPl2 * multiplier);

                // Garantir limites mínimos seguros
                calculatedPl1 = Math.Max(calculatedPl1, MinimumPl1);
                calculatedPl2 = Math.Max(calculatedPl2, MinimumPl2);
                calculatedPl2 = Math.Max(calculatedPl2, calculatedPl1);

                if (IsBatteryLow())
                {
                    int reduced = (int)(factoryPl1 * 0.40);
                    calculatedPl1 = Math.Min(calculatedPl1, Math.Max(reduced, MinimumPl1));
                    calculatedPl2 = Math.Min(calculatedPl2, Math.Min(calculatedPl1 * 2, reduced * 2));
                    _logger.LogWarning($"[PowerLimitManager] Bateria baixa (<20%): PLs reduzidos para {calculatedPl1}/{calculatedPl2}W");
                }

                int tau = 28;
                return (calculatedPl1, calculatedPl2, tau);
            }
            else
            {
                double safeTemp = 90.0;
                double criticalTemp = 100.0;
                double thermalHeadroom = Math.Max(0, (safeTemp - cpuTemp) / (safeTemp - criticalTemp));
                thermalHeadroom = Math.Min(1.0, Math.Max(0, thermalHeadroom));

                // Desktop: Aumenta +30% do valor original quando a temperatura estiver fria
                double multiplier = 1.0 + (thermalHeadroom * 0.30);

                int calculatedPl1 = (int)(factoryPl1 * multiplier);
                int calculatedPl2 = (int)(factoryPl2 * multiplier);

                calculatedPl1 = Math.Max(calculatedPl1, MinimumPl1);
                calculatedPl2 = Math.Max(calculatedPl2, MinimumPl2);
                calculatedPl2 = Math.Max(calculatedPl2, calculatedPl1);

                int tau = 56;
                return (calculatedPl1, calculatedPl2, tau);
            }
        }

        // =====================================================================
        //  §FACTORY — Captura / carrega / persiste os limites reais de fábrica
        //  Lê MSR 0x610 UMA ÚNICA VEZ e salva em disco para sobreviver a
        //  restart do app. Usado como base para cálculos e restauração.
        // =====================================================================
        private bool TryCaptureFactoryDefaults()
        {
            // Já capturamos nesta sessão?
            if (_factoryDefaultsCaptured && _factoryPl1.HasValue && _factoryPl2.HasValue)
                return true;

            // Tenta carregar de disco (arquivo criado na primeira execução do app)
            if (LoadFactoryDefaultsFromFile())
                return true;

            // Backend pronto?
            if (_backend.GetStatus() != BackendStatus.Ready)
            {
                _logger.LogWarning("[PowerLimitManager] Backend não pronto para capturar defaults de fábrica. Tentativa adiada.");
                return false;
            }

            // Lê MSR 0x610 e persiste
            if (!_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong msrValue))
            {
                _logger.LogWarning("[PowerLimitManager] Falha ao ler MSR 0x610 para capturar defaults de fábrica.");
                return false;
            }

            ParseMsr(msrValue, out int pl1, out int pl2, out _, out _);

            // INTELIGÊNCIA UNIVERSAL:
            // Se o MSR reportar um limite menor que o TDP Base oficial do processador (ex: ler 16W em uma CPU de 28W),
            // significa que a fabricante da placa-mãe mascarou o MSR e gerencia a energia via MMIO fechado.
            // Para não capar a máquina de outros usuários, elevamos o padrão lido no mínimo para o TDP Base.
            if (pl1 > 0 && pl1 < _tdpBase)
            {
                _logger.LogWarning($"[PowerLimitManager] MSR PL1 ({pl1}W) menor que o TDP Base ({_tdpBase}W). Assumindo TDP Base como proteção de fábrica.");
                pl1 = _tdpBase;
            }
            if (pl2 > 0 && pl2 <= pl1 && _tdpMax > pl1)
            {
                pl2 = _tdpMax;
            }

            _factoryPl1 = pl1;
            _factoryPl2 = pl2;
            _factoryDefaultsCaptured = true;
            _logger.LogSuccess($"[PowerLimitManager] ✅ Defaults de fábrica capturados do MSR: PL1={pl1}W, PL2={pl2}W");

            SaveFactoryDefaultsToFile();
            return true;
        }

        private bool LoadFactoryDefaultsFromFile()
        {
            try
            {
                if (!File.Exists(_factoryDefaultsPath))
                    return false;

                var lines = File.ReadAllLines(_factoryDefaultsPath);
                if (lines.Length < 2) return false;
                if (int.TryParse(lines[0], out int pl1) && int.TryParse(lines[1], out int pl2) && pl1 > 0 && pl2 > 0)
                {
                    _factoryPl1 = pl1;
                    _factoryPl2 = pl2;
                    _factoryDefaultsCaptured = true;
                    _logger.LogSuccess($"[PowerLimitManager] ✅ Defaults carregados de disco: PL1={pl1}W, PL2={pl2}W");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerLimitManager] Falha ao ler arquivo de defaults: {ex.Message}");
            }
            return false;
        }

        private void SaveFactoryDefaultsToFile()
        {
            if (!_factoryPl1.HasValue || !_factoryPl2.HasValue) return;
            try
            {
                var dir = Path.GetDirectoryName(_factoryDefaultsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_factoryDefaultsPath, $"{_factoryPl1.Value}\n{_factoryPl2.Value}");
                _logger.LogSuccess($"[PowerLimitManager] ✅ Defaults persistidos em {_factoryDefaultsPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerLimitManager] Não foi possível persistir defaults: {ex.Message}");
            }
        }

        public bool ApplyGamingPowerLimits(bool isLaptop)
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            _lastOperationContext = $"ApplyGamingPowerLimits_{operationId}";
            
            _logger?.LogTrace($"[PowerLimitManager][{operationId}] Enter ApplyGamingPowerLimits(isLaptop={isLaptop})");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] INICIANDO APLICAÇÃO DE POWER LIMITS");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Thread: {Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Plataforma: {(isLaptop ? "Notebook" : "Desktop")}");

            // 1. VALIDAÇÃO DO BACKEND
            var backendStatus = _backend.GetStatus();
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Status do Backend: {backendStatus}");
            
            // SecurityBlocked = SafeFallbackBackend está ativo (substitui WinRing0 por segurança).
            // Este é um estado ESPERADO por design — não é um erro crítico.
            // O SafeFallbackBackend nunca carrega driver e nunca escreve MSR por definição.
            // Logar apenas uma vez como Info para não poluir o log com Errors repetidos.
            if (backendStatus == BackendStatus.SecurityBlocked)
            {
                if (!_securityBlockedLoggedOnce)
                {
                    _securityBlockedLoggedOnce = true;
                    _logger.LogInfo(
                        $"[PowerLimitManager][{operationId}] ℹ️ Backend em modo seguro (SecurityBlocked — SafeFallbackBackend ativo). " +
                        "Ajuste fino de CPU via MSR desativado por design (substitui WinRing0). " +
                        "Funcionalidades de otimização de plano de energia continuam disponíveis.");
                }
                return false;
            }
            
            if (backendStatus != BackendStatus.Ready)
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Backend não está Ready. Tentando inicializar...");
                if (!_backend.Initialize())
                {
                    // Driver ausente / sem permissões de admin / OS não suportado são condições AMBIENTAIS,
                    // não bugs da aplicação: o app continua funcionando sem ajuste fino de CPU (modo degradado).
                    if (!_backendInitFailureLoggedOnce)
                    {
                        _backendInitFailureLoggedOnce = true;
                        _logger.LogWarning(
                            $"[PowerLimitManager][{operationId}] ⚠️ Backend indisponível após tentativa de inicialização. " +
                            $"Status: {_backend.GetStatus()} — driver não carregado ou sem permissões de administrador. " +
                            "Executando em modo degradado (ajuste fino de CPU via MSR desativado).");
                    }
                    return false;
                }
                _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ Backend inicializado com sucesso.");
            }

            // 2. DETECÇÃO DE HARDWARE
            var vendor = _backend.GetCpuVendor();
            var caps = _capDetector.GetCapabilities();
            _logger.LogInfo($"[PowerLimitManager][{operationId}] CPU Vendor: {vendor}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] CPU Modelo: {caps.CpuModel}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] CPU Geração: {caps.CpuGeneration}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Platform: {caps.Platform}");

            if (vendor == CpuVendor.AMD)
            {
                _logger.LogInfo($"[PowerLimitManager][{operationId}] ✅ AMD detectado: usando caminho de compatibilidade.");
                _logger.LogInfo($"[PowerLimitManager][{operationId}] MSR 0x610 será usado para leitura (fallback); ajuste fino via TDP discovery.");
            }

            if (vendor != CpuVendor.Intel && vendor != CpuVendor.AMD)
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] CPU nao suportada: {vendor}");
                return false;
            }

            // 1b. DESCOBERTA DO TDP BASE (§2)
            DiscoverTdpBase(out int discoveredTdp, out int discoveredMaxTdp);
            _logger.LogInfo($"[PowerLimitManager][{operationId}] TDP Base: {discoveredTdp}W | TDP Max: {discoveredMaxTdp}W");

            // 3. CAPTURA / CARREGAMENTO DOS FACTORY DEFAULTS (UMA ÚNICA VEZ)
            if (!_factoryDefaultsCaptured)
            {
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Tentando capturar factory defaults...");
                TryCaptureFactoryDefaults();
            }
            
            // LOG DE DIAGNÓSTICO: Verificar estado dos factory defaults
            _logger.LogInfo($"[PowerLimitManager][{operationId}] DIAGNÓSTICO Factory Defaults:");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _factoryPl1 = {_factoryPl1?.ToString() ?? "null"}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _factoryPl2 = {_factoryPl2?.ToString() ?? "null"}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _factoryDefaultsCaptured = {_factoryDefaultsCaptured}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _originalPl1 = {_originalPl1?.ToString() ?? "null"}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _originalPl2 = {_originalPl2?.ToString() ?? "null"}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _currentPl1 = {_currentPl1}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _currentPl2 = {_currentPl2}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _targetMaxPl1 = {_targetMaxPl1}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   _targetMaxPl2 = {_targetMaxPl2}W");

            if (!_factoryPl1.HasValue || !_factoryPl2.HasValue)
            {
                _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA: Não foi possível obter os limites de fábrica. Tentando leitura direta do MSR...");
                if (!_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong fallbackValue))
                {
                    _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA CRÍTICA: Não foi possível ler MSR 0x{MSR_PKG_POWER_LIMIT:X}");
                    _logger.LogError($"[PowerLimitManager][{operationId}] Motivo: CPU pode não suportar RAPL ou BIOS bloqueou acesso ao MSR.");
                    _logger.LogError($"[PowerLimitManager][{operationId}] Ação: Verifique se a CPU suporta Intel RAPL (6th gen+).");
                    return false;
                }
                ParseMsr(fallbackValue, out int fallbackPl1, out int fallbackPl2, out _, out _);
                _factoryPl1 = fallbackPl1;
                _factoryPl2 = fallbackPl2;
                _factoryDefaultsCaptured = true;
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Usando leitura direta do MSR como fallback: PL1={fallbackPl1}W, PL2={fallbackPl2}W");
            }

            // 4. LEITURA DO MSR ATUAL (validação + lock check)
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Lendo MSR 0x{MSR_PKG_POWER_LIMIT:X} (PKG_POWER_LIMIT) para validação...");
            if (!_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentMsrValue))
            {
                _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA CRÍTICA: Não foi possível ler MSR 0x{MSR_PKG_POWER_LIMIT:X}");
                _logger.LogError($"[PowerLimitManager][{operationId}] Motivo: CPU pode não suportar RAPL ou BIOS bloqueou acesso ao MSR.");
                _logger.LogError($"[PowerLimitManager][{operationId}] Ação: Verifique se a CPU suporta Intel RAPL (6th gen+).");
                return false;
            }
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ MSR 0x{MSR_PKG_POWER_LIMIT:X} lido: 0x{currentMsrValue:X}");

            // 5. PARSING DO VALOR ATUAL
            ParseMsr(currentMsrValue, out int currentPl1, out int currentPl2, out int timeWindow, out bool isLocked);
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Estado atual do hardware:");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   PL1: {currentPl1}W (Factory: {_factoryPl1}W)");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   PL2: {currentPl2}W (Factory: {_factoryPl2}W)");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   Time Window: {timeWindow}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   Lock Bit: {(isLocked ? "LOCKED (BIOS bloqueou)" : "UNLOCKED")}");

            // 6. CACHE DO ESTADO ORIGINAL (SEMPRE a partir dos factory defaults)
            if (_originalPl1 == null)
            {
                _originalPl1 = _factoryPl1;
                _originalPl2 = _factoryPl2;
                _originalLocked = isLocked;
                _originalMsrValue = BuildMsrValue(_factoryPl1.Value, _factoryPl2.Value, 56, isLocked);
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Estado de fábrica salvo para restauração: PL1={_factoryPl1}W, PL2={_factoryPl2}W");
            }
            else
            {
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Estado de fábrica já em cache: PL1={_originalPl1}W, PL2={_originalPl2}W");
            }

            // 7. VERIFICAÇÃO DE BIOS LOCK
            if (isLocked)
            {
                _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA: MSR 0x{MSR_PKG_POWER_LIMIT:X} está TRANCADO pelo BIOS (Bit 63 = 1)");
                _logger.LogError($"[PowerLimitManager][{operationId}] Motivo: Fabricante da placa mãe bloqueou alterações de power limit.");
                _logger.LogError($"[PowerLimitManager][{operationId}] Impacto: Não é possível alterar PL1/PL2 via software.");
                _logger.LogError($"[PowerLimitManager][{operationId}] Solução: Entre na BIOS e desative \"Power Limit Lock\" ou use ferramenta de BIOS mod.");
                return false;
            }

            // 7. CÁLCULO DINÂMICO DE PL1/PL2 (§2 - Sistema Inteligente)
            double cpuTemp = _thermalService.CurrentMetrics?.CpuTemperature ?? 60.0;
            var (targetPl1, targetPl2, targetWindow) = CalculateDynamicPowerLimits(isLaptop, cpuTemp);

            // §5 - AI Hook: permite que a IA Vanguard ajuste os valores calculados
            targetPl1 = (int)GetAiSuggestedPl1(targetPl1, cpuTemp);
            targetPl2 = (int)GetAiSuggestedPl2(targetPl2, cpuTemp);

            _targetMaxPl1 = targetPl1;
            _targetMaxPl2 = targetPl2;

            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] §2 LOG DE AUDITORIA (Original → Calculado → Aplicado → Confirmado)");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   Original (Factory): PL1={_factoryPl1}W | PL2={_factoryPl2}W | Atual: PL1={currentPl1}W | PL2={currentPl2}W | Tau={timeWindow}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   Calculado: PL1={targetPl1}W | PL2={targetPl2}W | Tau={targetWindow}s");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   TDP Base: {_tdpBase}W | Cap Máx: {_tdpMax}W | Temp: {cpuTemp:F1}°C");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   BatteryLow: {IsBatteryLow()}");

            // 8. ESCRITA COM RETRY (§7 - Validação Real)
            ulong newValue = BuildMsrValue(targetPl1, targetPl2, targetWindow, false);
            bool writeSuccess = false;
            bool validationPassed = false;
            int retryCount = 0;

            while (retryCount <= MaxRetryAttempts && !validationPassed)
            {
                if (retryCount > 0)
                {
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] Tentativa {retryCount}/{MaxRetryAttempts}...");
                }

                // Escrita MSR
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Escrevendo MSR 0x{MSR_PKG_POWER_LIMIT:X} (tentativa {retryCount + 1})...");
                writeSuccess = _backend.WriteMsr(MSR_PKG_POWER_LIMIT, newValue);

                if (!writeSuccess)
                {
                    _logger.LogError($"[PowerLimitManager][{operationId}] ❌ WriteMsr retornou false na tentativa {retryCount + 1}");
                    retryCount++;
                    continue;
                }

                // Validação: ler MSR de volta para confirmar (§7)
                if (_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong validationValue))
                {
                    ParseMsr(validationValue, out int valPl1, out int valPl2, out int valWindow, out bool valLocked);
                    _logger.LogInfo($"[PowerLimitManager][{operationId}]   Confirmado: PL1={valPl1}W | PL2={valPl2}W | Tau={valWindow} | Lock={valLocked}");

                    if (valPl1 == targetPl1 && valPl2 == targetPl2)
                    {
                        validationPassed = true;
                        _consecutiveFailures = 0; // §3 - Reset no contador de falhas
                        _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ Validação MSR OK (tentativa {retryCount + 1})");
                    }
                    else if (valPl1 == currentPl1 && valPl2 == currentPl2)
                    {
                        _logger.LogWarning($"[PowerLimitManager][{operationId}] EC manteve valores anteriores ({currentPl1}/{currentPl2}). Reaplicando...");
                        retryCount++;
                    }
                    else
                    {
                        _logger.LogWarning($"[PowerLimitManager][{operationId}] ⚠️ Valor inconsistente: solicitado {targetPl1}/{targetPl2}, lido {valPl1}/{valPl2}");
                        retryCount++;
                    }
                }
                else
                {
                    _logger.LogError($"[PowerLimitManager][{operationId}] ❌ Falha na leitura MSR pós-escrita (tentativa {retryCount + 1})");
                    retryCount++;
                }
            }

            if (!validationPassed)
            {
                _consecutiveFailures++;
                _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA APÓS {MaxRetryAttempts + 1} TENTATIVAS");
                _logger.LogError($"[PowerLimitManager][{operationId}] Falhas consecutivas: {_consecutiveFailures}/{MaxConsecutiveFailures}");

                // §4 - Auto-rollback após 3 falhas consecutivas
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError($"[PowerLimitManager][{operationId}] 🚨 AUTO-ROLLBACK: {MaxConsecutiveFailures} falhas consecutivas. Restaurando limites originais...");
                    RestoreOriginalLimits();
                    StopRollbackWatchdog();
                    return false;
                }

                return false;
            }

            // 9. MMIO SYNC
            _mmioSyncAttempted = true;
            _cachedMchBar = _backend.DiscoverMchBar();
            _mchBarDiscoveryAttempted = true;
            bool mmioSynced = false;
            _mmioSyncAvailable = false;
            _mmioSyncSupported = false;

            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] INICIANDO MMIO SYNC");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");

            if (_cachedMchBar > 0)
            {
                ulong mmioAddr = _cachedMchBar + 0x59A0;
                _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ MCHBAR descoberto: 0x{_cachedMchBar:X}");
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Endereço MMIO alvo: 0x{mmioAddr:X}");
                _mmioSyncSupported = true;

                _logger.LogInfo($"[PowerLimitManager][{operationId}] Lendo MMIO atual...");
                if (_backend.ReadPhysicalMemory(mmioAddr, 8, out ulong currentMmio))
                {
                    int mmioPl1Before = (int)((currentMmio & 0x7FFF) / 8);
                    int mmioPl2Before = (int)(((currentMmio >> 32) & 0x7FFF) / 8);
                    _logger.LogInfo($"[PowerLimitManager][{operationId}] MMIO atual: PL1={mmioPl1Before}W, PL2={mmioPl2Before}W (Raw: 0x{currentMmio:X})");

                    _logger.LogInfo($"[PowerLimitManager][{operationId}] Escrevendo MMIO...");
                    if (_backend.WritePhysicalMemory(mmioAddr, newValue, 8))
                    {
                        _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ WritePhysicalMemory executado");

                        _logger.LogInfo($"[PowerLimitManager][{operationId}] Verificando escrita MMIO...");
                        if (_backend.ReadPhysicalMemory(mmioAddr, 8, out ulong verifyMmio))
                        {
                            int mmioPl1After = (int)((verifyMmio & 0x7FFF) / 8);
                            int mmioPl2After = (int)(((verifyMmio >> 32) & 0x7FFF) / 8);
                            _logger.LogInfo($"[PowerLimitManager][{operationId}] MMIO após escrita: PL1={mmioPl1After}W, PL2={mmioPl2After}W (Raw: 0x{verifyMmio:X})");

                            if (mmioPl1After == targetPl1 && mmioPl2After == targetPl2)
                            {
                                _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ MMIO Sync CONFIRMADO: PL1={mmioPl1After}W, PL2={mmioPl2After}W");
                                mmioSynced = true;
                                _mmioSyncAvailable = true;
                            }
                            else
                            {
                                _logger.LogWarning($"[PowerLimitManager][{operationId}] MMIO Sync parcial ou falhou");
                                _logger.LogWarning($"[PowerLimitManager][{operationId}] Esperado: PL1={targetPl1}W, PL2={targetPl2}W");
                                _logger.LogWarning($"[PowerLimitManager][{operationId}] Lido: PL1={mmioPl1After}W, PL2={mmioPl2After}W");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"[PowerLimitManager][{operationId}] ⚠️ Falha ao ler MMIO após escrita");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[PowerLimitManager][{operationId}] Falha ao escrever MMIO — operando em MSR-only mode");
                    }
                }
                else
                {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] MMIO indisponivel: Falha na leitura inicial");
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Impacto: Operando em MSR-only mode (BIOS pode reverter limites).");
                }
            }
            else
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] MCHBAR nao disponivel (0x{_cachedMchBar:X})");
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Impacto: Operando em MSR-only mode.");
            }

            // 10. SUCESSO FINAL
            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            string mmioStatus = mmioSynced ? " + MMIO Sync ✅" : " (MSR-only ⚠️)";
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ POWER LIMITS APLICADOS COM SUCESSO");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}]   Original: PL1={_factoryPl1}W, PL2={_factoryPl2}W");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}]   Aplicado: PL1={targetPl1}W, PL2={targetPl2}W");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}]   Confirmado: ✅ (via MSR read-back)");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}]   MMIO: {(mmioSynced ? "Sincronizado" : "Não disponível")}{mmioStatus}");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}]   Tempo total: {elapsedMs:F0}ms");
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");

            _currentPl1 = targetPl1;
            _currentPl2 = targetPl2;

            _watchdogIntervalMs = mmioSynced ? 30000 : 1000;
            _logger.LogInfo($"[PowerLimitManager][{operationId}] 🐕 Watchdog configurado: intervalo={_watchdogIntervalMs}ms (MMIO={mmioSynced})");
            StartRollbackWatchdog();
            return true;
        }

        public bool RestoreOriginalLimits()
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            _lastOperationContext = $"RestoreOriginalLimits_{operationId}";
            
            _logger?.LogTrace($"[PowerLimitManager][{operationId}] Enter RestoreOriginalLimits");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] INICIANDO RESTAURAÇÃO DE LIMITES ORIGINAIS");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Thread: {Thread.CurrentThread.ManagedThreadId}");
            
            var backendStatus = _backend.GetStatus();
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Status do Backend: {backendStatus}");
            
            if (backendStatus != BackendStatus.Ready)
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Backend não está Ready. Tentando inicializar...");
                if (!_backend.Initialize())
                {
                    if (!_backendInitFailureLoggedOnce)
                    {
                        _backendInitFailureLoggedOnce = true;
                        _logger.LogWarning(
                            $"[PowerLimitManager][{operationId}] ⚠️ Backend indisponível. Status: {_backend.GetStatus()} — " +
                            "executando em modo degradado (ajuste fino de CPU via MSR desativado).");
                    }
                    return false;
                }
                _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ Backend inicializado com sucesso.");
            }
            
            // Tentar carregar factory defaults se ainda não foram capturados (defesa contra chamada sem ApplyGamingPowerLimits)
            if (!_factoryDefaultsCaptured)
            {
                TryCaptureFactoryDefaults();
            }

            // Prioridade: factory defaults > original cache > fallback TDP
            int restorePl1 = _factoryPl1 ?? _originalPl1 ?? (_tdpBase > 0 ? _tdpBase : 65);
            int restorePl2 = _factoryPl2 ?? _originalPl2 ?? (_tdpMax > 0 ? _tdpMax : (int)(restorePl1 * MaxTdpMultiplier));

            if (_factoryPl1.HasValue && _factoryPl2.HasValue)
            {
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Usando factory defaults persistentes para restauração.");
            }
            else if (_originalPl1.HasValue && _originalPl2.HasValue)
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] Factory defaults não disponíveis. Usando cache de sessão como fallback.");
            }
            else
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] ⚠️ NENHUM limite de fábrica disponível. Usando TDP detectado ({restorePl1}/{restorePl2}) como fallback emergencial.");
            }
            
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Estado de fábrica a restaurar:");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   PL1: {restorePl1}W");
            _logger.LogInfo($"[PowerLimitManager][{operationId}]   PL2: {restorePl2}W");

            ulong newValue = BuildMsrValue(restorePl1, restorePl2, 56, false);
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Valor calculado para restauração: 0x{newValue:X}");
            
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Escrevendo MSR 0x{MSR_PKG_POWER_LIMIT:X}...");
            bool result = _backend.WriteMsr(MSR_PKG_POWER_LIMIT, newValue);
            
            if (!result)
            {
                _logger.LogError($"[PowerLimitManager][{operationId}] ❌ FALHA: WriteMsr retornou false");
                _logger.LogError($"[PowerLimitManager][{operationId}] Motivo: Driver rejeitou escrita ou CPU bloqueou o MSR.");
                return false;
            }
            _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ WriteMsr executado com sucesso");

            // Restaurar MMIO também (Sync MMIO reverso)
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] INICIANDO MMIO SYNC REVERSO");
            _logger.LogInfo($"[PowerLimitManager][{operationId}] ============================================");
            
            ulong mchBar = GetMchBar();
            if (mchBar > 0)
            {
                ulong mmioAddr = mchBar + 0x59A0;
                _logger.LogInfo($"[PowerLimitManager][{operationId}] MCHBAR disponível: 0x{mchBar:X}");
                _logger.LogInfo($"[PowerLimitManager][{operationId}] Endereço MMIO: 0x{mmioAddr:X}");
                
                if (_backend.WritePhysicalMemory(mmioAddr, newValue, 8))
                {
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ MMIO Sync reverso aplicado");
                    
                    // Verificar escrita
                    if (_backend.ReadPhysicalMemory(mmioAddr, 8, out ulong verifyMmio))
                    {
                        int mmioPl1 = (int)((verifyMmio & 0x7FFF) / 8);
                        int mmioPl2 = (int)(((verifyMmio >> 32) & 0x7FFF) / 8);
                        _logger.LogInfo($"[PowerLimitManager][{operationId}] MMIO confirmado: PL1={mmioPl1}W, PL2={mmioPl2}W");
                    }
                }
                else
                {
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] MMIO Sync reverso falhou");
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] Motivo: Driver nao suporta escrita fisica.");
                }
            }
            else
            {
                _logger.LogInfo($"[PowerLimitManager][{operationId}] MCHBAR não disponível (MMIO sync reverso pulado)");
            }

            // Validação da restauração
            _logger.LogInfo($"[PowerLimitManager][{operationId}] Validando restauração...");
            if (_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong verifyMsr))
            {
                ParseMsr(verifyMsr, out int valPl1, out int valPl2, out _, out bool valLocked);
                _logger.LogInfo($"[PowerLimitManager][{operationId}] MSR após restauração: PL1={valPl1}W, PL2={valPl2}W, Lock={valLocked}");
                
                if (valPl1 == restorePl1 && valPl2 == restorePl2)
                {
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] ✅ LIMITES ORIGINAIS RESTAURADOS COM SUCESSO");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] PL1: {restorePl1}W (Confirmado)");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] PL2: {restorePl2}W (Confirmado)");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] Tempo: {elapsedMs:F0}ms");
                    _logger.LogSuccess($"[PowerLimitManager][{operationId}] ============================================");
                    
                    // Limpar cache após restauração bem-sucedida
                    StopRollbackWatchdog();
                    _consecutiveFailures = 0;
                    
                    // CORREÇÃO CRÍTICA: Resetar TODOS os estados de power para fábrica!
                    // _currentPl1/_currentPl2 = valores que o hardware está agora
                    // _targetMaxPl1/_targetMaxPl2 = valores máximos que o EvaluateThermalScaling tenta atingir
                    // Se _targetMaxPl1 ficar em 62W (gaming), o OnMetricsUpdated vai re-aumentar os PLs!
                    _currentPl1 = restorePl1;
                    _currentPl2 = restorePl2;
                    _targetMaxPl1 = restorePl1;  // CRÍTICO: Impede re-boost pelo thermal scaling
                    _targetMaxPl2 = restorePl2;  // CRÍTICO: Impede re-boost pelo thermal scaling
                    
                    // Manter factory defaults em cache para próxima ativação
                    // NÃO limpar _factoryPl1 e _factoryPl2 - eles persistem em disco
                    
                    _logger.LogInfo($"[PowerLimitManager][{operationId}] Estado interno preservado: PL1={_currentPl1}W, PL2={_currentPl2}W (valores de fábrica)");
                    _logger.LogInfo($"[PowerLimitManager][{operationId}] Target máximo resetado: PL1={_targetMaxPl1}W, PL2={_targetMaxPl2}W (= fábrica)");
                    _logger.LogInfo($"[PowerLimitManager][{operationId}] Factory defaults permanecem: PL1={_factoryPl1}W, PL2={_factoryPl2}W");
                    
                    // Limpar apenas o cache de sessão (original)
                    _originalPl1 = null;
                    _originalPl2 = null;
                    _originalLocked = false;
                    _originalMsrValue = 0;
                    _logger.LogInfo($"[PowerLimitManager][{operationId}] Cache de sessão limpo (factory defaults permanecem em disco).");
                }
                else
                {
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] ⚠️ Restauração parcial ou falhou");
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] Esperado: PL1={restorePl1}W, PL2={restorePl2}W");
                    _logger.LogWarning($"[PowerLimitManager][{operationId}] Confirmado: PL1={valPl1}W, PL2={valPl2}W");
                }
            }
            else
            {
                _logger.LogWarning($"[PowerLimitManager][{operationId}] ⚠️ Não foi possível validar restauração (falha na leitura MSR)");
            }
            
            return result;
        }

        // Watchdog que verifica a cada 30s se o BIOS/EC reverteu os PLs e reaplica se necessário.
        private void StartRollbackWatchdog()
        {
            StopRollbackWatchdog();
            _rollbackWatchdogCts = new CancellationTokenSource();
            _rollbackWatchdogTask = Task.Run(() => RollbackWatchdogLoopAsync(_rollbackWatchdogCts.Token));
            _logger.LogInfo("[PowerLimitManager] 🐕 Watchdog de rollback iniciado (verificação a cada 30s).");
        }

        private void StopRollbackWatchdog()
        {
            if (_rollbackWatchdogCts != null)
            {
                _rollbackWatchdogCts.Cancel();
                _rollbackWatchdogCts.Dispose();
                _rollbackWatchdogCts = null;
            }
            _rollbackWatchdogTask = null;
            _logger.LogDebug("[PowerLimitManager] Watchdog de rollback parado.");
        }

        private async Task RollbackWatchdogLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_watchdogIntervalMs, ct);
                    CheckAndReapplyLimits();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"[PowerLimitManager] Erro no watchdog de rollback: {ex.Message}");
                }
            }
        }

        private void CheckAndReapplyLimits()
        {
            _logger?.LogTrace("[PowerLimitManager] Enter CheckAndReapplyLimits");
            if (_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentValue))
            {
                ParseMsr(currentValue, out int currentPl1, out int currentPl2, out _, out _);

                if (currentPl1 == _currentPl1 && currentPl2 == _currentPl2)
                {
                    _consecutiveFailures = 0;
                    if (_watchdogIntervalMs < 5000 && _mmioSyncAvailable)
                    {
                        _logger.LogDebug($"[PowerLimitManager] Watchdog agressivo: limites estáveis, reaplicando MMIO...");
                        TrySyncMmio();
                    }
                    return;
                }

                _logger.LogWarning($"[PowerLimitManager] 🚨 Watchdog: ROLLBACK DETECTADO! MSR atual PL1={currentPl1}W, PL2={currentPl2}W. Reaplicando PL1={_currentPl1}W, PL2={_currentPl2}W...");

                ulong targetValue = BuildMsrValue(_currentPl1, _currentPl2, 56, false);
                if (_backend.WriteMsr(MSR_PKG_POWER_LIMIT, targetValue))
                {
                    TrySyncMmio();
                    _consecutiveFailures = 0;
                    _logger.LogSuccess("[PowerLimitManager] ✅ Watchdog: Limites reaplicados com sucesso!");
                }
                else
                {
                    _consecutiveFailures++;
                    _logger.LogError($"[PowerLimitManager] ❌ Watchdog: Falha ao reaplicar limites! ({_consecutiveFailures}/{MaxConsecutiveFailures})");

                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _logger.LogError($"[PowerLimitManager] 🚨 AUTO-ROLLBACK: {MaxConsecutiveFailures} falhas consecutivas no watchdog. Restaurando limites originais...");
                        StopRollbackWatchdog();
                        RestoreOriginalLimits();
                    }
                }
            }
            else
            {
                _logger.LogWarning("[PowerLimitManager] Watchdog: Falha ao ler MSR para verificação de rollback.");
            }
        }

        private ulong GetMchBar()
        {
            if (_cachedMchBar != 0)
                return _cachedMchBar;
            if (!_mchBarDiscoveryAttempted)
            {
                _cachedMchBar = _backend.DiscoverMchBar();
                _mchBarDiscoveryAttempted = true;
                _logger.LogInfo($"[PowerLimitManager] MCHBAR descoberto sob demanda: 0x{_cachedMchBar:X}");
            }
            return _cachedMchBar;
        }

        private void TrySyncMmio()
        {
            ulong mchBar = GetMchBar();
            if (mchBar == 0)
            {
                // Rate-limit log de "MCHBAR zero" para a cada 60s
                if ((DateTime.Now - _lastMmioLog).TotalSeconds >= 60)
                {
                    _lastMmioLog = DateTime.Now;
                    _logger.LogDebug("[PowerLimitManager] MMIO Sync ignorado: MCHBAR = 0 (MSR-only mode).");
                }
                return;
            }

            ulong targetValue = BuildMsrValue(_currentPl1, _currentPl2, 56, false);
            ulong mmioAddr = mchBar + 0x59A0;
            if (_backend.WritePhysicalMemory(mmioAddr, targetValue, 8))
            {
                _logger.LogDebug("[PowerLimitManager] MMIO sync aplicado via MCHBAR cacheado.");
            }
            else
            {
                _logger.LogDebug("[PowerLimitManager] MMIO sync falhou — WritePhysicalMemory retornou false.");
            }
        }

        // Event handler for thermal metric updates
        private void OnMetricsUpdated(object? sender, ThermalMetrics metrics)
        {
            _logger?.LogTrace("[PowerLimitManager] Enter OnMetricsUpdated");
            if (metrics == null)
            {
                _logger.LogDebug("[PowerLimitManager] Métricas térmicas recebidas nulas – ignorando.");
                return;
            }
            EvaluateThermalScaling(metrics);
        }

        // Avalia a zona térmica e ajusta os limites de energia conforme a lógica de 4 zonas.
        private void EvaluateThermalScaling(ThermalMetrics metrics)
        {
            _logger?.LogTrace("[PowerLimitManager] Enter EvaluateThermalScaling");
            double cpuTemp = metrics.CpuTemperature;
            bool isEstimated = metrics.IsCpuTemperatureEstimated;
            
            // DIAGNÓSTICO COMPLETO DO ESTADO INTERNO
            _logger.LogInfo($"[PowerLimitManager] ThermalScaling - CPU {cpuTemp}°C (Estimado={isEstimated})");
            _logger.LogInfo($"[PowerLimitManager]   Estado Interno: _currentPl1={_currentPl1}W, _currentPl2={_currentPl2}W, _targetMaxPl1={_targetMaxPl1}W, _targetMaxPl2={_targetMaxPl2}W");
            _logger.LogInfo($"[PowerLimitManager]   Factory: _factoryPl1={_factoryPl1?.ToString() ?? "null"}W, _factoryPl2={_factoryPl2?.ToString() ?? "null"}W");
            
            // Se temperatura for NaN ou estimada, usa 60°C como valor conservador
            if (double.IsNaN(cpuTemp) || cpuTemp <= 0)
            {
                _logger.LogWarning($"[PowerLimitManager] Temperatura CPU inválida (NaN/zero). Assumindo 60°C para scaling conservador. PLs mantidos inalterados.");
                return;
            }

            if (isEstimated)
            {
                _logger.LogInfo("[PowerLimitManager] Temperatura da CPU estimada – usando valor estimado diretamente ({cpuTemp}°C). Zona determinística baseada na carga.");
            }

            const double warningThreshold = 85.0;
            const double criticalThreshold = 95.0;

            string zone;
            if (cpuTemp < 80.0)
            {
                zone = "Verde";
                _logger.LogInfo($"[PowerLimitManager] Zona {zone}: CPU={cpuTemp}°C < 80°C");
                _logger.LogInfo($"[PowerLimitManager]   Condição: _currentPl1 ({_currentPl1}W) < _targetMaxPl1 ({_targetMaxPl1}W) = {_currentPl1 < _targetMaxPl1}");
                _logger.LogInfo($"[PowerLimitManager]   Condição: _currentPl2 ({_currentPl2}W) < _targetMaxPl2 ({_targetMaxPl2}W) = {_currentPl2 < _targetMaxPl2}");
                
                // Green zone – apply target max immediately to prevent stuttering from repeated Ring0 writes
                if (_currentPl1 < _targetMaxPl1 || _currentPl2 < _targetMaxPl2)
                {
                    _logger.LogInfo($"[PowerLimitManager] Zona {zone}: CPU={cpuTemp}°C < 80°C. Aplicando limite máximo PL1={_currentPl1}→{_targetMaxPl1}W, PL2={_currentPl2}→{_targetMaxPl2}W");
                    ApplyLimits(_targetMaxPl1, _targetMaxPl2, "Green zone – incremento de performance");
                }
                else
                {
                    _logger.LogDebug($"[PowerLimitManager] Zona {zone}: limites já no alvo (PL1={_currentPl1}W, PL2={_currentPl2}W). Target: PL1={_targetMaxPl1}W, PL2={_targetMaxPl2}W");
                    _logger.LogInfo($"[PowerLimitManager] Zona {zone}: Nenhum incremento necessário - limites atuais já estão no máximo target");
                }
            }
            else if (cpuTemp >= 80.0 && cpuTemp < warningThreshold)
            {
                zone = "Amarela";
                _logger.LogInfo($"[PowerLimitManager] Zona {zone}: CPU={cpuTemp}°C entre 80-85°C. Limites mantidos (estabilização). PL1={_currentPl1}W, PL2={_currentPl2}W");
            }
            else if (cpuTemp >= warningThreshold && cpuTemp < criticalThreshold)
            {
                zone = "Laranja";
                int newPl1 = Math.Max(_currentPl1 - 3, 0);
                int newPl2 = Math.Max(_currentPl2 - 3, 0);
                _logger.LogWarning($"[PowerLimitManager] Zona {zone}: CPU={cpuTemp}°C entre 85-95°C. Reduzindo PL1={_currentPl1}→{newPl1}W, PL2={_currentPl2}→{newPl2}W");
                ApplyLimits(newPl1, newPl2, "Orange zone – soft throttling");
            }
            else
            {
                zone = "Vermelha";
                int basePl1 = _factoryPl1 ?? _originalPl1 ?? 65;
                int basePl2 = _factoryPl2 ?? _originalPl2 ?? 65;
                int fallbackPl1 = (int)(basePl1 * 0.8);
                int fallbackPl2 = (int)(basePl2 * 0.8);
                _logger.LogError($"[PowerLimitManager] Zona {zone}: CPU={cpuTemp}°C >= 95°C! Hard throttling: PL1→{fallbackPl1}W, PL2→{fallbackPl2}W");
                ApplyLimits(fallbackPl1, fallbackPl2, "Red zone – hard throttling");
            }
        }

        // Apply new limits only if they differ from current, with MMIO sync and logging
        private void ApplyLimits(int pl1, int pl2, string reason)
        {
            _logger?.LogTrace($"[PowerLimitManager] Enter ApplyLimits(pl1={pl1}, pl2={pl2}, reason={reason})");
            
            _logger.LogInfo($"[PowerLimitManager] ApplyLimits - {reason}");
            _logger.LogInfo($"[PowerLimitManager]   Antes: PL1={_currentPl1}W, PL2={_currentPl2}W");
            _logger.LogInfo($"[PowerLimitManager]   Alvo:  PL1={pl1}W, PL2={pl2}W");
            
            if (pl1 == _currentPl1 && pl2 == _currentPl2)
            {
                _logger.LogDebug($"[PowerLimitManager] Limites já são {pl1}W/{pl2}W – nenhuma ação necessária.");
                return;
            }
            
            if (pl1 <= 0 || pl2 <= 0)
            {
                _logger.LogError($"[PowerLimitManager] ⚠️ ALERTA: Tentativa de aplicar PL1={pl1}W, PL2={pl2}W (valores <= 0 são inválidos!)");
            }

            _logger.LogInfo($"[PowerLimitManager] {reason}: Ajustando PL1={pl1}W, PL2={pl2}W");

            ulong newValue = BuildMsrValue(pl1, pl2, 56, false);
            _logger.LogInfo($"[PowerLimitManager] Escrevendo MSR 0x{MSR_PKG_POWER_LIMIT:X} com valor 0x{newValue:X}");
            
            if (!_backend.WriteMsr(MSR_PKG_POWER_LIMIT, newValue))
            {
                _logger.LogError("[PowerLimitManager] Falha ao escrever novos limites via MSR.");
                return;
            }
            
            _logger.LogSuccess($"[PowerLimitManager] ✅ MSR escrito com sucesso");

            TrySyncMmio();

            _currentPl1 = pl1;
            _currentPl2 = pl2;
            _logger.LogDebug($"[PowerLimitManager] Estado interno atualizado: Current PL1={_currentPl1}, PL2={_currentPl2}.");
            _logger.LogSuccess($"[PowerLimitManager] ✅ ApplyLimits concluído: PL1={_currentPl1}W, PL2={_currentPl2}W");
        }

        private void ParseMsr(ulong msrValue, out int pl1, out int pl2, out int timeWindow, out bool isLocked)
        {
            // O bit 63 é o Lock
            isLocked = (msrValue & (1UL << 63)) != 0;

            double unit = GetRaplPowerUnit();

            pl1 = (int)((msrValue & 0x7FFF) * unit); 
            pl2 = (int)(((msrValue >> 32) & 0x7FFF) * unit);

            // Time Window: PL1 bits 17-23. Calculo real é complexo (2^Y * (1.0 + Z/4)). 
            // Para simplificar logs e escrita, apenas extraímos um valor de referência.
            timeWindow = (int)((msrValue >> 17) & 0x7F);
        }

        private ulong BuildMsrValue(int pl1Watts, int pl2Watts, int timeWindow, bool lockMsr)
        {
            double unit = GetRaplPowerUnit();
            ulong pl1Raw = (ulong)(pl1Watts / unit) & 0x7FFF;
            ulong pl2Raw = (ulong)(pl2Watts / unit) & 0x7FFF;

            // Enable bits (15 in relative 32-bit space) e Clamp bits (16 in relative 32-bit space)
            ulong pl1Enable = 1UL << 15;
            ulong pl1Clamp = 1UL << 16;
            
            // O upper (PL2) é shiftado << 32 no final, então as posições devem ser relativas
            // Bit 15 virará 47, e Bit 16 virará 48 após o shift.
            ulong pl2Enable = 1UL << 15;
            ulong pl2Clamp = 1UL << 16;

            ulong timeWindowRaw = (ulong)(timeWindow & 0x7F);

            ulong lockBit = lockMsr ? (1UL << 63) : 0;

            ulong lower = pl1Raw | pl1Enable | pl1Clamp | (timeWindowRaw << 17);
            ulong upper = pl2Raw | pl2Enable | pl2Clamp | (timeWindowRaw << 17);

            return lower | (upper << 32) | lockBit;
        }
    }
}

