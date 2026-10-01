using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper centralizado para execução de processos externos
    /// Elimina código duplicado de RunProcess espalhado pelos serviços
    /// </summary>
    public static class ProcessHelper
    {
        /// <summary>
        /// Executa um processo de forma síncrona com timeout
        /// </summary>
        /// <param name="fileName">Nome do executável</param>
        /// <param name="arguments">Argumentos</param>
        /// <param name="stdOut">Saída padrão</param>
        /// <param name="stdErr">Saída de erro</param>
        /// <param name="timeoutMs">Timeout em milissegundos (padrão: 30000)</param>
        /// <returns>True se exitCode == 0</returns>
        public static bool Run(string fileName, string arguments, out string stdOut, out string stdErr, int timeoutMs = 30000)
{
    // Existing method
    stdOut = string.Empty;
    stdErr = string.Empty;

    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        stdOut = process.StandardOutput.ReadToEnd();
        stdErr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(timeoutMs))
        {
            ExceptionHelper.TryKillProcess(process);
            stdErr = "Process timed out";
            return false;
        }

        return process.ExitCode == 0;
    }
    catch (Exception ex)
    {
        stdErr = ex.Message;
        return false;
    }
}

// New safe helper to get process by ID that returns null if not found
public static Process? TryGetProcessById(int pid)
{
    try
    {
        return Process.GetProcessById(pid);
    }
    catch (ArgumentException)
    {
        // PID does not exist
        return null;
    }
}

        
        /// <summary>
        /// Executa um processo de forma assíncrona
        /// </summary>
        public static async Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct = default, int timeoutMs = 30000)
        {
            var result = new ProcessResult();
            
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                
                var tcs = new TaskCompletionSource<bool>();
                process.Exited += (s, e) => tcs.TrySetResult(true);
                
                process.Start();
                
                // Ler output de forma assíncrona
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();
                
                // Aguardar com timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                
                try
                {
                    await Task.WhenAny(tcs.Task, Task.Delay(-1, cts.Token));
                }
                catch (OperationCanceledException)
                {
                    ExceptionHelper.TryKillProcess(process);
                    result.StdErr = "Process timed out or cancelled";
                    return result;
                }
                
                result.StdOut = await stdOutTask;
                result.StdErr = await stdErrTask;
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                
                return result;
            }
            catch (Exception ex)
            {
                result.StdErr = ex.Message;
                return result;
            }
        }
        
        /// <summary>
        /// Executa comando PowerCfg
        /// </summary>
        public static bool RunPowerCfg(string arguments, out string output, out string error)
        {
            return Run("powercfg", arguments, out output, out error, 15000);
        }
        
        /// <summary>
        /// Executa comando Netsh
        /// </summary>
        public static bool RunNetsh(string arguments, out string output, out string error)
        {
            return Run("netsh", arguments, out output, out error, 15000);
        }
        
        /// <summary>
        /// Executa comando SC (Service Control)
        /// </summary>
        public static bool RunSc(string arguments, out string output, out string error)
        {
            return Run("sc", arguments, out output, out error, 10000);
        }
        
        /// <summary>
        /// Executa comando BCDEdit (requer admin)
        /// </summary>
        public static bool RunBcdEdit(string arguments, out string output, out string error)
        {
            return Run("bcdedit", arguments, out output, out error, 10000);
        }
        


        public static async Task<Process?> TryGetProcessByIdAsync(int processId)
        {
            try
            {
                return await Task.Run(() => Process.GetProcessById(processId)).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Obtém o caminho do executável de um processo de forma segura
        /// Funciona mesmo quando o processo é 64-bit e o aplicativo é 32-bit
        /// </summary>
        public static string? GetProcessExecutablePath(Process? process)
        {
            if (process == null || process.HasExited)
                return null;
            
            try
            {
                // Tentar usar MainModule primeiro (mais rápido)
                return process.MainModule?.FileName;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Se falhar (processo 64-bit acessado por 32-bit), usar WMI
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {process.Id}");
                    
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var path = obj["ExecutablePath"]?.ToString();
                        if (!string.IsNullOrEmpty(path))
                            return path;
                    }
                }
                catch
                {
                    // Se WMI falhar, tentar usar QueryFullProcessImageName (Windows Vista+)
                    try
                    {
                        return GetProcessImageFileName(process.Id);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
            
            return null;
        }
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);
        
        private static string? GetProcessImageFileName(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var buffer = new System.Text.StringBuilder(1024);
                int size = buffer.Capacity;
                
                if (QueryFullProcessImageName(process.Handle, 0, buffer, ref size))
                {
                    return buffer.ToString();
                }
            }
            catch
            {
                // Ignorar erros
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Resultado de execução de processo
    /// </summary>
    public class ProcessResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
    }

    public static class DispatcherExtensions
    {
        public static void SafeBeginInvoke(this DispatcherObject target, Action action, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
        {
            if (target == null) return;
            try
            {
                var dispatcher = target.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(action, priority);
            }
            catch { }
        }

        public static async Task SafeInvokeAsync(this DispatcherObject target, Action action, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
        {
            if (target == null) return;
            try
            {
                var dispatcher = target.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                await dispatcher.InvokeAsync(action, priority);
            }
            catch { }
        }

        public static bool IsWindowUsable(this Window? window)
        {
            return window != null && window.IsLoaded && window.IsVisible;
        }
    }

    public static class AppDispatcherGuard
    {
        public static bool CanInvoke()
        {
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true)
                    return false;
                if (Application.Current?.Dispatcher == null)
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

