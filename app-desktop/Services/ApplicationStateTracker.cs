using System;
using System.Windows;
using System.Windows.Threading;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Rastreador centralizado do estado da aplicação (foreground vs background).
    /// Usado pelo NotificationService para decidir se deve exibir CustomToast.
    /// EXPANDIDO: Agora dispara eventos para gerenciamento de ciclo de vida de performance.
    /// </summary>
    public static class ApplicationStateTracker
    {
        // volatile garante visibilidade imediata entre threads sem lock de alto custo
        private static volatile bool _isApplicationActive;
        private static volatile bool _isWindowMinimized;
        private static volatile bool _isWindowVisible;
        private static volatile bool _isInSystemTray;
        private static DateTime _lastStateChange = DateTime.UtcNow;
        private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(150);
        private static ApplicationLifecycleState _currentState = ApplicationLifecycleState.Foreground;

        /// <summary>
        /// Evento disparado quando o estado do ciclo de vida da aplicação muda.
        /// Usado para pausar/retomar animações, timers e monitoramentos.
        /// </summary>
        public static event EventHandler<ApplicationLifecycleStateChangedEventArgs>? StateChanged;

        public static bool IsApplicationActive
        {
            get => _isApplicationActive;
            private set
            {
                if (_isApplicationActive != value)
                {
                    _isApplicationActive = value;
                    _lastStateChange = DateTime.UtcNow;
                    App.LoggingService?.LogInfo($"[AppState] IsApplicationActive {value}");
                    UpdateLifecycleState();
                }
            }
        }

        public static bool IsWindowMinimized
        {
            get => _isWindowMinimized;
            private set
            {
                if (_isWindowMinimized != value)
                {
                    _isWindowMinimized = value;
                    _lastStateChange = DateTime.UtcNow;
                    App.LoggingService?.LogInfo($"[AppState] IsWindowMinimized {value}");
                    UpdateLifecycleState();
                }
            }
        }

        public static bool IsWindowVisible
        {
            get => _isWindowVisible;
            private set
            {
                if (_isWindowVisible != value)
                {
                    _isWindowVisible = value;
                    _lastStateChange = DateTime.UtcNow;
                    App.LoggingService?.LogInfo($"[AppState] IsWindowVisible {value}");
                    UpdateLifecycleState();
                }
            }
        }

        public static bool IsInSystemTray
        {
            get => _isInSystemTray;
            private set
            {
                if (_isInSystemTray != value)
                {
                    _isInSystemTray = value;
                    _lastStateChange = DateTime.UtcNow;
                    App.LoggingService?.LogInfo($"[AppState] IsInSystemTray {value}");
                    UpdateLifecycleState();
                }
            }
        }

        /// <summary>
        /// Estado atual do ciclo de vida da aplicação para otimizações de performance.
        /// </summary>
        public static ApplicationLifecycleState CurrentLifecycleState => _currentState;

        /// <summary>
        /// Retorna true se o CustomToast deve será SUPRIMIDO.
        /// Suprime quando a app está em foreground (ativa, visível e não minimizada).
        /// </summary>
        public static bool ShouldSuppressToast()
        {
            // Debounce: ignora mudanças de estado muito recentes para evitar race conditions
            if (DateTime.UtcNow - _lastStateChange < _debounceInterval)
            {
                App.LoggingService?.LogDebug("[AppState] Debounce ativo aguardando estabilização do estado");
            }

            // Suprimir se a aplicação está em primeiro plano
            // Condições de foreground: app ativa, visível, NÃO minimizada e NÃO no tray
            bool isForeground = IsApplicationActive && IsWindowVisible && !IsWindowMinimized && !IsInSystemTray;

            App.LoggingService?.LogDebug($"[AppState] ShouldSuppressToast = {isForeground} + (Active = {IsApplicationActive}, Visible = {IsWindowVisible}, Minimized = {IsWindowMinimized}, Tray = {IsInSystemTray})");

            return isForeground;
        }

        /// <summary>
        /// Retorna true se o CustomToast DEVE será exibido (background, minimizada ou tray).
        /// </summary>
        public static bool ShouldShowToast() => !ShouldSuppressToast();

        /// <summary>
        /// Atualiza o estado do ciclo de vida e dispara evento se mudou.
        /// </summary>
        private static void UpdateLifecycleState()
        {
            var newState = DetermineLifecycleState();

            if (newState != _currentState)
            {
                var oldState = _currentState;
                _currentState = newState;

                App.LoggingService?.LogInfo($"[Performance] Application lifecycle state changed: {oldState} → {newState}");

                try
                {
                    StateChanged?.Invoke(null, new ApplicationLifecycleStateChangedEventArgs(oldState, newState));
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[Performance] Error notifying lifecycle state change: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Determina o estado atual do ciclo de vida com base nas flags.
        /// </summary>
        private static ApplicationLifecycleState DetermineLifecycleState()
        {
            if (IsInSystemTray)
                return ApplicationLifecycleState.Tray;

            if (IsWindowMinimized)
                return ApplicationLifecycleState.Minimized;

            if (!IsWindowVisible || !IsApplicationActive)
                return ApplicationLifecycleState.Background;

            return ApplicationLifecycleState.Foreground;
        }

        // ── Métodos de atualização (chamados pelo MainWindow) ──────────────────
        public static void UpdateWindowActivated(bool isActive)
        {
            IsApplicationActive = isActive;
        }

        public static void UpdateWindowState(WindowState state)
        {
            IsWindowMinimized = state == WindowState.Minimized;

            if (IsWindowMinimized)
                IsApplicationActive = false;
        }

        public static void UpdateWindowVisibility(bool isVisible)
        {
            IsWindowVisible = isVisible;

            if (!isVisible)
                IsApplicationActive = false;
        }

        public static void UpdateTrayState(bool isInTray)
        {
            IsInSystemTray = isInTray;

            if (isInTray)
            {
                IsApplicationActive = false;
                IsWindowVisible = false;
            }
        }
    }

    // ── Tipos auxiliares do ciclo de vida ────────────────────────────────────

    /// <summary>
    /// Estados do ciclo de vida da aplicação para otimização de performance.
    /// </summary>
    public enum ApplicationLifecycleState
    {
        /// <summary>Aplicação em primeiro plano, visível e ativa. Todos os recursos funcionam normalmente.</summary>
        Foreground,

        /// <summary>Aplicação em segundo plano (perdeu foco mas ainda visível). Pode reduzir frequência.</summary>
        Background,

        /// <summary>Aplicação minimizada. Deve pausar animações visuais e reduzir monitoramentos.</summary>
        Minimized,

        /// <summary>Aplicação no system tray (oculta). Manter apenas serviços essenciais ativos.</summary>
        Tray
    }

    /// <summary>
    /// Argumentos do evento de mudança de estado do ciclo de vida.
    /// </summary>
    public class ApplicationLifecycleStateChangedEventArgs : EventArgs
    {
        public ApplicationLifecycleState OldState { get; }
        public ApplicationLifecycleState NewState { get; }

        public ApplicationLifecycleStateChangedEventArgs(ApplicationLifecycleState oldState, ApplicationLifecycleState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }
}
