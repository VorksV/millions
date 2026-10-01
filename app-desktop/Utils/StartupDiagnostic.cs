using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// Utilitário para diagnosticar problemas de inicialização com Windows
    /// </summary>
    public static class StartupDiagnostic
    {
        public static void RunDiagnostic()
        {
            try
            {
                App.LoggingService?.LogInfo("[StartupDiagnostic] Iniciando diagnóstico de startup...");
                var logDir = LogDirectoryResolver.Resolve();
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                    
                var diagnosticFile = Path.Combine(logDir, "startup_diagnostic.log");
                App.LoggingService?.LogInfo($"[StartupDiagnostic] Gravando resultados em: {diagnosticFile}");
                var log = new System.Text.StringBuilder();
                log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===== DIAGNÓSTICO DE STARTUP =====\n");
                
                // 1. Verificar registro
                log.AppendLine("1. VERIFICANDO REGISTRO DO WINDOWS:");
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                    var value = key?.GetValue("VoltrisOptimizer");
                    if (value != null)
                    {
                        log.AppendLine($"   ✓ ENCONTRADO: {value}");
                        log.AppendLine($"   Tipo: {value.GetType().Name}");
                        
                        // Verificar se o arquivo existe
                        var command = value.ToString();
                        var exePath = ExtractExePath(command);
                        log.AppendLine($"   Caminho extraído: {exePath}");
                        
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            if (File.Exists(exePath))
                            {
                                log.AppendLine($"   ✓ ARQUIVO EXISTE");
                                var fileInfo = new FileInfo(exePath);
                                log.AppendLine($"   Tamanho: {fileInfo.Length} bytes");
                                log.AppendLine($"   Última modificação: {fileInfo.LastWriteTime}");
                            }
                            else
                            {
                                log.AppendLine($"   ✗ ARQUIVO NÃO EXISTE!");
                            }
                        }
                    }
                    else
                    {
                        log.AppendLine("   ✗ NÃO ENCONTRADO NO REGISTRO");
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"   ✗ ERRO: {ex.Message}");
                }
                
                log.AppendLine();
                
                // 2. Verificar caminhos possíveis
                log.AppendLine("2. VERIFICANDO CAMINHOS POSSÍVEIS:");
                var possiblePaths = new[]
                {
                    Environment.ProcessPath,
                    System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltrisOptimizer.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Voltris Optimizer", "VoltrisOptimizer.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris Optimizer", "VoltrisOptimizer.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Voltris Optimizer", "VoltrisOptimizer.exe")
                };
                
                foreach (var path in possiblePaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (File.Exists(path))
                        {
                            log.AppendLine($"   ✓ {path}");
                        }
                        else
                        {
                            log.AppendLine($"   ✗ {path} (não existe)");
                        }
                    }
                }
                
                log.AppendLine();
                
                // 3. Verificar privilégios
                log.AppendLine("3. VERIFICANDO PRIVILÉGIOS:");
                log.AppendLine($"   É Admin: {AdminHelper.IsRunningAsAdministrator()}");
                log.AppendLine($"   Usuário: {Environment.UserName}");
                log.AppendLine($"   Máquina: {Environment.MachineName}");
                
                log.AppendLine();
                
                // 4. Verificar logs de execução
                log.AppendLine("4. VERIFICANDO LOGS DE EXECUÇÃO:");
                var logFiles = new[]
                {
                    "immediate_startup.log",
                    "startup_execution.log",
                    "startup_config.log"
                };
                
                foreach (var logFileName in logFiles)
                {
                    var logFilePath = Path.Combine(logDir, logFileName);
                    if (File.Exists(logFilePath))
                    {
                        var fileInfo = new FileInfo(logFilePath);
                        log.AppendLine($"   ✓ {logFileName} existe ({fileInfo.Length} bytes, modificado em {fileInfo.LastWriteTime})");
                        
                        // Ler últimas 5 linhas
                        try
                        {
                            var lines = File.ReadAllLines(logFilePath);
                            var lastLines = lines.Length > 5 ? lines.Skip(lines.Length - 5) : lines;
                            log.AppendLine($"      Últimas linhas:");
                            foreach (var line in lastLines)
                            {
                                log.AppendLine($"         {line}");
                            }
                        }
                        catch (IOException ioEx)
                        {
                            App.LoggingService?.LogWarning($"[StartupDiagnostic] Processo de fundo está bloqueando o arquivo de log (File Lock). {ioEx.Message}");
                        }
                        catch (Exception ex)
                        {
                            App.LoggingService?.LogDebug($"[StartupDiagnostic] Erro menor tentado ler as ultimas linhas do diagnóstico: {ex.Message}");
                        }
                    }
                    else
                    {
                        log.AppendLine($"   ✗ {logFileName} não existe");
                    }
                }
                
                log.AppendLine();
                log.AppendLine($"===== FIM DO DIAGNÓSTICO =====\n");
                
                File.AppendAllText(diagnosticFile, log.ToString());
                App.LoggingService?.LogSuccess("[StartupDiagnostic] Diagnóstico concluído com sucesso.");
            }
            catch (Exception ex)
            {
                try
                {
                    var logDir = LogDirectoryResolver.Resolve();
                    var diagnosticFile = Path.Combine(logDir, "startup_diagnostic.log");
                    File.AppendAllText(diagnosticFile, 
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERRO AO EXECUTAR DIAGNÓSTICO: {ex.Message}\n{ex.StackTrace}\n\n");
                    App.LoggingService?.LogError($"[StartupDiagnostic] Erro fatal: {ex.Message}");
                }
                catch (UnauthorizedAccessException uex)
                {
                    App.LoggingService?.LogWarning($"[StartupDiagnostic] Proteção de Gravação ou UAC bloqueou a gravação do Log Fatal no disco. ({uex.Message})");
                }
                catch (IOException ioex)
                {
                    App.LoggingService?.LogError($"[StartupDiagnostic] Corrupção/Espaço Insuficiente ao gravar o Log Fatal no Arquivo: {ioex.Message}");
                }
                // Decisão Arquititetural: NÃO disparar `throw` pois isto é um Fallback Root Catch de Utilitário Diagnóstico em andamento no Startup Principal do WPF.
            }
        }
        
        private static string? ExtractExePath(string? command)
        {
            if (string.IsNullOrEmpty(command))
                return null;
                
            // Remover espaços extras
            command = command.Trim();
            
            // Se começa com aspas, extrair o caminho entre aspas
            if (command.StartsWith("\""))
            {
                var endQuote = command.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    return command.Substring(1, endQuote - 1);
                }
            }
            
            // Se não tem aspas, pegar até o primeiro espaço
            var firstSpace = command.IndexOf(" ");
            if (firstSpace > 0)
            {
                return command.Substring(0, firstSpace);
            }
            
            return command;
        }
    }
}
