using System;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// CORREÇÃO CRÍTICA #4: RESOLVER CONFLITO COM WINDOWS GAME MODE
    /// 
    /// Problema identificado: Dois sistemas de otimização competindo (Voltris + Windows)
    /// Solução: Detectar e desabilitar Windows Game Mode OU integrar
    /// 
    /// Regra: NUNCA permitir dois sistemas simultâneos
    /// </summary>
    public class WindowsGameModeController
    {
        private readonly ILoggingService _logger;
        private readonly RegistryValidator _validator;
        
        private bool? _originalGameModeState;
        private bool? _originalAutoGameModeState;

        public WindowsGameModeController(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validator = new RegistryValidator(logger);
            _logger.LogEntry(nameof(WindowsGameModeController));
            _logger.LogExit(nameof(WindowsGameModeController));
        }

        /// <summary>
        /// Detecta se Windows Game Mode está ativo
        /// </summary>
        public bool IsWindowsGameModeActive()
        {
            _logger.LogEntry(nameof(IsWindowsGameModeActive));
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\GameBar", false);
                if (key == null) return false;

                var autoGameMode = key.GetValue("AutoGameModeEnabled");
                var allowGameMode = key.GetValue("AllowAutoGameMode");

                bool isActive = (autoGameMode is int auto && auto == 1) || 
                               (allowGameMode is int allow && allow == 1);

                if (isActive)
                {
                    _logger.LogWarning("[WindowsGameMode] ⚠️ Windows Game Mode está ATIVO");
                    _logger.LogWarning("[WindowsGameMode] Isso pode causar conflito com Voltris Gamer Mode");
                }

                _logger.LogExit(nameof(IsWindowsGameModeActive), isActive);
                return isActive;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[WindowsGameMode] Erro ao detectar: {ex.Message}");
                _logger.LogExit(nameof(IsWindowsGameModeActive), false);
                return false;
            }
        }

        /// <summary>
        /// Desabilita Windows Game Mode completamente
        /// </summary>
        public bool DisableWindowsGameMode()
        {
            _logger.LogEntry(nameof(DisableWindowsGameMode));
            try
            {
                _logger.LogInfo("[WindowsGameMode] Desabilitando Windows Game Mode...");

                // Fazer backup do estado original
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\GameBar", false))
                {
                    if (key != null)
                    {
                        var auto = key.GetValue("AutoGameModeEnabled");
                        var allow = key.GetValue("AllowAutoGameMode");
                        
                        if (auto is int autoVal) _originalAutoGameModeState = autoVal == 1;
                        if (allow is int allowVal) _originalGameModeState = allowVal == 1;
                    }
                }

                // Desabilitar Game Mode
                bool success1 = _validator.SetAndVerifyCurrentUser(
                    @"SOFTWARE\Microsoft\GameBar", 
                    "AutoGameModeEnabled", 
                    0, 
                    RegistryValueKind.DWord);

                bool success2 = _validator.SetAndVerifyCurrentUser(
                    @"SOFTWARE\Microsoft\GameBar", 
                    "AllowAutoGameMode", 
                    0, 
                    RegistryValueKind.DWord);

                // Desabilitar Game DVR (pode interferir)
                bool success3 = _validator.SetAndVerifyCurrentUser(
                    @"System\GameConfigStore", 
                    "GameDVR_Enabled", 
                    0, 
                    RegistryValueKind.DWord);

                if (success1 && success2)
                {
                    _logger.LogSuccess("[WindowsGameMode] ✅ Windows Game Mode desabilitado");
                    _logger.LogInfo("[WindowsGameMode] Voltris Gamer Mode agora tem controle exclusivo");
                    _logger.LogExit(nameof(DisableWindowsGameMode), true);
                    return true;
                }
                else
                {
                    _logger.LogWarning("[WindowsGameMode] ⚠️ Desabilitação parcial (algumas configurações revertidas)");
                    _logger.LogExit(nameof(DisableWindowsGameMode), false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WindowsGameMode] Erro ao desabilitar: {ex.Message}", ex);
                _logger.LogExit(nameof(DisableWindowsGameMode), false);
                return false;
            }
        }

        /// <summary>
        /// Restaura Windows Game Mode ao estado original
        /// </summary>
        public bool RestoreWindowsGameMode()
        {
            _logger.LogEntry(nameof(RestoreWindowsGameMode));
            try
            {
                if (!_originalGameModeState.HasValue && !_originalAutoGameModeState.HasValue)
                {
                    _logger.LogInfo("[WindowsGameMode] Nenhum estado para restaurar");
                    _logger.LogExit(nameof(RestoreWindowsGameMode), true);
                    return true;
                }

                _logger.LogInfo("[WindowsGameMode] Restaurando Windows Game Mode...");

                if (_originalAutoGameModeState.HasValue)
                {
                    _validator.SetAndVerifyCurrentUser(
                        @"SOFTWARE\Microsoft\GameBar",
                        "AutoGameModeEnabled",
                        _originalAutoGameModeState.Value ? 1 : 0,
                        RegistryValueKind.DWord);
                }

                if (_originalGameModeState.HasValue)
                {
                    _validator.SetAndVerifyCurrentUser(
                        @"SOFTWARE\Microsoft\GameBar",
                        "AllowAutoGameMode",
                        _originalGameModeState.Value ? 1 : 0,
                        RegistryValueKind.DWord);
                }

                _logger.LogSuccess("[WindowsGameMode] ✅ Estado original restaurado");
                _logger.LogExit(nameof(RestoreWindowsGameMode), true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WindowsGameMode] Erro ao restaurar: {ex.Message}", ex);
                _logger.LogExit(nameof(RestoreWindowsGameMode), false);
                return false;
            }
        }

        /// <summary>
        /// Verifica se há conflito e resolve automaticamente
        /// </summary>
        public bool ResolveConflict()
        {
            _logger.LogEntry(nameof(ResolveConflict));
            if (IsWindowsGameModeActive())
            {
                _logger.LogWarning("[WindowsGameMode] ⚠️ CONFLITO DETECTADO");
                _logger.LogInfo("[WindowsGameMode] Resolvendo automaticamente...");
                var result = DisableWindowsGameMode();
                _logger.LogExit(nameof(ResolveConflict), result);
                return result;
            }

            _logger.LogInfo("[WindowsGameMode] ✅ Sem conflitos detectados");
            _logger.LogExit(nameof(ResolveConflict), true);
            return true;
        }
    }
}
