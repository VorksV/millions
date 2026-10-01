using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.Power
{
    /// <summary>
    /// Central Optimization Manager — coordinates all system optimizations across modules.
    /// 
    /// Responsibilities:
    ///   1. Hardware detection (CPU, GPU, RAM, platform, Windows version)
    ///   2. Optimization registry with current vs recommended state
    ///   3. Deduplication — checks if Gamer Mode or other modules already applied an optimization
    ///   4. Safe apply with backup/restore
    ///   5. Deep logging for diagnostics
    /// </summary>
    public sealed class OptimizationManager : IDisposable
    {
        private const string TAG = "[OptManager]";

        #region Native API

        // [FIX:UNICA-FONTE] A ESCRITA DE ENERGIA SAIU DAQUI.
        //
        // Este serviço declarava os próprios `PowerWriteACValueIndex` e
        // escrevia por eles, com prioridade `PerformanceTweak` acima do
        // Perfil Inteligente. Era uma quarta via de escrita de energia, e ela
        // não consultava o `PowerWriteGate` — ou seja, podia sobrescrever o
        // PCIe Link State que o perfil acabara de definir, e ainda pedir troca
        // de plano.
        //
        // A escrita agora usa `PowerNativeMethods`, a superfície única de
        // `powrprof.dll` no projeto, onde o portão decide.
        //
        // Permanecem aqui apenas as DECLARAÇÕES DE LEITURA, que não alteram o
        // sistema e ainda são usadas para conferir valores antes de agir.
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr r, out IntPtr guid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr r, ref Guid scheme, ref Guid sub, ref Guid setting, out uint val);

        // PCIe Link State Power Management subgroup + setting
        private static Guid GUID_PCIE_SUBGROUP = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        private static Guid GUID_PCIE_LINK_STATE = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

        private static Guid GUID_PROC = new("54533251-82be-4824-96c1-47b60b740d00");
        private static Guid GUID_MIN_STATE = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        private static Guid GUID_MAX_STATE = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        private static Guid GUID_CORE_PARK_MIN = new("0cc5b647-c1df-4637-891a-dec35c318583");
        private static Guid GUID_CORE_PARK_MAX = new("ea062031-0e34-4ff1-9b6d-eb1059334028");
        private static Guid GUID_BOOST = new("45bcc044-d885-43e2-8605-ee0ec6e96b59");
        private static Guid GUID_EPP = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        private static Guid GUID_IDLE_OFF = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");
        private static Guid GUID_HWP = new("8baa4d57-3d56-4ad3-91f6-d4f1952f3c75");

        #endregion

        #region Models

        public enum OptStatus { NotApplied, Applied, Skipped, Failed, AlreadyOptimal }

        public class OptimizationEntry
        {
            public string Name { get; set; } = "";
            public string Module { get; set; } = "";
            public string Category { get; set; } = "";  // CPU, GPU, Latency, System
            public uint CurrentValue { get; set; }
            public uint RecommendedValue { get; set; }
            public OptStatus Status { get; set; } = OptStatus.NotApplied;
            public string StatusDetail { get; set; } = "";
            public bool IsAlreadyOptimal => CurrentValue == RecommendedValue;
        }

        public class HardwareInfo
        {
            public string CpuName { get; set; } = "Unknown";
            public string CpuVendor { get; set; } = "Unknown";
            public int CoreCount { get; set; }
            public int ThreadCount { get; set; }
            public string GpuName { get; set; } = "Unknown";
            public string GpuVendor { get; set; } = "Unknown";
            public int RamGb { get; set; }
            public bool IsLaptop { get; set; }
            public string WindowsVersion { get; set; } = "";
            public string ActivePowerPlan { get; set; } = "";
        }

        public class ApplyResult
        {
            public int Total { get; set; }
            public int Applied { get; set; }
            public int Skipped { get; set; }
            public int AlreadyOptimal { get; set; }
            public int Failed { get; set; }
            public long ElapsedMs { get; set; }
            public List<OptimizationEntry> Entries { get; set; } = new();
        }

        #endregion

        #region Fields

        private readonly ILoggingService _logger;
        private readonly SettingsService _settings;
        private readonly IGlobalThermalMonitorService _thermal;
        private readonly Gamer.Interfaces.IGamerModeOrchestrator? _gamerOrchestrator;

        // [FIX:UNICA-FONTE] O `_cpuProfileService` foi removido deste
        // gerenciador. Ele era a ponte para uma segunda escrita de energia
        // independente do Perfil Inteligente, e é justamente essa duplicação
        // que produzia planos com valores conflitantes.

        private HardwareInfo? _hardware;
        private readonly List<OptimizationEntry> _registry = new();
        private readonly Dictionary<string, uint> _backup = new();
        private readonly Dictionary<string, int> _registryBackup = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _disposed;

        #endregion

        public OptimizationManager(
            ILoggingService logger,
            SettingsService settings,
            IGlobalThermalMonitorService thermal,
            Gamer.Interfaces.IGamerModeOrchestrator? gamerOrchestrator = null)
        {
            _logger = logger;
            _settings = settings;
            _thermal = thermal;
            _gamerOrchestrator = gamerOrchestrator;
            _logger.LogInfo($"{TAG} Inicializado.");
        }

        #region Public API

        /// <summary>
        /// Main entry point — detect hardware, build registry, check conflicts, apply.
        /// </summary>
        public async Task<ApplyResult> ApplyIntelligentOptimizationsAsync(
            IntelligentProfileType? profileOverride = null,
            Action<int>? progress = null,
            CancellationToken ct = default)
        {
            if (!await _lock.WaitAsync(5000, ct))
            {
                _logger.LogWarning($"{TAG} Aplicação já em andamento.");
                return new ApplyResult();
            }

            var sw = Stopwatch.StartNew();
            var result = new ApplyResult();

            try
            {
                var profile = profileOverride ?? _settings.Settings.IntelligentProfile;
                _logger.LogInfo($"{TAG} ═══════════════════════════════════════════════════════");
                _logger.LogInfo($"{TAG} APLICANDO OTIMIZAÇÕES INTELIGENTES — Perfil: {profile}");
                _logger.LogInfo($"{TAG} ═══════════════════════════════════════════════════════");

                // 1. Detect hardware
                progress?.Invoke(5);
                await DetectHardwareAsync();
                progress?.Invoke(15);

                // 2. Check thermal safety
                if (!CheckThermalSafety(profile))
                {
                    _logger.LogWarning($"{TAG} Temperatura crítica — abortando otimizações agressivas.");
                    result.Entries = _registry.ToList();
                    return result;
                }
                progress?.Invoke(20);

                // 3. Build optimization registry (current vs recommended)
                _registry.Clear();
                BuildGpuOptimizationRegistry(profile);
                BuildLatencyOptimizationRegistry(profile);
                BuildPcieLinkStateRegistry(profile);
                progress?.Invoke(40);

                // 4. Check Gamer Mode conflicts
                CheckGamerModeConflicts();
                progress?.Invoke(50);

                // 5. Backup current state
                BackupCurrentState();
                progress?.Invoke(55);

                // 6. CPU / ENERGIA — [FIX:UNICA-FONTE] DELEGADO AO PERFIL INTELIGENTE
                //
                // A segunda via de escrita de energia foi removida daqui. Este
                // passo chamava o `o servico legado`, que gravava
                // EPP, min/max state e core parking no esquema ATIVO.
                //
                // Com ele fora, sobra uma única fonte de verdade para a energia:
                // o `ProfilePowerCoordinator`, que aplica a tabela
                // `ProfilePowerMatrix` para o perfil e o tier de hardware
                // detectados. Esta chamada abaixo é o que garante que o perfil
                // escolhido pelo usuário seja o mesmo que chegou ao plano.
                VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.ApplyNow(
                    profile, _logger);

                _logger.LogInfo(
                    $"{TAG} Energia aplicada pelo Perfil Inteligente (fonte única). " +
                    "Este gerenciador não grava mais EPP, plano ou core parking.");
                progress?.Invoke(65);

                // 7. Apply GPU + Latency + PCIe optimizations
                await Task.Run(() => ApplyRegistryOptimizations(), ct);
                progress?.Invoke(90);

                // 8. Compile results
                result.Total = _registry.Count;
                result.Applied = _registry.Count(e => e.Status == OptStatus.Applied);
                result.Skipped = _registry.Count(e => e.Status == OptStatus.Skipped);
                result.AlreadyOptimal = _registry.Count(e => e.Status == OptStatus.AlreadyOptimal);
                result.Failed = _registry.Count(e => e.Status == OptStatus.Failed);
                result.Entries = _registry.ToList();

                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;

                LogResults(result, profile);
                progress?.Invoke(100);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao aplicar otimizações: {ex.Message}", ex);
            }
            finally
            {
                _lock.Release();
            }

            return result;
        }

        /// <summary>
        /// Restore all optimizations to their backed-up state.
        /// </summary>
        public async Task RestoreAllAsync()
        {
            if (!await _lock.WaitAsync(5000))
            {
                _logger.LogWarning($"{TAG} Restauração já em andamento.");
                return;
            }

            try
            {
                _logger.LogInfo($"{TAG} ═══ RESTAURANDO ESTADO ORIGINAL ═══");

                // [FIX:UNICA-FONTE] A restauração de energia não acontece aqui.
                //
                // O código original delegava ao `o servico legado`
                // e devolvia o EPP/min/max ao valor anterior. Isso é errado no
                // desenho novo: "valor anterior" aqui significa o valor que o
                // PERFIL gravou, não um estado neutro. Ao reverter, este serviço
                // desfazia o Perfil Inteligente em vez de repará-lo.
                //
                // Quem restaura é o `ProfilePowerCoordinator.RestoreOriginal`, e
                // ele devolve a máquina ao plano que estava ativo antes da
                // aplicação do perfil — que é o estado que o usuário reconhece
                // como "como estava".
                VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.RestoreOriginal(_logger);

                // Restore GPU/Latency registry values
                await Task.Run(() => RestoreRegistryBackup());

                _logger.LogSuccess($"{TAG} ✓ Estado original restaurado.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao restaurar: {ex.Message}", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Get detected hardware info (cached after first detection).
        /// </summary>
        public HardwareInfo? Hardware => _hardware;

        /// <summary>
        /// Get the current optimization registry snapshot.
        /// </summary>
        public IReadOnlyList<OptimizationEntry> GetRegistry() => _registry.AsReadOnly();

        #endregion

        #region Hardware Detection

        private async Task DetectHardwareAsync()
        {
            if (_hardware != null) return; // cached

            _hardware = new HardwareInfo();
            await Task.Run(() =>
            {
                try
                {
                    // CPU
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            _hardware.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                            _hardware.CpuVendor = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                            _hardware.CoreCount = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                            _hardware.ThreadCount = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                            break;
                        }
                    }

                    // GPU
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController WHERE Availability = 3"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            var name = obj["Name"]?.ToString() ?? "";
                            _hardware.GpuName = name;
                            _hardware.GpuVendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                                : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                                : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                                : "Unknown";
                            break;
                        }
                    }

                    // RAM
                    using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            var bytes = Convert.ToInt64(obj["TotalPhysicalMemory"] ?? 0);
                            _hardware.RamGb = (int)(bytes / (1024L * 1024 * 1024));
                            break;
                        }
                    }

                    // Laptop detection
                    using (var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            if (obj["ChassisTypes"] is ushort[] types)
                            {
                                // Types 8,9,10,11,12,14,18,21,31,32 = portable/laptop
                                var laptopTypes = new ushort[] { 8, 9, 10, 11, 12, 14, 18, 21, 31, 32 };
                                _hardware.IsLaptop = types.Any(t => laptopTypes.Contains(t));
                            }
                            break;
                        }
                    }

                    // Windows version
                    _hardware.WindowsVersion = Environment.OSVersion.Version.ToString();

                    // Active power plan
                    try
                    {
                        PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePtr);
                        var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                        _hardware.ActivePowerPlan = scheme.ToString();
                    }
                    catch { }

                    LogHardware();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} Erro na detecção de hardware: {ex.Message}", ex);
                }
            });
        }

        private void LogHardware()
        {
            if (_hardware == null) return;
            _logger.LogInfo($"{TAG} ───── Hardware Detectado ─────");
            _logger.LogInfo($"{TAG}   CPU: {_hardware.CpuName} ({_hardware.CpuVendor})");
            _logger.LogInfo($"{TAG}   Cores: {_hardware.CoreCount} / Threads: {_hardware.ThreadCount}");
            _logger.LogInfo($"{TAG}   GPU: {_hardware.GpuName} ({_hardware.GpuVendor})");
            _logger.LogInfo($"{TAG}   RAM: {_hardware.RamGb} GB");
            _logger.LogInfo($"{TAG}   Plataforma: {(_hardware.IsLaptop ? "Laptop" : "Desktop")}");
            _logger.LogInfo($"{TAG}   Windows: {_hardware.WindowsVersion}");
            _logger.LogInfo($"{TAG} ──────────────────────────────");
        }

        #endregion

        #region Optimization Registry Builders

        private void BuildGpuOptimizationRegistry(IntelligentProfileType profile)
        {
            // HAGS (Hardware Accelerated GPU Scheduling)
            // FOI REMOVIDO: Forçar HAGS (HwSchMode = 2) via registro sem verificar suporte da GPU/Driver 
            // causa travamento total (DWM fallback) em sistemas incompatíveis após o reinício.

            // Game Mode (Windows Game Mode)
            // Registry: HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled
            try
            {
                uint currentGameMode = 1;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
                {
                    if (key?.GetValue("AutoGameModeEnabled") is int v) currentGameMode = (uint)v;
                }

                uint recommendedGameMode = profile switch
                {
                    IntelligentProfileType.GamerCompetitive => 1,
                    IntelligentProfileType.GamerSinglePlayer => 1,
                    IntelligentProfileType.EnterpriseSecure => 0,
                    _ => 1
                };

                _registry.Add(new OptimizationEntry
                {
                    Name = "Windows Game Mode",
                    Module = "OptimizationManager",
                    Category = "GPU",
                    CurrentValue = currentGameMode,
                    RecommendedValue = recommendedGameMode
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler Game Mode: {ex.Message}");
            }
        }

        private void BuildLatencyOptimizationRegistry(IntelligentProfileType profile)
        {
            // SystemResponsiveness — 0 = max gaming priority, 20 = default
            try
            {
                uint currentResp = 20;
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    if (key?.GetValue("SystemResponsiveness") is int v) currentResp = (uint)v;
                }

                uint recommendedResp = profile switch
                {
                    IntelligentProfileType.GamerCompetitive => 5,
                    IntelligentProfileType.GamerSinglePlayer => 10,
                    IntelligentProfileType.CreativeVideoEditing => 10,
                    IntelligentProfileType.DeveloperProgramming => 20,
                    _ => 20
                };

                _registry.Add(new OptimizationEntry
                {
                    Name = "System Responsiveness (MMCSS)",
                    Module = "OptimizationManager",
                    Category = "Latency",
                    CurrentValue = currentResp,
                    RecommendedValue = recommendedResp
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler SystemResponsiveness: {ex.Message}");
            }

            // NetworkThrottlingIndex — 0xFFFFFFFF = disabled (max throughput)
            try
            {
                uint currentNti = 10; // default
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    var val = key?.GetValue("NetworkThrottlingIndex");
                    if (val is int v) currentNti = (uint)v;
                }

                uint recommendedNti = profile switch
                {
                    IntelligentProfileType.GamerCompetitive => 0xFFFFFFFF,
                    IntelligentProfileType.GamerSinglePlayer => 0xFFFFFFFF,
                    _ => 10
                };

                _registry.Add(new OptimizationEntry
                {
                    Name = "Network Throttling Index",
                    Module = "OptimizationManager",
                    Category = "Latency",
                    CurrentValue = currentNti,
                    RecommendedValue = recommendedNti
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler NetworkThrottlingIndex: {ex.Message}");
            }

            // GPU Priority for Games task
            try
            {
                uint currentGpuPri = 8;
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    if (key?.GetValue("GPU Priority") is int v) currentGpuPri = (uint)v;
                }

                uint recommendedGpuPri = profile switch
                {
                    IntelligentProfileType.GamerCompetitive => 8,
                    IntelligentProfileType.GamerSinglePlayer => 8,
                    IntelligentProfileType.CreativeVideoEditing => 8,
                    _ => 2
                };

                _registry.Add(new OptimizationEntry
                {
                    Name = "GPU Priority (Games Task)",
                    Module = "OptimizationManager",
                    Category = "Latency",
                    CurrentValue = currentGpuPri,
                    RecommendedValue = recommendedGpuPri
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler GPU Priority: {ex.Message}");
            }

            // Scheduling Category for Games task
            try
            {
                uint currentPri = 2;
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    if (key?.GetValue("Priority") is int v) currentPri = (uint)v;
                }

                uint recommendedPri = profile switch
                {
                    IntelligentProfileType.GamerCompetitive => 6,
                    IntelligentProfileType.GamerSinglePlayer => 6,
                    _ => 2
                };

                _registry.Add(new OptimizationEntry
                {
                    Name = "Games Task Priority",
                    Module = "OptimizationManager",
                    Category = "Latency",
                    CurrentValue = currentPri,
                    RecommendedValue = recommendedPri
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler Games Task Priority: {ex.Message}");
            }
        }

        private void BuildPcieLinkStateRegistry(IntelligentProfileType profile)
        {
            // PCIe Link State Power Management via powrprof
            // 0 = Off (max performance), 1 = Moderate, 2 = Maximum power saving
            try
            {
                uint currentPcie = 1;
                PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePtr);
                var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref GUID_PCIE_SUBGROUP, ref GUID_PCIE_LINK_STATE, out currentPcie);

                uint recommendedPcie = profile switch
                {
                    // Desktop: PCIe Off (0) para máxima performance
                    // Notebook: PCIe Moderate (1) para evitar calor extra no barramento
                    IntelligentProfileType.GamerCompetitive => (uint)(_hardware.IsLaptop ? 1 : 0),
                    IntelligentProfileType.GamerSinglePlayer => (uint)(_hardware.IsLaptop ? 1 : 0),
                    IntelligentProfileType.CreativeVideoEditing => (uint)(_hardware.IsLaptop ? 1 : 0),
                    IntelligentProfileType.DeveloperProgramming => 1,
                    IntelligentProfileType.WorkOffice => 1,
                    IntelligentProfileType.GeneralBalanced => 1,
                    IntelligentProfileType.EnterpriseSecure => 2,
                    _ => 1
                };

                _logger.LogInfo($"{TAG} PCIe LSPM: current={currentPcie}, recommended={recommendedPcie} (IsLaptop={_hardware.IsLaptop})");

                _registry.Add(new OptimizationEntry
                {
                    Name = "PCIe Link State Power Management",
                    Module = "OptimizationManager",
                    Category = "System",
                    CurrentValue = currentPcie,
                    RecommendedValue = recommendedPcie
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao ler PCIe Link State: {ex.Message}");
            }
        }

        #endregion

        #region Gamer Mode Conflict Check

        private void CheckGamerModeConflicts()
        {
            bool gamerActive = _gamerOrchestrator?.IsActive ?? false;
            if (!gamerActive)
            {
                _logger.LogInfo($"{TAG} Modo Gamer inativo — sem conflitos.");
                return;
            }

            _logger.LogInfo($"{TAG} Modo Gamer ATIVO — verificando conflitos...");

            // Gamer Mode already manages: HAGS, SystemResponsiveness, NetworkThrottling, GPU Priority
            var gamerManagedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "HAGS (GPU Scheduling)",
                "System Responsiveness (MMCSS)",
                "Network Throttling Index",
                "GPU Priority (Games Task)",
                "Games Task Priority"
            };

            foreach (var entry in _registry)
            {
                if (gamerManagedNames.Contains(entry.Name))
                {
                    entry.Status = OptStatus.Skipped;
                    entry.StatusDetail = "Gerenciado pelo Modo Gamer";
                    _logger.LogInfo($"{TAG}   SKIP: {entry.Name} — já gerenciado pelo Modo Gamer");
                }
            }
        }

        #endregion

        #region Thermal Safety

        private bool CheckThermalSafety(IntelligentProfileType profile)
        {
            try
            {
                var metrics = _thermal.CurrentMetrics;
                if (metrics == null || !metrics.IsValid)
                {
                    _logger.LogInfo($"{TAG} Métricas térmicas indisponíveis — prosseguindo.");
                    return true;
                }

                var thresholds = ThermalThresholds.GetForProfile(profile);

                // Temperatura estimada (sensor indisponível) não deve bloquear otimizações —
                // a estimativa é baseada em carga de CPU e é imprecisa no boot.
                // Apenas leituras REAIS de sensor justificam abortar otimizações.
                if (metrics.IsCpuTemperatureEstimated)
                {
                    _logger.LogInfo($"{TAG} Temperatura ESTIMADA ({metrics.CpuTemperature:F1}°C) — sensor real indisponível. Prosseguindo com otimizações.");
                    return true;
                }

                if (metrics.CpuTemperature > thresholds.CpuCriticalThreshold)
                {
                    _logger.LogWarning($"{TAG} ⚠ CPU={metrics.CpuTemperature:F1}°C > Crítico={thresholds.CpuCriticalThreshold}°C");
                    return false;
                }

                if (metrics.GpuTemperature > thresholds.GpuCriticalThreshold)
                {
                    _logger.LogWarning($"{TAG} ⚠ GPU={metrics.GpuTemperature:F1}°C > Crítico={thresholds.GpuCriticalThreshold}°C");
                    return false;
                }

                _logger.LogInfo($"{TAG} Térmico OK — CPU={metrics.CpuTemperature:F1}°C, GPU={metrics.GpuTemperature:F1}°C");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao verificar térmico: {ex.Message}");
                return true; // Proceed if thermal check fails
            }
        }

        #endregion

        #region Apply & Restore

        private void BackupCurrentState()
        {
            _backup.Clear();
            _registryBackup.Clear();

            foreach (var entry in _registry)
            {
                _backup[entry.Name] = entry.CurrentValue;
            }

            // Backup registry values for restore
            BackupRegistryValue(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", Registry.LocalMachine);
            BackupRegistryValue(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", Registry.CurrentUser);
            BackupRegistryValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", Registry.LocalMachine);
            BackupRegistryValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", Registry.LocalMachine);
            BackupRegistryValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", Registry.LocalMachine);
            BackupRegistryValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", Registry.LocalMachine);

            _logger.LogInfo($"{TAG} Backup criado: {_backup.Count} otimizações, {_registryBackup.Count} chaves de registro.");
        }

        private void BackupRegistryValue(string path, string valueName, RegistryKey root)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                var val = key?.GetValue(valueName);
                if (val is int v)
                    _registryBackup[$"{path}\\{valueName}"] = v;
            }
            catch { }
        }

        private void ApplyRegistryOptimizations()
        {
            foreach (var entry in _registry)
            {
                if (entry.Status == OptStatus.Skipped) continue;

                if (entry.IsAlreadyOptimal)
                {
                    entry.Status = OptStatus.AlreadyOptimal;
                    entry.StatusDetail = $"Já otimizado ({entry.CurrentValue})";
                    _logger.LogInfo($"{TAG}   ✓ {entry.Name}: já otimizado ({entry.CurrentValue})");
                    continue;
                }

                try
                {
                    bool ok = entry.Name switch
                    {
                        "HAGS (GPU Scheduling)" => ApplyRegistryDword(
                            Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                            "HwSchMode", (int)entry.RecommendedValue),

                        "Windows Game Mode" => ApplyRegistryDword(
                            Registry.CurrentUser, @"Software\Microsoft\GameBar",
                            "AutoGameModeEnabled", (int)entry.RecommendedValue),

                        "System Responsiveness (MMCSS)" => ApplyRegistryDword(
                            Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                            "SystemResponsiveness", (int)entry.RecommendedValue),

                        "Network Throttling Index" => ApplyRegistryDword(
                            Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                            "NetworkThrottlingIndex", unchecked((int)entry.RecommendedValue)),

                        "GPU Priority (Games Task)" => ApplyRegistryDword(
                            Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                            "GPU Priority", (int)entry.RecommendedValue),

                        "Games Task Priority" => ApplyRegistryDword(
                            Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                            "Priority", (int)entry.RecommendedValue),

                        "PCIe Link State Power Management" => ApplyPcieLinkState(entry.RecommendedValue),

                        _ => false
                    };

                    entry.Status = ok ? OptStatus.Applied : OptStatus.Failed;
                    entry.StatusDetail = ok ? $"{entry.CurrentValue} → {entry.RecommendedValue}" : "Falha ao aplicar";

                    if (ok)
                        _logger.LogInfo($"{TAG}   ✓ {entry.Name}: {entry.CurrentValue} → {entry.RecommendedValue}");
                    else
                        _logger.LogWarning($"{TAG}   ✗ {entry.Name}: falha ao aplicar");
                }
                catch (Exception ex)
                {
                    entry.Status = OptStatus.Failed;
                    entry.StatusDetail = ex.Message;
                    _logger.LogError($"{TAG}   ✗ {entry.Name}: {ex.Message}");
                }
            }
        }

        private bool ApplyRegistryDword(RegistryKey root, string path, string valueName, int value)
        {
            try
            {
                using var key = root.OpenSubKey(path, writable: true)
                    ?? root.CreateSubKey(path);
                if (key == null) return false;
                key.SetValue(valueName, value, RegistryValueKind.DWord);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning($"{TAG} Sem permissão para escrever: {path}\\{valueName}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao escrever registro {path}\\{valueName}: {ex.Message}");
                return false;
            }
        }

        private bool ApplyPcieLinkState(uint value)
        {
            try
            {
                var resGet = PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePtr);
                if (resGet != 0 || schemePtr == IntPtr.Zero)
                {
                    _logger.LogWarning($"{TAG} Não foi possível obter o plano ativo para PCIe Link State (código {resGet}).");
                    return false;
                }

                Guid scheme;
                try
                {
                    scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                }
                finally
                {
                    if (schemePtr != IntPtr.Zero)
                        VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(schemePtr);
                }

                // [FIX P1] PCIe Link State só é gerenciado em planos Voltris (fail-closed).
                // Em plano Windows padrão, isso NÃO é falha — é inaplicável por design.
                if (!VoltrisOptimizer.Utils.Win32.PowerNativeMethods.IsVoltrisPlan(scheme))
                {
                    _logger.LogInfo($"{TAG} PCIe Link State ignorado: plano de energia ativo não é Voltris (fail-closed).");
                    return true;
                }

                // [FIX:UNICO-DONO-DE-ENERGIA] O PCIe Link State NÃO é gravado
                // aqui.
                //
                // Este era o ÚNICO ponto de escrita que sobrava dentro de
                // Services\Power, e ele é a peça que fecha o conjunto: os outros
                // arquivos auditados gravavam por fora do portão, e este gravava
                // por DENTRO dele. Passar pelo portão não transformava a escrita
                // em legítima — apenas a tornava mais difícil de enxergar numa
                // auditoria, porque o nome `OptimizationManager` e o diretório
                // `Services\Power` faziam o código parecer parte do sistema
                // canônico.
                //
                // O PCIe Link State é um dos valores da tabela do Perfil, com AC
                // e DC separados, e é um dos quatro que a REGRA 11 do self-test
                // proíbe baixar abaixo do padrão do Windows. Gravá-lo aqui
                // significava que a escolha dependia da ordem de execução de
                // dois serviços.
                //
                // Note que o próprio método já antecipa o problema: a linha
                // acima faz "fail-closed" quando o plano ativo não é o do
                // Voltris. A logica estava certa para o caso errado, e permissiva
                // justamente no caso em que o plano É o do Voltris — onde
                // sobrescrever o perfil causa dano real.
                _logger.LogInfo(
                    $"{TAG} PCIe Link State NÃO gravado (valor pedido {value}). " +
                    "PCIe Link State é valor da tabela do Perfil Inteligente, com AC e DC " +
                    "separados; gravar aqui criava uma segunda via de escrita. " +
                    "Solicitando reaplicação do perfil.");

                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "OptimizationManager.PcieLinkState",
                    $"pedido de PCIe Link State {value}",
                    _logger);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao aplicar PCIe Link State: {ex.Message}");
                return false;
            }
        }

        private void RestoreRegistryBackup()
        {
            foreach (var kvp in _registryBackup)
            {
                try
                {
                    var parts = kvp.Key.Split('\\');
                    var valueName = parts[^1];
                    var path = string.Join('\\', parts[..^1]);

                    // Determine root key from path
                    RegistryKey root;
                    if (path.StartsWith("Software\\Microsoft\\GameBar", StringComparison.OrdinalIgnoreCase))
                        root = Registry.CurrentUser;
                    else
                        root = Registry.LocalMachine;

                    using var key = root.OpenSubKey(path, writable: true);
                    key?.SetValue(valueName, kvp.Value, RegistryValueKind.DWord);
                    _logger.LogInfo($"{TAG}   Restaurado: {valueName} = {kvp.Value}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{TAG}   Falha ao restaurar {kvp.Key}: {ex.Message}");
                }
            }

            // Restore PCIe Link State from backup
            if (_backup.TryGetValue("PCIe Link State Power Management", out uint pcieVal))
            {
                ApplyPcieLinkState(pcieVal);
                _logger.LogInfo($"{TAG}   Restaurado: PCIe Link State = {pcieVal}");
            }
        }

        #endregion

        #region Logging

        private void LogResults(ApplyResult result, IntelligentProfileType profile)
        {
            _logger.LogInfo($"{TAG} ═══════════════════════════════════════════════════════");
            _logger.LogInfo($"{TAG} RESULTADO — Perfil: {profile}");
            _logger.LogInfo($"{TAG}   Total: {result.Total}");
            _logger.LogInfo($"{TAG}   Aplicadas: {result.Applied}");
            _logger.LogInfo($"{TAG}   Já otimizadas: {result.AlreadyOptimal}");
            _logger.LogInfo($"{TAG}   Ignoradas (conflito): {result.Skipped}");
            _logger.LogInfo($"{TAG}   Falhas: {result.Failed}");
            _logger.LogInfo($"{TAG}   Tempo: {result.ElapsedMs}ms");
            _logger.LogInfo($"{TAG} ═══════════════════════════════════════════════════════");

            foreach (var entry in result.Entries)
            {
                var icon = entry.Status switch
                {
                    OptStatus.Applied => "✓",
                    OptStatus.AlreadyOptimal => "≡",
                    OptStatus.Skipped => "⊘",
                    OptStatus.Failed => "✗",
                    _ => "?"
                };
                _logger.LogInfo($"{TAG}   {icon} [{entry.Category}] {entry.Name}: {entry.StatusDetail}");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.Dispose();
            _logger.LogInfo($"{TAG} Disposed.");
        }

        #endregion
    }
}
