using System;

using System.Collections.Generic;

using System.Linq;

using System.Text.RegularExpressions;

using System.Threading.Tasks;

using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Assistant 
{
/// <summary>
/// Processador de Linguagem Natural para comandos do assistente
/// Implementa processamento real de texto e extração de intenções
/// </summary>
public class NLPProcessor
 {
private readonly ILoggingService _logger;

// Dicion�rios de intenções e padrões
    private Dictionary<CommandIntent, List<string>> _intentPatterns;

 private Dictionary < string, CommandIntent > _keywordMapping;

 private List < string > _stopWords;

 public NLPProcessor(ILoggingService logger) {
_logger = logger??throw new ArgumentNullException(nameof(logger));

 InitializePatterns();

 _logger.LogInfo("[NLPProcessor]Processador de linguagem natural inicializado");

 }
/// <summary>
    /// Processa entrada de texto em linguagem natural
    /// </summary>
    public async Task<ProcessedInput> ProcessInputAsync(string input) {
_logger.LogInfo($"[NLPProcessor]Processando entrada: '{input}'");

 try {
var processedInput = new ProcessedInput {
OriginalText = input, NormalizedText = NormalizeText(input)};

// 1. Extração de intenções
        processedInput.Intent = ExtractIntent(processedInput.NormalizedText);

 processedInput.Confidence = CalculateIntentConfidence(processedInput.NormalizedText, processedInput.Intent);

// 2. Extração de entidades
        processedInput.Entities = ExtractEntities(processedInput.NormalizedText);

// 3. Extração de parâmetros
        processedInput.Parameters = ExtractParameters(processedInput.NormalizedText, processedInput.Entities);

 _logger.LogInfo($"[NLPProcessor]Processado: Intent = {processedInput.Intent}, Confiança = {processedInput.Confidence:P1}");

 return processedInput;

 }
 catch(Exception ex) {
_logger.LogError("[NLPProcessor]Erro ao processar entrada", ex);

 return new ProcessedInput {
OriginalText = input, NormalizedText = input, Intent = CommandIntent.Unknown, Confidence = 0};

 }
 }
 #region Processamento de Texto
    private string NormalizeText(string text) {
if(string.IsNullOrEmpty(text))return text;

// Converter para min�sculas
        text = text.ToLowerInvariant();

// Remover pontuação excessiva
        text = Regex.Replace(text, @"[^\w\s]", "");

// Remover espaços múltiplos
        text = Regex.Replace(text, @"\s+", "").Trim();

// Remover stop words
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

 var filteredWords = words.Where(word =>!_stopWords.Contains(word));

 return string.Join(" ", filteredWords);

 }
 private CommandIntent ExtractIntent(string normalizedText) {
var scores = new Dictionary < CommandIntent, double > ();

// Calcular scores para cada intenção
        foreach (var intent in _intentPatterns.Keys) {
scores[intent] = CalculateIntentScore(normalizedText, intent);

 }
// Encontrar a intenção com maior score
        if (scores.Any()) {
var bestIntent = scores.OrderByDescending(kvp => kvp.Value).First();

// Threshold mínimo para considerar válida
        if (bestIntent.Value >= 0.3) {
return bestIntent.Key;

 }
 }
// Tentar mapeamento por palavras-chave
        foreach (var keyword in _keywordMapping.Keys) {
if(normalizedText.Contains(keyword)) {
return _keywordMapping[keyword];

 }
 }
 return CommandIntent.Unknown;

 }
 private double CalculateIntentScore(string text, CommandIntent intent) {
if(!_intentPatterns.ContainsKey(intent))return 0;

 var patterns = _intentPatterns[intent];

 var totalScore = 0.0;

 var matchedPatterns = 0;

 foreach(var pattern in patterns) {
var patternScore = CalculatePatternScore(text, pattern);

 if(patternScore > 0) {
 totalScore += patternScore;

 matchedPatterns++;

 }
 }
 return matchedPatterns > 0?totalScore/matchedPatterns: 0;

 }
 private double CalculatePatternScore(string text, string pattern) {
var patternWords = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);

 var textWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

 var matchedWords = 0;

 foreach(var patternWord in patternWords) {
if(textWords.Contains(patternWord)) {
matchedWords++;

 }
 }
// Score baseado na proporção de palavras correspondentes
        return patternWords.Length > 0 ? (double)matchedWords/patternWords.Length : 0;

 }
 private double CalculateIntentConfidence(string text, CommandIntent intent) {
 if (intent == CommandIntent.Unknown) return 0;

 var score = CalculateIntentScore(text, intent);

// Ajustar confiança baseado na complexidade do texto
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

 var complexityBonus = Math.Min(wordCount/10.0, 0.2);

// M�ximo 20% de bônus
        return Math.Min(score + complexityBonus, 1.0);

 }
 private List < string > ExtractEntities(string normalizedText) {
var entities = new List < string > ();

// Extrair números (porcentagens, tamanhos, etc.)
        var numberPattern = @"\b\d+(?:\.\d+)?(?:%|gb|mb|kb|ms|s)?\b";

 var numbers = Regex.Matches(normalizedText, numberPattern);

 foreach(Match match in numbers) {
entities.Add(match.Value);

 }
// Extrair palavras-chave específicas
        var entityKeywords = new[]
        {
            "gaming","jogos","bateria","latência","storage","ssd","hdd","cpu","ram","memória"
        };

 foreach(var keyword in entityKeywords) {
if(normalizedText.Contains(keyword)) {
entities.Add(keyword);

 }
 }
 return entities.Distinct().ToList();

 }
 private Dictionary < string, object > ExtractParameters(string normalizedText, List < string > entities) {
var parameters = new Dictionary < string, object > ();

// Extrair parâmetros baseados em entidades
        foreach (var entity in entities) {
if(entity.Contains("%")) {
 if (double.TryParse(entity.Replace("%", ""), out var percentage)) {
parameters["percentage"] = percentage;

 }
 }
 else if (entity.Contains("gb") || entity.Contains("mb") || entity.Contains("kb")) {
 if (double.TryParse(Regex.Replace(entity, @"[^\d.]", ""), out var size)) {
parameters["size"] = size;

 parameters["size_unit"] = Regex.Match(entity, @"gb|mb|kb").Value;

 }
 }
 else if (entity.Contains("ms") || entity.Contains("s")) {
 if (double.TryParse(Regex.Replace(entity, @"[^\d.]", ""), out var time)) {
parameters["time"] = time;

 parameters["time_unit"] = entity.Contains("ms") ? "ms" : "s";

 }
 }
 else {
// Par�metros booleanos baseados em palavras-chave
        switch (entity.ToLowerInvariant())
        {
            case "gaming": case "jogos":
                parameters["gaming"] = true;
                break;
            case "bateria":
                parameters["battery"] = true;
                break;
            case "completo": case "tudo":
                parameters["full"] = true;
                break;
            case "rápido": case "rapido":
                parameters["fast"] = true;
                break;
        }
 }
 }
 return parameters;

 }
 #endregion
    private void InitializePatterns()
    {
        _intentPatterns = new Dictionary<CommandIntent, List<string>>
        {
            [CommandIntent.OptimizeSystem] = new List<string>
            {
                "otimizar sistema","otimizar tudo","otimizar completo","melhorar performance","otimizar pc","acelerar computador","deixar pc rápido"
            },
            [CommandIntent.CleanupSystem] = new List<string>
            {
                "limpar sistema","limpar pc","limpar arquivos","liberar espaço","remover lixo","limpar temp","limpar cache"
            },
            [CommandIntent.OptimizeForGaming] = new List<string>
            {
                "otimizar para jogos","otimizar gaming","melhorar jogos","otimização jogos","performance jogos","gaming mode","modo gamer"
            },
            [CommandIntent.AnalyzeSystem] = new List<string>
            {
                "analisar sistema","análise sistema","diagnosticar pc","ver status","checar sistema","examinar computador","relatório sistema"
            },
            [CommandIntent.OptimizeLatency] = new List<string>
            {
                "otimizar latência","reduzir latência","melhorar responsividade","otimizar input lag","reduzir delay","melhorar tempo resposta"
            },
            [CommandIntent.ManagePower] = new List<string>
            {
                "economizar bateria","economia energia","gerenciar energia","perfil energia","poupar bateria","otimizar bateria","modo economia"
            },
            [CommandIntent.OptimizeStorage] = new List<string>
            {
                "otimizar storage","otimizar disco","otimizar ssd","otimizar hdd","melhorar disco","otimizar armazenamento"
            },
            [CommandIntent.GetStatus] = new List<string>
            {
                "status","estado","como está","situação atual","informações sistema","dados sistema","resumo sistema"
            },
            [CommandIntent.Help] = new List<string>
            {
                "ajuda","ajudar","comandos","o que fazer","como usar","instru��es","opções","menu"
            }
 };

 _keywordMapping = new Dictionary<string, CommandIntent>
        {
            ["otimizar"] = CommandIntent.OptimizeSystem,
            ["limpar"] = CommandIntent.CleanupSystem,
            ["jogos"] = CommandIntent.OptimizeForGaming,
            ["gaming"] = CommandIntent.OptimizeForGaming,
            ["analisar"] = CommandIntent.AnalyzeSystem,
            ["status"] = CommandIntent.GetStatus,
            ["ajuda"] = CommandIntent.Help,
            ["latência"] = CommandIntent.OptimizeLatency,
            ["bateria"] = CommandIntent.ManagePower,
            ["storage"] = CommandIntent.OptimizeStorage,
            ["disco"] = CommandIntent.OptimizeStorage
        };

 _stopWords = new List<string>
        {
            "o","a","os","as","um","uma","uns","umas","de","do","da","dos","das","em","no","na","nos","nas","por","pelo","pela","pelos","pelas","com","como","para","sem","sãob","sãobre","entre","at�","desde","que","quem","qual","quais","quando","onde","como","porque","quanto","mais","menos","muito","pouco","t�o","t�o","t�o","j�","agora","aqui","ali","l�","hoje","ontem","amanhã","sempre","nunca","meu","minha","meus","minhas","teu","tua","teus","tuas","ele","ela","eles","elas","n�s","v�s","eu","tu","voc�","voc�s"
        };

 }
    }
    #region Classes de Suporte
    public class ProcessedInput
 {
public string OriginalText { get; set; }
    public string NormalizedText { get; set; }
    public CommandIntent Intent { get; set; }
    public double Confidence { get; set; }
    public List<string> Entities { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
}
#endregion
}
 
