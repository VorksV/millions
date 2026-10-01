using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Media;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Drivers
{
    public class SafeDriverInstaller : IDisposable
    {
        private readonly DriverBackupManager _backupManager;
        private readonly SystemToolsService _systemToolsService;
        private readonly ILoggingService? _ownedLogger;

        // Blacklist de componentes destrutivos (Controladores de disco, RAID, SATA, NVMe)
        private static readonly string[] CRITICAL_STORAGE_CLASSES = { "SCSIAdapter", "HDC", "Volume", "DiskDrive" };

        public SafeDriverInstaller()
        {
            _backupManager = new DriverBackupManager();

            // O construtor anterior criava um SetupApiEnumerator E NUNCA o descartava, vazando um
            // handle de device-info-set para toda a vida do processo — e o reutilizava de forma
            // não thread-safe. A validação pós-instalação agora cria o seu próprio enumerador.
            _systemToolsService = new SystemToolsService(App.LoggingService);
        }

        /// <summary>
        /// Executa a instalação segura de um driver já baixado e validado.
        /// </summary>
        /// <param name="update">Dispositivo alvo + pacote.</param>
        /// <param name="extractedFolderPath">Pasta extraída do pacote.</param>
        /// <param name="candidateInfFiles">
        /// Arquivos .INF encontrados no pacote. Quando informado, TODOS são avaliados — a versão
        /// anterior aceitava o primeiro .INF encontrado na pasta, que frequentemente é um driver
        /// auxiliar (ex.: o de Bluetooth) e não o do dispositivo alvo.
        /// </param>
        public async Task<bool> InstallDriverSafelyAsync(DriverUpdate update, string extractedFolderPath, IReadOnlyList<string>? candidateInfFiles = null)
        {
            if (update?.Device == null || update.NewDriver == null)
            {
                App.LoggingService?.LogError("[SafeInstaller] Chamada inválida: update sem dispositivo ou sem pacote.");
                return false;
            }

            using var op = new DriverOperationScope("DRIVER_SAFE_INSTALL", update.Device.DeviceName);

            op.Stage("CONTEXTO",
                $"device={update.Device.DeviceName} | id={update.Device.DeviceInstanceId} | " +
                $"driver={update.NewDriver.Title} v{update.NewDriver.Version} | pasta={extractedFolderPath}");

            string deviceClass = GetDeviceClass(update.Device.DeviceInstanceId);

            // 1. REJEIÇÃO ATIVA ÀS CAUSAS DE TELA AZUL: blacklist de armazenamento.
            if (CRITICAL_STORAGE_CLASSES.Contains(deviceClass, StringComparer.OrdinalIgnoreCase))
            {
                op.StageFailed("SEGURANCA",
                    $"classe '{deviceClass}' pertence a controlador de disco crítico — " +
                    "gerenciadores de driver causam BSOD ao automatizar esta classe; não processada", null);
                return false;
            }

            // 2. SELEÇÃO DO INF: avaliar todos e escolher o que declara o Hardware ID do alvo.
            var infCandidates = candidateInfFiles is { Count: > 0 }
                ? candidateInfFiles.ToList()
                : FindInfFiles(extractedFolderPath);

            if (infCandidates.Count == 0)
            {
                op.StageFailed("PAYLOAD", "nenhum arquivo .INF encontrado no pacote", null);
                return false;
            }

            op.Stage("INF_CANDIDATOS", string.Join(", ", infCandidates.Select(Path.GetFileName)));

            string? targetInf = infCandidates.FirstOrDefault(inf => IsHardwareExplicitlySupported(inf, update.Device.HardwareIds));

            if (targetInf == null)
            {
                op.StageFailed("COMPATIBILIDADE",
                    $"nenhum dos {infCandidates.Count} INF(s) do pacote declara o Hardware ID '{update.Device.HardwareIds}'. " +
                    "Instalação cancelada para não associar um driver incompatível.", null);
                return false;
            }

            if (infCandidates.Count > 1)
            {
                op.Stage("INF_SELECIONADO",
                    $"{Path.GetFileName(targetInf)} (de {infCandidates.Count} candidatos; compatível com o Hardware ID)");
            }

            // 3. SEGURANÇA: backup do DriverStore ANTES de qualquer alteração.
            op.Stage("BACKUP", "exportando o DriverStore via DISM");
            string backupPath;
            try
            {
                backupPath = await _backupManager.BackupAllDriversAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                op.StageFailed("BACKUP", "falha ao exportar o DriverStore — nenhuma alteração será feita", ex);
                return false;
            }

            if (string.IsNullOrEmpty(backupPath))
            {
                op.StageFailed("BACKUP", "o backup do DriverStore não produziu uma pasta utilizável", null);
                return false;
            }
            op.Stage("BACKUP_OK", $"pasta={backupPath}");

            // 4. INSTALAÇÃO unitária via pnputil.
            op.Stage("INSTALACAO", $"pnputil /add-driver \"{Path.GetFileName(targetInf)}\" /install");
            int pnpResult = await RunPnPUtilInstallAsync(targetInf, op).ConfigureAwait(false);

            if (pnpResult != 0)
            {
                op.StageFailed("INSTALACAO", $"pnputil retornou {pnpResult}", null);
                await TriggerRollback(backupPath, op, "pnputil recusou o pacote").ConfigureAwait(false);
                return false;
            }

            // 5. VALIDAÇÃO PÓS-INSTALAÇÃO contra o estado real do hardware.
            op.Stage("VALIDACAO", "re-enumerando o dispositivo via SetupAPI");
            bool passedPostTests = ValidatePostInstallation(update.Device, deviceClass, op);

            if (!passedPostTests)
            {
                await TriggerRollback(backupPath, op, "o dispositivo não respondeu corretamente após a instalação").ConfigureAwait(false);
                return false;
            }

            op.Succeed($"inf={Path.GetFileName(targetInf)} | reinicializacao_recomendada=true");
            return true;
        }

        private async Task TriggerRollback(string backupFolderPath, DriverOperationScope op, string reason)
        {
            if (string.IsNullOrEmpty(backupFolderPath))
            {
                op.StageFailed("ROLLBACK", $"não há pasta de backup para reverter ({reason})", null);
                return;
            }

            op.MarkRollback(reason);
            try
            {
                bool reverted = await _backupManager.RestoreAllDriversAsync(backupFolderPath).ConfigureAwait(false);
                if (reverted)
                    op.Stage("ROLLBACK_OK", $"DriverStore restaurado de '{backupFolderPath}'");
                else
                    op.StageFailed("ROLLBACK", "a restauração do DriverStore falhou", null);
            }
            catch (Exception ex)
            {
                op.StageFailed("ROLLBACK", "exceção durante a restauração do DriverStore", ex);
            }
        }


        private bool ValidatePostInstallation(DeviceInfo previousDeviceState, string deviceClass, DriverOperationScope op)
        {
            op.Stage("VALIDACAO", "escaneando o hardware (SetupAPI) apos a modificacao");

            // Enumerador proprio e descartavel: a versao anterior reutilizava um enumerador
            // criado no construtor e nunca descartado (vazamento de handle + uso concorrente).
            try
            {
                using var enumerator = new SetupApiEnumerator();
                var currentDevices = enumerator.EnumerateDevices();

                var hwTarget = currentDevices.FirstOrDefault(d =>
                    (!string.IsNullOrEmpty(d.DeviceInstanceId) && !string.IsNullOrEmpty(previousDeviceState.DeviceInstanceId) &&
                     d.DeviceInstanceId.Equals(previousDeviceState.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)) ||
                    (previousDeviceState.HardwareIdList.Count > 0 &&
                     previousDeviceState.HardwareIdList.Any(h =>
                         !string.IsNullOrEmpty(d.HardwareIds) &&
                         d.HardwareIds!.Contains(h, StringComparison.OrdinalIgnoreCase))));

                if (hwTarget == null)
                {
                    op.StageFailed("VALIDACAO", "o dispositivo desapareceu do Gerenciador de Dispositivos apos a instalacao", null);
                    return false;
                }

                if (hwTarget.IsProblem)
                {
                    op.StageFailed("VALIDACAO",
                        $"o dispositivo apresenta problema apos a instalacao (CM_PROB_{hwTarget.ProblemCode}: {hwTarget.ProblemDescription})", null);
                    return false;
                }

                op.Stage("VALIDACAO_OK", $"dispositivo presente e sem problema | versao={hwTarget.DriverVersion}");

                if (deviceClass.IndexOf("MEDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    deviceClass.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    op.Stage("VALIDACAO_AUDIO", "verificando o subsistema de audio do Windows");
                    try
                    {
                        SystemSounds.Beep.Play();
                        op.Stage("VALIDACAO_AUDIO_OK", "subsistema de midia respondendo");
                    }
                    catch (Exception ex)
                    {
                        op.StageFailed("VALIDACAO_AUDIO", "o subsistema de audio nao respondeu apos a instalacao", ex);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                op.StageFailed("VALIDACAO", "o watchdog de validacao lancou excecao inesperada", ex);
                return false;
            }
        }

        private List<string> FindInfFiles(string dirPath)
        {
            try
            {
                return Directory.GetFiles(dirPath, "*.inf", SearchOption.AllDirectories)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SafeInstaller] Falha ao listar INF em '{dirPath}': {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Confere se o INF declara explicitamente o Hardware ID do dispositivo.
        ///
        /// CORRECAO: a versao anterior usava File.ReadAllText, que decodifica como UTF-8. INFs do
        /// Windows sao frequentemente UTF-16 (BOM FF FE) - nesses casos o conteúdo vinha com
        /// caracteres NUL intercalados e NENHUM Hardware ID era encontrado, fazendo o instalador
        /// recusar drivers perfeitamente compativeis.
        /// </summary>
        private bool IsHardwareExplicitlySupported(string infFilePath, string? hardwareIdsStr)
        {
            try
            {
                string content = ReadInfText(infFilePath);
                if (string.IsNullOrEmpty(content))
                {
                    App.LoggingService?.LogWarning($"[SafeInstaller] INF ilegivel ou vazio: {Path.GetFileName(infFilePath)}");
                    return false;
                }

                var targetHwIds = (hardwareIdsStr ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(h => !string.IsNullOrWhiteSpace(h));

                foreach (var id in targetHwIds)
                {
                    // Compara VEN_xxxx&DEV_xxxx, ignorando revisao e sub-interface (&MI_xx).
                    var parts = id.Split('&');
                    string cleanId = parts.Length > 1 ? string.Join("&", parts.Take(2)) : id;
                    int mi = cleanId.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
                    if (mi > 0) cleanId = cleanId[..mi];

                    if (cleanId.Length > 3 && content.Contains(cleanId, StringComparison.OrdinalIgnoreCase))
                    {
                        App.LoggingService?.LogInfo(
                            $"[SafeInstaller] Hardware ID compativel encontrado no INF {Path.GetFileName(infFilePath)}: '{cleanId}'.");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SafeInstaller] Erro ao ler o INF '{infFilePath}'. {ex.Describe()}", ex);
                return false;
            }
        }

        /// <summary>Le um .INF respeitando BOM UTF-16 e ANSI.</summary>
        internal static string ReadInfText(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> bom = stackalloc byte[2];
            int read = fs.Read(bom);
            fs.Seek(0, SeekOrigin.Begin);

            if (read == 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                return File.ReadAllText(path, System.Text.Encoding.Unicode);
            if (read == 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return File.ReadAllText(path, System.Text.Encoding.BigEndianUnicode);

            using var reader = new StreamReader(fs, System.Text.Encoding.Default, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Executa o pnputil com ELEVACAO CORRETA.
        ///
        /// CORRECAO CRITICA: a versao anterior definia UseShellExecute = false E Verb = "runas".
        /// O .NET ignora completamente Verb quando UseShellExecute e false, portanto o pnputil
        /// rodava SEM privilegio de administrador e a instalacao falhava (ou era rejeitada)
        /// sem que o codigo registrasse o motivo. Alem disso, StandardOutput era redirigido mas
        /// nunca lido: se a saida do pnputil preenchesse o buffer do pipe, o processo ficaria
        /// bloqueado para sempre (deadlock).
        /// </summary>
        private async Task<int> RunPnPUtilInstallAsync(string infPath, DriverOperationScope op)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/add-driver \"{infPath}\" /install",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Verificar e elevar ANTES, de forma explícita. Sem isto a instalação nunca teve
            // privilégio suficiente.
            if (!IsRunningAsAdministrator())
            {
                op.StageFailed("ELEVACAO",
                    "o processo nao esta com privilegio de administrador; pnputil /add-driver exige elevacao", null);
                return -1;
            }

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                op.StageFailed("PNPUTIL", "falha ao iniciar o pnputil", ex);
                return -1;
            }

            // Ler os dois fluxos CONCURRENTEMENTE: um await por vez pode deadlock se o outro
            // pipe encher antes de ser lido.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            op.Stage("PNPUTIL_SAIDA", $"exitCode={process.ExitCode} | stdout={Truncate(stdout, 400)} | stderr={Truncate(stderr, 400)}");
            return process.ExitCode;
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat[..max] + "...";
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SafeInstaller] Falha ao verificar privilegio de administrador: {ex.Message}");
                return false;
            }
        }

        private string GetDeviceClass(string? deviceInstanceId)
        {
            if (string.IsNullOrEmpty(deviceInstanceId)) return "Unknown";

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{deviceInstanceId}");
                if (key?.GetValue("Class") is string className && !string.IsNullOrEmpty(className))
                {
                    return className;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SafeInstaller] Falha ao ler a classe de '{deviceInstanceId}' no registro: {ex.Message}");
            }

            if (deviceInstanceId.Contains("HDAUDIO", StringComparison.OrdinalIgnoreCase)) return "MEDIA";
            if (deviceInstanceId.Contains("DISPLAY", StringComparison.OrdinalIgnoreCase)) return "Display";
            if (deviceInstanceId.Contains("PCI\\VEN_", StringComparison.OrdinalIgnoreCase)) return "PCI";
            if (deviceInstanceId.Contains("USB\\", StringComparison.OrdinalIgnoreCase)) return "USB";
            return "Unknown";
        }

        public void Dispose()
        {
            _ownedLogger?.Dispose();
        }
    }
}
