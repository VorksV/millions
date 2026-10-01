using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Services.VMRG.Services
{
    public class VmLearningService : IVmLearningService
    {
        private readonly ILoggingService _logger;
        private List<VmLearningEntry> _history = new();

        private static readonly string StoragePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VoltrisOptimizer", "VMRG", "learning.json");

        private const int MaxHistoryEntries = 500;
        private const double MinEffectivenessRatio = 0.3;

        public VmLearningService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            try
            {
                var dir = Path.GetDirectoryName(StoragePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(StoragePath))
                {
                    var json = await File.ReadAllTextAsync(StoragePath, ct);
                    _history = JsonSerializer.Deserialize<List<VmLearningEntry>>(json) ?? new List<VmLearningEntry>();
                    _logger.LogInfo($"[VMRG] Aprendizado carregado: {_history.Count} entradas");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VMRG] Não foi possível carregar aprendizado: {ex.Message}");
                _history = new List<VmLearningEntry>();
            }
        }

        public async Task SaveAsync(CancellationToken ct)
        {
            try
            {
                var dir = Path.GetDirectoryName(StoragePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var tempPath = StoragePath + ".tmp";
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                await File.WriteAllTextAsync(tempPath, json, ct);
                if (File.Exists(StoragePath))
                    File.Delete(StoragePath);
                File.Move(tempPath, StoragePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[VMRG] Não foi possível salvar aprendizado: {ex.Message}");
            }
        }

        public async Task RecordOutcomeAsync(VmLearningEntry entry, CancellationToken ct)
        {
            _history.Add(entry);

            if (_history.Count > MaxHistoryEntries)
            {
                _history = _history.OrderByDescending(e => e.RecordedAt)
                                   .Take(MaxHistoryEntries)
                                   .ToList();
            }

            _logger.LogInfo($"[VMRG] Aprendizado registrado: {entry.Hypervisor} | {entry.AppliedAction} | efetivo={entry.WasEffective} | melhoria={entry.ImprovementScore:F2}");

            if (_history.Count % 25 == 0)
            {
                await SaveAsync(ct);
            }
        }

        public Task<bool> ShouldApplyActionAsync(VmHypervisor hypervisor, VmDecision.DecisionType action, VmContext context, CancellationToken ct)
        {
            var relevantEntries = _history
                .Where(e => e.Hypervisor == hypervisor && e.AppliedAction == action)
                .ToList();

            if (relevantEntries.Count < 3)
                return Task.FromResult(true);

            var recentEntries = relevantEntries
                .Where(e => e.RecordedAt > DateTime.UtcNow.AddDays(-14))
                .ToList();

            var pool = recentEntries.Count >= 3 ? recentEntries : relevantEntries;

            var effectivenessCount = pool.Count(e => e.WasEffective);
            var ratio = (double)effectivenessCount / pool.Count;

            var shouldApply = ratio >= MinEffectivenessRatio;

            _logger.LogDebug($"[VMRG] Aprendizado: {hypervisor}/{action} — efetivo {effectivenessCount}/{pool.Count} ({ratio:P0}) => {(shouldApply ? "aplicar" : "pular")}");

            return Task.FromResult(shouldApply);
        }
    }
}
