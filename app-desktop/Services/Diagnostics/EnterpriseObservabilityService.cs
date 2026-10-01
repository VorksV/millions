using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Core.Telemetry;

namespace VoltrisOptimizer.Services.Diagnostics
{
    public class ObservabilityReport
    {
        public int TotalComponents { get; set; }
        public int HealthyComponents { get; set; }
        public double GlobalSuccessRate { get; set; }
        public long TotalRollbacks { get; set; }
        public double AverageSystemDpcLatency { get; set; }
        public int CurrentCpuUsage { get; set; }
        public List<ComponentHealth> ComponentDetails { get; set; } = new();
    }

    public class ComponentHealth
    {
        public string Name { get; set; }
        public string Status { get; set; } // "Healthy", "Degraded", "Failing"
        public double AvgExecutionTimeMs { get; set; }
        public long SuccessCount { get; set; }
        public long RollbackCount { get; set; }
    }

    public interface IEnterpriseObservabilityService
    {
        ObservabilityReport GenerateHealthReport();
    }

    /// <summary>
    /// Fornece telemetria agregada para o painel de observabilidade interno.
    /// Exibe health, throughput, e taxas de rollback das otimizações.
    /// </summary>
    public class EnterpriseObservabilityService : IEnterpriseObservabilityService
    {
        private readonly UnifiedTelemetryBus _telemetry = UnifiedTelemetryBus.Instance;

        // Idealmente injetaríamos os serviços para varrer a saúde deles, 
        // mas o TelemetryBus já tem o cache interno.
        
        public ObservabilityReport GenerateHealthReport()
        {
            var report = new ObservabilityReport
            {
                AverageSystemDpcLatency = _telemetry.DpcLatencyMs,
                CurrentCpuUsage = _telemetry.CpuUsage
            };

            // Para simular as métricas dos componentes usando os nomes conhecidos.
            // O UnifiedTelemetryBus já armazena isso no GetInternalMetrics.
            string[] knownEngines = { "VISG", "VMRG", "Brain", "VFE", "SmartRepair", "RegistryManager", "PowerManager" };

            long totalSuccess = 0;
            long totalRollbacks = 0;

            foreach (var engine in knownEngines)
            {
                var metrics = _telemetry.GetInternalMetrics(engine);
                if (metrics != null)
                {
                    double successRate = metrics.ExecutionCount > 0 
                        ? (double)metrics.SuccessCount / metrics.ExecutionCount 
                        : 1.0;

                    string status = "Healthy";
                    if (successRate < 0.8) status = "Degraded";
                    if (successRate < 0.5) status = "Failing";

                    report.ComponentDetails.Add(new ComponentHealth
                    {
                        Name = engine,
                        Status = status,
                        AvgExecutionTimeMs = metrics.AverageExecutionTimeMs,
                        SuccessCount = metrics.SuccessCount,
                        RollbackCount = metrics.RollbackCount
                    });

                    totalSuccess += metrics.SuccessCount;
                    totalRollbacks += metrics.RollbackCount;
                }
            }

            report.TotalComponents = report.ComponentDetails.Count;
            report.HealthyComponents = report.ComponentDetails.Count(c => c.Status == "Healthy");
            
            long totalExec = totalSuccess + totalRollbacks;
            report.GlobalSuccessRate = totalExec > 0 ? (double)totalSuccess / totalExec : 1.0;
            report.TotalRollbacks = totalRollbacks;

            return report;
        }
    }
}
