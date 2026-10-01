using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço SEGURO para limpeza do Component Store (WinSxS)
    /// Usa APENAS métodos oficiais da Microsoft - NENHUMA deleção direta
    /// </summary>
    public class SafeWinSxSCleanupService
    {
        private readonly ILoggingService _logger;

        public SafeWinSxSCleanupService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Limpeza SEGURA do WinSxS usando apenas DISM oficial
        /// </summary>
        public async Task<long> CleanWinSxSSafeAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo("[SafeWinSxS] 🔍 Iniciando limpeza SEGURA do Component Store...");

                // VERIFICAÇÃO DE SEGURANÇA CRÍTICA
                var safetyChecker = new ComponentStoreSafetyChecker(_logger);
                if (!await safetyChecker.IsSafeForComponentStoreOperationAsync())
                {
                    _logger.LogWarning("[SafeWinSxS] ⚠ Operação BLOQUEADA por segurança do Component Store");
                    return 0;
                }

                var beforeSize = EstimateWinSxSSpace();

                // PASSO 1: Limpeza de componentes substituídos — SEGURO.
                // Remove apenas versões de pacotes que já foram substituídas por
                // atualizações mais recentes. Reversível, sem risco de rollback.
                _logger.LogInfo("[SafeWinSxS] PASSO 1: /StartComponentCleanup");
                await RunDismCommandAsync("/Online /Cleanup-Image /StartComponentCleanup");

                // PASSO 2: /ResetBase foi REMOVIDO do caminho automático.
                //
                // /ResetBase torna PERMANENTES todas as atualizações instaladas e
                // descarta a chance de desinstalar uma atualização ruim (a base de
                // dados de " uninstall list" do WinSxS é removida). Isso não é
                // reversível pelo usuário e não é decisão do limpador.
                //
                // Quem quiser esse ganho precisa chamar
                // CleanWinSxSWithResetBaseAsync() via UI de opt-in.
                _logger.LogInfo("[SafeWinSxS] /ResetBase NAO executado: torna atualizações permanentes. Disponível apenas via opt-in.");

                var afterSize = EstimateWinSxSSpace();
                var cleaned = beforeSize - afterSize;

                sw.Stop();
                _logger.LogSuccess($"[SafeWinSxS] ✅ Limpeza SEGURA concluída em {sw.Elapsed.TotalSeconds:F1}s. Liberado: {FormatBytes(cleaned)}");
                return cleaned > 0 ? cleaned : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError($"[SafeWinSxS] ❌ Erro na limpeza SEGURA ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Limpeza do Component Store com /ResetBase — EXIGE OPT-IN EXPLÍCITO.
        ///
        /// O que /ResetBase faz:
        ///  - torna PERMANENTES todas as atualizações já instaladas;
        ///  - descarta a "uninstall list", então nenhuma atualização poderá ser
        ///    removida depois (nem uma atualização com defeito);
        ///  - o espaço só volta a ser recuperável numa reinstalação/upgrade.
        ///
        /// Por isso está fora de CleanWinSxSSafeAsync e nunca roda no botão
        /// circular. Só chamar a partir de uma tela onde o usuário marcou
        /// conscientemente que aceita perder o rollback de updates.
        /// </summary>
        public async Task<long> CleanWinSxSWithResetBaseAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var safetyChecker = new ComponentStoreSafetyChecker(_logger);
                if (!await safetyChecker.IsSafeForComponentStoreOperationAsync())
                {
                    _logger.LogWarning("[SafeWinSxS] Operacao BLOQUEADA por seguranca do Component Store");
                    return 0;
                }

                var beforeSize = EstimateWinSxSSpace();
                _logger.LogWarning("[SafeWinSxS] OPT-IN: executando /ResetBase. Atualizacoes serao permanentes.");
                await RunDismCommandAsync("/Online /Cleanup-Image /StartComponentCleanup /ResetBase");

                var afterSize = EstimateWinSxSSpace();
                var cleaned = beforeSize - afterSize;
                sw.Stop();
                _logger.LogSuccess($"[SafeWinSxS] /ResetBase concluido em {sw.Elapsed.TotalSeconds:F1}s. Liberado: {FormatBytes(cleaned)}");
                return cleaned > 0 ? cleaned : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError($"[SafeWinSxS] Erro no /ResetBase ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Executa comando DISM com tratamento seguro
        /// </summary>
        private async Task RunDismCommandAsync(string arguments)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[SafeWinSxS] ▶ DISM: {arguments}");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas"
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning("[SafeWinSxS] ⚠ Não foi possível iniciar dism.exe");
                    return;
                }

                _logger.LogInfo($"[SafeWinSxS] ⏳ Aguardando DISM (PID={process.Id}, timeout=3min)...");
                bool exited = await Task.Run(() => process.WaitForExit(180000)).ConfigureAwait(false);

                sw.Stop();
                if (!exited)
                {
                    _logger.LogWarning($"[SafeWinSxS] ⏰ TIMEOUT DISM ({sw.Elapsed.TotalSeconds:F1}s), forçando encerramento");
                    try { process.Kill(); } catch { }
                }
                else
                {
                    _logger.LogSuccess($"[SafeWinSxS] ✅ DISM concluído em {sw.Elapsed.TotalSeconds:F1}s (ExitCode={process.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError($"[SafeWinSxS] ❌ Erro ao executar DISM ({sw.Elapsed.TotalSeconds:F1}s): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Estima espaço do WinSxS via registro (método não invasivo)
        /// </summary>
        private long EstimateWinSxSSpace()
        {
            try
            {
                // Método seguro: ler do registro do Windows
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing");
                if (key?.GetValue("ComponentStoreSize") is long storeSize)
                {
                    _logger.LogInfo($"[SafeWinSxS] Tamanho do Component Store (registro): {FormatBytes(storeSize)}");
                    return storeSize;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SafeWinSxS] Erro ao ler tamanho do WinSxS do registro: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Formata bytes para exibição
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
