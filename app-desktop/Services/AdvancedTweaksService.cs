using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services
{
    public class AdvancedTweaksService
    {
        private readonly ILoggingService _logger;
        public bool DryRun { get; set; } = true;
        private Thread? _timerThread;
        private CancellationTokenSource? _timerCts;
        private volatile bool _timerRequested;
        private uint _lastDesired;
        private readonly string _backupDir;
        private readonly string _backupFile;

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool Set, out uint CurrentResolution);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

        public AdvancedTweaksService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _backupDir = AppDataPaths.Backups;
            _backupFile = AppDataPaths.GetPath("Backups/advanced_tweaks.json");
            AppDataPaths.EnsureDirectory(_backupDir);
        }

        public void SetMaximumTimerResolution()
        {
            try
            {
                if (DryRun) return;
                if (_timerThread != null && _timerThread.IsAlive) return;
                _timerCts = new CancellationTokenSource();
                _timerThread = new Thread(() =>
                {
                    try
                    {
                        var ct = _timerCts!.Token;
                        uint min, max, cur;
                        NtQueryTimerResolution(out min, out max, out cur);
                        var desired = Math.Min(min, 5000);
                        _lastDesired = (uint)desired;
                        uint outCur;
                        var status = NtSetTimerResolution((uint)desired, true, out outCur);
                        _timerRequested = status == 0;
                        
                        // OTIMIZAÇÃO: Suspensão inteligente baseada em kernel (WaitHandle).
                        // O Threading.Sleep(1000) no while(!ct) custa milhares de trocas de contexto. 
                        // WaitHandle gasta 0% de CPU.
                        ct.WaitHandle.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Falha no timer resolution", ex);
                    }
                    finally
                    {
                        try
                        {
                            if (_timerRequested)
                            {
                                uint curRes;
                                var releaseDesired = _lastDesired != 0 ? _lastDesired : 5000u;
                                NtSetTimerResolution(releaseDesired, false, out curRes);
                                _timerRequested = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Falha ao liberar timer resolution", ex);
                        }
                    }
                })
                { IsBackground = true, Name = "Voltris_TimerResolution" };
                _timerThread.Start();
                _logger.LogSuccess("Timer resolution ajustado");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao ajustar timer resolution", ex);
            }
        }

        public void ReleaseTimerResolution()
        {
            try
            {
                _timerCts?.Cancel();
                _timerThread = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao desativar timer resolution", ex);
            }
        }

        public bool BackupNetworkSettings()
        {
            try
            {
                var backup = new NetworkBackup();
                backup.Timestamp = DateTime.Now;

                using var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", false);
                if (interfacesKey != null)
                {
                    foreach (var subName in interfacesKey.GetSubKeyNames())
                    {
                        using var subKey = interfacesKey.OpenSubKey(subName, false);
                        if (subKey == null) continue;
                        var entry = new InterfaceBackup { Key = subName };
                        entry.TcpAckFrequency = subKey.GetValue("TcpAckFrequency") as int?;
                        entry.TCPNoDelay = subKey.GetValue("TCPNoDelay") as int?;
                        backup.Interfaces.Add(entry);
                    }
                }

                using var sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", false);
                if (sysProfile != null)
                {
                    var val = sysProfile.GetValue("NetworkThrottlingIndex");
                    if (val is int i) backup.NetworkThrottlingIndex = i;
                }

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_backupFile, json);
                _logger.LogSuccess("Backup de rede criado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao criar backup de rede", ex);
                return false;
            }
        }

        public bool ApplyNetworkTweaks()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para escrever em HKLM");
                    return false;
                }

                using var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                if (interfacesKey != null)
                {
                    foreach (var subName in interfacesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = interfacesKey.OpenSubKey(subName, true);
                            if (subKey == null) continue;
                            // CORREÇÃO CRÍTICA: TcpAckFrequency REMOVIDO — forçar ACK imediato
                            // causa picos de latência (lag spikes) e perda de pacotes em ISPs residenciais.
                            // Mantido apenas TcpNoDelay (seguro).
                            subKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Erro ao aplicar tweak em {subName}", ex);
                        }
                    }
                }

                using var sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                if (sysProfile != null)
                {
                    try
                    {
                        sysProfile.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Erro ao ajustar NetworkThrottlingIndex", ex);
                    }
                }

                _logger.LogSuccess("Tweaks de rede aplicados");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao aplicar tweaks de rede", ex);
                return false;
            }
        }

        public bool RestoreNetworkSettings()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para restaurar HKLM");
                    return false;
                }
                if (!File.Exists(_backupFile))
                {
                    _logger.LogWarning("Backup de rede não encontrado");
                    return false;
                }

                var json = File.ReadAllText(_backupFile);
                var backup = JsonSerializer.Deserialize<NetworkBackup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new NetworkBackup();

                using var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                if (interfacesKey != null)
                {
                    foreach (var entry in backup.Interfaces)
                    {
                        try
                        {
                            using var subKey = interfacesKey.OpenSubKey(entry.Key, true);
                            if (subKey == null) continue;
                            if (entry.TcpAckFrequency.HasValue)
                                subKey.SetValue("TcpAckFrequency", entry.TcpAckFrequency.Value, RegistryValueKind.DWord);
                            else
                                subKey.DeleteValue("TcpAckFrequency", false);
                            if (entry.TCPNoDelay.HasValue)
                                subKey.SetValue("TCPNoDelay", entry.TCPNoDelay.Value, RegistryValueKind.DWord);
                            else
                                subKey.DeleteValue("TCPNoDelay", false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Erro ao restaurar {entry.Key}", ex);
                        }
                    }
                }

                using var sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                if (sysProfile != null)
                {
                    try
                    {
                        if (backup.NetworkThrottlingIndex.HasValue)
                            sysProfile.SetValue("NetworkThrottlingIndex", backup.NetworkThrottlingIndex.Value, RegistryValueKind.DWord);
                        else
                            sysProfile.DeleteValue("NetworkThrottlingIndex", false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Erro ao restaurar NetworkThrottlingIndex", ex);
                    }
                }

                _logger.LogSuccess("Backup de rede restaurado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar rede", ex);
                return false;
            }
        }

        public bool SetCoreParkingMinCores100()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para powercfg");
                    return false;
                }

                bool isLaptop = false;
                try
                {
                    var sysInfo = App.Services?.GetService(typeof(global::VoltrisOptimizer.Interfaces.ISystemInfoService)) as global::VoltrisOptimizer.Interfaces.ISystemInfoService;
                    isLaptop = sysInfo?.IsLaptop() ?? false;
                } catch { }

                if (isLaptop)
                {
                    _logger.LogWarning("Blocked by OEM Guard (Laptop detected)");
                    return true;
                }

                // [FIX:LEGACY-CORE-PARKING-NEUTRALIZADO]
                //
                // Este método gravava CPMINCORES=100 no esquema ATIVO e depois
                // pedia `RequestPlan(..., PerformanceTweak)`. Há dois defeitos
                // independentes aqui, e ambos já custaram desempenho medido.
                //
                // 1. O VALOR. 100 é "estaciona tudo cedo", e o oposto do que a
                //    tabela do perfil决定. Mais importante: este método roda
                //    junto do Modo Gamer, e o serviço legado que gravava
                //    estacionamento 0 estava sendo puxado logo atrás. Os dois se
                //    anulavam em ciclos de 12s, o que explica o comportamento
                //    instável que o usuário vinha descrevendo.
                //
                // 2. O ESQUEMA. `schemeGuid` estava `Guid.Empty`, ou seja,
                //    `SCHEME_CURRENT`. Somado ao `RequestPlan` de prioridade 4,
                //    este método tinha autoridade para tirar o plano gerenciado
                //    do ar — exatamente o que o log registrou.
                //
                // Estacionamento é valor do Perfil Inteligente. Este serviço
                // nao tem mais opiniao sobre energia.
                _logger.LogWarning(
                    "[AdvancedTweaks] SetCoreParkingMinCores100 NEUTRALIZADO: o estacionamento de " +
                    "núcleo é definido pelo Perfil Inteligente (ProfilePowerMatrix). Este método " +
                    "gravava no esquema ativo e podia derrubar o plano gerenciado.");

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao ajustar core parking", ex);
                return false;
            }
        }

        /// <summary>
        /// Reverte o core parking (CPMINCORES) para valores seguros ao desativar o Modo Gamer.
        /// AC=100% (performance total na tomada), DC=5% (economia de bateria).
        /// NUNCA usar 50% no AC — em Intel 12ª+ gen e AMD Ryzen modernos isso causa CPU 100% constante.
        /// </summary>
        public bool RevertCoreParkingToDefault()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para reverter core parking");
                    return false;
                }

                // AC=100%: na tomada, deixa o Windows gerenciar livremente (sem restrição mínima)
                // DC=5%:   na bateria, permite economia real de energia
                bool isLaptop = false;
                try
                {
                    var sysInfo = App.Services?.GetService(typeof(global::VoltrisOptimizer.Interfaces.ISystemInfoService)) as global::VoltrisOptimizer.Interfaces.ISystemInfoService;
                    isLaptop = sysInfo?.IsLaptop() ?? false;
                } catch { }

                if (isLaptop)
                {
                    _logger.LogWarning("Blocked by OEM Guard (Laptop detected)");
                    return true;
                }

                // [FIX:LEGACY-CORE-PARKING-NEUTRALIZADO] Mesmo defeito do método
                // acima: escrita em `SCHEME_CURRENT` + `RequestPlan` de
                // prioridade 4, com autoridade para derrubar o plano gerenciado.
                // E o valor 100 e exatamente o oposto do que a tabela do perfil
                // aplica. A "reversao" nao restaurava nada: reinstaurava um
                // numero que o Perfil Inteligente nunca pediu.
                //
                // Como este metodo roda ao DESLIGAR o Modo Gamer, neutraliza-lo
                // aqui impede que a saida do modo deixe o plano pior do que
                // estava.
                _logger.LogWarning(
                    "[AdvancedTweaks] RevertCoreParkingToDefault NEUTRALIZADO: o Perfil " +
                    "Inteligente ja aplica AC=100 / DC=5 pela tabela, e o vigia restaura " +
                    "valores divergentes a cada ciclo.");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao reverter core parking", ex);
                return false;
            }
        }

        private readonly string _msiBackupFileName = "msi_backup.json";

        public bool EnableMsiModeForGpuAndNetwork()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Ativar MSI para GPU/Rede");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para HKLM");
                    return false;
                }
                var backupPath = Path.Combine(_backupDir, _msiBackupFileName);
                var backup = new List<MsiBackupItem>();
                using var pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI", false);
                if (pci == null) return false;
                foreach (var dev in pci.GetSubKeyNames())
                {
                    using var devKey = pci.OpenSubKey(dev, false);
                    if (devKey == null) continue;
                    foreach (var inst in devKey.GetSubKeyNames())
                    {
                        using var instKey = devKey.OpenSubKey(inst, false);
                        if (instKey == null) continue;
                        var classGuid = instKey.GetValue("ClassGUID") as string;
                        var isGpu = string.Equals(classGuid, "{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase);
                        var isNet = string.Equals(classGuid, "{4d36e972-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase);
                        if (!isGpu && !isNet) continue;
                        using var msiProps = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", true);
                        if (msiProps == null) continue;
                        try
                        {
                            int? prev = null;
                            var val = msiProps.GetValue("MSISupported");
                            if (val is int i) prev = i;
                            var item = new MsiBackupItem { Path = instKey.Name + "\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties", Previous = prev };
                            msiProps.SetValue("MSISupported", 1, RegistryValueKind.DWord);
                            // Backup Affinity Policy DevicePriority
                            try
                            {
                                using var affRO = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy", false);
                                if (affRO != null)
                                {
                                    var dp = affRO.GetValue("DevicePriority");
                                    if (dp is string s) item.DevicePriority = s;
                                    else if (dp is int di) item.DevicePriority = di.ToString();
                                }
                            }
                            catch { }
                            backup.Add(item);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Falha ao ajustar MSI", ex);
                        }
                        try
                        {
                            using var aff = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy", true);
                            if (aff != null)
                            {
                                try
                                {
                                    var kind = aff.GetValueKind("DevicePriority");
                                    if (kind == RegistryValueKind.String)
                                        aff.SetValue("DevicePriority", "High", RegistryValueKind.String);
                                    else
                                        aff.SetValue("DevicePriority", 3, RegistryValueKind.DWord);
                                }
                                catch
                                {
                                    aff.SetValue("DevicePriority", "High", RegistryValueKind.String);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Falha ao ajustar prioridade de interrupção", ex);
                        }
                    }
                }
                try
                {
                    var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    File.WriteAllText(backupPath, json);
                }
                catch { }
                _logger.LogSuccess("MSI ativado para GPU e Rede quando suportado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao ativar MSI", ex);
                return false;
            }
        }

        public bool RestoreMsiSettings()
        {
            try
            {
                if (DryRun)
                {
                    _logger.LogInfo("[DRY-RUN] Restaurar configurações MSI");
                    return true;
                }
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para HKLM");
                    return false;
                }
                var backupPath = Path.Combine(_backupDir, _msiBackupFileName);
                if (!File.Exists(backupPath)) return false;
                var list = JsonSerializer.Deserialize<List<MsiBackupItem>>(File.ReadAllText(backupPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<MsiBackupItem>();
                foreach (var item in list)
                {
                    try
                    {
                        using var props = Registry.LocalMachine.OpenSubKey(item.Path, true);
                        if (props != null)
                        {
                            if (item.Previous.HasValue)
                                props.SetValue("MSISupported", item.Previous.Value, RegistryValueKind.DWord);
                            else
                                props.DeleteValue("MSISupported", false);
                        }
                        var affPath = item.Path.Replace("MessageSignaledInterruptProperties", "Affinity Policy");
                        using var aff = Registry.LocalMachine.OpenSubKey(affPath, true);
                        if (aff != null && !string.IsNullOrEmpty(item.DevicePriority))
                        {
                            aff.SetValue("DevicePriority", item.DevicePriority, RegistryValueKind.String);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Falha ao restaurar MSI", ex);
                    }
                }
                _logger.LogSuccess("MSI restaurado conforme backup");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar MSI", ex);
                return false;
            }
        }

        private class MsiBackupItem
        {
            public string Path { get; set; } = string.Empty;
            public int? Previous { get; set; }
            public string? DevicePriority { get; set; }
        }
        private static bool RunProcess(string fileName, string args, out string stdOut, out string stdErr)
        {
            stdOut = string.Empty;
            stdErr = string.Empty;
            try
            {


                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                stdOut = p.StandardOutput.ReadToEnd();
                stdErr = p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private class NetworkBackup
        {
            public DateTime Timestamp { get; set; }
            public List<InterfaceBackup> Interfaces { get; set; } = new List<InterfaceBackup>();
            public int? NetworkThrottlingIndex { get; set; }
        }

        private class InterfaceBackup
        {
            public string Key { get; set; } = string.Empty;
            public int? TcpAckFrequency { get; set; }
            public int? TCPNoDelay { get; set; }
        }
    }
}

