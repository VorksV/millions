using System;
using System.Security.Principal;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    public class StartupManager
    {
        private const string TaskName = "VoltrisOptimizer_Startup";
        private const string ValueName = "Voltris Optimizer";
        private const string LegacyValueName = "VoltrisOptimizer";
        private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private readonly ILoggingService? _logger;

        public StartupManager(ILoggingService? logger)
        {
            _logger = logger;
        }

        public void SetStartup(bool enable, bool startMinimized)
        {
            try
            {
                if (enable)
                {
                    var exePath = GetExecutablePath();

                    // [FIX:AUTOSTART-DETECT] O argumento "--autostart" é o que
                    // permite ao app distinguir, com certeza, "o Windows iniciou
                    // este programa" de "o usuário clicou no executável".
                    //
                    // Sem ele, as duas situations são indistinguíveis: o mesmo
                    // executável, sem argumentos, nos dois casos. Qualquer
                    // tentativa de adivinhar por registro ou por tempo de
                    // inicialização erra, e errar para o lado do auto-start
                    // significa esconder a janela de um clique manual — que é
                    // exatamente o defeito relatado.
                    //
                    // "--minimized" continua sendo gravado quando o usuário pede,
                    // mas deixa de ser a ÚNICA forma de chegar à bandeja: o
                    // auto-start já chega marcado e já sobe para a bandeja.
                    var valueData = startMinimized
                        ? $"\"{exePath}\" {StartupAutostartDetector.AutostartFlag} --minimized"
                        : $"\"{exePath}\" {StartupAutostartDetector.AutostartFlag}";

                    // 1. Task Scheduler: inicia o app elevado sem UAC no login
                    EnableTaskStartup(startMinimized);
                    _logger?.LogSuccess($"[StartupManager] Task Scheduler configurado. Minimizado: {startMinimized}");

                    // 2. Registro HKCU\Run: aparece no Gerenciador de Tarefas
                    TryWriteRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRunPath, ValueName, valueData);

                    // 3. StartupApproved: marca como habilitado no Gerenciador de Tarefas
                    var enabledData = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    TryWriteRegistryBinary(RegistryHive.CurrentUser, RegistryView.Registry64, StartupApprovedPath, ValueName, enabledData);

                    // 4. Limpar entradas antigas (evitar duplicidade)
                    TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRunPath, LegacyValueName);
                    TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry32, RegistryRunPath, LegacyValueName);

                    _logger?.LogSuccess($"[StartupManager] Startup ativado: Task Scheduler + Registro. Minimizado: {startMinimized}");
                }
                else
                {
                    RemoveRegistryEntry();
                    DisableTaskStartup();
                    RemoveStartupApprovedEntry();
                    _logger?.LogInfo("[StartupManager] Startup desabilitado. Task Scheduler e Registro limpos.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[StartupManager] Falha ao configurar startup: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// [FIX:AUTOSTART-DETECT] Garante que a entrada de auto-start do registro
        /// do usuário carregue o argumento "--autostart".
        ///
        /// POR QUE ISSO PRECISA EXISTIR
        /// ============================
        /// O app se registra em DOIS lugares: no Agendador de Tarefas (que exige
        /// elevação, por rodar com RunLevel=HighestAvailable) e em
        /// HKCU\...\Run (que qualquer processo do usuário pode gravar). Só o
        /// registro pode ser corrigido sem privilégio administrativo, então é
        /// ele que garante o sinalizador de origem.
        ///
        /// Sem o sinalizador, o logon do Windows inicia o app por um comando
        /// idêntico ao de um clique manual — e a aplicação não tem como saber
        /// qual dos dois aconteceu. O resultado é a janela aparecer no logon
        /// mesmo quando o usuário pediu para o app subir direto para a bandeja.
        ///
        /// O método é idempotente: se o valor já está correto, não escreve nada.
        /// </summary>
        /// <returns><c>true</c> se a entrada existe e está correta ao final.</returns>
        public bool EnsureAutostartEntryHasMarker()
        {
            try
            {
                var exePath = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return false;
                }

                var startMinimized = SettingsService.Instance?.Settings?.StartMinimized ?? false;
                var desired = startMinimized
                    ? $"\"{exePath}\" {StartupAutostartDetector.AutostartFlag} --minimized"
                    : $"\"{exePath}\" {StartupAutostartDetector.AutostartFlag}";

                using (var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true))
                {
                    if (key == null)
                    {
                        _logger?.LogInfo("[StartupManager] Entrada de auto-start ausente; nada a corrigir no registro.");
                        return false;
                    }

                    var current = key.GetValue(ValueName) as string;
                    if (!string.IsNullOrEmpty(current) &&
                        current.IndexOf(StartupAutostartDetector.AutostartFlag, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    TryWriteRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRunPath, ValueName, desired);
                    TryWriteRegistry(RegistryHive.CurrentUser, RegistryView.Registry32, RegistryRunPath, ValueName, desired);

                    _logger?.LogInfo(
                        "[StartupManager] Auto-start corrigido: argumento --autostart gravado na entrada do registro. " +
                        "Sem ele o logon do Windows seria indistinguivel de um clique manual.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[StartupManager] Não foi possível garantir o marcador de auto-start: {ex.Message}");
                return false;
            }
        }

        public bool IsStartupEnabled(out bool startsMinimized)
        {
            startsMinimized = false;

            // 1. Verificar Task Scheduler (mecanismo REAL de startup)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/query /tn \"{TaskName}\" /v /fo csv",
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;

                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        var psiXml = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/query /tn \"{TaskName}\" /xml",
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var pXml = Process.Start(psiXml))
                        {
                            if (pXml != null)
                            {
                                string xml = pXml.StandardOutput.ReadToEnd();
                                pXml.WaitForExit();
                                startsMinimized = xml.Contains("--minimized");
                            }
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[StartupManager] Erro ao verificar Task Scheduler: {ex.Message}");
            }

            // 2. Fallback: verificar no Registro HKCU\Run
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var rk = baseKey.OpenSubKey(RegistryRunPath, false))
                {
                    if (rk != null)
                    {
                        var val = rk.GetValue(ValueName) as string;
                        if (!string.IsNullOrEmpty(val))
                        {
                            startsMinimized = val.Contains("--minimized");
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public bool IsStartupEnabled() => IsStartupEnabled(out _);

        /// <summary>
        /// Verifica se o usuario desabilitou a entrada pelo Gerenciador de Tarefas.
        /// Retorna true se o StartupApproved estiver marcado como disabled.
        /// </summary>
        public bool IsDisabledByTaskManager()
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var rk = baseKey.OpenSubKey(StartupApprovedPath, false))
                {
                    if (rk?.GetValue(ValueName) is byte[] data && data.Length > 0)
                    {
                        // 0x02 = enabled, 0x03 = disabled
                        return data[0] == 0x03;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Sincroniza os mecanismos: se o usuario desabilitou pelo Gerenciador de Tarefas,
        /// desabilita tambem o Task Scheduler.
        /// </summary>
        public void SyncWithTaskManagerState()
        {
            try
            {
                if (IsDisabledByTaskManager())
                {
                    var taskExists = false;
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/query /tn \"{TaskName}\"",
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var p = Process.Start(psi))
                        {
                            p?.WaitForExit();
                            taskExists = p?.ExitCode == 0;
                        }
                    }
                    catch { }

                    if (taskExists)
                    {
                        DisableTaskStartup();
                        RemoveRegistryEntry();
                        _logger?.LogInfo("[StartupManager] Task Scheduler desabilitado porque usuario desativou no Gerenciador de Tarefas.");
                    }
                }
            }
            catch { }
        }

        private void EnableTaskStartup(bool startMinimized)
        {
            var exePath = GetExecutablePath();

            // [FIX:AUTOSTART-DETECT] Mesmo sinalizador na task: sem ele, a task
            // do Agendador de Tarefas inicia o app de forma indistinguível de
            // um clique manual e a janela é escondida à toa.
            var args = startMinimized
                ? $"{StartupAutostartDetector.AutostartFlag} --minimized"
                : StartupAutostartDetector.AutostartFlag;

            // Verificar se o processo já tem privilégios de administrador
            bool isAdmin = IsRunningAsAdministrator();

            try
            {
                var xmlPath = Path.Combine(Path.GetTempPath(), "VoltrisStartup.xml");

                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Inicialização automática do Voltris Optimizer.</Description>
    <Author>VOLTRIS</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT2S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
      <Arguments>{args}</Arguments>
      <WorkingDirectory>{Path.GetDirectoryName(exePath)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

                File.WriteAllText(xmlPath, taskXml, System.Text.Encoding.Unicode);

                // Remover tarefa anterior (sem exigir admin — /delete de tarefa própria é permitido)
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/delete /tn \"{TaskName}\" /f",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false
                    })?.WaitForExit();
                }
                catch { /* ignorar — pode não existir */ }

                bool taskCreated = false;

                if (isAdmin)
                {
                    // Processo já é admin: criação direta, sem UAC
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardError = true
                    };

                    using var p = Process.Start(psi);
                    p?.WaitForExit();
                    taskCreated = p?.ExitCode == 0;

                    if (!taskCreated)
                    {
                        var stderr = p != null ? p.StandardError.ReadToEnd() : "";
                        _logger?.LogWarning($"[StartupManager] schtasks (modo admin) falhou (exit={p?.ExitCode}): {stderr?.Trim()}");
                    }
                }
                else
                {
                    // Sem admin: tentar elevar com UAC via ShellExecute + runas
                    try
                    {
                        var psiElevated = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = true,
                            Verb = "runas"
                        };

                        using var p = Process.Start(psiElevated);
                        p?.WaitForExit(5000); // Aguardar no máximo 5 segundos (UAC pode aparecer)
                        taskCreated = p?.ExitCode == 0;
                    }
                    catch (System.ComponentModel.Win32Exception uacEx)
                        when (uacEx.NativeErrorCode == 1223) // ERROR_CANCELLED — usuário cancelou UAC
                    {
                        _logger?.LogInfo("[StartupManager] UAC cancelado pelo usuário — usando fallback HKCU\\Run.");
                        taskCreated = false;
                    }
                    catch (Exception elevEx)
                    {
                        _logger?.LogWarning($"[StartupManager] Elevação UAC falhou: {elevEx.Message} — usando fallback HKCU\\Run.");
                        taskCreated = false;
                    }
                }

                if (File.Exists(xmlPath))
                {
                    try { File.Delete(xmlPath); } catch { }
                }

                if (taskCreated)
                {
                    _logger?.LogSuccess($"[StartupManager] Task Scheduler criado com sucesso. Minimizado: {startMinimized}");
                }
                else
                {
                    // Fallback: HKCU\Run não precisa de admin e funciona em qualquer cenário
                    _logger?.LogWarning(
                        "[StartupManager] Task Scheduler indisponível (sem permissão ou política GPO). " +
                        "Usando fallback HKCU\\Run — startup ainda funcionará, mas sem elevação automática.");
                    // O chamador (SetStartup) já escreve no registro HKCU\Run — não duplicar aqui.
                }
            }
            catch (Exception ex)
            {
                // NUNCA logar acesso negado como Error — é esperado em ambientes sem admin/GPO restritivo
                // Códigos relevantes ao criar tarefas que exigem elevação sem admin:
                //   5   = ERROR_ACCESS_DENIED | 1260 = ERRO bloqueado por política
                //   1314= ERROR_PRIVILEGE_NOT_HELD | 740 = ERROR_ELEVATION_REQUIRED
                bool isAccessDenied = ex is UnauthorizedAccessException ||
                                      (ex is System.ComponentModel.Win32Exception w32 &&
                                       (w32.NativeErrorCode == 5 || w32.NativeErrorCode == 1260 ||
                                        w32.NativeErrorCode == 1314 || w32.NativeErrorCode == 740));

                if (isAccessDenied)
                {
                    _logger?.LogWarning(
                        $"[StartupManager] Task Scheduler bloqueado por permissões/política: {ex.Message}. " +
                        "Fallback para HKCU\\Run ativo.");
                }
                else
                {
                    _logger?.LogError($"[StartupManager] Erro ao criar Task Scheduler: {ex.Message}");
                }
            }
        }

        private void DisableTaskStartup()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch { }
        }

        private void RemoveRegistryEntry()
        {
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRunPath, ValueName);
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry32, RegistryRunPath, ValueName);
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRunPath, LegacyValueName);
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry32, RegistryRunPath, LegacyValueName);
        }

        private void RemoveStartupApprovedEntry()
        {
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry64, StartupApprovedPath, ValueName);
            TryDeleteRegistryValue(RegistryHive.CurrentUser, RegistryView.Registry32, StartupApprovedPath, ValueName);
        }

        #region Registry Helpers

        private bool TryWriteRegistry(RegistryHive hive, RegistryView view, string subKey, string name, string data)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var rk = baseKey.CreateSubKey(subKey, writable: true))
                {
                    rk.SetValue(name, data, RegistryValueKind.String);
                    return true;
                }
            }
            catch { return false; }
        }

        private bool TryWriteRegistryBinary(RegistryHive hive, RegistryView view, string subKey, string name, byte[] data)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var rk = baseKey.CreateSubKey(subKey, writable: true))
                {
                    rk.SetValue(name, data, RegistryValueKind.Binary);
                    return true;
                }
            }
            catch { return false; }
        }

        private bool TryDeleteRegistryValue(RegistryHive hive, RegistryView view, string subKey, string name)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var rk = baseKey.OpenSubKey(subKey, writable: true))
                {
                    if (rk?.GetValue(name) != null)
                    {
                        rk.DeleteValue(name);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region System Helpers

        private static string GetExecutablePath()
        {
            return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                   ?? throw new InvalidOperationException("Não foi possível obter o caminho do executável");
        }

        /// <summary>
        /// Verifica se o processo atual está sendo executado com privilégios de Administrador.
        /// </summary>
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
