using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Drivers;
using VoltrisOptimizer.Services.Telemetry;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Serviço de segurança preventiva para atualizações de drivers
    /// Implementa criação automática de ponto de restauração antes de qualquer atualização
    /// </summary>
    public class DriverSecurityService
    {
        private readonly SystemToolsService _systemToolsService;
        private readonly ILoggingService? _loggingService;

        /// <summary>
        /// O logger nunca pode ser nulo: a página de Drivers o injeta a partir de
        /// App.LoggingService, que é anulável. A versão anterior chamava _loggingService.LogInfo
        /// sem verificação, Macs Num_NULL ao primeiro uso se o logger fosse nulo.
        /// </summary>
        private ILoggingService Log => _loggingService ?? App.LoggingService ?? NullLogger.Instance;

        public DriverSecurityService(SystemToolsService systemToolsService, ILoggingService? loggingService)
        {
            _systemToolsService = systemToolsService;
            _loggingService = loggingService;
            App.LoggingService?.LogInfo("[DriverSecurity] Serviço de segurança de drivers inicializado.");
        }

        /// <summary>Logger no-op para quando nenhum serviço de log está disponível.</summary>
        private sealed class NullLogger : ILoggingService
        {
            public static readonly NullLogger Instance = new();
            public event EventHandler<string>? LogEntryAdded { add { } remove { } }
            public void LogInfo(string message) { }
            public void LogSuccess(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message, Exception? exception = null) { }
            public void LogDebug(string message, string? source = null) { }
            public void LogTrace(string message, string? source = null) { }
            public void LogCritical(string message, Exception? exception = null, string? source = null) { }
            public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null) { }
            public void Flush() { }
            public void ClearLogs() { }
            public string[] GetLogs() => Array.Empty<string>();
            public void ExportLogs(string destPath) { }
            public string GetLogDirectory() => string.Empty;
            public void Dispose() { }
        }

        /// <summary>
        /// Cria ponto de restauração automático antes de atualização de driver
        /// </summary>
        /// <param name="driverName">Nome do driver sendo atualizado</param>
        /// <param name="isBatchUpdate">Indica se é atualização em lote</param>
        /// <returns>True se o ponto foi criado com sucesso</returns>
        public async Task<bool> CreatePreUpdateRestorePointAsync(string driverName, bool isBatchUpdate = false)
        {
            using var op = new DriverOperationScope("RESTORE_POINT", driverName);

            try
            {
                var startTime = DateTime.Now;
                Log.LogInfo($"[DriverSecurity] Criando ponto de restauração preventivo | lote={isBatchUpdate} | inicio={startTime:HH:mm:ss.fff}");

                var securityMode = SecurityContext.GetCurrentSecurityMode();
                op.Stage("MODO", securityMode.ToString());

                if (securityMode == SecurityMode.Maintenance)
                {
                    bool maintenanceResult = await CreateMaintenanceRestorePoint(driverName, isBatchUpdate, startTime).ConfigureAwait(false);
                    if (maintenanceResult) op.Succeed("ponto criado em modo manutenção");
                    else op.Fail("ponto de restauração não criado em modo manutenção");
                    return maintenanceResult;
                }

                var description = isBatchUpdate
                    ? $"Voltris - Atualização Lote Drivers - {DateTime.Now:dd/MM/yyyy HH:mm}"
                    : $"Voltris - Atualização Driver {driverName} - {DateTime.Now:dd/MM/yyyy HH:mm}";

                op.Stage("DESCRICAO", description);

                if (!await EnsureSystemProtectionEnabledAsync().ConfigureAwait(false))
                {
                    op.StageFailed("PROTECAO", "não foi possível garantir a proteção do sistema", null);
                    return false;
                }

                op.Stage("CHECKPOINT", "executando Checkpoint-Computer");
                bool restorePointCreated = await _systemToolsService.CreateSystemRestorePointAsync(description).ConfigureAwait(false);
                double durationMs = (DateTime.Now - startTime).TotalMilliseconds;

                if (restorePointCreated)
                {
                    op.Succeed($"ponto criado | duracao={durationMs:F0}ms");
                    Log.LogSuccess($"[DriverSecurity] Ponto de restauração criado para '{driverName}' em {durationMs:F0}ms.");
                    return true;
                }

                op.StageFailed("CHECKPOINT", $"Checkpoint-Computer não confirmou a criação do ponto | duracao={durationMs:F0}ms", null);
                return false;
            }
            catch (Exception ex)
            {
                op.Fail("exceção ao criar o ponto de restauração", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica e ativa a proteção do sistema (System Restore) se necessário
        /// </summary>
        /// <returns>True se a proteção está ativa ou foi ativada com sucesso</returns>
        private async Task<bool> EnsureSystemProtectionEnabledAsync()
        {
            try
            {
                if (!IsRunningAsAdministrator())
                {
                    Log.LogWarning("[DriverSecurity] O aplicativo não está rodando como administrador; a proteção do sistema não pode ser garantida.");
                    await ShowAdminRequiredMessageAsync().ConfigureAwait(true);
                    return false;
                }

                bool isRestoreEnabled = await IsSystemRestoreEnabledAsync().ConfigureAwait(false);

                if (isRestoreEnabled)
                {
                    Log.LogInfo("[DriverSecurity] System Restore já está ativo.");
                    return true;
                }

                Log.LogWarning("[DriverSecurity] System Restore está desativado. Tentando ativar...");

                bool activationResult = await EnableSystemRestoreAsync().ConfigureAwait(false);
                if (activationResult)
                {
                    await NotifyUserAboutSystemActivationAsync().ConfigureAwait(true);
                    return true;
                }

                await ShowSystemActivationFailureMessageAsync().ConfigureAwait(true);

                // CORREÇÃO DE SEGURANÇA: a versão anterior retornava TRUE aqui, autorizando a
                // instalação de driver sem nenhuma proteção do sistema. Sem ponto de
                // restauração, a instalação é cancelada — é o requisito explícito da política.
                Log.LogError("[DriverSecurity] System Restore não pôde ser ativado. A instalação de drivers será cancelada por segurança.");
                return false;
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Erro ao verificar/ativar a proteção do sistema. {ex.Describe()}", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica se o processo possui privilégio de administrador.
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriverSecurity] Falha ao verificar privilégios de administrador. {ex.Describe()}", ex);
                return false;
            }
        }

        /// <summary>
        /// Cria ponto de restauração em modo manutenção (sem alertas)
        /// </summary>
        private async Task<bool> CreateMaintenanceRestorePoint(string driverName, bool isBatchUpdate, DateTime startTime)
        {
            try
            {
                var description = isBatchUpdate
                    ? $"Atualização Lote Drivers - {DateTime.Now:dd/MM/yyyy HH:mm}"
                    : $"Atualização Driver {driverName} - {DateTime.Now:dd/MM/yyyy HH:mm}";

                Log.LogInfo($"[DriverSecurity] Modo manutenção: criando ponto '{description}'");
                bool created = await _systemToolsService.CreateSystemRestorePointAsync(description).ConfigureAwait(false);
                double durationMs = (DateTime.Now - startTime).TotalMilliseconds;

                if (created)
                    Log.LogSuccess($"[DriverSecurity] Ponto criado em modo manutenção para '{driverName}' em {durationMs:F0}ms.");
                else
                    Log.LogError($"[DriverSecurity] Falha ao criar o ponto em modo manutenção para '{driverName}'.");

                return created;
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Exceção ao criar o ponto em modo manutenção. {ex.Describe()}", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifica se System Restore está ativado no sistema.
        ///
        /// CORREÇÃO: a versão anterior retornava <c>output.Contains("1")</c>. Isso é um
        /// falso-positivo grave: qualquer saída do PowerShell contendo o caractere "1" em
        /// qualquer posição — inclusive em mensagens de erro — era interpretada como
        /// "System Restore ativado", liberando a instalação de driver sem a proteção que se
        /// previa existir.
        /// </summary>
        public async Task<bool> IsSystemRestoreEnabledAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                const string psScript =
                    "$ErrorActionPreference='Stop';" +
                    "$p='HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore';" +
                    "if(-not (Test-Path $p)){Write-Output 'VOLTRIS_SR=ABSENT';exit};" +
                    "$v=(Get-ItemProperty -Path $p -Name 'Enable' -ErrorAction SilentlyContinue).Enable;" +
                    "if($null -eq $v){Write-Output 'VOLTRIS_SR=UNSET';exit};" +
                    "Write-Output ('VOLTRIS_SR=' + $v)";

                string output = await RunPowerShellAsync(psScript).ConfigureAwait(true);

                // Parser estrito: somente o token explícito com valor 1/true habilita a proteção.
                bool enabled = output.Split('\n', '\r')
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("VOLTRIS_SR=", StringComparison.Ordinal))
                    .Select(l => l["VOLTRIS_SR=".Length..].Trim())
                    .Any(v => v == "1" || v.Equals("True", StringComparison.OrdinalIgnoreCase));

                App.LoggingService?.LogInfo(
                    $"[DriverSecurity] IsSystemRestoreEnabled -> {enabled} | saida='{Summarize(output)}' | {sw.ElapsedMilliseconds}ms");
                return enabled;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriverSecurity] Erro ao verificar System Restore. {ex.Describe()}");
                return false;
            }
        }

        /// <summary>
        /// Executa um script PowerShell e devolve a saída.
        ///
        /// CORREÇÃO CRÍTICA DE ELEVAÇÃO: a versão anterior combinava
        /// <c>UseShellExecute = false</c> com <c>Verb = "runas"</c>. O .NET ignora
        /// <c>Verb</c> quando <c>UseShellExecute</c> é false, e <c>RedirectStandardOutput</c>
        /// é incompatível com <c>UseShellExecute = true</c>. A combinação anterior portanto
        /// NUNCA elevava e NUNCA conseguia redirecionar: a ativação do System Restore falhava
        /// sem que a causa fosse registrada. Aqui a elevação é exigida e verificada
        /// explicitamente, e a ausência de privilégio é reportada em vez de silenciada.
        /// </summary>
        private static async Task<string> RunPowerShellAsync(string script)
        {
            // Escritas em HKLM e cmdlets de restauração exigem privilégio de administrador.
            if (!IsRunningAsAdministrator())
            {
                App.LoggingService?.LogError(
                    "[DriverSecurity] Operação exige privilégio de administrador e o processo não o possui. " +
                    "Nenhuma alteração no System Restore foi realizada.");
                throw new InvalidOperationException(
                    "Administrator privileges are required to modify System Restore settings.");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return string.Empty;

            // Leitura concorrente dos dois fluxos: um await por vez pode travar se o outro
            // pipe encher antes de ser consumido.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                App.LoggingService?.LogWarning($"[DriverSecurity] PowerShell terminou com {process.ExitCode}: {Summarize(stderr)}");

            return stdout;
        }

        private static string Summarize(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return "(vazia)";
            string flat = output!.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 200 ? flat : flat[..200] + "...";
        }

        /// <summary>
        /// Ativa o System Restore no sistema. Exige elevação real.
        /// </summary>
        private async Task<bool> EnableSystemRestoreAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                const string psScript =
                    "$ErrorActionPreference='Stop';" +
                    "$p='HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore';" +
                    "if(-not (Test-Path $p)){New-Item -Path $p -Force | Out-Null};" +
                    "Set-ItemProperty -Path $p -Name 'Enable' -Value 1 -Type DWord -Force;" +
                    "Set-ItemProperty -Path $p -Name 'DisableSR' -Value 0 -Type DWord -Force;" +
                    "Set-ItemProperty -Path $p -Name 'SystemRestorePointCreationFrequency' -Value 0 -Type DWord -Force;" +
                    "try{Set-Service srsvc -StartupType Automatic;Start-Service srsvc -ErrorAction SilentlyContinue}catch{};" +
                    "Write-Output ('VOLTRIS_SR=' + (Get-ItemProperty -Path $p -Name 'Enable').Enable)";

                string output = await RunPowerShellAsync(psScript).ConfigureAwait(true);

                bool enabled = output.Split('\n', '\r')
                    .Select(l => l.Trim())
                    .Any(l => l.Equals("VOLTRIS_SR=1", StringComparison.OrdinalIgnoreCase));

                App.LoggingService?.LogInfo(
                    $"[DriverSecurity] EnableSystemRestore -> {enabled} | saida='{Summarize(output)}' | {sw.ElapsedMilliseconds}ms");

                if (enabled)
                    Log.LogSuccess("[DriverSecurity] System Restore ativado com sucesso");
                else
                    Log.LogError("[DriverSecurity] Falha ao ativar o System Restore.");

                return enabled;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriverSecurity] Erro ao ativar System Restore. {ex.Describe()}");
                return false;
            }
        }

        /// <summary>
        /// Exibe mensagem de administrador requerido
        /// </summary>
        private async Task ShowAdminRequiredMessageAsync()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowAdminRequiredMessageAsync - ENTER");
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("AdminRequiredSystemProtection"),
                        LocalizationService.Instance.GetString("AdminRequired"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Erro ao exibir mensagem de administrador: {ex.Message}");
                App.LoggingService?.LogError($"[DriverSecurity] ShowAdminRequiredMessageAsync - Exception: {ex.Message}", ex);
            }
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowAdminRequiredMessageAsync - EXIT [{sw.ElapsedMilliseconds}ms]");
        }

        private async Task NotifyUserAboutSystemActivationAsync()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverSecurity] NotifyUserAboutSystemActivationAsync - ENTER");
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("SystemProtectionActivated"),
                        LocalizationService.Instance.GetString("DriverSecurity"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Erro ao exibir mensagem de ativação: {ex.Message}");
                App.LoggingService?.LogError($"[DriverSecurity] NotifyUserAboutSystemActivationAsync - Exception: {ex.Message}", ex);
            }
            App.LoggingService?.LogInfo($"[DriverSecurity] NotifyUserAboutSystemActivationAsync - EXIT [{sw.ElapsedMilliseconds}ms]");
        }

        /// <summary>
        /// Exibe mensagem de falha na ativação da proteção do sistema
        /// </summary>
        private async Task ShowSystemActivationFailureMessageAsync()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowSystemActivationFailureMessageAsync - ENTER");
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("SystemProtectionActivationFailed"),
                        LocalizationService.Instance.GetString("SecurityFailure"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Erro ao exibir mensagem de falha: {ex.Message}");
                App.LoggingService?.LogError($"[DriverSecurity] ShowSystemActivationFailureMessageAsync - Exception: {ex.Message}", ex);
            }
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowSystemActivationFailureMessageAsync - EXIT [{sw.ElapsedMilliseconds}ms]");
        }

        /// <summary>
        /// Exibe mensagem de falha na criação do ponto de restauração
        /// </summary>
        /// <param name="driverName">Nome do driver</param>
        /// <param name="errorDetails">Detalhes do erro</param>
        public async Task ShowRestorePointFailureMessageAsync(string driverName, string errorDetails = null)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowRestorePointFailureMessageAsync - ENTER [driverName={driverName}]");
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var message = string.Format(LocalizationService.Instance.GetString("RestorePointCreationFailed"), driverName);

                    if (!string.IsNullOrEmpty(errorDetails))
                    {
                        message += string.Format(LocalizationService.Instance.GetString("ErrorDetails"), errorDetails);
                    }

                    message += LocalizationService.Instance.GetString("RestorePointFailureSolutions");

                    ModernMessageBox.Show(
                        message,
                        LocalizationService.Instance.GetString("SecurityFailure"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
            catch (Exception ex)
            {
                Log.LogError($"[DriverSecurity] Erro ao exibir mensagem de falha de restore point: {ex.Message}");
                App.LoggingService?.LogError($"[DriverSecurity] ShowRestorePointFailureMessageAsync - Exception: {ex.Message}", ex);
            }
            App.LoggingService?.LogInfo($"[DriverSecurity] ShowRestorePointFailureMessageAsync - EXIT [{sw.ElapsedMilliseconds}ms]");
        }
    }
}
