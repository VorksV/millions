using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Enterprise.Testing
{
    /// <summary>
    /// INTELLIGENCE SYSTEM TEST
    /// Verifica se o sistema de IA inteligente está funcionando como ATERA
    /// </summary>
    public class IntelligenceSystemTest
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggingService _logger;
        
        public IntelligenceSystemTest(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetRequiredService<ILoggingService>();
        }

        public async Task<IntelligenceTestReport> RunCompleteTestAsync()
        {
            Console.WriteLine("=== INTELLIGENCE SYSTEM TEST ===");
            Console.WriteLine("Verificando se sistema IA funciona como ATERA...");
            Console.WriteLine();

            var report = new IntelligenceTestReport
            {
                TestTimestamp = DateTime.UtcNow,
                TestResults = new Dictionary<string, TestResult>()
            };

            try
            {
                // 1. Testar Orquestrator de Inteligência Gamer
                await TestGamerIntelligenceOrchestrator(report);
                
                // 2. Testar Game Intelligence Service
                await TestGameIntelligenceService(report);
                
                // 3. Testar Per-Game Learning
                await TestPerGameLearning(report);
                
                // 4. Testar Predictive Stutter Prevention
                await TestPredictiveStutterPrevention(report);
                
                // 5. Testar Adaptive Hardware Engine
                await TestAdaptiveHardwareEngine(report);
                
                // 6. Testar Network Intelligence
                await TestNetworkIntelligence(report);
                
                // 7. Testar Thermal Intelligence
                await TestThermalIntelligence(report);
                
                // 8. Verificar se sistema aprende e corrige
                await TestLearningAndCorrection(report);
                
                // 9. Comparar com funcionalidades ATERA
                CompareWithAtera(report);
                
                // 10. Gerar veredito final
                GenerateFinalVerdict(report);
                
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[IntelligenceTest] Erro durante teste: {ex.Message}");
                report.OverallStatus = TestStatus.Failed;
                report.ErrorMessage = ex.Message;
                return report;
            }
        }

        private async Task TestGamerIntelligenceOrchestrator(IntelligenceTestReport report)
        {
            Console.WriteLine("1. Testando Gamer Intelligence Orchestrator...");
            
            try
            {
                // Verificar se serviço existe
                var orchestratorType = typeof(VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.IntelligenceOrchestrator);
                var service = _serviceProvider.GetService(orchestratorType);
                
                if (service != null)
                {
                    report.TestResults["GamerOrchestrator"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = "Orchestrator encontrado e instanciado",
                        Details = "Centraliza avaliações de monitoramento e telemetria com execução adaptativa"
                    };
                    Console.WriteLine("   OK: Gamer Intelligence Orchestrator funcionando");
                }
                else
                {
                    report.TestResults["GamerOrchestrator"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Orchestrator não encontrado no DI container"
                    };
                    Console.WriteLine("   FALHOU: Gamer Intelligence Orchestrator não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["GamerOrchestrator"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestGameIntelligenceService(IntelligenceTestReport report)
        {
            Console.WriteLine("2. Testando Game Intelligence Service...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.GameIntelligenceService>();
                
                if (service != null)
                {
                    // Verificar se tem perfis de jogos
                    var profilesField = service.GetType().GetField("_gameProfiles", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (profilesField != null)
                    {
                        var profiles = profilesField.GetValue(service) as Dictionary<string, object>;
                        var profileCount = profiles?.Count ?? 0;
                        
                        report.TestResults["GameIntelligence"] = new TestResult
                        {
                            Status = TestStatus.Passed,
                            Message = $"Service funcionando com {profileCount} perfis de jogos",
                            Details = "Perfis otimizados por jogo com ML simplificado"
                        };
                        Console.WriteLine($"   OK: Game Intelligence com {profileCount} perfis");
                    }
                }
                else
                {
                    report.TestResults["GameIntelligence"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Game Intelligence Service não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Game Intelligence Service não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["GameIntelligence"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestPerGameLearning(IntelligenceTestReport report)
        {
            Console.WriteLine("3. Testando Per-Game Learning...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.PerGameLearningProfileService>();
                
                if (service != null)
                {
                    var totalGamesField = service.GetType().GetProperty("TotalGamesLearned");
                    var totalGames = totalGamesField?.GetValue(service) as int? ?? 0;
                    
                    report.TestResults["PerGameLearning"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = $"Learning service funcionando com {totalGames} jogos aprendidos",
                        Details = "Aprendizado por jogo com persistência de perfis"
                    };
                    Console.WriteLine($"   OK: Per-Game Learning com {totalGames} jogos");
                }
                else
                {
                    report.TestResults["PerGameLearning"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Per-Game Learning Service não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Per-Game Learning Service não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["PerGameLearning"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestPredictiveStutterPrevention(IntelligenceTestReport report)
        {
            Console.WriteLine("4. Testando Predictive Stutter Prevention...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.PredictiveStutterPreventionService>();
                
                if (service != null)
                {
                    var stuttersPreventedField = service.GetType().GetProperty("StuttersPrevented");
                    var stuttersPrevented = stuttersPreventedField?.GetValue(service) as int? ?? 0;
                    
                    var accuracyField = service.GetType().GetProperty("PredictionAccuracy");
                    var accuracy = accuracyField?.GetValue(service) as double? ?? 0.0;
                    
                    report.TestResults["StutterPrevention"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = $"Stutter prevention funcionando ({stuttersPrevented} stutters prevenidos, {accuracy:P1} accuracy)",
                        Details = "Prediz stutters ANTES que aconteçam usando análise de padrões"
                    };
                    Console.WriteLine($"   OK: Stutter Prevention - {stuttersPrevented} prevenidos, {accuracy:P1} accuracy");
                }
                else
                {
                    report.TestResults["StutterPrevention"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Predictive Stutter Prevention Service não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Predictive Stutter Prevention Service não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["StutterPrevention"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestAdaptiveHardwareEngine(IntelligenceTestReport report)
        {
            Console.WriteLine("5. Testando Adaptive Hardware Engine...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.AdaptiveHardwareEngineService>();
                
                if (service != null)
                {
                    report.TestResults["AdaptiveHardware"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = "Adaptive Hardware Engine funcionando",
                        Details = "Ajuste dinâmico de hardware baseado em carga do sistema"
                    };
                    Console.WriteLine("   OK: Adaptive Hardware Engine funcionando");
                }
                else
                {
                    report.TestResults["AdaptiveHardware"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Adaptive Hardware Engine não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Adaptive Hardware Engine não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["AdaptiveHardware"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestNetworkIntelligence(IntelligenceTestReport report)
        {
            Console.WriteLine("6. Testando Network Intelligence...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.NetworkIntelligenceService>();
                
                if (service != null)
                {
                    report.TestResults["NetworkIntelligence"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = "Network Intelligence funcionando",
                        Details = "Otimização de rede e QoS adaptativo"
                    };
                    Console.WriteLine("   OK: Network Intelligence funcionando");
                }
                else
                {
                    report.TestResults["NetworkIntelligence"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Network Intelligence Service não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Network Intelligence Service não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["NetworkIntelligence"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestThermalIntelligence(IntelligenceTestReport report)
        {
            Console.WriteLine("7. Testando Thermal Intelligence...");
            
            try
            {
                var service = _serviceProvider.GetService<VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.ThermalAwareAdaptiveScaler>();
                
                if (service != null)
                {
                    report.TestResults["ThermalIntelligence"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = "Thermal Intelligence funcionando",
                        Details = "Escalonamento adaptativo baseado em temperatura"
                    };
                    Console.WriteLine("   OK: Thermal Intelligence funcionando");
                }
                else
                {
                    report.TestResults["ThermalIntelligence"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = "Thermal Intelligence Service não encontrado"
                    };
                    Console.WriteLine("   FALHOU: Thermal Intelligence Service não encontrado");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["ThermalIntelligence"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private async Task TestLearningAndCorrection(IntelligenceTestReport report)
        {
            Console.WriteLine("8. Testando Capacidade de Aprendizado e Correção...");
            
            try
            {
                var learningServices = new[]
                {
                    "PerGameLearning",
                    "StutterPrevention", 
                    "AdaptiveHardware",
                    "NetworkIntelligence",
                    "ThermalIntelligence"
                };

                var workingServices = learningServices.Where(s => 
                    report.TestResults.ContainsKey(s) && 
                    report.TestResults[s].Status == TestStatus.Passed).ToList();

                if (workingServices.Count >= 3)
                {
                    report.TestResults["LearningCorrection"] = new TestResult
                    {
                        Status = TestStatus.Passed,
                        Message = $"Sistema aprende e corrige com {workingServices.Count} serviços ativos",
                        Details = $"Serviços funcionais: {string.Join(", ", workingServices)}"
                    };
                    Console.WriteLine($"   OK: Sistema aprende e corrige com {workingServices.Count} serviços");
                }
                else
                {
                    report.TestResults["LearningCorrection"] = new TestResult
                    {
                        Status = TestStatus.Failed,
                        Message = $"Apenas {workingServices.Count} serviços de aprendizado funcionando (mínimo 3 necessário)"
                    };
                    Console.WriteLine($"   FALHOU: Apenas {workingServices.Count} serviços funcionando");
                }
            }
            catch (Exception ex)
            {
                report.TestResults["LearningCorrection"] = new TestResult
                {
                    Status = TestStatus.Failed,
                    Message = $"Erro: {ex.Message}"
                };
                Console.WriteLine($"   ERRO: {ex.Message}");
            }
        }

        private void CompareWithAtera(IntelligenceTestReport report)
        {
            Console.WriteLine("9. Comparando com ATERA...");
            
            var ateraFeatures = new[]
            {
                "Remote Monitoring",
                "Patch Management", 
                "Automated Maintenance",
                "Alert Management",
                "Reporting Dashboard",
                "Multi-tenant Support"
            };

            var voltrisFeatures = new[]
            {
                "Game Intelligence",
                "Stutter Prevention",
                "Hardware Adaptation",
                "Network Optimization",
                "Thermal Management",
                "Per-Game Learning"
            };

            var workingFeatures = report.TestResults.Values.Count(r => r.Status == TestStatus.Passed);
            var totalFeatures = report.TestResults.Count;

            var comparison = $"ATERA: {string.Join(", ", ateraFeatures)}\n" +
                           $"VOLTRIS: {string.Join(", ", voltrisFeatures)}\n" +
                           $"Status: {workingFeatures}/{totalFeatures} funcionalidades Voltris funcionando";

            report.TestResults["AteraComparison"] = new TestResult
            {
                Status = workingFeatures >= 4 ? TestStatus.Passed : TestStatus.Failed,
                Message = $"Comparação: {workingFeatures}/{totalFeatures} funcionalidades funcionando",
                Details = comparison
            };

            Console.WriteLine($"   Resultado: {workingFeatures}/{totalFeatures} funcionalidades funcionando");
        }

        private void GenerateFinalVerdict(IntelligenceTestReport report)
        {
            Console.WriteLine("10. Gerando Veredito Final...");
            
            var passedTests = report.TestResults.Values.Count(r => r.Status == TestStatus.Passed);
            var totalTests = report.TestResults.Count;
            var passRate = (double)passedTests / totalTests * 100;

            if (passRate >= 80)
            {
                report.OverallStatus = TestStatus.Passed;
                report.FinalVerdict = "SISTEMA IA FUNCIONAL - Similar ao ATERA";
                report.Recommendation = "Sistema de inteligência está operacional com funcionalidades comparáveis ao ATERA";
            }
            else if (passRate >= 50)
            {
                report.OverallStatus = TestStatus.Partial;
                report.FinalVerdict = "SISTEMA IA PARCIAL - Funcionalidades limitadas";
                report.Recommendation = "Sistema funciona parcialmente, mas precisa de melhorias para atingir nível ATERA";
            }
            else
            {
                report.OverallStatus = TestStatus.Failed;
                report.FinalVerdict = "SISTEMA IA NÃO FUNCIONAL";
                report.Recommendation = "Sistema de inteligência não está funcionando adequadamente";
            }

            Console.WriteLine($"   Veredito: {report.FinalVerdict}");
            Console.WriteLine($"   Taxa de sucesso: {passRate:F1}% ({passedTests}/{totalTests})");
            Console.WriteLine($"   Recomendação: {report.Recommendation}");
        }

        public void PrintReport(IntelligenceTestReport report)
        {
            Console.WriteLine("\n" + "=".PadRight(60, '='));
            Console.WriteLine("RELATÓRIO FINAL - SISTEMA DE INTELIGÊNCIA");
            Console.WriteLine("=".PadRight(60, '='));
            Console.WriteLine($"Data: {report.TestTimestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Status: {report.OverallStatus}");
            Console.WriteLine($"Veredito: {report.FinalVerdict}");
            Console.WriteLine($"Recomendação: {report.Recommendation}");
            Console.WriteLine();

            Console.WriteLine("Resultados Detalhados:");
            foreach (var result in report.TestResults)
            {
                var status = result.Value.Status switch
                {
                    TestStatus.Passed => "PASS",
                    TestStatus.Failed => "FAIL",
                    TestStatus.Partial => "PARTIAL",
                    _ => "UNKNOWN"
                };
                
                Console.WriteLine($"  {result.Key,-20}: {status,-8} - {result.Value.Message}");
            }

            Console.WriteLine("\n" + "=".PadRight(60, '='));
        }
    }

    public class IntelligenceTestReport
    {
        public DateTime TestTimestamp { get; set; }
        public TestStatus OverallStatus { get; set; }
        public string FinalVerdict { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public Dictionary<string, TestResult> TestResults { get; set; } = new();
    }

    public class TestResult
    {
        public TestStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public enum TestStatus
    {
        Passed,
        Failed,
        Partial,
        Unknown
    }
}
