using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// Utilitário para testar e validar o mascaramento de segurança
    /// Verifica se todas as informações sensíveis estão sendo protegidas
    /// </summary>
    public static class SecurityTestingUtility
    {
        /// <summary>
        /// Casos de teste para validação de mascaramento
        /// </summary>
        public static class TestCases
        {
            public static readonly List<SecurityTestCase> AllTests = new()
            {
                // JWT Tokens
                new SecurityTestCase
                {
                    Name = "JWT Token Supabase",
                    Input = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsInRlc3QiOiJzaW11bGFjYW8iLCJpYXQiOjE3MDAwMDAwMDAsImV4cCI6MTk5OTk5OTk5OX0.abc123def456ghi789jkl012mno345pqr678stuv901",
                    ExpectedPattern = "eyJ***[JWT_TOKEN_MASCARADO]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Critical
                },
                
                // HWIDs
                new SecurityTestCase
                {
                    Name = "HWID Completo",
                    Input = "🔑 HWID: 1b13c7beb22c60067072d6885404b96c5ec7088b58cef8e2b45fea0b0eb9abb6c6104e2b17db4cfaf3e21da8653c2a6e98483e9728e01d48c3bfc63011607dcb",
                    ExpectedPattern = "🔑 HWID: 1b13c7be...[HWID_MASCARADO]",
                    Severity = SecurityAuditService.SecurityAuditSeverity.High
                },
                
                new SecurityTestCase
                {
                    Name = "CurrentHwid Property",
                    Input = "CurrentHwid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2",
                    ExpectedPattern = "CurrentHwid: a1b2c3d4...[HWID_MASCARADO]",
                    Severity = SecurityAuditService.SecurityAuditSeverity.High
                },
                
                // URLs Supabase
                new SecurityTestCase
                {
                    Name = "URL Supabase Endpoint",
                    Input = "📡 URL: https://zamjyyzockbbugjepkhk.supabase.co/functions/v1/clever-endpoint",
                    ExpectedPattern = "https://***[SUPABASE_ENDPOINT_MASCARADO]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Medium
                },
                
                // Chaves API
                new SecurityTestCase
                {
                    Name = "Service Role Key",
                    Input = "🔑 Chave: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsInRlc3QiOiJzaW11bGFjYW8iLCJpYXQiOjE3MDAwMDAwMDAsImV4cCI6MTk5OTk5OTk5OX0.abc123def456ghi789jkl012mno345pqr678stuv901",
                    ExpectedPattern = "🔑 Chave: eyJhbGciOi...[API_KEY_MASCARADA]",
                    Severity = SecurityAuditService.SecurityAuditSeverity.High
                },
                
                // Emails
                new SecurityTestCase
                {
                    Name = "Endereço Email",
                    Input = "Email de contato: usuario@exemplo.com",
                    ExpectedPattern = "Email de contato: ***[EMAIL_MASCARADO]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Low
                },
                
                // Assembly Hashes
                new SecurityTestCase
                {
                    Name = "Assembly Hash",
                    Input = "Assembly hash: C0B52848799E058E962F014C41C5D6D7",
                    ExpectedPattern = "Assembly hash: ***[ASSEMBLY_HASH_MASCARADO]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Low
                },
                
                // URLs com parâmetros
                new SecurityTestCase
                {
                    Name = "URL com Parâmetros Sensíveis",
                    Input = "Request: https://api.example.com/users?token=abc123&secret=def456",
                    ExpectedPattern = "***[URL_COM_PARAMETROS_MASCARADA]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Low
                },
                
                // Tokens de autenticação
                new SecurityTestCase
                {
                    Name = "Auth Token",
                    Input = "Authorization: Bearer sk-1234567890abcdef1234567890abcdef12345678",
                    ExpectedPattern = "token: ***[AUTH_TOKEN_MASCARADO]***",
                    Severity = SecurityAuditService.SecurityAuditSeverity.Medium
                }
            };
        }

        /// <summary>
        /// Caso de teste individual
        /// </summary>
        public class SecurityTestCase
        {
            public string Name { get; set; } = string.Empty;
            public string Input { get; set; } = string.Empty;
            public string ExpectedPattern { get; set; } = string.Empty;
            public SecurityAuditService.SecurityAuditSeverity Severity { get; set; }
        }

        /// <summary>
        /// Resultado de um teste de segurança
        /// </summary>
        public class SecurityTestResult
        {
            public string TestCaseName { get; set; } = string.Empty;
            public string OriginalInput { get; set; } = string.Empty;
            public string MaskedOutput { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string Reason { get; set; } = string.Empty;
            public SecurityAuditService.SecurityAuditSeverity Severity { get; set; }
        }

        /// <summary>
        /// Executa todos os testes de segurança
        /// </summary>
        /// <returns>Resultados dos testes</returns>
        public static List<SecurityTestResult> RunAllSecurityTests()
        {
            var results = new List<SecurityTestResult>();

            foreach (var testCase in TestCases.AllTests)
            {
                var result = RunSingleTest(testCase);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Executa um teste individual
        /// </summary>
        /// <param name="testCase">Caso de teste</param>
        /// <returns>Resultado do teste</returns>
        public static SecurityTestResult RunSingleTest(SecurityTestCase testCase)
        {
            var maskedOutput = SecurityMaskingService.MaskSensitiveData(testCase.Input);
            
            var result = new SecurityTestResult
            {
                TestCaseName = testCase.Name,
                OriginalInput = testCase.Input,
                MaskedOutput = maskedOutput,
                Severity = testCase.Severity
            };

            // Verificar se o mascaramento foi aplicado corretamente
            var containsOriginalSensitiveData = SecurityMaskingService.ContainsSensitiveData(testCase.Input);
            var containsMaskedData = maskedOutput.Contains("[MASCARADO]") || 
                                  maskedOutput.Contains("[REMOVIDO]") || 
                                  maskedOutput.Contains("***");

            if (!containsOriginalSensitiveData)
            {
                result.Passed = true;
                result.Reason = "Nenhuma informação sensível detectada na entrada";
            }
            else if (containsMaskedData)
            {
                result.Passed = true;
                result.Reason = "Informação sensível mascarada com sucesso";
            }
            else
            {
                result.Passed = false;
                result.Reason = "Falha no mascaramento - informação sensível não foi protegida";
            }

            return result;
        }

        /// <summary>
        /// Valida se o mascaramento está funcionando para todos os cenários
        /// </summary>
        /// <param name="results">Resultados dos testes</param>
        /// <returns>True se todos passaram</returns>
        public static bool ValidateSecurityMasking(List<SecurityTestResult> results)
        {
            return results.All(r => r.Passed);
        }

        /// <summary>
        /// Gera relatório detalhado dos testes de segurança
        /// </summary>
        /// <param name="results">Resultados dos testes</param>
        /// <returns>Relatório formatado</returns>
        public static string GenerateTestReport(List<SecurityTestResult> results)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("=== RELATÓRIO DE TESTES DE SEGURANÇA ===");
            report.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Total de testes: {results.Count}");
            report.AppendLine($"Testes passados: {results.Count(r => r.Passed)}");
            report.AppendLine($"Testes falhados: {results.Count(r => !r.Passed)}");
            report.AppendLine($"Status geral: {(ValidateSecurityMasking(results) ? "[OK] SEGURO" : "[CRITICAL] VULNERÁVEL")}");
            report.AppendLine();

            report.AppendLine("=== RESULTADOS DETALHADOS ===");
            report.AppendLine();

            foreach (var result in results.OrderBy(r => r.Severity).ThenBy(r => r.TestCaseName))
            {
                var status = result.Passed ? "[OK] PASSOU" : "[FAIL] FALHOU";
                var severity = result.Severity switch
                {
                    SecurityAuditService.SecurityAuditSeverity.Critical => "[RED] CRÍTICO",
                    SecurityAuditService.SecurityAuditSeverity.High => "[ORANGE] ALTO",
                    SecurityAuditService.SecurityAuditSeverity.Medium => "[YELLOW] MÉDIO",
                    SecurityAuditService.SecurityAuditSeverity.Low => "[GREEN] BAIXO",
                    _ => "[WHITE] DESCONHECIDO"
                };

                report.AppendLine($"{status} | {severity} | {result.TestCaseName}");
                report.AppendLine($"   Entrada: {result.OriginalInput}");
                report.AppendLine($"   Saída:  {result.MaskedOutput}");
                report.AppendLine($"   Motivo:  {result.Reason}");
                report.AppendLine();
            }

            if (!ValidateSecurityMasking(results))
            {
                report.AppendLine("=== RECOMENDAÇÕES IMEDIATAS ===");
                report.AppendLine("[ALERT] FALHAS CRÍTICAS DETECTADAS!");
                report.AppendLine("1. Revisar implementação do SecurityMaskingService");
                report.AppendLine("2. Verificar padrões regex estão corretos");
                report.AppendLine("3. Testar mascaramento por contexto");
                report.AppendLine("4. Implementar validação em tempo de execução");
                report.AppendLine("5. Considerar remoção completa de dados sensíveis");
            }

            return report.ToString();
        }

        /// <summary>
        /// Testa cenários do mundo real com logs simulados
        /// </summary>
        /// <returns>Resultados dos testes</returns>
        public static List<SecurityTestResult> RunRealWorldTests()
        {
            var realWorldLogs = new[]
            {
                // Log real de licença
                "[2026-04-23 14:07:30.632] [Informação] [LicenseManager] Autenticação Supabase configurada com service_role key eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsInRlc3QiOiJzaW11bGFjYW8iLCJpYXQiOjE3MDAwMDAwMDAsImV4cCI6MTk5OTk5OTk5OX0.abc123def456ghi789jkl012mno345pqr678stuv901",
                
                // Log real de HWID
                "[2026-04-23 14:07:31.912] [Informação] [HWID-TRIAL] 🔑 HWID: 1b13c7beb22c60067072d6885404b96c5ec7088b58cef8e2b45fea0b0eb9abb6c6104e2b17db4cfaf3e21da8653c2a6e98483e9728e01d48c3bfc63011607dcb",
                
                // Log real de URL
                "[2026-04-23 14:07:31.912] [Informação] [HWID-TRIAL] 📡 URL: https://zamjyyzockbbugjepkhk.supabase.co/functions/v1/clever-endpoint",
                
                // Log real de assembly hash
                "[2026-04-23 14:07:31.934] [Informação] [INTEGRITY] Assembly hash: C0B52848799E058E962F014C41C5D6D7",
                
                // Log combinado com múltiplas informações sensíveis
                "[2026-04-23 14:07:32.039] [Informação] [HWID-TRIAL] 🚀 Iniciando verificação online: https://zamjyyzockbbugjepkhk.supabase.co/functions/v1/clever-endpoint 🔑 HWID: 1b13c7beb22c60067072d6885404b96c5ec7088b58cef8e2b45fea0b0eb9abb6c6104e2b17db4cfaf3e21da8653c2a6e98483e9728e01d48c3bfc63011607dcb"
            };

            var results = new List<SecurityTestResult>();

            for (int i = 0; i < realWorldLogs.Length; i++)
            {
                var log = realWorldLogs[i];
                var masked = SecurityMaskingService.MaskSensitiveData(log);
                
                var result = new SecurityTestResult
                {
                    TestCaseName = $"Real World Test #{i + 1}",
                    OriginalInput = log,
                    MaskedOutput = masked,
                    Severity = SecurityMaskingService.ContainsSensitiveData(log) ? 
                        SecurityAuditService.SecurityAuditSeverity.High : 
                        SecurityAuditService.SecurityAuditSeverity.Safe
                };

                // Verificar se todas as informações sensíveis foram mascaradas
                result.Passed = !SecurityMaskingService.ContainsSensitiveData(masked);
                result.Reason = result.Passed ? 
                    "Log real mascarado com sucesso" : 
                    "Log real ainda contém informações sensíveis";

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Valida performance do mascaramento
        /// </summary>
        /// <param name="iterations">Número de iterações</param>
        /// <returns>Tempo médio por operação</returns>
        public static double BenchmarkMaskingPerformance(int iterations = 10000)
        {
            var testMessage = "Test message with JWT eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.123.456 and HWID 1b13c7beb22c60067072d6885404b96c5ec7088b58cef8e2b45fea0b0eb9abb6c6104e2b17db4cfaf3e21da8653c2a6e98483e9728e01d48c3bfc63011607dcb";
            
            var startTime = DateTime.Now;
            
            for (int i = 0; i < iterations; i++)
            {
                SecurityMaskingService.MaskSensitiveData(testMessage);
            }
            
            var endTime = DateTime.Now;
            var totalTime = (endTime - startTime).TotalMilliseconds;
            
            return totalTime / iterations;
        }

        /// <summary>
        /// Executa validação completa de segurança
        /// </summary>
        /// <returns>Relatório completo</returns>
        public static string RunCompleteSecurityValidation()
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("=== VALIDAÇÃO COMPLETA DE SEGURANÇA ===");
            report.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // 1. Testes unitários
            report.AppendLine("1. TESTES UNITÁRIOS");
            report.AppendLine(new string('=', 50));
            var unitTestResults = RunAllSecurityTests();
            report.AppendLine(GenerateTestReport(unitTestResults));
            report.AppendLine();

            // 2. Testes do mundo real
            report.AppendLine("2. TESTES DO MUNDO REAL");
            report.AppendLine(new string('=', 50));
            var realWorldResults = RunRealWorldTests();
            report.AppendLine(GenerateTestReport(realWorldResults));
            report.AppendLine();

            // 3. Performance
            report.AppendLine("3. PERFORMANCE DO MASCARAMENTO");
            report.AppendLine(new string('=', 50));
            var avgTime = BenchmarkMaskingPerformance();
            report.AppendLine($"Tempo médio por operação: {avgTime:F3}ms");
            report.AppendLine($"Status: {(avgTime < 1.0 ? "✅ ACEITÁVEL" : "⚠️ LENTO")}");
            report.AppendLine();

            // 4. Validação final
            var allResults = new List<SecurityTestResult>();
            allResults.AddRange(unitTestResults);
            allResults.AddRange(realWorldResults);
            
            var allPassed = ValidateSecurityMasking(allResults);
            
            report.AppendLine("4. RESUMO FINAL");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"Status geral: {(allPassed ? "[OK] SISTEMA SEGURO" : "[CRITICAL] VULNERABILIDADES DETECTADAS")}");
            report.AppendLine($"Total de testes: {allResults.Count}");
            report.AppendLine($"Taxa de sucesso: {(double)allResults.Count(r => r.Passed) / allResults.Count * 100:F1}%");

            if (!allPassed)
            {
                report.AppendLine();
                report.AppendLine("[ALERT] AÇÕES RECOMENDADAS:");
                report.AppendLine("1. Corrigir falhas de mascaramento imediatamente");
                report.AppendLine("2. Implementar validação em tempo de execução");
                report.AppendLine("3. Revisar logs existentes e remover informações sensíveis");
                report.AppendLine("4. Adicionar testes automatizados ao pipeline de CI/CD");
                report.AppendLine("5. Considerar remoção completa em ambiente de produção");
            }

            return report.ToString();
        }
    }
}
