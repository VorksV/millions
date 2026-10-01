using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Hardware
{
    public enum DriveType { Unknown, HDD, SSD }

    public class StorageUnit
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public DriveType Type { get; set; } = DriveType.Unknown;
        public bool IsSystemDrive { get; set; }
        public long SizeBytes { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Serviço de otimização automática de drives (TRIM para SSD, Defrag para HDD)
    /// Integrado ao GlobalProgressService e LoggingService original do Voltris.
    /// </summary>
    public class StorageOptimizerService
    {
        private readonly ILoggingService? _logger;
        private readonly GlobalProgressService _progressService;

        public StorageOptimizerService()
        {
            _logger = App.LoggingService;
            _progressService = GlobalProgressService.Instance;
        }

        /// <summary>
        /// Detecta todas as unidades de armazenamento e seus tipos
        /// </summary>
        public async Task<List<StorageUnit>> GetStorageUnitsAsync()
        {
            return await Task.Run(() =>
            {
                var units = new List<StorageUnit>();
                try
                {
                    _logger?.Log(LogLevel.Debug, LogCategory.Optimization, "Iniciando detecção de unidades físicas e lógicas.");

                    // 1. Obter drives lógicos via DriveInfo
                    var logicalDrives = DriveInfo.GetDrives()
                        .Where(d => d.IsReady && (d.DriveType == System.IO.DriveType.Fixed || d.DriveType == System.IO.DriveType.Removable))
                        .ToList();

                    // 2. Usar WMI para detectar tipo de disco (SSD/HDD) via MSFT_PhysicalDisk
                    var physicalDisks = GetPhysicalDiskMediaTypes();

                    foreach (var drive in logicalDrives)
                    {
                        var unit = new StorageUnit
                        {
                            DriveLetter = drive.Name.Replace("\\", ""),
                            Label = drive.VolumeLabel,
                            SizeBytes = drive.TotalSize,
                            IsSystemDrive = IsDriveSystem(drive.Name),
                            Type = DetectMediaType(drive.Name, physicalDisks)
                        };
                        units.Add(unit);
                    }
                    
                    _logger?.Log(LogLevel.Info, LogCategory.Optimization, $"Detecção concluída: {units.Count} unidades identificadas.");
                }
                catch (Exception ex)
                {
                    _logger?.Log(LogLevel.Error, LogCategory.Optimization, $"Falha na detecção de drives: {ex.Message}", ex);
                }
                return units;
            });
        }

        /// <summary>
        /// Executa a otimização em todas as unidades detectadas, integrado à barra de progresso global.
        /// </summary>
        public async Task OptimizeAllDrivesAsync(IProgress<string>? localProgress = null)
        {
            _logger?.Log(LogLevel.Info, LogCategory.Optimization, "Iniciando Operação Global: Otimização Inteligente de Armazenamento.");
            
            // Log inicial para o usuário saber que o método foi chamado
            localProgress?.Report("[Defrag] Iniciando detecção de discos no sistema...");
            
            // Iniciar operação na fila global do footer
            using var token = _progressService.BeginOperation("Otimização de Armazenamento", isPriority: false);
            token.UpdateProgress(5, "Analisando hardware...");

            try
            {
                var units = await GetStorageUnitsAsync();
                
                if (units == null || units.Count == 0)
                {
                    localProgress?.Report("⚠️ [Defrag ERRO] Nenhuma unidade detectada para otimização!");
                    token.Complete("Sem unidades elegíveis");
                    return;
                }

                localProgress?.Report($"[Defrag] Detectadas {units.Count} unidades armazenáveis.");

                // Priorizar o disco do sistema (Windows)
                var sortedUnits = units.OrderByDescending(u => u.IsSystemDrive).ToList();
                int total = sortedUnits.Count;
                int processed = 0;

                foreach (var unit in sortedUnits)
                {
                    processed++;
                    int globalPercent = 5 + (int)((double)processed / total * 90);

                    try
                    {
                        // Filtro de segurança: evitar removíveis instáveis
                        var driveInfo = new DriveInfo(unit.DriveLetter);
                        if (driveInfo.DriveType == System.IO.DriveType.Removable && !unit.IsSystemDrive)
                        {
                            _logger?.Log(LogLevel.Warning, LogCategory.Optimization, $"Ignorando mídia removível: {unit.DriveLetter}");
                            localProgress?.Report($"[Defrag] Ignorando mídia removível {unit.DriveLetter}");
                            continue;
                        }

                        string actionVerbose = unit.Type == DriveType.SSD ? "Executando TRIM" : (unit.Type == DriveType.HDD ? "Desfragmentando" : "Analisando");
                        string modelPart = string.IsNullOrWhiteSpace(unit.Model) ? string.Empty : $" ({unit.Model.Trim()})";
                        string statusMsg = $"{actionVerbose} em {unit.DriveLetter}{modelPart} [Tipo WMI: {unit.Type}]";
                        
                        _logger?.Log(LogLevel.Info, LogCategory.Optimization, $"Iniciando {unit.Type} Optimization em {unit.DriveLetter}");
                        
                        localProgress?.Report($"──────────────────────────────────────────");
                        localProgress?.Report($"🔵 [{processed}/{total}] {statusMsg}");
                        token.UpdateProgress(globalPercent, statusMsg);

                        string args = "";
                        if (unit.Type == DriveType.SSD)
                        {
                            args = "/O"; // /O realiza a otimização adequada para a mídia (TRIM no SSD) e ATUALIZA a interface gráfica do Windows (dfrgui)
                            _logger?.Log(LogLevel.Debug, LogCategory.Optimization, $"[StorageOptimizer] Unidade {unit.DriveLetter} (SSD). defrag.exe {args}");
                            localProgress?.Report($"[Defrag] Configurado para SSD (Trim/Otimização Media Tier /O)");
                        }
                        else if (unit.Type == DriveType.HDD)
                        {
                            args = "/O"; // /O faz desfragmentação no HDD e atualiza a interface
                            _logger?.Log(LogLevel.Debug, LogCategory.Optimization, $"[StorageOptimizer] Unidade {unit.DriveLetter} (HDD). defrag.exe {args}");
                            localProgress?.Report($"[Defrag] Configurado para HDD (Desfragmentação Física /O)");
                        }
                        else
                        {
                            args = "/A";
                            _logger?.Log(LogLevel.Warning, LogCategory.Optimization, $"[StorageOptimizer] Unidade {unit.DriveLetter} DESCONHECIDA. defrag.exe /A");
                            localProgress?.Report($"[Defrag] TIPO DESCONHECIDO para {unit.DriveLetter}. Apenas verificando a unidade sem aplicar ações (/A)");
                        }

                        localProgress?.Report($"[Defrag] Disparando defrag.exe {unit.DriveLetter}: {args}...");
                        await RunOptimizeVolumeAsync(unit.DriveLetter, args, localProgress);

                        localProgress?.Report($"✅ Otimização em {unit.DriveLetter} processada.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log(LogLevel.Error, LogCategory.Optimization, $"Falha ao otimizar {unit.DriveLetter}: {ex.Message}");
                        localProgress?.Report($"⚠️ Erro em {unit.DriveLetter}");
                    }
                }
                
                _logger?.Log(LogLevel.Success, LogCategory.Optimization, "Fluxo de otimização de armazenamento finalizado.");
                localProgress?.Report("Otimização completa!");
                token.Complete("✅ Armazenamento Otimizado");
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Critical, LogCategory.Optimization, "Falha catastrófica no serviço de otimização.", ex);
                token.Complete("❌ Falha na otimização de disco");
            }
        }
        private async Task RunOptimizeVolumeAsync(string driveLetter, string args, IProgress<string>? localProgress)
        {
            try
            {
                string letter = driveLetter.TrimEnd('\\').Replace(":", "");
                
                // Resolver o caminho correto do defrag.exe (SysNative para processos 32-bit em SO 64-bit)
                string execFileName = "defrag.exe";
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(winDir))
                {
                    if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                    {
                        var sysNativePath = Path.Combine(winDir, "SysNative", "defrag.exe");
                        if (File.Exists(sysNativePath)) execFileName = sysNativePath;
                    }
                    else
                    {
                        var system32Path = Path.Combine(winDir, "System32", "defrag.exe");
                        if (File.Exists(system32Path)) execFileName = system32Path;
                    }
                }

                _logger?.Log(LogLevel.Debug, LogCategory.Optimization, $"[RunOptimizeVolumeAsync] Executando: {execFileName} {letter}: {args}");
                localProgress?.Report($"[DEBUG] Executando: {execFileName} {letter}: {args}");

                var psi = new ProcessStartInfo
                {
                    FileName = execFileName,
                    Arguments = $"{letter}: {args}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Registrar provider de CodePages para suportar o encoding do Console OEM (CodePage 850) no .NET 6+
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                try {
                    psi.StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    psi.StandardErrorEncoding = psi.StandardOutputEncoding;
                } catch { 
                    // Fallback se ainda der erro
                }

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        var line = e.Data.Replace("\0", "").Trim();
                        // Reportar diretamente para a UI (Debug log mode em RepairView)
                        localProgress?.Report($"[Defrag] {line}");
                        outputBuilder.AppendLine(line);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        var line = e.Data.Replace("\0", "").Trim();
                        localProgress?.Report($"[Defrag ERRO] {line}");
                        errorBuilder.AppendLine(line);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                
                string output = outputBuilder.ToString();
                _logger?.Log(LogLevel.Debug, LogCategory.Optimization, $"[RunOptimizeVolumeAsync] Output de {letter}:\n{output}");
                
                if (process.ExitCode != 0)
                {
                    string error = errorBuilder.ToString();
                    _logger?.Log(LogLevel.Warning, LogCategory.Optimization, $"defrag.exe ({letter}) Erro {process.ExitCode}: {error.Trim()}");
                    localProgress?.Report($"⚠️ defrag.exe falhou com código {process.ExitCode}: {error.Trim()}");
                }
                else
                {
                    _logger?.Log(LogLevel.Debug, LogCategory.Optimization, $"defrag.exe {letter} finalizado com sucesso. Status na interface gráfica do Windows atualizado.");
                    localProgress?.Report($"✅ Otimização física do {letter}: concluída!");
                }
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.Optimization, $"Erro crítico ao executar defrag.exe na unidade {driveLetter}: {ex.Message}");
                localProgress?.Report($"❌ Exceção ao executar defrag.exe: {ex.Message}");
            }
        }

        private bool IsDriveSystem(string driveName)
        {
            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:";
            return driveName.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase);
        }

        private DriveType DetectMediaType(string driveName, Dictionary<int, DriveType> physicalDisks)
        {
            try
            {
                int diskIndex = GetDiskIndexFromDriveInternal(driveName);
                if (diskIndex >= 0 && physicalDisks.ContainsKey(diskIndex))
                {
                    return physicalDisks[diskIndex];
                }
            }
            catch { }
            return DriveType.Unknown;
        }

        private int GetDiskIndexFromDriveInternal(string driveName)
        {
            try
            {
                string letter = driveName.TrimEnd('\\').Replace(":", "");
                using var searcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                
                foreach (ManagementObject partition in searcher.Get())
                {
                using var __dispose_partition = partition;
                    using var diskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                using var __dispose_disk = disk;
                        var index = disk["Index"]?.ToString();
                        if (int.TryParse(index, out int result)) return result;
                    }
                }
            }
            catch { }
            return -1;
        }

        private Dictionary<int, DriveType> GetPhysicalDiskMediaTypes()
        {
            var results = new Dictionary<int, DriveType>();
            try
            {
                using var searcher = new ManagementObjectSearcher(@"Root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    string deviceIdStr = obj["DeviceId"]?.ToString() ?? "-1";
                    string mediaTypeStr = obj["MediaType"]?.ToString() ?? "0";

                    if (int.TryParse(deviceIdStr, out int deviceId) && int.TryParse(mediaTypeStr, out int mediaType))
                    {
                        results[deviceId] = mediaType switch
                        {
                            3 => DriveType.HDD,
                            4 => DriveType.SSD,
                            5 => DriveType.SSD, 
                            _ => DriveType.Unknown
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Warning, LogCategory.Optimization, $"Falha ao consultar MSFT_PhysicalDisk (WMI Storage), tentando fallback. Motivo: {ex.Message}");
                
                try
                {
                    using var searcherFallback = new ManagementObjectSearcher("SELECT Index, Model, Caption FROM Win32_DiskDrive");
                    foreach (ManagementObject disk in searcherFallback.Get())
                    {
                using var __dispose_disk = disk;
                        int index = int.Parse(disk["Index"]?.ToString() ?? "-1");
                        string model = disk["Model"]?.ToString()?.ToUpperInvariant() ?? "";
                        string caption = disk["Caption"]?.ToString()?.ToUpperInvariant() ?? "";

                        if (model.Contains("SSD") || model.Contains("NVME") || caption.Contains("SSD") || caption.Contains("NVME"))
                            results[index] = DriveType.SSD;
                        else
                            results[index] = DriveType.HDD;
                    }
                }
                catch { }
            }
            return results;
        }
    }
}
