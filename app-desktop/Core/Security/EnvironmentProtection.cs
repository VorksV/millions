using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace VoltrisOptimizer.Core.Security
{
    /// <summary>
    /// EnvironmentProtection — Detecta manipulação de variáveis de ambiente,
    /// proxies maliciosos, ambientes de sandbox e virtualização suspeita.
    /// </summary>
    public static class EnvironmentProtection
    {
        // Variáveis de ambiente que não devem existir em ambiente limpo
        private static readonly string[] SuspiciousEnvVars = {
            "METERPRETER", "MSF_", "COBALT", "BEACON_",
            "C2_SERVER", "PAYLOAD_", "REVERSE_",
            "INJECT_DLL", "HOOK_DLL", "DEBUG_ATTACH"
        };

        // Proxies conhecidos de interceptação
        private static readonly string[] SuspiciousProxyPorts = {
            "8080", "8888", "9090", "8443", "3128", "1080"
        };

        /// <summary>
        /// Verifica variáveis de ambiente suspeitas que indicam
        /// ferramentas de ataque configuradas no sistema.
        /// </summary>
        public static List<EnvironmentThreat> ScanEnvironmentVariables()
        {
            var threats = new List<EnvironmentThreat>();

            try
            {
                var envVars = Environment.GetEnvironmentVariables();
                foreach (string key in envVars.Keys)
                {
                    var keyUpper = key.ToUpperInvariant();
                    var value = envVars[key]?.ToString() ?? "";

                    // Verificar nomes suspeitos
                    foreach (var pattern in SuspiciousEnvVars)
                    {
                        if (keyUpper.Contains(pattern))
                        {
                            threats.Add(new EnvironmentThreat
                            {
                                Type = "SuspiciousEnvVar",
                                Key = key,
                                Value = value.Length > 50 ? value[..50] + "..." : value,
                                Severity = "High",
                                Description = $"Variável de ambiente suspeita: {key} (padrão: {pattern})"
                            });
                            LogSecurity($"ENV VAR SUSPEITA: {key}={value[..Math.Min(50, value.Length)]}");
                        }
                    }

                    // Verificar se PATH foi manipulado para incluir diretórios temporários
                    if (keyUpper == "PATH")
                    {
                        var paths = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in paths)
                        {
                            var lower = p.ToLowerInvariant().Trim();
                            if (lower.Contains(@"\temp\") || lower.Contains(@"\tmp\") ||
                                lower.Contains(@"\appdata\local\temp"))
                            {
                                threats.Add(new EnvironmentThreat
                                {
                                    Type = "PathManipulation",
                                    Key = "PATH",
                                    Value = p,
                                    Severity = "Medium",
                                    Description = $"PATH contém diretório temporário: {p}"
                                });
                                LogSecurity($"PATH manipulado com dir temporário: {p}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSecurity($"Erro ao escanear variáveis de ambiente: {ex.Message}");
            }

            return threats;
        }

        /// <summary>
        /// Detecta se há proxy configurado no sistema que pode interceptar tráfego.
        /// </summary>
        public static ProxyDetectionResult DetectProxy()
        {
            var result = new ProxyDetectionResult();

            try
            {
                // 1. Verificar proxy do sistema via WebRequest
                var systemProxy = WebRequest.GetSystemWebProxy();
                var testUri = new Uri("https://voltris.com.br");
                var proxyUri = systemProxy.GetProxy(testUri);

                if (proxyUri != null && proxyUri != testUri)
                {
                    result.HasProxy = true;
                    result.ProxyAddress = proxyUri.ToString();
                    result.ProxyPort = proxyUri.Port;

                    // Verificar se é proxy suspeito (porta de interceptação)
                    result.IsSuspicious = SuspiciousProxyPorts.Contains(proxyUri.Port.ToString());

                    LogSecurity($"Proxy detectado: {result.ProxyAddress} (Suspeito: {result.IsSuspicious})");
                }

                // 2. Verificar variáveis de proxy
                var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ??
                                Environment.GetEnvironmentVariable("http_proxy");
                var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ??
                                 Environment.GetEnvironmentVariable("https_proxy");

                if (!string.IsNullOrEmpty(httpProxy))
                {
                    result.HasProxy = true;
                    result.HttpProxy = httpProxy;
                    LogSecurity($"HTTP_PROXY configurado: {httpProxy}");
                }

                if (!string.IsNullOrEmpty(httpsProxy))
                {
                    result.HasProxy = true;
                    result.HttpsProxy = httpsProxy;
                    LogSecurity($"HTTPS_PROXY configurado: {httpsProxy}");
                }
            }
            catch (Exception ex)
            {
                LogSecurity($"Erro na detecção de proxy: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Detecta se a aplicação está rodando em ambiente de sandbox ou VM suspeita.
        /// </summary>
        public static SandboxDetectionResult DetectSandbox()
        {
            var result = new SandboxDetectionResult();

            try
            {
                // 1. Verificar processos de sandbox
                var sandboxProcesses = new[] {
                    "vboxservice", "vboxtray", "vmtoolsd", "vmwaretray",
                    "sandboxie", "sbiectrl", "sbieDll",
                    "wireshark", "fiddler", "charles", "burpsuite",
                    "procmon", "procexp", "apimonitor"
                };

                var running = System.Diagnostics.Process.GetProcesses()
                    .Select(p => { try { return p.ProcessName.ToLowerInvariant(); } catch { return ""; } })
                    .ToHashSet();

                foreach (var sp in sandboxProcesses)
                {
                    if (running.Contains(sp))
                    {
                        result.IsInSandbox = true;
                        result.DetectedTools.Add(sp);
                    }
                }

                // 2. Verificar MAC address de VM
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    var mac = nic.GetPhysicalAddress().ToString().ToUpperInvariant();
                    if (mac.Length < 6) continue;

                    var prefix = mac[..6];
                    // VMware: 00:0C:29, 00:50:56
                    // VirtualBox: 08:00:27
                    // Hyper-V: 00:15:5D
                    if (prefix is "000C29" or "005056" or "080027" or "00155D")
                    {
                        result.IsVirtualMachine = true;
                        result.VmType = prefix switch
                        {
                            "000C29" or "005056" => "VMware",
                            "080027" => "VirtualBox",
                            "00155D" => "Hyper-V",
                            _ => "Unknown VM"
                        };
                    }
                }

                // 3. Verificar arquivos de sandbox
                var sandboxFiles = new[] {
                    @"C:\windows\system32\drivers\vboxdrv.sys",
                    @"C:\windows\system32\drivers\vmhgfs.sys",
                    @"C:\windows\system32\drivers\vmmouse.sys"
                };

                foreach (var file in sandboxFiles)
                {
                    if (File.Exists(file))
                    {
                        result.IsVirtualMachine = true;
                        result.DetectedTools.Add(Path.GetFileName(file));
                    }
                }

                if (result.IsInSandbox || result.DetectedTools.Count > 0)
                {
                    LogSecurity($"Sandbox/VM detectado: Tools={string.Join(",", result.DetectedTools)}, VM={result.VmType}");
                }
            }
            catch (Exception ex)
            {
                LogSecurity($"Erro na detecção de sandbox: {ex.Message}");
            }

            return result;
        }

        private static void LogSecurity(string message)
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                Directory.CreateDirectory(logDir);
                File.AppendAllText(
                    Path.Combine(logDir, "environment_security.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }

    #region Models

    public class EnvironmentThreat
    {
        public string Type { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";
        public string Description { get; set; } = string.Empty;
    }

    public class ProxyDetectionResult
    {
        public bool HasProxy { get; set; }
        public bool IsSuspicious { get; set; }
        public string ProxyAddress { get; set; } = string.Empty;
        public int ProxyPort { get; set; }
        public string? HttpProxy { get; set; }
        public string? HttpsProxy { get; set; }
    }

    public class SandboxDetectionResult
    {
        public bool IsInSandbox { get; set; }
        public bool IsVirtualMachine { get; set; }
        public string VmType { get; set; } = string.Empty;
        public List<string> DetectedTools { get; set; } = new();
    }

    #endregion
}
