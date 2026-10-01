using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Notifications;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Motor de Foco e Priorização Dinâmica de Recursos para Aplicativos em Primeiro Plano (Smart Boost).
    /// Utiliza APIs nativas do Win32 (SetPriorityClass) para garantir que o app em uso receba tempo prioritário de CPU e I/O.
    /// As configurações (perfil de prioridade, cooldowns, listas) são lidas de SettingsService.AppSettings.
    /// </summary>
    public sealed class SmartAppFocusEngine : IDisposable
    {
        private static readonly Lazy<SmartAppFocusEngine> _instance =
            new(() => new SmartAppFocusEngine(App.LoggingService));
        public static SmartAppFocusEngine Instance => _instance.Value;

        private readonly ILoggingService? _logger;
        private readonly object _lock = new();
        private bool _started;

        private int _lastForegroundPid = 0;
        private uint _lastOriginalPriority = ProcessNativeMethods.NORMAL_PRIORITY_CLASS;
        private string _lastProcessName = string.Empty;

        // Processos do sistema Windows que NUNCA devem ter prioridade alterada
        private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "services",
            "lsass", "svchost", "fontdrvhost", "dwm", "explorer", "sihost",
            "taskhostw", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost",
            "ShellExperienceHost", "ctfmon", "SecurityHealthSystray", "SecurityHealthService",
            "VoltrisOptimizer", "devenv", "vshost", "audiodg"
        };

        public SmartAppFocusEngine(ILoggingService? logger)
        {
            _logger = logger;
            _logger?.LogInfo("[SmartAppFocusEngine] Motor de priorização dinâmica instanciado.");
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_started) return;
                _started = true;
            }

            // Garante que o ForegroundWindowTracker está rodando
            ForegroundWindowTracker.Instance.Start();
            ForegroundWindowTracker.Instance.ForegroundChanged += OnForegroundChanged;
            _logger?.LogInfo("[SmartAppFocusEngine] Subscrito ao ForegroundWindowTracker para priorização dinâmica em tempo real.");
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_started) return;
                _started = false;
            }

            ForegroundWindowTracker.Instance.ForegroundChanged -= OnForegroundChanged;
            RestorePreviousProcessPriority();
            _logger?.LogInfo("[SmartAppFocusEngine] Parado e prioridades originais restauradas.");
        }

        /// <summary>
        /// Monitora o app em foco e aplica boost de CPU/I/O conforme configuração.
        /// A notificação é única (uma vez na vida do produto), sem spam ao alternar janelas.
        /// </summary>
        private void OnForegroundChanged(object? sender, int newPid)
        {
            if (!_started || newPid <= 0) return;

            var settings = SettingsService.Instance.Settings;
            if (!settings.EnableSmartFocus)
            {
                _logger?.LogTrace("[SmartAppFocusEngine] Foco Inteligente desativado nas configurações — ignorando mudança de foco.");
                return;
            }

            lock (_lock)
            {
                if (newPid == _lastForegroundPid) return;

                // 1. Restaura o processo anterior antes de elevar o novo
                RestorePreviousProcessPriority();

                // 2. Avalia o novo processo
                string processName = string.Empty;
                try
                {
                    using var proc = Process.GetProcessById(newPid);
                    processName = proc.ProcessName;
                }
                catch
                {
                    return;
                }

                // 3. Ignora processos do sistema ou o próprio Voltris (salvo allow-list)
                bool allowListed = settings.SmartFocusBoostAllowList?.Contains(processName, StringComparer.OrdinalIgnoreCase) == true;
                bool isProtected = ProtectedSystemProcesses.Contains(processName) ||
                                   settings.SmartFocusProtectedProcesses?.Contains(processName, StringComparer.OrdinalIgnoreCase) == true;
                if (!allowListed && isProtected)
                {
                    _lastForegroundPid = 0;
                    _lastProcessName = string.Empty;
                    _logger?.LogTrace($"[SmartAppFocusEngine] Processo protegido '{processName}' ignorado.");
                    return;
                }

                // 4. Aplica a prioridade configurada no novo processo
                IntPtr hProcess = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    newPid);

                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        uint currentPriority = ProcessNativeMethods.GetPriorityClass(hProcess);
                        _lastOriginalPriority = currentPriority;
                        _lastForegroundPid = newPid;
                        _lastProcessName = processName;

                        // Define a prioridade alvo conforme perfil de configuração
                        uint targetPriority = settings.SmartFocusCpuPriority switch
                        {
                            0 => ProcessNativeMethods.NORMAL_PRIORITY_CLASS,
                            1 => ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS,
                            _ => ProcessNativeMethods.HIGH_PRIORITY_CLASS
                        };

                        if (currentPriority != targetPriority)
                        {
                            bool success = ProcessNativeMethods.SetPriorityClass(hProcess, targetPriority);
                            if (success)
                            {
                                _logger?.LogInfo($"[SmartAppFocusEngine] [PRIORITY-BOOST] '{processName}' (PID {newPid}) prioridade ajustada para {DescribePriority(targetPriority)}.");
                                TryNotifySmartFocus(processName, newPid);
                            }
                        }
                        else
                        {
                            _logger?.LogTrace($"[SmartAppFocusEngine] '{processName}' (PID {newPid}) já está em {DescribePriority(currentPriority)} — sem boost.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogTrace($"[SmartAppFocusEngine] Erro ao ajustar prioridade de '{processName}': {ex.Message}");
                    }
                    finally
                    {
                        ProcessNativeMethods.CloseHandle(hProcess);
                    }
                }
            }
        }

        /// <summary>
        /// Aviso único e profissional: notifica o usuário apenas UMA vez (persistida
        /// em Settings) de que o Foco Inteligente está ativo. Nunca spamma a cada troca.
        /// </summary>
        private void TryNotifySmartFocus(string processName, int pid)
        {
            var settings = SettingsService.Instance.Settings;

            if (settings.SmartFocusNotificationShownOnce)
            {
                _logger?.LogTrace("[SmartAppFocusEngine] Notificação única de Foco Inteligente já exibida — suprimida.");
                return;
            }

            _logger?.LogInfo($"[SmartAppFocusEngine] [NOTIFY] Foco Inteligente ativo para '{processName}' — exibindo aviso único.");

            string title = LocalizationService.Instance.GetString("SmartFocusNotificationTitle");
            string message = string.Format(
                LocalizationService.Instance.GetString("SmartFocusNotificationDesc"),
                processName, pid);

            GlobalNotificationService.ShowInfo(title, message);

            try
            {
                settings.SmartFocusNotificationShownOnce = true;
                SettingsService.Instance.SaveSettings();
                _logger?.LogInfo("[SmartAppFocusEngine] Flag de aviso único persistida.");
            }
            catch (Exception ex)
            {
                _logger?.LogTrace($"[SmartAppFocusEngine] Falha ao persistir flag de aviso único: {ex.Message}");
            }
        }

        private static string DescribePriority(uint priority)
        {
            return priority switch
            {
                ProcessNativeMethods.NORMAL_PRIORITY_CLASS => "NORMAL",
                ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS => "ABOVE_NORMAL",
                ProcessNativeMethods.BELOW_NORMAL_PRIORITY_CLASS => "BELOW_NORMAL",
                ProcessNativeMethods.HIGH_PRIORITY_CLASS => "HIGH",
                ProcessNativeMethods.REALTIME_PRIORITY_CLASS => "REALTIME",
                _ => $"0x{priority:X}"
            };
        }

        private void RestorePreviousProcessPriority()
        {
            if (_lastForegroundPid <= 0) return;

            try
            {
                IntPtr hPrev = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    _lastForegroundPid);

                if (hPrev != IntPtr.Zero)
                {
                    try
                    {
                        ProcessNativeMethods.SetPriorityClass(hPrev, _lastOriginalPriority);
                        _logger?.LogTrace($"[SmartAppFocusEngine] Prioridade de '{_lastProcessName}' (PID {_lastForegroundPid}) restaurada para {_lastOriginalPriority}.");
                    }
                    finally
                    {
                        ProcessNativeMethods.CloseHandle(hPrev);
                    }
                }
            }
            catch { }
            finally
            {
                _lastForegroundPid = 0;
                _lastProcessName = string.Empty;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}