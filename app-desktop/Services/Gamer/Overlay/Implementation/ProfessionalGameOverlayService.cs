using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;
using VoltrisOptimizer.UI.Overlay;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisOptimizer.Services.Monitoring.Implementation;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// SERVIÇO PROFISSIONAL DE OVERLAY PARA JOGOS
    /// Implementa captura REAL de FPS e hardware via hooks DirectX
    /// Design profissional igual MSI Afterburner/FRAPS/RivaTuner
    /// </summary>
    public class ProfessionalGameOverlayService : IOverlayService
    {
        private readonly ILoggingService? _logger;
        private ProfessionalGameOverlayWindow? _overlayWindow;
        private OverlaySettings _settings;
        private bool _isActive = false;
        private int _currentGameProcessId = 0;
        private readonly string _settingsPath;
        private readonly object _lock = new object();
        private System.Threading.Timer? _overlayDebounceTimer;
        private OverlaySettings? _pendingSettings;
        
        // Componentes profissionais
        // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture - usar SystemMetricsCache)
        private IEtwFrameTimeMonitor? _fpsMonitor;
        private IMetricToggleService? _metricToggles;
        
        // Eventos da interface
        public event EventHandler? OverlayActivated;
        public event EventHandler? OverlayDeactivated;
        public event EventHandler? SettingsUpdated;
        
        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _isActive && _overlayWindow != null;
                }
            }
        }

        public OverlaySettings Settings
        {
            get
            {
                lock (_lock)
                {
                    return _settings;
                }
            }
        }

        public ProfessionalGameOverlayService(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(ProfessionalGameOverlayService));
            try
            {
                _logger = logger;
                
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoltrisOptimizer"
                );
                _settingsPath = Path.Combine(appDataPath, "GamerOverlaySettings.json");

                _logger?.LogInfo("[PRO-GAME-OVERLAY] 🎮 Inicializando Overlay Profissional para Jogos com DirectX Hooks");

                // Inicializar SmartMetricToggleService para controle inteligente de métricas
                _metricToggles = new SmartMetricToggleService(logger);
                _logger?.LogInfo("[PRO-GAME-OVERLAY] ✔️ SmartMetricToggleService ativo — coleta inteligente por métrica habilitada");
                
                LoadSettingsDirect();
                
                // Garantir configurações profissionais para jogos
                EnsureProfessionalGameSettings();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[PRO-GAME-OVERLAY] Erro ao carregar configurações: {ex.Message}");
                _settings = CreateProfessionalGameSettings();
            }
            _logger?.LogExit(nameof(ProfessionalGameOverlayService));
        }
        
        public async Task<bool> StartAsync(int gameProcessId, CancellationToken cancellationToken = default)
        {
            _logger?.LogEntry(nameof(StartAsync));
            try
            {
                _logger?.LogInfo($"[PRO-GAME-OVERLAY] ════════════════════════════════");
                _logger?.LogInfo($"[PRO-GAME-OVERLAY] 🎮 INICIANDO OVERLAY PROFISSIONAL PARA JOGOS - PID: {gameProcessId}");
                
                lock (_lock)
                {
                    if (_isActive && _currentGameProcessId == gameProcessId)
                    {
                        _logger?.LogInfo("[PRO-GAME-OVERLAY] ✅ Overlay já está ativo para este processo");
                        return true;
                    }

                    // Verificar se precisa parar overlay anterior
                    bool needsStop = _isActive;
                    
                    if (needsStop)
                    {
                        _logger?.LogInfo("[PRO-GAME-OVERLAY] 🛑 Parando overlay anterior antes de iniciar novo");
                        // Sair do lock antes de chamar async
                    }
                    
                    // Verificar se o processo existe
                    if (!IsProcessRunning(gameProcessId))
                    {
                        _logger?.LogWarning($"[PRO-GAME-OVERLAY] ⚠️ Processo {gameProcessId} não está em execução");
                        return false;
                    }

                    _currentGameProcessId = gameProcessId;
                }
                
                // Parar overlay anterior fora do lock se necessário
                if (_isActive)
                {
                    await StopAsync();
                }

                // Iniciar componentes profissionais
                _logger?.LogInfo("[PRO-GAME-OVERLAY] ✅ Usando IEtwFrameTimeMonitor para FPS (unificado)");
                // _fpsCapture initialization removed - using IEtwFrameTimeMonitor
                // fpsStarted = await _fpsMonitor?.StartAsync(); // No need, monitor already started
                // _logger?.LogInfo($"[PRO-GAME-OVERLAY] Captura FPS iniciada: {fpsStarted}"); // No separate FPS capture
                
                _logger?.LogInfo("[PRO-GAME-OVERLAY] 📊 Iniciando monitoramento de hardware via SystemMetricsCache (reativo, zero polling)...");
                // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture)
                // _hardwareMonitor = new GameHardwareMonitor(_logger);
                // var hwStarted = await _hardwareMonitor.StartAsync();
                _logger?.LogInfo("[PRO-GAME-OVERLAY] ✅ Hardware monitoring via SystemMetricsCache (zero loops)");
                
                // Criar janela de overlay profissional para jogos
                _logger?.LogInfo("[PRO-GAME-OVERLAY] 🖥️ Criando janela de overlay profissional para jogos...");
                _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 SmartMetricToggles: {( _metricToggles != null ? "✔️ ativo" : "❌ não disponível" )}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // ❌ REMOVIDO: GameHardwareMonitor desativado (V2 Architecture)
                    _overlayWindow = new ProfessionalGameOverlayWindow(_settings, _logger, _metricToggles);
                    _overlayWindow.Show();
                });

                lock (_lock)
                {
                    _isActive = true;
                }

                _logger?.LogSuccess($"[PRO-GAME-OVERLAY] ✅ Overlay profissional para jogos iniciado para processo {gameProcessId}");
                OverlayActivated?.Invoke(this, EventArgs.Empty);

                _logger?.LogExit(nameof(StartAsync), true);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ Erro ao iniciar overlay: {ex.Message}", ex);
                _logger?.LogExit(nameof(StartAsync), false);
                return false;
            }
        }

        public async Task StopAsync()
        {
            _logger?.LogEntry(nameof(StopAsync));
            try
            {
                lock (_lock)
                {
                    if (!_isActive)
                    {
                        _logger?.LogInfo("[PRO-GAME-OVERLAY] StopAsync chamado mas overlay não está ativo");
                        return;
                    }

                    _logger?.LogInfo("[PRO-GAME-OVERLAY] 🛑 Parando overlay profissional para jogos...");
                    _isActive = false;
                    _currentGameProcessId = 0;
                }

                // Parar componentes profissionais
                // No separate FPS capture to stop
                // ❌ REMOVIDO: GameHardwareMonitor desativado
                // _hardwareMonitor?.Stop();

                // Fechar janela de overlay
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    try 
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (_overlayWindow != null)
                            {
                                try { _overlayWindow.Close(); } catch { }
                                _overlayWindow = null;
                            }
                        });
                    } 
                    catch (TaskCanceledException) { }
                    catch (Exception winEx) { _logger?.LogWarning($"[PRO-GAME-OVERLAY] Aviso ao fechar janela: {winEx.Message}"); }
                }

                _logger?.LogSuccess("[PRO-GAME-OVERLAY] ✅ Overlay profissional para jogos parado com sucesso");
                OverlayDeactivated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ Erro ao parar overlay: {ex.Message}", ex);
            }
            _logger?.LogExit(nameof(StopAsync));
        }

        public void UpdateSettings(OverlaySettings settings)
        {
            _logger?.LogEntry(nameof(UpdateSettings));
            _pendingSettings = settings;
            _overlayDebounceTimer?.Dispose();
            _overlayDebounceTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var m = settings?.Metrics;
                    _logger?.LogInfo("[PRO-GAME-OVERLAY] ════════════════════════════════════════");
                    _logger?.LogInfo("[PRO-GAME-OVERLAY] 🔧 Atualizando configurações do overlay profissional para jogos...");
                    _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 _overlayWindow: {(_overlayWindow != null ? "✔️ ativo" : "❌ null")}");
                    _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 FPS={m?.ShowFps}, FrameTime={m?.ShowFrameTime}");
                    _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 CPU={m?.ShowCpuUsage}/{m?.ShowCpuTemperature}/{m?.ShowCpuClock}");
                    _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 GPU={m?.ShowGpuUsage}/{m?.ShowGpuTemperature}/{m?.ShowGpuClock}");
                    _logger?.LogInfo($"[PRO-GAME-OVERLAY] 📊 RAM={m?.ShowRamUsage}, VRAM={m?.ShowVramUsage}");

                    // CORREÇÃO: Sincronizar SmartMetricToggleService com os settings reais
                    if (m != null && _metricToggles != null)
                    {
                        _logger?.LogInfo($"[PRO-GAME-OVERLAY] 🔄 Sincronizando SmartMetricToggleService — ShowFps={m.ShowFps}, ShowFrameTime={m.ShowFrameTime}, ShowCpuClock={m.ShowCpuClock}, ShowGpuClock={m.ShowGpuClock}, ShowInputLatency={m.ShowInputLatency}");
                        _metricToggles.SetEnabled(MetricType.Fps, m.ShowFps);
                        _metricToggles.SetEnabled(MetricType.FrameTime, m.ShowFrameTime);
                        _metricToggles.SetEnabled(MetricType.CpuUsage, m.ShowCpuUsage);
                        _metricToggles.SetEnabled(MetricType.CpuTemperature, m.ShowCpuTemperature);
                        _metricToggles.SetEnabled(MetricType.CpuClock, m.ShowCpuClock);
                        _metricToggles.SetEnabled(MetricType.GpuUsage, m.ShowGpuUsage);
                        _metricToggles.SetEnabled(MetricType.GpuTemperature, m.ShowGpuTemperature);
                        _metricToggles.SetEnabled(MetricType.GpuClock, m.ShowGpuClock);
                        _metricToggles.SetEnabled(MetricType.GpuMemoryClock, m.ShowGpuClock);
                        _metricToggles.SetEnabled(MetricType.RamUsage, m.ShowRamUsage);
                        _metricToggles.SetEnabled(MetricType.VramUsage, m.ShowVramUsage);
                        _metricToggles.SetEnabled(MetricType.InputLatency, m.ShowInputLatency);
                        _logger?.LogSuccess("[PRO-GAME-OVERLAY] ✅ SmartMetricToggleService sincronizado com overlay settings");

                        // Verificação pós-sync
                        bool fpsOk = _metricToggles.IsEnabled(MetricType.Fps);
                        bool cpuClockOk = _metricToggles.IsEnabled(MetricType.CpuClock);
                        bool gpuClockOk = _metricToggles.IsEnabled(MetricType.GpuClock);
                        bool latOk = _metricToggles.IsEnabled(MetricType.InputLatency);
                        _logger?.LogInfo($"[PRO-GAME-OVERLAY] 🔍 Pós-sync: Fps={fpsOk}, CpuClock={cpuClockOk}, GpuClock={gpuClockOk}, InputLatency={latOk}");
                    }

                    lock (_lock)
                    {
                        var s = _pendingSettings ?? settings;
                        _settings = s;
                        
                        if (_overlayWindow != null)
                        {
                            _logger?.LogInfo("[PRO-GAME-OVERLAY] ➡️ Chamando _overlayWindow.UpdateSettings() via Dispatcher");
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                try
                                {
                                    _overlayWindow.UpdateSettings(s);
                                    _logger?.LogSuccess("[PRO-GAME-OVERLAY] ✅ overlayWindow.UpdateSettings() executado com sucesso");
                                }
                                catch (Exception winEx)
                                {
                                    _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ overlayWindow.UpdateSettings() lançou exceção: {winEx.Message}", winEx);
                                }
                            });
                        }
                        else
                        {
                            _logger?.LogWarning("[PRO-GAME-OVERLAY] ⚠️ _overlayWindow é null — configurações salvas no service mas não aplicadas na UI");
                        }
                    }
                    
                    SettingsUpdated?.Invoke(this, EventArgs.Empty);
                    _logger?.LogSuccess("[PRO-GAME-OVERLAY] ✅ Configurações atualizadas com sucesso");
                }
                catch (Exception ex)
                {
                _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ Erro ao atualizar configurações: {ex.Message}", ex);
            }
            _logger?.LogExit(nameof(UpdateSettings));
            }, null, 300, System.Threading.Timeout.Infinite);
        }

        private void LoadSettingsDirect()
        {
            _logger?.LogEntry(nameof(LoadSettingsDirect));
            try
            {
                lock (_lock)
                {
                    _settings = OverlaySettings.LoadFromFile(_settingsPath);
                }
            }
            catch
            {
                _settings = CreateProfessionalGameSettings();
            }
            _logger?.LogExit(nameof(LoadSettingsDirect));
        }

        public async Task LoadSettingsAsync()
        {
            _logger?.LogEntry(nameof(LoadSettingsAsync));
            try
            {
                _logger?.LogInfo("[PRO-GAME-OVERLAY] 📁 Carregando configurações profissionais para jogos...");
                
                lock (_lock)
                {
                    _settings = OverlaySettings.LoadFromFile(_settingsPath);
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ Erro ao carregar configurações: {ex.Message}", ex);
                _settings = CreateProfessionalGameSettings();
            }
            _logger?.LogExit(nameof(LoadSettingsAsync));
        }

        public async Task SaveSettingsAsync()
        {
            _logger?.LogEntry(nameof(SaveSettingsAsync));
            try
            {
                _logger?.LogInfo("[PRO-GAME-OVERLAY] 💾 Salvando configurações profissionais para jogos...");
                
                lock (_lock)
                {
                    if (_settings != null)
                    {
                        _settings.SaveToFile();
                    }
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-GAME-OVERLAY] ❌ Erro ao salvar configurações: {ex.Message}", ex);
            }
            _logger?.LogExit(nameof(SaveSettingsAsync));
        }

        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _ = StopAsync();
            _logger?.LogExit(nameof(Dispose));
        }
        
        #region Métodos Auxiliares
        
        private bool IsProcessRunning(int processId)
        {
            _logger?.LogEntry(nameof(IsProcessRunning));
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                _logger?.LogExit(nameof(IsProcessRunning), false);
                return false;
            }
            _logger?.LogExit(nameof(IsProcessRunning));
        }
        
        private void EnsureProfessionalGameSettings()
        {
            _logger?.LogEntry(nameof(EnsureProfessionalGameSettings));
            // CORREÇÃO: Respeitar configurações salvas pelo usuário — NÃO sobrescrever!
            // Apenas definir defaults se as propriedades NÃO foram carregadas do disco.
            bool hasSavedSettings = _settings.Metrics.ShowFps || _settings.Metrics.ShowCpuUsage || 
                                     _settings.Metrics.ShowGpuUsage || _settings.Metrics.ShowRamUsage;
            
            if (!hasSavedSettings)
            {
                _logger?.LogInfo("[PRO-GAME-OVERLAY] 🔧 Aplicando configurações padrão (primeira execução)");
                
                // Garantir que overlay inicie automaticamente com jogos
                _settings.StartWithGame = true;
                
                // Garantir que métricas profissionais para jogos estejam ativas
                _settings.Metrics.ShowFps = true;
                _settings.Metrics.ShowFrameTime = true;
                _settings.Metrics.ShowCpuUsage = true;
                _settings.Metrics.ShowGpuUsage = true;
                _settings.Metrics.ShowRamUsage = true;
                _settings.Metrics.ShowVramUsage = true;
                _settings.Metrics.ShowCpuTemperature = true;
                _settings.Metrics.ShowGpuTemperature = true;
                _settings.Metrics.ShowCpuClock = true;
                _settings.Metrics.ShowGpuClock = true;
                _settings.Metrics.ShowInputLatency = true;
                
                // Configurações específicas para jogos
                _settings.PositionX = 100;
                _settings.PositionY = 100;
                _settings.Opacity = 0.9;
                
                _logger?.LogInfo("[PRO-GAME-OVERLAY] ✅ Configurações padrão aplicadas");
            }
            else
            {
                _logger?.LogInfo("[PRO-GAME-OVERLAY] ✅ Configurações carregadas do disco — preservando ajustes do usuário");
            }
            _logger?.LogExit(nameof(EnsureProfessionalGameSettings));
        }
        
        private OverlaySettings CreateProfessionalGameSettings()
        {
            _logger?.LogEntry(nameof(CreateProfessionalGameSettings));
            _logger?.LogExit(nameof(CreateProfessionalGameSettings));
            return new OverlaySettings
            {
                IsEnabled = true,
                StartWithGame = true,
                ShowFps = true,
                ShowCpu = true,
                ShowGpu = true,
                ShowMemory = true,
                ShowTemperature = true,
                PositionX = 100,
                PositionY = 100,
                Opacity = 0.9,
                Metrics = new OverlayMetrics
                {
                    ShowFps = true,
                    ShowFrameTime = true,
                    ShowCpuUsage = true,
                    ShowGpuUsage = true,
                    ShowRamUsage = true,
                    ShowVramUsage = true,
                    ShowCpuTemperature = true,
                    ShowGpuTemperature = true,
                    ShowCpuClock = true,
                    ShowGpuClock = true,
                    ShowInputLatency = true
                }
            };
        }
        
        #endregion
    }
}
