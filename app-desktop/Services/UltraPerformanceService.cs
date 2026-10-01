using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Services.Performance.Models;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// VOLTRIS ULTRA PERFORMANCE - Otimizacoes Revolucionarias
    /// Sistema inteligente de otimizacao que NENHUM outro programa oferece
    /// 100% seguro - nao quebra impressoras, drivers ou funcionalidades essenciais
    /// </summary>
    public class UltraPerformanceService
    {
        private readonly ILoggingService _logger;
        private readonly ISystemChangeTransactionService? _txService;
        private readonly ICapabilityGuard? _capabilityGuard;
        private ISystemTransaction? _currentTx;
        private readonly Dictionary<string, object> _backups = new();
        private PerformanceSystemProfile? _cachedProfile;
        private PerformanceSystemProfile _systemProfile => DetectSystemProfile();
        private readonly bool _isGamingMode;
        private readonly bool _isThermalConstrained;
        private readonly bool _isOnBattery;
        private readonly Lazy<ScheduledTasksOptimizer> _scheduledTasksOptimizer;
        private readonly RegistryBackupManager _backupManager;

        public UltraPerformanceService(ILoggingService logger, ISystemChangeTransactionService? txService = null, ICapabilityGuard? capabilityGuard = null, bool isGamingMode = false, bool isThermalConstrained = false, bool isOnBattery = false)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _txService = txService;
            _capabilityGuard = capabilityGuard;
            _isGamingMode = isGamingMode;
            _isThermalConstrained = isThermalConstrained;
            _isOnBattery = isOnBattery;
            _scheduledTasksOptimizer = new Lazy<ScheduledTasksOptimizer>(() => new ScheduledTasksOptimizer(logger));
            _backupManager = new RegistryBackupManager(logger);
            // O perfil será detectado sob demanda ou via EnsureProfileDetectedAsync
        }

        /// <summary>
        /// Garante que o perfil do sistema foi detectado, executando de forma assíncrona se necessário.
        /// </summary>
        public async Task<PerformanceSystemProfile> EnsureProfileDetectedAsync()
        {
            if (_cachedProfile != null) return _cachedProfile;

            return await Task.Run(() => DetectSystemProfile()).ConfigureAwait(false);
        }
        
        /// <summary>
        /// Aplica SvcHostSplitThresholdInKB baseado na quantidade de RAM do sistema
        /// Deve ser chamado apenas na primeira vez pelo perfil inteligente
        /// </summary>
        public void ApplySvcHostSplitThreshold()
        {
            try
            {
                // Verificar se ja foi aplicado
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control", false);
                if (key?.GetValue("SvcHostSplitThresholdInKB") != null)
                {
                    // Ja foi configurado, nao aplicar novamente
                    return;
                }

                // Calcular valor baseado na RAM
                var ramGB = _systemProfile.TotalRAMGB;
                int thresholdKB;

                if (ramGB <= 4)
                    thresholdKB = 4 * 1024 * 1024; // 4GB
                else if (ramGB <= 8)
                    thresholdKB = 8 * 1024 * 1024; // 8GB
                else if (ramGB <= 16)
                    thresholdKB = 16 * 1024 * 1024; // 16GB
                else if (ramGB <= 32)
                    thresholdKB = 32 * 1024 * 1024; // 32GB
                else if (ramGB <= 48)
                    thresholdKB = 48 * 1024 * 1024; // 48GB
                else if (ramGB <= 64)
                    thresholdKB = 64 * 1024 * 1024; // 64GB
                else
                    thresholdKB = 128 * 1024 * 1024; // 128GB+

                using var writeKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control", true);
                if (writeKey != null)
                {
                    BackupValue(writeKey, "SvcHostSplitThresholdInKB");
                    writeKey.SetValue("SvcHostSplitThresholdInKB", thresholdKB, RegistryValueKind.DWord);
                    _logger.LogInfo($"[UltraPerf] SvcHostSplitThresholdInKB configurado para {ramGB:F0}GB RAM: {thresholdKB / (1024 * 1024)}GB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Erro ao aplicar SvcHostSplitThreshold: {ex.Message}");
            }
        }

        #region System Profile Detection

        /// <summary>
        /// Detecta automaticamente o perfil do sistema para otimizacoes inteligentes
        /// </summary>
        public PerformanceSystemProfile DetectSystemProfile()
        {
            if (_cachedProfile != null) return _cachedProfile;

            var profile = new PerformanceSystemProfile();
            try
            {
                // Detectar tipo de armazenamento
                DetectStorageDetails(profile);

                // Detectar RAM
                profile.TotalRAMGB = GetTotalRAMGB();
                profile.RAMManufacturer = GetRAMManufacturer();
                profile.IsLowRAM = profile.TotalRAMGB < 8;
                profile.IsHighRAM = profile.TotalRAMGB >= 16;

                // Detectar CPU
                profile.CPUCores = Environment.ProcessorCount;
                profile.IsMultiCore = profile.CPUCores >= 4;
                profile.CPUName = GetCPUName();

                // Detectar GPU
                profile.HasDedicatedGPU = DetectDedicatedGPU();
                profile.GPUName = GetGPUName();

                // Detectar tipo de dispositivo
                profile.IsLaptop = WindowsCompatibilityHelper.IsLaptop();

                // Detectar versão do Windows
                profile.WindowsVersion = Environment.OSVersion.Version;
                profile.IsWindows11 = WindowsCompatibilityHelper.IsWindows11();

                // Classificar tipo de PC
                profile.IsGamingPC = DetectGamingPC(profile);
                profile.IsWorkstation = DetectWorkstation(profile);

                _cachedProfile = profile;
            }
            catch (Exception ex)
            {
                _logger.LogError("[UltraPerf] Erro ao detectar perfil do sistema", ex);
            }

            return _cachedProfile ?? profile;
        }

        private void DetectStorageDetails(PerformanceSystemProfile profile)
        {
            try
            {
                // 1. Identificar qual o PhysicalDisk que contém a partição do Windows (C:)
                string systemDriveLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
                uint bootDiskNumber = 0;
                bool foundBootDiskNumber = false;

                try
                {
                    // Relacionar C: -> Partição -> Disco Físico
                    using var partitionSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDriveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
                    
                    foreach (ManagementObject partition in partitionSearcher.Get())
                    {
                using var __dispose_partition = partition;
                        using var diskSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
                        
                        foreach (ManagementObject diskDrive in diskSearcher.Get())
                        {
                using var __dispose_diskDrive = diskDrive;
                            var indexStr = diskDrive["Index"]?.ToString();
                            if (uint.TryParse(indexStr, out uint idx))
                            {
                                bootDiskNumber = idx;
                                foundBootDiskNumber = true;
                                break;
                            }
                        }
                        if (foundBootDiskNumber) break;
                    }
                }
                catch { /* Fallback para pegar o primeiro disk se falhar */ }

                // 2. Tentar via MSFT_PhysicalDisk (Namespace Storage) para detalhes de Bus/Media
                using var searcher = new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk");
                
                var physicalDisks = searcher.Get().Cast<ManagementObject>().ToList();
                ManagementObject? bootDiskObj = null;

                if (foundBootDiskNumber)
                {
                    // O DeviceId no root/Microsoft/Windows/Storage costuma bater com o Index do Win32_DiskDrive
                    bootDiskObj = physicalDisks.FirstOrDefault(d => d["DeviceId"]?.ToString() == bootDiskNumber.ToString());
                }

                // Se não achou pelo DeviceId ou falhou a relação, pega o primeiro SSD que encontrar ou simplesmente o primeiro
                bootDiskObj ??= physicalDisks.FirstOrDefault(d => Convert.ToInt32(d["MediaType"]) == 4) ?? physicalDisks.FirstOrDefault();

                if (bootDiskObj != null)
                {
                    var name = bootDiskObj["FriendlyName"]?.ToString() ?? "";
                    var mediaType = Convert.ToInt32(bootDiskObj["MediaType"]);
                    var busType = Convert.ToInt32(bootDiskObj["BusType"]);

                    profile.DiskModel = name;
                    
                    if (busType == 17) // NVMe
                    {
                        profile.DiskType = "SSD NVMe";
                        profile.HasNVMe = true;
                        profile.HasSSD = true;
                    }
                    else if (mediaType == 4) // SSD
                    {
                        profile.DiskType = (busType == 11) ? "SSD SATA III" : "SSD";
                        profile.HasSSD = true;
                    }
                    else if (mediaType == 3) // HDD
                    {
                        profile.DiskType = (busType == 11) ? "HD SATA III" : "HD";
                        profile.HasSSD = false;
                    }
                    else
                    {
                        profile.DiskType = "Disco";
                    }

                    _logger.LogInfo($"[UltraPerf] Disco de Sistema detectado: {name} | Tipo: {profile.DiskType} | Bus: {busType}");
                }

                // Fallback via Win32_DiskDrive se o Storage Namespace estiver vazio/indisponível
                if (string.IsNullOrEmpty(profile.DiskModel))
                {
                    using var driveSearcher = new ManagementObjectSearcher("SELECT Index, Caption, InterfaceType FROM Win32_DiskDrive");
                    var allDrives = driveSearcher.Get().Cast<ManagementObject>().ToList();
                    
                    var targetDrive = foundBootDiskNumber 
                        ? (allDrives.FirstOrDefault(d => d["Index"]?.ToString() == bootDiskNumber.ToString()) ?? allDrives.FirstOrDefault())
                        : allDrives.FirstOrDefault();

                    if (targetDrive != null)
                    {
                        profile.DiskModel = targetDrive["Caption"]?.ToString() ?? "Disco Desconhecido";
                        var interfaceType = targetDrive["InterfaceType"]?.ToString() ?? "";
                        
                        if (interfaceType.Contains("NVMe"))
                        {
                            profile.DiskType = "SSD NVMe";
                            profile.HasNVMe = true;
                            profile.HasSSD = true;
                        }
                        else if (profile.DiskModel.ToLower().Contains("ssd"))
                        {
                            profile.DiskType = interfaceType.Contains("SATA") ? "SSD SATA III" : "SSD";
                            profile.HasSSD = true;
                        }
                        else
                        {
                            profile.DiskType = interfaceType.Contains("SATA") ? "HD SATA III" : "HD";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Erro em DetectStorageDetails: {ex.Message}");
                profile.DiskType = "Disco";
                profile.DiskModel = "Desconhecido";
            }
        }

        private double GetTotalRAMGB()
        {
            try
            {
                return (double)SystemInfoService.GetTotalRAMBytes() / (1024 * 1024 * 1024);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Erro em GetTotalRAMGB: {ex.Message}");
                return 0;
            }
        }

        private string GetRAMManufacturer()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var mfg = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                        return mfg;
                }
            }
            catch { }
            return "";
        }
        
        private string GetCPUName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    return obj["Name"]?.ToString() ?? "Processador Desconhecido";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Erro em GetCPUName: {ex.Message}");
                return "Processador Desconhecido";
            }
            
            return "Processador Desconhecido";
        }

        /// <summary>
        /// Detecta se o sistema possui uma placa de vídeo dedicada real
        /// </summary>
        private bool DetectDedicatedGPU()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, VideoProcessor, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
                
                bool foundDedicated = false;

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString()?.Trim() ?? "";
                    var nameLower = name.ToLower();
                    var processor = obj["VideoProcessor"]?.ToString()?.ToLower() ?? "";
                    var pnpId = obj["PNPDeviceID"]?.ToString()?.ToUpper() ?? "";
                    
                    _logger.LogInfo($"[UltraPerf] Detectando GPU: {name} | Processor: {processor}");

                    // 1. Filtrar Placas Virtuais ou Genéricas
                    if (nameLower.Contains("virtual") || nameLower.Contains("basic display") || nameLower.Contains("warpdrive"))
                        continue;

                    // 2. NVIDIA - Quase sempre dedicada (exceto cloud)
                    if (nameLower.Contains("nvidia") || nameLower.Contains("geforce") || nameLower.Contains("quadro") || nameLower.Contains("tesla"))
                    {
                        _logger.LogInfo($"[UltraPerf] GPU NVIDIA dedicada detectada: {name}");
                        foundDedicated = true;
                        continue;
                    }

                    // 3. AMD - Precisa de filtro rigoroso
                    if (nameLower.Contains("amd") || nameLower.Contains("radeon") || nameLower.Contains("ati"))
                    {
                        // Se tiver RX, XT, Pro, R9, R7 (sem ser integrado), costuma ser dedicada
                        // Mas "Radeon Graphics" sozinho é INTEGRADA
                        bool isIntegrated = false;
                        
                        if (nameLower.Contains("graphics") && !nameLower.Contains("rx")) isIntegrated = true;
                        if (nameLower.Contains("vega") && !nameLower.Contains("56") && !nameLower.Contains("64") && !nameLower.Contains("frontier")) isIntegrated = true;
                        if (nameLower.Contains("integrated") || nameLower.Contains("apus")) isIntegrated = true;
                        if (processor.Contains("internal") || processor.Contains("apu")) isIntegrated = true;

                        if (isIntegrated)
                        {
                            _logger.LogInfo($"[UltraPerf] GPU AMD Integrada detectada (Ignorada como dedicada): {name}");
                            continue;
                        }

                        // Se passou pelo filtro de integradas e tem Radeon, consideramos dedicada
                        _logger.LogInfo($"[UltraPerf] GPU AMD dedicada detectada: {name}");
                        foundDedicated = true;
                        continue;
                    }

                    // 4. Intel - Quase sempre integrada (Arc é a exceção)
                    if (nameLower.Contains("intel"))
                    {
                        // Intel Arc A-Series (A770, A750, etc) são dedicadas
                        // Intel Arc Graphics (nos Core Ultra) são integradas
                        if (nameLower.Contains("arc") && !nameLower.Contains("graphics"))
                        {
                            _logger.LogInfo($"[UltraPerf] GPU Intel Arc dedicada detectada: {name}");
                            foundDedicated = true;
                            continue;
                        }

                        _logger.LogInfo($"[UltraPerf] GPU Intel Integrada detectada (Ignorada como dedicada): {name}");
                        continue;
                    }

                    // 5. Fallback por AdapterRAM (se o WMI reportar corretamente > 2GB de VRAM física)
                    if (obj["AdapterRAM"] != null)
                    {
                        try
                        {
                            long ramBytes = Convert.ToInt64(obj["AdapterRAM"]);
                            if (ramBytes > 2147483648L) // 2GB
                            {
                                _logger.LogInfo($"[UltraPerf] GPU detectada como dedicada via VRAM (>2GB): {name} ({ramBytes / 1024 / 1024}MB)");
                                foundDedicated = true;
                                continue;
                            }
                        }
                        catch { }
                    }
                }
                return foundDedicated;
            }
            catch (Exception ex) 
            { 
                _logger.LogWarning($"[UltraPerf] Erro em DetectDedicatedGPU: {ex.Message}"); 
            }
            
            return false;
        }

        private string GetGPUName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(name) && !name.Contains("Basic"))
                        return name;
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Erro em GetGPUName: {ex.Message}"); }
            
            return "Placa Grafica Desconhecida";
        }

        private bool DetectGamingPC(PerformanceSystemProfile profile)
        {
            // Critérios Rigorosos para PC Gamer:
            // 1. OBRIGATÓRIO ter GPU dedicada
            // 2. RAM >= 16GB
            // 3. CPU >= 6 cores lógicos (evita quad-cores antigos)
            // 4. SSD/NVMe
            return profile.HasDedicatedGPU && 
                   profile.TotalRAMGB >= 15.5 && // 16GB nominais
                   profile.CPUCores >= 6 && 
                   (profile.HasSSD || profile.HasNVMe);
        }

        private bool DetectWorkstation(PerformanceSystemProfile profile)
        {
            // Critérios para Workstation:
            // - Muita RAM (>= 32GB)
            // - Processador forte (>= 12 cores lógicos)
            // - GPU dedicada
            // - NVMe SSD
            return profile.TotalRAMGB >= 31.5 && 
                   profile.CPUCores >= 12 && 
                   profile.HasDedicatedGPU && 
                   profile.HasNVMe;
        }

        #endregion

        #region Hardware Classification

        /// <summary>
        /// Classifica o hardware do sistema em tier (fraco/medio/forte)
        /// </summary>
        public HardwareTier ClassifyHardware()
        {
            return ClassifyHardware(_systemProfile);
        }

        /// <summary>
        /// Classifica hardware baseado no perfil do sistema
        /// </summary>
        public static HardwareTier ClassifyHardware(PerformanceSystemProfile profile)
        {
            // Critérios para PC Fraco (LowEnd)
            // - RAM <= 4GB OU
            // - CPU <= 2 cores OU
            // - Sem GPU dedicada E RAM <= 8GB
            if (profile.TotalRAMGB <= 4.5 ||
                profile.CPUCores <= 2 ||
                (!profile.HasDedicatedGPU && profile.TotalRAMGB <= 8.5))
            {
                return HardwareTier.LowEnd;
            }

            // Critérios para PC Forte (HighEnd)
            // - RAM > 12GB E
            // - CPU > 6 cores E
            // - GPU dedicada E
            // - NVMe SSD
            if (profile.TotalRAMGB > 12 &&
                profile.CPUCores > 6 &&
                profile.HasDedicatedGPU &&
                profile.HasNVMe)
            {
                return HardwareTier.HighEnd;
            }

            // Caso contrário, é PC médio
            return HardwareTier.MidRange;
        }

        #endregion

        #region Optimization Categories

        /// <summary>
        /// Obtem todas as categorias de otimizacao disponiveis
        /// </summary>
        public IReadOnlyList<PerformanceCategory> GetOptimizationCategories()
        {
            var categories = new List<PerformanceCategory>();
            var tier = ClassifyHardware();

            // Categoria: Otimização de Memória
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização de Memória",
                Icon = "🧠",
                Description = "Melhora o gerenciamento de memória do sistema",
                Optimizations = GetMemoryOptimizations(tier)
            });

            // Categoria: Otimização de CPU
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização de CPU",
                Icon = "⚡",
                Description = "Maximiza o desempenho do processador",
                Optimizations = GetCpuOptimizations(tier)
            });

            // Categoria: Otimização de Disco
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização de Disco",
                Icon = "💾",
                Description = "Acelera operações de leitura e escrita",
                Optimizations = GetDiskOptimizations(tier)
            });

            // Categoria: Otimização de Rede
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização de Rede",
                Icon = "🌐",
                Description = "Melhora a velocidade e estabilidade da conexão",
                Optimizations = GetNetworkOptimizations(tier)
            });

            // Categoria: Otimização Visual
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização Visual",
                Icon = "🎨",
                Description = "Equilibra desempenho e qualidade visual",
                Optimizations = GetVisualOptimizations(tier)
            });

            // Categoria: Otimização de Inicialização
            categories.Add(new PerformanceCategory
            {
                Name = "Otimização de Inicialização",
                Icon = "🚀",
                Description = "Reduz tempo de boot e programas na inicialização",
                Optimizations = GetStartupOptimizations(tier)
            });

            // Categoria: Essential Tweaks
            categories.Add(new PerformanceCategory
            {
                Name = "Essential Tweaks",
                Icon = "🔧",
                Description = "Configurações fundamentais do sistema",
                Optimizations = GetEssentialTweaks(tier)
            });

            // Categoria: Otimizações de Responsividade do Sistema
            categories.Add(new PerformanceCategory
            {
                Name = "Responsividade do Sistema",
                Icon = "🖱️",
                Description = "Otimizações de latência, resposta de UI e redução de overhead",
                Optimizations = GetSystemResponsivenessOptimizations(tier)
            });

            // Categoria: Otimização de Navegação e GUI
            categories.Add(new PerformanceCategory
            {
                Name = "Navegação e Interface",
                Icon = "📂",
                Description = "Acelera a navegação no Explorer e limpa rastros do sistema",
                Optimizations = GetNavigationOptimizations(tier)
            });

            return categories;
        }

        private List<PerformanceOptimization> GetMemoryOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Otimização de Compressão de Memória
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Compressão de Memória",
                Description = "Habilita compressão de memória para reduzir uso de disco",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = tier >= HardwareTier.MidRange,
                ApplyAction = OptimizeMemoryCompression,
                RevertAction = RevertMemoryCompression,
                RequiredRestart = RestartScope.Explorer,
                MinimumRAMGB = 8,
                IncompatibilityReason = "Requer pelo menos 8GB de RAM"
            });

            // 2. Ajuste de Prioridade de Memória
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Ajuste de Prioridade de Memória",
                Description = "Ajusta prioridade de alocação para aplicações",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = tier <= HardwareTier.LowEnd, // Apenas para PCs com RAM muito limitada
                ApplyAction = AdjustMemoryPriority,
                RevertAction = RevertMemoryPriority,
                RequiredRestart = RestartScope.None

            });

            // 4. Otimização de Superfetch
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Superfetch",
                Description = "Ajusta serviço SysMain para melhor uso de RAM",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = tier >= HardwareTier.MidRange,
                ApplyAction = OptimizeSuperfetch,
                RevertAction = RevertSuperfetch,
                RequiredRestart = RestartScope.PC,
                MinimumRAMGB = 8,
                IncompatibilityReason = "Requer pelo menos 8GB de RAM"
            });

            // 5. Limpar PageFile ao Desligar
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimizar PageFile no Desligamento",
                Description = "Desativa limpeza do arquivo de página ao desligar, reduzindo o tempo de desligamento e evitando escrita desnecessária no SSD",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableClearPageFileAtShutdown,
                RevertAction = EnableClearPageFileAtShutdown,
                RequiredRestart = RestartScope.PC
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetCpuOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Governor Adaptativo de CPU
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Governor Adaptativo de CPU",
                Description = "Ajusta perfil de energia da CPU dinamicamente",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Advanced,
                IsRecommended = tier >= HardwareTier.MidRange && !_systemProfile.IsLaptop,
                ApplyAction = ApplyAdaptiveCpuGovernor,
                RevertAction = RevertAdaptiveCpuGovernor,
                RequiredRestart = RestartScope.PC,
                NotForLaptops = true,
                IncompatibilityReason = "Nao recomendado para laptops"
            });

            // 2. Otimizacao de Threads
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimizacao de Threads",
                Description = "Ajusta agendamento de threads para melhor desempenho",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = _systemProfile.IsMultiCore,
                ApplyAction = OptimizeThreadScheduling,
                RevertAction = RevertThreadScheduling,
                RequiredRestart = RestartScope.Explorer,
                MinimumCores = 4,
                IncompatibilityReason = "Requer pelo menos 4 nucleos de CPU"
            });

            // 4. Ajuste de Quantum Length
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Ajuste de Quantum Length",
                Description = "Otimiza tempo de slice de CPU para processos",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = tier >= HardwareTier.HighEnd,
                ApplyAction = AdjustQuantumLength,
                RevertAction = RevertQuantumLength,
                RequiredRestart = RestartScope.Explorer,
                MinimumCores = 6,
                IncompatibilityReason = "Requer pelo menos 6 núcleos de CPU"
            });

            // 5. Prioridade Multimídia & Jogos
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Prioridade Multimídia & Jogos",
                Description = "Ajusta prioridades de GPU e agendamento para máximo desempenho em tempo real",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ApplyGamingPriorities,
                RevertAction = RevertGamingPriorities,
                RequiredRestart = RestartScope.PC
            });

            // 6. Power Throttling & Core Parking
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Power Throttling & Core Parking",
                Description = "Garante que o Windows não limite o clock do processador e evita o estacionamento de núcleos",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.IsGamingPC || _systemProfile.IsWorkstation,
                ApplyAction = ApplyPowerThrottling,
                RevertAction = RevertPowerThrottling,
                RequiredRestart = RestartScope.PC,
                IncompatibilityReason = "Pode aumentar o consumo de bateria em laptops"
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetDiskOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Otimização de Prefetch
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Prefetch",
                Description = "Ajusta cache de prefetch para inicialização mais rápida",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.HasSSD || _systemProfile.HasNVMe,
                ApplyAction = OptimizePrefetch,
                RevertAction = RevertPrefetch,
                RequiredRestart = RestartScope.PC,
                RequiresSSD = true,
                IncompatibilityReason = "Requer SSD/NVMe para máxima eficácia"
            });

            // 2. Desfragmentação Inteligente
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desfragmentação Inteligente",
                Description = "Desfragmenta discos mecânicos quando necessário",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = !_systemProfile.HasSSD && !_systemProfile.HasNVMe,
                ApplyAction = OptimizeDefrag,
                RevertAction = null, // Não precisa reverter
                RequiredRestart = RestartScope.None,
                RequiresSSD = false,
                IncompatibilityReason = "Apenas para discos mecânicos"
            });

            // 3. Otimização de NVMe
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de NVMe",
                Description = "Habilita recursos avançados de drives NVMe",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = _systemProfile.HasNVMe,
                ApplyAction = OptimizeNVMe,
                RevertAction = RevertNVMe,
                RequiredRestart = RestartScope.PC,
                RequiresSSD = true,
                IncompatibilityReason = "Requer NVMe SSD"
            });

            // 4. Ajuste de Latência de Disco
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Ajuste de Latência de Disco",
                Description = "Reduz latência de operações de disco",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = tier >= HardwareTier.MidRange && (_systemProfile.HasSSD || _systemProfile.HasNVMe),
                ApplyAction = AdjustDiskLatency,
                RevertAction = RevertDiskLatency,
                RequiredRestart = RestartScope.Explorer,
                RequiresSSD = true,
                IncompatibilityReason = "Requer SSD/NVMe"
            });

            // 5. Otimização de Write-Cache
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Write-Cache",
                Description = "Ajusta buffer de escrita para melhor performance",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = _systemProfile.HasSSD || _systemProfile.HasNVMe,
                ApplyAction = OptimizeWriteCache,
                RevertAction = RevertWriteCache,
                RequiredRestart = RestartScope.Explorer,
                RequiresSSD = true,
                IncompatibilityReason = "Requer SSD/NVMe"
            });

            // 6. SSD Boost Mode (Réplica SSD Booster)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "SSD Boost Mode (Réplica SSD Booster)",
                Description = "Aplica um conjunto completo de otimizações para SSDs (idêntico ao SSD Booster)",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.HasSSD || _systemProfile.HasNVMe,
                ApplyAction = ApplySsdBoostReplica,
                RevertAction = RevertSsdBoostReplica,
                RequiredRestart = RestartScope.PC,
                RequiresSSD = true,
                IncompatibilityReason = "Requer SSD/NVMe"
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetNetworkOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. TCP/IP Tuning
            optimizations.Add(new PerformanceOptimization
            {
                Name = "TCP/IP Tuning",
                Description = "Otimiza pilha TCP/IP para maior throughput",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = true,
                ApplyAction = OptimizeTcpIp,
                RevertAction = RevertTcpIp,
                RequiredRestart = RestartScope.Explorer
            });

            // 2. Buffer de Rede
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Buffer de Rede",
                Description = "Ajusta buffers de rede para menor latência",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.IsGamingPC,
                ApplyAction = OptimizeNetworkBuffers,
                RevertAction = RevertNetworkBuffers,
                RequiredRestart = RestartScope.Explorer,
                ConflictsWithGamerMode = true,
                IncompatibilityReason = "Conflita com Modo Gamer"
            });

            // 3. Desativação de NetBIOS
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desativação de NetBIOS",
                Description = "Remove protocolo legado para melhor performance",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableNetBios,
                RevertAction = RevertNetBios,
                RequiredRestart = RestartScope.Explorer
            });

            // 4. Otimização de Placa de Rede
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Placa de Rede",
                Description = "Ajusta driver da placa para máxima performance",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Advanced,
                IsRecommended = tier >= HardwareTier.MidRange,
                ApplyAction = OptimizeNetworkAdapter,
                RevertAction = RevertNetworkAdapter,
                RequiredRestart = RestartScope.PC,
                MinimumTier = HardwareTier.MidRange,
                IncompatibilityReason = "Requer hardware médio ou superior"
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetVisualOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Animações do Sistema
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Animações do Sistema",
                Description = "Reduz animações para maior responsividade",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = tier <= HardwareTier.MidRange,
                ApplyAction = ReduceAnimations,
                RevertAction = RevertAnimations,
                RequiredRestart = RestartScope.Explorer
            });

            // 2. Efeitos Visuais
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Efeitos Visuais",
                Description = "Desativa efeitos visuais pesados",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = tier <= HardwareTier.MidRange || _systemProfile.IsLaptop,
                ApplyAction = DisableVisualEffects,
                RevertAction = RevertVisualEffects,
                RequiredRestart = RestartScope.Explorer,
                NotForLaptops = false
            });

            // 3. Transparências
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Transparências",
                Description = "Desativa transparências para economizar recursos",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = tier <= HardwareTier.MidRange || _systemProfile.IsLaptop,
                ApplyAction = DisableTransparency,
                RevertAction = RevertTransparency,
                RequiredRestart = RestartScope.Explorer
            });

            // 4. Aceleração de Hardware
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Aceleração de Hardware",
                Description = "Otimiza aceleração de hardware para aplicações",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = _systemProfile.HasDedicatedGPU,
                ApplyAction = OptimizeHardwareAcceleration,
                RevertAction = RevertHardwareAcceleration,
                RequiredRestart = RestartScope.Explorer,
                RequiresDedicatedGPU = true,
                IncompatibilityReason = "Requer GPU dedicada"
            });

            // 4. Otimização de Janela DirectX
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Janela DirectX",
                Description = "Melhora a performance de jogos rodando em modo janela (SwapEffectUpgrade & VRR Bypass)",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.IsGamingPC,
                ApplyAction = ApplyWindowedOptimization,
                RevertAction = RevertWindowedOptimization,
                RequiredRestart = RestartScope.None
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetStartupOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Timeout de Boot
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Timeout de Boot",
                Description = "Reduz tempo de espera no boot",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ReduceBootTimeout,
                RevertAction = RevertBootTimeout,
                RequiredRestart = RestartScope.PC
            });

            // 2. Programas na Inicialização
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Programas na Inicialização",
                Description = "Desativa programas desnecessários na inicialização",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = OptimizeStartupPrograms,
                RevertAction = RevertStartupPrograms,
                RequiredRestart = RestartScope.Explorer
            });

            // 3. Serviços de Inicialização
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Serviços de Inicialização",
                Description = "Ajusta serviços iniciados com o sistema",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = tier >= HardwareTier.MidRange && WindowsCompatibilityHelper.AllowServiceTweaks(),
                ApplyAction = OptimizeStartupServices,
                RevertAction = RevertStartupServices,
                RequiredRestart = RestartScope.PC,
                MinimumTier = HardwareTier.MidRange,
                IncompatibilityReason = "Requer hardware médio ou superior"
            });

            // 4. Fast Startup
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Fast Startup",
                Description = "Habilita inicialização rápida do Windows",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = EnableFastStartup,
                RevertAction = RevertFastStartup,
                RequiredRestart = RestartScope.PC
            });

            return optimizations;
        }

        private List<PerformanceOptimization> GetEssentialTweaks(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Telemetria
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Telemetria",
                Description = "Reduz coleta de dados do sistema",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = OptimizeTelemetry,
                RevertAction = RevertTelemetry,
                RequiredRestart = RestartScope.Explorer
            });

            // 2. Defender
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Defender",
                Description = "Otimiza scans do Windows Defender",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = OptimizeDefender,
                RevertAction = RevertDefender,
                RequiredRestart = RestartScope.None
            });

            // 3. Updates
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Updates",
                Description = "Ajusta frequência de atualizações",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = !_systemProfile.IsWorkstation,
                ApplyAction = OptimizeUpdates,
                RevertAction = RevertUpdates,
                RequiredRestart = RestartScope.PC
            });

            // 4. Power Plan
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Power Plan",
                Description = "Aplica plano de energia otimizado",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = !_systemProfile.IsLaptop,
                ApplyAction = ApplyUltimatePerformancePlan,
                RevertAction = RevertUltimatePerformancePlan,
                RequiredRestart = RestartScope.None,
                NotForLaptops = true,
                IncompatibilityReason = "Não recomendado para laptops"
            });

            // 5. Ajustes de Sistema Avançados
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Ajustes de Sistema Avançados",
                Description = "Conjunto de ajustes para privacidade, segurança e produtividade (Copilot, LongPaths, BitLocker)",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ApplyAdvancedSystemSettings,
                RevertAction = RevertAdvancedSystemSettings,
                RequiredRestart = RestartScope.Explorer
            });

            // 6. Privacidade e Apps Sugeridos
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Privacidade e Apps Sugeridos",
                Description = "Desativa instalação automática de apps (Candy Crush, etc) e reduz carga de processos UWP",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ApplyPrivacyHardening,
                RevertAction = RevertPrivacyHardening,
                RequiredRestart = RestartScope.Explorer
            });

            // 7. Refinamentos de Produtividade
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Refinamentos de Produtividade",
                Description = "Exibe caminhos completos e mantém impressora padrão fixa (Ideal para empresas)",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.IsWorkstation,
                ApplyAction = ApplySystemUIRefinement,
                RevertAction = RevertSystemUIRefinement,
                RequiredRestart = RestartScope.Explorer
            });

            // 8. Desativar Tarefas Agendadas (Telemetria/Diagnóstico)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desativar Tarefas Agendadas",
                Description = "Desativa tarefas de telemetria, diagnóstico, coleta de dados e CEIP do Windows e Office",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableScheduledTasks,
                RevertAction = RestoreScheduledTasks,
                RequiredRestart = RestartScope.None
            });

            // 9. Otimização de Desligamento do Sistema
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimização de Desligamento",
                Description = "Reduz timeouts de serviços e aplicativos para desligamento mais rápido (WaitToKillServiceTimeout)",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ApplyShutdownOptimization,
                RevertAction = RevertShutdownOptimization,
                RequiredRestart = RestartScope.PC
            });

            return optimizations;
        }

        /// <summary>
        /// Otimizações de responsividade do sistema inspiradas no RyTuneX.
        /// Foco em redução de latência de UI, overhead de processos em segundo plano e crash dumps.
        /// </summary>
        private List<PerformanceOptimization> GetSystemResponsivenessOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Desabilitar atraso de exibição do menu
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Atraso de Exibição do Menu",
                Description = "Remove o atraso antes dos menus aparecerem, tornando a navegação instantânea",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableMenuShowDelay,
                RevertAction = RevertMenuShowDelay,
                RequiredRestart = RestartScope.Explorer
            });

            // 2. Desabilitar tempo de foco do mouse
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Tempo de Foco do Mouse",
                Description = "Reduz o tempo que o sistema espera antes de disparar um evento de foco, acelerando interações",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableMouseHoverTime,
                RevertAction = RevertMouseHoverTime,
                RequiredRestart = RestartScope.Explorer
            });

            // 3. Desabilitar aplicativos em segundo plano
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Apps em Segundo Plano",
                Description = "Impede que aplicativos UWP/Store executem em segundo plano, liberando CPU e RAM",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableBackgroundApps,
                RevertAction = RevertBackgroundApps,
                RequiredRestart = RestartScope.Explorer
            });

            // 4. Desabilitar Crash Dump
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Crash Dump",
                Description = "Desativa a geração de dumps de memória em falhas, economizando espaço em disco e I/O",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = tier >= HardwareTier.MidRange,
                ApplyAction = DisableCrashDump,
                RevertAction = RevertCrashDump,
                RequiredRestart = RestartScope.PC
            });

            // 5. Desabilitar preenchimento automático
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Preenchimento Automático",
                Description = "Desativa sugestões automáticas em campos de pesquisa e formulários, reduzindo distrações e uso de CPU",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = false,
                ApplyAction = DisableAutoSuggest,
                RevertAction = RevertAutoSuggest,
                RequiredRestart = RestartScope.Explorer
            });

            // 6. Desabilitar Nagle's Algorithm (TCP No Delay)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Nagle's Algorithm",
                Description = "Reduz latência de rede desabilitando o agrupamento de pacotes TCP pequenos",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Moderate,
                IsRecommended = _systemProfile.IsGamingPC,
                ApplyAction = DisableNaglesAlgorithm,
                RevertAction = RevertNaglesAlgorithm,
                RequiredRestart = RestartScope.PC,
                IncompatibilityReason = "Recomendado principalmente para jogos online"
            });

            // 7. Desabilitar Delivery Optimization (P2P de Updates)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Delivery Optimization",
                Description = "Impede que o Windows use sua conexão para distribuir atualizações para outros PCs via P2P",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableDeliveryOptimization,
                RevertAction = RevertDeliveryOptimization,
                RequiredRestart = RestartScope.None
            });

            // 8. Otimizar SvcHost Split Threshold
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Otimizar SvcHost Split Threshold",
                Description = "Agrupa serviços do Windows em menos processos svchost, reduzindo uso de RAM",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = _systemProfile.TotalRAMGB >= 8,
                ApplyAction = ApplySvcHostOptimization,
                RevertAction = RevertSvcHostOptimization,
                RequiredRestart = RestartScope.PC,
                MinimumRAMGB = 8,
                IncompatibilityReason = "Requer pelo menos 8GB de RAM"
            });

            // 9. Desabilitar Cortana
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar Cortana",
                Description = "Desativa a Cortana para liberar recursos de CPU e memória em segundo plano",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableCortana,
                RevertAction = RevertCortana,
                RequiredRestart = RestartScope.Explorer
            });

            // 10. Desabilitar GameDVR e Game Bar
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desabilitar GameDVR e Game Bar",
                Description = "Desativa a gravação em segundo plano e a Game Bar do Xbox, liberando GPU e CPU",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableGameDVR,
                RevertAction = RevertGameDVR,
                RequiredRestart = RestartScope.Explorer
            });

            return optimizations;
        }

        /// <summary>
        /// Otimizações de navegação no Windows Explorer e limpeza de rastros.
        /// </summary>
        private List<PerformanceOptimization> GetNavigationOptimizations(HardwareTier tier)
        {
            var optimizations = new List<PerformanceOptimization>();

            // 1. Desativar Miniaturas (Ícones Apenas)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desativar Miniaturas",
                Description = "Força o Explorer a exibir apenas ícones em vez de miniaturas, reduzindo drasticamente o uso de CPU e I/O de disco",
                Impact = OptimizationImpact.High,
                Safety = OptimizationSafety.Safe,
                IsRecommended = false, // OTIMIZAÇÃO: Desativado por padrão pois prejudica a experiência visual do Explorer
                ApplyAction = DisableThumbnails,
                RevertAction = RevertThumbnails,
                RequiredRestart = RestartScope.Explorer

            });

            // 2. Desativar Busca Automática de Rede
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desativar Busca de Rede",
                Description = "Impede que o Explorer procure automaticamente por pastas e impressoras na rede, evitando travamentos ao abrir 'Este Computador'",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableNetCrawling,
                RevertAction = RevertNetCrawling,
                RequiredRestart = RestartScope.Explorer
            });

            // 3. Limpar Arquivos Recentes
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Limpar Arquivos Recentes",
                Description = "Remove o histórico de arquivos e pastas acessados recentemente para tornar o Acesso Rápido mais leve",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = ClearRecentFiles,
                RevertAction = null, // Operação de limpeza única
                RequiredRestart = RestartScope.Explorer
            });

            // 4. Desativar Histórico de Arquivos
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Desativar Histórico de Arquivos",
                Description = "Desativa o rastreamento de documentos abertos recentemente pelo Windows, reduzindo o overhead do Explorer",
                Impact = OptimizationImpact.Low,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = DisableRecentTrack,
                RevertAction = RevertRecentTrack,
                RequiredRestart = RestartScope.Explorer
            });

            // 5. Separar Processos do Explorer (SeparateProcess)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Pastas em Processos Separados",
                Description = "Cada janela do Explorer roda em processo separado, evitando que uma trava derrube todas as pastas abertas",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = EnableSeparateProcess,
                RevertAction = DisableSeparateProcess,
                RequiredRestart = RestartScope.Explorer
            });

            // 6. Reinicialização Automática do Shell (AutoRestartShell)
            optimizations.Add(new PerformanceOptimization
            {
                Name = "Auto Restart do Shell",
                Description = "Garante que o Explorer reinicie automaticamente após uma falha, aumentando a estabilidade do sistema",
                Impact = OptimizationImpact.Medium,
                Safety = OptimizationSafety.Safe,
                IsRecommended = true,
                ApplyAction = EnableAutoRestartShell,
                RevertAction = DisableAutoRestartShell,
                RequiredRestart = RestartScope.PC
            });

            return optimizations;
        }

        #endregion

        #region Memory Optimizations Implementation

        private void OptimizeMemoryCompression()
        {
            try
            {
                // Habilitar compressao de memoria (mais eficiente que paging)
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                if (key != null)
                {
                    BackupValue(key, "DisablePagingCombining");
                    key.SetValue("DisablePagingCombining", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Memory Compression: {ex.Message}"); }
        }

        private void RevertMemoryCompression()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingCombining");
        }

        private void AdjustMemoryPriority()
        {
            try
            {
                // Ajustar prioridade de memoria para aplicacoes
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                if (key != null)
                {
                    BackupValue(key, "Win32PrioritySeparation");
                    // VALOR PROFISSIONAL: 26 (hex) ou 38 (dec) é agressivo. 
                    // Usaremos 2 (hex) ou 2 (dec) para um balanceamento melhor entre foreground e background.
                    key.SetValue("Win32PrioritySeparation", 2, RegistryValueKind.DWord);

                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Memory Priority: {ex.Message}"); }
        }

        private void RevertMemoryPriority()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation");
        }

        internal void OptimizeSuperfetch()
        {
            try
            {
                // Otimizar servico SysMain (Superfetch)
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true);
                if (key != null)
                {
                    BackupValue(key, "Start");
                    key.SetValue("Start", 2, RegistryValueKind.DWord); // Automatic
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Superfetch: {ex.Message}"); }
        }

        internal void DisableSuperfetch()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true);
                if (key != null)
                {
                    BackupValue(key, "Start");
                    key.SetValue("Start", 4, RegistryValueKind.DWord); // Disabled
                    _logger.LogInfo("[UltraPerf] Superfetch desativado");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] DisableSuperfetch: {ex.Message}"); }
        }

        private void RevertSuperfetch()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\SysMain", "Start");
        }

        private void DisableClearPageFileAtShutdown()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                if (key != null)
                {
                    BackupValue(key, "ClearPageFileAtShutdown");
                    key.SetValue("ClearPageFileAtShutdown", 0, RegistryValueKind.DWord);
                    _logger.LogInfo("[UltraPerf] ClearPageFileAtShutdown desativado");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] ClearPageFileAtShutdown: {ex.Message}"); }
        }

        private void EnableClearPageFileAtShutdown()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "ClearPageFileAtShutdown");
        }

        #endregion

        #region CPU Optimizations Implementation

        /// [FIX:UNICO-DONO-DE-ENERGIA] "Governor adaptativo de CPU" não grava mais.
        ///
        /// Este é o ÚNICO lugar do projeto inteiro que usava o GUID de core
        /// parking CORRETO (`…dec35c318583`) e mesmo assim gravava o atributo
        /// errado.
        ///
        /// `SetValue("Attributes", 0)` no core parking significa "deixar o
        /// setting VISÍVEL na interface do Windows". O método se chamava
        /// "ApplyAdaptiveCpuGovernor" e o log diria que um governor estava
        /// sendo aplicado — mas o que ele fazia era tornar o controle de
        /// estacionamento de núcleo visível nas Opções de Energia. Não aplicava
        /// governor nenhum.
        ///
        /// É o caso mais enganoso dos quatro que auditamos, porque a chave estava
        /// certa. Uma chave certa e um atributo errado produzem código que
        /// "funciona" e não faz nada de útil — e que, para quem lê, parece
        /// indeksado corretamente.
        ///
        /// Vale notar que `BackupValue`/`RestoreValue` aqui FAZIAM um par
        /// honesto de backup e restauração, ao contrário do `DeleteValue` do
        /// GpuClockGuardian. Ainda assim a escrita vai embora: o valor do
        /// estacionamento é do Perfil, e mexer no atributo de visibilidade não
        /// é uma forma de mudá-lo.
        private void ApplyAdaptiveCpuGovernor()
        {
            _logger.LogInfo(
                "[UltraPerf] ApplyAdaptiveCpuGovernor NEUTRALIZADO. O método escrevia " +
                "\"Attributes\" = 0 no core parking — ou seja, apenas tornava o " +
                "setting visível nas Opções de Energia. Não aplicava governor nenhum, " +
                "e estacionamento é valor do Perfil Inteligente.");
        }

        /// [FIX:UNICO-DONO-DE-ENERGIA] A reversão vai embora com a escrita.
        /// Ver <see cref="ApplyAdaptiveCpuGovernor"/> para o que o método fazia de
        /// fato. O `RestoreValue` era correto, mas não há o que restaurar.
        private void RevertAdaptiveCpuGovernor()
        {
            _logger.LogDebug(
                "[UltraPerf] Sem o que reverter: a gravação de \"Attributes\" foi neutralizada.");
        }

        private void OptimizeThreadScheduling()
        {
            try
            {
                // Otimizar agendamento de threads
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                if (key != null)
                {
                    // Removido IRQ8Priority: Tweak legado que não tem efeito real em sistemas modernos e pode causar instabilidade de clock.

                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Thread Scheduling: {ex.Message}"); }
        }

        private void RevertThreadScheduling()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "IRQ8Priority");
        }

        private void AdjustQuantumLength()
        {
            try
            {
                // Ajustar quantum length para melhor resposta
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                if (key != null)
                {
                    BackupValue(key, "WeightedBackground");
                    key.SetValue("WeightedBackground", 2, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Quantum Length: {ex.Message}"); }
        }

        private void RevertQuantumLength()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "WeightedBackground");
        }

        #endregion

        #region Disk Optimizations Implementation

        private void OptimizePrefetch()
        {
            try
            {
                // Otimizar prefetch
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", true);
                if (key != null)
                {
                    BackupValue(key, "EnablePrefetcher");
                    key.SetValue("EnablePrefetcher", 3, RegistryValueKind.DWord); // 3 = Application and Boot
                    
                    BackupValue(key, "EnableSuperfetch");
                    key.SetValue("EnableSuperfetch", 3, RegistryValueKind.DWord); // 3 = Application and Boot
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Prefetch: {ex.Message}"); }
        }

        private void RevertPrefetch()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher");
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnableSuperfetch");
        }

        private void OptimizeDefrag()
        {
            try
            {
                // Otimizar defragmentacao
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Dfrg\Statistics", true);
                if (key != null)
                {
                    BackupValue(key, "BootOptimizeFunction");
                    key.SetValue("BootOptimizeFunction", "Y", RegistryValueKind.String);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Defrag: {ex.Message}"); }
        }

        // RevertDefrag nao e necessario pois nao ha mudanca significativa

        private void OptimizeNVMe()
        {
            try
            {
                // Otimizar NVMe
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", true);
                if (key != null)
                {
                    BackupValue(key, "InterruptManagement");
                    key.SetValue("InterruptManagement", 1, RegistryValueKind.DWord);
                    
                    BackupValue(key, "PerformanceMode");
                    key.SetValue("PerformanceMode", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] NVMe: {ex.Message}"); }
        }

        private void RevertNVMe()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "InterruptManagement");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "PerformanceMode");
        }

        private void AdjustDiskLatency()
        {
            try
            {
                // Ajustar latencia de disco
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\disk", true);
                if (key != null)
                {
                    BackupValue(key, "TimeOutValue");
                    key.SetValue("TimeOutValue", 60, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Disk Latency: {ex.Message}"); }
        }

        private void RevertDiskLatency()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\disk", "TimeOutValue");
        }

        private void OptimizeWriteCache()
        {
            try
            {
                // Otimizar write-cache
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Volmgr", true);
                if (key != null)
                {
                    BackupValue(key, "EnableCache");
                    key.SetValue("EnableCache", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Write Cache: {ex.Message}"); }
        }

        private void RevertWriteCache()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Volmgr", "EnableCache");
        }

        private void ApplySsdBoostReplica()
        {
            try
            {
                _logger.LogInfo("[UltraPerf] Aplicando SSD Boost Mode (Réplica SSD Booster)...");

                // 2. Last Access Timestamp (OFF) - Reduces write operations
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "NtfsDisableLastAccessUpdate");
                        key.SetValue("NtfsDisableLastAccessUpdate", 1, RegistryValueKind.DWord);
                    }
                }

                // 3. Boot Files Defragmentation (OFF) - SSDs don't need this
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DfrgBootOptimize, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "Enable");
                        key.SetValue("Enable", "N", RegistryValueKind.String);
                    }
                }

                // 5. Superfetch / SysMain (OFF) - Highly recommended for SSD health
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "Start");
                        key.SetValue("Start", 4, RegistryValueKind.DWord);
                    }
                }

                // 6. 8.3 Name Creation (OFF) - Legacy support not needed for speed
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "NtfsDisable8dot3NameCreation");
                        key.SetValue("NtfsDisable8dot3NameCreation", 1, RegistryValueKind.DWord);
                    }
                }

                // 9. Event Logging (Minimized) - Disabling heavy telemetry logging
                using (var diagkey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", true))
                {
                    if (diagkey != null)
                    {
                        BackupValue(diagkey, "Start");
                        diagkey.SetValue("Start", 4, RegistryValueKind.DWord);
                    }
                }

                // 11. TRIM Support (ON) - CRITICAL for SSD performance
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "DisableDeleteNotify");
                        key.SetValue("DisableDeleteNotify", 0, RegistryValueKind.DWord);
                    }
                }

                // 12. Drive Optimization (Manual) - Prevent automatic heavy defrag
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\defragsvc", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "Start");
                        key.SetValue("Start", 3, RegistryValueKind.DWord); // Manual
                    }
                }
                
                // Disable Scheduled Defrag Task
                Task.Run(() => {
                    try {
                        var psi = new ProcessStartInfo("schtasks", "/change /tn \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /disable") {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        Process.Start(psi)?.WaitForExit(5000);
                    } catch { }
                });

                _logger.LogSuccess("[UltraPerf] SSD Boost Mode (Replica) aplicado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraPerf] Falha ao aplicar SSD Boost Replica: {ex.Message}", ex);
            }
        }

        private void RevertSsdBoostReplica()
        {
            try
            {
                _logger.LogInfo("[UltraPerf] Revertendo SSD Boost Mode (Replica)...");

                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.SessionManagerMemory, "DisablePagingExecutive");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, "NtfsDisableLastAccessUpdate");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DfrgBootOptimize, "Enable");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MemoryPrefetch, "EnablePrefetcher");
                RestoreValue(@"SYSTEM\CurrentControlSet\Services\SysMain", "Start");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, "NtfsDisable8dot3NameCreation");
                RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableThumbnailCache", true);
                RestoreValue(@"SYSTEM\CurrentControlSet\Services\WSearch", "Start");
                RestoreValue(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.PowerControl, "HibernateEnabled");
                RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, "DisableDeleteNotify");
                RestoreValue(@"SYSTEM\CurrentControlSet\Services\defragsvc", "Start");

                // Re-enable Scheduled Defrag Task
                Task.Run(() => {
                    try {
                        var psi = new ProcessStartInfo("schtasks", "/change /tn \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /enable") {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        Process.Start(psi)?.WaitForExit(5000);
                    } catch { }
                });

                _logger.LogSuccess("[UltraPerf] SSD Boost Mode (Replica) revertido com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraPerf] Falha ao reverter SSD Boost Replica: {ex.Message}", ex);
            }
        }

        #endregion

        #region Network Optimizations Implementation

        private void OptimizeTcpIp()
        {
            try
            {
                // Otimizar TCP/IP
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
                if (key != null)
                {
                    BackupValue(key, "DefaultTTL");
                    key.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                    
                    BackupValue(key, "Tcp1323Opts");
                    key.SetValue("Tcp1323Opts", 1, RegistryValueKind.DWord);
                    
                    BackupValue(key, "TcpWindowSize");
                    key.SetValue("TcpWindowSize", 65535, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] TCP/IP: {ex.Message}"); }
        }

        private void RevertTcpIp()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DefaultTTL");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Tcp1323Opts");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpWindowSize");
        }

        private void OptimizeNetworkBuffers()
        {
            try
            {
                // Otimizar buffers de rede
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", true);
                if (key != null)
                {
                    BackupValue(key, "IRPStackSize");
                    key.SetValue("IRPStackSize", 32, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Network Buffers: {ex.Message}"); }
        }

        private void RevertNetworkBuffers()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "IRPStackSize");
        }

        private void DisableNetBios()
        {
            try
            {
                // Desativar NetBIOS
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", true);
                if (key != null)
                {
                    BackupValue(key, "NetbiosOptions");
                    key.SetValue("NetbiosOptions", 2, RegistryValueKind.DWord); // 2 = Disabled
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] NetBIOS: {ex.Message}"); }
        }

        private void RevertNetBios()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "NetbiosOptions");
        }

        private void OptimizeNetworkAdapter()
        {
            try
            {
                // Otimizar adaptador de rede
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
                if (key != null)
                {
                    BackupValue(key, "DisableTaskOffload");
                    key.SetValue("DisableTaskOffload", 0, RegistryValueKind.DWord);
                    
                    BackupValue(key, "EnableTCPChimney");
                    key.SetValue("EnableTCPChimney", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Network Adapter: {ex.Message}"); }
        }

        private void RevertNetworkAdapter()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DisableTaskOffload");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "EnableTCPChimney");
        }

        #endregion

        #region Visual Optimizations Implementation

        private void ReduceAnimations()
        {
            try
            {
                // Reduzir animacoes do sistema
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics", true);
                if (key != null)
                {
                    BackupValue(key, "MinAnimate", true);
                    key.SetValue("MinAnimate", "0", RegistryValueKind.String);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Animations: {ex.Message}"); }
        }

        private void RevertAnimations()
        {
            RestoreValue(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", true);
        }

        internal void DisableVisualEffects()
        {
            try
            {
                // Desativar efeitos visuais
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", true);
                if (key != null)
                {
                    BackupValue(key, "VisualFXSetting");
                    key.SetValue("VisualFXSetting", 3, RegistryValueKind.DWord); // 3 = Custom
                }
                
                using var key2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key2 != null)
                {
                    BackupValue(key2, "ListviewAlphaSelect");
                    key2.SetValue("ListviewAlphaSelect", 0, RegistryValueKind.DWord);
                    
                    BackupValue(key2, "ListviewShadow");
                    key2.SetValue("ListviewShadow", 0, RegistryValueKind.DWord);
                    
                    BackupValue(key2, "TaskbarAnimations");
                    key2.SetValue("TaskbarAnimations", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Visual Effects: {ex.Message}"); }
        }

        private void RevertVisualEffects()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect");
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow");
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations");
        }

        private void DisableTransparency()
        {
            try
            {
                // Desativar transparencias
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
                if (key != null)
                {
                    BackupValue(key, "EnableTransparency");
                    key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Transparency: {ex.Message}"); }
        }

        private void RevertTransparency()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
        }

        private void OptimizeHardwareAcceleration()
        {
            try
            {
                // Otimizar aceleracao de hardware
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Avalon.Graphics", true);
                if (key != null)
                {
                    BackupValue(key, "DisableHWAcceleration");
                    key.SetValue("DisableHWAcceleration", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Hardware Acceleration: {ex.Message}"); }
        }

        private void RevertHardwareAcceleration()
        {
            RestoreValue(@"Software\Microsoft\Avalon.Graphics", "DisableHWAcceleration");
        }

        #endregion

        #region Startup Optimizations Implementation

        private void ReduceBootTimeout()
        {
            // bcdedit removido — boot timeout not modified
            _logger.LogInfo("[UltraPerf] Boot timeout: sem alteração via bcdedit.");
        }

        private void RevertBootTimeout()
        {
            // Nada a reverter
        }

        private void OptimizeStartupPrograms()
        {
            try
            {
                // Desativar programas desnecessarios na inicializacao
                var programsToDisable = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\OneDrive",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Spotify",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Steam",
                    @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run\CCleaner"
                };

                foreach (var program in programsToDisable)
                {
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(program, true);
                        if (key != null)
                        {
                            BackupValue(key, "", true); // Backup do valor inteiro
                            key.DeleteValue("");
                        }
                    }
                    catch { /* Ignorar erros individuais */ }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Startup Programs: {ex.Message}"); }
        }

        private void RevertStartupPrograms()
        {
            // Esta otimizacao nao tem reversao automatica pois depende de programas especificos
        }

        private void OptimizeStartupServices()
        {
            try
            {
                // Otimizar servicos na inicializacao
                var servicesToOptimize = new Dictionary<string, int>
                {
                    { "DiagTrack", 4 }, // Disabled
                    { "dmwappushservice", 4 }, // Disabled
                    { "WerSvc", 3 }, // Manual
                    { "PcaSvc", 3 } // Manual
                };

                foreach (var service in servicesToOptimize)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service.Key}", true);
                        if (key != null)
                        {
                            BackupValue(key, "Start");
                            key.SetValue("Start", service.Value, RegistryValueKind.DWord);
                        }
                    }
                    catch { /* Ignorar erros individuais */ }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Startup Services: {ex.Message}"); }
        }

        private void RevertStartupServices()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\WerSvc", "Start");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\PcaSvc", "Start");
        }

        private void EnableFastStartup()
        {
            try
            {
                // Habilitar Fast Startup
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", true);
                if (key != null)
                {
                    BackupValue(key, "HiberbootEnabled");
                    key.SetValue("HiberbootEnabled", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Fast Startup: {ex.Message}"); }
        }

        private void RevertFastStartup()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");
        }

        #endregion

        #region Advanced System Settings Implementation

        private void ApplyAdvancedSystemSettings()
        {
            try
            {
                // 1. BitLocker Auto-Encryption (OFF) - Prevents device lockout risks
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.BitLocker, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "PreventDeviceEncryption");
                        key.SetValue("PreventDeviceEncryption", 1, RegistryValueKind.DWord);
                    }
                }

                // 2. Windows Copilot (OFF) - Privacy and resources
                using (var key = Registry.LocalMachine.CreateSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.WindowsCopilot, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "TurnOffWindowsCopilot");
                        key.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                    }
                }

                // 3. Device Metadata (OFF) - Stops background lookups
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DeviceMetadata, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "PreventDeviceMetadataFromNetwork");
                        key.SetValue("PreventDeviceMetadataFromNetwork", 1, RegistryValueKind.DWord);
                    }
                }

                // 4. Remote Assistance (OFF) - Security hardening
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.RemoteAssistance, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "fAllowToGetHelp");
                        key.SetValue("fAllowToGetHelp", 0, RegistryValueKind.DWord);
                    }
                }

                // 5. Consumer Features (OFF)
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CloudContent, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "DisableWindowsConsumerFeatures");
                        key.SetValue("DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord);
                        BackupValue(key, "DisableConsumerAccountStateContent");
                        key.SetValue("DisableConsumerAccountStateContent", 1, RegistryValueKind.DWord);
                    }
                }
                
                // 6. Long File Paths (ON) - Practical improvement
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "LongPathsEnabled");
                        key.SetValue("LongPathsEnabled", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Advanced Settings: {ex.Message}"); }
        }

        private void RevertAdvancedSystemSettings()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.BitLocker, "PreventDeviceEncryption");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.WindowsCopilot, "TurnOffWindowsCopilot");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DeviceMetadata, "PreventDeviceMetadataFromNetwork");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.RemoteAssistance, "fAllowToGetHelp");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CloudContent, "DisableWindowsConsumerFeatures");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CloudContent, "DisableConsumerAccountStateContent");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.FileSystem, "LongPathsEnabled");
        }

        private void ApplyGamingPriorities()
        {
            try
            {
                // 1. System Responsiveness & Network Throttling
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaSystemProfile, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "SystemResponsiveness");
                        key.SetValue("SystemResponsiveness", VoltrisOptimizer.Core.Constants.SystemConstants.MultimediaProfile.MaxGamingPriority, RegistryValueKind.DWord);
                        BackupValue(key, "NetworkThrottlingIndex");
                        key.SetValue("NetworkThrottlingIndex", VoltrisOptimizer.Core.Constants.SystemConstants.MultimediaProfile.ThrottlingDisabled, RegistryValueKind.DWord);
                    }
                }

                // 2. Multimedia Tasks - Games
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaGames, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "GPU Priority");
                        key.SetValue("GPU Priority", VoltrisOptimizer.Core.Constants.SystemConstants.MultimediaProfile.GpuPriorityHigh, RegistryValueKind.DWord);
                        BackupValue(key, "Priority");
                        key.SetValue("Priority", VoltrisOptimizer.Core.Constants.SystemConstants.MultimediaProfile.ProcessPriorityHigh, RegistryValueKind.DWord);
                        BackupValue(key, "Scheduling Category");
                        key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Gaming Priorities: {ex.Message}"); }
        }

        private void RevertGamingPriorities()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaSystemProfile, "SystemResponsiveness");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaSystemProfile, "NetworkThrottlingIndex");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaGames, "GPU Priority");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaGames, "Priority");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.MultimediaGames, "Scheduling Category");
        }

        private void ApplyWindowedOptimization()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DirectXUserGpu, true);
                if (key != null)
                {
                    BackupValue(key, "DirectXUserGlobalSettings");
                    key.SetValue("DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;", RegistryValueKind.String);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Windowed Optimization: {ex.Message}"); }
        }

        private void RevertWindowedOptimization()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.DirectXUserGpu, "DirectXUserGlobalSettings", true);
        }

        private void ApplyPrivacyHardening()
        {
            try
            {
                // 1. Disable Content Delivery (Ads/Promotions)
                using (var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.ContentDelivery, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "ContentDeliveryAllowed");
                        key.SetValue("ContentDeliveryAllowed", 0, RegistryValueKind.DWord);
                        BackupValue(key, "OemPreInstalledAppsEnabled");
                        key.SetValue("OemPreInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                        BackupValue(key, "PreInstalledAppsEnabled");
                        key.SetValue("PreInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                        BackupValue(key, "SilentInstalledAppsEnabled");
                        key.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                    }
                }

                // 2. Disable Input Experience Preload
                using (var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.InputAppPreload, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "IsInputAppPreloadEnabled");
                        key.SetValue("IsInputAppPreloadEnabled", 0, RegistryValueKind.DWord);
                    }
                }

                // 3. Disable Prelaunch
                using (var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.Dsh, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "IsPrelaunchEnabled");
                        key.SetValue("IsPrelaunchEnabled", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Privacy Hardening: {ex.Message}"); }
        }

        private void RevertPrivacyHardening()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.ContentDelivery, "ContentDeliveryAllowed", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.ContentDelivery, "OemPreInstalledAppsEnabled", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.ContentDelivery, "PreInstalledAppsEnabled", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.ContentDelivery, "SilentInstalledAppsEnabled", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.InputAppPreload, "IsInputAppPreloadEnabled", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.Dsh, "IsPrelaunchEnabled", true);
        }

        private void ApplySystemUIRefinement()
        {
            try
            {
                // 1. Show Full Path in Title Bar
                using (var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CabinetState, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "FullPath");
                        key.SetValue("FullPath", 1, RegistryValueKind.DWord);
                    }
                }

                // 2. Legacy Printer Mode
                using (var key = Registry.CurrentUser.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.PrinterLegacy, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "LegacyDefaultPrinterMode");
                        key.SetValue("LegacyDefaultPrinterMode", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] UI Refinement: {ex.Message}"); }
        }

        private void RevertSystemUIRefinement()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CabinetState, "FullPath", true);
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.PrinterLegacy, "LegacyDefaultPrinterMode", true);
        }

        private void ApplyPowerThrottling()
        {
            try
            {
                // CORREÇÃO: Apenas em modo Gamer com validação térmica
                if (!_isGamingMode || _isThermalConstrained || _isOnBattery)
                {
                    _logger.LogInfo("[UltraPerf] Power Throttling: Pulando - Sistema em modo normal/bateria/thermal constrained");
                    return;
                }
                
                // CORREÇÃO: Desativação segura com validação
                _logger.LogInfo("[UltraPerf] Aplicando Power Throttling (modo Gamer seguro)...");
                
                // 1. Disable Power Throttling (apenas se seguro)
                using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.PowerThrottling, true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "PowerThrottlingOff");
                        key.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
                        _logger.LogInfo("[UltraPerf] Power Throttling desativado com segurança");
                    }
                }

                // CORREÇÃO: Core Parking apenas para desktops/workstations
                if (!_systemProfile.IsLaptop)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CoreParking, true))
                    {
                        if (key != null)
                        {
                            BackupValue(key, "ValueMax");
                            key.SetValue("ValueMax", 0, RegistryValueKind.DWord);
                            _logger.LogInfo("[UltraPerf] Core Parking desativado (desktop/workstation)");
                        }
                    }
                }
                else
                {
                    _logger.LogInfo("[UltraPerf] Core Parking pulado - sistema é notebook");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Power Throttling Optimization: {ex.Message}"); }
        }

        private void RevertPowerThrottling()
        {
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.PowerThrottling, "PowerThrottlingOff");
            RestoreValue(VoltrisOptimizer.Core.Constants.SystemConstants.RegistryPaths.CoreParking, "ValueMax");
        }

        #endregion

        #region Essential Tweaks Implementation

        private void OptimizeTelemetry()
        {
            try
            {
                // Reduzir telemetria sem desativar completamente
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true);
                if (key != null)
                {
                    BackupValue(key, "AllowTelemetry");
                    key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord); // 0 = Desabilitado (consistente com demais módulos)
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Telemetry: {ex.Message}"); }
        }

        private void RevertTelemetry()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
        }

        private void DisableScheduledTasks()
        {
            try
            {
                _logger.LogInfo("[UltraPerf] Desativando tarefas agendadas de telemetria/diagnóstico...");
                _scheduledTasksOptimizer.Value.DisableAllTasks();
                _logger.LogSuccess("[UltraPerf] Tarefas agendadas desativadas com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraPerf] Erro ao desativar tarefas agendadas: {ex.Message}", ex);
            }
        }

        private void RestoreScheduledTasks()
        {
            try
            {
                _logger.LogInfo("[UltraPerf] Restaurando tarefas agendadas ao estado original...");
                _scheduledTasksOptimizer.Value.RestoreAllTasks();
                _logger.LogSuccess("[UltraPerf] Tarefas agendadas restauradas com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraPerf] Erro ao restaurar tarefas agendadas: {ex.Message}", ex);
            }
        }

        private void OptimizeDefender()
        {
            try
            {
                // Otimizar Windows Defender
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", true);
                if (key != null)
                {
                    BackupValue(key, "DisableRealtimeMonitoring");
                    key.SetValue("DisableRealtimeMonitoring", 0, RegistryValueKind.DWord); // Mantem ativado mas otimizado
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Defender: {ex.Message}"); }
        }

        private void RevertDefender()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring");
        }

        private void ApplyShutdownOptimization()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control", true);
                if (key != null)
                {
                    BackupValue(key, "WaitToKillServiceTimeout");
                    key.SetValue("WaitToKillServiceTimeout", 5000, RegistryValueKind.DWord);
                    _logger.LogInfo("[UltraPerf] WaitToKillServiceTimeout definido para 5000ms");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] WaitToKillServiceTimeout: {ex.Message}"); }
        }

        private void RevertShutdownOptimization()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout");
        }

        private void OptimizeUpdates()
        {
            try
            {
                // Otimizar atualizacoes
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true);
                if (key != null)
                {
                    BackupValue(key, "NoAutoUpdate");
                    key.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord); // Mantem updates mas otimizados
                    
                    BackupValue(key, "AUOptions");
                    key.SetValue("AUOptions", 3, RegistryValueKind.DWord); // Download automatico, notificacao antes de instalar
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Updates: {ex.Message}"); }
        }

        private void RevertUpdates()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions");
        }

        private void ApplyUltimatePerformancePlan()
        {
            try
            {
                // CORREÇÃO: Detecção inteligente de sistema
                if (_systemProfile.IsLaptop || _isOnBattery || _isThermalConstrained)
                {
                    _logger.LogInfo("[UltraPerf] Ultimate Performance: Pulando - Sistema é notebook/bateria/thermal constrained");
                    return;
                }
                
                // CORREÇÃO: Apenas para desktops/workstations
                _logger.LogInfo("[UltraPerf] Aplicando Ultimate Performance Plan (desktop/workstation)...");
                
                // [FIX:UNICO-DONO-DE-ENERGIA] A duplicação de plano foi neutralizada.
                //
                // Este serviço duplicava o plano "Ultimate Performance" e depois
                // delegava ao Perfil. A delegação já estava no lugar certo; a
                // criação é que não podia ficar aqui.
                //
                // Vale registrar a ironia do contexto: o guarda logo acima
                // desiste deste caminho quando a máquina é notebook, está na
                // bateria ou tem restrição térmica — e o comentário diz "Apenas
                // para desktops/workstations". O Perfil Inteligente, este
                // projeto, já resolve desktop E notebook, com tier de capacidade
                // medido. O caminho existia porque o Perfil ainda não era o dono;
                // agora que é, o guarda inteiro deixa de ser necessário, e a
                // criação do plano vai junto.
                _logger.LogInfo("[UltraPerf] Criacao de plano neutralizada " +
                    "(antes: PowerDuplicateScheme do Ultimate Performance). " +
                    "O Perfil Inteligente cobre desktop e notebook, com tier medido.");

                ProfilePowerAuthority.RequestProfileApply(
                    "UltraPerformance.ActivateUltimatePerformance",
                    "otimizacao ultra pede energia",
                    _logger);

                _logger.LogSuccess("[UltraPerf] Perfil Inteligente reaplicado");
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Power Plan: {ex.Message}"); }
        }

        private void RevertUltimatePerformancePlan()
        {
            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] Reverter aqui significava reativar
                // o Balanced nativo do Windows — o plano que desligava o próprio
                // dono do estado equilibrado. O Perfil reasserta o que é dele.
                ProfilePowerAuthority.RequestProfileApply(
                    "UltraPerformance.RevertUltimatePerformance",
                    "revertendo otimizacao ultra",
                    _logger);
            }
            catch { }
        }

        #endregion

        #region System Responsiveness Optimizations Implementation

        private void DisableMenuShowDelay()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                if (key != null)
                {
                    BackupValue(key, "MenuShowDelay", true);
                    key.SetValue("MenuShowDelay", "100", RegistryValueKind.String);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] MenuShowDelay: {ex.Message}"); }
        }

        private void RevertMenuShowDelay()
        {
            RestoreValue(@"Control Panel\Desktop", "MenuShowDelay", true);
        }

        private void DisableMouseHoverTime()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
                if (key != null)
                {
                    BackupValue(key, "MouseHoverTime", true);
                    key.SetValue("MouseHoverTime", "10", RegistryValueKind.String);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] MouseHoverTime: {ex.Message}"); }
        }

        private void RevertMouseHoverTime()
        {
            RestoreValue(@"Control Panel\Mouse", "MouseHoverTime", true);
        }

        private void DisableBackgroundApps()
        {
            try
            {
                // Política global para desabilitar apps em segundo plano
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "LetAppsRunInBackground");
                        key.SetValue("LetAppsRunInBackground", 2, RegistryValueKind.DWord); // 2 = Force Deny
                    }
                }

                // Desabilitar via configuração do usuário
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "GlobalUserDisabled", true);
                        key.SetValue("GlobalUserDisabled", 1, RegistryValueKind.DWord);
                    }
                }

                // Desabilitar Search Background
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "BackgroundAppGlobalToggle", true);
                        key.SetValue("BackgroundAppGlobalToggle", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] BackgroundApps: {ex.Message}"); }
        }

        private void RevertBackgroundApps()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground");
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", true);
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", true);
        }

        private void DisableCrashDump()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl", true);
                if (key != null)
                {
                    BackupValue(key, "CrashDumpEnabled");
                    key.SetValue("CrashDumpEnabled", 0, RegistryValueKind.DWord); // 0 = None

                    BackupValue(key, "LogEvent");
                    key.SetValue("LogEvent", 0, RegistryValueKind.DWord);

                    BackupValue(key, "AutoReboot");
                    key.SetValue("AutoReboot", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] CrashDump: {ex.Message}"); }
        }

        private void RevertCrashDump()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled");
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\CrashControl", "LogEvent");
            RestoreValue(@"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot");
        }

        private void DisableAutoSuggest()
        {
            try
            {
                // Desabilitar AutoSuggest no Explorer
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "AutoSuggest", true);
                        key.SetValue("AutoSuggest", "no", RegistryValueKind.String);
                    }
                }

                // Desabilitar sugestões de pesquisa na barra de endereço
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "Start_SearchFiles", true);
                        key.SetValue("Start_SearchFiles", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] AutoSuggest: {ex.Message}"); }
        }

        private void RevertAutoSuggest()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", "AutoSuggest", true);
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_SearchFiles", true);
        }

        private void DisableNaglesAlgorithm()
        {
            try
            {
                // Desabilitar Nagle's Algorithm para todas as interfaces de rede
                // CORREÇÃO CRÍTICA: TcpAckFrequency NÃO é alterado — forçar ACK imediato
                // causa picos de latência (lag spikes) e perda de pacotes em ISPs residenciais.
                // Mantido apenas TcpNoDelay (seguro).
                using var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
                if (interfacesKey != null)
                {
                    foreach (var subKeyName in interfacesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var ifKey = interfacesKey.OpenSubKey(subKeyName, true);
                            if (ifKey != null)
                            {
                                BackupValue(ifKey, "TCPNoDelay");
                                ifKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                            }
                        }
                        catch { /* Ignorar interfaces individuais */ }
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] NaglesAlgorithm: {ex.Message}"); }
        }

        private void RevertNaglesAlgorithm()
        {
            try
            {
                using var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
                if (interfacesKey != null)
                {
                    foreach (var subKeyName in interfacesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var ifKey = interfacesKey.OpenSubKey(subKeyName, true);
                            if (ifKey != null)
                            {
                                ifKey.DeleteValue("TCPNoDelay", false);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] RevertNagles: {ex.Message}"); }
        }

        private void DisableDeliveryOptimization()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "DODownloadMode");
                        key.SetValue("DODownloadMode", 0, RegistryValueKind.DWord); // 0 = Disabled
                    }
                }

                // Desabilitar serviço de Delivery Optimization
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DoSvc", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "Start");
                        key.SetValue("Start", 4, RegistryValueKind.DWord); // Disabled
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] DeliveryOptimization: {ex.Message}"); }
        }

        private void RevertDeliveryOptimization()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
            RestoreValue(@"SYSTEM\CurrentControlSet\Services\DoSvc", "Start");
        }

        private void ApplySvcHostOptimization()
        {
            try
            {
                // Aumentar threshold para agrupar serviços em menos processos svchost
                var ramKB = (long)(_systemProfile.TotalRAMGB * 1024 * 1024);
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control", true);
                if (key != null)
                {
                    BackupValue(key, "SvcHostSplitThresholdInKB");
                    key.SetValue("SvcHostSplitThresholdInKB", ramKB, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] SvcHostSplit: {ex.Message}"); }
        }

        private void RevertSvcHostOptimization()
        {
            RestoreValue(@"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB");
        }

        private void DisableCortana()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "AllowCortana");
                        key.SetValue("AllowCortana", 0, RegistryValueKind.DWord);
                    }
                }

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "CortanaConsent", true);
                        key.SetValue("CortanaConsent", 0, RegistryValueKind.DWord);

                        BackupValue(key, "BingSearchEnabled", true);
                        key.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Cortana: {ex.Message}"); }
        }

        private void RevertCortana()
        {
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana");
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", true);
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", true);
        }

        private void DisableGameDVR()
        {
            try
            {
                // Desabilitar Game DVR
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "AppCaptureEnabled", true);
                        key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                    }
                }

                // Desabilitar Game Bar
                using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "GameDVR_Enabled", true);
                        key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    }
                }

                // Política de grupo para Game DVR
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true))
                {
                    if (key != null)
                    {
                        BackupValue(key, "AllowGameDVR");
                        key.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] GameDVR: {ex.Message}"); }
        }

        private void RevertGameDVR()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", true);
            RestoreValue(@"System\GameConfigStore", "GameDVR_Enabled", true);
            RestoreValue(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
        }

        #endregion

        #region Navigation and Interface Implementation

        private void DisableThumbnails()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    BackupValue(key, "IconsOnly", true);
                    key.SetValue("IconsOnly", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] Thumbnails: {ex.Message}"); }
        }

        private void RevertThumbnails()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", true);
        }

        private void DisableNetCrawling()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    BackupValue(key, "NoNetCrawling", true);
                    key.SetValue("NoNetCrawling", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] NetCrawling: {ex.Message}"); }
        }

        private void RevertNetCrawling()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NoNetCrawling", true);
        }

        private void ClearRecentFiles()
        {
            try
            {
                // Deletar RecentDocs
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer", true))
                {
                    key?.DeleteSubKeyTree("RecentDocs", false);
                }

                // Deletar BagMRU e Bags (Limpa posições de janelas e histórico de pastas)
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell", true))
                {
                    key?.DeleteSubKeyTree("BagMRU", false);
                    key?.DeleteSubKeyTree("Bags", false);
                }
                
                _logger.LogInfo("[UltraPerf] Arquivos recentes e cache de pastas limpos");
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] ClearRecentFiles: {ex.Message}"); }
        }

        private void DisableRecentTrack()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    BackupValue(key, "Start_TrackDocs", true);
                    key.SetValue("Start_TrackDocs", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] RecentTrack: {ex.Message}"); }
        }

        private void RevertRecentTrack()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", true);
        }

        private void EnableSeparateProcess()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    BackupValue(key, "SeparateProcess", true);
                    key.SetValue("SeparateProcess", 1, RegistryValueKind.DWord);
                    _logger.LogInfo("[UltraPerf] SeparateProcess ativado");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] SeparateProcess: {ex.Message}"); }
        }

        private void DisableSeparateProcess()
        {
            RestoreValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", true);
        }

        private void EnableAutoRestartShell()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
                if (key != null)
                {
                    BackupValue(key, "AutoRestartShell");
                    key.SetValue("AutoRestartShell", 1, RegistryValueKind.DWord);
                    _logger.LogInfo("[UltraPerf] AutoRestartShell ativado");
                }
            }
            catch (Exception ex) { _logger.LogWarning($"[UltraPerf] AutoRestartShell: {ex.Message}"); }
        }

        private void DisableAutoRestartShell()
        {
            RestoreValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoRestartShell");
        }

        #endregion

        #region Apply/Revert Methods

        /// <summary>
        /// Aplica todas as otimizacoes recomendadas para o perfil de hardware atual
        /// </summary>
        public async Task<PerformanceOptimizationResult> ApplyRecommendedOptimizationsAsync(CancellationToken ct = default)
        {
            var result = new PerformanceOptimizationResult
            {
                Success = true,
                TotalApplied = 0
            };
            
            RestartManagerService.Instance.ClearPendingChanges();
            
            try
            {
                _logger.LogInfo("[UltraPerf] Iniciando aplicação de otimizações recomendadas...");
        
                // Criar cancellation token com timeout de 60 segundos para a operação completa
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
        
                // CORREÇÃO CRÍTICA: Executar em background thread para não travar a UI
                await Task.Run(() =>
                {
                    // Iniciar transação
                    _currentTx = _txService?.Begin("UltraPerformance_Optimizations");
        
                    var categories = GetOptimizationCategories();
                    var tier = ClassifyHardware();
        
                    foreach (var category in categories)
                    {
                        cts.Token.ThrowIfCancellationRequested();
        
                        foreach (var optimization in category.Optimizations)
                        {
                            cts.Token.ThrowIfCancellationRequested();
        
                            // Verificar se a otimização é recomendada e compatível
                            if (optimization.IsRecommended && IsOptimizationCompatible(optimization, tier))
                            {
                                try
                                {
                                    _logger.LogInfo($"[UltraPerf] Aplicando: {optimization.Name}");
                                    optimization.ApplyAction?.Invoke();
                                    result.Applied.Add(optimization.Name);
                                    result.TotalApplied++;
                            
                                    RestartManagerService.Instance.RegisterChange(
                                        category.Name,
                                        optimization.Name,
                                        optimization.RequiredRestart == RestartScope.PC ? RestartType.Required :
                                        optimization.RequiredRestart == RestartScope.Explorer ? RestartType.Recommended : RestartType.None,
                                        $"{category.Name}: {optimization.Name}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[UltraPerf] Erro ao aplicar {optimization.Name}: {ex.Message}", ex);
                                    result.Errors.Add($"{optimization.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
        
                    // Commit da transação
                    _currentTx?.Commit();
                    _currentTx = null;
                }, cts.Token).ConfigureAwait(false);
        
                _logger.LogSuccess($"[UltraPerf] Otimizações aplicadas com sucesso: {result.TotalApplied} otimizações");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[UltraPerf] Aplicação de otimizações cancelada pelo usuário");
                result.Success = false;
                result.Errors.Add("Operação cancelada pelo usuário");
                _currentTx?.Rollback();
                _currentTx = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("[UltraPerf] Erro crítico ao aplicar otimizações", ex);
                result.Success = false;
                result.Errors.Add($"Erro crítico: {ex.Message}");
                _currentTx?.Rollback();
                _currentTx = null;
            }
            
            // Nota: O modal de reinício é exibido pelo ViewModel (RestartConfirmationModal),
            // portanto NÃO chamamos CheckAndPromptRestartAsync aqui para evitar modal duplo.
            
            return result;
        }
        
        public async Task<PerformanceOptimizationResult> ApplyOptimizationsAsync(IEnumerable<string> selectedOptimizationNames, CancellationToken ct = default)
        {
            var result = new PerformanceOptimizationResult
            {
                Success = true,
                TotalApplied = 0
            };
        
            var selectedSet = new HashSet<string>(selectedOptimizationNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        
            if (selectedSet.Count == 0)
            {
                _logger.LogInfo("[UltraPerf] Nenhuma otimização selecionada para aplicar.");
                return result;
            }
        
            try
            {
                _logger.LogInfo("[UltraPerf] Iniciando aplicação de otimizações selecionadas...");
        
                // Criar cancellation token com timeout de 60 segundos para a operação completa
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
        
                // CORREÇÃO CRÍTICA: Executar em background thread para não travar a UI
                await Task.Run(() =>
                {
                    // Iniciar transação
                    _currentTx = _txService?.Begin("UltraPerformance_Optimizations_Selected");
        
                    var categories = GetOptimizationCategories();
                    var tier = ClassifyHardware();
        
                    foreach (var category in categories)
                    {
                        cts.Token.ThrowIfCancellationRequested();
        
                        foreach (var optimization in category.Optimizations)
                        {
                            cts.Token.ThrowIfCancellationRequested();
        
                            if (!selectedSet.Contains(optimization.Name))
                                continue;
        
                            if (!IsOptimizationCompatible(optimization, tier))
                                continue;
        
                            try
                            {
                                _logger.LogInfo($"[UltraPerf] Aplicando (selecionada): {optimization.Name}");
                                optimization.ApplyAction?.Invoke();
                                result.Applied.Add(optimization.Name);
                                result.TotalApplied++;
                        
                                RestartManagerService.Instance.RegisterChange(
                                    category.Name,
                                    optimization.Name,
                                    optimization.RequiredRestart == RestartScope.PC ? RestartType.Required :
                                    optimization.RequiredRestart == RestartScope.Explorer ? RestartType.Recommended : RestartType.None,
                                    $"{category.Name}: {optimization.Name}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"[UltraPerf] Erro ao aplicar {optimization.Name}: {ex.Message}", ex);
                                result.Errors.Add($"{optimization.Name}: {ex.Message}");
                            }
                        }
                    }
        
                    // Commit da transação
                    _currentTx?.Commit();
                    _currentTx = null;
                }, cts.Token).ConfigureAwait(false);
        
                _logger.LogSuccess($"[UltraPerf] Otimizações selecionadas aplicadas com sucesso: {result.TotalApplied} otimizações");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[UltraPerf] Aplicação de otimizações selecionadas cancelada pelo usuário");
                result.Success = false;
                result.Errors.Add("Operação cancelada pelo usuário");
                _currentTx?.Rollback();
                _currentTx = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("[UltraPerf] Erro crítico ao aplicar otimizações selecionadas", ex);
                result.Success = false;
                result.Errors.Add($"Erro crítico: {ex.Message}");
                _currentTx?.Rollback();
                _currentTx = null;
            }
            
            // Nota: O modal de reinício é exibido pelo ViewModel (RestartConfirmationModal),
            // portanto NÃO chamamos CheckAndPromptRestartAsync aqui para evitar modal duplo.
            
            return result;
        }

        /// <summary>
        /// Reverte todas as otimizacoes aplicadas
        /// </summary>
        public async Task<PerformanceOptimizationResult> RevertAllOptimizationsAsync(CancellationToken ct = default)
        {
            var result = new PerformanceOptimizationResult
            {
                Success = true,
                TotalApplied = 0
            };
            
            RestartManagerService.Instance.ClearPendingChanges();
            
            try
            {
                _logger.LogInfo("[UltraPerf] Iniciando reversao de todas as otimizacoes...");

                // Criar cancellation token com timeout de 60 segundos para a operacao completa
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));

                // CORREÇÃO CRÍTICA: Executar em background thread para não travar a UI
                await Task.Run(() =>
                {
                    var categories = GetOptimizationCategories();

                    foreach (var category in categories)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        foreach (var optimization in category.Optimizations)
                        {
                            cts.Token.ThrowIfCancellationRequested();

                            try
                            {
                                _logger.LogInfo($"[UltraPerf] Revertendo: {optimization.Name}");
                                optimization.RevertAction?.Invoke();
                                result.Applied.Add(optimization.Name);
                                result.TotalApplied++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"[UltraPerf] Erro ao reverter {optimization.Name}: {ex.Message}", ex);
                                result.Errors.Add($"{optimization.Name}: {ex.Message}");
                            }
                        }
                    }

                    // Limpar backups
                    _backups.Clear();
                }, cts.Token).ConfigureAwait(false);

                _logger.LogSuccess($"[UltraPerf] Reversao concluida: {result.TotalApplied} otimizacoes revertidas");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[UltraPerf] Reversao cancelada pelo usuario");
                result.Success = false;
                result.Errors.Add("Operacao cancelada pelo usuario");
            }
            catch (Exception ex)
            {
                _logger.LogError("[UltraPerf] Erro critico ao reverter otimizacoes", ex);
                result.Success = false;
                result.Errors.Add($"Erro critico: {ex.Message}");
            }
            
            // Nota: O modal de reinício é exibido pelo ViewModel (RestartConfirmationModal),
            // portanto NÃO chamamos CheckAndPromptRestartAsync aqui para evitar modal duplo.
            
            return result;
        }

        /// <summary>
        /// Verifica se uma otimizacao e compativel com o hardware e contexto atual
        /// </summary>
        private bool IsOptimizationCompatible(PerformanceOptimization optimization, HardwareTier tier)
        {
            // Verificar tier minimo
            if (optimization.MinimumTier.HasValue && tier < optimization.MinimumTier.Value)
                return false;

            // Verificar GPU dedicada
            if (optimization.RequiresDedicatedGPU && !_systemProfile.HasDedicatedGPU)
                return false;

            // Verificar SSD
            if (optimization.RequiresSSD && !_systemProfile.HasSSD && !_systemProfile.HasNVMe)
                return false;

            // Verificar RAM minima
            if (optimization.MinimumRAMGB.HasValue && _systemProfile.TotalRAMGB < optimization.MinimumRAMGB.Value)
                return false;

            // Verificar nucleos minimos
            if (optimization.MinimumCores.HasValue && _systemProfile.CPUCores < optimization.MinimumCores.Value)
                return false;

            // Verificar laptops
            if (optimization.NotForLaptops && _systemProfile.IsLaptop)
                return false;

            // Verificar compatibilidade com Modo Gamer
            // (Esta verificacao seria feita no ViewModel, nao aqui)

            return true;
        }

        #endregion

        #region Backup/Restore Methods

        /// <summary>
        /// Faz backup de um valor do registro antes de modifica-lo (PERSISTENTE)
        /// </summary>
        private void BackupValue(RegistryKey key, string valueName, bool isCurrentUser = false)
        {
            var path = key.Name.Substring(key.Name.IndexOf('\\') + 1);
            _backupManager.BackupValue(isCurrentUser ? "HKCU" : "HKLM", path, valueName);
        }

        private void BackupValue(RegistryKey key, string valueName)
        {
            var isCurrentUser = key.Name.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);
            BackupValue(key, valueName, isCurrentUser);
        }

        /// <summary>
        /// Restaura um valor do registro a partir do backup (PERSISTENTE)
        /// </summary>
        private void RestoreValue(string path, string valueName, bool isCurrentUser = false)
        {
            var hive = isCurrentUser ? "HKCU" : "HKLM";
            var oldValue = _backupManager.GetBackupValue(hive, path, valueName);

            if (oldValue != null)
            {
                try
                {
                    var root = isCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = root.OpenSubKey(path, true);
                    if (key != null)
                    {
                        // Validação profissional: Se o backup for uma string vazia e o tipo esperado for diferente,
                        // podemos estar tentando setar algo inválido.
                        if (oldValue is JsonElement element)
                        {
                            // Converter de JSON se necessário
                            oldValue = ConvertJsonElement(element);
                        }

                        key.SetValue(valueName, oldValue);
                        _logger.LogInfo($"[UltraPerf] Restaurado: {hive}\\{path}\\{valueName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[UltraPerf] Erro ao restaurar {valueName}: {ex.Message}");
                }
            }
            else
            {
                // NÃO DELETAR se não houver backup (Segurança Profissional)
                _logger.LogInfo($"[UltraPerf] Reversão ignorada para {valueName} (Nenhum backup original encontrado)");
            }
        }

        private object ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.Number => element.TryGetInt32(out int i) ? (object)i : element.GetInt64(),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => element.GetRawText()
            };
        }

        private void RestoreValue(RegistryKey key, string valueName)
        {
            var isCurrentUser = key.Name.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);
            var path = key.Name.Substring(key.Name.IndexOf('\\') + 1);
            RestoreValue(path, valueName, isCurrentUser);
        }

        #endregion

        #region SSD Optimizations Implementation

        /// <summary>
        /// Força a ativação do TRIM no Windows para manter a performance do SSD
        /// </summary>
        public bool EnforceTrim()
        {
            try
            {
                var psi = new ProcessStartInfo("fsutil", "behavior set DisableDeleteNotify 0")
                {
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                _logger.LogSuccess("[UltraPerf] TRIM ativado com sucesso");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao ativar TRIM: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Desativa a hibernação para economizar espaço e reduzir escritas no SSD
        /// </summary>
        public bool DisableHibernation()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "-h off")
                {
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                _logger.LogSuccess("[UltraPerf] Hibernação desativada (Arquivo hiberfil.sys removido)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar hibernação: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Desativa o Windows Search/Indexing para reduzir overhead de escrita
        /// </summary>
        public bool DisableIndexing()
        {
            try
            {
                using var sc = new ServiceController("WSearch");
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                }

                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch", true);
                if (key != null)
                {
                    BackupValue(key, "Start");
                    key.SetValue("Start", 4, RegistryValueKind.DWord); // Disabled
                }
                _logger.LogSuccess("[UltraPerf] Indexação (Windows Search) desativada");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar Indexação: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Desativa a atualização do carimbo de último acesso aos arquivos
        /// </summary>
        public bool DisableLastAccessUpdate()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem", true);
                if (key != null)
                {
                    BackupValue(key, "NtfsDisableLastAccessUpdate");
                    key.SetValue("NtfsDisableLastAccessUpdate", 1, RegistryValueKind.DWord);
                }
                _logger.LogSuccess("[UltraPerf] Atualização de Último Acesso desativada via Registry");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar LastAccess: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Desativa a criação de nomes curtos (MS-DOS 8.3) para melhor performance do NTFS
        /// </summary>
        public bool Disable83Naming()
        {
            try
            {
                var psi = new ProcessStartInfo("fsutil", "behavior set disable8dot3 1")
                {
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                _logger.LogSuccess("[UltraPerf] Nomes legados MS-DOS 8.3 desativados");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar 8.3 naming: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Impede que o sistema tente desfragmentar arquivos de boot (desnecessário em SSD)
        /// </summary>
        public bool DisableDefragBootFiles()
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Dfrg\BootOptimizeFunction", true);
                if (key != null)
                {
                    BackupValue(key, "Enable");
                    key.SetValue("Enable", "N", RegistryValueKind.String);
                }
                _logger.LogSuccess("[UltraPerf] Defragmentação de boot desativada para SSD");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar defrag boot: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Desativa o Prefetcher do Windows
        /// </summary>
        public bool DisablePrefetcher()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", true);
                if (key != null)
                {
                    BackupValue(key, "EnablePrefetcher");
                    key.SetValue("EnablePrefetcher", 0, RegistryValueKind.DWord);
                }
                _logger.LogSuccess("[UltraPerf] Prefetcher desativado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UltraPerf] Falha ao desativar Prefetcher: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}

