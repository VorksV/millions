using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Providers
{
    public class VisualCppProvider : IGameDependencyProvider
    {
        private readonly ILoggingService _logger;
        private readonly ISecureDownloadService _downloader;

        public string ProviderName => "Microsoft Visual C++ Redistributable";

        private class VcDefinition
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public string Keyword { get; set; }
            public string UrlX64 { get; set; }
            public string UrlX86 { get; set; }
            public string InstallArgs { get; set; }
            public string ExpectedDll { get; set; }
        }

        private readonly List<VcDefinition> _versions = new List<VcDefinition>
        {
            new VcDefinition { Id = "VC_2005", Label = "Visual C++ 2005", Keyword = "Visual C++ 2005", UrlX64 = "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x64.EXE", UrlX86 = "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x86.EXE", InstallArgs = "/q:a /c:\"msiexec /i vcredist.msi /qn /norestart\"", ExpectedDll = "msvcp80.dll" },
            new VcDefinition { Id = "VC_2008", Label = "Visual C++ 2008", Keyword = "Visual C++ 2008", UrlX64 = "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x64.exe", UrlX86 = "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x86.exe", InstallArgs = "/q:a /c:\"msiexec /i vcredist.msi /qn /norestart\"", ExpectedDll = "msvcp90.dll" },
            new VcDefinition { Id = "VC_2010", Label = "Visual C++ 2010", Keyword = "Visual C++ 2010", UrlX64 = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe", UrlX86 = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe", InstallArgs = "/quiet /norestart", ExpectedDll = "msvcp100.dll" },
            new VcDefinition { Id = "VC_2012", Label = "Visual C++ 2012", Keyword = "Visual C++ 2012", UrlX64 = "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe", UrlX86 = "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe", InstallArgs = "/quiet /norestart", ExpectedDll = "msvcp110.dll" },
            new VcDefinition { Id = "VC_2013", Label = "Visual C++ 2013", Keyword = "Visual C++ 2013", UrlX64 = "https://aka.ms/highdpimfc2013x64enu", UrlX86 = "https://aka.ms/highdpimfc2013x86enu", InstallArgs = "/quiet /norestart", ExpectedDll = "msvcp120.dll" },
            new VcDefinition { Id = "VC_2015_2022", Label = "Visual C++ 2015-2022", Keyword = "Visual C++ 2015", UrlX64 = "https://aka.ms/vs/17/release/vc_redist.x64.exe", UrlX86 = "https://aka.ms/vs/17/release/vc_redist.x86.exe", InstallArgs = "/install /quiet /norestart", ExpectedDll = "msvcp140.dll" }
        };

        public VisualCppProvider(ILoggingService logger, ISecureDownloadService downloader)
        {
            _logger = logger;
            _downloader = downloader;
        }

        public async Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct)
        {
            var results = new List<GameDependencyDiagnosis>();
            bool is64Bit = Environment.Is64BitOperatingSystem;

            foreach (var vc in _versions)
            {
                ct.ThrowIfCancellationRequested();
                var diag = new GameDependencyDiagnosis
                {
                    ComponentId = vc.Id,
                    Title = vc.Label,
                    Description = $"Bibliotecas runtime essenciais C/C++ ({vc.Label})."
                };

                // Evidência 1: Registro (DisplayName e Publisher)
                bool hasRegX64 = is64Bit && CheckRegistry(vc.Keyword, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                bool hasRegX86 = CheckRegistry(vc.Keyword, is64Bit ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                
                _logger.LogInfo($"[VisualCppProvider] {vc.Id} - Verificação de Registro: x64={hasRegX64}, x86={hasRegX86}");
                
                // Evidência 2: Arquivos DLL físicos em Sys32/SysWOW64
                string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System));
                string sysWow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
                
                bool hasDllX64 = is64Bit && File.Exists(Path.Combine(sys32, vc.ExpectedDll));
                bool hasDllX86 = is64Bit ? File.Exists(Path.Combine(sysWow, vc.ExpectedDll)) : File.Exists(Path.Combine(sys32, vc.ExpectedDll));

                _logger.LogInfo($"[VisualCppProvider] {vc.Id} - Verificação de DLL física ({vc.ExpectedDll}): x64={hasDllX64}, x86={hasDllX86}");

                if (is64Bit)
                {
                    diag.IsHealthy = (hasRegX64 || hasDllX64) && (hasRegX86 || hasDllX86);
                    if (!hasRegX64 && !hasDllX64) diag.MissingEvidences.Add("x64 Runtime Missing");
                    if (!hasRegX86 && !hasDllX86) diag.MissingEvidences.Add("x86 Runtime Missing");
                }
                else
                {
                    diag.IsHealthy = hasRegX86 || hasDllX86;
                    if (!diag.IsHealthy) diag.MissingEvidences.Add("x86 Runtime Missing");
                }

                if (diag.IsHealthy)
                {
                    diag.Evidences.Add("Registry/DLLs verificadas");
                    _logger.LogInfo($"[VisualCppProvider] {vc.Id} considerado ÍNTEGRO (Healthy). Evidências comprovadas.");
                }
                else
                {
                    _logger.LogWarning($"[VisualCppProvider] {vc.Id} diagnosticado com FALHA. Faltam {diag.MissingEvidences.Count} evidências essenciais.");
                }

                results.Add(diag);
            }

            return results;
        }

        private bool CheckRegistry(string keyword, string path)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) return false;
                
                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var name = sub?.GetValue("DisplayName") as string;
                    var publisher = sub?.GetValue("Publisher") as string;
                    
                    if (!string.IsNullOrEmpty(name) && name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        if (publisher != null && publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public async Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            var vc = _versions.FirstOrDefault(v => v.Id == issue.ComponentId);
            if (vc == null) throw new Exception("VC++ version not recognized.");

            var result = new GameDependencyRepairResult();
            var sw = new Stopwatch();
            sw.Start();

            bool is64Bit = Environment.Is64BitOperatingSystem;
            
            // X64 Download and Install
            if (is64Bit)
            {
                progress?.Report((GameDependencyState.Downloading, 10, $"Baixando {vc.Label} x64..."));
                string x64Exe = await _downloader.DownloadAndVerifyAsync(vc.UrlX64, "", true, ct);
                result.DownloadTimeMs += sw.ElapsedMilliseconds;
                
                sw.Restart();
                progress?.Report((GameDependencyState.Installing, 30, $"Instalando {vc.Label} x64..."));
                await RunInstallerAsync(x64Exe, vc.InstallArgs, ct);
                result.InstallTimeMs += sw.ElapsedMilliseconds;
            }

            // X86 Download and Install (Sempre instala x86, mesmo em x64, por conta de jogos legados)
            sw.Restart();
            progress?.Report((GameDependencyState.Downloading, 50, $"Baixando {vc.Label} x86..."));
            string x86Exe = await _downloader.DownloadAndVerifyAsync(vc.UrlX86, "", true, ct);
            result.DownloadTimeMs += sw.ElapsedMilliseconds;

            sw.Restart();
            progress?.Report((GameDependencyState.Installing, 70, $"Instalando {vc.Label} x86..."));
            await RunInstallerAsync(x86Exe, vc.InstallArgs, ct);
            result.InstallTimeMs += sw.ElapsedMilliseconds;

            // Wait a moment for stabilization (files being registered, MSI finishing background workers)
            await Task.Delay(3000, ct);

            result.Success = true;
            return result;
        }

        private async Task RunInstallerAsync(string exePath, string args, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<int>();
            using var proc = new Process();
            proc.StartInfo.FileName = exePath;
            proc.StartInfo.Arguments = args;
            proc.StartInfo.UseShellExecute = true; // Necessário para UAC/Wrapper MSI
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.EnableRaisingEvents = true;

            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            using (ct.Register(() => 
            {
                tcs.TrySetCanceled();
                try { if (!proc.HasExited) proc.Kill(); } catch { }
            }))
            {
                if (!proc.Start()) throw new Exception("Falha ao iniciar o processo do instalador.");
                
                int code = await tcs.Task;
                _logger.LogInfo($"[VisualCppProvider] Instalador finalizou com código {code}.");
                
                // Aceita 0 (Success), 3010 (Reboot required), 1638 (Another version is installed)
                if (code != 0 && code != 3010 && code != 1638)
                {
                    _logger.LogWarning($"[VisualCppProvider] Código não ideal recebido: {code}. Validação técnica dirá se funcionou.");
                }
            }
        }

        public async Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            // Validação pós-reparo: Reexecutamos o diagnóstico de forma isolada
            var diagnosis = await DiagnoseAsync(ct);
            var result = diagnosis.FirstOrDefault(d => d.ComponentId == issue.ComponentId);
            return result != null && result.IsHealthy;
        }

        public Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            _logger.LogWarning($"[VisualCppProvider] Rollback solicitado para {issue.ComponentId}. Nenhuma desinstalação automática implementada para não comprometer outros jogos.");
            return Task.CompletedTask;
        }
    }
}
