using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Enterprise.Testing
{
    /// <summary>
    /// PROJECT ARCHITECTURE AUDIT
    /// Auditor Completo de Arquitetura e Estrutura do Projeto
    /// 
    /// MISSÃO: Identificar arquivos reais, duplicados, mortos e conflitos arquiteturais
    /// </summary>
    public class ProjectArchitectureAudit
    {
        private readonly Dictionary<string, FileAnalysis> _fileAnalysis = new();
        private readonly Dictionary<string, List<string>> _duplicates = new();
        private readonly List<string> _deadCode = new();
        private readonly List<string> _dangerousFiles = new();

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== PROJECT ARCHITECTURE AUDIT ===");
            Console.WriteLine("Auditor Completo de Arquitetura e Estrutura");
            Console.WriteLine("MISSÃO: Identificar arquivos reais, duplicados, mortos e conflitos");
            Console.WriteLine();

            try
            {
                var audit = new ProjectArchitectureAudit();
                await audit.ExecuteCompleteAuditAsync();
                
                audit.GenerateFinalClassification();
                
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AUDIT ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Executar auditoria completa
        /// </summary>
        public async Task ExecuteCompleteAuditAsync()
        {
            Console.WriteLine("=== INICIANDO AUDITORIA COMPLETA DE ARQUITETURA ===");
            Console.WriteLine("Análise profunda de todos os arquivos e dependências");
            Console.WriteLine();

            // FASE 1: Mapeamento Real de Uso
            await ExecuteRealUsageMapping();

            // FASE 2: Detecção de Duplicidade
            await ExecuteDuplicationDetection();

            // FASE 3: Detecção de Código Morto
            await ExecuteDeadCodeDetection();

            // FASE 4: Conflitos Arquiteturais
            await ExecuteArchitecturalConflictsDetection();
        }

        #region FASE 1: MAPEAMENTO REAL DE USO

        private async Task ExecuteRealUsageMapping()
        {
            Console.WriteLine("=== FASE 1: MAPEAMENTO REAL DE USO ===");
            Console.WriteLine("Identificando arquivos em runtime, parciais e não utilizados");
            Console.WriteLine();

            var projectRoot = "E:\\Minhas Coisas\\Desktop\\APLICATIVO VOLTRIS";
            var allCsFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories);

            Console.WriteLine($"Analisando {allCsFiles.Length} arquivos C#...");

            foreach (var file in allCsFiles)
            {
                var relativePath = Path.GetRelativePath(projectRoot, file);
                var analysis = await AnalyzeFileUsage(file, relativePath);
                _fileAnalysis[relativePath] = analysis;

                var status = analysis.UsageStatus switch
                {
                    UsageStatus.Runtime => "USADO EM RUNTIME",
                    UsageStatus.Partial => "USADO PARCIALMENTE",
                    UsageStatus.Unused => "NÃO UTILIZADO",
                    _ => "DESCONHECIDO"
                };

                Console.Write($"\r{status}: {relativePath}");
            }
            Console.WriteLine();

            // Resumo por categoria
            var runtimeFiles = _fileAnalysis.Count(f => f.Value.UsageStatus == UsageStatus.Runtime);
            var partialFiles = _fileAnalysis.Count(f => f.Value.UsageStatus == UsageStatus.Partial);
            var unusedFiles = _fileAnalysis.Count(f => f.Value.UsageStatus == UsageStatus.Unused);

            Console.WriteLine($"\n=== RESUMO MAPEAMENTO ===");
            Console.WriteLine($"Runtime: {runtimeFiles} arquivos");
            Console.WriteLine($"Parcial: {partialFiles} arquivos");
            Console.WriteLine($"Não utilizados: {unusedFiles} arquivos");
            Console.WriteLine();
        }

        private async Task<FileAnalysis> AnalyzeFileUsage(string filePath, string relativePath)
        {
            var content = await File.ReadAllTextAsync(filePath);
            var analysis = new FileAnalysis
            {
                FilePath = relativePath,
                Content = content,
                UsageStatus = UsageStatus.Unused
            };

            // Verificar se está registrado no DI
            if (IsRegisteredInDI(content))
            {
                analysis.UsageStatus = UsageStatus.Runtime;
                analysis.DIRegistration = true;
            }

            // Verificar se é chamado por outros arquivos
            var references = CountFileReferences(relativePath);
            if (references > 0)
            {
                analysis.UsageStatus = analysis.UsageStatus == UsageStatus.Runtime ? 
                    UsageStatus.Runtime : UsageStatus.Partial;
                analysis.ReferenceCount = references;
            }

            // Verificar se é classe principal (App, MainWindow, etc.)
            if (IsMainClass(relativePath, content))
            {
                analysis.UsageStatus = UsageStatus.Runtime;
                analysis.IsMainClass = true;
            }

            // Verificar se é test
            if (relativePath.Contains("Test") || relativePath.Contains("test"))
            {
                analysis.UsageStatus = UsageStatus.Unused; // Tests não são runtime
                analysis.IsTest = true;
            }

            return analysis;
        }

        private bool IsRegisteredInDI(string content)
        {
            return content.Contains("AddSingleton") || 
                   content.Contains("AddTransient") || 
                   content.Contains("AddScoped") ||
                   content.Contains("services.Add") ||
                   content.Contains("ConfigureServices");
        }

        private int CountFileReferences(string relativePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            var className = Path.GetFileName(relativePath).Replace(".cs", "");
            
            // Contar referências em outros arquivos
            var references = 0;
            foreach (var file in _fileAnalysis.Keys.Where(f => f != relativePath))
            {
                var content = _fileAnalysis[file].Content;
                if (content.Contains(className) || content.Contains(fileName))
                {
                    references++;
                }
            }
            return references;
        }

        private bool IsMainClass(string relativePath, string content)
        {
            return relativePath.Contains("App.xaml") ||
                   relativePath.Contains("MainWindow") ||
                   relativePath.Contains("Bootstrapper") ||
                   content.Contains("public partial class App") ||
                   content.Contains("public partial class MainWindow");
        }

        #endregion

        #region FASE 2: DETECÇÃO DE DUPLICIDADE

        private async Task ExecuteDuplicationDetection()
        {
            Console.WriteLine("=== FASE 2: DETECÇÃO DE DUPLICIDADE ===");
            Console.WriteLine("Identificando serviços com mesma responsabilidade");
            Console.WriteLine();

            // Detectar múltiplos Orchestrators
            await DetectOrchestratorDuplicates();

            // Detectar serviços duplicados
            await DetectServiceDuplicates();

            // Detectar classes semelhantes
            await DetectSimilarClasses();
        }

        private async Task DetectOrchestratorDuplicates()
        {
            Console.WriteLine("Detectando múltiplos Orchestrators...");

            var orchestrators = _fileAnalysis
                .Where(f => f.Key.Contains("Orchestrator") && !f.Key.Contains("Test"))
                .ToList();

            Console.WriteLine($"\nEncontrados {orchestrators.Count} Orchestrators:");

            foreach (var orchestrator in orchestrators)
            {
                var analysis = orchestrator.Value;
                Console.WriteLine($"  {orchestrator.Key}");
                Console.WriteLine($"    Status: {analysis.UsageStatus}");
                Console.WriteLine($"    DI: {analysis.DIRegistration}");
                Console.WriteLine($"    Referências: {analysis.ReferenceCount}");
                
                if (analysis.UsageStatus == UsageStatus.Runtime)
                {
                    _dangerousFiles.Add(orchestrator.Key);
                }
            }

            if (orchestrators.Count > 3)
            {
                _duplicates["Orchestrators"] = orchestrators.Select(o => o.Key).ToList();
            }
        }

        private async Task DetectServiceDuplicates()
        {
            Console.WriteLine("Detectando serviços duplicados...");

            var serviceGroups = _fileAnalysis
                .Where(f => f.Key.Contains("Service") && !f.Key.Contains("Test"))
                .GroupBy(f => ExtractServiceName(f.Key))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in serviceGroups)
            {
                Console.WriteLine($"\nServiço duplicado: {group.Key}");
                foreach (var file in group)
                {
                    Console.WriteLine($"  {file.Key}");
                }
                _duplicates[group.Key] = group.Select(f => f.Key).ToList();
            }
        }

        private async Task DetectSimilarClasses()
        {
            Console.WriteLine("Detectando classes semelhantes...");

            // Detectar classes com nomes similares
            var classGroups = _fileAnalysis
                .Where(f => !f.Key.Contains("Test"))
                .GroupBy(f => ExtractBaseClassName(f.Key))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in classGroups)
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    Console.WriteLine($"\nClasse similar: {group.Key}");
                    foreach (var file in group)
                    {
                        Console.WriteLine($"  {file.Key}");
                    }
                    _duplicates[group.Key] = group.Select(f => f.Key).ToList();
                }
            }
        }

        private string ExtractServiceName(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.EndsWith("Service")) return fileName;
            if (fileName.Contains("Service")) return fileName.Split("Service")[0] + "Service";
            return fileName;
        }

        private string ExtractBaseClassName(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // Remover sufixos comuns
            var suffixes = new[] { "Service", "Manager", "Controller", "Handler", "Provider", "Engine" };
            foreach (var suffix in suffixes)
            {
                if (fileName.EndsWith(suffix))
                {
                    return fileName.Replace(suffix, "");
                }
            }
            
            return fileName;
        }

        #endregion

        #region FASE 3: DETECÇÃO DE CÓDIGO MORTO

        private async Task ExecuteDeadCodeDetection()
        {
            Console.WriteLine("=== FASE 3: DETECÇÃO DE CÓDIGO MORTO ===");
            Console.WriteLine("Identificando arquivos nunca referenciados");
            Console.WriteLine();

            var deadFiles = _fileAnalysis
                .Where(f => f.Value.UsageStatus == UsageStatus.Unused && 
                           !f.Value.IsTest &&
                           !f.Key.Contains("Temporary") &&
                           !f.Key.Contains("Backup"))
                .ToList();

            Console.WriteLine($"Encontrados {deadFiles.Count} arquivos potencialmente mortos:");

            foreach (var dead in deadFiles)
            {
                Console.WriteLine($"  {dead.Key}");
                _deadCode.Add(dead.Key);
            }

            // Verificar métodos não utilizados
            await DetectUnusedMethods();
        }

        private async Task DetectUnusedMethods()
        {
            Console.WriteLine("\nDetectando métodos não utilizados...");

            foreach (var file in _fileAnalysis.Where(f => f.Value.UsageStatus == UsageStatus.Runtime))
            {
                var content = file.Value.Content;
                var methods = ExtractMethods(content);
                
                foreach (var method in methods)
                {
                    var references = CountMethodReferences(method, file.Key);
                    if (references == 0 && !IsSpecialMethod(method))
                    {
                        Console.WriteLine($"  Método não utilizado: {method} em {file.Key}");
                    }
                }
            }
        }

        private List<string> ExtractMethods(string content)
        {
            var methods = new List<string>();
            var lines = content.Split('\n');
            
            foreach (var line in lines)
            {
                if (line.Contains("public ") && line.Contains("(") && line.Contains(")"))
                {
                    var method = line.Trim();
                    if (method.Contains(" void ") || method.Contains(" Task ") || method.Contains(" async "))
                    {
                        methods.Add(method);
                    }
                }
            }
            
            return methods;
        }

        private int CountMethodReferences(string method, string filePath)
        {
            var methodName = ExtractMethodName(method);
            var references = 0;
            
            foreach (var file in _fileAnalysis.Where(f => f.Key != filePath))
            {
                if (file.Value.Content.Contains(methodName))
                {
                    references++;
                }
            }
            
            return references;
        }

        private string ExtractMethodName(string methodDeclaration)
        {
            var parts = methodDeclaration.Split(' ');
            var methodIndex = Array.IndexOf(parts, "void") >= 0 ? 
                Array.IndexOf(parts, "void") + 1 : 
                Array.IndexOf(parts, "Task") >= 0 ? 
                Array.IndexOf(parts, "Task") + 1 : -1;
            
            if (methodIndex >= 0 && methodIndex < parts.Length)
            {
                var methodName = parts[methodIndex];
                return methodName.Split('(')[0];
            }
            
            return "";
        }

        private bool IsSpecialMethod(string method)
        {
            return method.Contains("Main(") ||
                   method.Contains("OnStartup") ||
                   method.Contains("OnExit") ||
                   method.Contains("Initialize") ||
                   method.Contains("Dispose") ||
                   method.Contains("constructor");
        }

        #endregion

        #region FASE 4: CONFLITOS ARQUITETURAIS

        private async Task ExecuteArchitecturalConflictsDetection()
        {
            Console.WriteLine("=== FASE 4: CONFLITOS ARQUITETURAIS ===");
            Console.WriteLine("Identificando múltiplos sistemas coexistindo");
            Console.WriteLine();

            // Detectar múltiplos sistemas de IA
            await DetectMultipleIASystems();

            // Detectar serviços competindo
            await DetectCompetingServices();

            // Detectar loops paralelos
            await DetectParallelLoops();
        }

        private async Task DetectMultipleIASystems()
        {
            Console.WriteLine("Detectando múltiplos sistemas de IA...");

            var iaSystems = _fileAnalysis
                .Where(f => f.Key.Contains("Intelligence") || 
                           f.Key.Contains("AI") || 
                           f.Key.Contains("Neural") ||
                           f.Key.Contains("Optimization"))
                .Where(f => f.Value.UsageStatus == UsageStatus.Runtime)
                .ToList();

            Console.WriteLine($"\nEncontrados {iaSystems.Count} sistemas de IA ativos:");

            foreach (var ia in iaSystems)
            {
                Console.WriteLine($"  {ia.Key}");
                if (ia.Key.Contains("Orchestrator"))
                {
                    _dangerousFiles.Add(ia.Key);
                }
            }

            if (iaSystems.Count > 5)
            {
                Console.WriteLine("  ALERTA: Muitos sistemas de IA podem causar conflitos!");
            }
        }

        private async Task DetectCompetingServices()
        {
            Console.WriteLine("Detectando serviços competindo...");

            var competingServices = new Dictionary<string, List<string>>();

            // Detectar múltiplos otimizadores
            var optimizers = _fileAnalysis
                .Where(f => f.Key.Contains("Optimizer") && f.Value.UsageStatus == UsageStatus.Runtime)
                .ToList();

            if (optimizers.Count > 3)
            {
                competingServices["Optimizers"] = optimizers.Select(o => o.Key).ToList();
                Console.WriteLine($"  ALERTA: {optimizers.Count} otimizadores ativos podem competir!");
            }

            // Detectar múltiplos profilers
            var profilers = _fileAnalysis
                .Where(f => f.Key.Contains("Profiler") && f.Value.UsageStatus == UsageStatus.Runtime)
                .ToList();

            if (profilers.Count > 2)
            {
                competingServices["Profilers"] = profilers.Select(p => p.Key).ToList();
                Console.WriteLine($"  ALERTA: {profilers.Count} profilers ativos podem causar overhead!");
            }
        }

        private async Task DetectParallelLoops()
        {
            Console.WriteLine("Detectando loops paralelos...");

            var loopFiles = _fileAnalysis
                .Where(f => f.Value.Content.Contains("while (true)") ||
                           f.Value.Content.Contains("for (;;)") ||
                           f.Value.Content.Contains("Task.Run") ||
                           f.Value.Content.Contains("async Task"))
                .Where(f => f.Value.UsageStatus == UsageStatus.Runtime)
                .ToList();

            Console.WriteLine($"\nEncontrados {loopFiles.Count} arquivos com loops/tasks paralelos:");

            foreach (var loop in loopFiles)
            {
                Console.WriteLine($"  {loop.Key}");
                if (loop.Key.Contains("Orchestrator") || loop.Key.Contains("Service"))
                {
                    _dangerousFiles.Add(loop.Key);
                }
            }
        }

        #endregion

        #region CLASSIFICAÇÃO FINAL

        private void GenerateFinalClassification()
        {
            Console.WriteLine();
            Console.WriteLine("=== CLASSIFICAÇÃO FINAL ===");
            Console.WriteLine("Organização completa do projeto");
            Console.WriteLine();

            // ARQUIVOS PRINCIPAIS (CORE DO SISTEMA)
            Console.WriteLine("=== ARQUIVOS PRINCIPAIS (CORE DO SISTEMA) ===");
            var coreFiles = _fileAnalysis
                .Where(f => f.Value.UsageStatus == UsageStatus.Runtime && 
                           f.Value.IsMainClass)
                .OrderBy(f => f.Key)
                .ToList();

            foreach (var file in coreFiles)
            {
                Console.WriteLine($"  {file.Key}");
            }

            // ARQUIVOS DUPLICADOS
            Console.WriteLine("\n=== ARQUIVOS DUPLICADOS ===");
            foreach (var duplicate in _duplicates)
            {
                Console.WriteLine($"\n{duplicate.Key}:");
                foreach (var file in duplicate.Value)
                {
                    var status = _fileAnalysis[file].UsageStatus;
                    var recommendation = status == UsageStatus.Runtime ? 
                        "MANTER" : "REMOVER";
                    Console.WriteLine($"  {file} - {recommendation}");
                }
            }

            // ARQUIVOS INÚTEIS / NÃO UTILIZADOS
            Console.WriteLine("\n=== ARQUIVOS INÚTEIS / NÃO UTILIZADOS ===");
            var unusedFiles = _deadCode.OrderBy(f => f).ToList();
            Console.WriteLine($"Total: {unusedFiles.Count} arquivos");
            
            foreach (var file in unusedFiles.Take(20)) // Limitar output
            {
                Console.WriteLine($"  {file}");
            }
            
            if (unusedFiles.Count > 20)
            {
                Console.WriteLine($"  ... e mais {unusedFiles.Count - 20} arquivos");
            }

            // ARQUIVOS PERIGOSOS
            Console.WriteLine("\n=== ARQUIVOS PERIGOSOS ===");
            Console.WriteLine("Podem causar bugs, conflitos ou consumo excessivo:");
            
            foreach (var dangerous in _dangerousFiles.Distinct().OrderBy(f => f))
            {
                Console.WriteLine($"  {dangerous}");
            }

            // ESTATÍSTICAS FINAIS
            Console.WriteLine("\n=== ESTATÍSTICAS FINAIS ===");
            var totalFiles = _fileAnalysis.Count;
            var runtimeFiles = _fileAnalysis.Count(f => f.Value.UsageStatus == UsageStatus.Runtime);
            var unusedFilesCount = _fileAnalysis.Count(f => f.Value.UsageStatus == UsageStatus.Unused);
            var duplicateGroups = _duplicates.Count;
            var dangerousCount = _dangerousFiles.Distinct().Count();

            Console.WriteLine($"Total de arquivos: {totalFiles}");
            Console.WriteLine($"Arquivos em runtime: {runtimeFiles} ({(double)runtimeFiles/totalFiles*100:F1}%)");
            Console.WriteLine($"Arquivos não utilizados: {unusedFilesCount} ({(double)unusedFilesCount/totalFiles*100:F1}%)");
            Console.WriteLine($"Grupos duplicados: {duplicateGroups}");
            Console.WriteLine($"Arquivos perigosos: {dangerousCount}");

            // RECOMENDAÇÕES
            Console.WriteLine("\n=== RECOMENDAÇÕES ===");
            Console.WriteLine("1. Remover arquivos não utilizados para reduzir complexidade");
            Console.WriteLine("2. Consolidar serviços duplicados para evitar conflitos");
            Console.WriteLine("3. Revisar arquivos perigosos que podem causar bugs");
            Console.WriteLine("4. Manter apenas os Orchestrators realmente necessários");
            Console.WriteLine("5. Documentar o fluxo principal do sistema");
        }

        #endregion
    }

    #region Classes de Apoio

    public class FileAnalysis
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public UsageStatus UsageStatus { get; set; }
        public bool DIRegistration { get; set; }
        public int ReferenceCount { get; set; }
        public bool IsMainClass { get; set; }
        public bool IsTest { get; set; }
    }

    public enum UsageStatus
    {
        Runtime,
        Partial,
        Unused
    }

    #endregion
}
