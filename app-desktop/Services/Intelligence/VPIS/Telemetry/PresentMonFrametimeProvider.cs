using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Telemetry
{
    /// <summary>
    /// Wrapper para a captura ETW via executável do PresentMon (Padrão da indústria).
    /// Requer que o binário 'PresentMon64.exe' esteja presente no diretório.
    /// </summary>
    public class PresentMonFrametimeProvider : IFrametimeProvider
    {
        public bool IsAvailable => File.Exists(GetPresentMonPath());
        public string ProviderName => "PresentMon (ETW Wrapper)";

        public event EventHandler<FrametimeData> FrametimeCaptured;

        private Process _presentMonProcess;
        private CancellationTokenSource _cts;

        public void StartCapture(string targetProcessName)
        {
            if (!IsAvailable)
            {
                System.Diagnostics.Debug.WriteLine("[VPIS-PresentMon] Binário do PresentMon não encontrado. Falha ao iniciar.");
                return;
            }

            _cts = new CancellationTokenSource();
            
            // Inicia o PresentMon direcionando a saída via console para ler em tempo real (-process_name, -output_stdout)
            var psi = new ProcessStartInfo
            {
                FileName = GetPresentMonPath(),
                Arguments = $"-process_name {targetProcessName}.exe -output_stdout -no_csv",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _presentMonProcess = new Process { StartInfo = psi };
            _presentMonProcess.OutputDataReceived += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;

                // Aqui o parser faria a extração do msMsTime (Frametime) da string do stdout.
                // Como exemplo de implementação (Placeholder Parser):
                if (TryParsePresentMonLine(args.Data, out var data))
                {
                    data.ProcessName = targetProcessName;
                    FrametimeCaptured?.Invoke(this, data);
                }
            };

            try
            {
                _presentMonProcess.Start();
                _presentMonProcess.BeginOutputReadLine();
                System.Diagnostics.Debug.WriteLine($"[VPIS-PresentMon] Captura iniciada para {targetProcessName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VPIS-PresentMon] Erro ao iniciar: {ex.Message}");
            }
        }

        public void StopCapture()
        {
            _cts?.Cancel();
            
            if (_presentMonProcess != null && !_presentMonProcess.HasExited)
            {
                try
                {
                    _presentMonProcess.Kill();
                }
                catch { }
                finally
                {
                    _presentMonProcess.Dispose();
                    _presentMonProcess = null;
                }
            }
        }

        private bool TryParsePresentMonLine(string line, out FrametimeData data)
        {
            data = null;
            // Formato PresentMon CSV: Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,MsBetweenPresents,MsBetweenDisplayChange
            // Parser real implementado — sem dados falsos/mockados
            try
            {
                var parts = line.Split(',');
                if (parts.Length < 9) return false;

                // Pular cabeçalho
                if (parts[0].Equals("Application", StringComparison.OrdinalIgnoreCase)) return false;

                if (double.TryParse(parts[8], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var msBetweenPresents)
                    && msBetweenPresents > 0)
                {
                    data = new FrametimeData
                    {
                        Timestamp = DateTime.UtcNow,
                        FrametimeMs = msBetweenPresents,
                        Fps = msBetweenPresents > 0 ? 1000.0 / msBetweenPresents : 0.0
                    };
                    return true;
                }
            }
            catch { }
            return false;
        }

        private string GetPresentMonPath()
        {
            // Busca na raiz do app ou na pasta de DLLs
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DLLS", "PresentMon64.exe");
        }
    }
}
