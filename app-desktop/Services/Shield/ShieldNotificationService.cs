using System;
using System.IO;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Shield.Network;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Serviço que conecta eventos do Voltris Shield ao sistema de notificações.
    ///
    /// A responsabilidade de "o que merece aviso" NÃO é mais desta camada. Ela
    /// roteia. Quem decide é o FileAssessmentPipeline (o arquivo é ameaça?) e o
    /// AlertThrottleService (esta ameaça já foi avisada?).
    ///
    /// A versão anterior decidia aqui apenas por tipo, e o tipo vinha de um
    /// Severity = Medium fixo no produtor. Como `Warning` tinha bypass de cooldown
    /// em duas camadas da cadeia de notificação, cada detecção virava um toast.
    /// </summary>
    public class ShieldNotificationService : IDisposable
    {
        private readonly VoltrisShieldService _shieldService;
        private readonly RansomwareMonitorService _ransomwareMonitor;
        private readonly DeviceTrackerService _deviceTracker;
        private readonly AlertThrottleService _throttle;
        private readonly ILoggingService _logger;
        private bool _disposed;

        public ShieldNotificationService(
            VoltrisShieldService shieldService,
            RansomwareMonitorService ransomwareMonitor,
            DeviceTrackerService deviceTracker,
            ILoggingService logger)
            : this(shieldService, ransomwareMonitor, deviceTracker, logger, throttle: null)
        {
        }

        public ShieldNotificationService(
            VoltrisShieldService shieldService,
            RansomwareMonitorService ransomwareMonitor,
            DeviceTrackerService deviceTracker,
            ILoggingService logger,
            AlertThrottleService? throttle)
        {
            _shieldService = shieldService ?? throw new ArgumentNullException(nameof(shieldService));
            _ransomwareMonitor = ransomwareMonitor ?? throw new ArgumentNullException(nameof(ransomwareMonitor));
            _deviceTracker = deviceTracker ?? throw new ArgumentNullException(nameof(deviceTracker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _throttle = throttle ?? new AlertThrottleService(logger);

            _shieldService.ThreatDetected += OnThreatDetected;
            _shieldService.AdwareDetected += OnAdwareDetected;
            _shieldService.ThreatAlertRaised += OnThreatProtectionAlert;
            _shieldService.FileAssessed += OnFileThreatAssessed;
            _throttle.BurstReady += OnBurstReady;
            _deviceTracker.NewDeviceDetected += OnNewDeviceDetected;
            _deviceTracker.DeviceDisconnected += OnDeviceDisconnectedFromNetwork;
            _ransomwareMonitor.SuspiciousActivityDetected += OnRansomwareAlert;

            _logger.LogInfo("[ShieldNotification] Serviço de notificações do Shield inicializado");
        }

        /// <summary>Limitador em uso, exposto para diagnóstico.</summary>
        public AlertThrottleService Throttle => _throttle;

        private void OnAdwareDetected(object? sender, AdwareDetectedEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) return;

                NotificationManager.ShowCritical(LocalizationService.Instance.GetString("AdwareDetected"), string.Format(LocalizationService.Instance.GetString("UnwantedSoftware"), e.Name));
                _logger.LogInfo($"[ShieldNotification] Notificação de Adware enviada: {e.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao notificar Adware: {ex.Message}");
            }
        }

        private void OnThreatProtectionAlert(object? sender, ThreatAlertEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) return;

                // [FIX:SHIELD-TITULO] A CHAVE ERRADA DEIXAVA O "{0}" NA NOTIFICAÇÃO.
                //
                // Este `switch` é o título da notificação, e ele usava a chave
                // `ThreatDetected` — que é uma FRASE COMPLETA com espaço
                // reservado: "Ameaça detectada: {0}", esperando o nome do
                // processo. Passada ao `GetString` e usada como título, o
                // `{0}` não tinha a quem ser substituído, e ia literal para a
                // tela. O log do usuário mostra o resultado:
                //
                //     [TOAST] Exibindo: Ameaça detectada: {0} | DPI=125%
                //
                // A chave correta para um título é `ThreatDetectedTitle`
                // ("Ameaça Detectada"), que é usada no outro mapa de severidade
                // desta mesma classe — linha 201. As duas rotas mostravam textos
                // diferentes para a mesma gravidade, e só uma delas estava
                // certa.
                //
                // A distinção que importa: `ThreatDetected` (com {0}) é MENSAGEM,
                // e precisa do nome do processo; `ThreatDetectedTitle` é TÍTULO,
                // e é um rótulo fixo. Misturar as duas produz texto quebrado em
                // vez de erro visível — que é por que isso passou despercebido.
                var (title, type) = e.Severity switch
                {
                    ThreatSeverity.Critical => (LocalizationService.Instance.GetString("CriticalThreatDetected"), NotificationType.Error),
                    ThreatSeverity.High => (LocalizationService.Instance.GetString("ThreatDetectedTitle"), NotificationType.Error),
                    _ => (LocalizationService.Instance.GetString("SuspiciousActivity"), NotificationType.Warning)
                };

                NotificationManager.Show(title, $"{e.ThreatType}: {e.ProcessName}", type, critical: true);
                _logger.LogInfo($"[ShieldNotification] Notificação de Proteção enviada: {e.ThreatType} ({e.Severity})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao notificar ThreatProtection alert: {ex.Message}");
            }
        }

        /// <summary>
        /// Rota de ameaça de ARQUIVO, vinda do pipeline de detecção.
        ///
        /// Antes: qualquer `SuspiciousFileDetected` virava um toast imediato, com
        /// `Severity = Medium` fixo no VoltrisShieldService, e os dois cooldowns da
        /// cadeia eram bypassados para `Warning`. Resultado observado no log de
        /// 27/09/2026: 15 toasts em 2 segundos, todos falsos positivos, para DLLs
        /// do Windows Setup e para a DLL assinada do Windscribe.
        ///
        /// Agora a notificação é uma DECISÃO, não um efeito colateral:
        ///   1. o pipeline já filtrou o que não passa de NotifyThreshold;
        ///   2. o AlertThrottleService deduplica por hash de conteúdo;
        ///   3. o token bucket por severidade limita rajadas;
        ///   4. o que for suprimido vira UM alerta de resumo, não N toasts.
        /// </summary>
        private void OnFileThreatAssessed(object? sender, FileThreatEventArgs e)
        {
            try
            {
                var verdict = e.Verdict;

                if (!verdict.ShouldRecord)
                    return;

                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) return;

                // Dedup por CONTEUDO: a mesma ameaça copiada para vários
                // diretórios é um evento, não vários.
                var dedupKey = string.IsNullOrEmpty(verdict.FileHash)
                    ? verdict.FilePath.ToLowerInvariant()
                    : verdict.FileHash;

                var decision = _throttle.Evaluate(dedupKey, verdict.FilePath, verdict.Severity);

                switch (decision)
                {
                    case AlertDecision.Show:
                        var (title, type) = MapSeverity(verdict.Severity);
                        _throttle.MarkNotified(dedupKey);

                        NotificationManager.Show(
                            title,
                            BuildMessage(verdict),
                            type,
                            critical: verdict.Severity >= ThreatSeverity.High);

                        _logger.LogInfo($"[ShieldNotification] Notificação de ameaça enviada: " +
                                        $"{verdict.FileName} (score {verdict.Score}, {verdict.Severity})");
                        return;

                    case AlertDecision.SuppressDuplicate:
                        _logger.LogDebug($"[ShieldNotification] Duplicata suprimida: {verdict.FileName}");
                        return;

                    default:
                        _logger.LogInfo($"[ShieldNotification] Evento agrupado em rajada: {verdict.FileName} " +
                                        $"({verdict.Severity}, score {verdict.Score})");
                        return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao processar veredito de arquivo: {ex.Message}");
            }
        }

        private void OnBurstReady(object? sender, BurstSummary e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) return;

                var message = string.Format(
                    LocalizationService.Instance.GetString("ShieldBurstSummary"),
                    e.TotalEvents,
                    e.DistinctItems,
                    e.DirectoryHint);

                _logger.LogInfo($"[ShieldNotification] Alerta de rajada: {e}");

                GlobalNotificationService.Show(
                    LocalizationService.Instance.GetString("SuspiciousActivity"),
                    message,
                    NotificationType.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao emitir resumo de rajada: {ex.Message}");
            }
        }

        private static (string Title, NotificationType Type) MapSeverity(ThreatSeverity severity) => severity switch
        {
            ThreatSeverity.Critical => (LocalizationService.Instance.GetString("CriticalThreatDetected"), NotificationType.Error),
            ThreatSeverity.High => (LocalizationService.Instance.GetString("ThreatDetectedTitle"), NotificationType.Error),
            ThreatSeverity.Medium => (LocalizationService.Instance.GetString("SuspiciousActivity"), NotificationType.Warning),
            _ => (LocalizationService.Instance.GetString("SecurityAlert"), NotificationType.Info)
        };

        private static string BuildMessage(ThreatVerdict verdict)
        {
            var name = verdict.FileName;
            return verdict.Score > 0
                ? $"{name} (score {verdict.Score}/100, conf. {verdict.Confidence}%)"
                : name;
        }

        private void OnThreatDetected(object? sender, ThreatDetectedEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) return;

                // Eventos sem caminho de arquivo (adware, conexão, comportamento)
                // continuam pela rota legada, mas agora com severidade real vinda
                // do produtor do evento, e passando pelo mesmo limitador.
                if (e.Severity < ThreatSeverity.Medium) return;

                var dedupKey = $"{e.ThreatType}|{e.FilePath}|{e.Severity}";

                var decision = _throttle.Evaluate(dedupKey, e.FilePath, e.Severity);
                if (decision != AlertDecision.Show)
                {
                    _logger.LogDebug($"[ShieldNotification] Evento suprimido ({decision}): {e.ThreatType} {e.FilePath}");
                    return;
                }

                var (title, type) = MapSeverity(e.Severity);
                _throttle.MarkNotified(dedupKey);

                var message = string.IsNullOrEmpty(e.Details)
                    ? $"{e.ThreatType}: {Path.GetFileName(e.FilePath)}"
                    : $"{e.ThreatType}: {Path.GetFileName(e.FilePath)} — {e.Details}";

                NotificationManager.Show(title, message, type, critical: e.Severity >= ThreatSeverity.High);
                _logger.LogInfo($"[ShieldNotification] Notificação de ameaça enviada: {e.ThreatType} ({e.Severity})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao notificar ameaça: {ex.Message}");
            }
        }

        private void OnNewDeviceDetected(object? sender, DeviceEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnNewDevice != true) 
                {
                    _logger.LogInfo($"[ShieldNotification] ⚠️ NOTIFICAÇÕES DE NOVO DISPOSITIVO DESATIVADAS NOS SETTINGS");
                    return;
                }
                
                if (e?.Device == null) 
                {
                    _logger.LogWarning($"[ShieldNotification] ❌ DEVICE ARGUMENT É NULL - IMPOSSÍVEL NOTIFICAR");
                    return;
                }

                _logger.LogInfo($"[ShieldNotification] 🎉 PROCESSANDO NOVO DISPOSITIVO PARA NOTIFICAÇÃO:");
                _logger.LogInfo($"[ShieldNotification] 📡 IP: {e.Device.IPAddress}");
                _logger.LogInfo($"[ShieldNotification] 📡 MAC: {e.Device.MacAddress}");
                _logger.LogInfo($"[ShieldNotification] 📡 Hostname: {e.Device.Hostname}");
                _logger.LogInfo($"[ShieldNotification] 📡 Vendor: {e.Device.Vendor}");
                _logger.LogInfo($"[ShieldNotification] 📡 DeviceType: {e.Device.DeviceType}");
                _logger.LogInfo($"[ShieldNotification] 📡 OS: {e.Device.OperatingSystem}");
                _logger.LogInfo($"[ShieldNotification] 📡 FriendlyName: {e.Device.FriendlyName}");
                _logger.LogInfo($"[ShieldNotification] 📡 Confidence: {e.Device.ConfidenceLevel}%");

                var deviceName = !string.IsNullOrEmpty(e.Device.FriendlyName)
                    ? e.Device.FriendlyName
                    : e.Device.IPAddress;

                var notificationTitle = LocalizationService.Instance.GetString("NewDeviceOnNetwork");
                var notificationMessage = string.Format(LocalizationService.Instance.GetString("DeviceConnectedToNetwork"), deviceName, e.Device.DeviceType);

                _logger.LogInfo($"[ShieldNotification] 🔔 ENVIANDO NOTIFICAÇÃO VISUAL:");
                _logger.LogInfo($"[ShieldNotification] 📋 Título: {notificationTitle}");
                _logger.LogInfo($"[ShieldNotification] 📋 Mensagem: {notificationMessage}");

                // Mesmo glifo e mesma cor de destaque usados na lista da aba Rede,
                // para que a notificação seja reconhecível de imediato.
                var (glyph, accent) = VoltrisOptimizer.UI.Converters.DeviceGlyphResolver.Resolve(
                    new object?[] { e.Device.DeviceType, e.Device.OperatingSystem, e.Device.Vendor });

                GlobalNotificationService.Show(notificationTitle, notificationMessage, NotificationType.Info, glyph, accent);

                _logger.LogSuccess($"[ShieldNotification] ✅ NOTIFICAÇÃO DE NOVO DISPOSITIVO ENVIADA COM SUCESSO!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldNotification] ❌ ERRO CRÍTICO AO NOTIFICAR NOVO DISPOSITIVO", ex);
            }
        }

        /// <summary>
        /// Notificação de dispositivo que DEIXOU de responder na rede.
        /// Não existia antes: o evento era ignorado e o usuário só via a
        /// lista da aba Rede mudar, sem qualquer aviso.
        /// </summary>
        private void OnDeviceDisconnectedFromNetwork(object? sender, DeviceEventArgs e)
        {
            try
            {
                if (e?.Device == null) return;

                var device = e.Device;
                _logger.LogInfo($"[ShieldNotification] Processando desconexão: {device.FriendlyName} ({device.DeviceType}) - {device.IPAddress}");

                // O gateway é a própria rede: não faz sentido avisar que ele "saiu".
                if (device.IsGateway)
                {
                    _logger.LogInfo("[ShieldNotification] Desconexão do gateway ignorada (é a infraestrutura da rede).");
                    return;
                }

                var deviceName = !string.IsNullOrEmpty(device.FriendlyName)
                    ? device.FriendlyName
                    : device.IPAddress;

                var notificationTitle = LocalizationService.Instance.GetString("DeviceDisconnectedFromNetwork");
                var notificationMessage = string.Format(
                    LocalizationService.Instance.GetString("DeviceDisconnectedFromNetworkMsg"),
                    deviceName,
                    device.DeviceType);

                // Mesmo glifo e cor da aba Rede, para o usuário reconhecer o dispositivo.
                var (glyph, accent) = VoltrisOptimizer.UI.Converters.DeviceGlyphResolver.Resolve(
                    new object?[] { device.DeviceType, device.OperatingSystem, device.Vendor });

                GlobalNotificationService.Show(notificationTitle, notificationMessage, NotificationType.Warning, glyph, accent);

                _logger.LogSuccess($"[ShieldNotification] Notificação de desconexão enviada: {deviceName}");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldNotification] Erro ao notificar dispositivo desconectado", ex);
            }
        }

        private void OnRansomwareAlert(object? sender, RansomwareAlertEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnThreatDetected != true) 
                {
                    _logger.LogInfo($"[ShieldNotification] ⚠️ NOTIFICAÇÕES DE AMEAÇAS DESATIVADAS NOS SETTINGS");
                    return;
                }

                _logger.LogInfo($"[ShieldNotification] 🚨 PROCESSANDO ALERTA DE RANSOMWARE PARA NOTIFICAÇÃO:");
                _logger.LogInfo($"[ShieldNotification] 📋 Alert Type: {e.AlertType}");
                _logger.LogInfo($"[ShieldNotification] 📡 Details: {e.Details}");

                var notificationTitle = LocalizationService.Instance.GetString("RansomwareAlert");
                var notificationMessage = string.Format(LocalizationService.Instance.GetString("RansomwareAlertDetails"), e.AlertType, e.Details);

                _logger.LogInfo($"[ShieldNotification] 🔔 ENVIANDO NOTIFICAÇÃO VISUAL:");
                _logger.LogInfo($"[ShieldNotification] 📋 Título: {notificationTitle}");
                _logger.LogInfo($"[ShieldNotification] 📋 Mensagem: {notificationMessage}");
                
                NotificationManager.Show(notificationTitle, notificationMessage, NotificationType.Error, critical: true);

                _logger.LogSuccess($"[ShieldNotification] ✅ NOTIFICAÇÃO DE RANSOMWARE ENVIADA COM SUCESSO!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldNotification] ❌ ERRO CRÍTICO AO NOTIFICAR RANSOMWARE", ex);
            }
        }

        /// <summary>
        /// Envia notificação de concluso de scan (chamado externamente pelo ShieldViewModel).
        /// </summary>
        public void NotifyScanComplete(ScanResult result)
        {
            try
            {
                var settings = SettingsService.Instance?.Settings;
                if (settings?.NotifyOnScanComplete != true) return;

                var scanName = result.ScanType switch
                {
                    ScanType.Quick => LocalizationService.Instance.GetString("QuickScan"),
                    ScanType.Full => LocalizationService.Instance.GetString("FullScan"),
                    ScanType.Adware => LocalizationService.Instance.GetString("AdwareScan"),
                    _ => LocalizationService.Instance.GetString("Verification")
                };

                var type = result.ThreatsFound > 0 ? NotificationType.Warning : NotificationType.Success;
                var message = result.ThreatsFound > 0
                    ? string.Format(LocalizationService.Instance.GetString("ThreatsFound"), result.ThreatsFound)
                    : LocalizationService.Instance.GetString("NoThreatsFound");

                NotificationManager.Show(string.Format(LocalizationService.Instance.GetString("ScanCompleted"), scanName), message, type);
                if (type == NotificationType.Success)
                    GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("ScanCompleted"), scanName) + ": " + message);
                else
                    GlobalNotificationService.ShowWarning("Shield", string.Format(LocalizationService.Instance.GetString("ScanCompleted"), scanName) + ": " + message);
                _logger.LogInfo($"[ShieldNotification] Notificação de scan: {scanName} - {result.ThreatsFound} ameaças");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldNotification] Erro ao notificar scan: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _shieldService.ThreatDetected -= OnThreatDetected;
            _shieldService.AdwareDetected -= OnAdwareDetected;
            _shieldService.ThreatAlertRaised -= OnThreatProtectionAlert;
            _shieldService.FileAssessed -= OnFileThreatAssessed;
            _throttle.BurstReady -= OnBurstReady;
            _deviceTracker.NewDeviceDetected -= OnNewDeviceDetected;
            _deviceTracker.DeviceDisconnected -= OnDeviceDisconnectedFromNetwork;
            _ransomwareMonitor.SuspiciousActivityDetected -= OnRansomwareAlert;

            _throttle.Dispose();

            _logger.LogInfo("[ShieldNotification] Serviço de notificações do Shield disposed");
        }
    }
}
