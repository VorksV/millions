using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Serviço central responsável por gerenciar alterações no Windows Shell
    /// Detecta automaticamente a versão do Windows e aplica o método correto de atualização
    /// Windows 10: Broadcast WM_SETTINGCHANGE
    /// Windows 11: Restart ShellExperienceHost
    /// </summary>
    public class WindowsShellControlService
    {
        private readonly ILoggingService _logger;
        private readonly bool _isWindows11;
        
        // P/Invoke para broadcast de mensagens
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);
        
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const int HWND_BROADCAST = 0xFFFF;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public WindowsShellControlService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isWindows11 = WindowsCompatibilityHelper.IsWindows11();
            _logger.LogEntry(nameof(WindowsShellControlService));
            _logger.LogInfo($"[ShellControl] 🖥️ Sistema detectado: {(_isWindows11 ? "Windows 11" : "Windows 10")}");
            _logger.LogExit(nameof(WindowsShellControlService));
        }

        /// <summary>
        /// Aplica mudanças no shell de forma inteligente baseado na versão do Windows
        /// </summary>
        public void ApplyShellChanges(string context = "Shell")
        {
            _logger.LogEntry(nameof(ApplyShellChanges), ("context", context));
            try
            {
                _logger.LogInfo($"[ShellControl] 🔄 Aplicando mudanças no {context}...");
                
                if (_isWindows11)
                {
                    ApplyWindows11ShellRefresh();
                }
                else
                {
                    ApplyWindows10ShellRefresh();
                }
                
                _logger.LogSuccess($"[ShellControl] ✅ Mudanças aplicadas com sucesso ({context})");
                _logger.LogExit(nameof(ApplyShellChanges));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShellControl] Erro ao aplicar mudanças no shell: {ex.Message}", ex);
                _logger.LogExit(nameof(ApplyShellChanges));
            }
        }

        /// <summary>
        /// Windows 10: Broadcast WM_SETTINGCHANGE para atualizar sem reiniciar Explorer
        /// </summary>
        private void ApplyWindows10ShellRefresh()
        {
            _logger.LogEntry(nameof(ApplyWindows10ShellRefresh));
            try
            {
                _logger.LogInfo("[ShellControl] 📡 Enviando WM_SETTINGCHANGE broadcast (Windows 10)...");
                
                // Broadcast 1: TraySettings - Atualiza configurações da barra de tarefas
                _logger.LogInfo("[ShellControl]   → Broadcast: TraySettings");
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "TraySettings",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                
                // Broadcast 2: Policy - Força atualização de políticas
                _logger.LogInfo("[ShellControl]   → Broadcast: Policy");
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "Policy",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                
                // Broadcast 3: ImmersiveColorSet - Atualiza Search Highlights
                _logger.LogInfo("[ShellControl]   → Broadcast: ImmersiveColorSet");
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "ImmersiveColorSet",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                
                // Broadcast 4: Environment - Configurações do ambiente
                _logger.LogInfo("[ShellControl]   → Broadcast: Environment");
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "Environment",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                
                // Broadcast 5: NULL - Broadcast geral para News & Interests
                _logger.LogInfo("[ShellControl]   → Broadcast: NULL (geral)");
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    null,
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                
                // Aguardar um momento para o Windows processar
                System.Threading.Thread.Sleep(500);
                
                _logger.LogSuccess("[ShellControl] ✅ Todos os broadcasts enviados - mudanças devem estar visíveis");
                _logger.LogExit(nameof(ApplyWindows10ShellRefresh));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShellControl] Falha no broadcast: {ex.Message}");
                _logger.LogExit(nameof(ApplyWindows10ShellRefresh));
            }
        }

        /// <summary>
        /// Windows 11: Reinicia apenas ShellExperienceHost.exe
        /// Evita reiniciar Explorer.exe e fechar janelas abertas
        /// </summary>
        private void ApplyWindows11ShellRefresh()
        {
            _logger.LogEntry(nameof(ApplyWindows11ShellRefresh));
            try
            {
                _logger.LogInfo("[ShellControl] 🔄 Reiniciando ShellExperienceHost (Windows 11)...");
                
                // Encontrar e encerrar ShellExperienceHost
                var shellProcesses = Process.GetProcessesByName("ShellExperienceHost");
                
                if (shellProcesses.Length > 0)
                {
                    foreach (var proc in shellProcesses)
                    {
                        try
                        {
                            _logger.LogInfo($"[ShellControl]   → Encerrando ShellExperienceHost (PID: {proc.Id})");
                            proc.Kill();
                            proc.WaitForExit(2000);
                            _logger.LogSuccess($"[ShellControl]   ✓ ShellExperienceHost encerrado (PID: {proc.Id})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[ShellControl] Falha ao encerrar processo: {ex.Message}");
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    
                    // Aguardar o Windows reiniciar automaticamente
                    System.Threading.Thread.Sleep(1000);
                    _logger.LogSuccess("[ShellControl] ✅ ShellExperienceHost será reiniciado automaticamente pelo Windows");
                }
                else
                {
                    _logger.LogWarning("[ShellControl] ShellExperienceHost não está em execução");
                }
                
                // Também reiniciar SearchHost para aplicar Search Highlights
                _logger.LogInfo("[ShellControl] 🔍 Reiniciando SearchHost para aplicar Search Highlights...");
                var searchProcesses = Process.GetProcessesByName("SearchHost");
                
                if (searchProcesses.Length > 0)
                {
                    foreach (var proc in searchProcesses)
                    {
                        try
                        {
                            _logger.LogInfo($"[ShellControl]   → Encerrando SearchHost (PID: {proc.Id})");
                            proc.Kill();
                            proc.WaitForExit(2000);
                            _logger.LogSuccess($"[ShellControl]   ✓ SearchHost encerrado (PID: {proc.Id})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[ShellControl] Falha ao encerrar SearchHost: {ex.Message}");
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    
                    System.Threading.Thread.Sleep(500);
                }
                
                _logger.LogSuccess("[ShellControl] ✅ Processos reiniciados - mudanças devem estar visíveis");
                _logger.LogExit(nameof(ApplyWindows11ShellRefresh));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShellControl] Erro ao reiniciar ShellExperienceHost: {ex.Message}", ex);
                _logger.LogExit(nameof(ApplyWindows11ShellRefresh));
            }
        }

        /// <summary>
        /// Método de fallback: reinicia Explorer.exe (usado apenas em casos extremos)
        /// ATENÇÃO: Fecha todas as janelas do Explorer
        /// </summary>
        public void RestartExplorerFallback()
        {
            _logger.LogEntry(nameof(RestartExplorerFallback));
            try
            {
                _logger.LogWarning("[ShellControl] ⚠️ Usando fallback: reiniciando Explorer.exe...");
                _logger.LogWarning("[ShellControl] ATENÇÃO: Janelas do Explorer serão fechadas temporariamente");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c taskkill /f /im explorer.exe & timeout /t 1 /nobreak > nul & start explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                Process.Start(psi);
                _logger.LogInfo("[ShellControl] Explorer.exe reiniciado");
                _logger.LogExit(nameof(RestartExplorerFallback));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShellControl] Erro ao reiniciar Explorer: {ex.Message}", ex);
                _logger.LogExit(nameof(RestartExplorerFallback));
            }
        }
    }
}
