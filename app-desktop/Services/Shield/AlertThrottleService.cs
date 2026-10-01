using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>Decisao do limitador para um evento de ameaca.</summary>
    public enum AlertDecision
    {
        /// <summary>Notificar agora.</summary>
        Show,

        /// <summary>Ja notificado recentemente: mesma ameaca, mesmo hash.</summary>
        SuppressDuplicate,

        /// <summary>Chegou junto de uma rajada: agrupar e resumir.</summary>
        SuppressRateLimited
    }

    /// <summary>
    /// Limitador e agregador de alertas de seguranca.
    ///
    /// O QUE ESTE SERVICO CORRIGE. A cadeia anterior
    /// FileMonitor -&gt; ShieldService -&gt; ShieldNotification -&gt; NotificationManager
    /// -&gt; GlobalNotificationService tinha DOIS mecanismos de cooldown, e ambos
    /// eram deliberadamente desligados para a classe de notificacao que mais gerava
    /// ruido:
    ///
    ///   NotificationManager.cs       bypassCooldown = type == Error || Warning
    ///   GlobalNotificationService   if (type == Warning) return false;   (antes do dict)
    ///
    /// "Medium" era mapeado para Warning, portanto nao havia cooldown, nao havia
    /// dedup e nao havia limite de rajada. Um instalador gravando 7 DLLs em
    /// %TEMP%\{GUID}\ produzia 7 toasts em 2 segundos — confirmado no log de
    /// 27/09/2026 (15 alertas entre 15:59:41 e 16:00:09, todos falsos positivos).
    ///
    /// A logica de bypass estava INVERTIDA. A intencao original — "nao silenciar
    /// ameaca critica" — e legitima, mas "ameaca" foi confundida com "aviso". Um
    /// Warning e justamente a classe MENOS confiavel: e onde cai heuristica
    /// estatica. Quanto menos confiavel a evidencia, MAIS agressivo precisa ser o
    /// filtro, nao menos.
    ///
    /// MODELO ADOTADO (alinhado a Kaspersky/ESET):
    ///   1. DEDUP POR CONTEUDO. A mesma ameaca (mesmo hash) numa janela de
    ///      cooldown nao vira um segundo alerta. Copiou para 5 pastas = 1 alerta.
    ///   2. TOKEN BUCKET POR SEVERIDADE. Teto de rajada + refill. Impede
    ///      inundacao mesmo com conteudo distinto.
    ///   3. SESSIONIZACAO. Eventos contidos sao acumulados e emitidos como UM
    ///      alerta resumido ("15 arquivos em 2 diretorios") em vez de N toasts.
    ///   4. Critical SEMPRE passa. Evidencia confirmada nao espera cooldown.
    /// </summary>
    public sealed class AlertThrottleService : IDisposable
    {
        private sealed class Bucket
        {
            public double Tokens;
            public DateTime LastRefillUtc;
            public readonly object Sync = new();
        }

        private sealed class SuppressedEntry
        {
            public string Key = string.Empty;
            public string Description = string.Empty;
            public ThreatSeverity Severity;
            public DateTime FirstSeenUtc;
            public DateTime LastSeenUtc;
            public int Count;
        }

        private readonly ILoggingService _logger;
        private readonly ConcurrentDictionary<string, SuppressedEntry> _pending = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _recentlyNotified = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<ThreatSeverity, Bucket> _buckets = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Timer _flushTimer;
        private bool _disposed;

        // --- Politica de limite por severidade ---
        private static readonly TimeSpan DupWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// (teto de rajada, refill por segundo).
        ///
        /// Critical TEM cota, mas generosa. A distincao importa:
        ///
        ///   - Cooldown (o que foi corrigido em NotificationManager) responde "houve
        ///     outro alerta ha pouco?" — e evidencia confirmada nao deve esperar.
        ///   - Topo de rajada (isto aqui) responde "quantas ameacas distintas
        ///     cabe no mesmo instante?" — e nem um produto de severidade infinita
        ///     quer substituir a tela do usuario por 20 toasts empilhados.
        ///
        /// Com refill de 1/2s e rajada 5, uma ameaca critica isolada notifica
        /// instantaneamente (o balde comeca cheio), enquanto um dump de 20
        /// ameacas distintas para 5 toasts + 1 a cada 2 segundos.
        /// </summary>
        private static readonly Dictionary<ThreatSeverity, (int Burst, double RefillPerSecond)> Limits = new()
        {
            [ThreatSeverity.Critical] = (5, 1.0 / 2.0),    // 1 a cada 2s, rajada 5
            [ThreatSeverity.High] = (5, 1.0 / 30.0),       // 1 a cada 30s, rajada 5
            [ThreatSeverity.Medium] = (2, 1.0 / 120.0),    // 1 a cada 2min, rajada 2
            [ThreatSeverity.Low] = (1, 1.0 / 300.0)
        };

        /// <summary>
        /// Notificado quando uma rajada agrupada deve virar uma unica notificacao.
        /// </summary>
        public event EventHandler<BurstSummary>? BurstReady;

        public AlertThrottleService(ILoggingService logger, TimeSpan? flushInterval = null)
        {
            _logger = logger;

            foreach (var severity in Limits.Keys)
                _buckets[severity] = new Bucket { Tokens = Limits[severity].Burst, LastRefillUtc = DateTime.UtcNow };

            var interval = flushInterval ?? TimeSpan.FromSeconds(20);
            _flushTimer = new Timer(_ => FlushPending(), null, interval, interval);
        }

        /// <summary>
        /// Decide se um evento deve gerar notificacao, ser deduplicado, ou ser
        /// acumulado para o proximo resumo de rajada.
        /// </summary>
        public AlertDecision Evaluate(string dedupKey, string description, ThreatSeverity severity)
        {
            try
            {
                // --- 1. Dedup por CONTEUDO, PARA TODAS AS SEVERIDADES ---
                //
                // Inclusive Critical. "Critical nao espera nada" significa que
                // ela nao espera COOLDOWN — nao que o mesmo arquivo possa ser
                // anunciado cinco vezes. Sao a mesma ameaca em cinco diretorios,
                // e o usuario precisa saber uma vez. Sem esta distincao, um
                // dropper copiado em varias pastas produzia um toast por copia,
                // que e exatamente a inundacao que este servico existe para
                // impedir — apenas movida para a severidade maxima.
                var now = DateTime.UtcNow;
                if (_recentlyNotified.TryGetValue(dedupKey, out var lastNotified) &&
                    now - lastNotified < DupWindow)
                {
                    RecordPending(dedupKey, description, severity);
                    return AlertDecision.SuppressDuplicate;
                }

                // --- 2. Token bucket ---
                if (!TryConsumeToken(severity))
                {
                    RecordPending(dedupKey, description, severity);
                    return AlertDecision.SuppressRateLimited;
                }

                MarkNotified(dedupKey);
                return AlertDecision.Show;
            }
            catch (Exception ex)
            {
                // Falha no limitador nunca pode CALAR uma ameaca real.
                _logger.LogWarning($"[AlertThrottle] Erro ao avaliar alerta ({ex.Message}). Permitindo exibicao.");
                return AlertDecision.Show;
            }
        }

        /// <summary>Marca uma chave como notificada (usado apos exibicao real).</summary>
        public void MarkNotified(string dedupKey)
        {
            if (string.IsNullOrEmpty(dedupKey)) return;
            _recentlyNotified[dedupKey] = DateTime.UtcNow;
            PruneRecentlyNotified();
        }

        private bool TryConsumeToken(ThreatSeverity severity)
        {
            if (!_buckets.TryGetValue(severity, out var bucket))
                return true;

            var (burst, refillPerSecond) = Limits[severity];

            lock (bucket.Sync)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - bucket.LastRefillUtc).TotalSeconds;

                if (elapsed > 0)
                {
                    bucket.Tokens = Math.Min(burst, bucket.Tokens + elapsed * refillPerSecond);
                    bucket.LastRefillUtc = now;
                }

                if (bucket.Tokens < 1.0)
                    return false;

                bucket.Tokens -= 1.0;
                return true;
            }
        }

        private void RecordPending(string key, string description, ThreatSeverity severity)
        {
            var now = DateTime.UtcNow;

            _pending.AddOrUpdate(key,
                _ => new SuppressedEntry
                {
                    Key = key,
                    Description = description,
                    Severity = severity,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    Count = 1
                },
                (_, existing) =>
                {
                    existing.LastSeenUtc = now;
                    existing.Count++;
                    if (severity > existing.Severity) existing.Severity = severity;
                    return existing;
                });
        }

        /// <summary>
        /// Consome os eventos acumulados e emite UM resumo de rajada.
        /// E o que substitui N toasts por um so.
        /// </summary>
        private void FlushPending()
        {
            if (_disposed) return;

            try
            {
                if (_pending.IsEmpty) return;

                var entries = _pending.ToArray();
                _pending.Clear();

                if (entries.Length == 0) return;

                var bySeverity = entries
                    .GroupBy(e => e.Value.Severity)
                    .OrderByDescending(g => (int)g.Key)
                    .ToArray();

                foreach (var group in bySeverity)
                {
                    var total = group.Sum(g => g.Value.Count);
                    var distinct = group.Count();

                    var summary = new BurstSummary
                    {
                        Severity = group.Key,
                        TotalEvents = total,
                        DistinctItems = distinct,
                        TopDescription = group
                            .OrderByDescending(g => g.Value.Count)
                            .First().Value.Description,
                        DirectoryHint = ExtractDirectory(group.First().Value.Description),
                        FirstSeenUtc = group.Min(g => g.Value.FirstSeenUtc),
                        LastSeenUtc = group.Max(g => g.Value.LastSeenUtc)
                    };

                    _logger.LogInfo($"[AlertThrottle] Rajada agrupada: {summary}");

                    if (summary.Severity >= ThreatSeverity.Medium)
                        BurstReady?.Invoke(this, summary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AlertThrottle] Erro ao agrupar rajada: {ex.Message}");
            }
        }

        private static string ExtractDirectory(string path)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(dir) ? path : dir;
            }
            catch
            {
                return path;
            }
        }

        private void PruneRecentlyNotified()
        {
            if (_recentlyNotified.Count < 500) return;

            var cutoff = DateTime.UtcNow - DupWindow;
            foreach (var key in _recentlyNotified
                         .Where(kvp => kvp.Value < cutoff)
                         .Select(kvp => kvp.Key)
                         .ToArray())
            {
                _recentlyNotified.TryRemove(key, out _);
            }
        }

        /// <summary>Estatisticas para diagnostico.</summary>
        public (int Pending, int TrackedKeys) Stats => (_pending.Count, _recentlyNotified.Count);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _flushTimer.Dispose();
                _cts.Cancel();
                _cts.Dispose();
            }
            catch
            {
                // Ignorar
            }
        }
    }

    /// <summary>
    /// Resumo de uma rajada de deteccoes agrupadas.
    /// </summary>
    public sealed class BurstSummary
    {
        public ThreatSeverity Severity { get; init; }
        public int TotalEvents { get; init; }
        public int DistinctItems { get; init; }
        public string TopDescription { get; init; } = string.Empty;
        public string DirectoryHint { get; init; } = string.Empty;
        public DateTime FirstSeenUtc { get; init; }
        public DateTime LastSeenUtc { get; init; }

        public TimeSpan Duration => LastSeenUtc - FirstSeenUtc;

        public override string ToString() =>
            $"{TotalEvents} evento(s), {DistinctItems} item(ns) distintos, " +
            $"sev={Severity}, janela={Duration.TotalSeconds:F1}s, dir={DirectoryHint}";
    }
}
