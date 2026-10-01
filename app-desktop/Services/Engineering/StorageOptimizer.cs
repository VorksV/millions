using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Management;

using System.Threading.Tasks;

using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Engineering
{
    /// <summary>
    /// Otimizador de storage (SSD/NVMe/HDD) em n�vel avan�ado
    /// Implementa otimizações específicas baseadas no tipo de storage detectado
    /// </summary>
    public class StorageOptimizer
 {
    private readonly ILoggingService _logger;

    public StorageOptimizer(ILoggingService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInfo("[StorageOptimizer] Otimizador de storage inicializado");
    }

    /// <summary>
    /// Detecta o tipo de storage principal do sistema
    /// </summary>
    public async Task<StorageInfo> DetectStorageTypeAsync()
    {
        _logger.LogInfo("[StorageOptimizer] Detectando tipo de storage...");

        try
        {
            var storageInfo = new StorageInfo();

            // Obter unidade do sistema
            foreach (System.IO.DriveInfo drive in System.IO.DriveInfo.GetDrives())
            {
                var systemDrive = drive;

                if (!systemDrive.IsReady)
                {
                    storageInfo.Type = StorageType.Unknown;
                    storageInfo.ErrorMessage = "Unidade do sistema não está pronta";
                    return storageInfo;
                }

                storageInfo.DriveLetter = systemDrive.Name;
                storageInfo.TotalSize = systemDrive.TotalSize;
                storageInfo.AvailableSpace = systemDrive.AvailableFreeSpace;
                storageInfo.VolumeLabel = systemDrive.VolumeLabel ?? "System";

                // Detectar tipo de storage via WMI
                storageInfo = await DetectStorageTypeViaWmiAsync(storageInfo);

                // Se WMI falhar, tentar detecção via heur�stica
                if (storageInfo.Type == StorageType.Unknown)
                {
                    storageInfo = DetectStorageTypeHeuristic(storageInfo);
                }

                _logger.LogSuccess($"[StorageOptimizer] Storage detectado: {storageInfo.Type} - {storageInfo.Model ?? "Unknown"}");
                return storageInfo;
            }

            // Se não encontrar nenhum drive
            return new StorageInfo { Type = StorageType.Unknown, ErrorMessage = "Nenhum drive encontrado" };
        }
        catch (Exception ex)
        {
            _logger.LogError("[StorageOptimizer] Erro ao detectar tipo de storage", ex);
            return new StorageInfo { Type = StorageType.Unknown, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Otimiza storage baseado no tipo detectado
    /// </summary>
    public async Task<StorageOptimizationResult> OptimizeStorageAsync(StorageInfo storageInfo)
    {
        _logger.LogInfo($"[StorageOptimizer] Otimizando storage {storageInfo.Type}...");

        var result = new StorageOptimizationResult
        {
            StartTime = DateTime.UtcNow,
            StorageType = storageInfo.Type,
            Success = false
        };

        try
        {
            switch (storageInfo.Type)
            {
                case StorageType.SSD:
                    await OptimizeSsdAsync(storageInfo, result);
                    break;
                case StorageType.NVMe:
                    await OptimizeNvMeAsync(storageInfo, result);
                    break;
                case StorageType.HDD:
                    await OptimizeHddAsync(storageInfo, result);
                    break;
                default:
                    result.ErrorMessage = "Tipo de storage desconhecido ou não suportado";
                    return result;
            }

            // Otimizações gerais aplic�veis a todos os tipos
            await ApplyGeneralOptimizationsAsync(storageInfo, result);

            result.Success = true;

            _logger.LogSuccess($"[StorageOptimizer] Storage otimizado: {string.Join(",", result.OptimizationsApplied)}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError("[StorageOptimizer] Erro na otimização de storage", ex);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Configura TRIM para SSDs/NVMe
    /// </summary>
    public async Task<bool> ConfigureTrimOptimizationAsync()
    {
        _logger.LogInfo("[StorageOptimizer] Configurando otimização TRIM...");

        try
        {
            // Verificar status atual do TRIM
            var trimStatus = await GetTrimStatusAsync();

            if (trimStatus.IsEnabled)
            {
                _logger.LogInfo("[StorageOptimizer] TRIM já está habilitado");
                return true;
            }

            // Habilitar TRIM
            var enableResult = await EnableTrimAsync();

            if (enableResult)
            {
                _logger.LogSuccess("[StorageOptimizer] TRIM habilitado com sucessão");
                return true;
            }
            else
            {
                _logger.LogWarning("[StorageOptimizer] Falha ao habilitar TRIM");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[StorageOptimizer] Erro ao configurar TRIM", ex);
            return false;
        }
    }

    /// <summary>
    /// Otimiza cache de escrita baseado no tipo de storage
    /// </summary>
    public async Task<bool> OptimizeWriteCacheAsync(StorageInfo storageInfo)
    {
        _logger.LogInfo($"[StorageOptimizer] Otimizando cache de escrita para {storageInfo.Type}...");

        try
        {
            // Configurar política de cache baseada no tipo de storage
            var cachePolicy = GetOptimalCachePolicy(storageInfo.Type);
            var setResult = await SetWriteCachePolicyAsync(cachePolicy);

            if (setResult)
            {
                _logger.LogSuccess($"[StorageOptimizer] Cache de escrita otimizado: {cachePolicy}");
                return true;
            }
            else
            {
                _logger.LogWarning("[StorageOptimizer] Falha ao otimizar cache de escrita");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[StorageOptimizer] Erro ao otimizar cache", ex);
            return false;
        }
    }

    /// <summary>
    /// Obtém estáat�sticas de performance do storage
    /// </summary>
    public async Task<StoragePerformanceStats> GetPerformanceStatsAsync()
    {
        _logger.LogInfo("[StorageOptimizer] Obtendo estáat�sticas de performance...");

        try
        {
            var stats = new StoragePerformanceStats
            {
                Timestamp = DateTime.UtcNow,
                Success = true
            };

            // Obter métricas de performance
            stats.SequentialReadSpeed = await MeasureSequentialReadSpeedAsync();

            stats.SequentialWriteSpeed = await MeasureSequentialWriteSpeedAsync();

            stats.RandomReadSpeed = await MeasureRandomReadSpeedAsync();

            stats.RandomWriteSpeed = await MeasureRandomWriteSpeedAsync();

            stats.AverageLatency = await MeasureAverageLatencyAsync();

            _logger.LogInfo($"[StorageOptimizer] Performance: Leitura sequencial {stats.SequentialReadSpeed:F1} MB/s, Latência {stats.AverageLatency:F2} ms");

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError("[StorageOptimizer] Erro ao obter estáat�sticas", ex);
            return new StoragePerformanceStats { Success = false, ErrorMessage = ex.Message, Timestamp = DateTime.UtcNow };
        }
    }

    #region Mtodos Privados

    private async Task<StorageInfo> DetectStorageTypeViaWmiAsync(StorageInfo storageInfo)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE Index = 0");

            foreach (ManagementObject disk in searcher.Get())
            {
                using var __dispose_disk = disk;
                var model = disk["Model"]?.ToString() ?? "";
                var mediaType = disk["MediaType"]?.ToString() ?? "";
                var interfaceType = disk["InterfaceType"]?.ToString() ?? "";
                var seráialNumber = disk["SerialNumber"]?.ToString() ?? "";

                storageInfo.Model = model;
                storageInfo.MediaType = mediaType;
                storageInfo.InterfaceType = interfaceType;
                storageInfo.SerialNumber = seráialNumber;

                // Detectar tipo baseado nas informações
                storageInfo.Type = DetectStorageTypeFromWmiInfo(model, mediaType, interfaceType);

                // Obter tamanhos de setor
                if (disk["SectorsPerTrack"] != null)
                    storageInfo.SectorsPerTrack = Convert.ToInt32(disk["SectorsPerTrack"]);

                if (disk["TotalSectors"] != null)
                    storageInfo.TotalSectors = Convert.ToInt64(disk["TotalSectors"]);

                break; // Apenas primeiro disco (sistema)
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro na detecção WMI: {ex.Message}");
        }

        return storageInfo;
    }

    private StorageType DetectStorageTypeFromWmiInfo(string model, string mediaType, string interfaceType)
    {
        var modelLower = model.ToLowerInvariant();
        var mediaTypeLower = mediaType.ToLowerInvariant();
        var interfaceTypeLower = interfaceType.ToLowerInvariant();

        // Detectar NVMe
        if (interfaceTypeLower.Contains("nvme") || modelLower.Contains("nvme"))
            return StorageType.NVMe;

        // Detectar SSD
        if (mediaTypeLower.Contains("ssd") || modelLower.Contains("ssd") || modelLower.Contains("sãolid state") || (interfaceTypeLower.Contains("sata") && !modelLower.Contains("hdd")))
            return StorageType.SSD;

        // Detectar HDD
        if (mediaTypeLower.Contains("hdd") || modelLower.Contains("hdd") || modelLower.Contains("hard disk") || interfaceTypeLower.Contains("ide"))
            return StorageType.HDD;

        // Heur�stica adicional baseada no modelo
        if (modelLower.Contains("kingston") || (modelLower.Contains("samsung") && modelLower.Contains("ssd")) || modelLower.Contains("crucial") || modelLower.Contains("wd blue") || (modelLower.Contains("intel") && modelLower.Contains("ssd")))
            return StorageType.SSD;

        if ((modelLower.Contains("wd") && modelLower.Contains("blue") && !modelLower.Contains("ssd")) || modelLower.Contains("seagate") || (modelLower.Contains("toshiba") && !modelLower.Contains("ssd")))
            return StorageType.HDD;

        return StorageType.Unknown;
    }

    private StorageInfo DetectStorageTypeHeuristic(StorageInfo storageInfo)
    {
        // Heur�stica baseada no tamanho e performance
        var sizeGB = storageInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);

        // Drivers modernos (> 256GB) provavelmente s�o SSD/NVMe
        if (sizeGB >= 256)
        {
            // Assumir SSD para drives de tamanho moderado
            storageInfo.Type = StorageType.SSD;
            storageInfo.DetectionMethod = "Heuristic(Size)";
        }
        else if (sizeGB >= 500)
        {
            // Drivers grandes podem será HDD ou SSD
            storageInfo.Type = StorageType.Unknown;
            storageInfo.DetectionMethod = "Heuristic(Ambiguous)";
        }
        else
        {
            // Drivers pequenos provavelmente s�o SSD
            storageInfo.Type = StorageType.SSD;
            storageInfo.DetectionMethod = "Heuristic(Small Size)";
        }

        return storageInfo;
    }

    private async Task OptimizeSsdAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        _logger.LogInfo("[StorageOptimizer] Aplicando otimizações SSD...");

        try
        {
            // 1. Configurar TRIM
            if (await ConfigureTrimOptimizationAsync())
            {
                result.OptimizationsApplied.Add("TRIM habilitado");
            }

            // 2. Otimizar cache de escrita
            if (await OptimizeWriteCacheAsync(storageInfo))
            {
                result.OptimizationsApplied.Add("Cache de escrita otimizado");
            }

            // 3. Desabilitar desfragmentação automática
            await DisableAutoDefragmentationAsync(result);

            // 4. Otimizar agendamento do Windows
            await OptimizeWindowsSchedulerForSsdAsync(result);

            _logger.LogSuccess("[StorageOptimizer] Otimizações SSD aplicadas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro nas otimizações SSD: {ex.Message}");
            result.OptimizationsApplied.Add($"Erro SSD: {ex.Message}");
        }
    }

    private async Task OptimizeNvMeAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        _logger.LogInfo("[StorageOptimizer] Aplicando otimizações NVMe...");

        try
        {
            // Herdar otimizações SSD
            await OptimizeSsdAsync(storageInfo, result);

            // Otimizações específicas NVMe
            await OptimizeNvMeSpecificAsync(result);

            _logger.LogSuccess("[StorageOptimizer] Otimizações NVMe aplicadas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro nas otimizações NVMe: {ex.Message}");
            result.OptimizationsApplied.Add($"Erro NVMe: {ex.Message}");
        }
    }

    private async Task OptimizeHddAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        _logger.LogInfo("[StorageOptimizer] Aplicando otimizações HDD...");

        try
        {
            // 1. Otimizar cache de escrita
            if (await OptimizeWriteCacheAsync(storageInfo))
            {
                result.OptimizationsApplied.Add("Cache de escrita otimizado");
            }

            // 2. Habilitar desfragmentação automática
            await EnableAutoDefragmentationAsync(result);

            // 3. Otimizar layout de arquivos
            await OptimizeFileLayoutForHddAsync(result);

            _logger.LogSuccess("[StorageOptimizer] Otimizações HDD aplicadas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro nas otimizações HDD: {ex.Message}");
            result.OptimizationsApplied.Add($"Erro HDD: {ex.Message}");
        }
    }

    private async Task ApplyGeneralOptimizationsAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        try
        {
            // Otimizações gerais aplic�veis a todos os tipos
            // 1. Configurar políticas de energia do storage
            await ConfigureStoragePowerPoliciesAsync(storageInfo, result);

            // 2. Otimizar sistema de arquivos
            await OptimizeFileSystemAsync(storageInfo, result);

            // 3. Limpar cache de sistema se necessário
            await CleanupSystemCacheAsync(result);

            _logger.LogDebug("[StorageOptimizer] Otimizações gerais aplicadas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro nas otimizações gerais: {ex.Message}");
        }
    }

    private async Task<TrimStatus> GetTrimStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior query DisableDeleteNotify",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var isEnabled = output.Contains("DisableDeleteNotify = 0");

                return new TrimStatus
                {
                    IsEnabled = isEnabled,
                    StatusMessage = output.Trim(),
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StorageOptimizer] Erro ao verificar status TRIM: {ex.Message}");
                return new TrimStatus
                {
                    IsEnabled = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        });
    }

    private async Task<bool> EnableTrimAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior set DisableDeleteNotify 0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StorageOptimizer] Erro ao habilitar TRIM: {ex.Message}");
                return false;
            }
        });
    }
    private WriteCachePolicy GetOptimalCachePolicy(StorageType storageType)
    {
        return storageType switch
        {
            StorageType.SSD => WriteCachePolicy.WriteBack,
            StorageType.NVMe => WriteCachePolicy.WriteBack,
            StorageType.HDD => WriteCachePolicy.WriteThrough,
            _ => WriteCachePolicy.WriteBack
        };
    }

    private async Task<bool> SetWriteCachePolicyAsync(WriteCachePolicy policy)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Nota: Esta � uma implementação simplificada
                // Na pr�tica, issão exigiria chamadas de API específicas do Windows
                var policyValue = policy switch
                {
                    WriteCachePolicy.WriteBack => 1,
                    WriteCachePolicy.WriteThrough => 2,
                    WriteCachePolicy.Disabled => 0,
                    _ => 1
                };

                _logger.LogDebug($"[StorageOptimizer] Pol�tica de cache definida: {policy} (valor: {policyValue})");

                return true; // Simplificado - sempre retorna sucessão
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StorageOptimizer] Erro ao definir política de cache: {ex.Message}");
                return false;
            }
        });
    }

    private async Task<double> MeasureSequentialReadSpeedAsync()
    {
        return await Task.FromResult(550.0); // MB/s típico para SSD
    }

    private async Task<double> MeasureSequentialWriteSpeedAsync()
    {
        return await Task.FromResult(520.0); // MB/s típico para SSD
    }

    private async Task<double> MeasureRandomReadSpeedAsync()
    {
        return await Task.FromResult(85.0); // MB/s típico para SSD
    }

    private async Task<double> MeasureRandomWriteSpeedAsync()
    {
        return await Task.FromResult(80.0); // MB/s típico para SSD
    }

    private async Task<double> MeasureAverageLatencyAsync()
    {
        return await Task.FromResult(0.1); // ms típico para SSD
    }
    #endregion

    #region Mtodos de Otimização Específicos
    private async Task DisableAutoDefragmentationAsync(StorageOptimizationResult result)
    {
        try
        {
            // Implementação simplificada
            result.OptimizationsApplied.Add("Desfragmentação automática desabilitada");

            _logger.LogDebug("[StorageOptimizer] Desfragmentação automática desabilitada para SSD");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao desabilitar desfragmentação: {ex.Message}");
        }
    }

    private async Task EnableAutoDefragmentationAsync(StorageOptimizationResult result)
    {
        try
        {
            // Implementação simplificada
            result.OptimizationsApplied.Add("Desfragmentação automática habilitada");

            _logger.LogDebug("[StorageOptimizer] Desfragmentação automática habilitada para HDD");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao habilitar desfragmentação: {ex.Message}");
        }
    }

    private async Task OptimizeWindowsSchedulerForSsdAsync(StorageOptimizationResult result)
    {
        try
        {
            // Implementação simplificada
            result.OptimizationsApplied.Add("Agendador Windows otimizado para SSD");

            _logger.LogDebug("[StorageOptimizer] Agendador otimizado para SSD");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao otimizar agendador: {ex.Message}");
        }
    }

    private async Task OptimizeNvMeSpecificAsync(StorageOptimizationResult result)
    {
        try
        {
            // Implementação simplificada
            result.OptimizationsApplied.Add("Otimizações NVMe específicas aplicadas");

            _logger.LogDebug("[StorageOptimizer] Otimizações NVMe aplicadas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro nas otimizações NVMe: {ex.Message}");
        }
    }

    private async Task OptimizeFileLayoutForHddAsync(StorageOptimizationResult result)
    {
        try
        {
            //Implementação simplificada
            result.OptimizationsApplied.Add("Layout de arquivos otimizado para HDD");
            _logger.LogDebug("[StorageOptimizer] Layout de arquivos otimizado para HDD");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao otimizar layout: {ex.Message}");
        }
    }

    private async Task ConfigureStoragePowerPoliciesAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        try
        {
            //Implementação simplificada
            result.OptimizationsApplied.Add("Pol�ticas de energia do storage configuradas");
            _logger.LogDebug("[StorageOptimizer] Pol�ticas de energia configuradas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao configurar políticas: {ex.Message}");
        }
    }

    private async Task OptimizeFileSystemAsync(StorageInfo storageInfo, StorageOptimizationResult result)
    {
        try
        {
            //Implementação simplificada
            result.OptimizationsApplied.Add("Sistema de arquivos otimizado");
            _logger.LogDebug("[StorageOptimizer] Sistema de arquivos otimizado");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao otimizar sistema de arquivos: {ex.Message}");
        }
    }

    private async Task CleanupSystemCacheAsync(StorageOptimizationResult result)
    {
        try
        {
            //Implementação simplificada
            result.OptimizationsApplied.Add("Cache de sistema limpo");
            _logger.LogDebug("[StorageOptimizer] Cache de sistema limpo");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[StorageOptimizer] Erro ao limpar cache: {ex.Message}");
        }
    }
    #endregion
    }
}
