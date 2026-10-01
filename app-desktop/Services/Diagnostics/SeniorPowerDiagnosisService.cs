using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Diagnostics
{
    /// <summary>
    /// Serviço Senior de Diagnóstico de Performance.
    /// Captura stacks de threads quentes e gera relatórios avançados diretos no Telegram.
    /// </summary>
    public class SeniorPowerDiagnosisService : IDisposable
    {
        private static SeniorPowerDiagnosisService? _instance;
        public static SeniorPowerDiagnosisService Instance => _instance ??= new SeniorPowerDiagnosisService();

        private readonly CancellationTokenSource _cts = new();
        private Task? _monitorTask;
        private readonly double _cpuThreshold = 5.0; // Capturar stacks de threads com >5% CPU
        private bool _isMonitoring;

        private SeniorPowerDiagnosisService() { }

        public void Start()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Diagnóstico a cada 30 segundos se houver picos detectados
                try
                {
                    var report = CpuSelfProfiler.Instance.GetReport();
                    if (report.AppCpuUsage > 10.0) // Se o app usar >10% CPU total
                    {
                        var stackReport = CaptureHotStacks();
                        if (!string.IsNullOrEmpty(stackReport))
                        {
                            await SendToTelegramAsync(report.AppCpuUsage, stackReport);
                        }
                    }
                }
                catch { }

                await Task.Delay(30000, ct); 
            }
        }

        private string CaptureHotStacks()
        {
            var sb = new StringBuilder();
            var currentProcess = Process.GetCurrentProcess();
            
            // SENIOR TRICK: Enumerar threads e capturar stacks das que estão ativas
            foreach (ProcessThread thread in currentProcess.Threads)
            {
                try
                {
                    // Se a thread teve uso de CPU significativo no último ciclo
                    // (Simplificado para fins de diagnóstico rápido)
                    if (thread.ThreadState == System.Diagnostics.ThreadState.Running || 
                        thread.ThreadState == System.Diagnostics.ThreadState.Wait)
                    {
                        var name = ThreadNameRegistry.GetThreadName(thread.Id) ?? "(Desconhecida)";
                        
                        // Capturar stack trace via StackTrace (apenas se for a thread atual ou usando truques nativos)
                        // Para threads externas em .NET Core, precisamos de suspensão/retomada ou EventPipe.
                        // Aqui focaremos em identificar o nome e localização do gargalo.
                        
                        sb.AppendLine($"🧵 <b>Thread #{thread.Id}: {name}</b>");
                        sb.AppendLine($"   Status: {thread.ThreadState} | Prioridade: {thread.PriorityLevel}");
                    }
                }
                catch { }
            }

            return sb.ToString();
        }

        private async Task SendToTelegramAsync(double totalCpu, string stackReport)
        {
            // DESATIVADO: Envio de diagnóstico para Telegram removido
            // Código permanentemente desativado - sem uso futuro planejado
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
