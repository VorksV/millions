using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using VoltrisOptimizer.Services.Notifications;
using VoltrisOptimizer.UI.Windows;

namespace VoltrisOptimizer.Services
{
    public static class GlobalNotificationService
    {
        private static VoltrisNotificationService? _innerService;
        private static ILoggingService? _logger;
        private static Timer? _cleanupTimer;
           private static readonly object _toastLock = new();
           private static readonly List<CustomToastWindow> _activeToasts = new();

           // FILA DE EXIBIÇÃO: antes, N notificações disparadas no mesmo instante criavam N
           // janelas ao mesmo tempo, todas animando da mesma posição e todas reposicionadas
           // de forma assíncrona e repetida — o resultado era o piscar. Agora cada toast entra
           // por vez, em ordem, respeitando o limite de slots visíveis.
           private static readonly Queue<PendingToast> _pendingToasts = new();
           private static bool _isShowingToast;

           private static readonly int ToastWidth = 376;
           private static readonly int ToastHeightEstimated = 90;
           private const int ToastMargin = 16;
           private const int ToastStackSpacing = 12;
           private const int MaxVisibleToasts = 3;
           private const int MaxQueuedToasts = 12;

        private readonly record struct PendingToast(
            string Title, string Message, NotificationType Type,
            System.Windows.Media.Geometry? Glyph, System.Windows.Media.Color? Accent,
            // Glifos "cheios" (termômetro dos cartões de temperatura) vs
            // "contornados" (ícones de dispositivo da aba Rede).
            bool Filled);

        private static readonly Dictionary<string, DateTime> _notificationHistoryTracker = new();
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(3.5);

        private const int HistoryTrackerMaxItems = 1000;
        private const int HistoryTrackerTrimCount = 200;

        private static bool IsSpam(string title, string message, NotificationType type)
        {
            // CORREÇÃO: o filtro de duplicata agora se aplica a TODOS os tipos,
            // inclusive Warning.
            //
            // A versão anterior retornava `false` imediatamente para Error e
            // Warning, ANTES de consultar o dicionário de histórico. Ou seja: as
            // duas classes que mais disparam alertas de segurança eram
            // exatamente as duas que nunca passavam por deduplicação. Um mesmo
            // arquivo detectado em rajada produzia um toast por evento.
            //
            // A intenção original — não silenciar uma ameaça confirmada — é
            // atendida de forma mais precisa por tipo: `Error` corresponde a
            // evidência comportamental confirmada (injeção de código, ransomware)
            // e é raro por natureza, então pode passar. `Warning` é heurística
            // estática, e deduplicá-la é o que impede a inundação de alertas.
            string hashKey = $"{title}_{message}";
            if (_notificationHistoryTracker.TryGetValue(hashKey, out DateTime lastTime))
            {
                var elapsed = DateTime.Now - lastTime;

                // Error tem janela curta: é raro e crítico.
                // Info/Success/Processing usam a janela padrão.
                // Warning tem janela longa, porque é a classe ruidosa.
                var window = type switch
                {
                    NotificationType.Error => TimeSpan.FromSeconds(1.5),
                    NotificationType.Warning => TimeSpan.FromSeconds(30),
                    _ => CooldownDuration
                };

                if (elapsed < window)
                {
                    return true;
                }
            }
            _notificationHistoryTracker[hashKey] = DateTime.Now;

            // Prevenir crescimento infinito: trim quando exceder o limite
            if (_notificationHistoryTracker.Count > HistoryTrackerMaxItems)
            {
                var toRemove = _notificationHistoryTracker
                    .OrderBy(kvp => kvp.Value)
                    .Take(HistoryTrackerTrimCount)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in toRemove)
                    _notificationHistoryTracker.Remove(key);
            }

            return false;
        }

        public static void Initialize(VoltrisNotificationService service, ILoggingService logger)
        {
            _innerService = service;
            _logger = logger;

            _cleanupTimer = new Timer(_ =>
            {
                try { RemoveExpiredNotifications(); } catch { }
            }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            _logger.LogInfo("[GlobalNotification] Service initialized");
        }

        /// <summary>
        /// Se true, toasts visuais estão silenciados (mas o Drawer continua recebendo notificações).
        /// Lê/grava de SettingsService.Instance.Settings.ToastNotificationsMuted.
        /// </summary>
        public static bool IsToastMuted
        {
            get => SettingsService.Instance.Settings.ToastNotificationsMuted;
            set
            {
                SettingsService.Instance.Settings.ToastNotificationsMuted = value;
                SettingsService.Instance.SaveSettings();
                _logger?.LogInfo($"[GlobalNotification] Toast silenciamento alterado para: {value}");
                ToastMuteChanged?.Invoke(null, value);
            }
        }

        /// <summary>
        /// Dispara quando o estado de mute muda. Interessados (SettingsView, etc.) se inscrevem.
        /// </summary>
        public static event EventHandler<bool>? ToastMuteChanged;

        public static void Show(string title, string message, NotificationType type = NotificationType.Info)
        {
            Show(title, message, type, null, null);
        }

        /// <summary>
        /// True quando o Modo Gamer está ligado. Resolve pelo ServiceLocator e
        /// falha de forma segura: se o DI ainda não estiver pronto, devolve
        /// false e o toast segue normal.
        /// </summary>
        public static bool IsGamerModeActive()
        {
            try
            {
                var manager = VoltrisOptimizer.Core.ServiceLocator
                    .GetService<VoltrisOptimizer.Services.Gamer.GamerModeManager.IGamerModeManager>();
                return manager?.IsActive == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Exibe uma notificação com ícone contextual (ex.: o dispositivo detectado na rede),
        /// usando o mesmo glifo e a mesma cor de destaque da aba Rede do Voltris Shield.
        /// </summary>
        /// <param name="critical">
        /// Alerta de segurança crítico (ransomware, ameaça ativa). Ignora a
        /// supressão do modo gamer: o usuário pediu para não ser incomodado
        /// durante o jogo, mas silenciar um ransomware seria uma falha de
        /// segurança. Só these casos signalizam <c>true</c>.
        /// </param>
        public static void Show(string title, string message, NotificationType type,
            System.Windows.Media.Geometry? contextualGlyph, System.Windows.Media.Color? contextualAccent,
            bool critical = false, bool contextualFilled = false)
        {
            if (!SettingsService.Instance.Settings.NotificationsEnabled) return;

            string localizedTitle = LocalizeTitle(title);
            string localizedMessage = LocalizeMessage(message);

            _logger?.LogInfo($"[GlobalNotification] Show: [{type}] {localizedTitle}");

            // Drawer de notificações recebe as strings ORIGINAIS (raw) para poder re-traduzir quando o idioma mudar
            _innerService?.ShowNotificationAsync(title, message, type);

            bool showToast = ApplicationStateTracker.ShouldShowToast() && !IsToastMuted;

            // Modo gamer ligado: nada de toast atrapalhando a jogabilidade.
            if (showToast && !critical && IsGamerModeActive())
            {
                showToast = false;
                _logger?.LogInfo("[GlobalNotification] Toast suprimido: Modo Gamer ativo (respeitando a jogabilidade).");
            }

            _logger?.LogInfo($"[GlobalNotification] ShouldShowToast={ApplicationStateTracker.ShouldShowToast()}, IsToastMuted={IsToastMuted}, GamerMode={IsGamerModeActive()}, critical={critical}, showToast={showToast}");

            if (showToast)
            {
                if (IsSpam(localizedTitle, localizedMessage, type))
                {
                    _logger?.LogInfo($"[GlobalNotification] Toast suprimido por filtro de cooldown anti-spam: {localizedTitle}");
                    return;
                }
                ShowCustomToast(localizedTitle, localizedMessage, type, contextualGlyph, contextualAccent, contextualFilled);
            }
            else
            {
                _logger?.LogInfo("[GlobalNotification] Toast suprimido (foreground ou mute). Apenas drawer.");
            }
        }

        public static string LocalizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;

            string localized = LocalizationService.Instance.GetString(title);
            if (localized != title) return localized;

            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lang == Language.Portuguese) return title;

            string trimmed = title.Trim();

            // Temperature alerts como título (vindos do GlobalThermalMonitorService)
            if (trimmed.StartsWith("Temperatura CPU elevada:", StringComparison.OrdinalIgnoreCase))
            {
                string tempPart = trimmed.Substring("Temperatura CPU elevada:".Length).Trim();
                return lang == Language.English
                    ? $"High CPU temperature: {tempPart}"
                    : $"Temperatura de CPU elevada: {tempPart}";
            }
            if (trimmed.StartsWith("Temperatura GPU elevada:", StringComparison.OrdinalIgnoreCase))
            {
                string tempPart = trimmed.Substring("Temperatura GPU elevada:".Length).Trim();
                return lang == Language.English
                    ? $"High GPU temperature: {tempPart}"
                    : $"Temperatura de GPU elevada: {tempPart}";
            }

            switch (trimmed)
            {
                case "Segurança":
                    return lang == Language.English ? "Security" : "Seguridad";
                case "Temperatura Atualizada":
                    return lang == Language.English ? "Temperature Updated" : "Temperatura Actualizada";
                case "🌡️ ALERTA DE TEMPERATURA":
                    return lang == Language.English ? "🌡️ TEMPERATURE ALERT" : "🌡️ ALERTA DE TEMPERATURA";
                case "🔌 OTIMIZAÇÃO DE ENERGIA":
                    return lang == Language.English ? "🔌 POWER OPTIMIZATION" : "🔌 OPTIMIZACIÓN DE ENERGÍA";
                case "⚠️ ALERTA DE PERFORMANCE":
                    return lang == Language.English ? "⚠️ PERFORMANCE ALERT" : "⚠️ ALERTA DE RENDIMIENTO";
                case "Corrigir Erros nos Jogos":
                    return lang == Language.English ? "Fix Game Errors" : "Corregir Errores en Juegos";
                case "Network":
                    return lang == Language.English ? "Network" : (lang == Language.Spanish ? "Red" : "Rede");
                case "Otimizacao Concluida":
                case "Otimização Concluída":
                    return lang == Language.English ? "Optimization Completed" : "Optimización Completada";
                case "Otimização Rápida":
                    return lang == Language.English ? "Quick Optimization" : "Optimización Rápida";
                case "Limpeza Concluída":
                    return lang == Language.English ? "Cleanup Completed" : "Limpieza Completada";
                case "Limpeza Rápida":
                    return lang == Language.English ? "Quick Cleanup" : "Limpieza Rápida";
                case "❌ MODO GAMER BLOQUEADO":
                    return lang == Language.English ? "❌ GAMER MODE BLOCKED" : "❌ MODO GAMER BLOQUEADO";
                case "⚠️ TEMPERATURA ELEVADA":
                    return lang == Language.English ? "⚠️ HIGH TEMPERATURE" : "⚠️ TEMPERATURA ELEVADA";
                case "Windows Update":
                    return "Windows Update";
                case "Stream Hub":
                    return "Stream Hub";
                case "Shield":
                    return "Shield";
                case "Dashboard":
                    return "Dashboard";
                // Gamer Mode — Otimizações Temporárias
                case "⚡ Otimizações Temporárias Ativadas":
                    return lang == Language.English ? "⚡ Temporary Optimizations Activated" : "⚡ Optimizaciones Temporales Activadas";
                case "Otimizações Temporárias Desativadas":
                    return lang == Language.English ? "Temporary Optimizations Deactivated" : "Optimizaciones Temporales Desactivadas";
                case "Rollback de Emergência":
                    return lang == Language.English ? "Emergency Rollback" : "Rollback de Emergencia";
                // Gamer Mode — Otimizações Adaptativas PRO
                case "🎮 OTIMIZAÇÕES ADAPTATIVAS CONCLUÍDAS":
                    return lang == Language.English ? "🎮 ADAPTIVE OPTIMIZATIONS COMPLETED" : "🎮 OPTIMIZACIONES ADAPTATIVAS COMPLETADAS";
                case "❌ ERRO NAS OTIMIZAÇÕES":
                    return lang == Language.English ? "❌ OPTIMIZATION ERROR" : "❌ ERROR EN LAS OPTIMIZACIONES";
                // Gamer Mode — Detecção de Jogo
                case "🎮 JOGO DETECTADO":
                    return lang == Language.English ? "🎮 GAME DETECTED" : "🎮 JUEGO DETECTADO";
                // Gamer Mode — Reparo de Jogos
                case "🎮 Reparo de Jogos Concluído":
                    return lang == Language.English ? "🎮 Game Repair Completed" : "🎮 Reparación de Juegos Completada";
                case "🎮 Reparo de Jogos":
                    return lang == Language.English ? "🎮 Game Repair" : "🎮 Reparación de Juegos";
                default:
                    return title;
            }
        }

        public static string LocalizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            string localized = LocalizationService.Instance.GetString(message);
            if (localized != message) return localized;

            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lang == Language.Portuguese) return message;

            string msg = message.Trim();

            // CPU/GPU Temperature alerts
            if (msg.StartsWith("Temperatura CPU elevada:", StringComparison.OrdinalIgnoreCase))
            {
                string tempPart = msg.Substring("Temperatura CPU elevada:".Length).Trim();
                return lang == Language.English 
                    ? $"High CPU temperature: {tempPart}" 
                    : $"Temperatura de CPU elevada: {tempPart}";
            }
            if (msg.StartsWith("Temperatura GPU elevada:", StringComparison.OrdinalIgnoreCase))
            {
                string tempPart = msg.Substring("Temperatura GPU elevada:".Length).Trim();
                return lang == Language.English 
                    ? $"High GPU temperature: {tempPart}" 
                    : $"Temperatura de GPU elevada: {tempPart}";
            }
            if (msg.Equals("Verifique refrigeração e feche apps pesados", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English
                    ? "Check cooling and close heavy apps"
                    : "Verifique la refrigeración y cierre apps pesadas";
            }
            if (msg.Equals("Verifique ventilação do sistema", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English
                    ? "Check system ventilation"
                    : "Verifique la ventilación del sistema";
            }

            // Twitch / YouTube
            if (msg.Equals("Twitch conectada com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Twitch connected successfully!" : "¡Twitch conectada con éxito!";
            }
            if (msg.Equals("Falha ao conectar Twitch. Verifique seu token.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Failed to connect Twitch. Check your token." : "Fallo al conectar Twitch. Verifique su token.";
            }
            if (msg.StartsWith("Erro ao conectar Twitch:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao conectar Twitch:".Length);
                return lang == Language.English ? $"Error connecting Twitch:{errPart}" : $"Error al conectar Twitch:{errPart}";
            }
            if (msg.Equals("YouTube conectado com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "YouTube connected successfully!" : "¡YouTube conectado con éxito!";
            }
            if (msg.Equals("Falha ao conectar YouTube. Verifique a API Key.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Failed to connect YouTube. Check the API Key." : "Fallo al conectar YouTube. Verifique la API Key.";
            }
            if (msg.StartsWith("Erro ao conectar YouTube:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao conectar YouTube:".Length);
                return lang == Language.English ? $"Error connecting YouTube:{errPart}" : $"Error al conectar YouTube:{errPart}";
            }

            // RAM Cleanup
            if (msg.StartsWith("RAM Limpa!", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("RAM Limpa!".Length).Trim();
                rest = rest.Replace("liberados.", lang == Language.English ? "released." : "liberados.");
                rest = rest.Replace("liberados", lang == Language.English ? "released" : "liberados");
                return lang == Language.English ? $"RAM Cleaned! {rest}" : $"¡RAM Limpia! {rest}";
            }
            if (msg.Equals("Falha ao obter ProcessArm para limpeza.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Failed to get ProcessArm for cleanup." : "Fallo al obtener ProcessArm para la limpieza.";
            }
            if (msg.StartsWith("Erro na limpeza de RAM:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro na limpeza de RAM:".Length);
                return lang == Language.English ? $"Error during RAM cleanup:{errPart}" : $"Error en la limpieza de RAM:{errPart}";
            }

            // Network optimization
            if (msg.Equals("Rede otimizada! Prioridade de tráfego ativada para OBS.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English 
                    ? "Network optimized! Traffic priority activated for OBS." 
                    : "¡Red optimizada! Prioridad de tráfico activada para OBS.";
            }
            if (msg.Equals("Falha ao obter NetworkArm.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Failed to get NetworkArm." : "Fallo al obtener NetworkArm.";
            }
            if (msg.StartsWith("Erro na otimização de rede:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro na otimização de rede:".Length);
                return lang == Language.English ? $"Error in network optimization:{errPart}" : $"Error en la optimización de red:{errPart}";
            }
            if (msg.Equals("Stream Hub parado com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Stream Hub stopped successfully!" : "¡Stream Hub detenido con éxito!";
            }
            if (msg.StartsWith("Falha ao parar Stream Hub:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Falha ao parar Stream Hub:".Length);
                return lang == Language.English ? $"Failed to stop Stream Hub:{errPart}" : $"Fallo al detener Stream Hub:{errPart}";
            }
            if (msg.Equals("Stream Hub iniciado e otimizado com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Stream Hub started and optimized successfully!" : "¡Stream Hub iniciado y optimizado con éxito!";
            }
            if (msg.Equals("Falha ao conectar OBS Studio. Verifique as configurações.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English 
                    ? "Failed to connect OBS Studio. Verify configurations." 
                    : "Fallo al conectar OBS Studio. Verifique la configuración.";
            }
            if (msg.StartsWith("Falha ao iniciar Stream Hub:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Falha ao iniciar Stream Hub:".Length);
                return lang == Language.English ? $"Failed to start Stream Hub:{errPart}" : $"Fallo al iniciar Stream Hub:{errPart}";
            }

            // Security scan
            if (msg.Equals("Verificação rápida concluída com sucesso.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Quick scan completed successfully." : "Verificación rápida completada con éxito.";
            }
            if (msg.Equals("Definições de vírus atualizadas com sucesso.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Virus definitions updated successfully." : "Definiciones de virus actualizadas con éxito.";
            }

            // Windows Update
            if (msg.Equals("Seu sistema está completamente atualizado.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Your system is completely up to date." : "Su sistema está completamente actualizado.";
            }
            if (msg.Contains("atualização(ões) pendente(s) encontrada(s)"))
            {
                string numPart = msg.Split(' ')[0];
                return lang == Language.English 
                    ? $"{numPart} pending update(s) found." 
                    : $"{numPart} actualización(es) pendiente(s) encontrada(s).";
            }
            if (msg.StartsWith("Erro ao verificar atualizações:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao verificar atualizações:".Length);
                return lang == Language.English ? $"Error checking updates:{errPart}" : $"Error al verificar actualizaciones:{errPart}";
            }
            if (msg.StartsWith("Erro ao instalar:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao instalar:".Length);
                return lang == Language.English ? $"Error installing:{errPart}" : $"Error al instalar:{errPart}";
            }
            // Windows Update — mensagens retornadas por WindowsUpdateService.InstallUpdatesAsync
            if (msg.Equals("Sistema já está atualizado.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "System is already up to date." : "El sistema ya está actualizado.";
            }
            if (msg.Equals("Atualizações instaladas. Reinicialização necessária.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Updates installed. Reboot required." : "Actualizaciones instaladas. Reinicio necesario.";
            }
            if (msg.Equals("Atualizações instaladas com sucesso.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Updates installed successfully." : "Actualizaciones instaladas con éxito.";
            }
            if (msg.StartsWith("Falha na instalação:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Falha na instalação:".Length);
                return lang == Language.English ? $"Installation failed:{errPart}" : $"Fallo en la instalación:{errPart}";
            }

            // Gamer Mode / CPU temperature / etc.
            if (msg.Equals("Nenhum problema detectado. Tudo OK!", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "No problems detected. All OK!" : "¡Ningún problema detectado. Todo bien!";
            }
            if (msg.Equals("Network optimizer não está disponível.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Network optimizer is not available." : "El optimizador de red no está disponible.";
            }
            if (msg.Equals("Otimização de rede concluída.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "Network optimization completed." : "Optimización de red completada.";
            }
            if (msg.StartsWith("Erro ao otimizar rede:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao otimizar rede:".Length);
                return lang == Language.English ? $"Error optimizing network:{errPart}" : $"Error al optimizar la red:{errPart}";
            }
            if (msg.StartsWith("Erro ao otimizar pilha:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao otimizar pilha:".Length);
                return lang == Language.English ? $"Error optimizing stack:{errPart}" : $"Error al optimizar la pila:{errPart}";
            }
            if (msg.StartsWith("Erro ao aplicar DNS:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao aplicar DNS:".Length);
                return lang == Language.English ? $"Error applying DNS:{errPart}" : $"Error al aplicar DNS:{errPart}";
            }
            if (msg.StartsWith("Erro ao restaurar DNS:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao restaurar DNS:".Length);
                return lang == Language.English ? $"Error restoring DNS:{errPart}" : $"Error al restaurar DNS:{errPart}";
            }
            if (msg.StartsWith("Erro ao atualizar:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro ao atualizar:".Length);
                return lang == Language.English ? $"Error updating:{errPart}" : $"Error al actualizar:{errPart}";
            }

            // Quick optimization/cleanup
            if (msg.StartsWith("Sistema limpo com sucesso!", StringComparison.OrdinalIgnoreCase))
            {
                string rest = msg.Substring("Sistema limpo com sucesso!".Length).Trim();
                rest = rest.Replace("liberados.", lang == Language.English ? "released." : "liberados.");
                rest = rest.Replace("liberados", lang == Language.English ? "released" : "liberados");
                return lang == Language.English 
                    ? $"System cleaned successfully! {rest}" 
                    : $"¡Sistema limpiado con éxito! {rest}";
            }
            if (msg.StartsWith("Erro durante a limpeza:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro durante a limpeza:".Length);
                return lang == Language.English ? $"Error during cleanup:{errPart}" : $"Error durante la limpieza:{errPart}";
            }
            if (msg.StartsWith("Erro:", StringComparison.OrdinalIgnoreCase))
            {
                string errPart = msg.Substring("Erro:".Length);
                return lang == Language.English ? $"Error:{errPart}" : $"Error:{errPart}";
            }

            // Temperature Warning from thermal service or dashboard
            if (msg.Contains("CPU muito quente"))
            {
                msg = msg.Replace("CPU muito quente", lang == Language.English ? "CPU very hot" : "CPU muy caliente");
            }
            if (msg.Contains("CPU aquecida"))
            {
                msg = msg.Replace("CPU aquecida", lang == Language.English ? "CPU heated" : "CPU calentada");
            }
            if (msg.Contains("GPU muito quente"))
            {
                msg = msg.Replace("GPU muito quente", lang == Language.English ? "GPU very hot" : "GPU muy caliente");
            }
            if (msg.Contains("GPU aquecida"))
            {
                msg = msg.Replace("GPU aquecida", lang == Language.English ? "GPU heated" : "GPU calentada");
            }
            if (msg.Contains("Recomendação: Troque a pasta térmica e faça uma limpeza profissional na máquina."))
            {
                msg = msg.Replace("Recomendação: Troque a pasta térmica e faça uma limpeza profissional na máquina.", 
                    lang == Language.English 
                        ? "Recommendation: Replace thermal paste and perform a professional cleanup of the machine." 
                        : "Recomendación: Cambie la pasta térmica y realice una limpieza profesional de la máquina.");
            }

            // Power/Performance alert messages
            if (msg.Equals("Plano de Energia Alterado para Desempenho Máximo", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English 
                    ? "Power Plan Changed to Maximum Performance" 
                    : "Plan de Energía Cambiado a Máximo Rendimiento";
            }

            // Gamer mode activation/deactivation errors and status
            if (msg.Contains("Para ativar o Modo Gamer, altere o Perfil Inteligente nas Configurações."))
            {
                msg = msg.Replace("Para ativar o Modo Gamer, altere o Perfil Inteligente nas Configurações.",
                    lang == Language.English 
                        ? "To activate Gamer Mode, change the Intelligent Profile in Settings." 
                        : "Para activar el Modo Gamer, cambie el Perfil Inteligente en la Configuración.");
            }

            // Gamer Mode — Otimizações Temporárias
            if (msg.Contains("otimizações aplicadas com sucesso!"))
            {
                msg = msg.Replace("otimizações aplicadas com sucesso!",
                    lang == Language.English ? "optimizations applied successfully!" : "optimizaciones aplicadas con éxito!");
            }
            if (msg.Equals("Todas as mudanças foram revertidas", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "All changes have been reverted" : "Todos los cambios han sido revertidos";
            }
            if (msg.Equals("Todas as otimizações foram revertidas", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English ? "All optimizations have been reverted" : "Todas las optimizaciones han sido revertidas";
            }

            // Gamer Mode — Detecção de Jogo
            if (msg.Contains("foi detectado!") && msg.Contains("O modo gamer será ativado para máxima performance."))
            {
                // Formato: "{game.Name} foi detectado! \nO modo gamer será ativado para máxima performance."
                string gameName = msg.Split(new[] { " foi detectado!" }, StringSplitOptions.None)[0].Trim();
                return lang == Language.English
                    ? $"{gameName} was detected! \nGamer mode will be activated for maximum performance."
                    : $"¡{gameName} fue detectado! \nEl modo gamer será activado para máximo rendimiento.";
            }

            // Gamer Mode — Reparo de Jogos (mensagens dinâmicas)
            if (msg.Contains("problemas corrigidos.") && msg.Contains("Reinicie o PC"))
            {
                // Formato: "X/Y problemas corrigidos.\n🔄 Reinicie o PC para aplicar todas as correções."
                string countPart = msg.Split(new[] { " problemas corrigidos." }, StringSplitOptions.None)[0].Trim();
                return lang == Language.English
                    ? $"{countPart} issues fixed.\n🔄 Restart the PC to apply all fixes."
                    : $"{countPart} problemas corregidos.\n🔄 Reinicie el PC para aplicar todas las correcciones.";
            }
            if (msg.Contains("problemas corrigidos com sucesso."))
            {
                string countPart = msg.Split(new[] { " problemas corrigidos com sucesso." }, StringSplitOptions.None)[0].Trim();
                return lang == Language.English
                    ? $"{countPart} issues fixed successfully."
                    : $"{countPart} problemas corregidos con éxito.";
            }
            if (msg.Contains("problema(s) requerem ação manual"))
            {
                string countPart = msg.Split(new[] { " problema(s) requerem ação manual" }, StringSplitOptions.None)[0].Trim();
                return lang == Language.English
                    ? $"{countPart} issue(s) require manual action. No automatic fix applied."
                    : $"{countPart} problema(s) requieren acción manual. Ninguna corrección automática aplicada.";
            }
            if (msg.Equals("Nenhuma alteração necessária — tudo já está correto.", StringComparison.OrdinalIgnoreCase))
            {
                return lang == Language.English
                    ? "No changes needed — everything is already correct."
                    : "No se necesitan cambios — todo ya está correcto.";
            }

            // Gamer Mode — Otimizações Adaptativas PRO (mensagens complexas com ⚡ e 📋)
            if (msg.Contains("Falha ao aplicar otimizações adaptativas"))
            {
                return lang == Language.English
                    ? "Failed to apply adaptive optimizations. Check the log for details."
                    : "Fallo al aplicar optimizaciones adaptativas. Verifique el log para detalles.";
            }
            if (msg.Contains("⚡ Otimizações aplicadas:"))
            {
                msg = msg.Replace("⚡ Otimizações aplicadas:", lang == Language.English ? "⚡ Optimizations applied:" : "⚡ Optimizaciones aplicadas:");
                msg = msg.Replace("📋 Aplicadas:", lang == Language.English ? "📋 Applied:" : "📋 Aplicadas:");
                msg = msg.Replace("⏭️ Puladas (por perfil):", lang == Language.English ? "⏭️ Skipped (by profile):" : "⏭️ Omitidas (por perfil):");
            }

            // Dashboard — Score message
            if (msg.StartsWith("Score:") && msg.Contains("Perfil:"))
            {
                msg = msg.Replace("Perfil:", lang == Language.English ? "Profile:" : "Perfil:");
            }
            if (msg.StartsWith("Sistema otimizado com sucesso. Perfil:", StringComparison.OrdinalIgnoreCase))
            {
                msg = msg.Replace("Sistema otimizado com sucesso. Perfil:",
                    lang == Language.English ? "System optimized successfully. Profile:" : "Sistema optimizado con éxito. Perfil:");
            }

            return msg;
        }

        public static void ShowSuccess(string title, string message) => Show(title, message, NotificationType.Success);
        public static void ShowError(string title, string message) => Show(title, message, NotificationType.Error);
        public static void ShowWarning(string title, string message) => Show(title, message, NotificationType.Warning);
        public static void ShowInfo(string title, string message) => Show(title, message, NotificationType.Info);
        public static void ShowProcessing(string title, string message) => Show(title, message, NotificationType.Processing);

        private static void ShowCustomToast(string title, string message, NotificationType type,
            System.Windows.Media.Geometry? contextualGlyph = null,
            System.Windows.Media.Color? contextualAccent = null,
            bool contextualFilled = false)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                // Tudo acontece na thread da UI, de forma SERIAL: enfileira e bombeia.
                if (dispatcher.CheckAccess())
                {
                    EnqueueAndPump(title, message, type, contextualGlyph, contextualAccent, contextualFilled);
                }
                else
                {
                    dispatcher.BeginInvoke(() => EnqueueAndPump(title, message, type, contextualGlyph, contextualAccent, contextualFilled));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GlobalNotification] Erro no dispatcher: {ex.Message}");
            }
        }

        private static void EnqueueAndPump(string title, string message, NotificationType type,
            System.Windows.Media.Geometry? glyph, System.Windows.Media.Color? accent, bool filled = false)
        {
            // Duplicata exata ainda visível: apenas reinicia o timer, sem criar outra janela.
            var existing = _activeToasts.FirstOrDefault(t =>
                t.TitleText.Text == title && t.MessageText.Text == message);
            if (existing != null)
            {
                existing.RefreshToast();
                _logger?.LogInfo($"[TOAST] Duplicado evitado, Toast existente atualizado: {title}");
                return;
            }

            if (_pendingToasts.Count >= MaxQueuedToasts)
            {
                _logger?.LogInfo("[TOAST] Fila cheia, notificação mais antiga descartada para evitar acúmulo.");
                _pendingToasts.Dequeue();
            }

            _pendingToasts.Enqueue(new PendingToast(title, message, type, glyph, accent, filled));
            _logger?.LogInfo($"[TOAST] Notificação enfileirada. Fila={_pendingToasts.Count}, visíveis={_activeToasts.Count}");
            PumpQueue();
        }

        /// <summary>
        /// Exibe a próxima notificação da fila quando há vaga. Chamado sempre na thread da UI.
        /// </summary>
        private static void PumpQueue()
        {
            if (_isShowingToast) return;
            if (_activeToasts.Count >= MaxVisibleToasts) return;
            if (_pendingToasts.Count == 0) return;

            PendingToast next;
            lock (_toastLock)
            {
                if (_activeToasts.Count >= MaxVisibleToasts || _pendingToasts.Count == 0) return;
                next = _pendingToasts.Dequeue();
            }

            _isShowingToast = true;
            try
            {
                var toastRect = CalculateToastPosition();
                _logger?.LogInfo($"[TOAST] Exibindo: {next.Title} | DPI={toastRect.DpiScale:P0} | Fila restante={_pendingToasts.Count}");

                var toast = new CustomToastWindow(next.Title, next.Message, next.Type,
                    toastRect.Left, toastRect.Top, ToastWidth, ToastHeightEstimated,
                    next.Glyph, next.Accent, next.Filled);

                lock (_toastLock)
                {
                    _activeToasts.Add(toast);
                }

                toast.Closed += (s, e) =>
                {
                    lock (_toastLock)
                    {
                        _activeToasts.Remove(toast);
                    }
                    _isShowingToast = false;
                    RepositionAllToasts();
                    // Continua a fila assim que um slot libera.
                    PumpQueue();
                };

                toast.Show();
                RepositionAllToasts();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GlobalNotification] Erro ao exibir CustomToast: {ex.Message}");
                _isShowingToast = false;
            }
        }

        private static (double Left, double Top, double DpiScale, System.Drawing.Rectangle WorkArea, System.Drawing.Rectangle MonitorRect) CalculateToastPosition()
        {
            // 1. Detectar monitor correto (onde a MainWindow está)
            var monitorRect = GetMonitorRect();
            var workArea = GetMonitorWorkArea();

            // 2. Obter DPI scaling do monitor
            double dpiScale = GetMonitorDpiScale();

            // 3. Calcular posição no CANTO INFERIOR DIREITO
            //    WPF trabalha em pixels independentes de dispositivo (DIPs)
            //    O WorkingArea da Win32 está em pixels físicos, precisamos converter
            double workAreaRightDips = workArea.Right / dpiScale;
            double workAreaBottomDips = workArea.Bottom / dpiScale;

            double toastLeft = workAreaRightDips - ToastWidth - ToastMargin;
            double toastTop = workAreaBottomDips - ToastHeightEstimated - ToastMargin;

            _logger?.LogInfo($"[TOAST] DPI scale={dpiScale:P0} | physical WA=({workArea.Left},{workArea.Top},{workArea.Right},{workArea.Bottom}) | DIP Left={toastLeft:F0} Top={toastTop:F0}");

            return (toastLeft, toastTop, dpiScale, workArea, monitorRect);
        }

        private static void RepositionAllToasts()
        {
            // Snapshot da lista: evita segurar o lock durante o cálculo (que faz P/Invoke de monitor).
            CustomToastWindow[] snapshot;
            lock (_toastLock)
            {
                if (_activeToasts.Count == 0) return;
                snapshot = _activeToasts.ToArray();
            }

            var (_, baseTop, _, _, _) = CalculateToastPosition();

            int activeCount = snapshot.Length;
            int startIndex = Math.Max(0, activeCount - MaxVisibleToasts);

            for (int i = 0; i < activeCount; i++)
            {
                var t = snapshot[i];
                if (t == null) continue;

                if (i < startIndex)
                {
                    // Excedente: fecha sem animação para não piscar.
                    try { t.Close(); } catch { }
                    continue;
                }

                // Mais novos embaixo, mais antigos sobem.
                int stackPosition = activeCount - 1 - i;
                double newTop = baseTop - (stackPosition * (ToastHeightEstimated + ToastStackSpacing));

                try
                {
                    // Idempotente: só move se a posição realmente mudou. Sem isso, cada
                    // repaint re-dispara o reposicionamento e o toast "dança" na tela.
                    if (t.IsLoaded && Math.Abs(t.Top - newTop) > 0.5)
                    {
                        t.Reposition(t.Left, newTop);
                        _logger?.LogInfo($"[TOAST] Reposicionado #{i}: Y={newTop:F0}");
                    }
                }
                catch { }
            }
        }

        #region Win32 Monitor Detection

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public override string ToString() => $"({Left},{Top},{Right},{Bottom})";
        }

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
        private const int MDT_EFFECTIVE_DPI = 0;

        private static System.Drawing.Rectangle GetMonitorWorkArea()
        {
            try
            {
                var hMonitor = GetCurrentMonitorHandle();
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        _logger?.LogInfo($"[TOAST] GetMonitorInfo workArea: ({mi.rcWork.Left},{mi.rcWork.Top},{mi.rcWork.Right},{mi.rcWork.Bottom})");
                        return new System.Drawing.Rectangle(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[TOAST] Erro GetMonitorWorkArea: {ex.Message}");
            }

            // Fallback para SystemParameters.WorkArea (pode estar em DIPs)
            var fallback = SystemParameters.WorkArea;
            _logger?.LogWarning($"[TOAST] Fallback WorkArea: ({fallback.Left},{fallback.Top},{fallback.Right},{fallback.Bottom})");
            return new System.Drawing.Rectangle((int)fallback.Left, (int)fallback.Top, (int)fallback.Right, (int)fallback.Bottom);
        }

        private static System.Drawing.Rectangle GetMonitorRect()
        {
            try
            {
                var hMonitor = GetCurrentMonitorHandle();
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        return new System.Drawing.Rectangle(mi.rcMonitor.Left, mi.rcMonitor.Top,
                            mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    }
                }
            }
            catch { }

            // FIX: Substituir Screen.PrimaryScreen (WinForms — causa freeze/SystemEvents na UI thread)
            // por SystemParameters WPF + user32.dll diretamente.
            try
            {
                return new System.Drawing.Rectangle(
                    0, 0,
                    (int)SystemParameters.PrimaryScreenWidth,
                    (int)SystemParameters.PrimaryScreenHeight);
            }
            catch
            {
                return new System.Drawing.Rectangle(0, 0, 1920, 1080);
            }
        }

        private static double GetMonitorDpiScale()
        {
            try
            {
                var hMonitor = GetCurrentMonitorHandle();
                if (hMonitor != IntPtr.Zero)
                {
                    if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                    {
                        return dpiX / 96.0;
                    }
                }
            }
            catch { }

            // Fallback: estimar pelo SystemParameters
            double estimatedDpi = SystemParameters.PrimaryScreenWidth / SystemParameters.WorkArea.Width * 1.0;
            if (estimatedDpi > 0.8 && estimatedDpi < 3.0)
                return estimatedDpi;

            return 1.0;
        }

        private static IntPtr GetCurrentMonitorHandle()
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow);
                    var hwnd = helper.Handle;
                    if (hwnd != IntPtr.Zero)
                        return MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                }
            }
            catch { }

            // FIX: Fallback 100% WPF/Win32 nativo — sem Screen.PrimaryScreen (WinForms).
            // MonitorFromWindow com HWND nulo retorna o monitor primário de forma segura.
            return MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTONEAREST);
        }

        #endregion

        private static void RemoveExpiredNotifications()
        {
            if (_innerService == null) return;
            var cutoff = DateTime.Now.AddHours(-24);
            var history = _innerService.NotificationHistory;
            var expired = history.Where(n => n.Timestamp < cutoff).ToList();
            foreach (var n in expired)
                _innerService.MarkAsRead(n.Id);
        }

        private static readonly Regex EmojiRegex = new(
            @"[\uD800-\uDFFF]|\uFE0F|\u20E3|\u00A9|\u00AE|\u2122|\u2600-\u27BF|\u2B00-\u2BFF|\u2190-\u21FF|\u2300-\u23FF|\u25A0-\u25FF|\u2B05-\u2B07",
            RegexOptions.Compiled);

        /// <summary>
        /// Remove emojis/glifos de conclusão (🎯, ⚡, ✅, ❌, etc.) do texto da notificação.
        /// O ícone moderno do cabeçalho é o ÚNICO ícone exibido — o texto deve ser plano.
        /// </summary>
        public static string StripEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return EmojiRegex.Replace(text, string.Empty).Trim();
        }
    }
}
