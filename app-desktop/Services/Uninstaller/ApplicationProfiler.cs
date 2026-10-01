using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Uninstaller
{
    /// <summary>
    /// FASE 1: Criação de perfil detalhado do aplicativo
    /// </summary>
    public class ApplicationProfiler
    {
        private readonly ILoggingService _logger;

        public ApplicationProfiler(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<ApplicationProfile> CreateApplicationProfile(AppInfo app)
        {
            var profile = new ApplicationProfile
            {
                Name = app.Name,
                Publisher = app.Publisher,
                Version = app.Version,
                InstallLocation = app.InstallLocation,
                DisplayIcon = app.DisplayIcon,
                UninstallString = app.UninstallString,
                QuietUninstallString = app.QuietUninstallString,
                RegistryPath = app.RegistryPath,
                Guid = app.Guid,
                IsWin32 = app.IsWin32
            };

            // Identificar se é MSI
            profile.IsMsi = !string.IsNullOrEmpty(app.Guid) && 
                           app.Guid.StartsWith("{") && 
                           app.Guid.EndsWith("}");

            // Identificar executáveis principais
            if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var exeFiles = Directory.GetFiles(app.InstallLocation, "*.exe", SearchOption.AllDirectories)
                            .Where(f => !f.Contains("unins", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .Take(5)
                            .ToList();

                        profile.MainExecutables.AddRange(exeFiles);
                        _logger.LogInfo($"[Profiler] Encontrados {exeFiles.Count} executáveis principais.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[Profiler] Erro ao escanear executáveis: {ex.Message}");
                    }
                });
            }

            // Identificar processos em execução
            await Task.Run(() =>
            {
                try
                {
                    var runningProcs = Process.GetProcesses()
                        .Where(p =>
                        {
                            try
                            {
                                return p.ProcessName.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ||
                                       (!string.IsNullOrEmpty(app.Publisher) && 
                                        p.ProcessName.Contains(app.Publisher, StringComparison.OrdinalIgnoreCase));
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .Select(p => p.ProcessName)
                        .Distinct()
                        .ToList();

                    profile.RunningProcesses.AddRange(runningProcs);
                    
                    if (runningProcs.Any())
                    {
                        _logger.LogInfo($"[Profiler] Encontrados {runningProcs.Count} processos em execução.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Profiler] Erro ao identificar processos: {ex.Message}");
                }
            });

            // Identificar serviços relacionados
            await Task.Run(() =>
            {
                try
                {
                    var services = System.ServiceProcess.ServiceController.GetServices()
                        .Where(s =>
                        {
                            try
                            {
                                return s.ServiceName.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ||
                                       s.DisplayName.Contains(app.Name, StringComparison.OrdinalIgnoreCase) ||
                                       (!string.IsNullOrEmpty(app.Publisher) &&
                                        (s.ServiceName.Contains(app.Publisher, StringComparison.OrdinalIgnoreCase) ||
                                         s.DisplayName.Contains(app.Publisher, StringComparison.OrdinalIgnoreCase)));
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .Select(s => s.ServiceName)
                        .ToList();

                    profile.RelatedServices.AddRange(services);
                    
                    if (services.Any())
                    {
                        _logger.LogInfo($"[Profiler] Encontrados {services.Count} serviços relacionados.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Profiler] Erro ao identificar serviços: {ex.Message}");
                }
            });

            return profile;
        }
    }

    /// <summary>
    /// Perfil completo de um aplicativo instalado
    /// </summary>
    public class ApplicationProfile
    {
        public string Name { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? InstallLocation { get; set; }
        public string? DisplayIcon { get; set; }
        public string? UninstallString { get; set; }
        public string? QuietUninstallString { get; set; }
        public string? RegistryPath { get; set; }
        public string? Guid { get; set; }
        public bool IsWin32 { get; set; }
        public bool IsMsi { get; set; }

        public List<string> MainExecutables { get; set; } = new();
        public List<string> RunningProcesses { get; set; } = new();
        public List<string> RelatedServices { get; set; } = new();

        public bool HasUninstaller => 
            !string.IsNullOrEmpty(UninstallString) || 
            !string.IsNullOrEmpty(QuietUninstallString) ||
            IsMsi;
    }
}
