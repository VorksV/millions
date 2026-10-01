using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Performance.Models
{
    /// <summary>
    /// Resultado de uma operação de performance com suporte a sucesso parcial
    /// </summary>
    public class PerformanceOperationResult
    {
        public OperationStatus Status { get; set; } = OperationStatus.Success;
        public string Message { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, object> Details { get; set; } = new();
        public Dictionary<string, SettingVerificationResult> VerificationResults { get; set; } = new();

        /// <summary>
        /// Compatibilidade com código existente (getter e setter)
        /// </summary>
        public bool Success
        {
            get => Status == OperationStatus.Success || Status == OperationStatus.PartialSuccess;
            set => Status = value ? OperationStatus.Success : OperationStatus.Failed;
        }
    }

    /// <summary>
    /// Status da operação
    /// </summary>
    public enum OperationStatus
    {
        Success,        // Tudo aplicado corretamente
        PartialSuccess, // Algumas configurações não foram aplicadas
        Failed          // Falha completa
    }

    /// <summary>
    /// Resultado da verificação de uma configuração individual
    /// </summary>
    public class SettingVerificationResult
    {
        public string SettingName { get; set; } = string.Empty;
        public uint ExpectedValue { get; set; }
        public uint ActualValue { get; set; }
        public bool Verified { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
