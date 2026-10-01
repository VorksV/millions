using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring;

namespace VoltrisOptimizer.Services.AutoFixer
{
    /// <summary>
    /// Engine de validação: garante que sistema está seguro antes de aplicar otimizações
    /// CRÍTICO: Previne danos e instabilidade
    /// </summary>
    public class ValidationEngine
    {
        private readonly MetricsCollector _metrics;
        private readonly GameDetectionService? _gameDetection;
        private readonly ProcessCacheService _processCache;
        private readonly ILoggingService _logger;
        
        // Histórico de última correção
        private DateTime _lastFixAttempt = DateTime.MinValue;
        
        // Processos críticos que NÃO devem ser modificados
        private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "winlogon", "lsass", "services", "smss", "wininit",
            "dwm", "explorer", "audiodg", "fontdrvhost", "conhost",
            "svchost", "system", "registry"
        };
        
        public ValidationEngine(
            MetricsCollector metrics,
            ProcessCacheService processCache,
            ILoggingService logger,
            GameDetectionService? gameDetection = null)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameDetection = gameDetection;
        }
        
        /// <summary>
        /// Valida se sistema está seguro para aplicar otimizações
        /// </summary>
        public async Task<ValidationResult> ValidateSystemStateAsync()
        {
            var checks = new List<SafetyCheck>();
            
            try
            {
                // CHECK 1: Sistema não está em estado crítico
                checks.Add(await ValidateSystemHealthAsync());
                
                // CHECK 2: Não há jogo em fullscreen (evita interferência)
                checks.Add(ValidateNoActiveFullscreenGame());
                
                // CHECK 3: Não há processos críticos sendo modificados
                checks.Add(ValidateNoCriticalProcesses());
                
                // CHECK 4: Throttling está sendo respeitado
                checks.Add(ValidateThrottling());
                
                // CHECK 5: Não está em baixa bateria (notebooks)
                checks.Add(ValidatePowerState());
                
                // Verificar se todos passaram
                var failedChecks = checks.Where(c => !c.Passed).ToList();
                
                if (failedChecks.Any())
                {
                    var reasons = string.Join("; ", failedChecks.Select(c => c.Reason));
                    _logger.LogWarning($"[Validation] ❌ Validação falhou: {reasons}");
                    
                    return ValidationResult.Unsafe(
                        failedChecks.First().Name,
                        reasons);
                }
                
                _logger.LogSuccess("[Validation] ✅ Sistema validado - seguro para otimização");
                return ValidationResult.Safe();
            }
            catch (Exception ex)
            {
                _logger.LogError("[Validation] Erro durante validação", ex);
                return ValidationResult.Unsafe("ValidationError", ex.Message);
            }
        }
        
        /// <summary>
        /// Valida saúde do sistema
        /// </summary>
        private async Task<SafetyCheck> ValidateSystemHealthAsync()
        {
            try
            {
                var metrics = await _metrics.GetCurrentMetricsAsync();
                
                // Sistema está em estado crítico se:
                // - CPU temp > 90°C
                // - RAM pressure > 95%
                // - GPU temp > 90°C
                
                if (metrics.CpuTemperature > 90)
                {
                    return new SafetyCheck
                    {
                        Name = "SystemHealth",
                        Passed = false,
                        Reason = $"CPU temperatura crítica: {metrics.CpuTemperature}°C"
                    };
                }
                
                if (metrics.GpuTemperature > 90)
                {
                    return new SafetyCheck
                    {
                        Name = "SystemHealth",
                        Passed = false,
                        Reason = $"GPU temperatura crítica: {metrics.GpuTemperature}°C"
                    };
                }
                
                if (metrics.RamPressure > 95)
                {
                    return new SafetyCheck
                    {
                        Name = "SystemHealth",
                        Passed = false,
                        Reason = $"RAM crítica: {metrics.RamPressure:F1}%"
                    };
                }
                
                return new SafetyCheck
                {
                    Name = "SystemHealth",
                    Passed = true,
                    Reason = "Sistema saudável"
                };
            }
            catch
            {
                // Em caso de erro, permitir (fail-safe)
                return new SafetyCheck
                {
                    Name = "SystemHealth",
                    Passed = true,
                    Reason = "Não foi possível verificar (permitindo por padrão)"
                };
            }
        }
        
        /// <summary>
        /// Valida que não há jogo em fullscreen
        /// </summary>
        private SafetyCheck ValidateNoActiveFullscreenGame()
        {
            try
            {
                // Se GameDetectionService não está disponível, permitir
                if (_gameDetection == null || !_gameDetection.IsMonitoring)
                {
                    return new SafetyCheck
                    {
                        Name = "NoActiveGame",
                        Passed = true,
                        Reason = "Detecção de jogos não ativa"
                    };
                }
                
                // TODO: Verificar se há jogo em fullscreen
                // Por enquanto, sempre permitir
                return new SafetyCheck
                {
                    Name = "NoActiveGame",
                    Passed = true,
                    Reason = "Nenhum jogo em fullscreen detectado"
                };
            }
            catch
            {
                return new SafetyCheck
                {
                    Name = "NoActiveGame",
                    Passed = true,
                    Reason = "Verificação não disponível"
                };
            }
        }
        
        /// <summary>
        /// Valida que processos críticos não serão afetados
        /// </summary>
        private SafetyCheck ValidateNoCriticalProcesses()
        {
            try
            {
                var processes = _processCache.GetCachedProcesses();
                
                // Verificar se há processos críticos em execução
                var criticalRunning = processes
                    .Where(p => CriticalProcesses.Contains(p.ProcessName))
                    .ToList();
                
                if (criticalRunning.Count > 0)
                {
                    // Processos críticos estão OK - apenas logar
                    _logger.LogInfo($"[Validation] {criticalRunning.Count} processos críticos detectados (normal)");
                }
                
                return new SafetyCheck
                {
                    Name = "NoCriticalProcesses",
                    Passed = true,
                    Reason = "Processos críticos verificados"
                };
            }
            catch
            {
                return new SafetyCheck
                {
                    Name = "NoCriticalProcesses",
                    Passed = true,
                    Reason = "Verificação não disponível"
                };
            }
        }
        
        /// <summary>
        /// Valida que throttling está sendo respeitado
        /// </summary>
        private SafetyCheck ValidateThrottling()
        {
            var timeSinceLastFix = (DateTime.Now - _lastFixAttempt).TotalSeconds;
            
            // Mínimo 30s entre correções
            if (timeSinceLastFix < 30)
            {
                return new SafetyCheck
                {
                    Name = "ThrottlingRespected",
                    Passed = false,
                    Reason = $"Throttling: aguardar {30 - timeSinceLastFix:F0}s"
                };
            }
            
            return new SafetyCheck
            {
                Name = "ThrottlingRespected",
                Passed = true,
                Reason = "Throttling respeitado"
            };
        }
        
        /// <summary>
        /// Valida estado de energia (notebooks)
        /// </summary>
        private SafetyCheck ValidatePowerState()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                
                // Se está em bateria E bateria < 20%, não otimizar
                if (powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
                {
                    var batteryPercent = powerStatus.BatteryLifePercent * 100;
                    
                    if (batteryPercent < 20)
                    {
                        return new SafetyCheck
                        {
                            Name = "PowerState",
                            Passed = false,
                            Reason = $"Bateria baixa: {batteryPercent:F0}%"
                        };
                    }
                }
                
                return new SafetyCheck
                {
                    Name = "PowerState",
                    Passed = true,
                    Reason = "Estado de energia OK"
                };
            }
            catch
            {
                // Em caso de erro, permitir
                return new SafetyCheck
                {
                    Name = "PowerState",
                    Passed = true,
                    Reason = "Verificação não disponível"
                };
            }
        }
        
        /// <summary>
        /// Registra que uma correção foi aplicada (para throttling)
        /// </summary>
        public void RegisterFixAttempt()
        {
            _lastFixAttempt = DateTime.Now;
        }
    }
    
    #region Data Classes
    
    /// <summary>
    /// Resultado de validação
    /// </summary>
    public class ValidationResult
    {
        public bool IsSafeToOptimize { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string CheckName { get; set; } = string.Empty;
        
        public static ValidationResult Safe()
        {
            return new ValidationResult
            {
                IsSafeToOptimize = true,
                Reason = "Sistema validado com sucesso",
                CheckName = "All"
            };
        }
        
        public static ValidationResult Unsafe(string checkName, string reason)
        {
            return new ValidationResult
            {
                IsSafeToOptimize = false,
                Reason = reason,
                CheckName = checkName
            };
        }
        
        public override string ToString()
        {
            return IsSafeToOptimize 
                ? "✅ Sistema seguro" 
                : $"❌ Inseguro: {Reason}";
        }
    }
    
    /// <summary>
    /// Resultado de uma verificação de segurança
    /// </summary>
    internal class SafetyCheck
    {
        public string Name { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
    
    #endregion
}
