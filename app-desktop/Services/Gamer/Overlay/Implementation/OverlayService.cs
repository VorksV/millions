using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;
using VoltrisOptimizer.UI.Overlay;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// SERVIÇO PRINCIPAL DE OVERLAY - CORRIGIDO E PROFISSIONAL
    /// Agora usa o ProfessionalGameOverlayService para captura REAL de FPS
    /// </summary>
    public class OverlayService : IOverlayService
    {
        private readonly ILoggingService? _logger;
        private ProfessionalGameOverlayService? _professionalOverlay;
        private OverlaySettings _settings;
        private bool _isActive = false;
        private int _currentGameProcessId = 0;
        private readonly string _settingsPath;
        private readonly object _lock = new object();
        
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
                    return _isActive && _professionalOverlay != null;
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

        public OverlayService(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(OverlayService));
            _logger?.LogInfo("[OverlayService] ENTER: Constructor");
            try
            {
                _logger = logger;
                
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoltrisOptimizer"
                );
                _settingsPath = Path.Combine(appDataPath, "GamerOverlaySettings.json");

                _logger?.LogInfo("[OVERLAY-SERVICE] 🎮 Inicializando OverlayService com ProfessionalGameOverlayService");
                
                // LoadSettingsAsync().Wait(); // ❌ REMOVIDO .Wait() - CAUSA DEADLOCK E CRASH NO STARTUP
                _settings = new OverlaySettings 
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

                // Inicializar em background para evitar travar o startup
                Task.Run(async () => {
                    try {
                        await LoadSettingsAsync();
                    } catch (Exception ex) {
                        _logger?.LogWarning($"[OVERLAY-SERVICE] Erro ao carregar configurações em background: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[OVERLAY-SERVICE] Erro no construtor do OverlayService: {ex.Message}");
                _settings = new OverlaySettings { IsEnabled = true };
            }
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: Constructor");
                _logger?.LogExit(nameof(OverlayService));
            }
        }
        
        public async Task<bool> StartAsync(int gameProcessId, CancellationToken cancellationToken = default)
        {
            _logger?.LogEntry(nameof(StartAsync));
            _logger?.LogInfo("[OverlayService] ENTER: StartAsync");
            try
            {
                _logger?.LogInfo($"[OVERLAY-SERVICE] ════════════════════════════════");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 🎮 INICIANDO OVERLAY PROFISSIONAL - Jogo PID: {gameProcessId}");
                
                lock (_lock)
                {
                    if (_isActive && _currentGameProcessId == gameProcessId)
                    {
                        _logger?.LogInfo("[OVERLAY-SERVICE] ✅ Overlay já está ativo para este processo");
                        return true;
                    }

                    // Verificar se precisa parar overlay anterior
                    bool needsStop = _isActive;
                    
                    if (needsStop)
                    {
                        _logger?.LogInfo("[OVERLAY-SERVICE] 🛑 Parando overlay anterior antes de iniciar novo");
                        // Sair do lock antes de chamar async
                    }
                    
                    // Verificar se o processo existe
                    if (!IsProcessRunning(gameProcessId))
                    {
                        _logger?.LogWarning($"[OVERLAY-SERVICE] ⚠️ Processo {gameProcessId} não está em execução");
                        return false;
                    }

                    _currentGameProcessId = gameProcessId;
                }
                
                // Parar overlay anterior fora do lock se necessário
                if (_isActive)
                {
                    await StopAsync();
                }

                // Iniciar overlay profissional para jogos
                _logger?.LogInfo("[OVERLAY-SERVICE] 🎯 Iniciando ProfessionalGameOverlayService...");
                _professionalOverlay = new ProfessionalGameOverlayService(_logger);
                
                // Configurar eventos
                _professionalOverlay.OverlayActivated += (s, e) => OverlayActivated?.Invoke(this, e);
                _professionalOverlay.OverlayDeactivated += (s, e) => OverlayDeactivated?.Invoke(this, e);
                _professionalOverlay.SettingsUpdated += (s, e) => SettingsUpdated?.Invoke(this, e);
                
                // Iniciar overlay profissional
                var success = await _professionalOverlay.StartAsync(gameProcessId, cancellationToken);
                
                if (success)
                {
                    lock (_lock)
                    {
                        _isActive = true;
                    }

                    // Aplicar configurações pendentes que foram salvas enquanto o
                    // overlay ainda não existia (UpdateSettings com _professionalOverlay null).
                    if (_settings != null)
                    {
                        _professionalOverlay.UpdateSettings(_settings);
                        _logger?.LogInfo("[OVERLAY-SERVICE] Configurações pendentes aplicadas ao overlay recém-iniciado.");
                    }

                    _logger?.LogSuccess($"[OVERLAY-SERVICE] ✅ Overlay profissional iniciado para processo {gameProcessId}");
                    OverlayActivated?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    _logger?.LogError("[OVERLAY-SERVICE] ❌ Falha ao iniciar overlay profissional");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[OVERLAY-SERVICE] ❌ Erro ao iniciar overlay: {ex.Message}", ex);
                return false;
            }
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: StartAsync");
                _logger?.LogExit(nameof(StartAsync));
            }
        }

        public async Task StopAsync()
        {
            _logger?.LogEntry(nameof(StopAsync));
            _logger?.LogInfo("[OverlayService] ENTER: StopAsync");
            try
            {
                lock (_lock)
                {
                    if (!_isActive)
                    {
                        _logger?.LogInfo("[OVERLAY-SERVICE] StopAsync chamado mas overlay não está ativo");
                        return;
                    }

                    _logger?.LogInfo("[OVERLAY-SERVICE] 🛑 Parando overlay profissional...");
                    _isActive = false;
                    _currentGameProcessId = 0;
                }

                // Parar overlay profissional
                if (_professionalOverlay != null)
                {
                    await _professionalOverlay.StopAsync();
                    _professionalOverlay.Dispose();
                    _professionalOverlay = null;
                }

                _logger?.LogSuccess("[OVERLAY-SERVICE] ✅ Overlay profissional parado com sucesso");
                OverlayDeactivated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[OVERLAY-SERVICE] ❌ Erro ao parar overlay: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: StopAsync");
                _logger?.LogExit(nameof(StopAsync));
            }
        }

        public void UpdateSettings(OverlaySettings settings)
        {
            _logger?.LogEntry(nameof(UpdateSettings));
            _logger?.LogInfo("[OverlayService] ENTER: UpdateSettings");
            try
            {
                var m = settings?.Metrics;
                _logger?.LogInfo("[OVERLAY-SERVICE] ════════════════════════════════════════");
                _logger?.LogInfo("[OVERLAY-SERVICE] 🔧 Atualizando configurações do overlay...");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 FPS={m?.ShowFps}, FrameTime={m?.ShowFrameTime}");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 CPU: usage={m?.ShowCpuUsage}, temp={m?.ShowCpuTemperature}, clock={m?.ShowCpuClock}");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 GPU: usage={m?.ShowGpuUsage}, temp={m?.ShowGpuTemperature}, clock={m?.ShowGpuClock}");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 Mem: RAM={m?.ShowRamUsage}, VRAM={m?.ShowVramUsage}");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 Latency={m?.ShowInputLatency}");
                _logger?.LogInfo($"[OVERLAY-SERVICE] 📊 Opacity={settings?.Opacity}, Position={settings?.Position}");
                // "nao iniciado" e o estado normal antes do primeiro jogo, nao um erro.
                // Logava com ❌ e fazia a auditoria de logs acusar um falso positivo.
                _logger?.LogInfo($"[OVERLAY-SERVICE] _professionalOverlay={( _professionalOverlay != null ? "✔️ ativo" : "⏸️ ainda nao iniciado" )}");
                
                lock (_lock)
                {
                    _settings = settings;
                    
                    if (_professionalOverlay != null)
                    {
                        _logger?.LogInfo("[OVERLAY-SERVICE] ➡️ Delegando para ProfessionalGameOverlayService.UpdateSettings()");
                        _professionalOverlay.UpdateSettings(settings);
                    }
                    else
                    {
                        // Overlay ainda não iniciado: settings ficam pendentes e são
                        // aplicadas automaticamente no próximo StartAsync (veja StartAsync).
                        _logger?.LogInfo("[OVERLAY-SERVICE] _professionalOverlay null — settings salvas como pendentes; serão aplicadas quando o overlay iniciar.");
                    }
                }
                
                SettingsUpdated?.Invoke(this, EventArgs.Empty);
                _logger?.LogSuccess("[OVERLAY-SERVICE] ✅ Configurações atualizadas com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[OVERLAY-SERVICE] ❌ Erro ao atualizar configurações: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: UpdateSettings");
                _logger?.LogExit(nameof(UpdateSettings));
            }
        }

        public async Task LoadSettingsAsync()
        {
            _logger?.LogEntry(nameof(LoadSettingsAsync));
            _logger?.LogInfo("[OverlayService] ENTER: LoadSettingsAsync");
            try
            {
                _logger?.LogInfo("[OVERLAY-SERVICE] 📁 Carregando configurações...");
                
                lock (_lock)
                {
                    _settings = OverlaySettings.LoadFromFile(_settingsPath);
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[OVERLAY-SERVICE] ❌ Erro ao carregar configurações: {ex.Message}", ex);
                _settings = new OverlaySettings 
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
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: LoadSettingsAsync");
                _logger?.LogExit(nameof(LoadSettingsAsync));
            }
        }

        public async Task SaveSettingsAsync()
        {
            _logger?.LogEntry(nameof(SaveSettingsAsync));
            _logger?.LogInfo("[OverlayService] ENTER: SaveSettingsAsync");
            try
            {
                _logger?.LogInfo("[OVERLAY-SERVICE] 💾 Salvando configurações...");
                
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
                _logger?.LogError($"[OVERLAY-SERVICE] ❌ Erro ao salvar configurações: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogInfo("[OverlayService] EXIT: SaveSettingsAsync");
                _logger?.LogExit(nameof(SaveSettingsAsync));
            }
        }

        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _logger?.LogInfo("[OverlayService] ENTER: Dispose");
            _ = StopAsync();
            _logger?.LogInfo("[OverlayService] EXIT: Dispose");
            _logger?.LogExit(nameof(Dispose));
        }
        
        #region Métodos Auxiliares
        
        private bool IsProcessRunning(int processId)
        {
            _logger?.LogEntry(nameof(IsProcessRunning));
            _logger?.LogDebug("[OverlayService] ENTER: IsProcessRunning");
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
            finally
            {
                _logger?.LogDebug("[OverlayService] EXIT: IsProcessRunning");
                _logger?.LogExit(nameof(IsProcessRunning));
            }
        }
        
        #endregion
    }
}
