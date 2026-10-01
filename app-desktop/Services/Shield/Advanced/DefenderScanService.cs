using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Advanced
{
    /// <summary>
    /// Integração com Windows Defender (MpCmdRun.exe) — Executa e analisa scans externos de suporte.
    /// </summary>
    public class DefenderScanService
    {
        private readonly ILoggingService _logger;
        private readonly string _mpCmdRunPath;

        public DefenderScanService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Local padrão do utilitário do Windows Defender
            _mpCmdRunPath = @"C:\Program Files\Windows Defender\MpCmdRun.exe";
            if (!File.Exists(_mpCmdRunPath))
            {
                _mpCmdRunPath = @"C:\ProgramData\Microsoft\Windows Defender\Platform\";
                // Tenta encontrar a versão mais recente caso o caminho acima mude
                try
                {
                    if (Directory.Exists(_mpCmdRunPath))
                    {
                        var latest = new DirectoryInfo(_mpCmdRunPath)
                            .GetDirectories()
                            .OrderByDescending(directory => directory.LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (latest != null)
                        {
                            var candidate = Path.Combine(latest.FullName, "MpCmdRun.exe");
                            if (File.Exists(candidate))
                            {
                                _mpCmdRunPath = candidate;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public Task<bool> StartQuickScanAsync()
        {
            if (!File.Exists(_mpCmdRunPath))
                return Task.FromResult(false);

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _mpCmdRunPath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add("-Scan");
                process.StartInfo.ArgumentList.Add("-ScanType");
                process.StartInfo.ArgumentList.Add("1");
                process.Start();
                _logger.LogInfo("[DefenderScan] Scan rápido iniciado via MpCmdRun");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DefenderScan] Erro ao iniciar scan rápido: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> UpdateDefinitionsAsync()
        {
            if (!File.Exists(_mpCmdRunPath))
                return false;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _mpCmdRunPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.StartInfo.ArgumentList.Add("-SignatureUpdate");
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DefenderScan] Erro ao atualizar definições: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Solicita ao Windows Defender a análise de um arquivo específico em background.
        /// </summary>
        public async Task<DefenderAdvancedScanResult> ScanFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return new DefenderAdvancedScanResult { IsDetected = false };
                if (!File.Exists(_mpCmdRunPath)) return new DefenderAdvancedScanResult { IsDetected = false, Error = "MpCmdRun.exe não encontrado" };

                _logger.LogInfo($"[DefenderScan] Iniciando scan do Defender para: {Path.GetFileName(filePath)}");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _mpCmdRunPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add("-Scan");
                process.StartInfo.ArgumentList.Add("-ScanType");
                process.StartInfo.ArgumentList.Add("3");
                process.StartInfo.ArgumentList.Add("-File");
                process.StartInfo.ArgumentList.Add(filePath);
                process.StartInfo.ArgumentList.Add("-DisableRemediation");

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Analisar resultado (0 = Limpo, 2 = Ameaça encontrada)
                // O MpCmdRun retorna ExitCode 2 se detectar ameaça
                bool detected = process.ExitCode == 2 || 
                               output.Contains("Threat detected", StringComparison.OrdinalIgnoreCase) || 
                               output.Contains("malware", StringComparison.OrdinalIgnoreCase);

                if (detected)
                {
                    _logger.LogWarning($"[DefenderScan] Defender detectou ameaça em {filePath}");
                }

                return new DefenderAdvancedScanResult
                {
                    IsDetected = detected,
                    Details = output.Length > 200 ? output.Substring(0, 200) + "..." : output,
                    ExitCode = process.ExitCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DefenderScan] Erro ao integrar com Windows Defender: {ex.Message}");
                return new DefenderAdvancedScanResult { IsDetected = false, Error = ex.Message };
            }
        }

    }

    public class DefenderAdvancedScanResult
    {
        public bool IsDetected { get; set; }
        public string Details { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int ExitCode { get; set; }
    }
}
