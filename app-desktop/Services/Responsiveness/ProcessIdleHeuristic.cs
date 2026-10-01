using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Responsiveness
{
    /// <summary>
    /// Heurística de idle para o Startup Mode.
    ///
    /// PROBLEMA ORIGINAL: WS > 200MB sozinho não indica processo pesado.
    /// Um processo pode ter 500MB de RAM e estar completamente idle.
    /// Throttlar esse processo causaria efeito contrário ao desejado
    /// (o usuário pode abrir esse app logo depois).
    ///
    /// SOLUÇÃO: Combinar WS com indicadores reais de atividade:
    ///   1. I/O rate via contadores de performance (se disponível)
    ///   2. Prioridade atual do processo (se já está em BelowNormal, está idle)
    ///   3. Tempo desde o último acesso ao processo (via StartTime como proxy)
    ///   4. Whitelist de processos conhecidamente ativos no startup
    ///
    /// CRITÉRIO FINAL: processo é "pesado e idle" se:
    ///   - WS > threshold E
    ///   - NÃO está na whitelist de ativos E
    ///   - Prioridade atual <= Normal (não foi priorizado por ninguém) E
    ///   - Não tem janela visível (MainWindowHandle == Zero) — proxy para background
    /// </summary>
    internal static class ProcessIdleHeuristic
    {
        /// <summary>
        /// Processos que tipicamente estão ativos no startup e NÃO devem ser throttlados.
        /// São processos que o usuário provavelmente vai interagir logo.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> _startupActiveWhitelist =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Browsers — usuário provavelmente vai usar
                "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
                // Comunicação — notificações importantes
                "Discord", "DiscordPTB", "Slack", "Teams", "Zoom", "WhatsApp",
                // Launchers de jogos — usuário pode querer abrir jogo
                "Steam", "EpicGamesLauncher", "Battle.net", "Origin", "Uplay",
                // IDEs e editores — usuário pode estar trabalhando
                "devenv", "Code", "rider64", "idea64", "pycharm64",
                // Explorador de arquivos
                "explorer"
            };

        /// <summary>
        /// Avalia se um processo é candidato a throttle no startup.
        /// Retorna true apenas se o processo for genuinamente pesado E idle.
        /// </summary>
        public static bool IsHeavyAndIdle(Process p, long wsThresholdBytes)
        {
            try
            {
                if (p.HasExited) return false;

                // 1. Working Set mínimo
                if (p.WorkingSet64 < wsThresholdBytes) return false;

                // 2. Whitelist de processos ativos no startup
                if (_startupActiveWhitelist.Contains(p.ProcessName)) return false;

                // 3. Se o processo já tem prioridade elevada, alguém o priorizou — respeitar
                if (p.PriorityClass >= ProcessPriorityClass.AboveNormal) return false;

                // 4. Proxy de background: sem janela principal visível
                // Processos com janela visível provavelmente estão sendo usados
                bool hasVisibleWindow = p.MainWindowHandle != IntPtr.Zero;
                if (hasVisibleWindow) return false;

                // 5. Processo recém-iniciado (< 30s) pode estar carregando — não throttlar
                try
                {
                    var age = DateTime.Now - p.StartTime;
                    if (age.TotalSeconds < 30) return false;
                }
                catch { /* StartTime pode lançar para processos do sistema */ }

                // Passou em todos os critérios: é pesado e idle
                return true;
            }
            catch { return false; }
        }
    }
}
