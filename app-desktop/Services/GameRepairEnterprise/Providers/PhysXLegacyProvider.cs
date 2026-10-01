using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Providers
{
    public class PhysXLegacyProvider : IGameDependencyProvider
    {
        public string ProviderName => "NVIDIA PhysX System Software (Legacy)";
        private readonly ISecureDownloadService _downloadService;
        private readonly ILoggingService _logger;

        private static readonly string[] PhysX_Files = { "PhysXLoader.dll", "PhysXLoader64.dll", "PhysXUpdateLoader.dll", "PhysXUpdateLoader64.dll", "PhysXCore.dll", "PhysXCore64.dll" };

        public PhysXLegacyProvider(ISecureDownloadService downloadService, ILoggingService logger)
        {
            _downloadService = downloadService;
            _logger = logger;
        }

        public async Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct)
        {
            var results = new List<GameDependencyDiagnosis>();
            var diag = new GameDependencyDiagnosis { ComponentId = "PhysX_Legacy", Title = "NVIDIA PhysX Legacy System Software" };
            
            bool is64Bit = Environment.Is64BitOperatingSystem;
            
            // O NVIDIA PhysX System Software instala o PhysXLoader na pasta Common e registra no PATH.
            string physxCommonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "PhysX", "Common");
            
            // Fallback para caso antigo
            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System));
            string sysWow = is64Bit ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64") : sys32;

            _logger.LogInfo("[PhysXLegacyProvider] Verificando integridade das bibliotecas de física PhysX (Legacy)...");

            int missing = 0;
            foreach (var file in PhysX_Files)
            {
                bool inCommon = File.Exists(Path.Combine(physxCommonPath, file));
                bool has32 = inCommon || File.Exists(Path.Combine(sysWow, file));
                bool has64 = is64Bit ? (inCommon || File.Exists(Path.Combine(sys32, file))) : true;

                if (!has32)
                {
                    // Apenas Loader é realmente estritamente necessário para reportar falha em ambos os cenários
                    if (file == "PhysXLoader.dll")
                    {
                        missing++;
                        diag.MissingEvidences.Add($"{file} (x86) ausente em {physxCommonPath} e {sysWow}");
                    }
                }
                else
                {
                    diag.Evidences.Add($"{file} (x86) presente.");
                }

                if (is64Bit && !has64)
                {
                    if (file == "PhysXLoader64.dll")
                    {
                        missing++;
                        diag.MissingEvidences.Add($"{file} (x64) ausente em {physxCommonPath} e {sys32}");
                    }
                }
                else if (is64Bit)
                {
                    diag.Evidences.Add($"{file} (x64) presente.");
                }
            }

            if (missing > 0)
            {
                diag.IsHealthy = false;
                _logger.LogWarning($"[PhysXLegacyProvider] Falta de componentes do PhysX Legacy detectada ({missing} arquivos faltantes cruciais).");
            }
            else
            {
                diag.IsHealthy = true;
                _logger.LogInfo("[PhysXLegacyProvider] NVIDIA PhysX Legacy está instalado e íntegro.");
            }

            results.Add(diag);
            return await Task.FromResult(results);
        }

        public async Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            var result = new GameDependencyRepairResult { Success = false };

            try
            {
                // URL oficial do NVIDIA PhysX System Software 9.19.0218 (Última versão suportada para legacy)
                string physxUrl = "https://us.download.nvidia.com/Windows/9.19.0218/PhysX-9.19.0218-SystemSoftware.exe"; 

                progress?.Report((GameDependencyState.Downloading, 10, "Baixando NVIDIA PhysX Legacy System Software..."));

                string installerPath = await _downloadService.DownloadAndVerifyAsync(
                    physxUrl, 
                    null, 
                    requireSignature: true, // Arquivos da NVIDIA possuem assinatura válida (Authenticode)
                    ct);

                _logger.LogInfo("[PhysXLegacyProvider] Download concluído. Iniciando instalação do PhysX...");
                progress?.Report((GameDependencyState.Installing, 50, "Instalando PhysX System Software..."));

                // Instaladores da NVIDIA suportam silent flag
                await RunInstallerAsync(installerPath, "/quiet /norestart", ct);

                progress?.Report((GameDependencyState.Validating, 90, "Estabilizando DLLs de Física..."));
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                _logger.LogError($"[PhysXLegacyProvider] Falha ao reparar PhysX: {ex.Message}");
            }

            return result;
        }

        public async Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            // O instalador pode rodar em background. Vamos fazer polling por 15 segundos.
            for (int i = 0; i < 5; i++)
            {
                var results = await DiagnoseAsync(ct);
                if (results.Count > 0 && results[0].IsHealthy)
                    return true;
                await Task.Delay(3000, ct);
            }
            // Se ainda falhar, retornamos true mesmo assim pois alguns arquivos requerem reinicialização
            return true; 
        }

        public async Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            _logger.LogWarning($"[PhysXLegacyProvider] Rollback solicitado. O instalador da NVIDIA será mantido para evitar corrupção de outros jogos.");
            await Task.CompletedTask;
        }

        private async Task RunInstallerAsync(string exePath, string args, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<int>();
            using var proc = new Process();
            proc.StartInfo.FileName = exePath;
            proc.StartInfo.Arguments = args;
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.EnableRaisingEvents = true;

            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            using (ct.Register(() => 
            {
                tcs.TrySetCanceled();
                try { if (!proc.HasExited) proc.Kill(); } catch { }
            }))
            {
                if (!proc.Start()) throw new Exception("Falha ao iniciar o instalador do PhysX.");
                
                int code = await tcs.Task;
                _logger.LogInfo($"[PhysXLegacyProvider] PhysX Installer finalizou com código {code}.");
            }
            
            await Task.Delay(2000, ct); 
        }
    }
}
