using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Services.Enterprise.Testing
{
    /// <summary>
    /// DEPENDENCY MAPPER
    /// Mapeia TODAS as dependências reais entre arquivos
    /// </summary>
    public class DependencyMapper
    {
        private readonly Dictionary<string, List<string>> _dependencies = new();
        private readonly Dictionary<string, List<string>> _reverseDependencies = new();
        
        public void MapAllDependencies(string rootPath)
        {
            Console.WriteLine("=== MAPEANDO DEPENDÊNCIAS REAIS ===");
            
            // 1. Encontrar todos os arquivos .cs
            var csFiles = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                .ToArray();
            
            Console.WriteLine($"Encontrados {csFiles.Length} arquivos C#");
            
            // 2. Para cada arquivo, analisar dependências
            foreach (var file in csFiles)
            {
                AnalyzeFileDependencies(file);
            }
            
            // 3. Gerar relatório
            GenerateDependencyReport();
        }
        
        private void AnalyzeFileDependencies(string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var fileName = Path.GetFileName(filePath);
                var dependencies = new List<string>();
                
                // Detectar using statements
                var usingPattern = @"using\s+([\w\.]+);";
                var usingMatches = Regex.Matches(content, usingPattern);
                
                foreach (Match match in usingMatches)
                {
                    var namespaceName = match.Groups[1].Value;
                    if (namespaceName.Contains("VoltrisOptimizer"))
                    {
                        dependencies.Add($"USING:{namespaceName}");
                    }
                }
                
                // Detectar referências de classes
                var classPattern = @"(?:new\s+|:\s+|<\s*|,\s*|(\s+)\s*)([A-Z][a-zA-Z0-9_]+)";
                var classMatches = Regex.Matches(content, classPattern);
                
                foreach (Match match in classMatches)
                {
                    var className = match.Groups[2].Value;
                    if (IsKnownClass(className) && !dependencies.Contains($"CLASS:{className}"))
                    {
                        dependencies.Add($"CLASS:{className}");
                    }
                }
                
                // Detectar chamadas de método
                var methodPattern = @"\.([a-zA-Z_][a-zA-Z0-9_]*)\(";
                var methodMatches = Regex.Matches(content, methodPattern);
                
                foreach (Match match in methodMatches)
                {
                    var methodName = match.Groups[1].Value;
                    if (IsKnownMethod(methodName) && !dependencies.Contains($"METHOD:{methodName}"))
                    {
                        dependencies.Add($"METHOD:{methodName}");
                    }
                }
                
                _dependencies[fileName] = dependencies;
                
                // Mapear dependências reversas
                foreach (var dep in dependencies)
                {
                    if (!_reverseDependencies.ContainsKey(dep))
                    {
                        _reverseDependencies[dep] = new List<string>();
                    }
                    _reverseDependencies[dep].Add(fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro analisando {filePath}: {ex.Message}");
            }
        }
        
        private bool IsKnownClass(string className)
        {
            var knownClasses = new[]
            {
                "IntelligenceOrchestrator", "RealDiagnosticEngine", "SpecificActionEngine",
                "ConsolidatedIntelligenceOrchestrator", "RealIntelligenceOrchestrator",
                "GamerModeOrchestrator", "PerformanceOrchestrator",
                "RuleBasedDecisionEngine", "ETWTelemetryCollector",
                "RealTimeMetricsProcessor", "PredictiveAnalysisEngine"
            };
            
            return knownClasses.Contains(className);
        }
        
        private bool IsKnownMethod(string methodName)
        {
            var knownMethods = new[]
            {
                "StartAsync", "Stop", "ApplySpecificActionAsync", "DetectRealProblem",
                "CaptureRealMetrics", "GetRealTimeMetrics", "ApplyOptimizationAsync"
            };
            
            return knownMethods.Contains(methodName);
        }
        
        public void GenerateDependencyReport()
        {
            Console.WriteLine("\n=== RELATÓRIO DE DEPENDÊNCIAS ===");
            
            // Arquivos com mais dependências
            var mostDependent = _dependencies.OrderByDescending(kvp => kvp.Value.Count).Take(10);
            
            Console.WriteLine("\nTop 10 arquivos com mais dependências:");
            foreach (var kvp in mostDependent)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value.Count} dependências");
                foreach (var dep in kvp.Value.Take(5))
                {
                    Console.WriteLine($"  - {dep}");
                }
                if (kvp.Value.Count > 5)
                {
                    Console.WriteLine($"  ... e mais {kvp.Value.Count - 5}");
                }
            }
            
            // Classes mais referenciadas
            var mostReferenced = _reverseDependencies.Where(kvp => kvp.Key.StartsWith("CLASS:"))
                .OrderByDescending(kvp => kvp.Value.Count).Take(10);
            
            Console.WriteLine("\nTop 10 classes mais referenciadas:");
            foreach (var kvp in mostReferenced)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value.Count} referências");
                foreach (var refFile in kvp.Value.Take(3))
                {
                    Console.WriteLine($"  - {refFile}");
                }
                if (kvp.Value.Count > 3)
                {
                    Console.WriteLine($"  ... e mais {kvp.Value.Count - 3}");
                }
            }
            
            // Identificar arquivos sem dependências (candidatos para remoção)
            var noDependencies = _dependencies.Where(kvp => kvp.Value.Count == 0).ToList();
            
            Console.WriteLine($"\nArquivos sem dependências ({noDependencies.Count}):");
            foreach (var kvp in noDependencies)
            {
                Console.WriteLine($"  - {kvp.Key}");
            }
        }
        
        public List<string> GetSafeToRemoveFiles()
        {
            var safeFiles = new List<string>();
            
            foreach (var kvp in _dependencies)
            {
                var fileName = kvp.Key;
                var dependencies = kvp.Value;
                
                // Verificar se arquivo não é referenciado por outros
                var isReferenced = _reverseDependencies.Any(rd => 
                    rd.Value.Contains(fileName) && !rd.Key.StartsWith("USING:"));
                
                // Verificar se não tem dependências críticas
                var hasCriticalDependencies = dependencies.Any(dep => 
                    dep.Contains("IntelligenceOrchestrator") || 
                    dep.Contains("ConsolidatedIntelligenceOrchestrator") ||
                    dep.Contains("RealDiagnosticEngine"));
                
                if (!isReferenced && !hasCriticalDependencies)
                {
                    safeFiles.Add(fileName);
                }
            }
            
            return safeFiles;
        }
    }
}
