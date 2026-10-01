using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Providers
{
    public class DotNetFrameworkProvider : IGameDependencyProvider
    {
        public string ProviderName => ".NET Framework (Legacy & Modern)";
        private readonly ILoggingService _logger;

        public DotNetFrameworkProvider(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct)
        {
            var results = new List<GameDependencyDiagnosis>();
            var diag = new GameDependencyDiagnosis { ComponentId = "DotNet_Framework_35", Title = ".NET Framework 3.5 (Launchers/Mods)" };
            
            _logger.LogInfo("[DotNetFrameworkProvider] Avaliando instalação do .NET Framework 3.5 no Windows...");

            await Task.Run(() =>
            {
                bool is35Installed = false;
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5"))
                    {
                        if (key != null)
                        {
                            var install = key.GetValue("Install");
                            if (install != null && Convert.ToInt32(install) == 1)
                            {
                                is35Installed = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DotNetFrameworkProvider] Falha ao consultar o Registro para o .NET 3.5: {ex.Message}");
                }

                if (is35Installed)
                {
                    diag.Evidences.Add("Chave de registro 'Install=1' detectada para v3.5.");
                    diag.IsHealthy = true;
                    _logger.LogInfo("[DotNetFrameworkProvider] .NET Framework 3.5 está ativo no sistema.");
                }
                else
                {
                    diag.MissingEvidences.Add("A feature do .NET Framework 3.5 (NetFx3) não está habilitada neste PC.");
                    diag.IsHealthy = false;
                    _logger.LogWarning("[DotNetFrameworkProvider] .NET Framework 3.5 ausente. Launchers de jogos independentes e instaladores de Mods falharão ao abrir.");
                }

            }, ct);

            results.Add(diag);
            return results;
        }

        public async Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            var result = new GameDependencyRepairResult { Success = false };

            try
            {
                progress?.Report((GameDependencyState.Installing, 20, "Invocando DISM para habilitar .NET Framework 3.5 no Windows..."));

                // .NET 3.5 is a Windows Feature and can be enabled natively via DISM (Deployment Image Servicing and Management).
                // Requires internet connection to download files from Windows Update if not in local cache.
                await RunDismAsync("/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart", ct);

                _logger.LogInfo("[DotNetFrameworkProvider] DISM executado com sucesso para habilitação do NetFx3.");
                progress?.Report((GameDependencyState.Validating, 90, "Validando feature instalada..."));
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                _logger.LogError($"[DotNetFrameworkProvider] Falha ao habilitar .NET Framework via DISM: {ex.Message}");
            }

            return result;
        }

        public async Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            var results = await DiagnoseAsync(ct);
            return results.Count > 0 && results[0].IsHealthy;
        }

        public async Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            _logger.LogInfo($"[DotNetFrameworkProvider] Desativando .NET Framework 3.5 via DISM...");
            await RunDismAsync("/Online /Disable-Feature /FeatureName:NetFx3 /NoRestart", ct);
        }

        private async Task RunDismAsync(string args, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<int>();
            using var proc = new Process();
            proc.StartInfo.FileName = "dism.exe";
            proc.StartInfo.Arguments = args;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.EnableRaisingEvents = true;

            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            using (ct.Register(() => 
            {
                tcs.TrySetCanceled();
                try { if (!proc.HasExited) proc.Kill(); } catch { }
            }))
            {
                if (!proc.Start()) throw new Exception("Falha ao iniciar o DISM.exe.");
                
                int code = await tcs.Task;
                if (code != 0 && code != 3010) // 3010 is ERROR_SUCCESS_REBOOT_REQUIRED
                {
                    throw new Exception($"O DISM falhou com o código de saída: {code}. Verifique a conexão com o Windows Update.");
                }
            }
        }
    }
}
