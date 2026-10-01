using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Shield.Network
{
    public class NetworkMonitorService
    {
        private readonly ILoggingService _logger;
        private readonly NetworkScannerService _scanner;
        private readonly DeviceTrackerService _tracker;
        
        private CancellationTokenSource? _monitoringCts;
        private bool _isMonitoring;
        private bool _lowActivityMode;
        private NetworkRange _networkRange;
        private readonly SemaphoreSlim _scanGate = new(1, 1);
        private Task? _monitoringTask;
        
        // Intervalos de scan: otimizados para tempo real
        private const int NORMAL_SCAN_INTERVAL_MS = 15_000;   // 15 segundos (tempo real)
        private const int GAMER_SCAN_INTERVAL_MS = 30_000;   // 30 segundos (gamer mode)
        
        public bool IsMonitoring => _isMonitoring;
        
        public event EventHandler<MonitoringStatusChangedEventArgs> StatusChanged;
        
        public NetworkMonitorService(
            ILoggingService logger,
            NetworkScannerService scanner,
            DeviceTrackerService tracker)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }
        
        public async Task<bool> StartMonitoringAsync()
        {
            if (_isMonitoring)
                return true;
            
            try
            {
                _logger.LogInfo("[NetworkMonitor] Iniciando monitoramento de rede...");
                
                // Detectar range da rede
                _networkRange = await _scanner.DetectLocalNetworkRangeAsync();
                if (_networkRange == null)
                {
                    _logger.LogWarning("[NetworkMonitor] Não foi possível detectar a rede");
                    return false;
                }
                
                var monitoringCts = new CancellationTokenSource();
                _monitoringCts = monitoringCts;
                _isMonitoring = true;

                _monitoringTask = Task.Run(async () =>
                {
                    try
                    {
                        await MonitoringLoopAsync(monitoringCts.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError("[NetworkMonitor] Erro fatal no monitoramento", ex);
                    }
                });
                
                StatusChanged?.Invoke(this, new MonitoringStatusChangedEventArgs { IsActive = true });
                GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("NetworkMonitoringActivated"));
                _logger.LogSuccess("[NetworkMonitor] Monitoramento iniciado");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkMonitor] Erro ao iniciar monitoramento", ex);
                return false;
            }
        }
        
        public async Task StopMonitoringAsync()
        {
            if (!_isMonitoring && _monitoringTask == null)
                return;

            var monitoringCts = _monitoringCts;
            var monitoringTask = _monitoringTask;
            _monitoringCts = null;
            _monitoringTask = null;

            try
            {
                _logger.LogInfo("[NetworkMonitor] Parando monitoramento...");
                monitoringCts?.Cancel();
                if (monitoringTask != null)
                    await monitoringTask;

                _isMonitoring = false;
                StatusChanged?.Invoke(this, new MonitoringStatusChangedEventArgs { IsActive = false });
                GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("NetworkMonitoringDeactivated"));
                _logger.LogInfo("[NetworkMonitor] Monitoramento parado");
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkMonitor] Erro ao parar monitoramento", ex);
            }
            finally
            {
                monitoringCts?.Dispose();
            }
        }
        
        public async Task<bool> RunManualScanAsync()
        {
            await _scanGate.WaitAsync();
            try
            {
                _logger.LogInfo("[NetworkMonitor] Executando scan manual...");

                if (_networkRange == null)
                {
                    _networkRange = await _scanner.DetectLocalNetworkRangeAsync();
                    if (_networkRange == null)
                        return false;
                }

                var devices = await _scanner.ScanNetworkAsync(_networkRange);
                await _tracker.UpdateDevicesAsync(devices, _networkRange.GatewayIP);

                GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("NetworkScanCompleteDevices"), devices.Count));
                _logger.LogSuccess($"[NetworkMonitor] Scan manual concluído: {devices.Count} dispositivos");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[NetworkMonitor] Erro no scan manual", ex);
                return false;
            }
            finally
            {
                _scanGate.Release();
            }
        }
        
        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            _logger.LogInfo($"[NetworkMonitor] Modo baixa atividade: {enabled} (intervalo: {(enabled ? GAMER_SCAN_INTERVAL_MS / 1000 : NORMAL_SCAN_INTERVAL_MS / 1000)}s)");
        }
        
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await _scanGate.WaitAsync(0, cancellationToken))
                {
                    try { await Task.Delay(1000, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                try
                {
                    var devices = await _scanner.ScanNetworkAsync(_networkRange);
                    await _tracker.UpdateDevicesAsync(devices, _networkRange.GatewayIP);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[NetworkMonitor] Erro no loop de monitoramento", ex);
                }
                finally
                {
                    _scanGate.Release();
                }

                try
                {
                    var interval = _lowActivityMode ? GAMER_SCAN_INTERVAL_MS : NORMAL_SCAN_INTERVAL_MS;
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
    
    public class MonitoringStatusChangedEventArgs : EventArgs
    {
        public bool IsActive { get; set; }
    }
}
