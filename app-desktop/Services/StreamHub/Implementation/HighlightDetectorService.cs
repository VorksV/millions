using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Detector de momentos de destaque baseado em análise de atividade do chat,
    /// eventos de plataforma e picos de atividade. Funciona 100% offline.
    /// </summary>
    public class HighlightDetectorService : IHighlightDetectorService
    {
        private readonly ILoggingService _logger;
        private readonly List<HighlightMoment> _highlights = new();
        private readonly Queue<DateTime> _recentMessages = new();
        private DateTime _streamStartTime = DateTime.Now;
        private DateTime _lastHighlightTime = DateTime.MinValue;
        private const int MinSecondsBetweenHighlights = 30; // Evitar spam de highlights

        // Thresholds
        public int ChatSpikeThreshold { get; set; } = 8; // msgs em 5s = spike
        public int ChatSpikeWindowSeconds { get; set; } = 5;

        public event EventHandler<HighlightMoment>? HighlightDetected;

        public IReadOnlyList<HighlightMoment> Highlights => _highlights.AsReadOnly();

        public HighlightDetectorService(ILoggingService logger)
        {
            _logger = logger;
            _logger.Log(LogLevel.Debug, LogCategory.General, "[HighlightDetector] Instanciado", source: "StreamHub");
        }

        public void SetStreamStartTime(DateTime startTime)
        {
            _streamStartTime = startTime;
            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[HighlightDetector] Tempo de início da stream definido: {startTime:HH:mm:ss}", source: "StreamHub");
        }

        public void OnChatActivity(int messagesInWindow)
        {
            // Registrar timestamp da mensagem
            _recentMessages.Enqueue(DateTime.Now);

            // Limpar mensagens fora da janela de tempo
            var cutoff = DateTime.Now.AddSeconds(-ChatSpikeWindowSeconds);
            while (_recentMessages.Count > 0 && _recentMessages.Peek() < cutoff)
                _recentMessages.Dequeue();

            int count = _recentMessages.Count;

            if (count >= ChatSpikeThreshold)
            {
                int intensity = Math.Min(10, (int)(count / (double)ChatSpikeThreshold * 5));
                TryCreateHighlight(
                    HighlightTrigger.ChatSpike,
                    $"Pico de chat: {count} mensagens em {ChatSpikeWindowSeconds}s",
                    intensity);
            }
        }

        public void OnPlatformEvent(HighlightTrigger trigger, string description)
        {
            int intensity = trigger switch
            {
                HighlightTrigger.DonationEvent => 9,
                HighlightTrigger.SubscriptionEvent => 7,
                HighlightTrigger.FollowEvent => 5,
                _ => 6
            };

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[HighlightDetector] Evento de plataforma: {trigger} - {description}", source: "StreamHub");

            TryCreateHighlight(trigger, description, intensity);
        }

        public void MarkManualHighlight(TimeSpan streamTimestamp, string description = "Marcação manual")
        {
            var moment = new HighlightMoment
            {
                Trigger = HighlightTrigger.ManualMark,
                Description = description,
                StreamTimestamp = streamTimestamp,
                Intensity = 8
            };

            _highlights.Add(moment);
            _lastHighlightTime = DateTime.Now;

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[HighlightDetector] Highlight manual marcado em {streamTimestamp:hh\\:mm\\:ss}: {description}",
                source: "StreamHub");

            HighlightDetected?.Invoke(this, moment);
        }

        private void TryCreateHighlight(HighlightTrigger trigger, string description, int intensity)
        {
            // Cooldown para evitar spam
            if ((DateTime.Now - _lastHighlightTime).TotalSeconds < MinSecondsBetweenHighlights)
            {
                _logger.Log(LogLevel.Debug, LogCategory.General,
                    $"[HighlightDetector] Highlight ignorado (cooldown): {trigger}", source: "StreamHub");
                return;
            }

            var streamTimestamp = DateTime.Now - _streamStartTime;
            var moment = new HighlightMoment
            {
                Trigger = trigger,
                Description = description,
                StreamTimestamp = streamTimestamp,
                Intensity = intensity
            };

            _highlights.Add(moment);
            _lastHighlightTime = DateTime.Now;

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[HighlightDetector] HIGHLIGHT detectado [{trigger}] em {streamTimestamp:hh\\:mm\\:ss} " +
                $"(intensidade: {intensity}/10): {description}",
                source: "StreamHub");

            HighlightDetected?.Invoke(this, moment);
        }
    }
}
