using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// Coletor de métricas do sistema para o overlay OSD
    /// CORREÇÃO FINAL: Usa ProfessionalHardwareMonitor para métricas 100% precisas
    /// + Fallback para GlobalThermalMonitorService quando sensores não disponíveis
    /// </summary>
    public class MetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly ILoggingService? _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _collectionTask;
        private readonly MetricsData _currentMetrics = new MetricsData();
        
        // CORREÇÃO FINAL: Usar SystemMetricsCache (V2 Architecture - zero polling)
        // ❌ REMOVIDO: ProfessionalHardwareMonitor desativado
        
        // MELHORIA: RealTimeFpsCapture para leitura precisa de FPS
        private IEtwFrameTimeMonitor? _fpsMonitor;
        private FrameMetrics? _latestFpsMetrics;
        
        // CORREÇÃO: Referência ao ThermalMonitorService para fallback de temperatura
        private readonly VoltrisOptimizer.Services.Thermal.IThermalMonitorService? _thermalMonitor;
        
        private bool _disposed = false;
        private int _loopIteration = 0;
        
        public event EventHandler<MetricsData>? MetricsUpdated;
        
        public MetricsData CurrentMetrics => _currentMetrics;
        
        public MetricsData GetCurrentMetrics() => _currentMetrics;
        
        public MetricsCollector(ILoggingService? logger = null, VoltrisOptimizer.Services.Thermal.IThermalMonitorService? thermalMonitor = null)
        {
            _logger?.LogEntry(nameof(MetricsCollector));
            _logger?.LogInfo("[MetricsCollector] ENTER: Constructor");
            _logger = logger;
            _thermalMonitor = thermalMonitor ?? App.ThermalMonitorService;
            _logger?.LogInfo("[MetricsCollector] EXIT: Constructor");
            _logger?.LogExit(nameof(MetricsCollector));
        }
        
        public async Task StartAsync(int gameProcessId, CancellationToken cancellationToken = default)
        {
            _logger?.LogEntry(nameof(StartAsync));
            _logger?.LogInfo("[MetricsCollector] ENTER: StartAsync");
            if (_disposed) throw new ObjectDisposedException(nameof(MetricsCollector));
            
            _logger?.LogInfo("[MetricsCollector] Iniciando coleta de métricas...");
            
            try
            {
                // Obter processo do jogo
                var gameProcess = Process.GetProcessById(gameProcessId);
                
                // ❌ REMOVIDO: ProfessionalHardwareMonitor desativado (V2 Architecture)
                // Usar SystemMetricsCache (reativo, zero polling)
                _logger?.LogInfo("[MetricsCollector] ✅ Usando SystemMetricsCache (V2 Architecture)");
                
                // Inicializar IEtwFrameTimeMonitor (FPS)
                try
                {
                    _fpsMonitor = ServiceLocator.GetRequiredService<IEtwFrameTimeMonitor>();
                    _fpsMonitor.MetricsUpdated += OnFpsMetricsUpdated;
                    await _fpsMonitor.StartAsync();
                    _logger?.LogSuccess("[MetricsCollector] ✅ IEtwFrameTimeMonitor inicializado");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[MetricsCollector] ⚠️ Erro ao inicializar IEtwFrameTimeMonitor: {ex.Message}");
                }
                
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                // Iniciar Task de coleta destacada
                _collectionTask = Task.Run(() => CollectionLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                
                _logger?.LogSuccess("[MetricsCollector] ✅ Coleta de métricas iniciada");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[MetricsCollector] ❌ Erro ao iniciar coleta: {ex.Message}", ex);
                throw;
            }
            finally
            {
                _logger?.LogInfo("[MetricsCollector] EXIT: StartAsync");
                _logger?.LogExit(nameof(StartAsync));
            }
        }
        
        public async Task StopAsync()
        {
            _logger?.LogEntry(nameof(StopAsync));
            _logger?.LogInfo("[MetricsCollector] ENTER: StopAsync");
            if (_disposed)
            {
                _logger?.LogInfo("[MetricsCollector] EXIT: StopAsync (already disposed)");
                return;
            }
            
            _logger?.LogInfo("[MetricsCollector] Parando coleta de métricas...");
            
            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_collectionTask != null)
                {
                    await _collectionTask;
                    _collectionTask = null;
                }
                
                 // Parar monitor de FPS (não disposicionar singleton)
                 if (_fpsMonitor != null)
                 {
                     _fpsMonitor.MetricsUpdated -= OnFpsMetricsUpdated;
// Não chamar Stop ou Dispose, pois o monitor pode ser compartilhado
                     _fpsMonitor = null;
                  }
                
                // ❌ REMOVIDO: ProfessionalHardwareMonitor desativado
                // _hardwareMonitor?.Dispose();
                
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                _disposed = true;
                
                _logger?.LogSuccess("[MetricsCollector] ✅ Coleta de métricas parada");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[MetricsCollector] ❌ Erro ao parar coleta: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogInfo("[MetricsCollector] EXIT: StopAsync");
                _logger?.LogExit(nameof(StopAsync));
            }
        }
        
        private void OnFpsMetricsUpdated(object? sender, FrameMetrics metrics)
        {
            _logger?.LogEntry(nameof(OnFpsMetricsUpdated));
            _logger?.LogDebug("[MetricsCollector] ENTER: OnFpsMetricsUpdated");
            _latestFpsMetrics = metrics;
            _logger?.LogDebug("[MetricsCollector] EXIT: OnFpsMetricsUpdated");
            _logger?.LogExit(nameof(OnFpsMetricsUpdated));
        }

        private async Task CollectionLoopAsync(CancellationToken cancellationToken)
        {
            _logger?.LogEntry(nameof(CollectionLoopAsync));
            _logger?.LogInfo("[MetricsCollector] ENTER: CollectionLoopAsync");
            _logger?.LogInfo("[MetricsCollector] Iniciando loop de coleta de métricas...");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CollectAllMetrics(cancellationToken);
                    
                    // Notificar ouvintes
                    MetricsUpdated?.Invoke(this, _currentMetrics);
                    
                    // Aguardar próximo ciclo
                    await Task.Delay(250, cancellationToken); // 4Hz
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[MetricsCollector] Erro no loop de coleta: {ex.Message}", ex);
                    await Task.Delay(1000, cancellationToken); // Esperar 1s em caso de erro
                }
                
                _loopIteration++;
            }
            
            _logger?.LogInfo($"[MetricsCollector] Loop de coleta encerrado após {_loopIteration} iterações");
            _logger?.LogInfo("[MetricsCollector] EXIT: CollectionLoopAsync");
            _logger?.LogExit(nameof(CollectionLoopAsync));
        }
        
        private async Task CollectAllMetrics(CancellationToken cancellationToken)
        {
            _logger?.LogEntry(nameof(CollectAllMetrics));
            _logger?.LogDebug("[MetricsCollector] ENTER: CollectAllMetrics");
            try
            {
                // ✅ V2 Architecture: Coletar métricas do SystemMetricsCache (zero polling)
                var cache = SystemMetricsCache.Instance;
                
                // Mapear para o formato MetricsData
                _currentMetrics.CpuUsagePercent = cache.CpuPercent;
                _currentMetrics.CpuTemperature = cache.Hardware.CpuTemperature;
                _currentMetrics.CpuClockMhz = cache.CpuClockMhz;
                _currentMetrics.GpuUsagePercent = cache.GpuUsagePercent;
                _currentMetrics.GpuTemperature = cache.Hardware.GpuTemperature;
                _currentMetrics.GpuCoreClockMhz = cache.GpuCoreClockMhz;
                _currentMetrics.RamUsagePercent = cache.MemoryUsedPercent;
                
                _logger?.LogInfo($"[MetricsCollector]   CPU: {_currentMetrics.CpuUsagePercent:F1}% @ {_currentMetrics.CpuClockMhz:F0}MHz ({_currentMetrics.CpuTemperature:F0}°C)");
                _logger?.LogInfo($"[MetricsCollector]   GPU: {_currentMetrics.GpuUsagePercent:F1}% @ {_currentMetrics.GpuCoreClockMhz:F0}MHz ({_currentMetrics.GpuTemperature:F0}°C)");
                
                // Coletar FPS do IEtwFrameTimeMonitor
                if (_latestFpsMetrics != null)
                {
                    _currentMetrics.Fps = _latestFpsMetrics.CurrentFps;
                    _currentMetrics.FrameTimeMs = _latestFpsMetrics.CurrentFps > 0 ? 1000.0 / _latestFpsMetrics.CurrentFps : 0;
                    
                    // Log de debug a cada ~5 segundos
                    if (_loopIteration % 50 == 1)
                    {
                        _logger?.LogInfo($"[MetricsCollector]   FPS: {_currentMetrics.Fps:F1} | Frame: {_currentMetrics.FrameTimeMs:F2}ms");
                    }
                }
                
                // Aplicar thermal fallback se necessário
                await ApplyThermalFallbackAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[MetricsCollector] Erro ao coletar métricas: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogDebug("[MetricsCollector] EXIT: CollectAllMetrics");
                _logger?.LogExit(nameof(CollectAllMetrics));
            }
        }
        
        private async Task ApplyThermalFallbackAsync()
        {
            _logger?.LogEntry(nameof(ApplyThermalFallbackAsync));
            _logger?.LogDebug("[MetricsCollector] ENTER: ApplyThermalFallbackAsync");
            try
            {
                if (_thermalMonitor != null && 
                    (_currentMetrics.CpuTemperature == null || _currentMetrics.GpuTemperature == null))
                {
                    var thermalMetrics = await _thermalMonitor.GetCurrentMetricsAsync();
                    
                    if (_currentMetrics.CpuTemperature == null && thermalMetrics.CpuTemperature > 0)
                    {
                        _currentMetrics.CpuTemperature = thermalMetrics.CpuTemperature;
                    }
                    
                    if (_currentMetrics.GpuTemperature == null && thermalMetrics.GpuTemperature > 0)
                    {
                        _currentMetrics.GpuTemperature = thermalMetrics.GpuTemperature;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[MetricsCollector] Erro ao aplicar thermal fallback: {ex.Message}");
            }
            finally
            {
                _logger?.LogDebug("[MetricsCollector] EXIT: ApplyThermalFallbackAsync");
                _logger?.LogExit(nameof(ApplyThermalFallbackAsync));
            }
        }
        
        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _logger?.LogInfo("[MetricsCollector] ENTER: Dispose");
            if (_disposed)
            {
                _logger?.LogInfo("[MetricsCollector] EXIT: Dispose (already disposed)");
                _logger?.LogExit(nameof(Dispose));
                return;
            }
            
            _ = StopAsync();
            GC.SuppressFinalize(this);
            _logger?.LogInfo("[MetricsCollector] EXIT: Dispose");
            _logger?.LogExit(nameof(Dispose));
        }
    }
}
