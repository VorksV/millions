using System;

using System.Collections.Generic;

using System.Linq;

using System.Threading.Tasks;

using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Assistant 
{
/// <summary>
/// Validador de segurança para comandos do assistente inteligente
/// Implementa validação real para evitar comandos perigosos
/// </summary>
public class SafetyValidator
 {
private readonly ILoggingService _logger;

// Listas de comandos e palavras perigosas
    private List<string> _dangerousCommands;

 private List < string > _forbiddenKeywords;

 private List < string > _cautionKeywords;

 private List < string > _systemCriticalPaths;

 public SafetyValidator(ILoggingService logger) {
_logger = logger??throw new ArgumentNullException(nameof(logger));

 InitializeSafetyRules();

 _logger.LogInfo("[SafetyValidator]Validador de segurança inicializado");

 }
/// <summary>
    /// Valida se um comando � seguro para execução
    /// </summary>
    public async Task<SafetyValidation> ValidateCommandAsync(ProcessedInput processedInput, SystemContext context) {
_logger.LogInfo($"[SafetyValidator]Validando comando: {processedInput.Intent}");

 try {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

// 1. Validar intenções perigosas
        var intentValidation = ValidateIntent(processedInput.Intent);

 if(!intentValidation.IsSafe) {
return intentValidation;

 }
// 2. Validar texto por palavras proibidas
        var textValidation = ValidateText(processedInput.NormalizedText);

 if(!textValidation.IsSafe) {
return textValidation;

 }
// 3. Validar parâmetros
        var parameterValidation = ValidateParameters(processedInput.Parameters);

 if(!parameterValidation.IsSafe) {
return parameterValidation;

 }
// 4. Validar contexto
        var contextValidation = ValidateContext(processedInput, context);

 if(!contextValidation.IsSafe) {
return contextValidation;

 }
// 5. Validar entidades
        var entityValidation = ValidateEntities(processedInput.Entities);

 if(!entityValidation.IsSafe) {
return entityValidation;

 }
// Combinar resultados
        validation.IsSafe = intentValidation.IsSafe && textValidation.IsSafe && parameterValidation.IsSafe && contextValidation.IsSafe && entityValidation.IsSafe;

 if(!validation.IsSafe) {
validation.Level = SafetyLevel.Dangerous;

 validation.Reason = "M�ltiplas validações falharam";

 }
 _logger.LogInfo($"[SafetyValidator]Validação concluída: {(validation.IsSafe ? LocalizationService.Instance.GetString("RiskLevelSafe") : LocalizationService.Instance.GetString("RiskLevelDangerous"))} - {validation.Level}");

 return validation;

 }
 catch(Exception ex) {
_logger.LogError("[SafetyValidator]Erro na validação", ex);

 return new SafetyValidation
        {
            IsSafe = false,
            Level = SafetyLevel.Dangerous,
            Reason = $"Erro na validação: {ex.Message}"
        };

 }
 }
 #region Validações Específicas
    private SafetyValidation ValidateIntent(CommandIntent intent) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

// Verificar se a intenção está na lista de comandos perigosos
        var intentName = intent.ToString().ToLowerInvariant();

 if(_dangerousCommands.Contains(intentName)) {
validation.IsSafe = false;

 validation.Level = SafetyLevel.Forbidden;

 validation.Reason = $"Inten��o '{intent}' proibida por motivos de segurança";

 return validation;

 }
// Verificar intenções que requerem cuidado
        if (_cautionKeywords.Contains(intentName)) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = $"Inten��o '{intent}' requer cuidado especial";

 }
 return validation;

 }
 private SafetyValidation ValidateText(string normalizedText) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

// Verificar palavras proibidas no texto
        foreach (var keyword in _forbiddenKeywords) {
if(normalizedText.Contains(keyword)) {
validation.IsSafe = false;

 validation.Level = SafetyLevel.Forbidden;

 validation.Reason = $"Palavra proibida detectada: '{keyword}'";

 return validation;

 }
 }
// Verificar palavras que requerem cuidado
        var cautionFound = false;

 foreach(var keyword in _cautionKeywords) {
if(normalizedText.Contains(keyword)) {
cautionFound = true;

 break;

 }
 }
 if(cautionFound) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = "Texto cont�m termos que requerem cuidado";

 }
 return validation;

 }
 private SafetyValidation ValidateParameters(Dictionary < string, object > parameters) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

 foreach(var param in parameters) {
var paramValidation = ValidateParameter(param.Key, param.Value);

 if(!paramValidation.IsSafe) {
return paramValidation;

 }
 if(paramValidation.Level > validation.Level) {
validation.Level = paramValidation.Level;

 }
 }
 return validation;

 }
 private SafetyValidation ValidateParameter(string key, object value) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

 var keyLower = key.ToLowerInvariant();

 var valueStr = value?.ToString()?.ToLowerInvariant()??"";

// Verificar parâmetros perigosos
        if (_forbiddenKeywords.Contains(keyLower) || _forbiddenKeywords.Contains(valueStr)) {
validation.IsSafe = false;

 validation.Level = SafetyLevel.Forbidden;

 validation.Reason = $"Par�metro perigoso: {key} = {value}";

 return validation;

 }
// Verificar valores extremos
        if (value is double doubleValue) {
if(doubleValue > 100&&key.Contains("percentage")) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = $"Porcentagem suspeita: {doubleValue}%";

 }
 if(doubleValue < 0) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = $"Valor negativo detectado: {doubleValue}";

 }
 }
// Verificar paths perigosos
        if (value is string stringValue && _systemCriticalPaths.Any(path => stringValue.Contains(path))) {
validation.IsSafe = false;

 validation.Level = SafetyLevel.Forbidden;

 validation.Reason = $"Path crítico do sistema detectado: {stringValue}";

 return validation;

 }
 return validation;

 }
 private SafetyValidation ValidateContext(ProcessedInput processedInput, SystemContext context) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

// Verificar se o comando � apropriado para o contexto
        if (context.IsOnBattery && processedInput.Intent == CommandIntent.OptimizeSystem) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = "Otimização completa em bateria pode reduzir vida �til";

 }
 if (context.SystemLoad == SystemLoad.Critical && processedInput.Intent == CommandIntent.OptimizeSystem) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = "Sistema sãob carga crítica - otimização pode piorar situação";

 }
// Verificar se há jogos rodando e o comando pode interferir
        if (context.IsGamingMode && processedInput.Intent == CommandIntent.CleanupSystem) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = "Limpeza durante jogo pode causará stutter";

 }
 return validation;

 }
 private SafetyValidation ValidateEntities(List < string > entities) {
var validation = new SafetyValidation {
IsSafe = true, Level = SafetyLevel.Safe};

 foreach(var entity in entities) {
var entityLower = entity.ToLowerInvariant();

// Verificar entidades perigosas
        if (_forbiddenKeywords.Contains(entityLower)) {
validation.IsSafe = false;

 validation.Level = SafetyLevel.Forbidden;

 validation.Reason = $"Entidade perigosa detectada: '{entity}'";

 return validation;

 }
// Verificar entidades que requerem cuidado
        if (_cautionKeywords.Contains(entityLower)) {
validation.Level = SafetyLevel.Caution;

 validation.Reason = $"Entidade requer cuidado: '{entity}'";

 }
 }
 return validation;

 }
 #endregion
    private void InitializeSafetyRules()
    {
        // Comandos completamente proibidos
        _dangerousCommands = new List<string>
        {
            "delete","remove","uninstall","format","destároy","erase","shutdown","restart","reboot","poweroff","halt","registry","regedit","system32","windows","boot"
        };

        // Palavras proibidas (não permitidas sãob nenhuma circunstência)
        _forbiddenKeywords = new List<string>
        {
            "formatar","deletar","apagar","remover","desinstalar","desligar","reiniciar","rebootar","desligar","shutdown","registro","regedit","system32","windows","boot","senha","password","admin","administrator","root","hack","crack","break","bypass","override","system","kernel","driver","firmware","bios"
        };

        // Palavras que requerem cuidado especial
        _cautionKeywords = new List<string>
        {
            "completo","total","tudo","todos","tudo","for�ar","force","agressivo","extremo","profundo","deep","avan�ado","advanced","experimental","beta","testáe","testá","manual","custom","persãonalizado","específico"
        };

        // Paths críticos do sistema (nunca devem será manipulados)
        _systemCriticalPaths = new List<string>
        {
            "c:\\windows","c:\\program files","c:\\program files(x86)","c:\\programdata","c:\\userás","c:\\documents and settings","system32","syswow64","boot","efi","recovery","windows\\system32","windows\\syswow64","windows\\boot","program files","program files(x86)","programdata"
        };
    }
 }
 #region Classes de Suporte

public class SafetyValidation
 {
public bool IsSafe {
get;

 set;

 }
 public string Reason {
get;

 set;

 }
 public SafetyLevel Level {
get;

 set;

 }
 public DateTime Timestamp
 {
 get;
 set;
 } = DateTime.UtcNow;
 }
 #endregion
}
 
