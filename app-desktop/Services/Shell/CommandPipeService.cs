using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Shell
{
    public class CommandPipeService : IDisposable
    {
        private const string TAG = "[CommandPipe]";
        private const string PIPE_NAME = "VoltrisOptimizerPipe";

        private readonly ILoggingService? _logger;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private bool _disposed;

        public event Action<string>? OnCommandReceived;

        public CommandPipeService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        public void StartServer()
        {
            if (_cts != null)
            {
                Log("StartServer: servidor já está rodando");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _serverTask = Task.Run(() => PipeServerLoop(token), token);
            Log("Servidor de named pipe iniciado");
        }

        private async Task PipeServerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PIPE_NAME, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                    Log("Aguardando conexão no pipe...");
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    Log("Cliente conectado ao pipe.");

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var command = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(command))
                    {
                        Log($"Comando recebido do pipe: \"{command}\"");

                        // Envia confirmação
                        using var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };
                        await writer.WriteLineAsync($"OK:{command}").ConfigureAwait(false);

                        // Dispara o evento na thread correta
                        try
                        {
                            OnCommandReceived?.Invoke(command);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Erro ao processar comando \"{command}\": {ex.Message}");
                        }
                    }
                    else
                    {
                        Log("Comando vazio recebido, ignorando.");
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("Pipe server cancelado.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    Log("Pipe server disposto.");
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Erro no loop do pipe server: {ex.Message}");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }

        public static async Task<bool> SendCommandAsync(string command, int timeoutMs = 5000)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);
                Log($"Tentando conectar ao pipe existente (timeout={timeoutMs}ms)...");
                await client.ConnectAsync(timeoutMs).ConfigureAwait(false);
                Log("Conectado ao pipe do servidor.");

                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                await writer.WriteLineAsync(command).ConfigureAwait(false);

                using var reader = new StreamReader(client, Encoding.UTF8);
                var response = await reader.ReadLineAsync().ConfigureAwait(false);
                Log($"Resposta do servidor: \"{response}\"");

                return response?.StartsWith("OK:") == true;
            }
            catch (TimeoutException)
            {
                Log($"Timeout ao conectar ao pipe ({timeoutMs}ms) — servidor não está rodando.");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Erro ao enviar comando via pipe: {ex.Message}");
                return false;
            }
        }

        public void StopServer()
        {
            Log("Parando servidor de pipe...");
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _serverTask = null;
            Log("Servidor de pipe parado.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopServer();
            OnCommandReceived = null;
        }

        private static void Log(string message)
        {
            try
            {
                Debug.WriteLine($"{TAG} {message}");
            }
            catch { }
        }

        private static void LogError(string message)
        {
            try
            {
                Debug.WriteLine($"{TAG} {message}");
            }
            catch { }
        }
    }
}
