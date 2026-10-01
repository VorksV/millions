using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces
{
    public interface IPowerProfileDiagnosticsService
    {
        PowerDiagnosticState CurrentState { get; }
        Task StartAnalysisAsync(string gameName, CancellationToken ct = default);
        void ProcessSample(GameDiagnosticsService.Sample sample);
        event Action<string> DiagnosticMessageGenerated;
    }
}
