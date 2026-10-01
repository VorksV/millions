using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Cloud;        // DeviceCredentialStore
using VoltrisOptimizer.Services.Enterprise.Models;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Interfaces;


namespace VoltrisOptimizer.Services.Enterprise
{
    /// <summary>
    /// Serviço de comandos remotos do dashboard web (voltris.com.br).
    ///
    /// Implementa "bate na porta > abre" via Server-Sent Events (SSE):
    /// o app abre UMA conexão HTTP longa com /api/v1/commands/stream e
    /// aguarda eventos empurrados pelo servidor. Comandos chegam em menos
    /// de 1 segundo sem nenhum polling ativo.
    ///
    /// Fluxo:
    ///   1. App conecta ao endpoint SSE.
    ///   2. Servidor subscreve ao Supabase Realtime para este machine_id.
    ///   3. Dashboard cria comando → Supabase INSERT → Realtime → SSE → app.
    ///   4. A cada ~5 min o servidor fecha a sessão e envia "event: reconnect".
    ///   5. App reconecta imediatamente (sem janela de perda de evento).
    ///
    /// Custo: 1 invocação Vercel / 5 min (vs. 1/5 min com polling antigo,
    /// porém com latência de entrega < 1 s vs. até 5 min antes).
    ///
    /// HEARTBEAT: timer separado a cada 15 min, sem relação com o SSE.
    /// </summary>
    public sealed class RemoteCommandService : IDisposable
    {
        private const string TAG = "[RemoteCmd]";
        private const string API_BASE = "https://www.voltris.com.br/api/v1";

        // Intervalo do heartbeat (atualiza last_heartbeat no dashboard).
        private const int HEARTBEAT_INTERVAL_MS = 900_000; // 15 minutos

        // Backoff SSE: tempo de espera antes de reconectar após erro de rede.
        // Dobra a cada falha consecutiva, máximo 5 min.
        private const int SSE_RECONNECT_BASE_MS  = 5_000;   // 5 s
        private const int SSE_RECONNECT_MAX_MS   = 300_000; // 5 min

        // Timeout de leitura do stream SSE. O servidor envia pings a cada 25 s;
        // 90 s sem atividade indica que a conexão está morta.
        private static readonly TimeSpan SSE_READ_TIMEOUT = TimeSpan.FromSeconds(90);

        private readonly ILoggingService _logger;
        // _sseClient: timeout infinito — a conexão SSE é mantida aberta intencionalmente.
        private readonly HttpClient _sseClient;
        // _apiClient: 15 s — para heartbeat e atualizações de status (requests normais).
        private readonly HttpClient _apiClient;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public RemoteCommandService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Cliente SSE: timeout infinito (a conexão fica aberta minutos).
            // O timeout por linha é controlado via CancellationToken em ConnectAndReadSseAsync.
            _sseClient = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            _sseClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/event-stream"));

            // Cliente API: timeout de 15 s para heartbeat e atualizações de status.
            _apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            _logger.LogInfo($"{TAG} Serviço SSE de comandos remotos inicializado.");
        }

        public void Start()
        {
            if (_cts != null) return;

            var settings = SettingsService.Instance.Settings;
            if (!settings.IsDeviceLinked || string.IsNullOrEmpty(settings.InstallationId))
            {
                _logger.LogInfo($"{TAG} Dispositivo não vinculado — SSE não iniciado.");
                return;
            }

            _cts = new CancellationTokenSource();
            _ = SseLoopAsync(_cts.Token);
            _ = HeartbeatLoopAsync(_cts.Token);
            _logger.LogInfo($"{TAG} SSE iniciado. Heartbeat a cada {HEARTBEAT_INTERVAL_MS / 60_000} min.");
        }

        public void Stop()
        {
            _logger.LogInfo($"{TAG} Serviço SSE parado.");
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // SSE — loop de conexão e reconexão
        // ══════════════════════════════════════════════════════════════════════

        private async Task SseLoopAsync(CancellationToken ct)
        {
            // Aguarda 5 s antes de conectar para não competir com o boot do app.
            await Task.Delay(5_000, ct).ConfigureAwait(false);

            int consecutiveErrors = 0;

            while (!ct.IsCancellationRequested)
            {
                var reconnectMs = (int)Math.Min(
                    SSE_RECONNECT_BASE_MS * Math.Pow(2, consecutiveErrors),
                    SSE_RECONNECT_MAX_MS);

                try
                {
                    var installationId = SettingsService.Instance.Settings.InstallationId;
                    if (string.IsNullOrWhiteSpace(installationId))
                    {
                        _logger.LogWarning($"{TAG} InstallationId ausente — aguardando 60 s.");
                        await Task.Delay(60_000, ct).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogInfo($"{TAG} Conectando ao stream SSE...");
                    await ConnectAndReadSseAsync(installationId, ct).ConfigureAwait(false);

                    // Chegou aqui: o servidor pediu reconexão limpa (event: reconnect).
                    // Reconecta imediatamente sem backoff.
                    consecutiveErrors = 0;
                    _logger.LogInfo($"{TAG} Sessão SSE encerrada pelo servidor — reconectando.");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogWarning(
                        $"{TAG} Erro SSE #{consecutiveErrors}: {ex.Message}. " +
                        $"Reconectando em {reconnectMs / 1000} s.");

                    try { await Task.Delay(reconnectMs, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.LogInfo($"{TAG} SseLoop encerrado.");
        }

        /// <summary>
        /// Abre a conexão SSE e processa eventos até o servidor fechar ou erro.
        /// Retorna normalmente quando o servidor envia "event: reconnect".
        /// Lança exceção em caso de erro de rede/HTTP.
        /// </summary>
        private async Task ConnectAndReadSseAsync(string installationId, CancellationToken ct)
        {
            var url = $"{API_BASE}/commands/stream?machine_id={Uri.EscapeDataString(installationId)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                "x-correlation-id", CorrelationId.New("VOLTRIS-SSE"));
            AttachCredential(request);

            using var response = await _sseClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode}: {Truncate(err)}");
            }

            using var stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Estado do parser SSE
            string? currentEvent = null;
            var dataBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                // Timeout por linha: se ficar 90 s sem atividade, a conexão está morta.
                using var lineTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lineTimeout.CancelAfter(SSE_READ_TIMEOUT);

                string? line;
                try
                {
                    line = await reader.ReadLineAsync(lineTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout de leitura (não cancelamento externo): conexão morta.
                    throw new IOException("SSE: timeout de leitura — conexão inativa por >90 s.");
                }

                if (line == null) break; // Stream fechado pelo servidor

                if (line.StartsWith(":", StringComparison.Ordinal))
                {
                    // Linha de comentário SSE (ping) — ignora, o timeout foi resetado.
                    continue;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    currentEvent = line["event:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataBuffer.Append(line["data:".Length..].Trim());
                    continue;
                }

                // Linha vazia = fim de um evento SSE
                if (line.Length == 0 && dataBuffer.Length > 0)
                {
                    var data = dataBuffer.ToString();
                    dataBuffer.Clear();

                    if (currentEvent == "reconnect")
                    {
                        // Servidor pedindo reconexão limpa — sai sem erro.
                        currentEvent = null;
                        return;
                    }

                    if (currentEvent == "command" && data != "{}")
                    {
                        await ProcessSseCommandAsync(data).ConfigureAwait(false);
                    }

                    currentEvent = null;
                }
            }
        }

        /// <summary>Deserializa e executa um comando recebido via SSE.</summary>
        private async Task ProcessSseCommandAsync(string json)
        {
            try
            {
                var cmd = JsonSerializer.Deserialize<RemoteCommandModel>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                    });

                if (cmd == null || string.IsNullOrEmpty(cmd.id))
                {
                    _logger.LogWarning($"{TAG} Comando SSE inválido (null ou sem id): {Truncate(json)}");
                    return;
                }

                _logger.LogInfo($"{TAG} ══ Comando recebido via SSE: type={cmd.command_type} id={cmd.id}");
                await ExecuteCommandAsync(cmd).ConfigureAwait(false);
                await UpdateCommandStatusAsync(cmd.id, "completed").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao processar comando SSE: {ex.Message}", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Heartbeat — timer independente do SSE
        // ══════════════════════════════════════════════════════════════════════

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            // Aguarda 15 s antes do primeiro heartbeat para não competir com o boot.
            await Task.Delay(15_000, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                var installationId = SettingsService.Instance.Settings.InstallationId;
                if (!string.IsNullOrEmpty(installationId))
                {
                    await SendHeartbeatAsync(installationId).ConfigureAwait(false);
                }

                try { await Task.Delay(HEARTBEAT_INTERVAL_MS, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>Adiciona credencial de dispositivo ao request, se disponível.</summary>
        private static void AttachCredential(HttpRequestMessage request)
        {
            var credential = DeviceCredentialStore.Current;
            if (!string.IsNullOrEmpty(credential))
                request.Headers.TryAddWithoutValidation("x-voltris-device-credential", credential);
        }

        private async Task<List<RemoteCommandModel>?> FetchPendingCommandsAsync(string installationId)
        {
            try
            {
                var correlationId = CorrelationId.New("VOLTRIS-POLL");
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{API_BASE}/commands/pending?machine_id={Uri.EscapeDataString(installationId)}");
                request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

                using var response = await _apiClient.SendAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogWarning(
                        $"{TAG} [{correlationId}] FetchPending: HTTP {(int)response.StatusCode} - {Truncate(err)}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<RemoteCommandResponseModel>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                    });
                return result?.commands;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} FetchPending erro: {ex.Message}");
                return null;
            }
        }

        private async Task UpdateCommandStatusAsync(string commandId, string status)
        {
            try
            {
                // snake_case: a API le `command_id`. Com CamelCase virava
                // `commandId` e o servidor respondia 400 "Invalid payload",
                // deixando todo comando travado em `pending` para sempre.
                var payload = JsonSerializer.Serialize(
                    new { command_id = commandId, status },
                    App.ApiJsonOptions);

                var correlationId = CorrelationId.New("VOLTRIS-CMD");
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{API_BASE}/commands/update")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

                using var response = await _apiClient.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogSuccess($"{TAG} [{correlationId}] Comando {commandId} marcado como '{status}'.");
                }
                else
                {
                    _logger.LogWarning(
                        $"{TAG} [{correlationId}] Falha ao reportar status do comando {commandId}: " +
                        $"HTTP {(int)response.StatusCode} - {Truncate(body)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao atualizar status do comando {commandId}: {ex.Message}");
            }
        }

        private async Task SendHeartbeatAsync(string installationId)
        {
            var correlationId = CorrelationId.New("VOLTRIS-HB");
            try
            {
                // snake_case: com CamelCase o servidor recebia `installationId` e
                // respondia 400 "Missing installation_id" — o heartbeat nunca
                // chegava ao banco.
                var payload = JsonSerializer.Serialize(new
                {
                    installation_id = installationId,
                    app_version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                    hardware = new
                    {
                        pc_name = Environment.MachineName,
                        os_name = Environment.OSVersion.VersionString,
                        architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
                    }
                }, App.ApiJsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{API_BASE}/install")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

                using var response = await _apiClient.SendAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"{TAG} [{correlationId}] Heartbeat registrado.");
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    // Log em warning (antes era engolido em `catch {}`): era
                    // impossivel saber que o heartbeat estava falhando.
                    _logger.LogWarning(
                        $"{TAG} [{correlationId}] Heartbeat recusado: HTTP {(int)response.StatusCode} - {Truncate(body)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [{correlationId}] Falha de rede no heartbeat: {ex.Message}");
            }
        }

        private static string Truncate(string value, int max = 300)
            => value.Length <= max ? value : value[..max] + "…";

        private async Task ExecuteCommandAsync(RemoteCommandModel cmd)
        {
            _logger.LogInfo($"{TAG} ══════════════════════════════════════════");
            _logger.LogInfo($"{TAG} Executando comando: type={cmd.command_type} id={cmd.id}");
            _logger.LogInfo($"{TAG} ══════════════════════════════════════════");

            try
            {
                switch (cmd.command_type)
                {
                    // ═══ SISTEMA ═══
                    case "optimize":
                    case "quick_optimize":
                        _logger.LogInfo($"{TAG} [Sistema] Executando otimização rápida...");
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                var dashVm = App.Services?.GetService(typeof(VoltrisOptimizer.UI.ViewModels.DashboardViewModel)) as VoltrisOptimizer.UI.ViewModels.DashboardViewModel;
                                if (dashVm?.QuickOptimizeCommand?.CanExecute(null) == true)
                                {
                                    dashVm.QuickOptimizeCommand.Execute("Remote");
                                    _logger.LogSuccess($"{TAG} [Sistema] Otimização rápida disparada via Dashboard.");
                                }
                                else
                                    _logger.LogWarning($"{TAG} [Sistema] DashboardViewModel ou QuickOptimizeCommand indisponível.");
                            }
                            catch (Exception ex) { _logger.LogError($"{TAG} [Sistema] Erro: {ex.Message}", ex); }
                        });
                        break;

                    case "quick_cleanup":
                        _logger.LogInfo($"{TAG} [Limpeza] Executando limpeza rápida...");
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                var dashVm = App.Services?.GetService(typeof(VoltrisOptimizer.UI.ViewModels.DashboardViewModel)) as VoltrisOptimizer.UI.ViewModels.DashboardViewModel;
                                if (dashVm?.QuickCleanupCommand?.CanExecute(null) == true)
                                {
                                    dashVm.QuickCleanupCommand.Execute("Remote");
                                    _logger.LogSuccess($"{TAG} [Limpeza] Limpeza rápida disparada via Dashboard.");
                                }
                            }
                            catch (Exception ex) { _logger.LogError($"{TAG} [Limpeza] Erro: {ex.Message}", ex); }
                        });
                        break;

                    case "shutdown":
                        _logger.LogWarning($"{TAG} [Sistema] DESLIGANDO sistema...");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown", Arguments = "/s /t 10 /c \"Voltris: Comando remoto de desligamento.\"",
                            CreateNoWindow = true, UseShellExecute = false
                        });
                        break;

                    case "restart_link":
                        _logger.LogWarning($"{TAG} [Sistema] REINICIANDO sistema...");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown", Arguments = "/r /t 10 /c \"Voltris: Comando remoto de reinicialização.\"",
                            CreateNoWindow = true, UseShellExecute = false
                        });
                        break;

                    // ═══ LIMPEZA ═══
                    case "cleanup_analyze":
                        _logger.LogInfo($"{TAG} [Limpeza] Analisando sistema...");
                        if (App.UltraCleaner != null)
                        {
                            var analysis = await App.UltraCleaner.AnalyzeAllAsync(null);
                            _logger.LogSuccess($"{TAG} [Limpeza] Análise concluída: {analysis?.TotalFoundSpace / 1024 / 1024}MB recuperáveis.");
                        }
                        else _logger.LogWarning($"{TAG} [Limpeza] UltraCleaner indisponível.");
                        break;

                    case "cleanup_execute":
                        _logger.LogInfo($"{TAG} [Limpeza] Executando limpeza completa...");
                        if (App.UltraCleaner != null)
                        {
                            var analysis = await App.UltraCleaner.AnalyzeAllAsync(null);
                            if (analysis != null)
                            {
                                var allItems = analysis.Categories.SelectMany(c => c.Items).Where(i => i.IsSafe).ToList();
                                await App.UltraCleaner.CleanSelectedAsync(allItems, null);
                                _logger.LogSuccess($"{TAG} [Limpeza] Limpeza completa executada ({allItems.Count} itens).");
                            }
                        }
                        else _logger.LogWarning($"{TAG} [Limpeza] UltraCleaner indisponível.");
                        break;

                    // ═══ REPARO ═══
                    case "repair_dism_sfc":
                        _logger.LogInfo($"{TAG} [Reparo] Executando DISM + SFC...");
                        await Task.Run(() =>
                        {
                            try
                            {
                                // DISM
                                var dism = Process.Start(new ProcessStartInfo
                                {
                                    FileName = "dism.exe", Arguments = "/Online /Cleanup-Image /RestoreHealth",
                                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                                });
                                dism?.WaitForExit(600000); // 10 min timeout
                                _logger.LogInfo($"{TAG} [Reparo] DISM concluído (exit={dism?.ExitCode}).");

                                // SFC
                                var sfc = Process.Start(new ProcessStartInfo
                                {
                                    FileName = "sfc.exe", Arguments = "/scannow",
                                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                                });
                                sfc?.WaitForExit(600000);
                                _logger.LogSuccess($"{TAG} [Reparo] SFC concluído (exit={sfc?.ExitCode}).");
                            }
                            catch (Exception ex) { _logger.LogError($"{TAG} [Reparo] Erro DISM/SFC: {ex.Message}", ex); }
                        });
                        break;

                    case "repair_disk_cleanup":
                        _logger.LogInfo($"{TAG} [Reparo] Iniciando limpeza de disco...");
                        Process.Start(new ProcessStartInfo { FileName = "cleanmgr.exe", UseShellExecute = true });
                        break;

                    case "repair_full":
                        _logger.LogInfo($"{TAG} [Reparo] Reparo completo solicitado remotamente — requer interação local.");
                        _logger.LogWarning($"{TAG} [Reparo] Reparo completo não pode ser executado remotamente (requer confirmação do usuário).");
                        break;

                    // ═══ GAMER ═══
                    case "gamer_mode":
                    case "gamer_activate":
                        _logger.LogInfo($"{TAG} [Gamer] Ativando Modo Gamer...");
                        await Task.Run(async () =>
                        {
                            try
                            {
                                var gamer = App.Services?.GetService(typeof(IGamerModeOrchestrator)) as IGamerModeOrchestrator;
                                if (gamer != null && !gamer.IsActive)
                                {
                                    var options = new GamerOptimizationOptions();
                                    await gamer.ActivateAsync(options, isManual: false);
                                    _logger.LogSuccess($"{TAG} [Gamer] Modo Gamer ativado.");
                                }
                                else if (gamer?.IsActive == true)
                                    _logger.LogInfo($"{TAG} [Gamer] Modo Gamer já está ativo.");
                                else
                                    _logger.LogWarning($"{TAG} [Gamer] IGamerModeOrchestrator indisponível.");
                            }
                            catch (Exception ex) { _logger.LogError($"{TAG} [Gamer] Erro: {ex.Message}", ex); }
                        });
                        break;

                    case "gamer_deactivate":
                        _logger.LogInfo($"{TAG} [Gamer] Desativando Modo Gamer...");
                        await Task.Run(async () =>
                        {
                            try
                            {
                                var gamer = App.Services?.GetService(typeof(IGamerModeOrchestrator)) as IGamerModeOrchestrator;
                                if (gamer != null && gamer.IsActive)
                                {
                                    await gamer.DeactivateAsync();
                                    _logger.LogSuccess($"{TAG} [Gamer] Modo Gamer desativado.");
                                }
                            }
                            catch (Exception ex) { _logger.LogError($"{TAG} [Gamer] Erro: {ex.Message}", ex); }
                        });
                        break;

                    // ═══ REDE ═══
                    case "network_optimize":
                        _logger.LogInfo($"{TAG} [Rede] Otimizando rede completa...");
                        try
                        {
                            if (App.NetworkOptimizer != null)
                            {
                                await App.NetworkOptimizer.FlushDnsAsync();
                                _logger.LogInfo($"{TAG} [Rede] DNS flushed.");
                                await App.NetworkOptimizer.RenewDhcpAsync();
                                _logger.LogInfo($"{TAG} [Rede] DHCP renovado.");
                                await App.NetworkOptimizer.ResetWinsockAsync();
                                _logger.LogInfo($"{TAG} [Rede] Winsock resetado.");
                                _logger.LogSuccess($"{TAG} [Rede] Otimização de rede concluída.");
                            }
                            else _logger.LogWarning($"{TAG} [Rede] NetworkOptimizer indisponível.");
                        }
                        catch (Exception ex) { _logger.LogError($"{TAG} [Rede] Erro: {ex.Message}", ex); }
                        break;

                    case "network_flush_dns":
                        _logger.LogInfo($"{TAG} [Rede] Flush DNS...");
                        if (App.NetworkOptimizer != null) await App.NetworkOptimizer.FlushDnsAsync();
                        _logger.LogSuccess($"{TAG} [Rede] DNS flushed.");
                        break;

                    case "network_reset_winsock":
                        _logger.LogInfo($"{TAG} [Rede] Reset Winsock...");
                        if (App.NetworkOptimizer != null) await App.NetworkOptimizer.ResetWinsockAsync();
                        _logger.LogSuccess($"{TAG} [Rede] Winsock resetado.");
                        break;

                    case "network_reset_tcp":
                        _logger.LogInfo($"{TAG} [Rede] Reset TCP/IP...");
                        if (App.NetworkOptimizer != null) await App.NetworkOptimizer.ResetIPStackAsync();
                        _logger.LogSuccess($"{TAG} [Rede] TCP/IP resetado.");
                        break;

                    // ═══ DESEMPENHO ═══
                    case "performance_optimize":
                        _logger.LogInfo($"{TAG} [Desempenho] Otimização inteligente solicitada remotamente.");
                        _logger.LogInfo($"{TAG} [Desempenho] Comando registrado — será processado pelo sistema.");
                        break;

                    // ═══ SHIELD ═══
                    case "shield_quick_scan":
                        _logger.LogInfo($"{TAG} [Shield] Scan rápido solicitado...");
                        try
                        {
                            var shield = App.Services?.GetService(typeof(Services.Shield.VoltrisShieldService)) as Services.Shield.VoltrisShieldService;
                            if (shield != null)
                            {
                                await shield.RunQuickScanAsync(null);
                                _logger.LogSuccess($"{TAG} [Shield] Scan rápido concluído.");
                            }
                            else _logger.LogWarning($"{TAG} [Shield] VoltrisShieldService indisponível.");
                        }
                        catch (Exception ex) { _logger.LogError($"{TAG} [Shield] Erro: {ex.Message}", ex); }
                        break;

                    case "shield_full_scan":
                        _logger.LogInfo($"{TAG} [Shield] Scan completo solicitado...");
                        try
                        {
                            var shield = App.Services?.GetService(typeof(Services.Shield.VoltrisShieldService)) as Services.Shield.VoltrisShieldService;
                            if (shield != null)
                            {
                                await shield.RunFullScanAsync(null);
                                _logger.LogSuccess($"{TAG} [Shield] Scan completo concluído.");
                            }
                        }
                        catch (Exception ex) { _logger.LogError($"{TAG} [Shield] Erro: {ex.Message}", ex); }
                        break;

                    // ═══ DRIVERS ═══
                    case "drivers_scan":
                        _logger.LogInfo($"{TAG} [Drivers] Scan de drivers solicitado remotamente.");
                        _logger.LogInfo($"{TAG} [Drivers] Comando registrado — requer interação na UI.");
                        break;

                    case "prepare_pc":
                        _logger.LogInfo($"{TAG} [Sistema] Preparar PC — limpeza + otimização leve...");
                        try
                        {
                            if (App.UltraCleaner != null)
                            {
                                var analysis = await App.UltraCleaner.AnalyzeAllAsync(null);
                                if (analysis != null)
                                {
                                    var safeItems = analysis.Categories.SelectMany(c => c.Items).Where(i => i.IsSafe).ToList();
                                    await App.UltraCleaner.CleanSelectedAsync(safeItems, null);
                                }
                            }
                            if (App.NetworkOptimizer != null)
                                await App.NetworkOptimizer.FlushDnsAsync();
                            _logger.LogSuccess($"{TAG} [Sistema] PC preparado.");
                        }
                        catch (Exception ex) { _logger.LogError($"{TAG} [Sistema] Erro: {ex.Message}", ex); }
                        break;

                    default:
                        _logger.LogWarning($"{TAG} Comando desconhecido: {cmd.command_type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao executar comando '{cmd.command_type}': {ex.Message}", ex);
            }

            _logger.LogInfo($"{TAG} Comando '{cmd.command_type}' finalizado.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _sseClient.Dispose();
            _apiClient.Dispose();
            _logger.LogInfo($"{TAG} Serviço disposed.");
        }
    }
}
