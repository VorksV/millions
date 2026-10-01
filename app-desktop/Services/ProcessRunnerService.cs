using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Implementação do IProcessRunner para execução segura de processos externos
    /// </summary>
    public class ProcessRunnerService : IProcessRunner
    {
        private readonly ILoggingService? _logger;

        public ProcessRunnerService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        public async Task<ProcessRunResult> RunAsync(string fileName, string arguments, int timeoutMs = 30000)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();
            var result = new ProcessRunResult();

            try
            {
                var context = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                _logger?.LogInfo($"[ProcessRunner|{correlationId}] Iniciando processo. Contexto: {context}");
                _logger?.LogInfo($"[ProcessRunner|{correlationId}] Cmd: {fileName} {arguments}");

                // Registrar CodePages para suportar encoding OEM (ex: 850, 437)
                // Necessário antes de chamar Encoding.GetEncoding com codepage não-padrão
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                // Usar encoding OEM do console do Windows para processos nativos (powercfg, bcdedit, etc.)
                // UTF-8 causa caracteres corrompidos em stderr de processos que usam codepage do sistema
                System.Text.Encoding consoleEncoding;
                try
                {
                    consoleEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                }
                catch
                {
                    consoleEncoding = System.Text.Encoding.UTF8;
                }

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = consoleEncoding,
                        StandardErrorEncoding = consoleEncoding
                    }
                };

                process.Start();

                // Ler output de forma assíncrona para evitar deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                bool exited = process.WaitForExit(timeoutMs);

                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    result.Success = false;
                    result.StandardError = "Processo excedeu o tempo limite e foi encerrado.";
                    _logger?.LogWarning($"[ProcessRunner|{correlationId}] Timeout ao executar: {fileName}. Tempo excedido ({timeoutMs}ms).");
                }
                else
                {
                    result.Success = process.ExitCode == 0;
                    result.ExitCode = process.ExitCode;
                    
                    // ✅ CORREÇÃO: Usar await seguro sem capturar contexto de sincronização (anti-deadlock WPF)
                    result.StandardOutput = await outputTask.ConfigureAwait(false);
                    result.StandardError = await errorTask.ConfigureAwait(false);

                    if (result.Success)
                    {
                        _logger?.LogInfo($"[ProcessRunner|{correlationId}] Sucesso: {fileName} (ExitCode: {result.ExitCode}, Duração: {stopwatch.ElapsedMilliseconds}ms)");
                        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                            _logger?.LogDebug($"[ProcessRunner|{correlationId}] StdOut:\n{result.StandardOutput}");
                    }
                    else
                    {
                        _logger?.LogWarning($"[ProcessRunner|{correlationId}] Falha: {fileName} (ExitCode: {result.ExitCode}, Duração: {stopwatch.ElapsedMilliseconds}ms)");
                        if (!string.IsNullOrEmpty(result.StandardError))
                            _logger?.LogWarning($"[ProcessRunner|{correlationId}] StdErr:\n{result.StandardError}");
                        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                            _logger?.LogWarning($"[ProcessRunner|{correlationId}] StdOut:\n{result.StandardOutput}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[ProcessRunner|{correlationId}] Exceção crítica ao executar processo: {fileName}", ex);
                result.Success = false;
                result.StandardError = ex.Message;
                _logger?.LogError($"[ProcessRunner] Exceção ao executar {fileName}: {ex.Message}", ex);
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
            }

            return result;
        }

        /// <summary>
        /// Wrapper síncrono para RunAsync (para compatibilidade com código legado)
        /// </summary>
        public ProcessRunResult Run(string fileName, string arguments, int timeoutMs = 30000)
        {
            // CORREÇÃO: Task.Run garante que GetAwaiter().GetResult() seja executado fora do
            // SynchronizationContext da UI thread, eliminando risco de deadlock quando chamado
            // de event handlers ou code-behind de Views.
            return Task.Run(() => RunAsync(fileName, arguments, timeoutMs)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executa um processo de forma assíncrona com suporte a CancellationToken
        /// </summary>
        public async Task<ProcessRunResult> RunAsync(string fileName, string arguments, int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            return await RunAsync(fileName, arguments, timeoutMs);
        }

        public ProcessRunResult RunElevated(string fileName, string arguments)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ProcessRunResult();

            try
            {
                _logger?.LogInfo($"[ProcessRunner] Executando elevado: {fileName} {arguments}");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true
                    }
                };

                process.Start();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException($"O processo {fileName} atingiu o timeout de 30s e foi finalizado à força.");
                }

                result.Success = process.ExitCode == 0;
                result.ExitCode = process.ExitCode;

                _logger?.LogInfo($"[ProcessRunner] Elevado concluído: {fileName} (exit code: {result.ExitCode})");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StandardError = ex.Message;
                _logger?.LogError($"[ProcessRunner] Exceção ao executar elevado {fileName}: {ex.Message}", ex);
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
            }

            return result;
        }

        public bool IsProcessRunning(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ProcessRunner] Erro ao verificar processo {processName}: {ex.Message}");
                return false;
            }
        }

        public Process[] GetProcessesByName(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[ProcessRunner] Erro ao obter processos {processName}: {ex.Message}");
                return Array.Empty<Process>();
            }
        }

        public bool TryKillProcess(string processName, int waitTimeMs = 2000)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    return true; // Já não está rodando
                }

                foreach (var process in processes)
                {
                    try
                    {
                        // Tentar fechar graciosamente primeiro
                        process.CloseMainWindow();

                        if (!process.WaitForExit(waitTimeMs))
                        {
                            // Forçar encerramento
                            process.Kill();
                            _logger?.LogWarning($"[ProcessRunner] Processo {processName} (PID: {process.Id}) foi forçadamente encerrado");
                        }
                        else
                        {
                            _logger?.LogInfo($"[ProcessRunner] Processo {processName} (PID: {process.Id}) encerrado graciosamente");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[ProcessRunner] Erro ao encerrar {processName}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[ProcessRunner] Exceção ao tentar encerrar {processName}: {ex.Message}", ex);
                return false;
            }
        }
    }
}

