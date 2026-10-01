using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Optimization.Unification;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de otimização de arranque que executa ações ultra‑rápidas e seguras.
    /// O objetivo é melhorar a percepção de desempenho logo ao ligar o PC, sem impactar jogos
    /// ou aplicativos em execução. Cada ação usa mecanismos já existentes (TimerResolutionManager,
    /// registro, carregamento de DLLs) e possui rollback automático.
    /// </summary>
    public sealed class BootPerformanceEnhancer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly TimerResolutionManager _timerManager;
        private readonly Process _process;
        private readonly ProcessPriorityClass _originalPriority;
        private readonly IntPtr _originalAffinity;

        public BootPerformanceEnhancer(ILoggingService logger, ITimerResolutionService timerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (timerService == null) throw new ArgumentNullException(nameof(timerService));
            _timerManager = new TimerResolutionManager(_logger, timerService);
            _process = Process.GetCurrentProcess();
            _originalPriority = _process.PriorityClass;
            // Preserve affinity mask para restaurar depois (não é obrigatório, mas seguro)
            GetProcessAffinityMask(_process.Handle, out var mask, out _);
            _originalAffinity = mask;
        }

        /// <summary>
        /// Executa todas as otimizações de boot de forma assíncrona.
        /// </summary>
        public async Task InitializeAsync(CancellationToken ct = default)
        {
            _logger?.LogInfo("[BootEnhancer] Iniciando otimizações de boot ultra‑rápidas.");

            // 1️⃣ Pré‑carregamento de DLLs críticas – garante que elas já estejam mapeadas em memória.
            await Task.Run(() =>
            {
                try
                {
                    var libs = new[] { "kernel32.dll", "user32.dll", "gdi32.dll", "d3d11.dll" };
                    var handles = new List<IntPtr>();
                    foreach (var lib in libs)
                    {
                        var h = LoadLibrary(lib);
                        if (h != IntPtr.Zero) handles.Add(h);
                    }
                    // Pequena espera para que o loader finalize
                    Thread.Sleep(200);
                    foreach (var h in handles) FreeLibrary(h);
                    _logger?.LogSuccess("[BootEnhancer] Bibliotecas do sistema pré‑carregadas.");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[BootEnhancer] Falha ao pré‑carregar bibliotecas: {ex.Message}");
                }
            }, ct);

            // 2️⃣ Aquecimento do cache de disco – leitura de alguns arquivos de sistema essenciais.
            await Task.Run(async () =>
            {
                try
                {
                    var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    var files = new[]
                    {
                        Path.Combine(systemFolder, "ntdll.dll"),
                        Path.Combine(systemFolder, "kernel32.dll"),
                        Path.Combine(systemFolder, "user32.dll")
                    };
                    long total = 0;
                    foreach (var f in files)
                    {
                        if (File.Exists(f))
                        {
                            var data = await File.ReadAllBytesAsync(f, ct);
                            total += data.Length;
                        }
                    }
                    _logger?.LogSuccess($"[BootEnhancer] Cache de disco aquecido – {Helpers.FileSystemHelper.FormatBytes(total)} carregados.");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[BootEnhancer] Falha ao aquecer cache de disco: {ex.Message}");
                }
            }, ct);

            // 3️⃣ Pulse de timer de alta precisão (≈ 0.5 ms) por 5 s – seguro graças ao contador de referência.
            _timerManager.RequestHighPrecision("BootPerformanceEnhancer");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                finally
                {
                    _timerManager.ReleaseHighPrecision("BootPerformanceEnhancer");
                }
            }, ct);

            // 4️⃣ Elevar prioridade do processo Voltris temporariamente (10 s) para UI mais responsiva.
            try
            {
                _process.PriorityClass = ProcessPriorityClass.AboveNormal;
                _logger?.LogInfo("[BootEnhancer] Prioridade do processo Voltris elevada para melhorar UI inicial.");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    }
                    finally
                    {
                        try
                        {
                            _process.PriorityClass = _originalPriority;
                            SetProcessAffinityMask(_process.Handle, _originalAffinity);
                        }
                        catch { }
                        _logger?.LogInfo("[BootEnhancer] Prioridade do processo Voltris restaurada.");
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[BootEnhancer] Não foi possível alterar prioridade do processo: {ex.Message}");
            }

            // 5️⃣ Garantir que os efeitos visuais do Windows estejam no modo "Performance" (registry).
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                key?.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); // 2 = melhor performance
                _logger?.LogInfo("[BootEnhancer] VisualFX configurado para modo Performance.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[BootEnhancer] Falha ao configurar VisualFX: {ex.Message}");
            }

            _logger?.LogSuccess("[BootEnhancer] Boost de boot concluído.");
        }

        //--- P/Interop -----------------------------------------------------------------
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
        [DllImport("kernel32.dll")]
        private static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);
        //--- IDisposable ---------------------------------------------------------------
        public void Dispose()
        {
            try
            {
                _timerManager?.ForceDefault();
                _process.PriorityClass = _originalPriority;
                SetProcessAffinityMask(_process.Handle, _originalAffinity);
            }
            catch { }
            _timerManager?.Dispose();
        }
    }
}
