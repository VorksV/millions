using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// Serviço de auditoria de segurança para logs
    /// Detecta e reporta informações sensíveis expostas
    /// </summary>
    public static class SecurityAuditService
    {
        /// <summary>
        /// Resultado de uma auditoria de segurança
        /// </summary>
        public class SecurityAuditResult
        {
            public DateTime AuditTimestamp { get; set; }
            public string LogFilePath { get; set; } = string.Empty;
            public int TotalLines { get; set; }
            public int SensitiveLinesFound { get; set; }
            public List<SensitiveDataFinding> Findings { get; set; } = new();
            public SecurityAuditSeverity OverallSeverity { get; set; }
            public bool IsSecure => OverallSeverity == SecurityAuditSeverity.Safe;
            public string Summary => GetSummary();
            
            private string GetSummary()
            {
                return OverallSeverity switch
                {
                    SecurityAuditSeverity.Safe => "[OK] Nenhuma informação sensível detectada",
                    SecurityAuditSeverity.Low => "[WARN] Poucas informações sensíveis detectadas",
                    SecurityAuditSeverity.Medium => "[ALERT] Informações sensíveis significativas detectadas",
                    SecurityAuditSeverity.High => "[CRITICAL] Vazamento crítico de informações sensíveis",
                    SecurityAuditSeverity.Critical => "[RISK_CRITICAL] RISCO CRÍTICO - Dados sensíveis expostos",
                    _ => "[UNKNOWN] Status desconhecido"
                };
            }
        }

        /// <summary>
        /// Finding de informação sensível
        /// </summary>
        public class SensitiveDataFinding
        {
            public int LineNumber { get; set; }
            public string LineContent { get; set; } = string.Empty;
            public SensitiveDataType DataType { get; set; }
            public string Description { get; set; } = string.Empty;
            public SecurityAuditSeverity Severity { get; set; }
            public string Recommendation => GetRecommendation();
            
            private string GetRecommendation()
            {
                return DataType switch
                {
                    SensitiveDataType.JWTToken => "Mascarar completamente tokens JWT em logs",
                    SensitiveDataType.HwidFull => "Truncar HWIDs para apenas 8 caracteres",
                    SensitiveDataType.ApiKey => "Remover chaves API completas dos logs",
                    SensitiveDataType.SupabaseUrl => "Mascarar endpoints de APIs Supabase",
                    SensitiveDataType.EmailAddress => "Remover endereços de email dos logs",
                    SensitiveDataType.AuthToken => "Mascarar tokens de autenticação",
                    SensitiveDataType.AssemblyHash => "Remover hashes de assembly dos logs",
                    SensitiveDataType.UrlWithParams => "Remover parâmetros de URLs sensíveis",
                    _ => "Revisar e mascarar informação sensível"
                };
            }
        }

        /// <summary>
        /// Tipos de dados sensíveis
        /// </summary>
        public enum SensitiveDataType
        {
            JWTToken,
            HwidFull,
            ApiKey,
            SupabaseUrl,
            EmailAddress,
            AuthToken,
            AssemblyHash,
            UrlWithParams,
            Unknown
        }

        /// <summary>
        /// Níveis de severidade de segurança
        /// </summary>
        public enum SecurityAuditSeverity
        {
            Safe = 0,
            Low = 1,
            Medium = 2,
            High = 3,
            Critical = 4
        }

        /// <summary>
        /// Realiza auditoria completa de segurança em um arquivo de log
        /// </summary>
        /// <param name="logFilePath">Caminho do arquivo de log</param>
        /// <returns>Resultado da auditoria</returns>
        public static SecurityAuditResult AuditLogFile(string logFilePath)
        {
            var result = new SecurityAuditResult
            {
                AuditTimestamp = DateTime.Now,
                LogFilePath = logFilePath
            };

            if (!File.Exists(logFilePath))
            {
                result.OverallSeverity = SecurityAuditSeverity.Safe;
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(logFilePath);
                result.TotalLines = lines.Length;

                for (int i = 0; i < lines.Length; i++)
                {
                    var findings = AnalyzeLine(lines[i], i + 1);
                    result.Findings.AddRange(findings);
                }

                result.SensitiveLinesFound = result.Findings.Count;
                result.OverallSeverity = CalculateOverallSeverity(result.Findings);
            }
            catch (Exception ex)
            {
                result.Findings.Add(new SensitiveDataFinding
                {
                    LineNumber = 0,
                    LineContent = ex.Message,
                    DataType = SensitiveDataType.Unknown,
                    Description = "Erro ao analisar arquivo de log",
                    Severity = SecurityAuditSeverity.Medium
                });
            }

            return result;
        }

        /// <summary>
        /// Analisa uma linha em busca de informações sensíveis
        /// </summary>
        /// <param name="line">Linha a analisar</param>
        /// <param name="lineNumber">Número da linha</param>
        /// <returns>Lista de findings</returns>
        private static List<SensitiveDataFinding> AnalyzeLine(string line, int lineNumber)
        {
            var findings = new List<SensitiveDataFinding>();

            // JWT Tokens
            if (line.Contains("eyJ") && line.Contains('.') && line.Split('.').Length >= 3)
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.JWTToken,
                    Description = "Token JWT completo detectado",
                    Severity = SecurityAuditSeverity.Critical
                });
            }

            // HWIDs completos (hashes longos em hexadecimal)
            var hwidPattern = System.Text.RegularExpressions.Regex.Match(line, @"\b[a-fA-F0-9]{64,128}\b");
            if (hwidPattern.Success)
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.HwidFull,
                    Description = "HWID completo detectado",
                    Severity = SecurityAuditSeverity.High
                });
            }

            // Contexto de HWID
            var hwidContextKeywords = new[] { "HWID:", "🔑 HWID:", "🔑 Chave:", "CurrentHwid:", "_currentHwid" };
            foreach (var keyword in hwidContextKeywords)
            {
                if (line.Contains(keyword))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, $@"{keyword}\s*([a-fA-F0-9]{{16}})");
                    if (match.Success && match.Groups[1].Value.Length > 16)
                    {
                        findings.Add(new SensitiveDataFinding
                        {
                            LineNumber = lineNumber,
                            LineContent = line,
                            DataType = SensitiveDataType.HwidFull,
                            Description = $"HWID completo em contexto ({keyword})",
                            Severity = SecurityAuditSeverity.High
                        });
                    }
                }
            }

            // URLs Supabase
            if (line.Contains("supabase.co") && line.Contains("functions/v1"))
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.SupabaseUrl,
                    Description = "URL de API Supabase detectada",
                    Severity = SecurityAuditSeverity.Medium
                });
            }

            // Chaves API longas
            var apiKeyPattern = System.Text.RegularExpressions.Regex.Match(line, @"[a-zA-Z0-9_-]{32}");
            if (apiKeyPattern.Success && !line.Contains("eyJ")) // Evitar duplicar com JWT
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.ApiKey,
                    Description = "Chave API longa detectada",
                    Severity = SecurityAuditSeverity.Medium
                });
            }

            // Contexto de API Key
            var apiKeyContextKeywords = new[] { "🔑 Chave:", "Chave:", "service_role key", "supabaseKey:" };
            foreach (var keyword in apiKeyContextKeywords)
            {
                if (line.Contains(keyword))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, $@"{keyword}\s*([a-zA-Z0-9_-]{{20}})");
                    if (match.Success)
                    {
                        findings.Add(new SensitiveDataFinding
                        {
                            LineNumber = lineNumber,
                            LineContent = line,
                            DataType = SensitiveDataType.ApiKey,
                            Description = $"Chave API em contexto ({keyword})",
                            Severity = SecurityAuditSeverity.High
                        });
                    }
                }
            }

            // Emails
            var emailPattern = System.Text.RegularExpressions.Regex.Match(line, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2}\b");
            if (emailPattern.Success)
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.EmailAddress,
                    Description = "Endereço de email detectado",
                    Severity = SecurityAuditSeverity.Low
                });
            }

            // Assembly hashes
            var assemblyHashPattern = System.Text.RegularExpressions.Regex.Match(line, @"[A-F0-9]{16}");
            if (assemblyHashPattern.Success && line.Contains("Assembly hash"))
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.AssemblyHash,
                    Description = "Hash de assembly detectado",
                    Severity = SecurityAuditSeverity.Low
                });
            }

            // URLs com parâmetros
            var urlWithParamsPattern = System.Text.RegularExpressions.Regex.Match(line, @"https?://[^\s\?]+\?[^\s]*");
            if (urlWithParamsPattern.Success && !line.Contains("voltris.com.br")) // Ignorar URLs seguras conhecidas
            {
                findings.Add(new SensitiveDataFinding
                {
                    LineNumber = lineNumber,
                    LineContent = line,
                    DataType = SensitiveDataType.UrlWithParams,
                    Description = "URL com parâmetros detectada",
                    Severity = SecurityAuditSeverity.Low
                });
            }

            return findings;
        }

        /// <summary>
        /// Calcula a severidade geral com base nos findings
        /// </summary>
        /// <param name="findings">Lista de findings</param>
        /// <returns>Severidade geral</returns>
        private static SecurityAuditSeverity CalculateOverallSeverity(List<SensitiveDataFinding> findings)
        {
            if (!findings.Any())
                return SecurityAuditSeverity.Safe;

            var maxSeverity = findings.Max(f => f.Severity);
            
            // Ajustar baseado na quantidade
            var criticalCount = findings.Count(f => f.Severity == SecurityAuditSeverity.Critical);
            var highCount = findings.Count(f => f.Severity == SecurityAuditSeverity.High);
            
            if (criticalCount > 0)
                return SecurityAuditSeverity.Critical;
            
            if (highCount > 3)
                return SecurityAuditSeverity.Critical;
            
            if (highCount > 1)
                return SecurityAuditSeverity.High;
            
            return maxSeverity;
        }

        /// <summary>
        /// Gera relatório detalhado da auditoria
        /// </summary>
        /// <param name="result">Resultado da auditoria</param>
        /// <returns>Relatório formatado</returns>
        public static string GenerateReport(SecurityAuditResult result)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("=== AUDITORIA DE SEGURANÇA DE LOGS ===");
            report.AppendLine($"Data: {result.AuditTimestamp:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Arquivo: {result.LogFilePath}");
            report.AppendLine($"Linhas totais: {result.TotalLines}");
            report.AppendLine($"Linhas com dados sensíveis: {result.SensitiveLinesFound}");
            report.AppendLine($"Severidade geral: {result.OverallSeverity}");
            report.AppendLine($"Status: {result.Summary}");
            report.AppendLine();

            if (!result.Findings.Any())
            {
                report.AppendLine("✅ Nenhuma informação sensível detectada. O sistema está seguro.");
                return report.ToString();
            }

            report.AppendLine("=== FINDINGS ENCONTRADOS ===");
            report.AppendLine();

            var groupedFindings = result.Findings.GroupBy(f => f.DataType).OrderByDescending(g => g.Max(f => f.Severity));

            foreach (var group in groupedFindings)
            {
                report.AppendLine($"[DATA] {group.Key} ({group.Count()} ocorrências) - Severidade: {group.Max(f => f.Severity)}");
                
                foreach (var finding in group.Take(5)) // Limitar a 5 exemplos por tipo
                {
                    report.AppendLine($"   Linha {finding.LineNumber}: {finding.Description}");
                    report.AppendLine($"   Recomendação: {finding.Recommendation}");
                    report.AppendLine();
                }
                
                if (group.Count() > 5)
                {
                    report.AppendLine($"   ... e mais {group.Count() - 5} ocorrências");
                }
                
                report.AppendLine();
            }

            report.AppendLine("=== RECOMENDAÇÕES GERAIS ===");
            report.AppendLine("1. Implementar mascaramento automático de informações sensíveis");
            report.AppendLine("2. Usar níveis diferentes de logging para produção vs desenvolvimento");
            report.AppendLine("3. Remover logs detalhados de ambiente de produção");
            report.AppendLine("4. Implementar validação de segurança em tempo de execução");
            report.AppendLine("5. Revisar permissões de acesso aos arquivos de log");

            return report.ToString();
        }

        /// <summary>
        /// Salva relatório em arquivo JSON
        /// </summary>
        /// <param name="result">Resultado da auditoria</param>
        /// <param name="filePath">Caminho para salvar</param>
        public static void SaveReportAsJson(SecurityAuditResult result, string filePath)
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Valida se o sistema de logging atual está seguro
        /// </summary>
        /// <param name="logDirectory">Diretório dos logs</param>
        /// <returns>True se seguro</returns>
        public static bool ValidateLoggingSecurity(string logDirectory)
        {
            if (!Directory.Exists(logDirectory))
                return true;

            var logFiles = Directory.GetFiles(logDirectory, "*.log");
            var allSecure = true;

            foreach (var logFile in logFiles)
            {
                var audit = AuditLogFile(logFile);
                if (!audit.IsSecure)
                {
                    allSecure = false;
                    break;
                }
            }

            return allSecure;
        }
    }
}
