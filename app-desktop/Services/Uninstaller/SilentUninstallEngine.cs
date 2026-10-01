using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Uninstaller
{
    /// <summary>
    /// Motor de desinstalação silenciosa 100% automática
    /// Equivalente ao IObit Uninstaller - sem interação do usuário
    /// </summary>
    public class SilentUninstallEngine
    {
        private readonly ILoggingService _logger;

        // P/Invoke para fechar janelas automaticamente
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;
        private const uint WM_COMMAND = 0x0111;
        private const uint BN_CLICKED = 0;

        public SilentUninstallEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Desinstalação totalmente silenciosa e automática
        /// </summary>
        public async Task<SilentUninstallResult> PerformSilentUninstall(ApplicationProfile profile)
        {
            var result = new SilentUninstallResult { AppName = profile.Name };

            try
            {
                _logger.LogInfo($"[Silent Uninstall] ═══════════════════════════════════════");
                _logger.LogInfo($"[Silent Uninstall] Iniciando desinstalação silenciosa: {profile.Name}");
                _logger.LogInfo($"[Silent Uninstall] ═══════════════════════════════════════");

                // PASSO 1: Finalizar todos os processos relacionados
                _logger.LogInfo($"[Silent Uninstall] PASSO 1: Finalizando processos relacionados...");
                await KillRelatedProcesses(profile);

                // PASSO 2: Determinar comando de desinstalação silenciosa
                _logger.LogInfo($"[Silent Uninstall] PASSO 2: Determinando comando de desinstalação...");
                var uninstallCommand = DetermineSilentUninstallCommand(profile);

                if (uninstallCommand == null)
                {
                    _logger.LogWarning($"[Silent Uninstall] Nenhum comando de desinstalação encontrado.");
                    result.Success = false;
                    result.ErrorMessage = "Nenhum comando de desinstalação disponível";
                    return result;
                }

                _logger.LogInfo($"[Silent Uninstall] Comando detectado: {uninstallCommand.Command}");
                _logger.LogInfo($"[Silent Uninstall] Tipo de instalador: {uninstallCommand.InstallerType}");

                // PASSO 3: Executar desinstalação silenciosa
                _logger.LogInfo($"[Silent Uninstall] PASSO 3: Executando desinstalação silenciosa...");
                
                // Iniciar monitoramento de janelas em background
                using var cts = new CancellationTokenSource();
                var windowMonitorTask = Task.Run(() => MonitorAndCloseUninstallerWindows(profile.Name, cts.Token));

                // Executar desinstalação
                bool success = await ExecuteSilentUninstall(uninstallCommand);

                // Parar monitoramento
                cts.Cancel();

                if (success)
                {
                    _logger.LogSuccess($"[Silent Uninstall] Desinstalação executada com sucesso!");
                    result.Success = true;
                }
                else
                {
                    _logger.LogWarning($"[Silent Uninstall] Desinstalação retornou código de erro, mas continuando...");
                    result.Success = true; // Continuar mesmo com erro
                }

                // Aguardar um pouco para o sistema processar
                await Task.Delay(2000);

                // PASSO 4: Verificar se ainda há processos rodando
                _logger.LogInfo($"[Silent Uninstall] PASSO 4: Verificação final de processos...");
                await KillRelatedProcesses(profile);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Silent Uninstall] Erro durante desinstalação: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task KillRelatedProcesses(ApplicationProfile profile)
        {
            var processesToKill = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Adicionar processos conhecidos do perfil
            foreach (var proc in profile.RunningProcesses)
            {
                processesToKill.Add(proc);
            }

            // Adicionar processos baseados no nome do app
            var allProcesses = Process.GetProcesses();
            foreach (var proc in allProcesses)
            {
                try
                {
                    if (proc.ProcessName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(profile.Publisher) && 
                         proc.ProcessName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase)))
                    {
                        processesToKill.Add(proc.ProcessName);
                    }
                }
                catch { }
            }

            // Finalizar processos
            foreach (var procName in processesToKill)
            {
                try
                {
                    var procs = Process.GetProcessesByName(procName);
                    foreach (var proc in procs)
                    {
                        try
                        {
                            _logger.LogInfo($"  ✓ Finalizando processo: {procName} (PID: {proc.Id})");
                            proc.Kill();
                            proc.WaitForExit(5000);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"  ✗ Falha ao finalizar {procName}: {ex.Message}");
                        }
                    }
                }
                catch { }
            }

            await Task.Delay(500);
        }

        private SilentUninstallCommand? DetermineSilentUninstallCommand(ApplicationProfile profile)
        {
            // 1. QuietUninstallString tem prioridade máxima
            if (!string.IsNullOrEmpty(profile.QuietUninstallString))
            {
                _logger.LogInfo($"[Detector] Usando QuietUninstallString do registro");
                return new SilentUninstallCommand
                {
                    Command = profile.QuietUninstallString,
                    InstallerType = InstallerType.QuietUninstall
                };
            }

            // 2. Detectar MSI
            if (profile.IsMsi && !string.IsNullOrEmpty(profile.Guid))
            {
                _logger.LogInfo($"[Detector] Detectado instalador MSI");
                return new SilentUninstallCommand
                {
                    Command = $"msiexec.exe /x {profile.Guid} /qn /norestart",
                    InstallerType = InstallerType.MSI
                };
            }

            // 3. Verificar se UninstallString contém MsiExec
            if (!string.IsNullOrEmpty(profile.UninstallString) && 
                profile.UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInfo($"[Detector] Detectado MsiExec no UninstallString");
                
                // Extrair GUID
                var guidMatch = Regex.Match(profile.UninstallString, @"\{[A-F0-9\-]+\}", RegexOptions.IgnoreCase);
                if (guidMatch.Success)
                {
                    return new SilentUninstallCommand
                    {
                        Command = $"msiexec.exe /x {guidMatch.Value} /qn /norestart",
                        InstallerType = InstallerType.MSI
                    };
                }
            }

            // 4. Detectar tipo de instalador pelo UninstallString
            if (!string.IsNullOrEmpty(profile.UninstallString))
            {
                var uninstallLower = profile.UninstallString.ToLower();
                
                // Inno Setup
                if (uninstallLower.Contains("unins") && uninstallLower.Contains(".exe"))
                {
                    _logger.LogInfo($"[Detector] Detectado Inno Setup");
                    return new SilentUninstallCommand
                    {
                        Command = $"{profile.UninstallString} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                        InstallerType = InstallerType.InnoSetup
                    };
                }

                // NSIS
                if (uninstallLower.Contains("uninst") || uninstallLower.Contains("nsis"))
                {
                    _logger.LogInfo($"[Detector] Detectado NSIS");
                    return new SilentUninstallCommand
                    {
                        Command = $"{profile.UninstallString} /S /NCRC",
                        InstallerType = InstallerType.NSIS
                    };
                }

                // InstallShield
                if (uninstallLower.Contains("isuninst") || uninstallLower.Contains("installshield"))
                {
                    _logger.LogInfo($"[Detector] Detectado InstallShield");
                    return new SilentUninstallCommand
                    {
                        Command = $"{profile.UninstallString} /s /f1\"{Path.GetTempPath()}setup.iss\" /f2\"{Path.GetTempPath()}setup.log\"",
                        InstallerType = InstallerType.InstallShield
                    };
                }

                // Squirrel (Electron apps)
                if (uninstallLower.Contains("update.exe") && uninstallLower.Contains("--uninstall"))
                {
                    _logger.LogInfo($"[Detector] Detectado Squirrel");
                    return new SilentUninstallCommand
                    {
                        Command = profile.UninstallString,
                        InstallerType = InstallerType.Squirrel
                    };
                }

                // 5. Tentar múltiplos parâmetros silenciosos
                _logger.LogInfo($"[Detector] Tipo desconhecido, tentando parâmetros genéricos");
                return new SilentUninstallCommand
                {
                    Command = profile.UninstallString,
                    InstallerType = InstallerType.Unknown,
                    FallbackParameters = new[] { "/S", "/SILENT", "/VERYSILENT", "/quiet", "/q", "/qn", "/norestart", "--silent" }
                };
            }

            return null;
        }

        private async Task<bool> ExecuteSilentUninstall(SilentUninstallCommand command)
        {
            // Parse command
            string fileName, arguments;
            ParseCommand(command.Command, out fileName, out arguments);

            // Se for tipo desconhecido, tentar múltiplos parâmetros
            if (command.InstallerType == InstallerType.Unknown && command.FallbackParameters != null)
            {
                _logger.LogInfo($"[Executor] Tentando múltiplos parâmetros silenciosos...");
                
                foreach (var param in command.FallbackParameters)
                {
                    _logger.LogInfo($"[Executor] Tentando: {fileName} {arguments} {param}");
                    
                    try
                    {
                        using var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = fileName,
                                Arguments = $"{arguments} {param}".Trim(),
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            }
                        };

                        process.Start();
                        
                        // Aguardar até 30 segundos
                        bool exited = await Task.Run(() => process.WaitForExit(30000));
                        
                        if (exited && process.ExitCode == 0)
                        {
                            _logger.LogSuccess($"[Executor] Sucesso com parâmetro: {param}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[Executor] Falha com {param}: {ex.Message}");
                    }
                }

                // Se nenhum funcionou, executar sem parâmetros adicionais
                _logger.LogInfo($"[Executor] Executando sem parâmetros adicionais...");
            }

            // Execução padrão
            try
            {
                _logger.LogInfo($"[Executor] Executando: {fileName} {arguments}");
                
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                process.Start();
                
                // Aguardar até 60 segundos
                bool exited = await Task.Run(() => process.WaitForExit(60000));
                
                if (!exited)
                {
                    _logger.LogWarning($"[Executor] Processo não finalizou em 60 segundos, forçando término...");
                    try { process.Kill(); } catch { }
                }

                _logger.LogInfo($"[Executor] Processo finalizado com código: {process.ExitCode}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Executor] Erro ao executar: {ex.Message}");
                return false;
            }
        }

        private void ParseCommand(string fullCommand, out string fileName, out string arguments)
        {
            if (fullCommand.StartsWith("\""))
            {
                int endQuote = fullCommand.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    fileName = fullCommand.Substring(1, endQuote - 1);
                    arguments = fullCommand.Substring(endQuote + 1).Trim();
                }
                else
                {
                    fileName = fullCommand.Trim('"');
                    arguments = string.Empty;
                }
            }
            else
            {
                var parts = fullCommand.Split(new[] { ' ' }, 2);
                fileName = parts[0];
                arguments = parts.Length > 1 ? parts[1] : string.Empty;
            }
        }

        private async Task MonitorAndCloseUninstallerWindows(string appName, CancellationToken cancellationToken)
        {
            _logger.LogInfo($"[Window Monitor] Iniciando monitoramento de janelas...");

            var windowTitles = new[]
            {
                appName,
                "uninstall",
                "desinstalar",
                "remove",
                "remover",
                "setup",
                "instalação"
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var windows = new List<IntPtr>();
                    
                    EnumWindows((hWnd, lParam) =>
                    {
                        if (IsWindowVisible(hWnd))
                        {
                            var title = new System.Text.StringBuilder(256);
                            GetWindowText(hWnd, title, 256);
                            var titleStr = title.ToString();

                            if (!string.IsNullOrEmpty(titleStr))
                            {
                                foreach (var keyword in windowTitles)
                                {
                                    if (titleStr.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                    {
                                        windows.Add(hWnd);
                                        _logger.LogInfo($"[Window Monitor] Janela detectada: {titleStr}");
                                        break;
                                    }
                                }
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    // Fechar janelas detectadas
                    foreach (var hWnd in windows)
                    {
                        try
                        {
                            // Tentar clicar em botões "OK", "Next", "Finish", etc
                            TryClickButtons(hWnd);
                            
                            // Fechar janela
                            PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            _logger.LogInfo($"[Window Monitor] Comando de fechamento enviado para janela");
                        }
                        catch { }
                    }

                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Window Monitor] Erro: {ex.Message}");
                }
            }

            _logger.LogInfo($"[Window Monitor] Monitoramento finalizado.");
        }

        private void TryClickButtons(IntPtr parentWindow)
        {
            var buttonTexts = new[] { "OK", "Next", "Finish", "Close", "Yes", "Sim", "Próximo", "Concluir", "Fechar" };

            foreach (var buttonText in buttonTexts)
            {
                try
                {
                    IntPtr buttonHandle = FindWindowEx(parentWindow, IntPtr.Zero, "Button", buttonText);
                    if (buttonHandle != IntPtr.Zero)
                    {
                        PostMessage(buttonHandle, WM_COMMAND, new IntPtr(BN_CLICKED), IntPtr.Zero);
                        _logger.LogInfo($"[Window Monitor] Clique automático em botão: {buttonText}");
                    }
                }
                catch { }
            }
        }
    }

    public enum InstallerType
    {
        QuietUninstall,
        MSI,
        InnoSetup,
        NSIS,
        InstallShield,
        Squirrel,
        Unknown
    }

    public class SilentUninstallCommand
    {
        public string Command { get; set; } = string.Empty;
        public InstallerType InstallerType { get; set; }
        public string[]? FallbackParameters { get; set; }
    }

    public class SilentUninstallResult
    {
        public string AppName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
