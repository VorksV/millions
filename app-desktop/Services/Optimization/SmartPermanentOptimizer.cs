using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Optimization.Unification;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// SMART PERMANENT OPTIMIZER - "The Enterprise Core"
    /// Aplica otimizações definitivas no Windows que não precisam ser revertidas,
    /// focando em Telemetria, Agendador de Tarefas e Latência de Sistema.
    /// Respeita 100% o Perfil Inteligente selecionado.
    /// </summary>
    public class SmartPermanentOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly IntelligentDecisionEngine _decisionEngine;

        public SmartPermanentOptimizer(ILoggingService logger, IntelligentDecisionEngine decisionEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        }

        /// <summary>
        /// Executa a otimização inteligente baseada no perfil atual.
        /// </summary>
        public async Task ExecuteSmartOptimizationAsync()
        {
            _logger.LogInfo("🚀 Iniciando Otimização Inteligente Nível Enterprise...");

            try
            {
                // 1. Otimização de Telemetria e Tarefas Agendadas (Seguro para todos os perfis)
                await OptimizeScheduledTasksAsync();

                // 2. Otimizações de Registro (Network, MMCSS, Mouse) baseadas no perfil
                ApplyRegistryTweaks();

                // 3. Otimização de Inicialização
                OptimizeStartupDelay();

                _logger.LogSuccess("✅ Otimização Inteligente aplicada com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SmartOptimizer] Falha crítica na otimização: {ex.Message}");
            }
        }

        private async Task OptimizeScheduledTasksAsync()
        {
            _logger.LogInfo("[SmartOptimizer] Higienizando Agendador de Tarefas (Exterminando Telemetria)...");

            string[] tasksToDisable = new[]
            {
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"\Microsoft\Windows\Application Experience\StartupAppTask",
                @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
                @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                @"\Microsoft\Windows\Autochk\Proxy",
                @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
                @"\Microsoft\Windows\Shell\FamilySafetyMonitor",
                @"\Microsoft\Windows\Windows Error Reporting\QueueReporting"
            };

            foreach (var taskPath in tasksToDisable)
            {
                await RunCommandAsync("schtasks", $"/Change /TN \"{taskPath}\" /Disable");
            }
        }

        private void ApplyRegistryTweaks()
        {
            var profile = _decisionEngine.GetCurrentProfile();
            _logger.LogInfo($"[SmartOptimizer] Aplicando Tweaks de Registro para o perfil: {profile}");

            // --- TWEAKS GERAIS (Seguros para todos) ---
            
            // 1. Menu Show Delay (Responsividade da UI) - delay reduzido para 100ms (ideal)
            Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "MenuShowDelay", "100", RegistryValueKind.String);

            // 2. Telemetria Geral
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord);

            // 3. WaitToKillServiceTimeout - reduz timeout de serviços no desligamento
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", 5000, RegistryValueKind.DWord);

            // 4. AutoRestartShell - reinício automático do Explorer em caso de falha
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoRestartShell", 1, RegistryValueKind.DWord);

            // 5. SeparateProcess - pastas do Explorer em processos separados
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", 1, RegistryValueKind.DWord);

            // 6. ClearPageFileAtShutdown - desativar limpeza do pagefile ao desligar (economiza I/O em SSD)
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "ClearPageFileAtShutdown", 0, RegistryValueKind.DWord);

            // --- TWEAKS ESPECÍFICOS POR PERFIL ---

            if (profile == IntelligentProfileType.GamerCompetitive || profile == IntelligentProfileType.GamerSinglePlayer)
            {
                // Network Throttling Index (Remover limite de rede para multimídia)
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                
                // System Responsiveness (10 = otimizado sem prejudicar background)
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10, RegistryValueKind.DWord);

                // Jogos: GPU Priority
                string gameTaskPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
                Registry.SetValue(gameTaskPath, "GPU Priority", 8, RegistryValueKind.DWord);
                Registry.SetValue(gameTaskPath, "Priority", 6, RegistryValueKind.DWord);
                Registry.SetValue(gameTaskPath, "Scheduling Category", "High", RegistryValueKind.String);
            }
            
            if (profile != IntelligentProfileType.EnterpriseSecure)
            {
                // Desativar GameDVR (Pode ser invasivo em Enterprise, então evitamos)
                Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
            }
        }

        private void OptimizeStartupDelay()
        {
            // Remove o delay artificial de 10s que o Windows coloca no boot para carregar apps da tray
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, RegistryValueKind.DWord);
        }

        private async Task RunCommandAsync(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process != null) await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[SmartOptimizer] Falha ao executar comando {fileName}: {ex.Message}");
            }
        }
    }
}
